using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LunaCero
{
    [RequireComponent(typeof(LunarHealth), typeof(LunarPlayerController))]
    public sealed class LunarPlayerCombat : MonoBehaviour
    {
        [SerializeField] private Camera aimCamera;
        [SerializeField] private Transform muzzle;
        [SerializeField] private Transform carrySocket;
        [SerializeField] private float damage = 25f;
        [SerializeField] private float fireInterval = 0.2f;
        [SerializeField] private int magazineSize = 12;
        [SerializeField] private float reloadDuration = 1.25f;
        [SerializeField] private float interactionRange = 3f;
        [SerializeField] private bool stationHealingOnly;
        [SerializeField] private int initialReserveAmmo = 72;
        [SerializeField] private int initialMedkits = 3;
        private LunarHealth health;
        private LunarPlayerController movement;
        private float nextShot;
        private float nextHeal;
        private LunarPickup nearby;
        private LunarPickup held;
        private string message;
        private float messageUntil;
        private float hitUntil;
        private float nextShove;
        private LunarPlayerStatus status;
        private LunarVitals vitals;
        private LunarViewModel viewModel;
        /// <summary>Boca del arma de la vista en primera persona (los disparos salen de ahi).</summary>
        public Transform MuzzleOverride { get; set; }
        private bool showHelp = true;
        private float helpHideAt = 12f;
        private GUIStyle labelStyle, valueStyle, bigStyle, smallStyle, centerStyle;

        [Header("Arsenal: armas que se encuentran en la nave (la pistola usa los valores de arriba)")]
        [SerializeField] private LunarWeaponData[] arsenal;
        [Tooltip("Modelo 3D opcional para la pistola inicial (reemplaza el cubo)")]
        [SerializeField] private GameObject pistolModel;
        private readonly List<LunarWeaponSlot> weapons = new List<LunarWeaponSlot>();
        private int currentIndex;
        private Coroutine reloadRoutine;
        private float baseFov = 60f;
        private Transform weaponVisual;
        private Renderer weaponRenderer;
        private Vector3 defaultWeaponScale;
        private GameObject weaponModel;

        public bool Healing { get; private set; }
        public bool StationHealingOnly => stationHealingOnly;
        public LunarWeaponSlot CurrentWeapon => weapons.Count > 0 ? weapons[currentIndex] : null;
        public IReadOnlyList<LunarWeaponSlot> Weapons => weapons;
        public int Ammo => CurrentWeapon?.Ammo ?? 0;
        public int ReserveAmmo => CurrentWeapon?.Reserve ?? 0;
        public int Medkits { get; private set; } = 3;
        public bool Reloading { get; private set; }
        public bool Aiming { get; private set; }
        public bool CanAct => health != null && !health.IsDead && (status == null || !status.BlocksActions);
        public LunarPickup HeldObject => held;
        public Camera AimCamera => aimCamera;

        private void Awake()
        {
            health = GetComponent<LunarHealth>();
            status = GetComponent<LunarPlayerStatus>();
            movement = GetComponent<LunarPlayerController>();
            vitals = GetComponent<LunarVitals>();
            if (vitals == null) vitals = gameObject.AddComponent<LunarVitals>();
            if (GetComponent<LunarRadar>() == null) gameObject.AddComponent<LunarRadar>();
            viewModel = GetComponent<LunarViewModel>();
            if (viewModel == null) viewModel = gameObject.AddComponent<LunarViewModel>();
            if (GetComponent<LunarPerformance>() == null) gameObject.AddComponent<LunarPerformance>();
            role = GetComponent<LunarPlayerRole>();
            if (role == null) role = gameObject.AddComponent<LunarPlayerRole>();
            role.Changed += ApplyRole;
            Medkits = initialMedkits;
            if (aimCamera != null) baseFov = aimCamera.fieldOfView;
            if (muzzle != null && muzzle.parent != null && muzzle.parent != transform)
            {
                weaponVisual = muzzle.parent;
                weaponRenderer = weaponVisual.GetComponent<Renderer>();
                defaultWeaponScale = weaponVisual.localScale;
            }
            weapons.Add(new LunarWeaponSlot(CreatePistol(), initialReserveAmmo));
            Equip(0, false);
            health.Died += OnDeath;
        }

        private void OnValidate()
        {
            if (arsenal == null || arsenal.Length == 0) arsenal = LunarWeaponData.Defaults();
        }

        /// <summary>Armas disponibles para el armero de la nave.</summary>
        public LunarWeaponData[] Arsenal
        {
            get
            {
                if (arsenal == null || arsenal.Length == 0) arsenal = LunarWeaponData.Defaults();
                foreach (var weapon in arsenal)
                {
                    if (weapon == null) continue;
                    var model = Resources.Load<GameObject>("LunarWeapons/" + weapon.id);
                    if (model != null) { weapon.model = model; weapon.pickupModel = model; }
                }
                return arsenal;
            }
        }

        // Primero el arsenal configurado en el prefab; si no esta (p. ej. objetos contundentes), el catalogo.
        public LunarWeaponData FindWeapon(string id) => Arsenal.FirstOrDefault(data => data != null && data.id == id) ?? LunarWeaponData.Find(id);

        // ---------------------------------------------------------------- Roles
        private LunarPlayerRole role;
        private LunarRoleDefinition RoleDef => LunarPlayerRole.Of(this);
        public LunarRoleDefinition Role => RoleDef;
        public float LockerCooldownScale => RoleDef.lockerCooldown;
        private bool roleApplied;

        private void ApplyRole(LunarRoleDefinition definition)
        {
            if (roleApplied) return;
            roleApplied = true;
            if (!Mathf.Approximately(definition.maxHealth, health.Maximum)) health.Configure(definition.maxHealth);
            if (definition.medkits > 0) Medkits = Mathf.Max(Medkits, definition.medkits);
            foreach (var slot in weapons) if (!slot.Data.IsMelee) slot.Reserve = Mathf.RoundToInt(slot.Reserve * definition.reserveAmmo);
            if (!string.IsNullOrEmpty(definition.startWeapon))
            {
                var data = FindWeapon(definition.startWeapon);
                if (data != null)
                {
                    weapons.Add(new LunarWeaponSlot(data, Mathf.RoundToInt(data.startReserve * definition.reserveAmmo)));
                    Equip(weapons.Count - 1, false);
                }
            }
            Tell($"Rol: {definition.title}. {definition.mission}");
            messageUntil = Time.time + 6f;
        }

        private LunarWeaponData CreatePistol() => new LunarWeaponData
        {
            id = "pistola", displayName = "Pistola", damage = damage, fireInterval = fireInterval, automatic = true,
            range = 100f, magazineSize = magazineSize, reloadDuration = reloadDuration, startReserve = initialReserveAmmo,
            ammoPickupScale = 1f, beamColor = Color.cyan, model = Resources.Load<GameObject>("LunarWeapons/pistola") ?? pistolModel,
            color = weaponRenderer != null && weaponRenderer.sharedMaterial != null ? weaponRenderer.sharedMaterial.color : new Color(0.3f, 0.3f, 0.35f),
            viewSize = weaponVisual != null ? defaultWeaponScale : new Vector3(0.16f, 0.18f, 0.65f)
        };

        public void AddWeapon(LunarWeaponData data)
        {
            int index = weapons.FindIndex(slot => slot.Data.id == data.id);
            if (index >= 0 && data.IsMelee)
            {
                // Otro objeto igual: se cambia por el nuevo (sin desgaste).
                weapons[index] = new LunarWeaponSlot(data, 0);
                Equip(index, false);
                Tell($"{data.displayName} nuevo  [tecla {index + 1}]");
                return;
            }
            if (index >= 0)
            {
                weapons[index].Reserve += data.magazineSize * 2;
                Tell($"{data.displayName}: +{data.magazineSize * 2} balas");
                return;
            }
            weapons.Add(new LunarWeaponSlot(data, data.startReserve));
            Equip(weapons.Count - 1, true);
            Tell($"Nueva arma: {data.displayName}  [tecla {weapons.Count}]");
        }

        public bool RefillAllWeapons()
        {
            bool any = false;
            foreach (var slot in weapons)
            {
                if (slot.Data.IsMelee) continue;
                int target = Mathf.Max(slot.Data.startReserve, slot.Data.magazineSize * 2);
                if (slot.Reserve < target) { slot.Reserve = target; any = true; }
            }
            Tell(any ? "Municion de reserva restablecida para todas tus armas." : "Tus armas ya tienen municion de reserva.");
            return any;
        }

        public bool Equip(int index, bool announce = true)
        {
            if (index < 0 || index >= weapons.Count) return false;
            if (reloadRoutine != null) { StopCoroutine(reloadRoutine); reloadRoutine = null; }
            Reloading = false;
            currentIndex = index;
            if (announce) nextShot = Mathf.Max(nextShot, Time.time + 0.3f);
            ApplyWeaponVisual(CurrentWeapon.Data);
            if (announce) Tell(CurrentWeapon.Data.displayName);
            return true;
        }

        private void ApplyWeaponVisual(LunarWeaponData data)
        {
            if (weaponVisual == null) return;
            if (weaponModel != null) Destroy(weaponModel);
            weaponVisual.localScale = data.viewSize;
            if (weaponRenderer != null)
            {
                weaponRenderer.sharedMaterial = LunarRuntimeMaterials.Get(data.color);
                weaponRenderer.enabled = data.model == null;
            }
            if (data.model != null)
            {
                weaponModel = Instantiate(data.model, weaponVisual.parent);
                weaponModel.SetActive(true); // las plantillas de objetos recogidos se guardan desactivadas
                weaponModel.transform.localPosition = weaponVisual.localPosition;
                weaponModel.transform.localRotation = weaponVisual.localRotation;
                foreach (var collider in weaponModel.GetComponentsInChildren<Collider>()) collider.enabled = false;
            }
        }

        private void HandleWeaponInput()
        {
            for (int i = 0; i < weapons.Count && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) && i != currentIndex) Equip(i);
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && weapons.Count > 1)
                Equip((currentIndex + (wheel < 0 ? 1 : -1) + weapons.Count) % weapons.Count);
            var data = CurrentWeapon.Data;
            Aiming = !data.IsMelee && data.zoomFov > 0 && Input.GetMouseButton(1);
            bool trigger = data.automatic ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
            if (trigger) TryFire(aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f)));
        }

        private void LateUpdate()
        {
            if (aimCamera == null) return;
            float target = Aiming && CurrentWeapon != null ? CurrentWeapon.Data.zoomFov : baseFov;
            aimCamera.fieldOfView = Mathf.Lerp(aimCamera.fieldOfView, target, 1f - Mathf.Exp(-14f * Time.deltaTime));
        }

        private void Update()
        {
            if (health.IsDead)
            {
                if (Input.GetKeyDown(KeyCode.C)) { LunarPlayerRole.Chosen = null; SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); }
                else if (Input.GetKeyDown(KeyCode.Return)) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }
            if (LunarPlayerRole.MenuOpen) return;
            nearby = FindPickup();
            if (Input.GetKeyDown(KeyCode.H)) { showHelp = !showHelp; helpHideAt = float.MaxValue; }
            if (showHelp && Time.timeSinceLevelLoad > helpHideAt) showHelp = false;
            if (Cursor.lockState != CursorLockMode.Locked) return;
            if (Input.GetKeyDown(KeyCode.Q) && (status == null || !status.IsControlled)) TryShove();
            if (!CanAct) { Aiming = false; return; }
            HandleWeaponInput();
            if (Input.GetKeyDown(KeyCode.R)) TryReload();
            if (Input.GetKeyDown(KeyCode.F)) TryHeal();
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (held != null) Drop(false);
                else if (nearby != null) nearby.Use(this);
            }
            if (Input.GetKeyDown(KeyCode.G)) Drop(true);
        }

        public bool TryFire(Ray aim)
        {
            var weapon = CurrentWeapon;
            if (!CanAct || Reloading || Healing || weapon == null || Time.time < nextShot) return false;
            if (weapon.Data.IsMelee) return TrySwing(weapon, aim);
            if (weapon.Ammo <= 0)
            {
                Tell(weapon.Reserve > 0 ? "Cargador vacio. Pulsa R para recargar." : "Sin balas. Busca cajas azules o el casillero de la nave.");
                nextShot = Time.time + 0.3f;
                return false;
            }
            var data = weapon.Data;
            nextShot = Time.time + data.fireInterval;
            weapon.Ammo--;
            GetComponent<LunarPlayerAnimation>()?.Shot();
            if (viewModel != null) viewModel.Kick();
            FindObjectOfType<LunarHordeDirector>()?.NotifyShot(transform.position);
            Vector3 origin = MuzzleOverride != null ? MuzzleOverride.position : muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.2f;
            float spread = Aiming ? data.spread * 0.25f : data.spread;
            float amount = data.damage * RoleDef.damage;
            for (int pellet = 0; pellet < Mathf.Max(1, data.pellets); pellet++)
            {
                Ray ray = spread > 0 ? new Ray(aim.origin, Spread(aim.direction, spread)) : aim;
                Vector3 end = ray.GetPoint(data.range);
                if (FirstWorldHit(ray, data.range, out RaycastHit sight)) end = sight.point;
                Vector3 direction = end - origin;
                var shot = new Ray(origin, direction.normalized);
                float length = direction.magnitude + 0.1f;
                if (data.mode == LunarFireMode.Piercing) end = FirePiercing(shot, data.range, data, amount);
                else if (FirstWorldHit(shot, length, out RaycastHit hit))
                {
                    end = hit.point;
                    if (data.mode == LunarFireMode.Explosive) Explode(hit.point, data, amount);
                    else HitSingle(hit, shot.direction, amount);
                }
                else if (data.mode == LunarFireMode.Explosive) Explode(end, data, amount);
                LunarCombatEffects.Beam(origin, end, data.beamColor);
            }
            return true;
        }

        /// <summary>Golpe con un objeto contundente: alcance corto, aturde y se desgasta (no hace ruido para la horda).</summary>
        private bool TrySwing(LunarWeaponSlot weapon, Ray aim)
        {
            var data = weapon.Data;
            nextShot = Time.time + data.fireInterval;
            GetComponent<LunarPlayerAnimation>()?.Shot();
            if (viewModel != null) viewModel.Swing(data.fireInterval);
            Vector3 origin = transform.position + Vector3.up * 1.35f;
            bool landed = false;
            foreach (var hit in Physics.SphereCastAll(origin, 0.45f, aim.direction, data.range, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (held != null && hit.collider.transform.IsChildOf(held.transform)) continue;
                Vector3 point = hit.distance <= 0f ? hit.collider.ClosestPoint(origin) : hit.point;
                var victim = hit.collider.GetComponentInParent<LunarHealth>();
                if (victim != null && victim != health)
                {
                    victim.Damage(data.damage * RoleDef.damage);
                    if (victim.TryGetComponent(out LunarEnemy enemy) && !enemy.IsDead) enemy.Stagger(data.stagger);
                    hitUntil = Time.time + 0.15f;
                }
                else if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                    hit.rigidbody.AddForceAtPosition(aim.direction * 5f, point, ForceMode.Impulse);
                else if (hit.collider.GetComponentInParent<LunarPickup>() != null) continue; // no gasta golpes en objetos sueltos
                LunarCombatEffects.Explosion(point, 0.25f, new Color(1f, 0.85f, 0.55f));
                landed = true;
                break;
            }
            if (landed && data.durability > 0 && --weapon.Ammo <= 0)
            {
                int index = weapons.IndexOf(weapon);
                weapons.RemoveAt(index);
                Equip(Mathf.Clamp(index - 1, 0, weapons.Count - 1), false);
                Tell($"Se rompio: {data.displayName}");
            }
            return true;
        }

        private static Vector3 Spread(Vector3 direction, float degrees)
        {
            Vector2 offset = Random.insideUnitCircle * Mathf.Tan(degrees * Mathf.Deg2Rad);
            Quaternion basis = Quaternion.LookRotation(direction);
            return (direction + basis * new Vector3(offset.x, offset.y, 0)).normalized;
        }

        private void HitSingle(RaycastHit hit, Vector3 direction, float amount)
        {
            var victim = hit.collider.GetComponentInParent<LunarHealth>();
            if (victim != null && victim != health)
            {
                victim.Damage(amount);
                hitUntil = Time.time + 0.15f;
            }
            else if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(direction * 3f, hit.point, ForceMode.Impulse);
        }

        private Vector3 FirePiercing(Ray shot, float range, LunarWeaponData data, float amount)
        {
            var damaged = new HashSet<LunarHealth>();
            Vector3 end = shot.GetPoint(range);
            foreach (var hit in Physics.RaycastAll(shot, range, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                var victim = hit.collider.GetComponentInParent<LunarHealth>();
                if (victim == null || victim == health) { HitSingle(hit, shot.direction, 0); end = hit.point; break; }
                if (!damaged.Add(victim)) continue;
                victim.Damage(amount);
                hitUntil = Time.time + 0.15f;
                if (damaged.Count >= data.pierce) { end = hit.point; break; }
            }
            return end;
        }

        private void Explode(Vector3 point, LunarWeaponData data, float amount)
        {
            var damaged = new HashSet<LunarHealth>();
            foreach (var collider in Physics.OverlapSphere(point, data.explosionRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (collider.transform.IsChildOf(transform)) continue;
                var victim = collider.GetComponentInParent<LunarHealth>();
                if (victim != null && victim != health && damaged.Add(victim))
                {
                    float distance = Vector3.Distance(point, collider.ClosestPoint(point));
                    victim.Damage(amount * Mathf.Lerp(1f, 0.35f, distance / data.explosionRadius));
                    if (victim.TryGetComponent(out LunarEnemy enemy) && !enemy.IsDead) enemy.Stagger(0.8f);
                    hitUntil = Time.time + 0.15f;
                }
                if (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic && collider.GetComponentInParent<LunarHealth>() == null)
                    collider.attachedRigidbody.AddExplosionForce(8f, point, data.explosionRadius, 0.5f, ForceMode.Impulse);
            }
            LunarCombatEffects.Explosion(point, data.explosionRadius, data.beamColor);
        }

        private bool FirstWorldHit(Ray ray, float distance, out RaycastHit result)
        {
            foreach (var hit in Physics.RaycastAll(ray, distance, ~0, QueryTriggerInteraction.Ignore).OrderBy(hit => hit.distance))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                result = hit;
                return true;
            }
            result = default;
            return false;
        }

        public bool TryHeal()
        {
            if (!CanAct || Time.time < nextHeal) return false;
            if (stationHealingOnly)
            {
                if (Healing || Reloading) return false;
                var station = FindObjectsOfType<LunarHealingStation>().FirstOrDefault(item => item.Contains(transform.position));
                // Fuera de una estacion se pueden usar los botiquines recogidos (el medico cura mas y empieza con 4).
                if (station == null && Medkits > 0) return UseMedkit();
                if (station == null) { Tell("Debes llegar a una estacion medica (o tener un botiquin) para sanar."); return false; }
                if (station.Exhausted) { Tell("Estacion agotada. Busca otra para sanar."); return false; }
                if (!station.Ready) { Tell("La estacion se esta recargando."); return false; }
                if (health.Current >= health.Maximum) { Tell("La vida ya esta completa."); return false; }
                StartCoroutine(StationHeal(station));
                return true;
            }
            if (Medkits <= 0) { Tell("No quedan botiquines."); return false; }
            return UseMedkit();
        }

        private bool UseMedkit()
        {
            float amount = 35f * RoleDef.healPower;
            if (!health.Heal(amount)) { Tell("La vida ya esta completa."); return false; }
            Medkits--;
            nextHeal = Time.time + 1.5f;
            Tell($"Curacion +{amount:0}  (botiquines: {Medkits})");
            return true;
        }

        public bool TryReload()
        {
            var weapon = CurrentWeapon;
            if (!CanAct || Reloading || Healing || weapon == null || weapon.Data.IsMelee || weapon.Ammo >= weapon.Data.magazineSize) return false;
            if (weapon.Reserve <= 0) { Tell("No tienes balas de reserva. Busca cajas azules (E) o el casillero de la nave."); return false; }
            reloadRoutine = StartCoroutine(Reload(weapon));
            return true;
        }

        private IEnumerator StationHeal(LunarHealingStation station)
        {
            Healing = true;
            float start = Time.time;
            float duration = RoleDef.stationHealTime;
            while (Time.time - start < duration)
            {
                if (!CanAct || station == null || !station.Contains(transform.position) || health.LastDamageTime > start)
                {
                    Healing = false;
                    Tell("Curacion interrumpida: permanece en el anillo y evita dano.");
                    yield break;
                }
                yield return null;
            }
            if (!station.Consume()) { Healing = false; Tell("Estacion no disponible."); yield break; }
            health.Heal(health.Maximum);
            Healing = false;
            nextHeal = Time.time + 1;
            Tell("Soporte vital restaurado.");
        }

        public bool TryShove()
        {
            if (!CanAct || Time.time < nextShove || Healing) return false;
            nextShove = Time.time + 1.1f;
            foreach (var enemy in FindObjectsOfType<LunarEnemy>())
            {
                Vector3 offset = enemy.transform.position - transform.position;
                if (offset.magnitude < 3 && Vector3.Dot(transform.forward, offset.normalized) > 0.25f) enemy.Stagger(1.5f);
            }
            Tell("Empujon");
            return true;
        }

        private IEnumerator Reload(LunarWeaponSlot weapon)
        {
            Reloading = true;
            yield return new WaitForSeconds(weapon.Data.reloadDuration * RoleDef.reloadTime);
            if (CanAct)
            {
                int amount = Mathf.Min(weapon.Data.magazineSize - weapon.Ammo, weapon.Reserve);
                weapon.Ammo += amount;
                weapon.Reserve -= amount;
            }
            Reloading = false;
            reloadRoutine = null;
        }

        /// <summary>Las cajas azules recargan la reserva del arma equipada, escalada segun el arma.</summary>
        public void AddAmmo(int amount)
        {
            var weapon = CurrentWeapon;
            // Con un objeto contundente en la mano, la municion va al arma de fuego que tenga menos reserva.
            if (weapon != null && weapon.Data.IsMelee)
                weapon = weapons.Where(slot => !slot.Data.IsMelee).OrderBy(slot => slot.Reserve).FirstOrDefault();
            if (weapon == null) return;
            int added = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0, amount) * weapon.Data.ammoPickupScale));
            weapon.Reserve += added;
            Tell($"Municion +{added} ({weapon.Data.displayName})");
        }
        public void AddMedkits(int amount) { Medkits += Mathf.Max(0, amount); Tell("Botiquin recogido"); }
        public int Ice { get; private set; }
        /// <summary>El hielo extraido se convierte en oxigeno (electrolisis del traje).</summary>
        public void AddIce(int oxygen)
        {
            Ice++;
            float amount = oxygen * RoleDef.iceOxygen;
            if (vitals != null) vitals.AddOxygen(amount);
            Tell($"Hielo extraido: +{amount:0} de oxigeno");
        }
        public void Notify(string value) => Tell(value);
        private void Tell(string value) { message = value; messageUntil = Time.time + 2.5f; }

        private LunarPickup FindPickup()
        {
            LunarPickup best = null;
            float score = float.MaxValue;
            Vector3 eye = transform.position + Vector3.up;
            foreach (var collider in Physics.OverlapSphere(eye, interactionRange, ~0, QueryTriggerInteraction.Ignore))
            {
                var pickup = collider.GetComponentInParent<LunarPickup>();
                if (pickup == null || pickup.IsHeld) continue;
                Vector3 offset = collider.bounds.center - eye;
                Vector3 flat = Vector3.ProjectOnPlane(offset, Vector3.up).normalized;
                if (Vector3.Dot(transform.forward, flat) < 0.1f) continue;
                if (FirstWorldHit(new Ray(eye, offset.normalized), offset.magnitude + 0.1f, out var hit)
                    && hit.collider.GetComponentInParent<LunarPickup>() != pickup) continue;
                float candidate = offset.sqrMagnitude;
                if (candidate < score) { best = pickup; score = candidate; }
            }
            return best;
        }

        public bool PickUp(LunarPickup pickup)
        {
            if (!CanAct || held != null || pickup == null || pickup.IsHeld || carrySocket == null) return false;
            if (Vector3.Distance(transform.position, pickup.transform.position) > interactionRange) return false;
            held = pickup;
            held.Hold(carrySocket);
            return true;
        }

        public bool Drop(bool throwObject)
        {
            if (held == null) return false;
            Vector3 origin = transform.position + Vector3.up * 1.1f;
            Vector3 desired = transform.forward * 1.3f;
            // Do not place a carried crate on the other side of a wall.
            if (FirstWorldHit(new Ray(origin, desired.normalized), desired.magnitude + 0.35f, out var hit))
                desired = desired.normalized * Mathf.Max(0.45f, hit.distance - 0.4f);
            held.Release(origin + desired, throwObject ? transform.forward * 7f + Vector3.up * 2f : Vector3.zero);
            held = null;
            return true;
        }

        private void OnDeath(LunarHealth dead)
        {
            Drop(false);
            StopAllCoroutines();
            Reloading = false;
            Healing = false;
            status?.Release();
            movement.enabled = false;
            var animation = GetComponent<LunarPlayerAnimation>();
            if (animation != null) animation.Die();
            GetComponent<Rigidbody>().velocity = Vector3.zero;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnGUI()
        {
            if (LunarPlayerRole.MenuOpen) return; // la pantalla de roles ocupa todo
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 original = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            EnsureStyles();
            bool blink = Mathf.Repeat(Time.time, 0.6f) < 0.3f;

            // Flash rojo al recibir dano / vineta al asfixiarse
            if (Time.time - health.LastDamageTime < 0.2f) Fill(new Rect(0, 0, width, height), new Color(1, 0, 0, 0.2f));
            if (vitals != null && vitals.Suffocating && !health.IsDead) Fill(new Rect(0, 0, width, height), new Color(0.05f, 0.1f, 0.3f, blink ? 0.28f : 0.18f));

            // Titulo y ayuda (H): a la derecha del radar, que ocupa la esquina superior izquierda.
            float hx = 18;
            if (TryGetComponent(out LunarRadar radar) && radar.enabled)
            {
                hx = radar.Area.xMax + 14;
                if (status != null)
                {
                    labelStyle.normal.textColor = status.FlashlightOn ? new Color(1f, 0.92f, 0.5f) : new Color(0.6f, 0.6f, 0.65f);
                    GUI.Label(new Rect(radar.Area.x + 2, radar.Area.yMax + 4, 260, 22), status.FlashlightOn ? "LINTERNA: ENCENDIDA (T)" : "LINTERNA: APAGADA (T)", labelStyle);
                    labelStyle.normal.textColor = new Color(0.85f, 0.92f, 1f);
                }
            }
            var roleDef = RoleDef;
            GUI.Label(new Rect(hx, 12, 600, 24), stationHealingOnly ? "LUNA CERO  |  Expedicion y hordas" : "LUNA CERO  |  Arena de combate", labelStyle);
            if (role != null && role.HasRole)
            {
                labelStyle.normal.textColor = roleDef.color;
                GUI.Label(new Rect(hx + 330, 12, 300, 24), "ROL: " + roleDef.title.ToUpperInvariant(), labelStyle);
                labelStyle.normal.textColor = new Color(0.85f, 0.92f, 1f);
            }
            if (showHelp)
            {
                Fill(new Rect(hx - 4, 38, 480, 112 + (status != null ? 20 : 0) + (LunarShip.All.Count > 0 ? 20 : 0)), new Color(0, 0, 0, 0.45f));
                GUI.Label(new Rect(hx + 6, 44, 470, 20), "WASD mover | Shift correr | Espacio saltar (en el aire: propulsor)", smallStyle);
                GUI.Label(new Rect(hx + 6, 64, 470, 20), "Clic disparar/golpear | R recargar | F curar | E usar/recoger | G lanzar", smallStyle);
                GUI.Label(new Rect(hx + 6, 84, 470, 20), "1-9 o rueda: cambiar arma | Clic derecho: apuntar (rifle de riel)", smallStyle);
                GUI.Label(new Rect(hx + 6, 104, 470, 20), "V: primera / tercera persona | H ocultar ayuda", smallStyle);
                GUI.Label(new Rect(hx + 6, 124, 470, 20), "Esc libera el cursor | Clic vuelve a capturarlo", smallStyle);
                if (status != null) GUI.Label(new Rect(hx + 6, 144, 470, 20), "Q empujar / liberarte (3 veces) | T linterna", smallStyle);
                if (LunarShip.All.Count > 0) GUI.Label(new Rect(hx + 6, status != null ? 164 : 144, 470, 20), "La nave tiene armas, municion y oxigeno", smallStyle);
            }
            else GUI.Label(new Rect(hx, 36, 300, 20), "H: controles", smallStyle);

            // Barras de soporte vital (abajo a la izquierda)
            float panelHeight = vitals != null ? 152 : 50;
            float px = 18, py = height - panelHeight - 18;
            Fill(new Rect(px - 6, py - 8, 340, panelHeight + 12), new Color(0, 0, 0, 0.5f));
            float hp = health.Current / health.Maximum;
            Bar(new Rect(px, py, 324, 30), "VIDA", $"{health.Current:0}/{health.Maximum:0}", hp,
                hp < 0.25f && blink ? new Color(1f, 0.35f, 0.35f) : Color.Lerp(new Color(0.85f, 0.12f, 0.12f), new Color(0.25f, 0.85f, 0.3f), hp));
            if (vitals != null)
            {
                float o2 = vitals.Oxygen / vitals.MaxOxygen;
                string o2Text = vitals.Refilling ? "RECARGANDO" : $"{o2 * 100f:0}%";
                Bar(new Rect(px, py + 40, 324, 30), "OXIGENO", o2Text, o2,
                    o2 < 0.2f && blink ? new Color(1f, 0.55f, 0.2f) : new Color(0.2f, 0.75f, 1f));
                float st = vitals.Stamina / vitals.MaxStamina;
                Bar(new Rect(px, py + 80, 324, 30), "ENERGIA", vitals.Exhausted ? "AGOTADO" : $"{st * 100f:0}%", st,
                    vitals.Exhausted ? new Color(0.5f, 0.5f, 0.5f) : new Color(1f, 0.82f, 0.2f));
                float fuel = vitals.Fuel / vitals.MaxFuel;
                Bar(new Rect(px, py + 120, 324, 30), "PROPULSOR", $"{fuel * 100f:0}%", fuel, new Color(0.7f, 0.45f, 1f));
            }

            // Municion y botiquines (abajo a la derecha)
            float ax = width - 250, ay = height - 118;
            Fill(new Rect(ax, ay, 232, 100), new Color(0, 0, 0, 0.5f));
            var current = CurrentWeapon;
            labelStyle.normal.textColor = current != null ? Color.Lerp(current.Data.color, Color.white, 0.45f) : Color.white;
            GUI.Label(new Rect(ax + 12, ay + 4, 210, 22), current != null ? current.Data.displayName.ToUpperInvariant() : "-", labelStyle);
            labelStyle.normal.textColor = new Color(0.85f, 0.92f, 1f);
            bool melee = current != null && current.Data.IsMelee;
            if (melee)
            {
                bigStyle.normal.textColor = Color.white;
                GUI.Label(new Rect(ax + 12, ay + 24, 210, 44), "GOLPE", bigStyle);
                GUI.Label(new Rect(ax + 92, ay + 38, 130, 24), current.Data.durability > 0 ? $"uso {current.Ammo}/{current.Data.durability}" : "irrompible", valueStyle);
            }
            else
            {
                bigStyle.normal.textColor = Ammo == 0 && blink ? new Color(1f, 0.35f, 0.3f) : Color.white;
                GUI.Label(new Rect(ax + 12, ay + 24, 120, 44), Ammo.ToString(), bigStyle);
                GUI.Label(new Rect(ax + 92, ay + 38, 130, 24), $"/ {ReserveAmmo}", valueStyle);
            }
            GUI.Label(new Rect(ax + 12, ay + 72, 220, 22), Reloading ? "RECARGANDO..." : stationHealingOnly && Medkits == 0 ? "Curacion: estaciones" : $"Botiquines: {Medkits} (F)", smallStyle);

            // Ranuras de armas (1-9 / rueda del raton)
            for (int i = 0; i < weapons.Count; i++)
            {
                var slot = weapons[i];
                // Hasta 5 por fila; las demas suben una fila.
                var r = new Rect(width - 250 + (i % 5) * 47, ay - 34 - (i / 5) * 32, 44, 28);
                Fill(r, i == currentIndex ? new Color(slot.Data.color.r, slot.Data.color.g, slot.Data.color.b, 0.85f) : new Color(0, 0, 0, 0.45f));
                Fill(new Rect(r.x, r.yMax - 3, r.width, 3), slot.Data.color);
                GUI.Label(new Rect(r.x + 4, r.y + 1, 40, 14), (i + 1).ToString(), smallStyle);
                GUI.Label(new Rect(r.x, r.y + 12, r.width - 4, 14), slot.Data.IsMelee ? (slot.Data.durability > 0 ? "x" + slot.Ammo : "golpe") : slot.Ammo + "/" + slot.Reserve, valueStyle);
            }

            // Mira
            if (Aiming)
            {
                // Mira telescopica simple
                Fill(new Rect(0, height / 2 - 0.5f, width, 1), new Color(0, 0, 0, 0.8f));
                Fill(new Rect(width / 2 - 0.5f, 0, 1, height), new Color(0, 0, 0, 0.8f));
            }
            centerStyle.normal.textColor = Time.time < hitUntil ? new Color(1f, 0.3f, 0.3f) : Color.white;
            GUI.Label(new Rect(width / 2 - 15, height / 2 - 15, 30, 30), Time.time < hitUntil ? "X" : "+", centerStyle);

            // Avisos
            centerStyle.normal.textColor = new Color(1f, 0.6f, 0.25f);
            if (vitals != null && !health.IsDead)
            {
                if (vitals.Suffocating) GUI.Label(new Rect(width / 2 - 250, 110, 500, 30), "SIN OXIGENO: llega a una estacion medica!", centerStyle);
                else if (vitals.Oxygen / vitals.MaxOxygen < 0.2f && blink) GUI.Label(new Rect(width / 2 - 250, 110, 500, 30), "OXIGENO BAJO - recarga en una estacion medica", centerStyle);
            }
            centerStyle.normal.textColor = Color.white;
            if (Healing) GUI.Box(new Rect(width / 2 - 170, height / 2 + 50, 340, 55), $"SANANDO... {roleDef.stationHealTime:0.#} segundos\nNo salgas del anillo ni recibas dano.");
            string prompt = held != null ? "E: soltar | G: lanzar " + held.displayName : nearby != null ? $"E: {nearby.verb} {nearby.displayName}" : "";
            if (prompt.Length > 0) GUI.Label(new Rect(width / 2 - 250, height - 90, 500, 25), prompt, centerStyle);
            if (Time.time < messageUntil) GUI.Label(new Rect(width / 2 - 250, height - 60, 500, 25), message, centerStyle);
            if (health.IsDead) GUI.Box(new Rect(width / 2 - 180, height / 2 - 50, 360, 100), "HAS MUERTO\n\nEnter: reiniciar con el mismo rol\nC: elegir otro rol");
            GUI.matrix = original;
        }

        private void Bar(Rect area, string label, string value, float fraction, Color color)
        {
            GUI.Label(new Rect(area.x, area.y - 2, 120, 16), label, smallStyle);
            GUI.Label(new Rect(area.x, area.y - 2, area.width, 16), value, valueStyle);
            var track = new Rect(area.x, area.y + 15, area.width, area.height - 17);
            Fill(track, new Color(1, 1, 1, 0.12f));
            Fill(new Rect(track.x, track.y, track.width * Mathf.Clamp01(fraction), track.height), color);
            Fill(new Rect(track.x, track.y, track.width * Mathf.Clamp01(fraction), 3), new Color(1, 1, 1, 0.25f));
        }

        private static void Fill(Rect area, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (labelStyle != null) return;
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            labelStyle.normal.textColor = new Color(0.85f, 0.92f, 1f);
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            smallStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            valueStyle = new GUIStyle(smallStyle) { alignment = TextAnchor.UpperRight };
            bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 36, fontStyle = FontStyle.Bold };
            centerStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }
    }
}





