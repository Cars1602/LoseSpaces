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
        public int Installed => installed.Count;
        public bool Repaired => installed.Count >= requiredParts;

        private void Update()
        {
            if (Repaired) return;
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
            int index = installed.Count - 1;
            float angle = index * Mathf.PI * 2f / Mathf.Max(1, requiredParts);
            Vector3 slot = transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (radius * 0.55f);
            part.Install(slot, Quaternion.Euler(0, angle * Mathf.Rad2Deg, 0) * part.transform.rotation);
            message = Repaired ? "NAVE REPARADA! Has recuperado las cinco piezas." : $"Pieza instalada: {part.partName} ({installed.Count}/{requiredParts})";
            messageUntil = Time.time + 4f;
        }

        private void OnGUI()
        {
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = Screen.width / scale;
            // Bajo el panel del director (arriba a la derecha); la izquierda la ocupa el radar.
            GUI.Box(new Rect(width - 320, 126, 300, 26), $"Piezas de la nave: {installed.Count}/{requiredParts}  (azules en el radar)");
            if (Time.time < messageUntil) GUI.Box(new Rect(width / 2 - 220, 140, 440, 30), message);
            if (Repaired) GUI.Box(new Rect(width / 2 - 220, Screen.height / scale / 2 - 90, 440, 60), "MISION CUMPLIDA\nLa nave Condor esta reparada.");
            Camera camera = Camera.main;
            if (camera != null && !Repaired)
            {
                Vector3 screen = camera.WorldToScreenPoint(transform.position + Vector3.up * 3f);
                float distance = Vector3.Distance(camera.transform.position, transform.position);
                // El piloto ve siempre donde esta la nave.
                if (screen.z > 0 && distance < (player != null && player.Role.shipMarker ? 5000 : 120))
                    GUI.Box(new Rect(screen.x / scale - 110, (Screen.height - screen.y) / scale, 220, 40), $"REPARACION DE NAVE [{distance:0} m]\nSuelta aqui las piezas");
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
