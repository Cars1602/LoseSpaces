using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LunaCero.Editor
{
    [InitializeOnLoad]
    public static class LunarRiderInstaller
    {
        const string Folder = "Assets/Jockey/moustro asechador/";
        const string Prefab = "Assets/LunaCero/Prefabs/Aliens/Rider.prefab";
        const string Controller = "Assets/LunaCero/Animations/Jockey.controller";
        static LunarRiderInstaller() { EditorApplication.delayCall += Once; }
        static void Once()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating) { EditorApplication.delayCall += Once; return; }
            if (EditorPrefs.GetBool(Application.dataPath + "JockeyModel.v1", false)) return;
            Install();
            EditorPrefs.SetBool(Application.dataPath + "JockeyModel.v1", true);
        }
        [MenuItem("Luna Cero/Instalar modelo Jockey")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Sal de Play primero.");
            var stalk = Clip("asechando", true);
            var death = Clip("MuerteASE", false);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(Controller);
            var machine = controller.layers[0].stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            var moving = machine.AddState("Acechar"); moving.motion = stalk; machine.defaultState = moving;
            machine.AddState("Muerte").motion = death;
            EditorUtility.SetDirty(controller);
            var root = PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                var old = root.transform.Find("Visual");
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                var anchor = new GameObject("Visual").transform; anchor.SetParent(root.transform, false);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "asechando.fbx"));
                model.transform.SetParent(anchor, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                var renderers = model.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) throw new Exception("FBX sin malla");
                Bounds bounds = new Bounds(anchor.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
                foreach (var renderer in renderers)
                    for (int i = 0; i < 8; i++) bounds.Encapsulate(anchor.InverseTransformPoint(renderer.bounds.center + Vector3.Scale(renderer.bounds.extents, new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
                anchor.localScale = Vector3.one * (1.08f / bounds.size.y);
                model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                foreach (var collider in model.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var driver = root.GetComponent<LunarRiderAnimation>() ?? root.AddComponent<LunarRiderAnimation>();
                driver.animator = animator; driver.deathDuration = death.length + 0.5f;
                string materialPath = "Assets/LunaCero/Materials/Jockey.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, materialPath); }
                var texturePath = AssetDatabase.FindAssets("alien_stalker_creature_3d_model_basecolor t:Texture2D", new[]{"Assets/Jockey"}).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
                if (texturePath == null) throw new Exception("Falta textura Jockey");
                material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                material.color = Color.white;
                material.SetFloat("_Glossiness", 0.2f);
                foreach (var renderer in renderers) renderer.sharedMaterials = renderer.sharedMaterials.Select(m => material).ToArray();
                EditorUtility.SetDirty(material);
                var bones = animator.GetComponentsInChildren<Transform>();
                foreach (string state in new[]{"Acechar", "Muerte"})
                {
                    animator.Rebind(); animator.Play(state, 0, 0.05f); animator.Update(0);
                    var pose = bones.Select(b => b.localRotation).ToArray();
                    animator.Update(animator.GetCurrentAnimatorStateInfo(0).length * 0.35f);
                    int changed = bones.Where((b,i)=>Quaternion.Angle(b.localRotation,pose[i]) > 0.01f).Count();
                    if (changed == 0) throw new Exception("No anima: " + state);
                    Debug.Log("LUNA_JOCKEY_CLIP_OK: " + state + " bones=" + changed);
                }
                // Restore the imported rest pose instead of serializing a sampled death pose.
                animator.Rebind(); animator.Play("Acechar", 0, 0); animator.Update(0);
                if (root.GetComponentsInChildren<Collider>().Length != 1 || root.GetComponent<LunarEnemy>().alienKind != LunarAlienKind.Rider) throw new Exception("Jockey logic/collider changed");
                PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("LUNA_JOCKEY_MODEL_OK: modelo texturizado, acechar y muerte verificados; IA y colision conservadas.");
        }
        static AnimationClip Clip(string name, bool loop)
        {
            string path = Folder + name + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.importAnimation = true; importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = loop;
                clip.lockRootHeightY = clip.lockRootPositionXZ = clip.lockRootRotation = true;
                clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips; importer.SaveAndReimport();
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
        }
    }
}
