using UnityEngine;

namespace LunaCero
{
    public sealed class LunarHazardZone : MonoBehaviour
    {
        public bool smoke;
        public float radius = 4;
        public float duration = 8;
        private float expires;
        private float nextTick;
        private float exposure;
        private LunarPlayerCombat player;

        private void Start() { expires = Time.time + duration; player = FindObjectOfType<LunarPlayerCombat>(); }

        private void Update()
        {
            if (Time.time >= expires) { Destroy(gameObject); return; }
            if (player == null || player.GetComponent<LunarHealth>().IsDead) return;
            // Rigidbody.position is authoritative; the interpolated visual transform can lag behind an escape.
            Vector3 delta = player.GetComponent<Rigidbody>().position - transform.position;
            bool inside = Vector3.ProjectOnPlane(delta, Vector3.up).sqrMagnitude < radius * radius && Mathf.Abs(delta.y) < 3f;
            if (smoke)
            {
                if (inside) player.GetComponent<LunarPlayerStatus>()?.ApplySmoke(0.3f);
                return;
            }
            exposure = inside ? exposure + Time.deltaTime : 0;
            if (inside && Time.time >= nextTick)
            {
                nextTick = Time.time + 0.5f;
                player.GetComponent<LunarHealth>().Damage(Mathf.Min(10, 2 + exposure * 1.5f));
            }
        }

        public static LunarHazardZone Create(Vector3 position, bool smoke, float radius = 4f)
        {
            var root = new GameObject(smoke ? "Nube de humo" : "Charco de acido");
            root.transform.position = new Vector3(position.x, 0.04f, position.z);
            var hazard = root.AddComponent<LunarHazardZone>();
            hazard.smoke = smoke;
            hazard.radius = radius;
            hazard.duration = smoke ? 7 : 9;
            var visual = GameObject.CreatePrimitive(smoke ? PrimitiveType.Sphere : PrimitiveType.Cylinder);
            Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = smoke ? Vector3.up * 1.5f : Vector3.zero;
            visual.transform.localScale = new Vector3(radius * 2, smoke ? 4f : 0.035f, radius * 2);
            visual.GetComponent<Renderer>().sharedMaterial = LunarRuntimeMaterials.Get(smoke ? new Color(0.3f, 0.3f, 0.35f, 0.45f) : new Color(0.35f, 0.9f, 0.05f, 0.65f));
            return hazard;
        }
    }

    public static class LunarRuntimeMaterials
    {
        private static readonly System.Collections.Generic.Dictionary<Color, Material> cache = new System.Collections.Generic.Dictionary<Color, Material>();
        public static Material Get(Color color)
        {
            if (cache.TryGetValue(color, out var material) && material != null) return material;
            material = new Material(Shader.Find("Standard"));
            material.color = color;
            material.enableInstancing = true; // muchos objetos del mismo color = menos llamadas de dibujo
            if (color.a < 1)
            {
                material.SetFloat("_Mode", 3);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = 3000;
            }
            cache[color] = material;
            return material;
        }

        private static readonly System.Collections.Generic.Dictionary<Color, Material> glowing = new System.Collections.Generic.Dictionary<Color, Material>();
        /// <summary>Material que brilla un poco: los objetos recogibles se ven en la oscuridad.</summary>
        public static Material Glow(Color color, float intensity = 0.6f)
        {
            if (glowing.TryGetValue(color, out var material) && material != null) return material;
            material = new Material(Shader.Find("Standard")) { color = color, enableInstancing = true };
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", color * intensity);
            glowing[color] = material;
            return material;
        }
    }
}
