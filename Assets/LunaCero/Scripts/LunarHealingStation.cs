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
            Camera camera = Camera.main;
            if (camera == null) return;
            float distance = Vector3.Distance(camera.transform.position, transform.position);
            if (distance > 100) return;
            Vector3 position = camera.WorldToScreenPoint(transform.position + Vector3.up * 4f);
            if (position.z <= 0) return;
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            string prompt = Exhausted ? "AGOTADA: busca otra estacion" : Ready ? "F: sanar dentro del anillo" : $"Disponible en {Mathf.CeilToInt(ReadyAt - Time.time)} s";
            GUI.Box(new Rect(position.x / scale - 115, (Screen.height - position.y) / scale, 230, 65), $"ESTACION MEDICA [{distance:0} m]\nCuraciones: {RemainingUses}/{maximumUses}\n{prompt}");
            GUI.matrix = previous;
        }
    }
}

