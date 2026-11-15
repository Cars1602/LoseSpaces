using System.Collections.Generic;
using UnityEngine;

namespace LunaCero
{
    /// <summary>
    /// Ajustes de rendimiento para el mapa grande (y para 2-4 jugadores):
    /// sombras a distancia corta, pocas luces por pixel, luces lejanas apagadas,
    /// 60 FPS como objetivo y limite de dibujo de la camara.
    /// Cada jugador solo dibuja su propia vista; lo que mas cuesta son las luces y las sombras.
    /// </summary>
    public sealed class LunarPerformance : MonoBehaviour
    {
        public int targetFrameRate = 60;
        public float shadowDistance = 55f;
        public int pixelLights = 4;
        [Tooltip("Las luces de puntos/focos mas alla de esta distancia se apagan")]
        public float lightCullDistance = 60f;
        public float rescanSeconds = 4f;

        private static bool applied;
        private readonly List<Light> lights = new List<Light>();
        private Camera view;
        private float nextScan, nextCull;

        private void Start()
        {
            view = GetComponentInChildren<Camera>();
            if (!applied)
            {
                applied = true;
                Application.targetFrameRate = targetFrameRate;
                QualitySettings.vSyncCount = 0;
                QualitySettings.shadowDistance = shadowDistance;
                QualitySettings.shadowCascades = 2;
                QualitySettings.pixelLightCount = pixelLights;
                QualitySettings.shadowResolution = ShadowResolution.Medium;
                QualitySettings.lodBias = 1f;
                QualitySettings.skinWeights = SkinWeights.TwoBones; // monstruos animados mas baratos
            }
            // Linterna con sombras duras (las suaves cuestan mucho mas).
            foreach (var light in GetComponentsInChildren<Light>(true))
                if (light.type == LightType.Spot && light.shadows == LightShadows.Soft) light.shadows = LightShadows.Hard;
            foreach (var terrain in Terrain.activeTerrains)
            {
                terrain.heightmapPixelError = 6;
                terrain.basemapDistance = 200;
                terrain.drawInstanced = true;
                terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private void Update()
        {
            if (view == null) { view = GetComponentInChildren<Camera>(); if (view == null) return; }
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + rescanSeconds;
                lights.Clear();
                culled.RemoveWhere(light => light == null);
                foreach (var light in FindObjectsOfType<Light>())
                    if (light.type != LightType.Directional && !light.transform.IsChildOf(transform)) lights.Add(light);
                foreach (var light in culled) if (!lights.Contains(light)) lights.Add(light);
            }
            if (Time.unscaledTime < nextCull) return;
            nextCull = Time.unscaledTime + 0.3f;
            Vector3 eye = view.transform.position;
            float limit = lightCullDistance * lightCullDistance;
            foreach (var light in lights)
            {
                if (light == null) continue;
                // Solo se tocan las luces encendidas o las que apago este componente (no las que apaga el juego).
                if (!light.enabled && !culled.Contains(light)) continue;
                float reach = lightCullDistance + light.range;
                bool near = (light.transform.position - eye).sqrMagnitude < Mathf.Max(limit, reach * reach * 0.5f);
                if (near && !light.enabled) { light.enabled = true; culled.Remove(light); }
                else if (!near && light.enabled) { light.enabled = false; culled.Add(light); }
            }
        }
        private readonly HashSet<Light> culled = new HashSet<Light>();
    }
}
