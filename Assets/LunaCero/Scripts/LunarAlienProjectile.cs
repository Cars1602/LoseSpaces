using System;
using UnityEngine;

namespace LunaCero
{
    public enum LunarProjectileKind { Bile, Acid, Rock }

    public sealed class LunarAlienProjectile : MonoBehaviour
    {
        /// <summary>Multijugador: el anfitrion reenvia cada lanzamiento (tipo, origen, velocidad) a los demas.</summary>
        public static event Action<LunarProjectileKind, Vector3, Vector3> Fired;
        private LunarEnemy owner;
        private LunarProjectileKind kind;
        private Vector3 velocity;
        private float lifetime;
        private bool visualOnly;

        public static void Fire(LunarEnemy source, LunarProjectileKind kind, Vector3 target)
        {
            Vector3 start = source.transform.position + Vector3.up * 1.4f;
            float seconds = Mathf.Clamp(Vector3.Distance(start, target) / 12f, 0.3f, 1.4f);
            Vector3 velocity = (target - start) / seconds + Vector3.up * 4.9f * seconds;
            var projectile = Create(kind, start, velocity, false);
            projectile.owner = source;
            Fired?.Invoke(kind, start, velocity);
        }

        /// <summary>Proyectil que solo se ve (en los clientes: el dano lo decide el anfitrion).</summary>
        public static void FireVisual(LunarProjectileKind kind, Vector3 start, Vector3 velocity) => Create(kind, start, velocity, true);

        private static LunarAlienProjectile Create(LunarProjectileKind kind, Vector3 start, Vector3 velocity, bool visual)
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ball.GetComponent<Collider>());
            ball.name = kind + " proyectil";
            ball.transform.position = start;
            ball.transform.localScale = Vector3.one * (kind == LunarProjectileKind.Rock ? 0.7f : 0.28f);
            ball.GetComponent<Renderer>().sharedMaterial = LunarRuntimeMaterials.Get(kind == LunarProjectileKind.Rock ? Color.gray : kind == LunarProjectileKind.Bile ? new Color(0.6f, 0.5f, 0.05f) : Color.green);
            var projectile = ball.AddComponent<LunarAlienProjectile>();
            projectile.kind = kind;
            projectile.velocity = velocity;
            projectile.lifetime = 3f;
            projectile.visualOnly = visual;
            return projectile;
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
                if (hit.collider.GetComponentInParent<LunarEnemy>() != null) continue; // los aliens no se tapan entre si
                if (!first.HasValue || hit.distance < first.Value.distance) first = hit;
            }
            if (first.HasValue)
            {
                var hit = first.Value;
                // Cualquier tripulante (local o remoto) recibe el impacto.
                var crew = hit.collider.GetComponentInParent<LunarHealth>();
                if (!visualOnly && crew != null && LunarCrew.IsCrew(crew))
                {
                    if (crew.TryGetComponent(out LunarPlayerStatus status))
                    {
                        if (kind == LunarProjectileKind.Bile) status.ApplyBile(10);
                        if (kind == LunarProjectileKind.Rock) status.Knockback(velocity.normalized * 6 + Vector3.up * 2);
                    }
                    crew.Damage(kind == LunarProjectileKind.Rock ? 20 : 4);
                }
                if (!visualOnly && kind == LunarProjectileKind.Acid) LunarHazardZone.Create(hit.point, false);
                Destroy(gameObject);
                return;
            }
            transform.position += delta;
            lifetime -= step;
            if (lifetime <= 0 || transform.position.y < -40) Destroy(gameObject);
        }
    }
}
