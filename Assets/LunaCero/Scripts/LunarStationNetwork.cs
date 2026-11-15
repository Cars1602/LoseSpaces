using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace LunaCero
{
    /// <summary>
    /// Garantiza 3 estaciones medicas repartidas por el mapa de expedicion. Conserva las que ya tiene
    /// la escena (la de la base) y coloca las que faltan en el camino hacia las piezas de la nave,
    /// lo mas separadas posible entre si. Las posiciones salen de la escena (no del azar), asi que
    /// todos los jugadores de una partida en red ven las estaciones en el mismo sitio.
    /// </summary>
    public sealed class LunarStationNetwork : MonoBehaviour
    {
        public const int Wanted = 3;
        public float minimumSpacing = 45f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneLoaded += Loaded;
            Loaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void Loaded(Scene scene, LoadSceneMode mode)
        {
            if (FindObjectOfType<LunarStationNetwork>() != null) return;
            new GameObject("Red de estaciones medicas").AddComponent<LunarStationNetwork>();
        }

        private IEnumerator Start()
        {
            yield return null; // la navegacion horneada se carga en el primer cuadro
            var repair = FindObjectOfType<LunarRepairPoint>();
            // Solo en los mapas de expedicion (con piezas que llevar a la nave); no en la arena ni en el mundo infinito.
            if (repair == null || FindObjectOfType<LunarEndlessWorld>() != null) { Destroy(gameObject); yield break; }

            var stations = FindObjectsOfType<LunarHealingStation>().Select(s => s.transform.position).ToList();
            Vector3 anchor = stations.Count > 0 ? stations[0] : repair.transform.position;
            float spacing = minimumSpacing;
            // Mapa pequeno: menos separacion.
            var parts = FindObjectsOfType<LunarShipPart>().OrderBy(p => p.partName).ToArray();
            float far = parts.Length > 0 ? parts.Max(p => Flat(p.transform.position, anchor)) : 0f;
            if (far < 120f) spacing = Mathf.Max(18f, far * 0.4f);

            // Candidatos: a mitad y a dos tercios del camino hacia cada pieza, y alrededor de la base.
            var candidates = new List<Vector3>();
            foreach (var part in parts)
                foreach (float t in new[] { 0.55f, 0.7f, 0.4f })
                    candidates.Add(Vector3.Lerp(anchor, part.transform.position, t));
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                candidates.Add(anchor + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Mathf.Max(spacing * 1.4f, 60f));
            }

            int created = 0;
            while (stations.Count < Wanted)
            {
                Vector3 best = default;
                float bestScore = -1f;
                foreach (var c in candidates)
                {
                    float nearest = stations.Count == 0 ? float.MaxValue : stations.Min(s => Flat(s, c));
                    if (nearest < spacing || nearest <= bestScore) continue;
                    if (!TryGround(c, out var ground)) continue;
                    best = ground;
                    bestScore = nearest;
                }
                if (bestScore < 0f) break;
                candidates.RemoveAll(c => Flat(c, best) < 6f);
                stations.Add(best);
                Build(best, "campo" + (++created));
            }
            if (created > 0) Debug.Log($"LUNA_ESTACIONES: {created} estaciones medicas nuevas ({stations.Count} en total)");
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>Suelo transitable y llano cerca del punto (no encima de una roca ni dentro de un crater profundo).</summary>
        private static bool TryGround(Vector3 point, out Vector3 ground)
        {
            ground = point;
            for (int ring = 0; ring < 4; ring++)
                for (int k = 0; k < (ring == 0 ? 1 : 8); k++)
                {
                    float a = k * Mathf.PI / 4f;
                    Vector3 probe = point + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * ring * 4f;
                    if (NavMesh.SamplePosition(probe + Vector3.up * 2f, out var nav, 6f, NavMesh.AllAreas)) probe = nav.position;
                    if (!Physics.Raycast(new Vector3(probe.x, probe.y + 60f, probe.z), Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.normal.y < 0.88f || hit.collider.GetComponentInParent<LunarPickup>() != null || hit.collider.attachedRigidbody != null) continue;
                    // Que quepa el anillo: nada solido en 2 m alrededor.
                    bool blocked = false;
                    foreach (var c in Physics.OverlapCapsule(hit.point + Vector3.up * 0.6f, hit.point + Vector3.up * 2f, 2f, ~0, QueryTriggerInteraction.Ignore))
                        if (c != hit.collider) { blocked = true; break; }
                    if (blocked) continue;
                    ground = hit.point;
                    return true;
                }
            return false;
        }

        private static void Build(Vector3 position, string key)
        {
            var root = new GameObject("Estacion medica " + key);
            root.transform.position = position;
            var station = root.AddComponent<LunarHealingStation>();
            station.radius = 2.4f;
            station.stationKey = key;

            Color green = new Color(0.25f, 1f, 0.6f);
            Part(root.transform, PrimitiveType.Cylinder, "Plataforma", new Vector3(0, 0.06f, 0), new Vector3(5.2f, 0.06f, 5.2f), new Color(0.32f, 0.36f, 0.42f), false);
            Part(root.transform, PrimitiveType.Cylinder, "Anillo", new Vector3(0, 0.13f, 0), new Vector3(4.8f, 0.012f, 4.8f), new Color(0.25f, 1f, 0.6f, 0.45f), false, true);
            Part(root.transform, PrimitiveType.Cube, "Columna", new Vector3(0, 1.4f, -2.2f), new Vector3(0.45f, 2.8f, 0.45f), new Color(0.85f, 0.88f, 0.92f), true);
            // Cruz medica brillante en lo alto de la columna (se ve de lejos en la oscuridad)
            Part(root.transform, PrimitiveType.Cube, "Cruz H", new Vector3(0, 3.2f, -2.2f), new Vector3(0.95f, 0.3f, 0.12f), green, false, true);
            Part(root.transform, PrimitiveType.Cube, "Cruz V", new Vector3(0, 3.2f, -2.2f), new Vector3(0.3f, 0.95f, 0.12f), green, false, true);
            var light = new GameObject("Luz").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0, 2.6f, 0);
            light.type = LightType.Point;
            light.color = new Color(0.1f, 1f, 0.65f);
            light.range = 8f;
            light.intensity = 1.6f;
            var sign = new GameObject("Letrero");
            sign.transform.SetParent(root.transform, false);
            sign.transform.localPosition = new Vector3(0, 3.9f, -2.2f);
            sign.AddComponent<LunarFaceCamera>();
            LunarShip.Label(sign.transform, "ESTACION MEDICA", Vector3.zero, 0.09f, new Color(0.3f, 1f, 0.7f));
        }

        private static void Part(Transform parent, PrimitiveType type, string name, Vector3 local, Vector3 scale, Color color, bool collider, bool glow = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = scale;
            if (!collider) Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = glow && color.a >= 1f ? LunarRuntimeMaterials.Glow(color, 1.2f) : LunarRuntimeMaterials.Get(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
