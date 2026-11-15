using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace LunaCero.Editor
{
    public static class LunarCombatBuilder
    {
        public const string ScenePath = "Assets/LunaCero/Scenes/LunaCeroCombat.unity";
        private const string PlayerPath = "Assets/LunaCero/Prefabs/LunarPlayer.prefab";

        [MenuItem("Luna Cero/Crear arena de combate")]
        public static void Create()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Sal de Play para crear la arena.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ConfigurePlayer();
            var prototype = EditorSceneManager.OpenScene(LunaPrototypeBuilder.ScenePath);
            EditorSceneManager.SaveScene(prototype, ScenePath, true);
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var player = UnityEngine.Object.FindObjectOfType<LunarPlayerController>();
            player.transform.position = new Vector3(0, 0.15f, -13);
            var arena = new GameObject("Arena de combate");
            arena.AddComponent<LunarCombatArena>();
            var weak = EnemyPrefab("BotDebil", "Bot debil", false, 60, 2.8f, 10, 1.1f, false, 1, new Color(0.8f, 0.25f, 0.15f));
            Vector3[] positions = { new Vector3(0, 0, -5), new Vector3(-7, 0, -4), new Vector3(7, 0, -4), new Vector3(-11, 0, 7), new Vector3(11, 0, 7), new Vector3(-5, 0, 15), new Vector3(5, 0, 15), new Vector3(0, 0, 17) };
            for (int i = 0; i < positions.Length; i++) Spawn(weak, positions[i], arena.transform, "Bot debil " + (i + 1));
            var guardian = EnemyPrefab("JefeCustodio", "Custodio", true, 250, 2.1f, 22, 1.5f, false, 1, new Color(0.85f, 0.6f, 0.12f));
            var sentinel = EnemyPrefab("JefeCentinela", "Centinela", true, 220, 2f, 12, 1.6f, true, 1, new Color(0.55f, 0.25f, 0.85f));
            var predator = EnemyPrefab("JefeDepredador", "Depredador", true, 180, 4.2f, 14, 0.85f, false, 1, new Color(0.1f, 0.65f, 0.6f));
            var commander = EnemyPrefab("JefeComandante", "Comandante", true, 300, 2.3f, 8, 2f, true, 3, new Color(0.8f, 0.12f, 0.45f));
            Spawn(guardian, new Vector3(-14, 0, -1), arena.transform);
            Spawn(sentinel, new Vector3(14, 0, -1), arena.transform);
            Spawn(predator, new Vector3(-14, 0, 14), arena.transform);
            Spawn(commander, new Vector3(14, 0, 14), arena.transform);
            var medkit = PickupPrefab("Botiquin", LunarPickupKind.Medkit, 1, new Color(0.15f, 0.85f, 0.3f));
            var ammo = PickupPrefab("Municion", LunarPickupKind.Ammo, 36, new Color(0.15f, 0.6f, 0.9f));
            var crate = PickupPrefab("Caja transportable", LunarPickupKind.Carryable, 1, new Color(0.9f, 0.65f, 0.1f));
            for (int i = 0; i < 6; i++)
            {
                float x = i % 2 == 0 ? -10 : 10;
                float z = -10 + i / 2 * 11;
                Spawn(medkit, new Vector3(x, 0.4f, z), arena.transform);
                Spawn(ammo, new Vector3(x + 1.2f, 0.4f, z + 1), arena.transform);
            }
            Spawn(crate, new Vector3(-1.5f, 0.6f, -11), arena.transform);
            Spawn(crate, new Vector3(1.5f, 0.6f, -11), arena.transform);
            Spawn(ammo, new Vector3(0.8f, 0.4f, -11.5f), arena.transform);
            Spawn(medkit, new Vector3(-0.8f, 0.4f, -11.5f), arena.transform);
            Cover("Cobertura izquierda", new Vector3(-5, 1, -8), new Vector3(3, 2, 1));
            Cover("Cobertura derecha", new Vector3(5, 1, -8), new Vector3(3, 2, 1));
            Cover("Cobertura central", new Vector3(0, 1, 3), new Vector3(3, 2, 1));
            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Validate();
            Selection.activeGameObject = player.gameObject;
            SceneView.lastActiveSceneView?.LookAt(player.transform.position + Vector3.up, Quaternion.Euler(20, 25, 0), 7f);
        }

        public static void ConfigurePlayer()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                if (root.GetComponent<LunarHealth>() == null) root.AddComponent<LunarHealth>();
                var combat = root.GetComponent<LunarPlayerCombat>();
                if (combat == null) combat = root.AddComponent<LunarPlayerCombat>();
                Camera camera = root.GetComponentInChildren<Camera>();
                camera.name = "Camara tercera persona";
                camera.cullingMask = ~0;
                camera.fieldOfView = 65;
                camera.transform.localPosition = new Vector3(0.65f, 2.5f, -4);
                camera.transform.localRotation = Quaternion.Euler(12, 0, 0);
                Transform socket = root.transform.Find("Punto de carga");
                if (socket == null)
                {
                    socket = new GameObject("Punto de carga").transform;
                    socket.SetParent(root.transform, false);
                    socket.localPosition = new Vector3(-0.6f, 1.05f, 0.6f);
                }
                Transform gun = root.transform.Find("Arma provisional");
                if (gun == null)
                {
                    gun = VisualBox("Arma provisional", root.transform, new Vector3(0.45f, 1.15f, 0.6f), new Vector3(0.16f, 0.18f, 0.65f), MaterialAsset("Arma", new Color(0.08f, 0.15f, 0.2f))).transform;
                }
                Transform muzzle = gun.Find("Boca del arma");
                if (muzzle == null)
                {
                    muzzle = new GameObject("Boca del arma").transform;
                    muzzle.SetParent(gun, false);
                    muzzle.localPosition = new Vector3(0, 0, 0.55f);
                }
                var data = new SerializedObject(combat);
                data.FindProperty("aimCamera").objectReferenceValue = camera;
                data.FindProperty("muzzle").objectReferenceValue = muzzle;
                data.FindProperty("carrySocket").objectReferenceValue = socket;
                data.ApplyModifiedPropertiesWithoutUndo();
                var controller = new SerializedObject(root.GetComponent<LunarPlayerController>());
                controller.FindProperty("thirdPerson").boolValue = true;
                controller.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static GameObject EnemyPrefab(string fileName, string title, bool boss, float hp, float speed, float damage, float interval, bool ranged, int burst, Color color)
        {
            var root = new GameObject(title);
            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = speed;
            agent.acceleration = 14;
            agent.angularSpeed = 360;
            agent.radius = 0.5f;
            agent.height = 2;
            agent.stoppingDistance = ranged ? 5.5f : 1.3f;
            agent.enabled = false; // Start enables it after the arena has built its navigation.
            var collider = root.AddComponent<CapsuleCollider>();
            collider.height = boss ? 2.8f : 1.8f;
            collider.center = Vector3.up * collider.height * 0.5f;
            collider.radius = boss ? 0.6f : 0.4f;
            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            root.AddComponent<LunarHealth>().Configure(hp);
            var enemy = root.AddComponent<LunarEnemy>();
            enemy.displayName = title;
            enemy.isBoss = boss;
            enemy.attackDamage = damage;
            enemy.attackInterval = interval;
            enemy.attackRange = ranged ? 7f : (boss ? 2.1f : 1.7f);
            enemy.detectionRange = boss ? 15f : 11f;
            enemy.ranged = ranged;
            enemy.burstCount = burst;
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            float size = boss ? 1.4f : 1f;
            visual.transform.localScale = Vector3.one * size;
            var material = MaterialAsset(fileName, color);
            VisualBox("Cuerpo", visual.transform, Vector3.up * 0.8f, new Vector3(0.8f, 1.1f, 0.6f), material);
            VisualBox("Cabeza", visual.transform, Vector3.up * 1.55f, new Vector3(0.65f, 0.5f, 0.6f), material);
            VisualBox("Sensor", visual.transform, new Vector3(0, 1.58f, 0.32f), new Vector3(0.4f, 0.12f, 0.08f), MaterialAsset("Sensor", Color.white));
            VisualBox("Pie izquierdo", visual.transform, new Vector3(-0.22f, 0.2f, 0), new Vector3(0.3f, 0.4f, 0.5f), material);
            VisualBox("Pie derecho", visual.transform, new Vector3(0.22f, 0.2f, 0), new Vector3(0.3f, 0.4f, 0.5f), material);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/LunaCero/Prefabs/" + fileName + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject PickupPrefab(string title, LunarPickupKind kind, int quantity, Color color)
        {
            string path = "Assets/LunaCero/Prefabs/" + kind + ".prefab";
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = title;
            root.transform.localScale = Vector3.one * (kind == LunarPickupKind.Carryable ? 0.6f : 0.4f);
            root.GetComponent<Renderer>().sharedMaterial = MaterialAsset(kind.ToString(), color);
            var body = root.AddComponent<Rigidbody>();
            body.mass = 5;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var pickup = root.AddComponent<LunarPickup>();
            pickup.kind = kind;
            pickup.displayName = title;
            pickup.quantity = quantity;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject Spawn(GameObject prefab, Vector3 position, Transform parent, string name = null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(parent, true);
            instance.transform.position = position;
            if (name != null) instance.name = name;
            return instance;
        }

        private static void Cover(string title, Vector3 position, Vector3 size)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = title;
            cube.transform.position = position;
            cube.transform.localScale = size;
            cube.GetComponent<Renderer>().sharedMaterial = MaterialAsset("Cobertura", new Color(0.22f, 0.32f, 0.4f));
        }

        private static GameObject VisualBox(string title, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = title;
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        private static Material MaterialAsset(string name, Color color)
        {
            string path = "Assets/LunaCero/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
            material.color = color;
            return material;
        }

        [MenuItem("Luna Cero/Validar arena de combate")]
        public static void Validate()
        {
            LunaPrototypeBuilder.Validate();
            var player = UnityEngine.Object.FindObjectOfType<LunarPlayerCombat>();
            var enemies = UnityEngine.Object.FindObjectsOfType<LunarEnemy>();
            if (player == null || player.GetComponent<LunarHealth>() == null) throw new Exception("Falta el jugador de combate.");
            if (enemies.Count(enemy => enemy.isBoss) != 4 || enemies.Count(enemy => !enemy.isBoss) != 8) throw new Exception("Deben existir cuatro jefes y ocho bots debiles.");
            if (UnityEngine.Object.FindObjectsOfType<LunarPickup>().Count(item => item.kind == LunarPickupKind.Carryable) != 2) throw new Exception("Faltan objetos transportables.");
            if ((player.GetComponentInChildren<Camera>().cullingMask & (1 << 2)) == 0) throw new Exception("La camara debe mostrar el modelo en tercera persona.");
            Debug.Log("LUNA_COMBAT_STRUCTURE_OK: tercera persona, 4 colliders, 4 jefes, 8 bots, botiquines, municion y objetos transportables.");
        }
    }
}
