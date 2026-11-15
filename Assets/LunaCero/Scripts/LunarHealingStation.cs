using UnityEngine;

namespace LunaCero
{
    public sealed class LunarHealingStation : MonoBehaviour
    {
        public sealed class Supply
        {
            public int Used;
            public float ReadyAt;
        }
        public float radius = 3.4f;
        public string stationKey;
        [Min(1)] public int maximumUses = 3;
        private Supply localSupply = new Supply();
        private Supply Stock
        {
            get
            {
                var world = GetComponentInParent<LunarEndlessWorld>(true);
                return world != null && !string.IsNullOrEmpty(stationKey) ? world.StationSupply(stationKey) : localSupply;
            }
        }
        public float ReadyAt => Stock.ReadyAt;
        public int RemainingUses => Mathf.Max(0, maximumUses - Stock.Used);
        public bool Exhausted => RemainingUses == 0;
        public bool Contains(Vector3 position) => Vector3.ProjectOnPlane(position - transform.position, Vector3.up).sqrMagnitude <= radius * radius && Mathf.Abs(position.y - transform.position.y) < 3;
        public bool Ready => !Exhausted && Time.time >= ReadyAt;
        public bool Consume()
        {
            if (!Ready) return false;
            Stock.Used++;
            Stock.ReadyAt = Time.time + 25;
            FindObjectOfType<LunarHordeDirector>()?.ReachStation(stationKey);
            return true;
        }
        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || LunarPlayerRole.MenuOpen || LostInSpaceFinale.Active) return;
            Camera camera = Camera.main;
            if (camera == null) return;
            float distance = Vector3.Distance(camera.transform.position, transform.position);
            if (distance > 12f) return; // de lejos la guia el panel de la derecha; aqui solo se explica al llegar
            Vector3 position = camera.WorldToScreenPoint(transform.position + Vector3.up * 4f);
            if (position.z <= 0) return;
            LunarHud.Begin(out var previous);
            float scale = LunarHud.Scale;
            string prompt = Exhausted ? "AGOTADA: busca otra estacion" : Ready ? $"Sanar dentro del anillo  |  Curaciones: {RemainingUses}/{maximumUses}" : $"Disponible en {Mathf.CeilToInt(ReadyAt - Time.time)} s";
            bool inside = Contains(camera.transform.position);
            LunarHud.Prompt(position.x / scale, (Screen.height - position.y) / scale, inside && Ready ? "F" : null, $"Estacion medica  [{distance:0} m]", prompt);
            GUI.matrix = previous;
        }
    }
}

