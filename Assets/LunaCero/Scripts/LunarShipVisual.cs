using UnityEngine;
using UnityEngine.Rendering;

namespace LunaCero
{
    /// <summary>Modelo real de la Condor: holograma durante la reparacion y nave solida al despegar.</summary>
    public sealed class LunarShipVisual : MonoBehaviour
    {
        private Renderer[] surfaces;
        private Material[][] original;
        private Material hologram;
        private float shown = -1f;

        public static LunarShipVisual Create(Transform parent)
        {
            var prefab = Resources.Load<GameObject>("LunarShip/Condor");
            if (prefab == null) return null;
            var root = new GameObject("Condor - nave de rescate");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.up * 0.15f;
            Instantiate(prefab, root.transform, false);
            foreach (var collider in root.GetComponentsInChildren<Collider>()) Destroy(collider);
            var visual = root.AddComponent<LunarShipVisual>();
            visual.surfaces = root.GetComponentsInChildren<Renderer>();
            visual.original = new Material[visual.surfaces.Length][];
            for (int i = 0; i < visual.surfaces.Length; i++) visual.original[i] = visual.surfaces[i].sharedMaterials;
            visual.hologram = new Material(Shader.Find("Standard")) { name = "Holograma Condor", hideFlags = HideFlags.DontSave };
            visual.hologram.SetFloat("_Mode", 2);
            visual.hologram.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            visual.hologram.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            visual.hologram.SetInt("_ZWrite", 0);
            visual.hologram.EnableKeyword("_ALPHABLEND_ON");
            visual.hologram.EnableKeyword("_EMISSION");
            visual.hologram.SetColor("_EmissionColor", new Color(0.06f, 0.3f, 0.4f));
            visual.hologram.renderQueue = 3000;
            visual.SetProgress(0f);
            return visual;
        }

        public void SetProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (Mathf.Approximately(shown, progress)) return;
            shown = progress;
            hologram.color = new Color(0.2f, 0.75f, 1f, Mathf.Lerp(0.12f, 0.28f, progress));
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (progress >= 1f) surfaces[i].sharedMaterials = original[i];
                else
                {
                    var materials = new Material[original[i].Length];
                    for (int j = 0; j < materials.Length; j++) materials[j] = hologram;
                    surfaces[i].sharedMaterials = materials;
                }
                surfaces[i].shadowCastingMode = progress >= 1f ? ShadowCastingMode.On : ShadowCastingMode.Off;
                surfaces[i].receiveShadows = progress >= 1f;
            }
        }
        private void OnDestroy()
        {
            if (hologram == null) return;
            if (Application.isPlaying) Destroy(hologram); else DestroyImmediate(hologram);
        }
    }
}
