using UnityEngine;
using UnityEngine.AI;

namespace LunaCero
{
    [RequireComponent(typeof(NavMeshAgent), typeof(LunarHealth))]
    public sealed partial class LunarEnemy : MonoBehaviour
    {
        public string displayName = "Bot debil";
        public bool isBoss;
        public float detectionRange = 18f;
        public float attackRange = 1.9f;
        public float attackDamage = 10f;
        public float attackInterval = 1.1f;
        public bool ranged;
        public int burstCount = 1;
        private NavMeshAgent agent;
        private LunarHealth health;
        private CapsuleCollider bodyCollider;
        private Rigidbody bodyRigidbody;
        private LunarHealth target;
        private float nextAttack;
        private float nextPath;
        private bool alerted;
        private Vector3 lastSafePosition;
        private bool hasSafePosition;
        private const float CollisionSkin = 0.04f;
        public bool IsDead => health != null && health.IsDead;

        /// <summary>Alcance real: los monstruos grandes golpean desde mas lejos (su cuerpo es mas ancho).</summary>
        public float Reach { get; private set; }
        public float BodyHeight { get; private set; } = 2f;
        private static Camera viewCamera;
        /// <summary>Barras de vida sobre los monstruos (apagadas; pon true para depurar).</summary>
        public static bool ShowHealthBars = false;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<LunarHealth>();
            bodyCollider = GetComponent<CapsuleCollider>();
            bodyRigidbody = GetComponent<Rigidbody>();
            health.Died += Die;
            Reach = attackRange;
            if (bodyCollider != null)
            {
                float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
                Reach = attackRange + Mathf.Max(0f, bodyCollider.radius * scale - 0.4f);
                BodyHeight = bodyCollider.height * transform.lossyScale.y;
                bodyCollider.isTrigger = false;

                // El agente y el cuerpo fisico deben tener el mismo volumen. Si el agente es
                // mas pequeno, puede calcular rutas pegadas a una pared y meter el modelo dentro.
                agent.radius = Mathf.Max(agent.radius, bodyCollider.radius);
                agent.height = Mathf.Max(agent.height, bodyCollider.height);
            }
            agent.autoRepath = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            if (bodyRigidbody != null)
            {
                bodyRigidbody.isKinematic = true;
                bodyRigidbody.useGravity = false;
                bodyRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                bodyRigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            }
        }

        // ---------------------------------------------------------------- Multijugador
        /// <summary>Monstruos con IA activa (en el anfitrion o en solitario). Las copias de red no estan aqui.</summary>
        public static readonly System.Collections.Generic.List<LunarEnemy> All = new System.Collections.Generic.List<LunarEnemy>();
        /// <summary>Indice del prefab en el director (para crear la copia en los clientes). -1 = no se replica.</summary>
        [System.NonSerialized] public int NetPrefab = -1;
        [System.NonSerialized] public uint NetId;
        /// <summary>Copia de red: el aturdimiento se envia al anfitrion en lugar de aplicarse aqui.</summary>
        public System.Action<float> StaggerRedirect;
        private float nextRetarget;
        private void OnEnable() { All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        /// <summary>Copia de red: muestra la muerte que decidio el anfitrion.</summary>
        public void ShowRemoteDeath()
        {
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            bool animated = false;
            if (TryGetComponent(out LunarAlienAnimation alienAnimation)) { alienAnimation.Die(); animated = alienAnimation.HasDeath; }
            var rider = GetComponent<LunarRiderAnimation>();
            if (rider != null) rider.Die();
            else if (!animated) { var visual = transform.Find("Visual"); if (visual != null) visual.localRotation = Quaternion.Euler(0, 0, 85); }
            Destroy(gameObject, alienAnimation != null && animated ? Mathf.Max(4f, alienAnimation.deathDuration) : 4f);
        }

        /// <summary>El tripulante vivo mas cercano (cualquier jugador). No cambia mientras ataca.</summary>
        private void Retarget()
        {
            if (Time.time < nextRetarget && target != null && !target.IsDead && !target.IsInvulnerable) return;
            nextRetarget = Time.time + 0.6f;
            if (busy) return;
            var nearest = LunarCrew.Nearest(transform.position);
            if (nearest == null)
            {
                var player = FindObjectOfType<LunarPlayerCombat>();
                if (player != null && !player.GetComponent<LunarHealth>().IsInvulnerable) nearest = player.GetComponent<LunarHealth>();
            }
            target = nearest;
        }

        private void Start()
        {
            Retarget();
            nextAttack = Time.time + 3f;
            nextAbility = Time.time + 2f;
            if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                agent.enabled = true;
                agent.Warp(hit.position);
                SetSafePosition(hit.position);
            }
            else Debug.LogError("No hay suelo navegable para " + displayName, this);
        }

        private void LateUpdate()
        {
            if (health == null || health.IsDead || bodyCollider == null || !bodyCollider.enabled) return;
            if (!hasSafePosition) { SetSafePosition(transform.position); return; }

            Vector3 movement = transform.position - lastSafePosition;
            if (movement.sqrMagnitude < 0.000001f) return;
            if (TrySweepBody(lastSafePosition, movement, out float allowedDistance))
            {
                Vector3 corrected = lastSafePosition + movement.normalized * allowedDistance;
                if (agent.enabled && agent.isOnNavMesh) agent.Warp(corrected);
                else transform.position = corrected;
            }
            SetSafePosition(transform.position);
        }

        /// <summary>
        /// Mueve una habilidad sin atravesar paredes. Devuelve false cuando el cuerpo choca.
        /// </summary>
        private bool TrySpecialMove(Vector3 destination, bool throughAgent)
        {
            Vector3 movement = destination - transform.position;
            if (movement.sqrMagnitude < 0.000001f) return true;
            if (TrySweepBody(transform.position, movement, out float allowedDistance))
            {
                Vector3 corrected = transform.position + movement.normalized * allowedDistance;
                if (throughAgent && agent.enabled && agent.isOnNavMesh) agent.Move(corrected - transform.position);
                else transform.position = corrected;
                if (!throughAgent) SetSafePosition(transform.position);
                return false;
            }

            if (throughAgent && agent.enabled && agent.isOnNavMesh) agent.Move(movement);
            else transform.position = destination;
            if (!throughAgent) SetSafePosition(transform.position);
            return true;
        }

        private bool TrySweepBody(Vector3 origin, Vector3 movement, out float allowedDistance)
        {
            float distance = movement.magnitude;
            allowedDistance = distance;
            if (distance <= 0.0001f || bodyCollider == null || !bodyCollider.enabled) return false;

            GetCapsuleAt(origin, out Vector3 bottom, out Vector3 top, out float radius);
            RaycastHit[] hits = Physics.CapsuleCastAll(bottom, top, radius, movement / distance,
                distance + CollisionSkin, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
                // El suelo y las pendientes transitables no son paredes laterales al avanzar
                // o despegar. Al descender si se conservan para que el salto aterrice encima.
                if (hit.normal.y > 0.62f && movement.y >= -0.01f) continue;
                if (hit.distance < nearest) nearest = hit.distance;
            }

            if (nearest == float.MaxValue) return false;
            allowedDistance = Mathf.Max(0f, Mathf.Min(distance, nearest - CollisionSkin));
            return true;
        }

        private void GetCapsuleAt(Vector3 position, out Vector3 bottom, out Vector3 top, out float radius)
        {
            Vector3 scale = transform.lossyScale;
            radius = bodyCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float height = Mathf.Max(radius * 2f, bodyCollider.height * Mathf.Abs(scale.y));
            Vector3 center = position + transform.rotation * Vector3.Scale(bodyCollider.center, scale);
            Vector3 axis = transform.up * Mathf.Max(0f, height * 0.5f - radius);
            bottom = center - axis;
            top = center + axis;
        }

        private void SetSafePosition(Vector3 position)
        {
            lastSafePosition = position;
            hasSafePosition = true;
        }

        private void Update()
        {
            if (LunarSession.WaitingForMatch) { Stop(); return; }
            if (!health.IsDead) Retarget();
            if (health.IsDead || target == null || target.IsDead || target.IsInvulnerable || !agent.enabled || !agent.isOnNavMesh) { Stop(); return; }
            float distance = Vector3.Distance(transform.position, target.transform.position);
            if (SpecialUpdate(distance)) return;
            if (relentless || distance <= detectionRange || health.Current < health.Maximum) alerted = true;
            if (!alerted) return;
            // La linea de vision solo importa al estar a distancia de golpe (ahorra rayos por enemigo).
            if (distance > Reach || !CanSeeTarget())
            {
                agent.isStopped = false;
                if (Time.time >= nextPath)
                {
                    agent.SetDestination(target.transform.position);
                    nextPath = Time.time + 0.25f;
                }
            }
            else
            {
                Stop();
                Vector3 facing = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
                if (facing.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(facing);
                if (Time.time >= nextAttack)
                {
                    nextAttack = Time.time + attackInterval;
                    if (ranged) StartCoroutine(Burst());
                    else PerformMelee();
                }
            }
        }

        private System.Collections.IEnumerator Burst()
        {
            for (int i = 0; i < burstCount; i++)
            {
                if (health.IsDead || target == null || target.IsDead) yield break;
                // Each shot checks cover again, so moving behind a wall stops a burst.
                if (CanSeeTarget())
                {
                    Vector3 origin = transform.position + Vector3.up * 1.1f;
                    Vector3 end = target.transform.position + Vector3.up;
                    LunarCombatEffects.Beam(origin, end, isBoss ? Color.magenta : Color.red);
                    target.Damage(attackDamage);
                }
                yield return new WaitForSeconds(0.18f);
            }
        }

        private bool CanSeeTarget()
        {
            Vector3 origin = transform.position + Vector3.up * 1.1f;
            Vector3 delta = target.transform.position + Vector3.up - origin;
            var hits = Physics.RaycastAll(origin, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            Collider first = null;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                first = hit.collider;
            }
            return first == null || first.GetComponentInParent<LunarHealth>() == target;
        }

        private void Stop() { if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true; }

        private void Die(LunarHealth dead)
        {
            SpecialDeath();
            StopAllCoroutines();
            Stop();
            agent.enabled = false;
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            var visual = transform.Find("Visual");
            var animated = GetComponent<LunarRiderAnimation>();
            bool animatedDeath = false;
            float wait = 4f;
            if (TryGetComponent(out LunarAlienAnimation alienAnimation))
            {
                alienAnimation.Die();
                animatedDeath = alienAnimation.HasDeath;
                if (animatedDeath) wait = Mathf.Max(4f, alienAnimation.deathDuration);
            }
            if (animated != null) { animated.Die(); wait = Mathf.Max(4f, animated.deathDuration); }
            else if (!animatedDeath && visual != null) visual.localRotation = Quaternion.Euler(0, 0, 85);
            Destroy(gameObject, wait);
        }

        private void OnGUI()
        {
            // Barras de vida desactivadas: el jugador solo ve al monstruo (el impacto se indica con la mira roja).
            if (!ShowHealthBars || Event.current.type != EventType.Repaint || health == null || health.IsDead || LunarPlayerRole.MenuOpen) return;
            if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = Camera.main;
            if (viewCamera == null) return;
            // Solo los cercanos (o jefes) muestran barra: menos trabajo de interfaz con muchas criaturas.
            float range = (transform.position - viewCamera.transform.position).sqrMagnitude;
            if (range > (isBoss ? 90f * 90f : 40f * 40f)) return;
            Vector3 position = viewCamera.WorldToScreenPoint(transform.position + Vector3.up * (BodyHeight + 0.5f));
            if (position.z <= 0 || position.x < 0 || position.x > Screen.width || position.y < 0 || position.y > Screen.height) return;
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Rect bar = new Rect(position.x - 65f * scale, Screen.height - position.y, 130f * scale, 7f * scale);
            GUI.color = Color.black;
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = isBoss ? new Color(1f, 0.3f, 0.6f) : Color.red;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * health.Current / health.Maximum, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUI.Label(new Rect(position.x / scale - 85, (Screen.height - position.y) / scale - 25, 250, 25), $"{displayName} {health.Current:0}");
            GUI.matrix = old;
        }
    }
}

