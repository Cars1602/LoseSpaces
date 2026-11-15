using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace LunaCero.Editor
{
    public static class LunarSurvivalBuilder
    {
        public const string ScenePath = "Assets/LunaCero/Scenes/LunaCeroSurvival.unity";
        public const string AlienFolder = "Assets/LunaCero/Prefabs/Aliens";

        [MenuItem("Luna Cero/Crear expedicion de supervivencia")]
        public static void Create()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Sal de Play antes de crear la expedicion.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/BotDebil.prefab") == null) throw new InvalidOperationException("Primero crea la arena de combate.");
            EnsureFolder(AlienFolder);
            EnsureFolder("Assets/LunaCero/World");
            LunarCombatBuilder.ConfigurePlayer();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.3f, 0.32f, 0.4f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.025f, 0.035f, 0.055f);
            RenderSettings.fogStartDistance = 45;
            RenderSettings.fogEndDistance = 92;
            var sun = new GameObject("Sol lunar").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.9f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(22, -30, 0);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/LunarPlayer.prefab"));
            player.name = "Astronauta";
            player.transform.position = new Vector3(0, 0.15f, -18);
            player.AddComponent<LunarPlayerStatus>();
            var combat = new SerializedObject(player.GetComponent<LunarPlayerCombat>());
            combat.FindProperty("stationHealingOnly").boolValue = true;
            combat.FindProperty("initialReserveAmmo").intValue = 0;
            combat.FindProperty("initialMedkits").intValue = 0;
            combat.ApplyModifiedPropertiesWithoutUndo();
            var camera = player.GetComponentInChildren<Camera>();
            camera.farClipPlane = 100;
            camera.backgroundColor = RenderSettings.fogColor;
            var worldRoot = new GameObject("Mundo circular continuo");
            var world = worldRoot.AddComponent<LunarEndlessWorld>();
            string meshPath = "Assets/LunaCero/World/SueloCircular.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null) { mesh = LunarEndlessWorld.CreateGroundMesh(); AssetDatabase.CreateAsset(mesh, meshPath); }
            var floor = new GameObject("Suelo circular generado");
            floor.transform.SetParent(worldRoot.transform, false);
            floor.AddComponent<MeshFilter>().sharedMesh = mesh;
            floor.AddComponent<MeshRenderer>().sharedMaterial = MaterialAsset("LunaSuelo", new Color(0.34f, 0.35f, 0.39f));
            floor.AddComponent<MeshCollider>().sharedMesh = mesh;
            world.ground = floor.transform;
            world.groundMesh = mesh;
            world.ammoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/Ammo.prefab");
            world.cratePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/Carryable.prefab");
            var director = new GameObject("Director de hordas").AddComponent<LunarHordeDirector>();
            director.commonPrefab = Alien(LunarAlienKind.Common, "Rastrero", 40, 3.5f, 6, new Vector3(0.9f, 0.9f, 0.9f), new Color(0.7f, 0.3f, 0.22f));
            director.specialPrefabs = new[]
            {
                Alien(LunarAlienKind.Bile, "Contaminador", 70, 2.2f, 8, new Vector3(1.5f, 1f, 1.4f), new Color(0.55f, 0.6f, 0.1f)),
                Alien(LunarAlienKind.Tongue, "Tentaculo", 200, 2.4f, 10, new Vector3(0.65f, 1.45f, 0.7f), new Color(0.65f, 0.25f, 0.7f)),
                Alien(LunarAlienKind.Pouncer, "Saltador", 100, 4.4f, 9, new Vector3(1f, 0.75f, 1f), new Color(0.2f, 0.35f, 0.75f)),
                Alien(LunarAlienKind.Dormant, "Durmiente", 250, 2f, 45, new Vector3(0.7f, 1.1f, 0.7f), new Color(0.85f, 0.8f, 0.85f)),
                Alien(LunarAlienKind.Brute, "Coloso", 500, 2.5f, 25, new Vector3(1.7f, 1.65f, 1.6f), new Color(0.55f, 0.25f, 0.18f)),
                Alien(LunarAlienKind.Charger, "Embestidor", 300, 3f, 16, new Vector3(1.3f, 1.25f, 1.3f), new Color(0.9f, 0.45f, 0.1f)),
                Alien(LunarAlienKind.Acid, "Escupidor", 90, 2.6f, 7, new Vector3(0.65f, 1.3f, 0.7f), new Color(0.15f, 0.8f, 0.3f)),
                Alien(LunarAlienKind.Rider, "Parasito", 90, 4f, 7, new Vector3(0.65f, 0.6f, 0.7f), new Color(0.25f, 0.75f, 0.85f))
            };
            if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Jockey/moustro asechador/asechando.fbx") != null) LunarRiderInstaller.Install();
            // Los aliens se copian del BotDebil y se tiñen: reinstala el Moustro Emjambre texturizado en los basicos.
            LunarModelInstaller.InstallAliens();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = player;
            SceneView.lastActiveSceneView?.LookAt(player.transform.position + Vector3.up, Quaternion.Euler(25, 25, 0), 10);
            Debug.Log("LUNA_SURVIVAL_CREATED: mundo circular continuo, reservas iniciales cero, estaciones medicas, hordas y ocho alienigenas especiales; cuatro tipos de jefe.");
        }

        private static GameObject Alien(LunarAlienKind kind, string title, float hp, float speed, float damage, Vector3 size, Color color)
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/LunaCero/Prefabs/BotDebil.prefab");
            try
            {
                root.name = title;
                var enemy = root.GetComponent<LunarEnemy>();
                enemy.displayName = title;
                enemy.alienKind = kind;
                enemy.relentless = true;
                enemy.ranged = false;
                enemy.attackRange = kind == LunarAlienKind.Brute ? 2.5f : 1.8f;
                enemy.attackDamage = damage;
                enemy.attackInterval = kind == LunarAlienKind.Dormant ? 1.5f : 1.2f;
                enemy.isBoss = Array.IndexOf(LunarModelInstaller.BossKinds, kind) >= 0; // Asechador, Gigante, Escupidor y Jockey
                root.GetComponent<LunarHealth>().Configure(hp);
                var agent = root.GetComponent<NavMeshAgent>();
                agent.enabled = false;
                agent.speed = speed;
                agent.stoppingDistance = kind == LunarAlienKind.Brute ? 2 : 1.25f;
                agent.radius = kind == LunarAlienKind.Brute ? 0.65f : 0.45f;
                var collider = root.GetComponent<CapsuleCollider>();
                collider.height = Mathf.Max(1, 1.8f * size.y);
                collider.center = Vector3.up * collider.height * 0.5f;
                collider.radius = Mathf.Min(0.75f, 0.4f * size.x);
                Transform visual = root.transform.Find("Visual");
                visual.localScale = size;
                var material = MaterialAsset("Alien_" + kind, color);
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                    if (renderer.name != "Sensor") renderer.sharedMaterial = material;
                if (kind == LunarAlienKind.Charger || kind == LunarAlienKind.Bile)
                {
                    var detail = GameObject.CreatePrimitive(kind == LunarAlienKind.Bile ? PrimitiveType.Sphere : PrimitiveType.Cube);
                    UnityEngine.Object.DestroyImmediate(detail.GetComponent<Collider>());
                    detail.name = kind == LunarAlienKind.Bile ? "Abdomen" : "Brazo robusto";
                    detail.transform.SetParent(visual, false);
                    detail.transform.localPosition = kind == LunarAlienKind.Bile ? new Vector3(0, 0.8f, 0.1f) : new Vector3(0.7f, 0.9f, 0);
                    detail.transform.localScale = kind == LunarAlienKind.Bile ? Vector3.one : new Vector3(0.65f, 1.2f, 0.7f);
                    detail.GetComponent<Renderer>().sharedMaterial = material;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, AlienFolder + "/" + kind + ".prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Material MaterialAsset(string name, Color color)
        {
            string path = "Assets/LunaCero/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
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

