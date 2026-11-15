using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace LunaCero.Editor
{
    /// <summary>
    /// Instala los modelos 3D:
    ///  - Moustro Emjambre (correr + atacar) en los aliens basicos: Common (horda) y BotDebil (arena).
    ///  - Pistola de Assets/pistola como modelo de la pistola inicial del jugador.
    /// Se ejecuta sola una vez; se puede repetir desde el menu Luna Cero.
    /// </summary>
    [InitializeOnLoad]
    public static class LunarModelInstaller
    {
        private const string Version = "LunaCero.Models.v4"; // v4: monstruos mas grandes (x1.35) con colision ajustada
        /// <summary>Escala de todos los monstruos con modelo. Sube o baja este numero y reinstala desde el menu.</summary>
        public const float SizeScale = 1.35f;
        private const string SwarmFolder = "Assets/Moustro Emjambre/";
        private const string SwarmController = "Assets/LunaCero/Animations/Emjambre.controller";
        private const string PistolFolder = "Assets/pistola";
        private const string PistolPrefab = "Assets/LunaCero/Prefabs/Armas/Pistola.prefab";
        private const string PlayerPrefab = "Assets/LunaCero/Prefabs/LunarPlayer.prefab";
        private static readonly (string path, float height)[] SwarmTargets =
        {
            ("Assets/LunaCero/Prefabs/Aliens/Common.prefab", 1.6f),
            ("Assets/LunaCero/Prefabs/BotDebil.prefab", 1.75f)
        };
        private const string SpitterFolder = "Assets/Boomer/";
        // El Boomer (Contaminador / Bile) es el Escupidor jefe. El de acido conserva el mismo modelo como especial.
        private static readonly (string path, float height)[] SpitterTargets =
        {
            ("Assets/LunaCero/Prefabs/Aliens/Bile.prefab", 2.3f),
            ("Assets/LunaCero/Prefabs/Aliens/Acid.prefab", 1.9f)
        };

        static LunarModelInstaller() { EditorApplication.delayCall += Once; }

        private static void Once()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += Once; return; }
            if (EditorPrefs.GetBool(Application.dataPath + Version, false)) return;
            try
            {
                InstallAll();
                EditorPrefs.SetBool(Application.dataPath + Version, true);
            }
            catch (Exception error) { Debug.LogError("LUNA_MODELS_FAILED: " + error); }
        }

        [MenuItem("Luna Cero/Instalar modelos (aliens, jefes y pistola)")]
        public static void InstallAll()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Sal de Play primero.");
            Exception failure = null;
            try { InstallSwarm(); } catch (Exception error) { failure = error; Debug.LogError("LUNA_SWARM_FAILED: " + error); }
            try { InstallSpitter(); } catch (Exception error) { failure = error; Debug.LogError("LUNA_ESCUPIDOR_FAILED: " + error); }
            try { InstallBosses(); } catch (Exception error) { failure = error; Debug.LogError("LUNA_JEFES_FAILED: " + error); }
            try { InstallPistol(); } catch (Exception error) { failure = error; Debug.LogError("LUNA_PISTOL_FAILED: " + error); }
            AssetDatabase.SaveAssets();
            if (failure != null) throw new Exception("Algun modelo no se instalo; revisa los errores anteriores.", failure);
            Debug.Log("LUNA_MODELS_OK: Moustro Emjambre en aliens basicos y pistola 3D en el jugador.");
        }

        // ---------------------------------------------------------------- Aliens animados
        /// <summary>Moustro Emjambre en los aliens basicos.</summary>
        public static void InstallSwarm() =>
            InstallAnimatedAlien("Emjambre", SwarmFolder, "CorreEmjambre.fbx", "atacarEmjambre.fbx", SwarmTargets);

        /// <summary>Escupidor (Assets/Boomer) en el Boomer/Contaminador (jefe) y en el alien de acido.</summary>
        public static void InstallSpitter() =>
            InstallAnimatedAlien("Escupidor", SpitterFolder, "Corres Escupidor.fbx", "atack escupidor.fbx", SpitterTargets);

        /// <summary>Reinstala todos los aliens con modelo (lo usa el generador de la expedicion).</summary>
        public static void InstallAliens()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SwarmFolder + "atacarEmjambre.fbx") != null) InstallSwarm();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SpitterFolder + "atack escupidor.fbx") != null) InstallSpitter();
            InstallBosses();
        }

        // ---------------------------------------------------------------- Jefes
        private const string GiantFolder = "Assets/Moustro Gigante/";
        private const string StalkerFolder = "Assets/Jockey/moustro asechador/";
        private const string AlienPrefabs = "Assets/LunaCero/Prefabs/Aliens/";

        /// <summary>Jefes: Asechador (salta), Gigante (rocas y golpes), Escupidor (lanza acido), Jockey (se monta).</summary>
        public static readonly LunarAlienKind[] BossKinds = { LunarAlienKind.Pouncer, LunarAlienKind.Brute, LunarAlienKind.Bile, LunarAlienKind.Rider };

        public static void InstallBosses()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GiantFolder + "Caminar Moustro.fbx") != null)
                InstallAnimatedAlien("Gigante", GiantFolder, "Caminar Moustro.fbx", "atacke moustro.fbx",
                    new[] { (AlienPrefabs + "Brute.prefab", 3.0f) }, "muerte.fbx");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(StalkerFolder + "asechando.fbx") != null)
                InstallAnimatedAlien("Asechador", StalkerFolder, "asechando.fbx", "asechando.fbx",
                    new[] { (AlienPrefabs + "Pouncer.prefab", 1.5f) }, "MuerteASE.fbx");
            ConfigureBosses();
        }

        /// <summary>Marca los 4 jefes (y quita la marca a los demas especiales) y ajusta su vida y nombre.</summary>
        public static void ConfigureBosses()
        {
            var settings = new Dictionary<LunarAlienKind, (string title, float health)>
            {
                { LunarAlienKind.Pouncer, ("Asechador", 260f) },
                { LunarAlienKind.Brute, ("Gigante", 500f) },
                { LunarAlienKind.Bile, ("Escupidor", 280f) },
                { LunarAlienKind.Acid, ("Acido", 90f) },
                { LunarAlienKind.Rider, ("Jockey", 240f) }
            };
            foreach (LunarAlienKind kind in Enum.GetValues(typeof(LunarAlienKind)))
            {
                if (kind == LunarAlienKind.Common) continue;
                string path = AlienPrefabs + kind + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var enemy = root.GetComponent<LunarEnemy>();
                    bool boss = BossKinds.Contains(kind);
                    enemy.isBoss = boss;
                    if (settings.TryGetValue(kind, out var s))
                    {
                        enemy.displayName = s.title;
                        var health = new SerializedObject(root.GetComponent<LunarHealth>());
                        health.FindProperty("maximum").floatValue = s.health;
                        health.ApplyModifiedPropertiesWithoutUndo();
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Debug.Log("LUNA_JEFES_OK: Asechador, Gigante, Escupidor y Jockey son los 4 jefes.");
        }

        private static void InstallAnimatedAlien(string name, string folder, string runFile, string attackFile,
            (string path, float height)[] targets, string deathFile = null)
        {
            var run = Clip(folder + runFile, true, false);
            // Si el FBX de correr se exporto sin animacion, el clip de ataque en bucle sirve de movimiento provisional.
            var attack = attackFile == runFile && run != null ? run : Clip(folder + attackFile, run == null, true);
            var death = deathFile != null ? Clip(folder + deathFile, false, false) : null;
            string modelPath = folder + (run != null ? runFile : attackFile);
            if (run == null)
            {
                Debug.LogWarning($"LUNA_{name.ToUpperInvariant()}_WARNING: {runFile} no contiene animacion (solo malla y esqueleto). " +
                                 $"Se usa {attackFile} en bucle para moverse. Re-exporta {runFile} con la animacion y vuelve a instalar.");
                run = attack;
            }
            string controllerPath = "Assets/LunaCero/Animations/" + name + ".controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            var moving = machine.AddState("Correr");
            moving.motion = run;
            machine.defaultState = moving;
            machine.AddState("Atacar").motion = attack;
            if (death != null) machine.AddState("Muerte").motion = death;
            EditorUtility.SetDirty(controller);
            var material = TexturedMaterial(name, folder, 0.2f, 0f);

            foreach (var (path, height) in targets)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { Debug.LogWarning("No existe " + path); continue; }
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int colliders = root.GetComponentsInChildren<Collider>().Length;
                    var visual = root.transform.Find("Visual");
                    if (visual == null) { visual = new GameObject("Visual").transform; visual.SetParent(root.transform, false); }
                    for (int i = visual.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(visual.GetChild(i).gameObject);
                    visual.localScale = Vector3.one; // escalas no uniformes de los cubos deformarian el modelo
                    visual.localRotation = Quaternion.identity;

                    var anchor = new GameObject("Modelo " + name).transform;
                    anchor.SetParent(visual, false);
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
                    model.transform.SetParent(anchor, false);
                    foreach (var collider in model.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                    var renderers = model.GetComponentsInChildren<Renderer>();
                    if (renderers.Length == 0) throw new Exception(modelPath + " no tiene malla");
                    foreach (var renderer in renderers) renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();

                    Bounds bounds = LocalBounds(anchor, renderers);
                    float finalHeight = height * SizeScale;
                    anchor.localScale = Vector3.one * (finalHeight / bounds.size.y);
                    model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                    FitBody(root, finalHeight);

                    var animator = model.GetComponent<Animator>();
                    if (animator == null) animator = model.AddComponent<Animator>();
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;
                    // Sin camara (editor de prefabs) el culling impediria evaluar huesos: verificar con AlwaysAnimate.
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    VerifyAnimates(animator, "Correr", "Atacar");
                    animator.Rebind(); animator.Play("Correr", 0, 0); animator.Update(0);
                    animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                    var driver = root.GetComponent<LunarAlienAnimation>();
                    if (driver == null) driver = root.AddComponent<LunarAlienAnimation>();
                    driver.animator = animator;
                    driver.attackDuration = Mathf.Clamp(attack.length, 0.4f, 1.5f);
                    driver.deathState = death != null ? "Muerte" : "";
                    driver.deathDuration = death != null ? death.length + 1f : 4f;
                    var agent = root.GetComponent<NavMeshAgent>();
                    if (agent != null) driver.referenceSpeed = Mathf.Max(1f, agent.speed);

                    if (root.GetComponentsInChildren<Collider>().Length != colliders) throw new Exception("Se alteraron los colisionadores de " + path);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"LUNA_{name.ToUpperInvariant()}_OK: " + path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        /// <summary>
        /// La capsula (impactos) y el agente de navegacion crecen con el modelo. El radio se escala en
        /// proporcion a la altura anterior, asi que reinstalar con la misma altura no lo cambia.
        /// </summary>
        private static void FitBody(GameObject root, float height)
        {
            var capsule = root.GetComponent<CapsuleCollider>();
            if (capsule != null && capsule.height > 0.1f)
            {
                float ratio = height / capsule.height;
                capsule.radius = Mathf.Clamp(capsule.radius * ratio, 0.25f, 1.3f);
                capsule.height = height;
                capsule.center = new Vector3(0, height / 2f, 0);
            }
            var agent = root.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.height = height;
                agent.radius = Mathf.Clamp(capsule != null ? capsule.radius : agent.radius, 0.4f, 1.1f);
            }
        }

        // ---------------------------------------------------------------- Pistola
        public static void InstallPistol()
        {
            string fbx = AssetDatabase.FindAssets("t:Model", new[] { PistolFolder }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            if (fbx == null) { Debug.LogWarning("No hay modelo en " + PistolFolder); return; }
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (importer.importAnimation || importer.animationType != ModelImporterAnimationType.None || importer.addCollider)
            {
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.addCollider = false;
                importer.SaveAndReimport();
            }
            EnsureFolder("Assets/LunaCero/Prefabs/Armas");
            var material = TexturedMaterial("Pistola", PistolFolder, 0.55f, 0.6f);

            var root = new GameObject("Pistola");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
                model.name = "Modelo";
                model.transform.SetParent(root.transform, false);
                foreach (var collider in model.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                foreach (var animator in model.GetComponentsInChildren<Animator>()) UnityEngine.Object.DestroyImmediate(animator);
                var renderers = model.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) throw new Exception("La pistola no tiene malla");
                foreach (var renderer in renderers) renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();

                // El cañon debe apuntar a +Z (hacia donde mira el jugador). Si el eje largo es X, giramos.
                Bounds bounds = LocalBounds(root.transform, renderers);
                if (bounds.size.x > bounds.size.z) model.transform.localRotation = Quaternion.Euler(0, 90, 0);
                bounds = LocalBounds(root.transform, renderers);
                float scale = 0.45f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                model.transform.localScale *= scale;
                bounds = LocalBounds(root.transform, renderers);
                model.transform.localPosition -= bounds.center;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PistolPrefab);

                var player = PrefabUtility.LoadPrefabContents(PlayerPrefab);
                try
                {
                    var combat = new SerializedObject(player.GetComponent<LunarPlayerCombat>());
                    combat.FindProperty("pistolModel").objectReferenceValue = prefab;
                    combat.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefab);
                }
                finally { PrefabUtility.UnloadPrefabContents(player); }
                Debug.Log("LUNA_PISTOL_OK: " + PistolPrefab + " asignada al jugador. Si el cañon apunta al reves, gira 'Modelo' 180 grados en el prefab.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // ---------------------------------------------------------------- utilidades
        private static AnimationClip Clip(string path, bool loop, bool required)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Falta " + path);
            importer.importAnimation = true; importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = loop;
                clip.lockRootHeightY = clip.lockRootPositionXZ = clip.lockRootRotation = true;
                clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var result = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (result == null || result.length <= 0 || result.empty)
            {
                if (required) throw new InvalidOperationException("Clip vacio: " + path);
                return null;
            }
            return result;
        }

        private static void VerifyAnimates(Animator animator, params string[] states)
        {
            var bones = animator.GetComponentsInChildren<Transform>();
            foreach (var state in states)
            {
                animator.Rebind(); animator.Play(state, 0, 0.05f); animator.Update(0);
                var pose = bones.Select(b => b.localRotation).ToArray();
                animator.Update(animator.GetCurrentAnimatorStateInfo(0).length * 0.35f);
                int changed = bones.Where((b, i) => Quaternion.Angle(b.localRotation, pose[i]) > 0.01f).Count();
                if (changed == 0) throw new Exception("El clip no anima el esqueleto: " + state);
            }
        }

        private static Material TexturedMaterial(string name, string folder, float smoothness, float metallic)
        {
            EnsureFolder("Assets/LunaCero/Materials");
            string path = "Assets/LunaCero/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
            var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { folder.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
            string baseColor = textures.FirstOrDefault(p => p.ToLowerInvariant().Contains("basecolor"));
            string normal = textures.FirstOrDefault(p => p.ToLowerInvariant().Contains("normal"));
            if (baseColor != null) material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(baseColor);
            if (normal != null)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(normal);
                if (importer.textureType != TextureImporterType.NormalMap) { importer.textureType = TextureImporterType.NormalMap; importer.SaveAndReimport(); }
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                material.EnableKeyword("_NORMALMAP");
            }
            material.color = Color.white;
            material.SetFloat("_Glossiness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Bounds LocalBounds(Transform space, Renderer[] renderers)
        {
            bool first = true;
            var bounds = new Bounds();
            foreach (var renderer in renderers)
            {
                var world = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = world.center + Vector3.Scale(world.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = space.InverseTransformPoint(corner);
                    if (first) { bounds = new Bounds(local, Vector3.zero); first = false; }
                    else bounds.Encapsulate(local);
                }
            }
            return bounds;
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
