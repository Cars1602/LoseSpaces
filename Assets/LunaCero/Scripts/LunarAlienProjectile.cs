using UnityEngine;

namespace LunaCero
{
    public enum LunarProjectileKind { Bile, Acid, Rock }

    public sealed class LunarAlienProjectile : MonoBehaviour
    {
        private LunarEnemy owner;
        private LunarProjectileKind kind;
        private Vector3 velocity;
        private float lifetime;

        public static void Fire(LunarEnemy source, LunarProjectileKind kind, Vector3 target)
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ball.GetComponent<Collider>());
            ball.name = kind + " proyectil";
            ball.transform.position = source.transform.position + Vector3.up * 1.4f;
            ball.transform.localScale = Vector3.one * (kind == LunarProjectileKind.Rock ? 0.7f : 0.28f);
            ball.GetComponent<Renderer>().sharedMaterial = LunarRuntimeMaterials.Get(kind == LunarProjectileKind.Rock ? Color.gray : kind == LunarProjectileKind.Bile ? new Color(0.6f, 0.5f, 0.05f) : Color.green);
            var projectile = ball.AddComponent<LunarAlienProjectile>();
            projectile.owner = source;
            projectile.kind = kind;
            float seconds = Mathf.Clamp(Vector3.Distance(ball.transform.position, target) / 12f, 0.3f, 1.4f);
            projectile.velocity = (target - ball.transform.position) / seconds + Vector3.up * 4.9f * seconds;
            projectile.lifetime = 3f;
        }

        private void Update()
        {
            float step = Time.deltaTime;
            Vector3 delta = velocity * step + Vector3.down * 4.9f * step * step;
            velocity += Vector3.down * 9.8f * step;
            RaycastHit? first = null;
            foreach (var hit in Physics.SphereCastAll(transform.position, kind == LunarProjectileKind.Rock ? 0.32f : 0.12f, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (owner != null && hit.collider.transform.IsChildOf(owner.transform)) continue;
                if (!first.HasValue || hit.distance < first.Value.distance) first = hit;
            }
            if (first.HasValue)
            {
                var hit = first.Value;
                var player = hit.collider.GetComponentInParent<LunarPlayerCombat>();
                if (player != null)
                {
                    if (kind == LunarProjectileKind.Bile) player.GetComponent<LunarPlayerStatus>()?.ApplyBile(10);
                    player.GetComponent<LunarHealth>().Damage(kind == LunarProjectileKind.Rock ? 20 : 4);
                    if (kind == LunarProjectileKind.Rock) player.GetComponent<LunarPlayerStatus>()?.Knockback(velocity.normalized * 6 + Vector3.up * 2);
                }
                if (kind == LunarProjectileKind.Acid) LunarHazardZone.Create(hit.point, false);
                Destroy(gameObject);
                return;
            }
            transform.position += delta;
            lifetime -= step;
            if (lifetime <= 0 || transform.position.y < -2) Destroy(gameObject);
        }
    }
}
