using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LunaCero
{
    public static class LunarCombatEffects
    {
        /// <summary>Multijugador: avisa de cada efecto para enviarlo a los demas (0 = rayo, 1 = explosion).</summary>
        public static event Action<byte, Vector3, Vector3, Color, float> Emitted;
        /// <summary>Mientras es true no se avisa (efectos que llegan de la red).</summary>
        public static bool Muted;

        private static Material beamMaterial;
        public static void Beam(Vector3 from, Vector3 to, Color color)
        {
            if (!Muted) Emitted?.Invoke(0, from, to, color, 0f);
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
            if (!Muted) Emitted?.Invoke(1, position, position, color, radius);
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
