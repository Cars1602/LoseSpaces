using UnityEngine;
using UnityEngine.AI;

namespace LunaCero
{
    /// <summary>
    /// Animacion de aliens con clips de correr y atacar (p. ej. Moustro Emjambre).
    /// Correr se acelera segun la velocidad del agente; atacar se dispara desde el golpe cuerpo a cuerpo.
    /// </summary>
    public sealed class LunarAlienAnimation : MonoBehaviour
    {
        public Animator animator;
        public string moveState = "Correr";
        public string attackState = "Atacar";
        public float attackDuration = 0.9f;
        [Tooltip("Estado de muerte (vacio si el modelo no tiene clip de muerte)")]
        public string deathState = "";
        public float deathDuration = 4f;
        public bool HasDeath => !string.IsNullOrEmpty(deathState) && animator != null && animator.HasState(0, Animator.StringToHash(deathState));
        [Tooltip("Velocidad del agente a la que el clip de correr se reproduce a velocidad normal")]
        public float referenceSpeed = 3.5f;
        [Tooltip("Evita que la cadera avance y salte hacia atras al repetir el ciclo")]
        public bool lockHipsInPlace = true;
        public Transform hips;

        private LunarEnemy enemy;
        private NavMeshAgent agent;
        private bool dead;
        private float attackUntil;
        private string current;
        private Vector3 hipsRest;

        private void Awake()
        {
            enemy = GetComponent<LunarEnemy>();
            agent = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.applyRootMotion = false;
            if (hips == null) hips = FindHips(animator.transform);
            if (hips != null) hipsRest = animator.transform.InverseTransformPoint(hips.position);
        }

        /// <summary>Multijugador: cuenta los ataques para repetirlos en las copias de los clientes.</summary>
        public int AttackCount { get; private set; }
        /// <summary>Copias de red: velocidad recibida del anfitrion (negativa = usar el agente).</summary>
        [System.NonSerialized] public float ExternalSpeed = -1f;

        public void Attack()
        {
            AttackCount++;
            if (dead || animator == null) return;
            attackUntil = Time.time + attackDuration;
            animator.speed = 1;
            animator.CrossFadeInFixedTime(attackState, 0.08f, 0, 0f);
            current = attackState;
        }

        public void Die()
        {
            dead = true;
            if (animator == null) return;
            if (HasDeath)
            {
                animator.speed = 1;
                animator.CrossFadeInFixedTime(deathState, 0.1f);
            }
            else animator.speed = 0; // sin clip de muerte: se congela y LunarEnemy lo tumba
        }

        private void Update()
        {
            if (animator == null || dead) return;
            if (Time.time < attackUntil) return;
            if (current != moveState)
            {
                animator.CrossFadeInFixedTime(moveState, 0.12f);
                current = moveState;
            }
            float speed = ExternalSpeed >= 0f ? ExternalSpeed : agent != null && agent.enabled && agent.isOnNavMesh ? agent.velocity.magnitude : 0f;
            animator.speed = speed > 0.2f ? Mathf.Clamp(speed / referenceSpeed, 0.6f, 1.6f) : enemy != null && enemy.IsBusy ? 1f : 0.15f;
        }

        private void LateUpdate()
        {
            if (!lockHipsInPlace || hips == null || animator == null || dead) return;
            Transform root = animator.transform;
            Vector3 local = root.InverseTransformPoint(hips.position);
            local.x = hipsRest.x;
            local.z = hipsRest.z;
            hips.position = root.TransformPoint(local);
        }

        private static Transform FindHips(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("hip") || n.Contains("pelvis")) return t;
            }
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.rootBone != null && skin.rootBone != root) return skin.rootBone;
            return null;
        }
    }
}
