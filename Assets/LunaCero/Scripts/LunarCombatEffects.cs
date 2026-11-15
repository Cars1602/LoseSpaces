using UnityEngine;

namespace LunaCero
{
    public static class LunarCombatEffects
    {
        private static Material beamMaterial;
        public static void Beam(Vector3 from, Vector3 to, Color color)
        {
            if (beamMaterial == null) beamMaterial = new Material(Shader.Find("Sprites/Default"));
            var effect = new GameObject("Disparo");
            var line = effect.AddComponent<LineRenderer>();
            line.sharedMaterial = beamMaterial;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = 0.045f;
            line.endWidth = 0.015f;
            line.startColor = color;
            line.endColor = color;
            Object.Destroy(effect, 0.08f);
        }

        /// <summary>Esfera provisional que crece y desaparece, con un destello de luz.</summary>
        public static void Explosion(Vector3 position, float radius, Color color)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Explosion";
            Object.Destroy(sphere.GetComponent<Collider>());
            sphere.transform.position = position;
            sphere.GetComponent<Renderer>().sharedMaterial = LunarRuntimeMaterials.Get(new Color(color.r, color.g, color.b, 0.45f));
            var light = sphere.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = radius * 3f;
            light.intensity = 4f;
            sphere.AddComponent<LunarExplosionFade>().radius = radius;
        }
    }
}
