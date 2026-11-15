using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LunaCero
{
    /// <summary>
    /// Reconstruccion de la nave Condor pieza por pieza (20 piezas en 5 sistemas).
    /// - Las 5 piezas originales del mapa pasan a ser la pieza principal de cada sistema.
    /// - Las otras 15 se reparten por el mapa (en las mismas posiciones para todos los jugadores).
    /// - En el anillo de reparacion hay un holograma de la nave: cada seccion se vuelve solida
    ///   cuando se instala su pieza, y al completar las 20 la nave despega.
    /// </summary>
    public static class LunarShipRebuild
    {
        public sealed class Piece
        {
            public string name, system, leader, model;
            public PrimitiveType shape;
            public Vector3 position, scale, rotation;
            public Color color;
            public bool glow;
        }

        private static Piece P(string system, string name, string leader, string model, PrimitiveType shape, Vector3 position, Vector3 scale, Color color, Vector3 rotation = default, bool glow = false) =>
            new Piece { system = system, name = name, leader = leader, model = model, shape = shape, position = position, scale = scale, color = color, rotation = rotation, glow = glow };

        private static readonly Color Hull = new Color(0.78f, 0.8f, 0.84f), Dark = new Color(0.3f, 0.32f, 0.36f), Orange = new Color(1f, 0.5f, 0.15f);

        /// <summary>Las 20 piezas. 'leader' = nombre de la pieza original del mapa que se convierte en esta.</summary>
        public static readonly Piece[] Catalog =
        {
            // Casco y soporte vital
            P("Casco", "Placa del casco", null, null, PrimitiveType.Cube, new Vector3(0, 1.6f, 0), new Vector3(2.4f, 1.5f, 6f), Hull),
            P("Casco", "Escudo termico", null, null, PrimitiveType.Cube, new Vector3(0, 0.75f, 0), new Vector3(2.8f, 0.2f, 6.6f), new Color(0.55f, 0.28f, 0.12f)),
            P("Casco", "Tanque de oxigeno", "Tanque", null, PrimitiveType.Capsule, new Vector3(1.55f, 1.5f, -0.4f), new Vector3(0.7f, 1.3f, 0.7f), Color.white, new Vector3(90, 0, 0)),
            P("Casco", "Tren de aterrizaje", null, null, PrimitiveType.Cube, new Vector3(0, 0.35f, 0.6f), new Vector3(3.4f, 0.14f, 0.3f), Dark),
            // Propulsion
            P("Propulsion", "Motor de propulsion", "Motor", "part-motor", PrimitiveType.Cylinder, new Vector3(0, 1.6f, -3.6f), new Vector3(1.5f, 0.75f, 1.5f), Dark, new Vector3(90, 0, 0)),
            P("Propulsion", "Tobera principal", null, "part-motor", PrimitiveType.Cylinder, new Vector3(0, 1.6f, -4.5f), new Vector3(1.05f, 0.25f, 1.05f), Orange, new Vector3(90, 0, 0), true),
            P("Propulsion", "Turbobomba", null, "part-motor", PrimitiveType.Cylinder, new Vector3(1.05f, 1.0f, -3.1f), new Vector3(0.5f, 0.55f, 0.5f), Dark, new Vector3(90, 0, 0)),
            P("Propulsion", "Inyector de combustible", null, "part-motor", PrimitiveType.Cylinder, new Vector3(-1.05f, 1.0f, -3.1f), new Vector3(0.5f, 0.55f, 0.5f), Dark, new Vector3(90, 0, 0)),
            // Energia
            P("Energia", "Nucleo de energia", "Nucleo", "part-nucleo", PrimitiveType.Sphere, new Vector3(0, 2.6f, -1.3f), new Vector3(1f, 1f, 1f), new Color(0.35f, 1f, 0.6f), default, true),
            P("Energia", "Celda de energia A", null, "part-nucleo", PrimitiveType.Cube, new Vector3(1.45f, 2.0f, -1.9f), new Vector3(0.5f, 0.6f, 1f), new Color(0.3f, 0.75f, 0.45f)),
            P("Energia", "Celda de energia B", null, "part-nucleo", PrimitiveType.Cube, new Vector3(-1.45f, 2.0f, -1.9f), new Vector3(0.5f, 0.6f, 1f), new Color(0.3f, 0.75f, 0.45f)),
            P("Energia", "Regulador de voltaje", null, "part-nucleo", PrimitiveType.Cube, new Vector3(0, 2.5f, -2.45f), new Vector3(0.8f, 0.4f, 0.6f), Dark),
            // Comunicaciones
            P("Comunicaciones", "Modulo de comunicaciones", "Comunicaciones", "part-comunicaciones", PrimitiveType.Cube, new Vector3(0, 2.65f, 0.6f), new Vector3(1.2f, 0.6f, 1.2f), Hull),
            P("Comunicaciones", "Antena parabolica", null, "part-comunicaciones", PrimitiveType.Sphere, new Vector3(0, 3.35f, 0.6f), new Vector3(1.4f, 0.22f, 1.4f), Color.white),
            P("Comunicaciones", "Transmisor de emergencia", null, "part-comunicaciones", PrimitiveType.Cube, new Vector3(1.35f, 2.5f, 1.3f), new Vector3(0.4f, 0.5f, 0.6f), Dark),
            P("Comunicaciones", "Baliza de rescate", null, "part-comunicaciones", PrimitiveType.Sphere, new Vector3(-1.35f, 2.6f, 1.3f), new Vector3(0.45f, 0.45f, 0.45f), new Color(1f, 0.25f, 0.2f), default, true),
            // Navegacion
            P("Navegacion", "Computadora de navegacion", "Navegacion", null, PrimitiveType.Cube, new Vector3(0, 1.9f, 2.4f), new Vector3(1.6f, 0.8f, 1f), Dark),
            P("Navegacion", "Cabina de mando", null, null, PrimitiveType.Sphere, new Vector3(0, 2.1f, 3.3f), new Vector3(2f, 1.4f, 2f), new Color(0.3f, 0.75f, 1f), default, true),
            P("Navegacion", "Giroscopio", null, null, PrimitiveType.Sphere, new Vector3(0, 1.15f, 2.7f), new Vector3(0.6f, 0.6f, 0.6f), new Color(0.85f, 0.7f, 0.2f)),
            P("Navegacion", "Sensor estelar", null, null, PrimitiveType.Cylinder, new Vector3(0, 2.95f, 3.0f), new Vector3(0.3f, 0.3f, 0.3f), Hull)
        };

        public static int IndexOf(string name)
        {
            for (int i = 0; i < Catalog.Length; i++) if (Catalog[i].name == name) return i;
            return -1;
        }

        // ------------------------------------------------------------ reparto de piezas
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneLoaded += Loaded;
            Prepare();
        }

        private static void Loaded(Scene scene, LoadSceneMode mode) => Prepare();

        /// <summary>
        /// Se hace antes del Start de la escena (antes de que el multijugador numere los objetos),
        /// con posiciones que salen de la propia escena: todos los jugadores obtienen lo mismo.
        /// </summary>
        private static void Prepare()
        {
            var repair = Object.FindObjectOfType<LunarRepairPoint>();
            if (repair == null || Object.FindObjectOfType<LunarEndlessWorld>() != null) return;
            var originals = Object.FindObjectsOfType<LunarShipPart>();
            if (originals.Length == 0 || originals.Any(p => IndexOf(p.partName) >= 0)) return; // ya preparada

            // 1) Las piezas originales pasan a ser la principal de su sistema.
            var leaders = new Dictionary<string, LunarShipPart>();
            foreach (var part in originals)
            {
                var piece = Catalog.FirstOrDefault(c => c.leader == part.partName);
                if (piece == null) continue;
                Rename(part, piece);
                leaders[piece.system] = part;
            }
            if (leaders.Count == 0) return;

            // 2) Lugares para las demas: junto al botin repartido y a los puntos de aparicion, lejos de la base.
            Vector3 home = repair.transform.position;
            var candidates = new List<Vector3>();
            foreach (var pickup in Object.FindObjectsOfType<LunarPickup>())
                if (pickup.kind == LunarPickupKind.Ammo || pickup.kind == LunarPickupKind.Weapon || pickup.kind == LunarPickupKind.Ice)
                    candidates.Add(pickup.transform.position + Offset(pickup.transform.position));
            var director = Object.FindObjectOfType<LunarHordeDirector>();
            if (director != null && director.spawnPoints != null)
                foreach (var point in director.spawnPoints) if (point != null) candidates.Add(point.position + Offset(point.position));
            foreach (var part in originals) candidates.Add(Vector3.Lerp(home, part.transform.position, 0.6f));
            candidates = candidates
                .OrderBy(c => Mathf.RoundToInt(c.x * 10)).ThenBy(c => Mathf.RoundToInt(c.z * 10)).ToList();
            float reach = candidates.Count > 0 ? candidates.Max(c => Flat(c, home)) : 60f;
            float fromHome = Mathf.Clamp(reach * 0.15f, 15f, 40f);

            var used = new List<Vector3>(originals.Select(p => p.transform.position)) { home };
            var missing = Catalog.Where(c => originals.All(p => p.partName != c.name)).ToList();
            var root = new GameObject("Piezas de la nave (20)").transform;
            int created = 0;
            foreach (var piece in missing)
            {
                if (!leaders.TryGetValue(piece.system, out var leader)) leader = leaders.Values.First();
                // El mas alejado de lo ya usado (reparto uniforme por todo el mapa).
                Vector3 best = default;
                float bestScore = -1f;
                foreach (var c in candidates)
                {
                    if (Flat(c, home) < fromHome) continue;
                    float nearest = used.Min(u => Flat(u, c));
                    if (nearest > bestScore) { bestScore = nearest; best = c; }
                }
                if (bestScore < 0f) break;
                candidates.RemoveAll(c => Flat(c, best) < 4f);
                if (!Ground(best, out var ground)) continue;
                used.Add(ground);
                Spawn(leader, piece, ground, root);
                created++;
            }
            repair.requiredParts = originals.Length + created;
            Debug.Log($"LUNA_NAVE_20: {repair.requiredParts} piezas para reconstruir la nave ({created} repartidas por el mapa)");
        }

        private static Vector3 Offset(Vector3 p)
        {
            // Separada unos metros del objeto que la inspira (angulo fijo segun la posicion).
            float a = (Mathf.Abs(Mathf.RoundToInt(p.x * 7f + p.z * 13f)) % 360) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3f;
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static bool Ground(Vector3 p, out Vector3 ground)
        {
            ground = p;
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, p.y + 80f, p.z), Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.collider.GetComponentInParent<LunarPickup>() != null || hit.collider.attachedRigidbody != null) continue;
                ground = hit.point;
                return true;
            }
            return false;
        }

        private static void Rename(LunarShipPart part, Piece piece)
        {
            part.partName = piece.name;
            part.gameObject.name = "Pieza " + piece.name;
            if (part.TryGetComponent(out LunarPickup pickup)) pickup.displayName = "pieza " + piece.name.ToLowerInvariant();
        }

        /// <summary>Copia la pieza principal del sistema (con su recogida y fisica) y le pone el modelo de la carpeta si existe.</summary>
        private static void Spawn(LunarShipPart leader, Piece piece, Vector3 ground, Transform root)
        {
            var copy = Object.Instantiate(leader.gameObject); // conserva la escala real de la original
            copy.transform.SetParent(root, true);
            copy.transform.rotation = Quaternion.Euler(0f, (IndexOf(piece.name) * 47f) % 360f, 0f);
            var part = copy.GetComponent<LunarShipPart>();
            Rename(part, piece);
            var model = string.IsNullOrEmpty(piece.model) ? null : Resources.Load<GameObject>("LunarWeapons/" + piece.model);
            if (model != null)
            {
                foreach (var r in copy.GetComponentsInChildren<Renderer>()) r.enabled = false;
                var visual = Object.Instantiate(model, copy.transform);
                visual.name = "Modelo " + piece.name;
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                Vector3 s = copy.transform.lossyScale;
                // Piezas secundarias algo mas pequenas que la principal.
                float size = piece.leader != null ? 1f : 0.75f;
                visual.transform.localScale = new Vector3(size / Mathf.Max(0.001f, s.x), size / Mathf.Max(0.001f, s.y), size / Mathf.Max(0.001f, s.z));
                if (copy.TryGetComponent(out BoxCollider box))
                {
                    var bounds = LunarLoot.LocalBounds(copy.transform);
                    box.center = bounds.center;
                    box.size = bounds.size;
                }
            }
            else copy.transform.localScale *= 0.8f;
            // Apoyada en el suelo
            Physics.SyncTransforms();
            float bottom = float.MaxValue;
            foreach (var r in copy.GetComponentsInChildren<Renderer>()) if (r.enabled) bottom = Mathf.Min(bottom, r.bounds.min.y);
            copy.transform.position = ground + Vector3.up * 0.3f;
            if (bottom < float.MaxValue)
            {
                Physics.SyncTransforms();
                bottom = float.MaxValue;
                foreach (var r in copy.GetComponentsInChildren<Renderer>()) if (r.enabled) bottom = Mathf.Min(bottom, r.bounds.min.y);
                copy.transform.position += Vector3.up * (ground.y + 0.05f - bottom);
            }
        }

        // ------------------------------------------------------------ holograma de la nave
        /// <summary>Construye la nave en reconstruccion: una seccion por pieza (holograma hasta instalarla).</summary>
        public static Renderer[] BuildFrame(Transform parent)
        {
            var frame = new GameObject("Nave Condor en reconstruccion").transform;
            frame.SetParent(parent, false);
            frame.localPosition = Vector3.up * 0.05f;
            var renderers = new Renderer[Catalog.Length];
            for (int i = 0; i < Catalog.Length; i++)
            {
                var piece = Catalog[i];
                var go = GameObject.CreatePrimitive(piece.shape);
                Object.Destroy(go.GetComponent<Collider>()); // se puede atravesar: no estorba al soltar piezas
                go.name = "Seccion " + piece.name;
                go.transform.SetParent(frame, false);
                go.transform.localPosition = piece.position;
                go.transform.localRotation = Quaternion.Euler(piece.rotation);
                go.transform.localScale = piece.scale;
                renderers[i] = go.GetComponent<Renderer>();
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return renderers;
        }

        public static void Paint(Renderer section, int index, bool installed)
        {
            if (section == null || index < 0 || index >= Catalog.Length) return;
            var piece = Catalog[index];
            section.sharedMaterial = installed
                ? (piece.glow ? LunarRuntimeMaterials.Glow(piece.color, 1.2f) : LunarRuntimeMaterials.Get(piece.color))
                : LunarRuntimeMaterials.Get(new Color(0.3f, 0.85f, 1f, 0.12f));
            section.shadowCastingMode = installed ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
