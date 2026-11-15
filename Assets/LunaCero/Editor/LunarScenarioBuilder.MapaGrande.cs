using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace LunaCero.Editor
{
    /// <summary>
    /// Mapa grande (Assets/esenario2/esenario/MapaGrande.unity): 400 x 400 m alrededor de la base original.
    /// Esa escena usa un terreno, prefabs y scripts que no estan en este proyecto, asi que se reconstruye
    /// a partir de sus posiciones (Assets/LunaCero/Data/MapaGrande.json): terreno con crateres, 450 rocas,
    /// rastros de escombros de la nave, hielo y nidos en crateres en sombra, piezas lejos de la base,
    /// armas y objetos contundentes repartidos, y navegacion horneada (carga al instante).
    /// </summary>
    public static partial class LunarScenarioBuilder
    {
        private const string BigVersion = "LunaCero.MapaGrande.v1";
        public const string BigScenePath = "Assets/LunaCero/Scenes/LunaCeroMapaGrande.unity";
        private const string BigData = "Assets/LunaCero/Data/MapaGrande.json";
        private const string BigFolder = "Assets/LunaCero/Scenes/MapaGrande";
        private const string Props = "Assets/esenario/props/";
        private const string NaveRotaFbx = "Assets/esenario/Zona_NaveRota.fbx";
        private const string FondoFbx = "Assets/esenario/Fondo.fbx";
        private const float InnerHalf = 38f; // zona del escenario original (terreno de 90 m con su borde)

        [Serializable] internal sealed class MapData { public Vector3 center; public float size; public MapRock[] rocks; public MapPlaced[] ice, nests, solar; public MapNamed[] spawns, parts; public MapDebris[] debris; public MapCrater[] craters; public Vector3 launch; }
        [Serializable] internal sealed class MapRock { public string kind; public Vector3 position; public Quaternion rotation; public float scale; }
        [Serializable] internal sealed class MapPlaced { public Vector3 position; public Quaternion rotation; public float scale; }
        [Serializable] internal sealed class MapNamed { public string name; public Vector3 position; }
        [Serializable] internal sealed class MapDebris { public string name; public long mesh, material, outlineMesh, outlineMaterial; public Vector3 position; public Quaternion rotation; public Vector3 scale; public bool collider; }
        [Serializable] internal sealed class MapCrater { public string name; public Vector3 position; public float radius; }

        private static void OnceBig()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += OnceBig; return; }
            if (EditorPrefs.GetBool(Application.dataPath + BigVersion, false)) return;
            EditorPrefs.SetBool(Application.dataPath + BigVersion, true); // una sola vez (el menu lo repite)
            try { CreateBig(); }
            catch (Exception error) { Debug.LogError("LUNA_MAPAGRANDE_FAILED: " + error); }
        }

        [MenuItem("Luna Cero/Crear mapa grande (esenario2)")]
        public static void CreateBig()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Sal de Play antes de crear el mapa.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(BigData);
            if (json == null) throw new InvalidOperationException("Falta " + BigData);
            var map = JsonUtility.FromJson<MapData>(json.text);
            var started = DateTime.Now;
            try
            {
                EditorUtility.DisplayProgressBar("Luna Cero", "Preparando modelos del mapa grande...", 0.05f);
                PrepareImport();
                foreach (var path in new[] { "Roca_Chica", "Roca_Mediana", "Roca_Grande", "Cristales_Hielo", "Nido_Criaturas" }) PrepareModel(Props + path + ".fbx");
                PrepareModel(FondoFbx);
                MakeReadable(NaveRotaFbx); // sus materiales internos se usan por nombre (no se reasignan)
                EnsureFolder(BigFolder);

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RenderSettings.skybox = null;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.055f, 0.06f, 0.085f);
                RenderSettings.fog = false;
                var sun = new GameObject("Sol").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 0.22f;
                sun.color = new Color(0.65f, 0.72f, 1f);
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(38, -35, 0);

                // Escenario original (base, nave, crater central) en el centro del mapa.
                var scenario = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ScenarioFbx));
                scenario.name = "Escenario original (base y crater)";
                // Se desempaqueta: hay que quitar su terreno pequeno y su fondo (no se puede borrar dentro de un prefab).
                PrefabUtility.UnpackPrefabInstance(scenario, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                foreach (Transform child in scenario.transform.Cast<Transform>().ToArray())
                    if (child.name.StartsWith("Montana_") || child.name == "Tierra" || child.name.EndsWith("_Marcador"))
                        Object.DestroyImmediate(child.gameObject); // el fondo se pone a escala del mapa grande
                var all = scenario.GetComponentsInChildren<Transform>(true);
                Transform Find(string name) => all.FirstOrDefault(t => t != null && t.name == name);
                FixOutlines(all);
                var special = new HashSet<Transform>();
                foreach (var (root, _) in ShipParts) { var t = Find(root); if (t != null) special.Add(t); }
                foreach (var t in all.Where(t => t.name.StartsWith("Hielo_") && !t.name.Contains("Contorno"))) special.Add(t);
                var lockerCrate = Find("Caja_1");
                if (lockerCrate != null) special.Add(lockerCrate);
                AddColliders(all, special);
                AddLights(all);

                EditorUtility.DisplayProgressBar("Luna Cero", "Generando terreno de 400 x 400 m...", 0.15f);
                var oldGround = Find("Terreno");
                if (oldGround == null) throw new InvalidOperationException("El escenario no tiene 'Terreno'.");
                Physics.SyncTransforms();
                var terrain = BuildTerrain(map, oldGround.GetComponent<Collider>());
                Object.DestroyImmediate(oldGround.gameObject);
                all = scenario.GetComponentsInChildren<Transform>(true);
                Physics.SyncTransforms();
                float GroundY(Vector3 p) => terrain.SampleHeight(p) + terrain.GetPosition().y;
                Vector3 OnSurface(Vector3 p)
                {
                    foreach (var hit in Physics.RaycastAll(new Vector3(p.x, 400f, p.z), Vector3.down, 800f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                        if (hit.collider.GetComponentInParent<LunarPickup>() == null) return hit.point;
                    return new Vector3(p.x, GroundY(p), p.z);
                }

                EditorUtility.DisplayProgressBar("Luna Cero", "Colocando rocas, escombros, hielo y nidos...", 0.4f);
                PlaceRocks(map, GroundY);
                var meleeDebris = PlaceDebris(map, GroundY);
                var iceRoot = new GameObject("Hielo de los crateres").transform;
                var nestRoot = new GameObject("Nidos de los crateres").transform;
                var iceModel = AssetDatabase.LoadAssetAtPath<GameObject>(Props + "Cristales_Hielo.fbx");
                var nestModel = AssetDatabase.LoadAssetAtPath<GameObject>(Props + "Nido_Criaturas.fbx");
                foreach (var item in map.ice)
                {
                    var go = Place(iceModel, iceRoot, item.position, item.rotation, item.scale, GroundY);
                    FitBox(go);
                    var pickup = go.AddComponent<LunarPickup>();
                    pickup.kind = LunarPickupKind.Ice;
                    pickup.displayName = "cristal de hielo";
                    pickup.verb = "extraer";
                    pickup.quantity = 30;
                    AddLights(go.GetComponentsInChildren<Transform>());
                }
                foreach (var item in map.nests)
                {
                    var go = Place(nestModel, nestRoot, item.position, item.rotation, item.scale, GroundY);
                    var parts = go.GetComponentsInChildren<Transform>();
                    AddColliders(parts, new HashSet<Transform>());
                    AddLights(parts);
                }
                var spawnRoot = new GameObject("Puntos de aparicion (mapa grande)").transform;
                var spawns = new List<Transform>();
                foreach (var point in map.spawns)
                {
                    var marker = new GameObject(point.name).transform;
                    marker.SetParent(spawnRoot, false);
                    marker.position = new Vector3(point.position.x, GroundY(point.position) + 0.3f, point.position.z);
                    spawns.Add(marker);
                }

                // Fondo (montanas y la Tierra) a la escala del mapa grande, sin colision.
                var fondo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FondoFbx));
                fondo.name = "Fondo lejano";
                fondo.transform.localScale = Vector3.one * 5.75f;
                foreach (var renderer in fondo.GetComponentsInChildren<Renderer>()) { renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; }
                FixOutlines(fondo.GetComponentsInChildren<Transform>());
                AddBoundaryWalls(new Bounds(new Vector3(map.center.x, 10f, map.center.z), new Vector3(map.size, 40f, map.size)));

                // Piezas de la nave: lejos de la base, en los puntos del mapa grande.
                var targets = new Dictionary<string, string>
                {
                    { "Pieza_Motor", "Motor" }, { "Pieza_Navegacion", "Navegacion" }, { "Pieza_Tanque", "Tanque" },
                    { "Pieza_Nucleo", "EscudoTermico" }, { "Pieza_Comunicaciones", "PanelSolar" }
                };
                foreach (var pair in targets)
                {
                    var piece = Find(pair.Key);
                    var point = map.parts.FirstOrDefault(p => p.name == pair.Value);
                    if (piece == null || point == null) continue;
                    piece.position = OnSurface(point.position) + Vector3.up * 0.5f;
                    var renderers = piece.GetComponentsInChildren<Renderer>();
                    if (renderers.Length > 0)
                    {
                        float bottom = renderers.Min(r => r.bounds.min.y);
                        piece.position += Vector3.up * (GroundY(piece.position) + 0.05f - bottom);
                    }
                }
                Physics.SyncTransforms();

                // Jugador
                var spawn = Find("Spawn_Jugador");
                var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/LunarPlayer.prefab"));
                player.name = "Astronauta";
                player.transform.position = (spawn != null ? spawn.position : Vector3.zero) + Vector3.up * 0.2f;
                var ship = Find("Nave_Proa");
                if (ship != null)
                {
                    Vector3 look = Vector3.ProjectOnPlane(ship.position - player.transform.position, Vector3.up);
                    if (look.sqrMagnitude > 0.1f) player.transform.rotation = Quaternion.LookRotation(look);
                }
                player.AddComponent<LunarPlayerStatus>();
                var combatComponent = player.GetComponent<LunarPlayerCombat>();
                var combat = new SerializedObject(combatComponent);
                combat.FindProperty("stationHealingOnly").boolValue = true;
                combat.FindProperty("initialReserveAmmo").intValue = 24;
                combat.FindProperty("initialMedkits").intValue = 0;
                combat.ApplyModifiedPropertiesWithoutUndo();
                var camera = player.GetComponentInChildren<Camera>();
                camera.farClipPlane = 1600;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.01f, 0.012f, 0.03f);
                CreateStars(new Vector3(map.center.x, 0, map.center.z), 1300f, "EstrellasMapaGrande");

                BuildCraterRamps(Find, terrain.transform, player.transform.position);
                BuildBase(Find, combatComponent, false); // sin armero: las armas estan repartidas por el mapa
                BuildShipParts(Find);
                BuildIce(all);

                EditorUtility.DisplayProgressBar("Luna Cero", "Repartiendo armas, objetos contundentes y municion...", 0.55f);
                ScatterLoot(map, combatComponent, player.transform.position, OnSurface, meleeDebris);

                // Navegacion horneada (en juego carga al instante) y hordas
                EditorUtility.DisplayProgressBar("Luna Cero", "Horneando la navegacion de los monstruos (puede tardar)...", 0.7f);
                Physics.SyncTransforms();
                var navigation = new GameObject("Navegacion del mapa grande").AddComponent<LunarLevelNavigation>();
                navigation.bounds = new Bounds(new Vector3(map.center.x, 10f, map.center.z), new Vector3(map.size + 4, 90f, map.size + 4));
                string navPath = BigFolder + "/Navegacion.asset";
                AssetDatabase.DeleteAsset(navPath);
                var navData = LunarLevelNavigation.Build(navigation.bounds, navigation.agentRadius, navigation.agentHeight, 0.25f);
                if (navData != null) { AssetDatabase.CreateAsset(navData, navPath); navigation.baked = navData; }
                else Debug.LogWarning("No se pudo hornear la navegacion; se construira al empezar la partida.");

                var director = new GameObject("Director de hordas").AddComponent<LunarHordeDirector>();
                director.commonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AlienFolder + "Common.prefab");
                director.specialPrefabs = new[] { "Bile", "Tongue", "Pouncer", "Dormant", "Brute", "Charger", "Acid", "Rider" }
                    .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(AlienFolder + n + ".prefab")).Where(p => p != null).ToArray();
                director.maximumEnemies = 22;
                director.minimumSpawnDistance = 22f;
                director.maximumSpawnDistance = 80f;
                director.spawnPoints = all.Where(t => t != null && t.name.StartsWith("Spawn_Criatura")).Concat(spawns).ToArray();

                EditorUtility.DisplayProgressBar("Luna Cero", "Guardando escena...", 0.95f);
                EditorSceneManager.SaveScene(scene, BigScenePath);
                var scenes = EditorBuildSettings.scenes.Where(s => s.path != BigScenePath).ToList();
                scenes.Insert(0, new EditorBuildSettingsScene(BigScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                AssetDatabase.SaveAssets();
                Selection.activeGameObject = player;
                SceneView.lastActiveSceneView?.LookAt(player.transform.position + Vector3.up, Quaternion.Euler(30, 30, 0), 25);
                Debug.Log($"LUNA_MAPAGRANDE_OK: {BigScenePath} | {map.size:0}x{map.size:0} m | rocas {map.rocks.Length} | escombros {map.debris.Length} | " +
                          $"crateres {map.craters.Length} | spawns {director.spawnPoints.Length} | navegacion {(navData != null ? "horneada" : "en juego")} | {(DateTime.Now - started).TotalSeconds:0} s");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private static void MakeReadable(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null || importer.isReadable) return;
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        private static bool Inner(Vector3 p, float half = InnerHalf) => Mathf.Abs(p.x) < half && Mathf.Abs(p.z) < half;

        private static float Ease(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        // ------------------------------------------------------------ terreno
        /// <summary>
        /// Alturas interpoladas desde los objetos del mapa (rocas, puntos, escombros: todos apoyados en el suelo
        /// original), crateres en sombra excavados con su borde, y el terreno original en el centro.
        /// </summary>
        private static Terrain BuildTerrain(MapData map, Collider original)
        {
            const int resolution = 513;
            float size = map.size;
            Vector3 origin = map.center - new Vector3(size / 2f, 0, size / 2f);

            var samples = new List<Vector3>();
            samples.AddRange(map.rocks.Select(r => r.position));
            samples.AddRange(map.spawns.Select(s => s.position));
            samples.AddRange(map.parts.Select(p => p.position));
            samples.AddRange(map.ice.Select(i => i.position));
            samples.AddRange(map.nests.Select(n => n.position));
            samples.AddRange(map.debris.Select(d => d.position));
            samples.RemoveAll(p => Inner(p));
            const float cell = 30f, sigma = 13f;
            var buckets = new Dictionary<(int, int), List<Vector3>>();
            foreach (var s in samples)
            {
                var key = (Mathf.FloorToInt(s.x / cell), Mathf.FloorToInt(s.z / cell));
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<Vector3>();
                list.Add(s);
            }
            float Interpolate(float x, float z)
            {
                int cx = Mathf.FloorToInt(x / cell), cz = Mathf.FloorToInt(z / cell);
                float sum = 0, weight = 0;
                for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (!buckets.TryGetValue((cx + dx, cz + dz), out var list)) continue;
                        foreach (var s in list)
                        {
                            float d2 = (s.x - x) * (s.x - x) + (s.z - z) * (s.z - z);
                            float w = Mathf.Exp(-d2 / (2f * sigma * sigma));
                            sum += w * s.y; weight += w;
                        }
                    }
                if (weight > 1e-4f) return sum / weight;
                // Sin datos cerca: el punto mas proximo.
                Vector3 nearest = samples.OrderBy(s => (s.x - x) * (s.x - x) + (s.z - z) * (s.z - z)).First();
                return nearest.y;
            }

            var craters = map.craters.Where(c => !Inner(c.position)).Select(c =>
            {
                float rim = 0;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    rim += Interpolate(c.position.x + Mathf.Cos(a) * c.radius * 1.3f, c.position.z + Mathf.Sin(a) * c.radius * 1.3f);
                }
                return (c, rim: Mathf.Max(rim / 8f, c.position.y + 3f) + 1.2f);
            }).ToArray();

            var heights = new float[resolution, resolution];
            float min = float.MaxValue, max = float.MinValue;
            for (int j = 0; j < resolution; j++)
                for (int i = 0; i < resolution; i++)
                {
                    float x = origin.x + i * size / (resolution - 1), z = origin.z + j * size / (resolution - 1);
                    float h = Interpolate(x, z);
                    foreach (var (c, rim) in craters)
                    {
                        float d = new Vector2(x - c.position.x, z - c.position.z).magnitude, r = c.radius;
                        if (d >= r * 1.45f) continue;
                        h = d < r ? Mathf.Lerp(c.position.y, rim, Ease(r * 0.4f, r, d)) : Mathf.Lerp(rim, h, Ease(r, r * 1.45f, d));
                    }
                    float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                    if (edge < InnerHalf && original != null && original.Raycast(new Ray(new Vector3(x, 200f, z), Vector3.down), out var hit, 400f))
                        h = Mathf.Lerp(h, hit.point.y, 1f - Ease(31f, InnerHalf, edge));
                    heights[j, i] = h;
                    min = Mathf.Min(min, h); max = Mathf.Max(max, h);
                }
            float baseY = min - 3f, range = max - baseY + 3f;
            for (int j = 0; j < resolution; j++)
                for (int i = 0; i < resolution; i++)
                    heights[j, i] = (heights[j, i] - baseY) / range;

            string dataPath = BigFolder + "/Terreno.asset";
            AssetDatabase.DeleteAsset(dataPath);
            var data = new TerrainData { heightmapResolution = resolution };
            data.size = new Vector3(size, range, size);
            data.SetHeights(0, 0, heights);
            data.terrainLayers = new[]
            {
                Layer("Regolito", new Color(0.55f, 0.55f, 0.58f), 9f, 1),
                Layer("RegolitoOscuro", new Color(0.3f, 0.3f, 0.34f), 7f, 2)
            };
            const int alpha = 256;
            data.alphamapResolution = alpha;
            var maps = new float[alpha, alpha, 2];
            for (int j = 0; j < alpha; j++)
                for (int i = 0; i < alpha; i++)
                {
                    float u = i / (alpha - 1f), v = j / (alpha - 1f);
                    float x = origin.x + u * size, z = origin.z + v * size;
                    float dark = Mathf.Clamp01((data.GetSteepness(u, v) - 22f) / 18f);
                    foreach (var (c, _) in craters)
                        dark = Mathf.Max(dark, 1f - Ease(c.radius * 0.5f, c.radius * 1.1f, new Vector2(x - c.position.x, z - c.position.z).magnitude));
                    maps[j, i, 0] = 1f - dark;
                    maps[j, i, 1] = dark;
                }
            data.SetAlphamaps(0, 0, maps);
            AssetDatabase.CreateAsset(data, dataPath);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terreno lunar (mapa grande)";
            go.transform.position = new Vector3(origin.x, baseY, origin.z);
            go.isStatic = true;
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 6;
            terrain.basemapDistance = 200;
            terrain.drawInstanced = true;
            terrain.shadowCastingMode = ShadowCastingMode.Off;
            Debug.Log($"LUNA_TERRENO: {size:0} m, alturas {min:0.0} a {max:0.0} m, {samples.Count} muestras, {craters.Length} crateres");
            return terrain;
        }

        /// <summary>Capa de terreno con textura de regolito generada (ruido periodico: sin costuras al repetirse).</summary>
        private static TerrainLayer Layer(string name, Color tint, float tile, int seed)
        {
            string texturePath = $"{BigFolder}/{name}.asset", layerPath = $"{BigFolder}/{name}.terrainlayer";
            AssetDatabase.DeleteAsset(texturePath);
            AssetDatabase.DeleteAsset(layerPath);
            const int n = 256;
            var random = new System.Random(seed);
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = name };
            var octaves = new[] { 4, 8, 16, 32, 64 };
            var lattices = octaves.Select(p => { var g = new float[p, p]; for (int y = 0; y < p; y++) for (int x = 0; x < p; x++) g[x, y] = (float)random.NextDouble(); return g; }).ToArray();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float value = 0, total = 0, amplitude = 1;
                    for (int o = 0; o < octaves.Length; o++)
                    {
                        int p = octaves[o];
                        float fx = x * p / (float)n, fy = y * p / (float)n;
                        int x0 = (int)fx, y0 = (int)fy;
                        float tx = fx - x0, ty = fy - y0;
                        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                        var g = lattices[o];
                        float a = Mathf.Lerp(g[x0 % p, y0 % p], g[(x0 + 1) % p, y0 % p], tx);
                        float b = Mathf.Lerp(g[x0 % p, (y0 + 1) % p], g[(x0 + 1) % p, (y0 + 1) % p], tx);
                        value += Mathf.Lerp(a, b, ty) * amplitude;
                        total += amplitude;
                        amplitude *= 0.55f;
                    }
                    float shade = 0.72f + 0.56f * (value / total - 0.5f) * 1.6f;
                    texture.SetPixel(x, y, new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f));
                }
            texture.Apply(true);
            AssetDatabase.CreateAsset(texture, texturePath);
            var layer = new TerrainLayer { diffuseTexture = texture, tileSize = new Vector2(tile, tile), smoothness = 0.05f, metallic = 0f };
            AssetDatabase.CreateAsset(layer, layerPath);
            return layer;
        }

        // ------------------------------------------------------------ objetos del mapa
        private static GameObject Place(GameObject model, Transform parent, Vector3 position, Quaternion rotation, float scale, Func<Vector3, float> groundY)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            float ground = groundY(position);
            // Se respeta la altura original si coincide con el terreno; si no, se apoya en el.
            float y = Mathf.Abs(position.y - ground) < 1.5f ? position.y : ground - 0.05f * scale;
            go.transform.SetPositionAndRotation(new Vector3(position.x, y, position.z), rotation);
            go.transform.localScale = Vector3.one * (scale > 0 ? scale : 1f);
            FixOutlines(go.GetComponentsInChildren<Transform>());
            return go;
        }

        private static void PlaceRocks(MapData map, Func<Vector3, float> groundY)
        {
            var root = new GameObject("Rocas del mapa grande").transform;
            var models = new Dictionary<string, GameObject>
            {
                { "Chica", AssetDatabase.LoadAssetAtPath<GameObject>(Props + "Roca_Chica.fbx") },
                { "Mediana", AssetDatabase.LoadAssetAtPath<GameObject>(Props + "Roca_Mediana.fbx") },
                { "Grande", AssetDatabase.LoadAssetAtPath<GameObject>(Props + "Roca_Grande.fbx") }
            };
            foreach (var rock in map.rocks)
            {
                if (Inner(rock.position, InnerHalf - 2f) || !models.TryGetValue(rock.kind, out var model) || model == null) continue;
                var go = Place(model, root, rock.position, rock.rotation, rock.scale, groundY);
                AddColliders(go.GetComponentsInChildren<Transform>(), new HashSet<Transform>());
                MarkStatic(go, rock.kind == "Chica" ? 0.012f : 0.006f);
            }
        }

        /// <summary>Escombros de la Condor (campo y rastros hacia las piezas). Devuelve los que sirven de arma.</summary>
        private static List<GameObject> PlaceDebris(MapData map, Func<Vector3, float> groundY)
        {
            var meshes = new Dictionary<long, Mesh>();
            var materials = new Dictionary<long, Material>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(NaveRotaFbx))
            {
                if (asset == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long id)) continue;
                if (asset is Mesh mesh) meshes[id] = mesh;
                else if (asset is Material material)
                    materials[id] = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{material.name}.mat") ?? material; // misma paleta
            }
            var outline = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Contorno.mat");
            var root = new GameObject("Escombros de la Condor").transform;
            var weapons = new List<GameObject>();
            int trail = 0, missing = 0;
            foreach (var item in map.debris)
            {
                if (Inner(item.position) || !meshes.TryGetValue(item.mesh, out var mesh)) { missing++; continue; }
                var go = new GameObject(item.name);
                go.transform.SetParent(root, false);
                float ground = groundY(item.position);
                float y = Mathf.Abs(item.position.y - ground) < 1.5f ? item.position.y : ground + 0.1f;
                go.transform.SetPositionAndRotation(new Vector3(item.position.x, y, item.position.z), item.rotation);
                go.transform.localScale = item.scale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = materials.TryGetValue(item.material, out var m) ? m : AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Metal.mat");
                if (meshes.TryGetValue(item.outlineMesh, out var outlineMesh))
                {
                    var edge = new GameObject(item.name + "_Contorno");
                    edge.transform.SetParent(go.transform, false);
                    edge.AddComponent<MeshFilter>().sharedMesh = outlineMesh;
                    var renderer = edge.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = outline;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                // Uno de cada dos trozos pequenos del rastro se puede agarrar y usar para golpear.
                bool small = item.name.StartsWith("Rastro_") && item.scale.x < 0.6f;
                if (small && trail++ % 2 == 0)
                {
                    LunarLoot.MakeMelee(go, "escombro", "escombro de la nave");
                    weapons.Add(go);
                    continue;
                }
                var box = go.AddComponent<BoxCollider>();
                box.center = mesh.bounds.center;
                box.size = mesh.bounds.size;
                MarkStatic(go, 0.01f);
            }
            if (missing > 0) Debug.Log($"LUNA_ESCOMBROS: {missing} escombros omitidos (zona central o malla no encontrada).");
            return weapons;
        }

        /// <summary>Estatico (se agrupa al dibujar) y se deja de dibujar cuando ocupa muy poco en pantalla.</summary>
        private static void MarkStatic(GameObject go, float cullScreenHeight)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(cullScreenHeight, renderers) });
            lod.RecalculateBounds();
        }

        // ------------------------------------------------------------ botin repartido
        private static void ScatterLoot(MapData map, LunarPlayerCombat combat, Vector3 playerSpawn, Func<Vector3, Vector3> onSurface, List<GameObject> meleeDebris)
        {
            var random = new System.Random(1602);
            var root = new GameObject("Botin repartido (armas, golpes, municion)").transform;
            Vector3 Jitter(Vector3 p, float amount)
            {
                double a = random.NextDouble() * Math.PI * 2;
                return p + new Vector3((float)Math.Cos(a), 0, (float)Math.Sin(a)) * amount;
            }
            var candidates = new List<Vector3>();
            candidates.AddRange(map.debris.Where(d => d.name.StartsWith("Escombro_Campo")).Select(d => Jitter(d.position, 2.5f)));
            candidates.AddRange(map.debris.Where(d => d.name.StartsWith("Rastro_")).Select(d => Jitter(d.position, 1.8f)));
            candidates.AddRange(map.spawns.Select(s => Jitter(s.position, 6f)));
            candidates.AddRange(map.ice.Select(i => Jitter(i.position, 3f)));
            candidates.AddRange(map.parts.Select(p => Jitter(p.position, 5f)));
            candidates.AddRange(map.rocks.Where((r, i) => i % 5 == 0).Select(r => Jitter(r.position, 2.4f * Mathf.Max(1f, r.scale))));
            candidates.RemoveAll(p => Inner(p, InnerHalf - 6f) || Mathf.Abs(p.x - map.center.x) > map.size / 2 - 6 || Mathf.Abs(p.z - map.center.z) > map.size / 2 - 6);
            candidates = candidates.OrderBy(_ => random.Next()).ToList();
            var placed = new List<Vector3>();
            bool Take(float spacing, float fromSpawn, out Vector3 position)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    Vector3 c = candidates[i];
                    if (Flat(c, playerSpawn) < fromSpawn || placed.Any(q => Flat(q, c) < spacing)) continue;
                    candidates.RemoveAt(i);
                    placed.Add(c);
                    position = onSurface(c);
                    return true;
                }
                position = default;
                return false;
            }
            float Yaw() => (float)random.NextDouble() * 360f;
            int guns = 0, melee = 0, ammo = 0, medkits = 0;
            foreach (var (id, count) in new[] { ("rifle", 3), ("escopeta", 3), ("riel", 2), ("plasma", 2) })
                for (int n = 0; n < count; n++)
                    if (Take(24f, 40f, out var p)) { LunarLoot.Weapon(root, combat.FindWeapon(id), p, Yaw()); guns++; }
            foreach (var (id, count) in new[] { ("llave", 2), ("tubo", 3), ("extintor", 3), ("barra", 3) })
                for (int n = 0; n < count; n++)
                    if (Take(14f, 25f, out var p)) { LunarLoot.Weapon(root, combat.FindWeapon(id), p, Yaw()); melee++; }
            for (int n = 0; n < 22; n++) if (Take(12f, 25f, out var p)) { LunarLoot.Ammo(root, p, 20, Yaw()); ammo++; }
            for (int n = 0; n < 8; n++) if (Take(16f, 30f, out var p)) { LunarLoot.Medkit(root, p, Yaw()); medkits++; }

            // Junto a la base, para empezar: una llave inglesa y un tubo (cualquier cosa de la nave sirve para golpear).
            LunarLoot.Weapon(root, combat.FindWeapon("llave"), onSurface(playerSpawn + new Vector3(1.6f, 0, 1.2f)), 30f);
            LunarLoot.Weapon(root, combat.FindWeapon("tubo"), onSurface(playerSpawn + new Vector3(-1.6f, 0, 1.4f)), -20f);
            Debug.Log($"LUNA_BOTIN: {guns} armas de fuego, {melee + 2} objetos contundentes, {meleeDebris.Count} escombros agarrables, {ammo} cajas de municion, {medkits} botiquines");
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
