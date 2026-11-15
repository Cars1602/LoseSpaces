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
        private LunarHealth target;
        private float nextAttack;
        private float nextPath;
        private bool alerted;
        public bool IsDead => health != null && health.IsDead;

        /// <summary>Alcance real: los monstruos grandes golpean desde mas lejos (su cuerpo es mas ancho).</summary>
        public float Reach { get; private set; }
        public float BodyHeight { get; private set; } = 2f;
        private static Camera viewCamera;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<LunarHealth>();
            health.Died += Die;
            Reach = attackRange;
            if (TryGetComponent(out CapsuleCollider body))
            {
                float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
                Reach = attackRange + Mathf.Max(0f, body.radius * scale - 0.4f);
                BodyHeight = body.height * transform.lossyScale.y;
            }
        }

        private void Start()
        {
            var player = FindObjectOfType<LunarPlayerCombat>();
            if (player != null) target = player.GetComponent<LunarHealth>();
            nextAttack = Time.time + 3f;
            nextAbility = Time.time + 2f;
            if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                agent.enabled = true;
                agent.Warp(hit.position);
            }
            else Debug.LogError("No hay suelo navegable para " + displayName, this);
        }

        private void Update()
        {
            if (health.IsDead || target == null || target.IsDead || !agent.enabled || !agent.isOnNavMesh) { Stop(); return; }
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
            if (Event.current.type != EventType.Repaint || health == null || health.IsDead || LunarPlayerRole.MenuOpen) return;
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

