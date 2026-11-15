using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LunaCero.Editor
{
    public static class LunaPrototypeBuilder
    {
        public const string ScenePath = "Assets/LunaCero/Scenes/LunaCeroPrototype.unity";
        private const string PrefabPath = "Assets/LunaCero/Prefabs/LunarPlayer.prefab";

        [MenuItem("Luna Cero/Crear escena de prueba")]
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder("Assets/LunaCero/Scenes");
            EnsureFolder("Assets/LunaCero/Prefabs");
            EnsureFolder("Assets/LunaCero/Materials");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.26f, 0.29f, 0.35f);
            var light = new GameObject("Sol provisional").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(18f, -35f, 0f);
            Material ground = MaterialAsset("Suelo", new Color(0.36f, 0.37f, 0.4f));
            Material obstacle = MaterialAsset("Obstaculos", new Color(0.6f, 0.35f, 0.15f));
            Material suit = MaterialAsset("Traje", new Color(0.8f, 0.85f, 0.9f));
            Box("Suelo", new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), ground);
            Box("Muro norte", new Vector3(0, 1.5f, 20), new Vector3(41, 3, 1), ground);
            Box("Muro sur", new Vector3(0, 1.5f, -20), new Vector3(41, 3, 1), ground);
            Box("Muro este", new Vector3(20, 1.5f, 0), new Vector3(1, 3, 40), ground);
            Box("Muro oeste", new Vector3(-20, 1.5f, 0), new Vector3(1, 3, 40), ground);
            Box("Obstaculo bajo", new Vector3(-2, 0.3f, 4), new Vector3(2, 0.6f, 2), obstacle);
            Box("Obstaculo alto", new Vector3(2, 0.65f, 7), new Vector3(2, 1.3f, 2), obstacle);
            Box("Pared de colision", new Vector3(0, 1.5f, 11), new Vector3(7, 3, 1), obstacle);
            var ramp = Box("Rampa", new Vector3(-7, 0.6f, 6), new Vector3(3, 0.4f, 5), obstacle);
            ramp.transform.rotation = Quaternion.Euler(-15f, 0, 0);

            var player = new GameObject("Astronauta provisional (4 colliders)");
            player.tag = "Player";
            player.transform.position = new Vector3(0, 0.15f, -5);
            var body = player.AddComponent<Rigidbody>();
            body.mass = 80f;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Four solid boxes form a single compound rigidbody. Visuals can be replaced independently.
            ColliderBox(player, "Cabeza", new Vector3(0, 1.64f, 0), new Vector3(0.5f, 0.4f, 0.5f));
            ColliderBox(player, "Torso", new Vector3(0, 1.08f, 0), new Vector3(0.7f, 0.72f, 0.5f));
            ColliderBox(player, "Pierna izquierda", new Vector3(-0.18f, 0.36f, 0), new Vector3(0.32f, 0.72f, 0.48f));
            ColliderBox(player, "Pierna derecha", new Vector3(0.18f, 0.36f, 0), new Vector3(0.32f, 0.72f, 0.48f));
            var visuals = new GameObject("Visual provisional - reemplazar por modelo");
            visuals.transform.SetParent(player.transform, false);
            foreach (BoxCollider collider in player.GetComponentsInChildren<BoxCollider>())
            {
                var visual = Box(collider.name + " visual", Vector3.zero, collider.size, suit);
                UnityEngine.Object.DestroyImmediate(visual.GetComponent<BoxCollider>());
                visual.transform.SetParent(visuals.transform, false);
                visual.transform.localPosition = collider.transform.localPosition;
                visual.layer = 2; // Ignore Raycast: visible in Scene view, hidden only from the player camera.
            }
            var cameraObject = new GameObject("Camara primera persona");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(player.transform, false);
            cameraObject.transform.localPosition = new Vector3(0, 1.65f, 0.12f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = 75f;
            camera.cullingMask = ~(1 << 2);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.015f, 0.02f, 0.04f);
            cameraObject.AddComponent<AudioListener>();
            var controller = player.AddComponent<LunarPlayerController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("view").objectReferenceValue = camera.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAssetAndConnect(player, PrefabPath, InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = player;
            Validate();
            if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/correr/correr.fbx") != null)
                LunarCharacterInstaller.Install();
        }

        public static void Validate()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new Exception("Falta prefab del astronauta.");
            Collider[] colliders = prefab.GetComponentsInChildren<Collider>();
            if (colliders.Length != 4) throw new Exception("El astronauta debe tener exactamente 4 colliders.");
            foreach (Collider collider in colliders)
                if (!(collider is BoxCollider) || collider.isTrigger) throw new Exception("Los cuatro colliders deben ser cajas solidas.");
            if (prefab.GetComponentsInChildren<Rigidbody>().Length != 1) throw new Exception("Debe existir un solo Rigidbody.");
            if (prefab.GetComponent<LunarPlayerController>() == null || prefab.GetComponentInChildren<Camera>() == null)
                throw new Exception("Falta controlador o camara.");
            Debug.Log("LUNA_CERO_VALIDATION_OK: prefab con 4 BoxCollider, un Rigidbody, controlador y camara.");
        }

        private static void ColliderBox(GameObject player, string name, Vector3 center, Vector3 size)
        {
            var child = new GameObject(name);
            child.transform.SetParent(player.transform, false);
            child.transform.localPosition = center;
            child.AddComponent<BoxCollider>().size = size;
        }

        private static GameObject Box(string name, Vector3 center, Vector3 size, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.position = center;
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            return box;
        }

        private static Material MaterialAsset(string name, Color color)
        {
            string path = "Assets/LunaCero/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int split = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }
    }
}
