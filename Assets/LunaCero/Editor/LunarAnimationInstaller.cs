using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LunaCero.Editor
{
    [InitializeOnLoad]
    public static class LunarAnimationInstaller
    {
        private const string ControllerPath = "Assets/LunaCero/Animations/Astronauta.controller";
        private const string Version = "LunaCero.AnimationPack.v2.Emote";
        static LunarAnimationInstaller() { EditorApplication.delayCall += AutoInstall; }
        private static void AutoInstall()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += AutoInstall; return; }
            if (EditorPrefs.GetBool(Application.dataPath + Version, false)) return;
            Install();
            EditorPrefs.SetBool(Application.dataPath + Version, true);
        }
        [MenuItem("Luna Cero/Conectar animaciones (con emote)")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Sal de Play primero.");
            string[] names = { "Reposo", "Caminar", "Correr", "Saltar", "Disparar", "Muerte", "Cavar", "Emote" };
            var clips = names.Select(name => Import(name)).ToArray();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var transition in machine.anyStateTransitions) machine.RemoveAnyStateTransition(transition);
            foreach (var state in machine.states) machine.RemoveState(state.state);
            controller.parameters = new AnimatorControllerParameter[0];
            for (int i = 0; i < names.Length; i++)
            {
                var state = machine.AddState(names[i], new Vector3(250 * (i % 3), 90 * (i / 3)));
                state.motion = clips[i];
                if (i == 0) machine.defaultState = state;
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Verify(controller, names);
            Debug.Log("LUNA_ANIMATION_PACK_OK: reposo, caminar, correr, saltar, disparar, muerte, cavar y emote conectados.");
        }
        private static AnimationClip Import(string name)
        {
            string path = "Assets/correr/" + name.ToLowerInvariant() + ".fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Falta " + path);
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            var clips = importer.clipAnimations;
            if (clips.Length == 0) clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                if (name == "Emote") clip.name = name;
                clip.loopTime = name != "Muerte" && name != "Saltar" && name != "Emote";
                clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var animation = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (animation == null || animation.length <= 0) throw new InvalidOperationException("Clip vacio: " + name);
            return animation;
        }
        private static void Verify(AnimatorController controller, string[] names)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/LunarPlayer.prefab"));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, scene);
                var animator = instance.GetComponentInChildren<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                var bones = animator.GetComponentsInChildren<Transform>();
                foreach (var name in names)
                {
                    animator.Rebind();
                    animator.Play(name, 0, 0.1f);
                    animator.Update(0);
                    var pose = bones.Select(b => b.localRotation).ToArray();
                    animator.Update(animator.GetCurrentAnimatorStateInfo(0).length * 0.35f);
                    int changed = bones.Where((b, i) => Quaternion.Angle(b.localRotation, pose[i]) > 0.01f).Count();
                    if (changed == 0) throw new InvalidOperationException("El clip no anima los huesos del personaje: " + name);
                    Debug.Log("LUNA_CLIP_OK: " + name + " bones=" + changed);
                }
                if (instance.GetComponentsInChildren<Collider>().Length != 4) throw new InvalidOperationException("Se alteraron los 4 colisionadores.");
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
