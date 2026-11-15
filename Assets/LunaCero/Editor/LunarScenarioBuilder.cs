using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LunaCero.Editor
{
    /// <summary>
    /// Recrea el escenario de Assets/esenario (Escenario_Completo.fbx) como nivel jugable:
    /// materiales, colisiones, luces, base con oxigeno/estacion/armero, piezas de la nave,
    /// punto de reparacion, hielo extraible, aliens desde sus puntos de aparicion y navegacion.
    /// </summary>
    [InitializeOnLoad]
    public static partial class LunarScenarioBuilder
    {
        private const string Version = "LunaCero.Escenario.v3"; // v3: escenario oscuro (linterna)
        static LunarScenarioBuilder()
        {
            EditorApplication.delayCall += Once;
            EditorApplication.delayCall += OnceBig; // mapa grande (esenario2), despues del escenario original
        }
        private static void Once()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += Once; return; }
            if (EditorPrefs.GetBool(Application.dataPath + Version, false)) return;
            EditorPrefs.SetBool(Application.dataPath + Version, true); // una sola vez, aunque falle (usa el menu para repetir)
            try { Create(); }
            catch (Exception error) { Debug.LogError("LUNA_ESCENARIO_FAILED: " + error); }
        }

        public const string ScenePath = "Assets/LunaCero/Scenes/LunaCeroEscenario.unity";
        private const string ScenarioFbx = "Assets/esenario/Escenario_Completo.fbx";
        private const string TerrainTexture = "Assets/esenario/Terreno_Color.png";
        private const string MaterialFolder = "Assets/LunaCero/Materials/Escenario";
        private const string AlienFolder = "Assets/LunaCero/Prefabs/Aliens/";

        // Colores y emision tomados de los materiales de Blender del escenario.
        private static readonly Dictionary<string, (Color color, float emission)> Palette = new Dictionary<string, (Color, float)>
        {
            { "Amarillo", (new Color(1f, 0.8f, 0.12f), 0) }, { "AzulOscuro", (new Color(0.12f, 0.18f, 0.4f), 0) },
            { "Blanco", (new Color(0.95f, 0.95f, 0.92f), 0) }, { "Contorno", (new Color(0.04f, 0.04f, 0.07f), 0) },
            { "Escarcha", (new Color(0.82f, 0.93f, 1f), 0) }, { "Hielo", (new Color(0.45f, 0.95f, 1f), 1.5f) },
            { "Huevo", (new Color(0.6f, 1f, 0.3f), 2.5f) }, { "LuzAmarilla", (new Color(1f, 0.8f, 0.2f), 4f) },
            { "LuzRoja", (new Color(1f, 0.15f, 0.1f), 5f) }, { "LuzVerde", (new Color(0.3f, 1f, 0.4f), 4f) },
            { "Metal", (new Color(0.55f, 0.58f, 0.65f), 0) }, { "Naranja", (new Color(1f, 0.42f, 0.08f), 0) },
            { "Nido", (new Color(0.45f, 0.14f, 0.55f), 0) }, { "Nucleo", (new Color(0.4f, 1f, 0.6f), 4f) },
            { "Plataforma", (new Color(0.36f, 0.44f, 0.58f), 0) }, { "Quemado", (new Color(0.1f, 0.09f, 0.11f), 0) },
            { "Roca", (new Color(0.44f, 0.42f, 0.5f), 0) }, { "RocaOscura", (new Color(0.3f, 0.29f, 0.37f), 0) },
            { "Rojo", (new Color(0.9f, 0.16f, 0.16f), 0) }, { "Terreno", (Color.white, 0) },
            { "TierraContinente", (new Color(0.25f, 0.75f, 0.35f), 0.4f) }, { "TierraOceano", (new Color(0.15f, 0.45f, 0.95f), 0.4f) },
            { "Ventana", (new Color(0.3f, 0.8f, 1f), 2f) }
        };

        private static readonly (string root, string title)[] ShipParts =
        {
            ("Pieza_Nucleo", "Nucleo"), ("Pieza_Navegacion", "Navegacion"), ("Pieza_Tanque", "Tanque"),
            ("Pieza_Motor", "Motor"), ("Pieza_Comunicaciones", "Comunicaciones")
        };

        // Objetos sin colision: decorado plano, contornos, fondo lejano y elementos interactivos que se tratan aparte.
        private static readonly string[] NoCollider = { "_Contorno", "Marcador", "Escarcha", "Surco", "Tierra", "Montana", "Pad_Aro", "Franja", "Bandera", "Nido_Huevo" };

        [MenuItem("Luna Cero/Crear escenario lunar (esenario)")]
        public static void Create()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Sal de Play antes de crear el escenario.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ScenarioFbx) == null) throw new InvalidOperationException("Falta " + ScenarioFbx);
            PrepareImport();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            // Noche lunar: el sol apenas ilumina; la linterna, las luces de la base y los postes guian al jugador.
            RenderSettings.ambientLight = new Color(0.055f, 0.06f, 0.085f);
            RenderSettings.fog = false; // sin niebla: las estrellas y la Tierra siguen visibles en el cielo
            var sun = new GameObject("Sol").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.22f;
            sun.color = new Color(0.65f, 0.72f, 1f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(38, -35, 0);

            var scenario = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ScenarioFbx));
            scenario.name = "Escenario";
            var all = scenario.GetComponentsInChildren<Transform>(true);
            Transform Find(string name) => all.FirstOrDefault(t => t.name == name);

            FixOutlines(all);
            var special = new HashSet<Transform>();
            foreach (var (root, _) in ShipParts) { var t = Find(root); if (t != null) special.Add(t); }
            foreach (var t in all.Where(t => t.name.StartsWith("Hielo_") && !t.name.Contains("Contorno"))) special.Add(t);
            var lockerCrate = Find("Caja_1");
            if (lockerCrate != null) special.Add(lockerCrate);
            AddColliders(all, special);
            AddLights(all);

            var terrain = Find("Terreno");
            if (terrain == null) throw new InvalidOperationException("El escenario no tiene 'Terreno'.");
            Bounds level = terrain.GetComponent<Renderer>().bounds;
            AddBoundaryWalls(level);

            // Jugador
            var spawn = Find("Spawn_Jugador");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/LunarPlayer.prefab"));
            player.name = "Astronauta";
            player.transform.position = (spawn != null ? spawn.position : level.center) + Vector3.up * 0.2f;
            var ship = Find("Nave_Proa");
            if (ship != null)
            {
                Vector3 look = Vector3.ProjectOnPlane(ship.position - player.transform.position, Vector3.up);
                if (look.sqrMagnitude > 0.1f) player.transform.rotation = Quaternion.LookRotation(look);
            }
            player.AddComponent<LunarPlayerStatus>();
            var combat = new SerializedObject(player.GetComponent<LunarPlayerCombat>());
            combat.FindProperty("stationHealingOnly").boolValue = true;
            combat.FindProperty("initialReserveAmmo").intValue = 24;
            combat.FindProperty("initialMedkits").intValue = 0;
            combat.ApplyModifiedPropertiesWithoutUndo();
            var camera = player.GetComponentInChildren<Camera>();
            camera.farClipPlane = 450;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.01f, 0.012f, 0.03f);
            CreateStars(level.center);

            BuildCraterRamps(Find, terrain, player.transform.position);
            BuildBase(Find, player.GetComponent<LunarPlayerCombat>());
            BuildShipParts(Find);
            BuildIce(all);

            // Navegacion y aliens
            var navigation = new GameObject("Navegacion del escenario").AddComponent<LunarLevelNavigation>();
            navigation.bounds = new Bounds(level.center, level.size + new Vector3(4, 40, 4));
            var director = new GameObject("Director de hordas").AddComponent<LunarHordeDirector>();
            director.commonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AlienFolder + "Common.prefab");
            director.specialPrefabs = new[] { "Bile", "Tongue", "Pouncer", "Dormant", "Brute", "Charger", "Acid", "Rider" }
                .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(AlienFolder + n + ".prefab")).Where(p => p != null).ToArray();
            director.maximumEnemies = 18;
            director.spawnPoints = all.Where(t => t.name.StartsWith("Spawn_Criatura")).ToArray();
            if (director.commonPrefab == null) Debug.LogWarning("No hay prefab Common; crea primero la expedicion de supervivencia.");

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            // El mapa grande, si existe, sigue siendo la primera escena del juego.
            scenes.Insert(scenes.Count > 0 && scenes[0].path == BigScenePath ? 1 : 0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = player;
            SceneView.lastActiveSceneView?.LookAt(player.transform.position + Vector3.up, Quaternion.Euler(30, 30, 0), 18);
            Debug.Log($"LUNA_ESCENARIO_OK: {ScenePath} | terreno {level.size.x:0}x{level.size.z:0} m | piezas {ShipParts.Length} | spawns {director.spawnPoints.Length}");
        }

        // ------------------------------------------------------------ importacion y materiales
        private static void PrepareImport() => PrepareModel(ScenarioFbx);

        /// <summary>Materiales de la paleta + lectura de mallas (navegacion) para cualquier FBX del escenario.</summary>
        private static void PrepareModel(string path)
        {
            EnsureFolder(MaterialFolder);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Falta " + path);
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.isReadable = true; // la navegacion en tiempo de ejecucion necesita leer las mallas
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            var terrainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TerrainTexture);
            foreach (var entry in Palette)
            {
                string materialPath = $"{MaterialFolder}/{entry.Key}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                bool outline = entry.Key == "Contorno";
                if (material == null)
                {
                    material = new Material(Shader.Find(outline ? "Unlit/Color" : "Standard"));
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                material.color = entry.Value.color;
                material.enableInstancing = true;
                if (!outline)
                {
                    material.SetFloat("_Glossiness", entry.Key == "Metal" || entry.Key == "Ventana" ? 0.55f : entry.Key == "Hielo" ? 0.8f : 0.12f);
                    material.SetFloat("_Metallic", entry.Key == "Metal" ? 0.5f : 0f);
                    if (entry.Value.emission > 0)
                    {
                        material.EnableKeyword("_EMISSION");
                        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                        material.SetColor("_EmissionColor", entry.Value.color * Mathf.Min(entry.Value.emission, 3f) * 0.6f);
                    }
                    else material.DisableKeyword("_EMISSION");
                    if (entry.Key == "Terreno" && terrainTexture != null) material.mainTexture = terrainTexture;
                }
                EditorUtility.SetDirty(material);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.Key), material);
            }
            importer.SaveAndReimport();
        }

        // Los contornos son copias con normales invertidas ("inverted hull"): deben coincidir con su padre.
        private static void FixOutlines(IEnumerable<Transform> all)
        {
            foreach (var t in all.Where(t => t.name.EndsWith("_Contorno")).ToArray())
            {
                var renderer = t.GetComponent<Renderer>();
                if (renderer == null) continue;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var parentRenderer = t.parent != null ? t.parent.GetComponent<Renderer>() : null;
                if (parentRenderer == null) continue;
                float tolerance = Mathf.Max(0.05f, parentRenderer.bounds.size.magnitude * 0.05f);
                if (Vector3.Distance(renderer.bounds.center, parentRenderer.bounds.center) > tolerance ||
                    Mathf.Abs(renderer.bounds.size.y - parentRenderer.bounds.size.y) > tolerance * 2)
                {
                    t.localPosition = Vector3.zero;
                    t.localRotation = Quaternion.identity;
                    t.localScale = Vector3.one;
                }
            }
        }

        private static bool IsUnder(Transform t, HashSet<Transform> roots)
        {
            for (var current = t; current != null; current = current.parent) if (roots.Contains(current)) return true;
            return false;
        }

        private static void AddColliders(IEnumerable<Transform> all, HashSet<Transform> special)
        {
            foreach (var t in all)
            {
                var filter = t.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                if (NoCollider.Any(n => t.name.Contains(n)) || IsUnder(t, special)) continue;
                var size = t.GetComponent<Renderer>().bounds.size;
                if (size.x < 0.25f && size.z < 0.25f && size.y < 0.25f) continue;
                var collider = t.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                t.gameObject.isStatic = true;
            }
        }

        private static void AddLights(IEnumerable<Transform> all)
        {
            foreach (var t in all)
            {
                if (t.name.Contains("_Contorno")) continue;
                var renderer = t.GetComponent<Renderer>();
                if (renderer == null || renderer.sharedMaterial == null) continue;
                string material = renderer.sharedMaterial.name;
                if (!material.StartsWith("Luz") && material != "Nucleo" && material != "Hielo") continue;
                var light = new GameObject("Luz " + t.name).AddComponent<Light>();
                light.transform.SetParent(t, false);
                light.transform.position = renderer.bounds.center;
                light.type = LightType.Point;
                light.color = renderer.sharedMaterial.color;
                light.range = material == "Hielo" ? 5f : material.StartsWith("Luz") ? 11f : 6f;
                light.intensity = material == "Hielo" ? 1.2f : 2.2f;
                light.shadows = LightShadows.None;
            }
        }

        private static void AddBoundaryWalls(Bounds level)
        {
            var root = new GameObject("Limites invisibles").transform;
            float height = 40f, thick = 2f;
            Vector3 c = level.center, s = level.size;
            void Wall(string name, Vector3 position, Vector3 size)
            {
                var wall = new GameObject(name);
                wall.transform.SetParent(root, false);
                wall.transform.position = position;
                wall.AddComponent<BoxCollider>().size = size;
                wall.isStatic = true;
            }
            Wall("Limite norte", new Vector3(c.x, c.y, c.z + s.z / 2 + thick / 2), new Vector3(s.x + 2 * thick, height, thick));
            Wall("Limite sur", new Vector3(c.x, c.y, c.z - s.z / 2 - thick / 2), new Vector3(s.x + 2 * thick, height, thick));
            Wall("Limite este", new Vector3(c.x + s.x / 2 + thick / 2, c.y, c.z), new Vector3(thick, height, s.z));
            Wall("Limite oeste", new Vector3(c.x - s.x / 2 - thick / 2, c.y, c.z), new Vector3(thick, height, s.z));
        }

        private static void CreateStars(Vector3 center, float radius = 400f, string assetName = "Estrellas")
        {
            var mesh = new Mesh { name = assetName };
            var random = new System.Random(7);
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (int i = 0; i < 900; i++)
            {
                var direction = new Vector3((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 0.9f + 0.05f, (float)random.NextDouble() * 2 - 1).normalized;
                vertices.Add(direction * radius);
                indices.Add(i);
            }
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Points, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 2.25f);
            string path = $"Assets/LunaCero/Materials/Escenario/{assetName}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var stars = new GameObject("Cielo estrellado");
            stars.transform.position = center;
            stars.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = stars.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = SolidMaterial("Estrella", Color.white, true);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------ rampas del crater
        /// <summary>
        /// Las paredes del crater superan los 50 grados (no se pueden subir). Se crean rampas
        /// desde el fondo hasta el borde, orientadas hacia la base y hacia la nave.
        /// </summary>
        private static void BuildCraterRamps(Func<string, Transform> find, Transform terrain, Vector3 playerSpawn)
        {
            var nest = find("Nido_Criaturas");
            if (nest == null) return;
            Physics.SyncTransforms();
            var terrainCollider = terrain.GetComponent<Collider>();
            float Height(Vector3 p)
            {
                var ray = new Ray(new Vector3(p.x, 200f, p.z), Vector3.down);
                return terrainCollider != null && terrainCollider.Raycast(ray, out var hit, 400f) ? hit.point.y : float.NaN;
            }
            Vector3 center = nest.position;
            var targets = new List<Vector3> { playerSpawn };
            var repair = find("Punto_Reparacion_Nave");
            if (repair != null) targets.Add(repair.position);
            var root = new GameObject("Rampas del crater").transform;
            var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/RocaOscura.mat");
            foreach (var target in targets)
            {
                Vector3 direction = Vector3.ProjectOnPlane(target - center, Vector3.up).normalized;
                if (direction.sqrMagnitude < 0.01f) continue;
                float floor = Height(center);
                float rimRadius = 0, rimHeight = float.MinValue, lastFloorRadius = 0;
                bool climbing = false;
                for (float r = 0; r <= 24f; r += 0.5f)
                {
                    float h = Height(center + direction * r);
                    if (float.IsNaN(h)) continue;
                    if (!climbing && h <= floor + 0.4f) lastFloorRadius = r;
                    if (h > floor + 1f) climbing = true;
                    if (climbing && h > rimHeight) { rimHeight = h; rimRadius = r; }
                    if (climbing && h < rimHeight - 0.5f) break; // pasado el borde
                }
                if (rimHeight - floor < 2f) continue;
                float startRadius = Mathf.Max(0f, lastFloorRadius - 5f);
                Vector3 start = center + direction * startRadius;
                start.y = Height(start);
                Vector3 end = center + direction * (rimRadius + 1.5f);
                end.y = Mathf.Max(rimHeight, Height(end));
                Vector3 along = end - start;
                var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ramp.name = "Rampa del crater";
                ramp.transform.SetParent(root, false);
                ramp.transform.rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
                ramp.transform.position = (start + end) / 2f - ramp.transform.up * 0.25f;
                ramp.transform.localScale = new Vector3(3f, 0.5f, along.magnitude + 1f);
                ramp.GetComponent<Renderer>().sharedMaterial = material;
                ramp.isStatic = true;
                float slope = Mathf.Atan2(along.y, new Vector2(along.x, along.z).magnitude) * Mathf.Rad2Deg;
                Debug.Log($"LUNA_RAMPA: {along.magnitude:0.0} m, pendiente {slope:0} grados");
            }
        }

        // ------------------------------------------------------------ base: oxigeno, estacion, armero
        private static void BuildBase(Func<string, Transform> find, LunarPlayerCombat combat, bool armory = true)
        {
            var platform = find("Base_Plataforma");
            if (platform != null)
            {
                var zone = new GameObject("Zona presurizada de la base").AddComponent<LunarOxygenZone>();
                zone.transform.position = platform.GetComponent<Renderer>().bounds.center;
                zone.radius = platform.GetComponent<Renderer>().bounds.extents.x * 0.95f;
            }
            var medPad = find("Punto_Colocacion_Base_03") ?? find("Pad_Base_3");
            if (medPad != null)
            {
                var station = new GameObject("Estacion medica").AddComponent<LunarHealingStation>();
                station.transform.position = medPad.position;
                station.radius = 1.8f;
                station.stationKey = "base";
                var light = station.gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.1f, 1f, 0.65f);
                light.range = 6;
                light.intensity = 1.5f;
                LunarShip.Label(station.transform, "ESTACION MEDICA", Vector3.up * 2.4f, 0.08f, new Color(0.3f, 1f, 0.7f));
            }

            // Casillero de municion en una caja de suministros
            var crate = find("Caja_1");
            if (crate != null)
            {
                FitBox(crate.gameObject);
                var locker = crate.gameObject.AddComponent<LunarPickup>();
                locker.kind = LunarPickupKind.AmmoLocker;
                locker.displayName = "caja de municion";
                locker.verb = "usar";
                locker.cooldown = 45f;
            }

            // Armero: pedestales junto a las cajas, del lado contrario al habitat
            var habitat = find("Habitat");
            Transform anchor = find("Caja_2") ?? crate;
            if (!armory || anchor == null || combat == null) return;
            Vector3 outward = habitat != null ? Vector3.ProjectOnPlane(anchor.position - habitat.position, Vector3.up).normalized : Vector3.forward;
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, outward);
            var rack = new GameObject("Armero").transform;
            rack.position = anchor.position + outward * 2.6f;
            rack.rotation = Quaternion.LookRotation(-outward);
            var weapons = combat.Arsenal;
            for (int i = 0; i < weapons.Length; i++)
            {
                Vector3 local = new Vector3((i - (weapons.Length - 1) / 2f) * 1.5f, 0, 0);
                var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pedestal.name = "Pedestal " + weapons[i].displayName;
                pedestal.transform.SetParent(rack, false);
                pedestal.transform.localPosition = local + Vector3.up * 0.5f;
                pedestal.transform.localScale = new Vector3(1.1f, 1f, 0.9f);
                pedestal.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Metal.mat");
                pedestal.isStatic = true;
                LunarShip.CreateWeaponPickup(rack, weapons[i], local + Vector3.up * 1.25f);
                LunarShip.Label(rack, weapons[i].displayName, local + Vector3.up * 1.95f, 0.1f, Color.white, Quaternion.Euler(0, 180, 0));
            }
            LunarShip.Label(rack, "ARMERO", Vector3.up * 2.6f, 0.2f, new Color(1f, 0.75f, 0.15f), Quaternion.Euler(0, 180, 0));
        }

        // ------------------------------------------------------------ piezas y reparacion
        private static void BuildShipParts(Func<string, Transform> find)
        {
            foreach (var (rootName, title) in ShipParts)
            {
                var root = find(rootName);
                if (root == null) { Debug.LogWarning("No se encontro " + rootName); continue; }
                FitBox(root.gameObject);
                var body = root.gameObject.AddComponent<Rigidbody>();
                body.mass = 25f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var pickup = root.gameObject.AddComponent<LunarPickup>();
                pickup.kind = LunarPickupKind.Carryable;
                pickup.displayName = "pieza " + title;
                pickup.verb = "cargar";
                pickup.keepOrientation = true;
                root.gameObject.AddComponent<LunarShipPart>().partName = title;
            }
            var repairPoint = find("Punto_Reparacion_Nave");
            if (repairPoint == null) { Debug.LogWarning("No hay Punto_Reparacion_Nave"); return; }
            var repair = new GameObject("Reparacion de la nave").AddComponent<LunarRepairPoint>();
            repair.transform.position = repairPoint.position;
            repair.requiredParts = ShipParts.Length;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Anillo de reparacion";
            UnityEngine.Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(repair.transform, false);
            ring.transform.localPosition = Vector3.up * 0.04f;
            ring.transform.localScale = new Vector3(repair.radius * 2, 0.02f, repair.radius * 2);
            ring.GetComponent<Renderer>().sharedMaterial = SolidMaterial("AnilloReparacion", new Color(0.3f, 1f, 0.45f, 0.35f), false);
            var light = repair.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.3f, 1f, 0.45f);
            light.range = 7;
            light.intensity = 1.2f;
        }

        private static void BuildIce(IEnumerable<Transform> all)
        {
            foreach (var crystal in all.Where(t => t.name.StartsWith("Hielo_") && !t.name.Contains("Contorno")).ToArray())
            {
                FitBox(crystal.gameObject);
                var pickup = crystal.gameObject.AddComponent<LunarPickup>();
                pickup.kind = LunarPickupKind.Ice;
                pickup.displayName = "cristal de hielo";
                pickup.verb = "extraer";
                pickup.quantity = 30;
            }
        }

        // ------------------------------------------------------------ utilidades
        /// <summary>BoxCollider en la raiz que envuelve todas sus mallas (en espacio local).</summary>
        private static void FitBox(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>().Where(r => !r.name.Contains("_Contorno")).ToArray();
            if (renderers.Length == 0) return;
            bool first = true;
            var bounds = new Bounds();
            foreach (var renderer in renderers)
            {
                var b = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = root.transform.InverseTransformPoint(corner);
                    if (first) { bounds = new Bounds(local, Vector3.zero); first = false; } else bounds.Encapsulate(local);
                }
            }
            var box = root.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.3f);
        }

        private static Material SolidMaterial(string name, Color color, bool unlit)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(unlit ? "Unlit/Color" : "Standard"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            if (!unlit && color.a < 1)
            {
                material.SetFloat("_Mode", 3);
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = 3000;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
