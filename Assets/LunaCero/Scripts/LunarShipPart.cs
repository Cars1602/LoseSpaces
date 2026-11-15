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

        private void Awake() { pickup = GetComponent<LunarPickup>(); }

        public void Install(Vector3 position, Quaternion rotation)
        {
            Installed = true;
            var body = GetComponent<Rigidbody>();
            if (body != null) { body.velocity = Vector3.zero; body.isKinematic = true; }
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            if (pickup != null) Destroy(pickup); // ya instalada: no se puede volver a cargar
            transform.SetPositionAndRotation(position, rotation);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || Installed || pickup == null || pickup.IsHeld) return;
            Camera camera = Camera.main;
            if (camera == null) return;
            float distance = Vector3.Distance(camera.transform.position, transform.position);
            // El ingeniero aeronautico ve la distancia a todas las piezas.
            if (engineer == null || Time.frameCount % 30 == 0) engineer = FindObjectOfType<LunarPlayerCombat>();
            bool marker = engineer != null && engineer.Role.partMarkers;
            if (distance > (marker ? 2000 : 70)) return;
            Vector3 screen = camera.WorldToScreenPoint(transform.position + Vector3.up * 2.5f);
            if (screen.z <= 0) return;
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUI.Box(new Rect(screen.x / scale - 90, (Screen.height - screen.y) / scale, 180, 40), $"PIEZA: {partName}\n[{distance:0} m]");
            GUI.matrix = previous;
        }
    }
}
