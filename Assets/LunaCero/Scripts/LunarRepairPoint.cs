using System.Collections.Generic;
using UnityEngine;

namespace LunaCero
{
    /// <summary>
    /// Punto de reparacion de la nave: las piezas soltadas dentro del radio se instalan.
    /// Al reunir todas, la nave queda reparada (victoria).
    /// </summary>
    public sealed class LunarRepairPoint : MonoBehaviour
    {
        public float radius = 4f;
        public int requiredParts = 5;
        public Transform slotsRoot;
        private readonly List<LunarShipPart> installed = new List<LunarShipPart>();
        private LunarPlayerCombat player;
        private float messageUntil;
        private string message;
        private void Awake(){if(GetComponent<LostInSpaceFinale>()==null)gameObject.AddComponent<LostInSpaceFinale>();}
        public int Installed => installed.Count;
        public bool Repaired => installed.Count >= requiredParts;
        private LunarShipPart[] missionParts;
        private readonly HashSet<int> optionalSections = new HashSet<int>();

        /// <summary>Se conservan los objetos y sus IDs de red; solo se activan los necesarios.</summary>
        public void SetMissionPlayers(int players)
        {
            if (missionParts == null)
            {
                missionParts = FindObjectsOfType<LunarShipPart>(true);
                System.Array.Sort(missionParts, (a, b) => MissionOrder(a).CompareTo(MissionOrder(b)));
            }
            if (missionParts.Length == 0) return;
            requiredParts = Mathf.Min(missionParts.Length, Mathf.Clamp(players, 1, 4) * 5);
            optionalSections.Clear();
            for (int i = 0; i < missionParts.Length; i++)
            {
                var part = missionParts[i];
                if (part == null) continue;
                bool needed = i < requiredParts || part.Installed;
                if (part.gameObject.activeSelf != needed) part.gameObject.SetActive(needed);
                if (!needed) optionalSections.Add(LunarShipRebuild.IndexOf(part.partName));
            }
            // Las secciones que no requieren recogida ya forman parte de la nave.
            shownInstalled = -1;
        }

        private static int MissionOrder(LunarShipPart part)
        {
            int index = LunarShipRebuild.IndexOf(part.partName);
            if (index < 0) return int.MaxValue;
            var piece = LunarShipRebuild.Catalog[index];
            // Primero una pieza principal de cada sistema; despues una secundaria por sistema.
            int layer = piece.leader != null ? 0 : 1;
            int group = index / 4;
            int secondary = 0;
            for (int i = group * 4; i < index; i++)
                if (LunarShipRebuild.Catalog[i].leader == null) secondary++;
            return (layer == 0 ? 0 : secondary + 1) * 5 + group;
        }

        /// <summary>Cliente en red: las piezas las instala el anfitrion (se recibe con ApplyInstalled).</summary>
        [System.NonSerialized] public bool remote;
        public System.Collections.Generic.IReadOnlyList<LunarShipPart> InstalledParts => installed;

        public void ApplyInstalled(LunarShipPart part, Vector3 position, Quaternion rotation)
        {
            if (part == null || installed.Contains(part)) return;
            installed.Add(part);
            part.Install(position, rotation);
            Announce(part);
        }

        private void Update()
        {
            if (Repaired || remote || LunarPlayerRole.MenuOpen) return;
            foreach (var collider in Physics.OverlapSphere(transform.position, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var part = collider.GetComponentInParent<LunarShipPart>();
                if (part == null || part.Installed || installed.Contains(part)) continue;
                var pickup = part.GetComponent<LunarPickup>();
                if (pickup != null && pickup.IsHeld) continue;
                Install(part);
            }
            // Tambien se acepta con la pieza en las manos si el jugador esta dentro del anillo.
            if (player == null) player = FindObjectOfType<LunarPlayerCombat>();
            // El ingeniero aeronautico instala desde el doble de distancia.
            if (player != null && player.HeldObject != null && Flat(player.transform.position) <= radius * player.Role.repairRadius)
            {
                var held = player.HeldObject.GetComponent<LunarShipPart>();
                if (held != null && !held.Installed)
                {
                    player.Drop(false);
                    Install(held);
                }
            }
        }

        private float Flat(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            return new Vector2(offset.x, offset.z).magnitude;
        }

        private void Install(LunarShipPart part)
        {
            installed.Add(part);
            // La pieza pasa a formar parte de la nave: su seccion del holograma se vuelve solida.
            part.Install(transform.position + Vector3.down * 2f, part.transform.rotation);
            Announce(part);
        }

        private void Announce(LunarShipPart part)
        {
            message = Repaired ? $"NAVE REPARADA! Las {requiredParts} piezas estan instaladas." : $"Pieza instalada: {part.partName} ({installed.Count}/{requiredParts})";
            messageUntil = Time.time + 4f;
        }

        // ------------------------------------------------------------ progreso visible de la reparacion
        private Renderer[] sections = new Renderer[0];
        private ParticleSystem smoke, sparks;
        private Light statusLight;
        private int shownInstalled = -1;
        private bool hasDirector;

        private void Start()
        {
            if (!LunarAutomation.Running && !LunarSession.InMultiplayer && !LunarSession.Deciding) SetMissionPlayers(1);
            hasDirector = FindObjectOfType<LunarHordeDirector>() != null;
            // Holograma de la nave Condor sobre el anillo: una seccion por cada pieza del catalogo.
            sections = LunarShipRebuild.BuildFrame(transform);
            // Humo y chispas sobre la nave: disminuyen con cada pieza instalada.
            var ship = GameObject.Find("Nave_Proa");
            Vector3 wreck = ship != null ? ship.transform.position + Vector3.up * 1.5f : transform.position + Vector3.up * 2f;
            smoke = Emitter("Humo de la nave", wreck, new Color(0.25f, 0.25f, 0.27f, 0.45f), 2.5f, 1.4f, 4f, 1.6f, 0.05f);
            sparks = Emitter("Chispas de la nave", wreck, new Color(1f, 0.6f, 0.2f), 0.6f, 4f, 0.12f, 0.6f, -0.4f);
            statusLight = new GameObject("Luz de estado de la nave").AddComponent<Light>();
            statusLight.transform.SetParent(transform, false);
            statusLight.transform.localPosition = Vector3.up * 3f;
            statusLight.type = LightType.Point;
            statusLight.range = 10f;
            statusLight.intensity = 1.6f;
            RefreshProgress();
        }

        private ParticleSystem Emitter(string name, Vector3 position, Color color, float lifetime, float speed, float size, float spread, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, true);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(-90, 0, 0);
            var particles = go.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.gravityModifier = gravity;
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = spread;
            var sizeOverLife = particles.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, gravity > 0 ? AnimationCurve.Linear(0, 0.6f, 1, 1.8f) : AnimationCurve.Linear(0, 1, 1, 0));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = LunarHud.Particle(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return particles;
        }

        private void LateUpdate()
        {
            if (shownInstalled != installed.Count) RefreshProgress();
        }

        private void RefreshProgress()
        {
            shownInstalled = installed.Count;
            float done = installed.Count / (float)Mathf.Max(1, requiredParts);
            var filled = new HashSet<int>();
            foreach (var part in installed)
            {
                if (part == null) continue;
                filled.Add(LunarShipRebuild.IndexOf(part.partName));
                // La pieza suelta desaparece: ahora es parte de la nave.
                foreach (var r in part.GetComponentsInChildren<Renderer>()) r.enabled = false;
            }
            for (int i = 0; i < sections.Length; i++) LunarShipRebuild.Paint(sections[i], i, filled.Contains(i) || optionalSections.Contains(i));
            if (smoke != null) { var emission = smoke.emission; emission.rateOverTime = 14f * (1f - done); }
            if (sparks != null) { var emission = sparks.emission; emission.rateOverTime = 10f * (1f - done); }
            if (statusLight != null) statusLight.color = Color.Lerp(new Color(1f, 0.25f, 0.15f), new Color(0.3f, 1f, 0.45f), done);
        }

        private void OnGUI()
        {
            if (LunarPlayerRole.MenuOpen || LostInSpaceFinale.Active) return;
            var size = LunarHud.Begin(out var previous);
            float width = size.x, height = size.y;
            // Progreso de la nave: una linea con barra, debajo del panel del director.
            float y = LunarPlayerCombat.VitalsPanelBottom + (hasDirector ? 56 : 10);
            GUI.Label(new Rect(width - 272, y, 120, 22), $"Nave  {installed.Count}/{requiredParts}", LunarHud.Label);
            var track = new Rect(width - 182, y + 7, 168, 9);
            LunarHud.Fill(track, new Color(1f, 1f, 1f, 0.18f));
            LunarHud.Fill(new Rect(track.x, track.y, track.width * installed.Count / Mathf.Max(1f, requiredParts), track.height), new Color(0.3f, 1f, 0.5f, 0.95f));
            if (Time.time < messageUntil) LunarHud.Prompt(width / 2, 120, null, message, null, Repaired);
            if (Repaired) LunarHud.Prompt(width / 2, height / 2 - 120, null, "MISION CUMPLIDA", "La nave Condor esta reparada. Preparando el despegue...", true);
            Camera camera = Camera.main;
            if (camera != null && !Repaired)
            {
                Vector3 screen = camera.WorldToScreenPoint(transform.position + Vector3.up * 3f);
                float distance = Vector3.Distance(camera.transform.position, transform.position);
                bool carrying = player != null && player.HeldObject != null && player.HeldObject.GetComponent<LunarShipPart>() != null;
                // Solo cuando sirve: de lejos (para orientarse) o llevando una pieza. Cerca ya se ve el anillo verde.
                float range = player != null && player.Role.shipMarker ? 5000 : 150;
                if (screen.z > 0 && distance < range && (distance > 25f || carrying))
                {
                    float s = LunarHud.Scale;
                    LunarHud.Marker(screen.x / s, (Screen.height - screen.y) / s, $"Nave  {distance:0} m", new Color(0.4f, 1f, 0.55f));
                }
            }
            GUI.matrix = previous;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
