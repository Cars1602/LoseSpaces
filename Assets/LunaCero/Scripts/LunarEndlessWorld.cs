using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace LunaCero
{
    [DefaultExecutionOrder(-200)]
    public sealed class LunarEndlessWorld : MonoBehaviour
    {
        public Transform ground;
        public Mesh groundMesh;
        public GameObject ammoPrefab;
        public GameObject cratePrefab;
        public float chunkSize = 24;
        public float loadedRadius = 86;
        [Tooltip("Crea la nave de partida (armero, municion y oxigeno) alrededor del jugador")]
        public bool spawnShip = true;
        private Vector3 shipCenter;
        private readonly Dictionary<Vector2Int, GameObject> chunks = new Dictionary<Vector2Int, GameObject>();
        private readonly HashSet<string> collected = new HashSet<string>();
        private readonly Dictionary<string, LunarHealingStation.Supply> stationSupplies = new Dictionary<string, LunarHealingStation.Supply>();
        public LunarHealingStation.Supply StationSupply(string key)
        {
            if (!stationSupplies.TryGetValue(key, out var supply))
                stationSupplies.Add(key, supply = new LunarHealingStation.Supply());
            return supply;
        }
        private NavMeshData navigation;
        private NavMeshDataInstance navigationInstance;
        private AsyncOperation update;
        private Transform player;
        private Vector2Int center;
        public int LoadedChunks => chunks.Count;
        public bool NavigationReady => navigationInstance.valid && (update == null || update.isDone);

        private void Awake()
        {
            player = FindObjectOfType<LunarPlayerController>().transform;
            // La nave se crea antes de generar sectores y navegacion: sus paredes bloquean a los aliens.
            shipCenter = new Vector3(player.position.x, 0, player.position.z);
            if (spawnShip) LunarShip.Build(transform, shipCenter, FindObjectOfType<LunarPlayerCombat>()?.Arsenal);
            center = Cell(player.position);
            Stream(center);
            var settings = Settings();
            navigation = NavMeshBuilder.BuildNavMeshData(settings, Sources(), Bounds(), Vector3.zero, Quaternion.identity);
            if (navigation != null) navigationInstance = NavMesh.AddNavMeshData(navigation);
        }

        private void Update()
        {
            if (player == null) return;
            Vector2Int next = Cell(player.position);
            if (next == center || update != null && !update.isDone) return;
            center = next;
            Stream(center);
            if (navigation != null) update = NavMeshBuilder.UpdateNavMeshDataAsync(navigation, Settings(), Sources(), Bounds());
        }

        private Vector2Int Cell(Vector3 position) => new Vector2Int(Mathf.RoundToInt(position.x / chunkSize), Mathf.RoundToInt(position.z / chunkSize));
        public void MarkCollected(string key) { collected.Add(key); }
        public bool WasCollected(string key) => collected.Contains(key);

        private Bounds Bounds() => new Bounds(ground.position, new Vector3(240, 35, 240));
        private NavMeshBuildSettings Settings()
        {
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = 0.5f;
            settings.agentHeight = 2;
            return settings;
        }

        private List<NavMeshBuildSource> Sources()
        {
            var result = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = groundMesh, transform = ground.localToWorldMatrix, area = 0 }
            };
            foreach (var box in GetComponentsInChildren<BoxCollider>())
            {
                if (!box.enabled || box.isTrigger || box.attachedRigidbody != null || box.GetComponentInParent<LunarPickup>() != null) continue;
                result.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = box.size,
                    transform = box.transform.localToWorldMatrix * Matrix4x4.Translate(box.center), area = 0 });
            }
            return result;
        }

        private void Stream(Vector2Int origin)
        {
            ground.position = new Vector3(origin.x * chunkSize, 0, origin.y * chunkSize);
            var wanted = new HashSet<Vector2Int>();
            int reach = Mathf.CeilToInt(loadedRadius / chunkSize);
            for (int x = -reach; x <= reach; x++)
                for (int z = -reach; z <= reach; z++)
                {
                    if (new Vector2(x, z).magnitude * chunkSize > loadedRadius) continue;
                    var key = origin + new Vector2Int(x, z);
                    wanted.Add(key);
                    if (!chunks.ContainsKey(key)) chunks[key] = CreateChunk(key);
                }
            var removed = new List<Vector2Int>();
            foreach (var entry in chunks)
                if (!wanted.Contains(entry.Key))
                {
                    // Deactivate immediately so obsolete colliders do not enter the next navigation build.
                    entry.Value.SetActive(false);
                    Destroy(entry.Value);
                    removed.Add(entry.Key);
                }
            foreach (var key in removed) chunks.Remove(key);
        }

        private GameObject CreateChunk(Vector2Int cell)
        {
            var root = new GameObject($"Sector {cell.x},{cell.y}");
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(cell.x * chunkSize, 0, cell.y * chunkSize);
            var random = new System.Random(unchecked(cell.x * 73856093 ^ cell.y * 19349663 ^ 42137));
            bool station = cell.x % 3 == 0 && cell.y % 3 == 0;
            for (int i = 0; i < 2; i++)
            {
                Vector3 position = root.transform.position + new Vector3((float)random.NextDouble() * 20 - 10, 0, (float)random.NextDouble() * 20 - 10);
                if (station && Vector3.Distance(position, root.transform.position) < 7) continue;
                if (Mathf.Abs(position.x) < 4 && position.z < 3 && position.z > -25) continue;
                if (spawnShip && LunarShip.Footprint(shipCenter, position)) continue;
                float height = 1.2f + (float)random.NextDouble() * 2.5f;
                Block("Roca", root.transform, position + Vector3.up * height / 2, new Vector3(2 + (float)random.NextDouble() * 2, height, 2.5f), new Color(0.25f, 0.27f, 0.3f));
            }
            if (station) CreateStation(root.transform, cell);
            Vector3 supplyPosition = root.transform.position + new Vector3(5, 0.35f, -4);
            string supplyKey = $"{cell.x}:{cell.y}:ammo";
            Vector3 cratePosition = root.transform.position + new Vector3(-4, 0.7f, 5);
            if (!collected.Contains(supplyKey) && random.NextDouble() < 0.8 && !InShip(supplyPosition))
                Supply(ammoPrefab, root.transform, supplyPosition, supplyKey);
            if (random.NextDouble() < 0.35 && !InShip(cratePosition)) Supply(cratePrefab, root.transform, cratePosition, "");
            if (cell == new Vector2Int(0, -1) && !collected.Contains("inicio-ammo"))
                Supply(ammoPrefab, root.transform, new Vector3(0.8f, 0.35f, -16.5f), "inicio-ammo");
            return root;
        }

        private bool InShip(Vector3 position) => spawnShip && LunarShip.Footprint(shipCenter, position, 0.5f);

        private void Supply(GameObject prefab, Transform parent, Vector3 position, string key)
        {
            if (prefab == null) return;
            var item = Instantiate(prefab, position, Quaternion.identity, parent);
            var pickup = item.GetComponent<LunarPickup>();
            pickup.persistenceKey = key;
            if (pickup.kind == LunarPickupKind.Ammo) pickup.quantity = 18;
        }

        private void CreateStation(Transform parent, Vector2Int cell)
        {
            var station = new GameObject("Estacion medica");
            station.transform.SetParent(parent, false);
            var logic = station.AddComponent<LunarHealingStation>();
            logic.stationKey = $"{cell.x}:{cell.y}";
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(station.transform, false);
            ring.transform.localPosition = Vector3.up * 0.025f;
            ring.transform.localScale = new Vector3(6.8f, 0.025f, 6.8f);
            ring.GetComponent<Renderer>().sharedMaterial = LunarRuntimeMaterials.Get(new Color(0.05f, 0.7f, 0.5f, 0.5f));
            Vector3 center = station.transform.position;
            Block("Terminal medico", station.transform, center + new Vector3(0, 1.2f, 1.5f), new Vector3(1.2f, 2.4f, 0.8f), new Color(0.08f, 0.5f, 0.35f));
            Block("Cruz horizontal", station.transform, center + new Vector3(0, 2.7f, 1.5f), new Vector3(1.2f, 0.2f, 0.25f), Color.white, false);
            Block("Cruz vertical", station.transform, center + new Vector3(0, 2.7f, 1.5f), new Vector3(0.2f, 1.2f, 0.25f), Color.white, false);
            var light = station.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.1f, 1, 0.65f);
            light.range = 8;
            light.intensity = 2;
        }

        private static GameObject Block(string name, Transform parent, Vector3 position, Vector3 size, Color color, bool collider = true)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, true);
            box.transform.position = position;
            box.transform.localScale = size;
            if (!collider) Destroy(box.GetComponent<Collider>());
            box.GetComponent<Renderer>().sharedMaterial = LunarRuntimeMaterials.Get(color);
            return box;
        }

        public LunarHealingStation NearestStation(Vector3 position)
        {
            LunarHealingStation nearest = null;
            float distance = float.MaxValue;
            foreach (var station in GetComponentsInChildren<LunarHealingStation>())
            {
                if (station.Exhausted) continue;
                float candidate = (station.transform.position - position).sqrMagnitude;
                if (candidate < distance) { nearest = station; distance = candidate; }
            }
            return nearest;
        }

        private void OnDestroy()
        {
            if (navigationInstance.valid) navigationInstance.Remove();
            if (navigation != null) Destroy(navigation);
        }

        public static Mesh CreateGroundMesh(float radius = 118, int segments = 128)
        {
            var vertices = new Vector3[segments + 1];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                uv[i + 1] = new Vector2(vertices[i + 1].x, vertices[i + 1].z) * 0.1f;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % segments + 1;
                triangles[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "Suelo circular" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

