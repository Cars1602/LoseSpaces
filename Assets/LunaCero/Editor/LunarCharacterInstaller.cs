using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LunaCero.Editor
{
    public static class LunarCharacterInstaller
    {
        private const string ModelPath = "Assets/correr/correr.fbx";
        private const string PrefabPath = "Assets/LunaCero/Prefabs/LunarPlayer.prefab";
        private const string ControllerPath = "Assets/LunaCero/Animations/Astronauta.controller";

        [MenuItem("Luna Cero/Usar personaje correr FBX")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Sal del modo Play antes de reemplazar el modelo.");
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("No se encontro correr.fbx.");
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            var clips = importer.clipAnimations;
            if (clips.Length == 0) clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = true;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var run = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview__"));
            if (run == null || run.length <= 0) throw new InvalidOperationException("El FBX no contiene una animacion utilizable.");
            if (!AssetDatabase.IsValidFolder("Assets/LunaCero/Animations"))
                AssetDatabase.CreateFolder("Assets/LunaCero", "Animations");
            var animationController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (animationController == null) animationController = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            animationController.parameters = new AnimatorControllerParameter[0];
            animationController.AddParameter("Speed", AnimatorControllerParameterType.Float);
            animationController.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            animationController.AddParameter("PlaybackSpeed", AnimatorControllerParameterType.Float);
            var machine = animationController.layers[0].stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            var idle = machine.AddState("Quieto provisional (pose fija)");
            idle.motion = run;
            idle.speed = 0f;
            machine.defaultState = idle;
            var running = machine.AddState("Correr");
            running.motion = run;
            running.speedParameter = "PlaybackSpeed";
            running.speedParameterActive = true;
            var toRun = idle.AddTransition(running);
            toRun.hasExitTime = false;
            toRun.duration = 0.12f;
            toRun.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");
            toRun.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            var stop = running.AddTransition(idle);
            stop.hasExitTime = false;
            stop.duration = 0.12f;
            stop.AddCondition(AnimatorConditionMode.Less, 0.15f, "Speed");
            var airborne = running.AddTransition(idle);
            airborne.hasExitTime = false;
            airborne.duration = 0.12f;
            airborne.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

            var player = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                foreach (Transform child in player.transform.Cast<Transform>().ToArray())
                    if (child.name.StartsWith("Visual provisional") || child.name == "Modelo astronauta")
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                var anchor = new GameObject("Modelo astronauta");
                anchor.transform.SetParent(player.transform, false);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                model.transform.SetParent(anchor.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException("El FBX no contiene mallas visibles.");
                Bounds bounds = new Bounds(anchor.transform.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
                foreach (var renderer in renderers)
                {
                    Bounds world = renderer.bounds;
                    for (int corner = 0; corner < 8; corner++)
                        bounds.Encapsulate(anchor.transform.InverseTransformPoint(world.center + Vector3.Scale(world.extents,
                            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
                }
                if (bounds.size.y < 0.0001f) throw new InvalidOperationException("Altura del modelo invalida.");
                float scale = 1.84f / bounds.size.y;
                anchor.transform.localScale = Vector3.one * scale;
                model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                foreach (Transform child in model.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = animationController;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var driver = player.GetComponent<LunarPlayerAnimation>();
                if (driver == null) driver = player.AddComponent<LunarPlayerAnimation>();
                var serialized = new SerializedObject(driver);
                serialized.FindProperty("animator").objectReferenceValue = animator;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player, PrefabPath);
                Debug.Log($"LUNA_CHARACTER_INSTALLED: clip={run.name}, duration={run.length:F2}s, originalBounds={bounds.size}, scale={scale:F4}, renderers={renderers.Length}");
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            AssetDatabase.SaveAssets();
            LunaPrototypeBuilder.Validate();
            LunarAnimationInstaller.Install();
            if (EditorSceneManager.GetActiveScene().path == LunaPrototypeBuilder.ScenePath)
            {
                // Keep an existing loose reference copy, but avoid a second astronaut in the level.
                foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == ModelPath)
                        root.SetActive(false);
                    if (root.GetComponent<LunarPlayerController>() != null)
                    {
                        SceneVisibilityManager.instance.Show(root, true);
                        Selection.activeGameObject = root;
                    }
                }
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            }
        }

        private static void VerifyAnimation()
        {
            var preview = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, preview);
                var animator = instance.GetComponentInChildren<Animator>();
                var bones = animator.GetComponentsInChildren<Transform>();
                animator.Rebind();
                animator.SetFloat("PlaybackSpeed", 1f);
                animator.SetFloat("Speed", 0f);
                animator.Play("Quieto provisional (pose fija)", 0, 0f);
                animator.Update(0f);
                var idle = bones.Select(bone => bone.localRotation).ToArray();
                animator.Update(0.3f);
                if (bones.Where((bone, i) => Quaternion.Angle(bone.localRotation, idle[i]) > 0.1f).Any())
                    throw new InvalidOperationException("La pose de reposo no permanece fija.");
                animator.SetFloat("Speed", 3.5f);
                animator.SetBool("Grounded", true);
                animator.Play("Correr", 0, 0f);
                animator.Update(0.15f);
                var pose = bones.Select(bone => bone.localRotation).ToArray();
                animator.Update(0.3f);
                int changed = bones.Where((bone, i) => Quaternion.Angle(bone.localRotation, pose[i]) > 0.1f).Count();
                if (changed == 0) throw new InvalidOperationException("La animacion de correr no mueve el esqueleto.");
                Debug.Log($"LUNA_ANIMATION_OK: reposo fijo, correr anima {changed} huesos, root motion desactivado.");
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
    }
}

