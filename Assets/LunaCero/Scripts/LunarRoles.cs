using System;
using UnityEngine;

namespace LunaCero
{
    public enum LunarRole { Piloto, Militar, Explorador, Medico, Ingeniero }

    /// <summary>Que aporta cada rol a la tripulacion. Todos pueden recoger armas y objetos contundentes.</summary>
    public sealed class LunarRoleDefinition
    {
        public LunarRole role;
        public string title;
        public string mission;
        public string[] perks;
        public Color color;
        public float maxHealth = 100f;
        public float damage = 1f;
        public float reloadTime = 1f;
        public float reserveAmmo = 1f;
        public float moveSpeed = 1f;
        public float stamina = 1f;
        public float oxygenDrain = 1f;
        public float fuel = 1f;
        public float fuelRegen = 1f;
        public float radarRange = 45f;
        public bool radarLoot;
        public float healPower = 1f;
        public float regenPerSecond;
        public float stationHealTime = 3f;
        public int medkits;
        public float iceOxygen = 1f;
        public float repairRadius = 1f;
        public float lockerCooldown = 1f;
        public bool partMarkers;
        public bool shipMarker;
        public string startWeapon;

        public static readonly LunarRoleDefinition[] All =
        {
            new LunarRoleDefinition
            {
                role = LunarRole.Piloto, title = "Piloto", color = new Color(0.35f, 0.75f, 1f),
                mission = "Guia a la tripulacion hasta la nave y la hace despegar.",
                perks = new[] { "Propulsor con 70% mas de combustible", "El propulsor se recarga 50% mas rapido", "Corre un poco mas rapido", "Ve la nave a cualquier distancia" },
                fuel = 1.7f, fuelRegen = 1.5f, moveSpeed = 1.05f, radarRange = 55f, shipMarker = true
            },
            new LunarRoleDefinition
            {
                role = LunarRole.Militar, title = "Militar", color = new Color(0.95f, 0.45f, 0.2f),
                mission = "Protege al grupo y contiene a las hordas y a los jefes.",
                perks = new[] { "Vida 125", "+25% de dano con todas las armas", "Recarga 25% mas rapido", "Empieza con rifle y mas municion" },
                maxHealth = 125f, damage = 1.25f, reloadTime = 0.75f, reserveAmmo = 1.6f, startWeapon = "rifle"
            },
            new LunarRoleDefinition
            {
                role = LunarRole.Explorador, title = "Explorador", color = new Color(0.4f, 0.95f, 0.45f),
                mission = "Recorre el mapa y encuentra armas, municion y piezas.",
                perks = new[] { "Radar de 85 m que muestra armas y municion", "Corre 18% mas rapido", "60% mas de energia", "Gasta 25% menos oxigeno" },
                moveSpeed = 1.18f, stamina = 1.6f, oxygenDrain = 0.75f, radarRange = 85f, radarLoot = true
            },
            new LunarRoleDefinition
            {
                role = LunarRole.Medico, title = "Medico", color = new Color(1f, 0.35f, 0.45f),
                mission = "Mantiene viva a la tripulacion.",
                perks = new[] { "Empieza con 4 botiquines que curan mas", "Regenera vida si no recibe dano", "Sana en estaciones en 1.5 s", "Usa botiquines en cualquier lugar (F)" },
                healPower = 1.6f, regenPerSecond = 1.5f, stationHealTime = 1.5f, medkits = 4
            },
            new LunarRoleDefinition
            {
                role = LunarRole.Ingeniero, title = "Ing. aeronautico", color = new Color(1f, 0.82f, 0.2f),
                mission = "Repara la nave: instala las piezas y mantiene el equipo.",
                perks = new[] { "Instala piezas desde el doble de distancia", "Ve la distancia a todas las piezas", "El hielo le da el doble de oxigeno", "Casillero de municion: la mitad de espera", "Empieza con una llave inglesa" },
                repairRadius = 2f, iceOxygen = 2f, lockerCooldown = 0.5f, partMarkers = true, startWeapon = "llave"
            }
        };

        public static LunarRoleDefinition Get(LunarRole role)
        {
            foreach (var definition in All) if (definition.role == role) return definition;
            return All[1];
        }
    }

    /// <summary>
    /// Rol del astronauta. Al empezar muestra la pantalla de eleccion (teclas 1-5 o clic);
    /// el rol se recuerda durante la sesion (al reiniciar tras morir no se vuelve a preguntar).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class LunarPlayerRole : MonoBehaviour
    {
        public static LunarRole? Chosen;
        private static bool menuOpen;
        /// <summary>Hay una pantalla a pantalla completa (roles o menu de sala): sin controles del juego.</summary>
        public static bool MenuOpen => menuOpen || LunarSession.Deciding;
        [SerializeField] private LunarRole role = LunarRole.Militar;
        [Tooltip("Mostrar la eleccion de rol al empezar la partida")]
        public bool askOnStart = true;
        public bool HasRole { get; private set; }
        public LunarRole Role => role;
        public LunarRoleDefinition Definition => LunarRoleDefinition.Get(role);
        public event Action<LunarRoleDefinition> Changed;
        private LunarHealth health;
        private GUIStyle title, body, small, perk;

        /// <summary>Definicion del rol de un astronauta (o la neutra si no tiene).</summary>
        public static LunarRoleDefinition Of(Component player)
        {
            if (player != null && player.TryGetComponent(out LunarPlayerRole component) && component.HasRole) return component.Definition;
            return Neutral;
        }
        private static readonly LunarRoleDefinition Neutral = new LunarRoleDefinition { title = "Sin rol", radarRange = 45f };

        private void Awake() { health = GetComponent<LunarHealth>(); }

        private void Start()
        {
            // Las pruebas automaticas juegan sin rol (valores neutros) para no alterar sus comprobaciones.
            bool automated = LunarAutomation.Running;
            if (automated) return;
            StartCoroutine(AskWhenReady());
        }

        // Primero se elige como jugar (solo / LAN / online) y despues el rol.
        private System.Collections.IEnumerator AskWhenReady()
        {
            while (LunarSession.Deciding) yield return null;
            if (Chosen.HasValue) Choose(Chosen.Value);
            else if (askOnStart) OpenMenu();
            else Choose(role);
        }

        public void OpenMenu()
        {
            menuOpen = true;
            if (!LunarSession.InMultiplayer) Time.timeScale = 0f; // en red el mundo sigue para los demas
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Choose(LunarRole value)
        {
            role = value;
            Chosen = value;
            HasRole = true;
            if (menuOpen)
            {
                menuOpen = false;
                Time.timeScale = 1f;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            Changed?.Invoke(Definition);
        }

        private void Update()
        {
            if (menuOpen)
            {
                for (int i = 0; i < LunarRoleDefinition.All.Length; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) { Choose(LunarRoleDefinition.All[i].role); break; }
                return;
            }
            // Medico: regeneracion lenta si lleva unos segundos sin recibir dano.
            var definition = Definition;
            if (HasRole && definition.regenPerSecond > 0 && health != null && !health.IsDead && Time.time - health.LastDamageTime > 6f)
                health.Heal(definition.regenPerSecond * Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (menuOpen) { menuOpen = false; Time.timeScale = 1f; }
        }

        private void OnGUI()
        {
            if (!menuOpen) return;
            EnsureStyles();
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = Screen.width / scale, height = Screen.height / scale;
            Fill(new Rect(0, 0, width, height), new Color(0.01f, 0.015f, 0.03f, 0.92f));
            GUI.Label(new Rect(0, 40, width, 40), "ELIGE TU ROL EN LA TRIPULACION", title);
            GUI.Label(new Rect(0, 84, width, 24), "Cada rol cumple una funcion. Todos pueden buscar y recoger armas y objetos contundentes.", body);

            var roles = LunarRoleDefinition.All;
            float cardWidth = Mathf.Min(228f, (width - 40f) / roles.Length - 12f);
            float total = roles.Length * (cardWidth + 12f) - 12f;
            float x = (width - total) / 2f, y = 130f, cardHeight = Mathf.Min(430f, height - 190f);
            for (int i = 0; i < roles.Length; i++)
            {
                var r = roles[i];
                var card = new Rect(x + i * (cardWidth + 12f), y, cardWidth, cardHeight);
                bool hover = card.Contains(Event.current.mousePosition);
                Fill(card, hover ? new Color(r.color.r * 0.25f, r.color.g * 0.25f, r.color.b * 0.25f, 0.95f) : new Color(0.06f, 0.07f, 0.1f, 0.95f));
                Fill(new Rect(card.x, card.y, card.width, 5), r.color);
                title.normal.textColor = r.color;
                GUI.Label(new Rect(card.x, card.y + 14, card.width, 30), r.title.ToUpperInvariant(), title);
                title.normal.textColor = Color.white;
                GUI.Label(new Rect(card.x + 12, card.y + 52, card.width - 24, 54), r.mission, small);
                float py = card.y + 112;
                foreach (var line in r.perks)
                {
                    GUI.Label(new Rect(card.x + 12, py, card.width - 24, 40), "- " + line, perk);
                    py += 38;
                }
                if (GUI.Button(new Rect(card.x + 12, card.yMax - 48, card.width - 24, 36), $"Elegir ({i + 1})")) Choose(r.role);
            }
            GUI.Label(new Rect(0, y + cardHeight + 16, width, 24), "Pulsa 1-5 o haz clic. Al morir puedes cambiar de rol con C.", body);
            GUI.matrix = previous;
        }

        private void EnsureStyles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = Color.white;
            body = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            body.normal.textColor = new Color(0.8f, 0.85f, 0.95f);
            small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, fontStyle = FontStyle.Italic };
            small.normal.textColor = new Color(0.85f, 0.88f, 0.95f);
            perk = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            perk.normal.textColor = new Color(0.95f, 0.95f, 0.95f);
        }

        private static void Fill(Rect area, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
