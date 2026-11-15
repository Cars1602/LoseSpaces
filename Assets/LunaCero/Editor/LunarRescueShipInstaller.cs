using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LunaCero.Editor
{
    [InitializeOnLoad]
    public static class LunarRescueShipInstaller
    {
        private const string Target = "Assets/LunaCero/Resources/LunarShip/Condor.prefab";
        static LunarRescueShipInstaller() { EditorApplication.delayCall += Once; }
        private static void Once()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating || EditorApplication.isCompiling)
            { EditorApplication.delayCall += Once; return; }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Target) == null) Install();
        }

        [MenuItem("Luna Cero/Instalar modelo de nave de rescate")]
        public static void Install()
        {
            string source = AssetDatabase.FindAssets("t:Model", new[] { "Assets/nave" })
                .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(source ?? "");
            if (model == null) { Debug.LogWarning("No se encontro el modelo de Assets/nave."); return; }
            if (!AssetDatabase.IsValidFolder("Assets/LunaCero/Resources/LunarShip"))
                AssetDatabase.CreateFolder("Assets/LunaCero/Resources", "LunarShip");
            var root = new GameObject("Condor");
            try
            {
                var shape = Object.Instantiate(model, root.transform, false);
                var renderers = shape.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) throw new System.InvalidOperationException("El modelo no contiene mallas visibles.");
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                shape.transform.localScale *= 10f / Mathf.Max(0.01f, Mathf.Max(bounds.size.x, bounds.size.z));
                bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                shape.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                const string materialPath = "Assets/LunaCero/Resources/LunarShip/Condor.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, materialPath); }
                var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/nave" }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
                string albedo = paths.FirstOrDefault(p => p.Contains("basecolor"));
                if (albedo != null) material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(albedo);
                material.color = Color.white;
                material.SetFloat("_Metallic", 0.25f);
                material.SetFloat("_Glossiness", 0.32f);
                foreach (var renderer in renderers)
                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                PrefabUtility.SaveAsPrefabAsset(root, Target);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
                Debug.Log("LUNA_CONDOR_OK: modelo real listo para holograma y despegue.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("Luna Cero/Previsualizar nave de rescate")]
        public static void Preview()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Vista previa Condor");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var visual = LunarShipVisual.Create(root.transform);
            var cameraObject = new GameObject("Camara de prueba", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.transform.position = new Vector3(-11, 8, -13);
            camera.transform.LookAt(new Vector3(0, 2, 0));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.035f, 0.06f);
            var lightObject = new GameObject("Luz de prueba", typeof(Light));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            var target = new RenderTexture(960, 640, 24);
            var previous = RenderTexture.active;
            try
            {
                if (visual == null) throw new System.InvalidOperationException("No se cargo el prefab Condor.");
                camera.targetTexture = target;
                foreach (bool solid in new[] { false, true })
                {
                    visual.SetProgress(solid ? 1f : 0f);
                    camera.Render(); RenderTexture.active = target;
                    var image = new Texture2D(960, 640, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); image.Apply();
                    System.IO.File.WriteAllBytes("Temp/Condor-" + (solid ? "solid" : "hologram") + ".png", image.EncodeToPNG());
                    Object.DestroyImmediate(image);
                }
                Debug.Log("LUNA_CONDOR_PREVIEW_OK: holograma y modelo solido renderizados en Temp.");
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                target.Release(); Object.DestroyImmediate(target);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
