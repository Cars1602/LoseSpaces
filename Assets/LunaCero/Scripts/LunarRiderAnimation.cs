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
            // Se frena y arranca poco a poco (antes pasaba de 0 a 1 de golpe y parecia congelarse).
            bool moving = ExternalSpeed >= 0f ? ExternalSpeed > 0.2f // copia de red
                : enemy.IsBusy || (agent.enabled && agent.isOnNavMesh && agent.velocity.sqrMagnitude > 0.04f);
            float target = moving ? 1f : 0.12f;
            animator.speed = Mathf.MoveTowards(animator.speed, target, 4f * Time.deltaTime);
        }
        [System.NonSerialized] public float ExternalSpeed = -1f;
    }
}
