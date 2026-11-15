using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace LunaCero.Editor
{
    [InitializeOnLoad]
    public static class LunarArsenalModelInstaller
    {
        const string Output = "Assets/LunaCero/Resources/LunarWeapons";
        static LunarArsenalModelInstaller() { EditorApplication.delayCall += Once; }
        static void Once()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating) { EditorApplication.delayCall += Once; return; }
            if (EditorPrefs.GetBool(Application.dataPath + "ArsenalModels.v1", false)) return;
            try { Install(); EditorPrefs.SetBool(Application.dataPath + "ArsenalModels.v1", true); }
            catch (Exception e) { Debug.LogError("LUNA_ARSENAL_MODELS_FAILED: " + e); }
        }
        [MenuItem("Luna Cero/Instalar modelos de armas")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new Exception("Sal de Play primero");
            Ensure(Output);
            Build("Pistola", "pistola", 0.45f);
            Build("Escopeta", "escopeta", 0.85f);
            Build("Rifle", "rifle", 0.95f);
            Build("Lanzador de plasma", "plasma", 0.8f);
            // No separate rail rifle asset supplied: reuse the rifle mesh at its longer gameplay scale.
            Build("Rifle", "riel", 1.25f);
            var player = PrefabUtility.LoadPrefabContents("Assets/LunaCero/Prefabs/LunarPlayer.prefab");
            try
            {
                var data = new SerializedObject(player.GetComponent<LunarPlayerCombat>());
                data.FindProperty("pistolModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Output + "/pistola.prefab");
                var arsenal = data.FindProperty("arsenal");
                for (int i=0;i<arsenal.arraySize;i++)
                {
                    var entry = arsenal.GetArrayElementAtIndex(i);
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(Output + "/" + entry.FindPropertyRelative("id").stringValue + ".prefab");
                    if (model == null) continue;
                    entry.FindPropertyRelative("model").objectReferenceValue = model;
                    entry.FindPropertyRelative("pickupModel").objectReferenceValue = model;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player,"Assets/LunaCero/Prefabs/LunarPlayer.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            AssetDatabase.SaveAssets();
            Debug.Log("LUNA_ARSENAL_MODELS_OK: pistola, escopeta, rifle, plasma y riel con modelos texturizados, sin colision extra.");
        }
        /// <param name="flip">Gira 180 grados el eje largo (la llave inglesa venia con la cabeza en el mango).</param>
        public static void Build(string name, string id, float length, string sourceFolder = null, bool flip = false)
        {
            string folder = sourceFolder ?? ("Assets/Armas/" + name);
            string path = sourceFolder == null ? folder + "/" + name + ".fbx" : AssetDatabase.FindAssets("t:Model",new[]{folder}).Select(AssetDatabase.GUIDToAssetPath).First(p=>p.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase));
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new Exception("Falta " + path);
            importer.importCameras = importer.importLights = importer.addCollider = false;
            importer.SaveAndReimport();
            var root = new GameObject(name);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                model.transform.SetParent(root.transform,false);
                foreach (var c in model.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
                foreach (var a in model.GetComponentsInChildren<Animator>()) UnityEngine.Object.DestroyImmediate(a);
                var renderers = model.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) throw new Exception("Sin malla: " + name);
                var bounds = Bounds(root.transform,renderers);
                // Estos FBX usan +X como largo del arma y +Z como parte superior.
                // En el juego el canon debe apuntar a +Z y la parte superior quedar en +Y.
                if (sourceFolder != null && bounds.size.y > Mathf.Max(bounds.size.x,bounds.size.z)) model.transform.localRotation=Quaternion.Euler(flip?-90:90,0,0);
                else if (bounds.size.x > bounds.size.z)
                    model.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.right);
                bounds = Bounds(root.transform,renderers);
                model.transform.localScale *= length / Mathf.Max(0.001f, sourceFolder != null ? Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z) : bounds.size.z);
                bounds = Bounds(root.transform,renderers);
                model.transform.localPosition -= bounds.center;
                var texture = AssetDatabase.FindAssets("t:Texture2D",new[]{folder}).Select(AssetDatabase.GUIDToAssetPath).First(p=>p.Contains("basecolor"));
                string materialPath = "Assets/LunaCero/Materials/Weapon_" + id + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat,materialPath); }
                mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texture); mat.color = Color.white; mat.SetFloat("_Glossiness",0.25f);
                foreach (var r in renderers) r.sharedMaterials = r.sharedMaterials.Select(_=>mat).ToArray();
                EditorUtility.SetDirty(mat);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root,Output + "/" + id + ".prefab");
                if (prefab.GetComponentsInChildren<Collider>().Length != 0) throw new Exception("Colision extra: " + id);
                Debug.Log("LUNA_WEAPON_MODEL_OK: " + id + " bounds=" + Bounds(root.transform,renderers).size);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static Bounds Bounds(Transform root,Renderer[] renderers)
        {
            var bounds = new Bounds(root.InverseTransformPoint(renderers[0].bounds.center),Vector3.zero);
            foreach(var r in renderers) for(int i=0;i<8;i++) bounds.Encapsulate(root.InverseTransformPoint(r.bounds.center + Vector3.Scale(r.bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
            return bounds;
        }
        static void Ensure(string path) { if(AssetDatabase.IsValidFolder(path)) return; int i=path.LastIndexOf('/'); Ensure(path.Substring(0,i)); AssetDatabase.CreateFolder(path.Substring(0,i),path.Substring(i+1)); }
    }
}
