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
        public bool CanAct => health != null && !health.IsDead && !LunarPlayerRole.MenuOpen && (status == null || !status.BlocksActions);
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
            if (GetComponent<LunarDamageFeedback>() == null) gameObject.AddComponent<LunarDamageFeedback>(); // sonido y direccion del dano
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
            LunarCrew.Add(health);
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
            var added = new LunarWeaponSlot(data, data.startReserve);
            string ammoKey = data.id == "riel" ? "rifle" : data.id;
            if (!data.IsMelee && pendingAmmo.TryGetValue(ammoKey, out int stored))
            {
                added.Reserve += Mathf.Max(1, Mathf.RoundToInt(stored * data.ammoPickupScale));
                pendingAmmo.Remove(ammoKey);
            }
            weapons.Add(added);
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
            Drop(false);
            if (reloadRoutine != null) { StopCoroutine(reloadRoutine); reloadRoutine = null; }
            Reloading = false;
            currentIndex = index;
            if (announce) nextShot = Mathf.Max(nextShot, Time.time + 0.3f);
            ApplyWeaponVisual(CurrentWeapon.Data);
            if (announce) { Tell(CurrentWeapon.Data.displayName); LunarAudio.Play("Armas/" + LunarAudio.Weapon(CurrentWeapon.Data.id) + "/Equipar", transform.position, 0.5f, false); }
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
            // Tercera persona: el arma va en la mano derecha del astronauta (antes flotaba al lado).
            if (!TryGetComponent(out LunarHandHold handHold)) handHold = gameObject.AddComponent<LunarHandHold>();
            handHold.Attach(weaponVisual, weaponModel, data.IsMelee, aimCamera != null ? aimCamera.transform : null);
        }

        private void HandleWeaponInput()
        {
            for (int i = 0; i < weapons.Count && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) && (i != currentIndex || held != null)) Equip(i);
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && (weapons.Count > 1 || held != null))
                Equip((currentIndex + (wheel < 0 ? 1 : -1) + weapons.Count) % weapons.Count);
            if (held != null) { Aiming = false; return; }
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
                // En red no se recarga el mapa (se perderia la partida de todos): se reaparece en la base.
                if (LunarSession.InMultiplayer) { if (Input.GetKeyDown(KeyCode.Return)) Revive(); }
                else if (Input.GetKeyDown(KeyCode.C)) { LunarPlayerRole.Chosen = null; SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); }
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
            if (!CanAct || held != null || Reloading || Healing || weapon == null || Time.time < nextShot) return false;
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
            LunarAudio.Play("Armas/" + LunarAudio.Weapon(data.id) + "/Disparo_0" + Random.Range(1,4), transform.position, 0.65f, false);
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
                if (victim != null && !LunarCrew.IsCrew(victim) && victim != health)
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
            if (victim != null && !LunarCrew.IsCrew(victim) && victim != health)
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
                if (victim == null || victim == health || LunarCrew.IsCrew(victim)) { HitSingle(hit, shot.direction, 0); end = hit.point; break; }
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
                if (victim != null && !LunarCrew.IsCrew(victim) && victim != health && damaged.Add(victim))
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
            string audioPath = "Armas/" + LunarAudio.Weapon(weapon.Data.id) + "/";
            float duration = weapon.Data.reloadDuration * RoleDef.reloadTime;
            LunarAudio.Play(audioPath + "Recarga_Extraer_Celda", transform.position, 0.5f, false);
            yield return new WaitForSeconds(duration * 0.45f);
            LunarAudio.Play(audioPath + "Recarga_Insertar_Celda", transform.position, 0.5f, false);
            yield return new WaitForSeconds(duration * 0.35f);
            LunarAudio.Play(audioPath + "Recarga_Activar_Energia", transform.position, 0.5f, false);
            yield return new WaitForSeconds(duration * 0.2f);
            if (CanAct)
            {
                int amount = Mathf.Min(weapon.Data.magazineSize - weapon.Ammo, weapon.Reserve);
                weapon.Ammo += amount;
                weapon.Reserve -= amount;
                LunarAudio.Play(audioPath + "Recarga_Completa", transform.position, 0.5f, false);
            }
            Reloading = false;
            reloadRoutine = null;
        }

        /// <summary>Las cajas azules recargan la reserva del arma equipada, escalada segun el arma.</summary>
        public bool AddAmmoForWeapon(string id, int amount)
        {
            if(string.IsNullOrEmpty(id)) { AddAmmo(amount); return true; }
            var slot=weapons.FirstOrDefault(w=>w.Data.id==id);
            if(id=="rifle" && CurrentWeapon?.Data.id=="riel") slot=CurrentWeapon;
            if(slot==null && id=="rifle") slot=weapons.FirstOrDefault(w=>w.Data.id=="riel");
            if(slot==null)
            {
                // Sin esa arma todavia: se guarda en la mochila y se suma cuando la recojas.
                pendingAmmo.TryGetValue(id, out int stored);
                pendingAmmo[id] = stored + Mathf.Max(1, amount);
                var data = FindWeapon(id);
                Tell($"Municion guardada para {(data != null ? data.displayName : id)} ({pendingAmmo[id]})");
                return true;
            }
            slot.Reserve+=Mathf.Max(1,Mathf.RoundToInt(amount*slot.Data.ammoPickupScale));
            Tell("Municion para "+slot.Data.displayName); return true;
        }
        /// <summary>Municion recogida de armas que aun no se tienen (por id de arma).</summary>
        private readonly Dictionary<string, int> pendingAmmo = new Dictionary<string, int>();
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
            Aiming = false;
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

        /// <summary>Multijugador: vuelve a la base con vida, oxigeno y energia, conservando las armas.</summary>
        public void Revive()
        {
            if (!health.IsDead) return;
            health.Revive();
            if (vitals != null) vitals.Refill();
            movement.enabled = true;
            movement.Respawn();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Tell("Has reaparecido en la base.");
        }

        private void OnDestroy() { LunarCrew.Remove(health); }

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

        /// <summary>Borde inferior (en coordenadas del HUD) del panel de soporte vital; los paneles de la derecha se colocan debajo.</summary>
        public static float VitalsPanelBottom = 190f;
        private LunarRepairPoint repairCache;
        private float repairLookup;

        private void OnGUI()
        {
            if (LunarPlayerRole.MenuOpen || LostInSpaceFinale.Active) return; // la pantalla de roles o el final ocupan todo
            var size = LunarHud.Begin(out Matrix4x4 original);
            float width = size.x, height = size.y;
            EnsureStyles();
            bool blink = Mathf.Repeat(Time.time, 0.6f) < 0.3f;

            // Flash rojo al recibir dano / vineta al asfixiarse
            if (Time.time - health.LastDamageTime < 0.2f) Fill(new Rect(0, 0, width, height), new Color(1, 0, 0, 0.2f));
            if (vitals != null && vitals.Suffocating && !health.IsDead) Fill(new Rect(0, 0, width, height), new Color(0.05f, 0.1f, 0.3f, blink ? 0.28f : 0.18f));

            // Linterna bajo el radar y ayuda (H) a su derecha.
            float hx = 18;
            if (TryGetComponent(out LunarRadar radar) && radar.enabled)
            {
                hx = radar.Area.xMax + 14;
                if (status != null)
                {
                    Color lamp = status.FlashlightOn ? new Color(1f, 0.85f, 0.3f) : new Color(0.55f, 0.58f, 0.65f);
                    LunarHud.Icon(new Rect(radar.Area.x + 2, radar.Area.yMax + 6, 34, 22), LunarHud.Flashlight, lamp);
                    LunarHud.Label.normal.textColor = lamp;
                    GUI.Label(new Rect(radar.Area.x + 42, radar.Area.yMax + 6, 220, 22), status.FlashlightOn ? "Linterna [T]" : "Linterna apagada [T]", LunarHud.Label);
                    LunarHud.Label.normal.textColor = LunarHud.Text;
                }
            }
            var roleDef = RoleDef;
            DrawVitals(width, blink);
            DrawMission(height, roleDef);
            if (showHelp)
            {
                Fill(new Rect(hx - 4, 38, 480, 112 + (status != null ? 20 : 0) + (LunarShip.All.Count > 0 ? 20 : 0)), new Color(0, 0, 0, 0.45f));
                GUI.Label(new Rect(hx + 6, 44, 470, 20), "WASD mover | Shift correr | Espacio saltar (en el aire: propulsor)", smallStyle);
                GUI.Label(new Rect(hx + 6, 64, 470, 20), "Clic disparar/golpear | R recargar | F curar | E usar/recoger | G lanzar", smallStyle);
                GUI.Label(new Rect(hx + 6, 84, 470, 20), "1-9 o rueda: cambiar arma | Clic derecho: apuntar (rifle de riel)", smallStyle);
                GUI.Label(new Rect(hx + 6, 104, 470, 20), "B: emote | V: primera / tercera persona | H ocultar ayuda", smallStyle);
                GUI.Label(new Rect(hx + 6, 124, 470, 20), "Esc libera el cursor | Clic vuelve a capturarlo", smallStyle);
                if (status != null) GUI.Label(new Rect(hx + 6, 144, 470, 20), "Q empujar / liberarte (3 veces) | T linterna", smallStyle);
                if (LunarShip.All.Count > 0) GUI.Label(new Rect(hx + 6, status != null ? 164 : 144, 470, 20), "La nave tiene armas, municion y oxigeno", smallStyle);
            }
            else GUI.Label(new Rect(hx, 36, 300, 20), "H: controles", smallStyle);

            DrawWeapons(width, height, blink);

            // Mira: cuatro trazos con hueco central (roja al acertar)
            if (Aiming)
            {
                // Mira telescopica simple
                Fill(new Rect(0, height / 2 - 0.5f, width, 1), new Color(0, 0, 0, 0.8f));
                Fill(new Rect(width / 2 - 0.5f, 0, 1, height), new Color(0, 0, 0, 0.8f));
            }
            Color cross = Time.time < hitUntil ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 1f, 1f, 0.92f);
            float cx = width / 2, cy = height / 2, gap = Time.time < hitUntil ? 7f : 5f;
            Fill(new Rect(cx - 1, cy - gap - 9, 2, 9), cross);
            Fill(new Rect(cx - 1, cy + gap, 2, 9), cross);
            Fill(new Rect(cx - gap - 9, cy - 1, 9, 2), cross);
            Fill(new Rect(cx + gap, cy - 1, 9, 2), cross);

            // Avisos
            centerStyle.normal.textColor = new Color(1f, 0.6f, 0.25f);
            if (vitals != null && !health.IsDead)
            {
                if (vitals.Suffocating) GUI.Label(new Rect(width / 2 - 250, 110, 500, 30), "SIN OXIGENO: llega a una estacion medica!", centerStyle);
                else if (vitals.Oxygen / vitals.MaxOxygen < 0.2f && blink) GUI.Label(new Rect(width / 2 - 250, 110, 500, 30), "OXIGENO BAJO - recarga en una estacion medica", centerStyle);
            }
            centerStyle.normal.textColor = Color.white;
            if (LunarSession.InMultiplayer && role != null && role.ProtectionSeconds > 0f)
                GUI.Label(new Rect(width / 2 - 250, height / 2 + 70, 500, 25), $"PROTECCION DE ENTRADA: {Mathf.CeilToInt(role.ProtectionSeconds)} s", centerStyle);
            if (Healing) LunarHud.Prompt(width / 2, height / 2 + 50, "F", $"SANANDO... {roleDef.stationHealTime:0.#} segundos", "No salgas del anillo ni recibas dano.");
            // Aviso de interaccion: aparece al acercarte a algo que se puede usar o recoger.
            if (!health.IsDead && !Healing)
            {
                if (held != null)
                {
                    var repair = Repair;
                    bool part = held.GetComponent<LunarShipPart>() != null;
                    string sub = part && repair != null
                        ? $"Llevala al anillo verde de la nave [{Vector3.Distance(transform.position, repair.transform.position):0} m]  |  G: lanzar"
                        : "G: lanzar  |  1-9: soltar y cambiar de arma";
                    LunarHud.Prompt(width / 2, height / 2 + 70, "E", "Soltar " + held.displayName, sub);
                }
                else if (nearby != null)
                    LunarHud.Prompt(width / 2, height / 2 + 70, "E", Capital(nearby.verb) + " " + nearby.displayName, Describe(nearby), nearby.GetComponent<LunarShipPart>() != null);
            }
            if (Time.time < messageUntil) GUI.Label(new Rect(width / 2 - 300, height / 2 + 136, 600, 25), message, centerStyle);
            if (health.IsDead) LunarHud.Prompt(width / 2, height / 2 - 40, "", "HAS MUERTO", LunarSession.InMultiplayer
                ? "Enter: reaparecer en la base (tus companeros siguen jugando)"
                : "Enter: reiniciar con el mismo rol  |  C: elegir otro rol", true);
            GUI.matrix = original;
        }

        private LunarRepairPoint Repair
        {
            get
            {
                if (repairCache == null && Time.unscaledTime >= repairLookup) { repairCache = FindObjectOfType<LunarRepairPoint>(); repairLookup = Time.unscaledTime + 2f; }
                return repairCache;
            }
        }

        private static string Capital(string value) => string.IsNullOrEmpty(value) ? "" : char.ToUpperInvariant(value[0]) + value.Substring(1);

        private string Describe(LunarPickup pickup)
        {
            if (pickup.GetComponent<LunarShipPart>() != null) return "Pieza de la nave: llevala a la zona de reparacion";
            switch (pickup.kind)
            {
                case LunarPickupKind.Weapon:
                    var data = FindWeapon(pickup.weaponId);
                    return data != null && data.IsMelee ? "Objeto contundente: golpea y aturde" : "Arma de fuego";
                case LunarPickupKind.Ammo: return "Municion para tus armas";
                case LunarPickupKind.Medkit: return "Botiquin: F para curarte";
                case LunarPickupKind.AmmoLocker: return Time.time >= pickup.ReadyAt ? "Rellena la reserva de todas tus armas" : $"Disponible en {Mathf.CeilToInt(pickup.ReadyAt - Time.time)} s";
                case LunarPickupKind.Ice: return "Hielo: se convierte en oxigeno";
                default: return "Se puede cargar y lanzar";
            }
        }

        /// <summary>Panel de soporte vital (arriba a la derecha): corazones, burbujas de oxigeno, energia y propulsor.</summary>
        private void DrawVitals(float width, bool blink)
        {
            float rows = vitals != null ? 4 : 1;
            var box = new Rect(width - 284, 12, 270, 12 + rows * 32);
            VitalsPanelBottom = box.yMax;
            LunarHud.Panel(box);
            float x = box.x + 12, y = box.y + 8, valueX = box.xMax - 76;

            // Vida: 5 corazones (cada uno una quinta parte), el ultimo se llena por partes.
            float hp = health.Current / health.Maximum;
            const int hearts = 5;
            for (int i = 0; i < hearts; i++)
            {
                var r = new Rect(x + i * 30, y + 2, 26, 22);
                LunarHud.Icon(r, LunarHud.Heart, new Color(0.25f, 0.27f, 0.32f, 0.9f));
                float part = Mathf.Clamp01(hp * hearts - i);
                if (part <= 0f) continue;
                Color red = hp < 0.25f && blink ? new Color(1f, 0.55f, 0.55f) : new Color(0.95f, 0.16f, 0.22f);
                Color previous = GUI.color;
                GUI.color = red;
                GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y, r.width * part, r.height), LunarHud.Heart, new Rect(0, 0, part, 1));
                GUI.color = previous;
            }
            GUI.Label(new Rect(valueX - 20, y + 1, 84, 24), $"{health.Current:0} HP", LunarHud.Value);
            if (vitals == null) return;

            // Oxigeno: 8 burbujas
            y += 32;
            float o2 = vitals.Oxygen / vitals.MaxOxygen;
            GUI.Label(new Rect(x, y, 30, 26), "O", LunarHud.Label);
            GUI.Label(new Rect(x + 12, y + 8, 14, 18), "2", LunarHud.Small);
            Color blue = o2 < 0.2f && blink ? new Color(1f, 0.55f, 0.2f) : new Color(0.25f, 0.75f, 1f);
            for (int i = 0; i < 8; i++)
            {
                var r = new Rect(x + 30 + i * 19, y + 5, 16, 16);
                float part = Mathf.Clamp01(o2 * 8 - i);
                LunarHud.Icon(r, LunarHud.Ring, new Color(0.45f, 0.5f, 0.58f, 0.8f));
                if (part > 0f) LunarHud.Icon(r, LunarHud.Bubble, new Color(blue.r, blue.g, blue.b, 0.25f + 0.75f * part));
            }
            GUI.Label(new Rect(valueX, y + 1, 64, 24), vitals.Refilling ? "+O2" : $"{o2 * 100f:0}%", LunarHud.Value);

            // Energia (correr)
            y += 32;
            float st = vitals.Stamina / vitals.MaxStamina;
            Color yellow = vitals.Exhausted ? new Color(0.5f, 0.5f, 0.5f) : new Color(1f, 0.85f, 0.15f);
            LunarHud.Icon(new Rect(x + 2, y + 2, 16, 22), LunarHud.Bolt, yellow);
            LunarHud.Segments(new Rect(x + 30, y + 7, 150, 13), st, 8, yellow, new Color(1f, 1f, 1f, 0.12f));
            GUI.Label(new Rect(valueX, y + 1, 64, 24), vitals.Exhausted ? "--" : $"{st * 100f:0}%", LunarHud.Value);

            // Propulsor (combustible propio)
            y += 32;
            float fuel = vitals.Fuel / vitals.MaxFuel;
            Color cyan = vitals.FuelCoolingDown ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.3f, 0.82f, 1f);
            LunarHud.Icon(new Rect(x, y + 1, 20, 24), LunarHud.Jetpack, cyan);
            LunarHud.Segments(new Rect(x + 30, y + 7, 150, 13), fuel, 8, cyan, new Color(1f, 1f, 1f, 0.12f));
            GUI.Label(new Rect(valueX, y + 1, 64, 24), vitals.FuelCoolingDown ? $"{Mathf.CeilToInt(vitals.FuelCooldownRemaining)} s" : $"{fuel * 100f:0}%", LunarHud.Value);
        }

        /// <summary>Abajo a la izquierda: rol y objetivo actual (dos lineas).</summary>
        private void DrawMission(float height, LunarRoleDefinition roleDef)
        {
            var box = new Rect(18, height - 66, 420, 50);
            LunarHud.Fill(box, new Color(0.01f, 0.03f, 0.06f, 0.45f));
            LunarHud.Fill(new Rect(box.x, box.y, 3, box.height), role != null && role.HasRole ? roleDef.color : LunarHud.Cyan);
            if (role != null && role.HasRole)
            {
                LunarHud.Label.normal.textColor = roleDef.color;
                GUI.Label(new Rect(box.x + 12, box.y + 4, 380, 22), roleDef.title, LunarHud.Label);
                LunarHud.Label.normal.textColor = LunarHud.Text;
            }
            GUI.Label(new Rect(box.x + 12, box.y + 26, 400, 20), Objective(), LunarHud.Small);
        }

        private string Objective()
        {
            var repair = Repair;
            if (held != null && held.GetComponent<LunarShipPart>() != null) return "Lleva la pieza al anillo verde de reparacion de la nave.";
            if (vitals != null && vitals.Oxygen / vitals.MaxOxygen < 0.3f) return "Oxigeno bajo: ve a una estacion medica o a la base.";
            if (health.Current / health.Maximum < 0.35f) return "Vida baja: busca una estacion medica (F para sanar).";
            if (repair != null && repair.Repaired) return "Nave reparada! Preparando el despegue.";
            if (repair != null) return $"Busca las piezas de la nave ({repair.Installed}/{repair.requiredParts}) y llevalas a la nave.";
            if (ReserveAmmo == 0 && CurrentWeapon != null && !CurrentWeapon.Data.IsMelee) return "Busca balas azules y llega a la estacion medica.";
            return "Sobrevive a las hordas.";
        }

        /// <summary>Abajo a la derecha: arma actual con su silueta y municion, y las ranuras 1-9 encima.</summary>
        private void DrawWeapons(float width, float height, bool blink)
        {
            var box = new Rect(width - 284, height - 96, 270, 80);
            LunarHud.Panel(box);
            var current = CurrentWeapon;
            if (current != null)
            {
                LunarHud.Icon(new Rect(box.x + 12, box.y + 6, 84, 30), LunarHud.WeaponIcon(current.Data), Color.Lerp(current.Data.color, Color.white, 0.55f));
                GUI.Label(new Rect(box.x + 104, box.y + 8, 170, 24), current.Data.displayName, LunarHud.Title);
                if (current.Data.IsMelee)
                {
                    GUI.Label(new Rect(box.x + 96, box.y + 30, box.width - 104, 44), current.Data.durability > 0 ? $"{current.Ammo} / {current.Data.durability}" : "GOLPE", LunarHud.Big);
                }
                else
                {
                    LunarHud.Big.normal.textColor = Ammo == 0 && blink ? new Color(1f, 0.35f, 0.3f) : Color.white;
                    GUI.Label(new Rect(box.x + 96, box.y + 30, box.width - 104, 44), $"{Ammo}  /  {ReserveAmmo}", LunarHud.Big);
                    LunarHud.Big.normal.textColor = Color.white;
                }
            }
            // Solo lo que cambia la decision del momento: recargando o botiquines disponibles.
            string footer = Reloading ? "RECARGANDO..." : Medkits > 0 ? $"+{Medkits} (F)" : "";
            if (footer.Length > 0) GUI.Label(new Rect(box.x + 14, box.yMax - 24, 120, 20), footer, LunarHud.Small);

            // Ranuras (hasta 5 por fila; las demas suben una fila)
            for (int i = 0; i < weapons.Count; i++)
            {
                var slot = weapons[i];
                int column = i % 5, row = i / 5;
                var r = new Rect(box.xMax - 5 * 56 + column * 56 + 4, box.y - 46 - row * 46, 52, 40);
                LunarHud.Panel(r, i == currentIndex);
                GUI.Label(new Rect(r.x + 7, r.y + 2, 20, 18), (i + 1).ToString(), LunarHud.Label);
                string ammo = slot.Data.IsMelee ? (slot.Data.durability > 0 ? "x" + slot.Ammo : "golpe") : slot.Ammo + "/" + slot.Reserve;
                GUI.Label(new Rect(r.x + 2, r.y + 19, r.width - 8, 18), ammo, LunarHud.Value);
            }
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






