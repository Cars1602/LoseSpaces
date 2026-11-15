using UnityEngine;
using UnityEngine.AI;
namespace LunaCero
{
    public sealed class LunarRiderAnimation : MonoBehaviour
    {
        public Animator animator;
        public float deathDuration = 4;
        private LunarEnemy enemy;
        private NavMeshAgent agent;
        private bool dead;
        private void Awake() { enemy = GetComponent<LunarEnemy>(); agent = GetComponent<NavMeshAgent>(); }
        public void Die()
        {
            dead = true;
            if (animator == null) return;
            animator.speed = 1;
            animator.CrossFadeInFixedTime("Muerte", 0.1f);
        }
        private void Update()
        {
            if (animator == null || dead) return;
            // Only two clips supplied: stalk while moving/attacking, hold its pose at rest.
            if (ExternalSpeed >= 0f) { animator.speed = ExternalSpeed > 0.2f ? 1 : 0; return; } // copia de red
            animator.speed = enemy.IsBusy ? 1 : agent.enabled && agent.isOnNavMesh && agent.velocity.sqrMagnitude > 0.04f ? 1 : 0;
        }
        [System.NonSerialized] public float ExternalSpeed = -1f;
    }
}
