using UnityEngine;

namespace LunaCero
{
    /// <summary>Pieza de la nave estrellada. Se carga (E) y se suelta en el punto de reparacion.</summary>
    public sealed class LunarShipPart : MonoBehaviour
    {
        public string partName = "Pieza";
        public bool Installed { get; private set; }
        private LunarPickup pickup;
        private static LunarPlayerCombat engineer;

        private GameObject beacon;

        private void Awake() { pickup = GetComponent<LunarPickup>(); }

        // Con 20 piezas solo se marcan las 3 mas cercanas (el resto se ve en el radar y por su baliza).
        private static readonly System.Collections.Generic.List<LunarShipPart> all = new System.Collections.Generic.List<LunarShipPart>();
        private static readonly System.Collections.Generic.HashSet<LunarShipPart> marked = new System.Collections.Generic.HashSet<LunarShipPart>();
        private static float nextRank;
        private void OnEnable() { all.Add(this); }
        private void OnDisable() { all.Remove(this); }

        private static void Rank(Vector3 from)
        {
            if (Time.unscaledTime < nextRank) return;
            nextRank = Time.unscaledTime + 0.5f;
            marked.Clear();
            all.RemoveAll(p => p == null);
            all.Sort((a, b) => (a.transform.position - from).sqrMagnitude.CompareTo((b.transform.position - from).sqrMagnitude));
            foreach (var p in all)
            {
                if (p.Installed || p.pickup == null || p.pickup.IsHeld) continue;
                marked.Add(p);
                if (marked.Count >= 3) break;
            }
        }

        /// <summary>Baliza: columna de luz azul sobre la pieza (se ve de lejos en la noche lunar).</summary>
        private void Start()
        {
            if (Installed) return;
            beacon = new GameObject("Baliza " + partName);
            var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(column.GetComponent<Collider>());
            column.name = "Columna";
            column.transform.SetParent(beacon.transform, false);
            column.transform.localPosition = Vector3.up * 14f;
            column.transform.localScale = new Vector3(0.35f, 14f, 0.35f);
            var renderer = column.GetComponent<Renderer>();
            renderer.sharedMaterial = LunarRuntimeMaterials.Get(new Color(0.3f, 0.75f, 1f, 0.22f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var glow = new GameObject("Brillo").AddComponent<Light>();
            glow.transform.SetParent(beacon.transform, false);
            glow.transform.localPosition = Vector3.up * 1.2f;
            glow.type = LightType.Point;
            glow.color = new Color(0.3f, 0.75f, 1f);
            glow.range = 6f;
            glow.intensity = 1.4f;
        }

        private void LateUpdate()
        {
            if (beacon == null) return;
            bool show = !Installed && (pickup == null || !pickup.IsHeld);
            if (beacon.activeSelf != show) beacon.SetActive(show);
            if (show) beacon.transform.position = transform.position;
        }

        private void OnDestroy() { if (beacon != null) Destroy(beacon); }

        public void Install(Vector3 position, Quaternion rotation)
        {
            Installed = true;
            if (beacon != null) Destroy(beacon);
            var body = GetComponent<Rigidbody>();
            if (body != null) { body.velocity = Vector3.zero; body.isKinematic = true; }
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            if (pickup != null) Destroy(pickup); // ya instalada: no se puede volver a cargar
            transform.SetPositionAndRotation(position, rotation);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || Installed || pickup == null || pickup.IsHeld || LunarPlayerRole.MenuOpen || LostInSpaceFinale.Active) return;
            Camera camera = Camera.main;
            if (camera == null) return;
            float distance = Vector3.Distance(camera.transform.position, transform.position);
            // El ingeniero aeronautico ve la distancia a todas las piezas.
            if (engineer == null || Time.frameCount % 30 == 0) engineer = FindObjectOfType<LunarPlayerCombat>();
            bool marker = engineer != null && engineer.Role.partMarkers;
            if (distance > (marker ? 2000 : 70)) return;
            Vector3 screen = camera.WorldToScreenPoint(transform.position + Vector3.up * 2.5f);
            if (screen.z <= 0) return;
            if (distance < 5f) return; // al lado ya sale el aviso [E] del jugador
            Rank(camera.transform.position);
            if (!marked.Contains(this)) return;
            LunarHud.Begin(out var previous);
            float scale = LunarHud.Scale;
            LunarHud.Marker(screen.x / scale, (Screen.height - screen.y) / scale, $"{partName}  {distance:0} m", new Color(0.4f, 0.8f, 1f));
            GUI.matrix = previous;
        }
    }
}
