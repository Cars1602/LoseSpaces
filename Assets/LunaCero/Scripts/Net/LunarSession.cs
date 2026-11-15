using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using LunaCero.Net;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace LunaCero
{
    /// <summary>
    /// Como se juega: solo, sala LAN (el relevo va dentro del juego del anfitrion) o sala online
    /// (el relevo es un proceso en el servidor de AWS). Persiste entre escenas y muestra el menu inicial.
    /// Hasta 4 jugadores. El creador de la sala es el anfitrion: su PC simula monstruos y objetos.
    /// </summary>
    public sealed class LunarSession : MonoBehaviour
    {
        public enum Mode { Deciding, Solo, Host, Client }
        public const string Build = "losesspaces-4"; // Profile supported by the deployed relay API.
        public const string GameProtocol = "lost-in-space-9"; // Validated directly between players.
        public const string ApiHost = "107.23.18.43";
        // Huella SHA-256 del certificado del servidor (autofirmado): solo se habla con ese servidor.
        public const string PinnedCertificate = "74B41A83383B162623A74A8E3662941A9ED903DAC935CBE0082ED1CD318E8B44";

        public static bool OpenOnlineOnNextScene;
        public static LunarSession Instance { get; private set; }
        public static Mode Current => Instance != null ? Instance.mode : Mode.Solo;
        public static bool Deciding => Instance != null && Instance.mode == Mode.Deciding && Instance.gameScene;
        public static bool InMultiplayer => Current == Mode.Host || Current == Mode.Client;
        public static bool IsHost => Current == Mode.Host;
        public static bool IsClient => Current == Mode.Client;
        public static bool PauseMenuOpen => Instance != null && Instance.pauseMenuOpen;
        private bool pauseMenuOpen;
        private float previousTimeScale = 1f;

        private void TogglePauseMenu()
        {
            pauseMenuOpen = !pauseMenuOpen;
            if (!InMultiplayer)
            {
                if (pauseMenuOpen) { previousTimeScale = Time.timeScale; Time.timeScale = 0f; }
                else Time.timeScale = previousTimeScale;
            }
            Cursor.lockState = pauseMenuOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = pauseMenuOpen;
        }
        public bool MatchStarted { get; private set; }
        public static bool WaitingForMatch => InMultiplayer && Instance != null && !Instance.MatchStarted;
        public void ApplyMatchStarted(bool started) { if (started) MatchStarted = true; }

        private Mode mode = Mode.Deciding;
        private bool gameScene;
        public HubClient Link { get; private set; }
        private LanHub lanHub;
        private LanFinder finder;
        public bool Online { get; private set; }
        public string RoomName { get; private set; } = "";
        public string PlayerName { get; private set; } = "Astronauta";
        private string status = "";
        private string error = "";
        private string joinAddress = "";
        private enum Screen { Main, Lan, Online, Connecting }
        private Screen screen = Screen.Main;
        private readonly List<RoomInfo> onlineRooms = new List<RoomInfo>();
        private bool busy;
        private GUIStyle title, label, small;

        [Serializable] private sealed class RoomInfo { public string id; public string name; public int players; public bool started; public string host; public int port; public string ticket; public string error; }
        [Serializable] private sealed class RoomList { public RoomInfo[] rooms; public string error; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("Sesion Luna Cero (red)");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<LunarSession>();
            Instance.PlayerName = PlayerPrefs.GetString("LunaCero.Nombre", "Astronauta " + UnityEngine.Random.Range(10, 99));
            Instance.joinAddress = PlayerPrefs.GetString("LunaCero.IP", "");
            SceneManager.sceneLoaded += (scene, _) => Instance.OnScene();
            Instance.OnScene();
        }

        private void OnScene()
        {
            pauseMenuOpen = false;
            gameScene = FindObjectOfType<LunarPlayerCombat>() != null && FindObjectOfType<LunarHordeDirector>() != null;
            if(gameScene && OpenOnlineOnNextScene) { OpenOnlineOnNextScene=false; screen=Screen.Online; RefreshOnline(); }
            bool automated = LunarAutomation.Running;
            if (!gameScene || automated) { if (mode == Mode.Deciding && automated) mode = Mode.Solo; return; }
            if (mode == Mode.Deciding) { Time.timeScale = 0f; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else if (InMultiplayer && FindObjectOfType<LunarCoop>() == null) new GameObject("Cooperativo (red)").AddComponent<LunarCoop>();
        }

        private void Update()
        {
            if (gameScene && !LunarAutomation.Running && !LostInSpaceFinale.Active && Input.GetKeyDown(KeyCode.Escape) &&
                (pauseMenuOpen || !LunarPlayerRole.MenuOpen)) TogglePauseMenu();
            finder?.Update();
            lanHub?.Update();
            if (Link == null) return;
            Link.Update();
            if (mode == Mode.Deciding && screen == Screen.Connecting)
            {
                if (Link.Error != null) { Fail(Link.Error); return; }
                if (Link.Connected) Begin(Link.Owner ? Mode.Host : Mode.Client);
            }
            else if (InMultiplayer && (Link.Error != null || Link.Closed))
                Leave(Link.Closed ? "El anfitrion cerro la sala." : Link.Error);
        }

        private void Begin(Mode value)
        {
            MatchStarted = false;
            LunarPlayerRole.Chosen = null;
            mode = value;
            finder?.Dispose();
            finder = null;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (FindObjectOfType<LunarCoop>() == null) new GameObject("Cooperativo (red)").AddComponent<LunarCoop>();
        }

        public void ReturnToMainMenu()
        {
            Shutdown();mode=Mode.Deciding;MatchStarted=false;gameScene=false;screen=Screen.Main;
            LunarPlayerRole.Chosen=null;Time.timeScale=1;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            SceneManager.LoadScene("MenuPrincipal");
        }
        private void StartSolo()
        {
            FindObjectOfType<LunarRepairPoint>()?.SetMissionPlayers(1);
            mode = Mode.Solo;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>Sale de la sala y vuelve a empezar el mapa con el menu (el mundo se reinicia).</summary>
        public void Leave(string reason = "")
        {
            if(LostInSpaceFinale.Active){ReturnToMainMenu();return;}
            Shutdown();
            mode = Mode.Deciding;
            screen = Screen.Main;
            error = reason ?? "";
            LunarPlayerRole.Chosen = null;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void Shutdown()
        {
            if (pauseMenuOpen && !InMultiplayer) Time.timeScale = previousTimeScale;
            pauseMenuOpen = false;
            MatchStarted = false;
            Link?.Dispose(); Link = null;
            lanHub?.Dispose(); lanHub = null;
            finder?.Dispose(); finder = null;
            Online = false;
        }

        private void OnApplicationQuit() { Shutdown(); }

        private void Fail(string message)
        {
            Shutdown();
            error = message;
            screen = Online ? Screen.Online : Screen.Main;
            busy = false;
        }

        // ------------------------------------------------------------ LAN
        private void HostLan()
        {
            try
            {
                lanHub = new LanHub(PlayerName);
                RoomName = "Sala de " + PlayerName;
                Online = false;
                Link = new HubClient(new IPEndPoint(IPAddress.Loopback, lanHub.Port), "lan", PlayerName);
                screen = Screen.Connecting;
                status = "Creando la sala LAN...";
            }
            catch (Exception e) { Fail("No se pudo abrir el puerto UDP " + LanHub.DefaultPort + " (otra sala abierta en esta PC?): " + e.Message); }
        }

        private void JoinLan(IPEndPoint end, string name)
        {
            Online = false;
            RoomName = name;
            Link = new HubClient(end, "lan", PlayerName);
            screen = Screen.Connecting;
            status = "Conectando con " + end + "...";
        }

        // ------------------------------------------------------------ Online (AWS)
        private sealed class PinnedCertificateHandler : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] certificateData)
            {
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(certificateData)).Replace("-", "") == PinnedCertificate;
            }
        }

        private IEnumerator Api(string method, string path, string body, Action<string> done)
        {
            busy = true;
            using (var request = new UnityWebRequest("https://" + ApiHost + path, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.certificateHandler = new PinnedCertificateHandler();
                request.timeout = 25;
                yield return request.SendWebRequest();
                busy = false;
                string text = request.downloadHandler != null ? request.downloadHandler.text : "";
                if (request.result == UnityWebRequest.Result.ConnectionError || (request.responseCode >= 400 && string.IsNullOrEmpty(text)))
                { error = "Sin conexion con el servidor online: " + request.error; done(null); yield break; }
                if (request.responseCode >= 400)
                {
                    string message = null;
                    try { message = JsonUtility.FromJson<RoomInfo>(text)?.error; } catch { }
                    error = string.IsNullOrEmpty(message) ? "El servidor respondio " + request.responseCode : message;
                    done(null);
                    yield break;
                }
                done(text);
            }
        }

        private void RefreshOnline()
        {
            error = "";
            StartCoroutine(Api("GET", "/rooms?build=" + Build, null, text =>
            {
                onlineRooms.Clear();
                if (text == null) return;
                var list = JsonUtility.FromJson<RoomList>(text);
                if (list?.rooms != null) onlineRooms.AddRange(list.rooms);
                status = onlineRooms.Count == 0 ? "No hay salas online abiertas. Crea una." : "";
            }));
        }

        private void CreateOnline()
        {
            error = "";
            status = "Creando sala en el servidor...";
            string name = "Sala de " + PlayerName;
            StartCoroutine(Api("POST", "/rooms", JsonUtility.ToJson(new CreateBody { build = Build, name = name }), text =>
            {
                if (text == null) { status = ""; return; }
                ConnectOnline(JsonUtility.FromJson<RoomInfo>(text));
            }));
        }

        private void JoinOnline(RoomInfo room)
        {
            error = "";
            status = "Pidiendo entrada a " + room.name + "...";
            StartCoroutine(Api("POST", "/rooms/" + room.id + "/join", JsonUtility.ToJson(new CreateBody { build = Build }), text =>
            {
                if (text == null) { status = ""; return; }
                ConnectOnline(JsonUtility.FromJson<RoomInfo>(text));
            }));
        }

        [Serializable] private sealed class CreateBody { public string build; public string name; }

        private void ConnectOnline(RoomInfo room)
        {
            if (room == null || string.IsNullOrEmpty(room.ticket) || !IPAddress.TryParse(room.host, out var address)) { error = "Respuesta invalida del servidor."; return; }
            Online = true;
            RoomName = room.name;
            Link = new HubClient(new IPEndPoint(address, room.port), room.ticket, PlayerName);
            screen = Screen.Connecting;
            status = "Conectando con la sala online...";
        }

        // ------------------------------------------------------------ Menu
        private void OnGUI()
        {
            if (pauseMenuOpen) { PauseMenu(); return; }
            if (!Deciding) { if (InMultiplayer) RoomBadge(); return; }
            EnsureStyles();
            float scale = Mathf.Max(0.5f, Mathf.Min(UnityEngine.Screen.width / 1280f, UnityEngine.Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = UnityEngine.Screen.width / scale, height = UnityEngine.Screen.height / scale;
            Fill(new Rect(0, 0, width, height), new Color(0.01f, 0.015f, 0.03f, 0.9f));
            var panel = new Rect(width / 2 - 300, Mathf.Max(30, height / 2 - 270), 600, 540);
            Fill(panel, new Color(0.05f, 0.07f, 0.11f, 0.97f));
            GUILayout.BeginArea(new Rect(panel.x + 24, panel.y + 18, panel.width - 48, panel.height - 36));
            GUILayout.Label("LOST IN SPACE", title);
            GUILayout.Label("Tripulacion de 1 a 4 astronautas", label);
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Tu nombre:", label, GUILayout.Width(100));
            string edited = GUILayout.TextField(PlayerName, 20, GUILayout.Width(260));
            if (edited != PlayerName) { PlayerName = edited; PlayerPrefs.SetString("LunaCero.Nombre", PlayerName); }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUI.enabled = !busy;
            switch (screen)
            {
                case Screen.Main: MainScreen(); break;
                case Screen.Lan: LanScreen(); break;
                case Screen.Online: OnlineScreen(); break;
                case Screen.Connecting:
                    GUILayout.Label(status, label);
                    if (GUILayout.Button("Cancelar", GUILayout.Height(30))) { Shutdown(); screen = Screen.Main; }
                    break;
            }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(error)) { var red = new GUIStyle(small); red.normal.textColor = new Color(1f, 0.45f, 0.4f); GUILayout.Label(error, red); }
            else if (!string.IsNullOrEmpty(status) && screen != Screen.Connecting) GUILayout.Label(status, small);
            GUILayout.EndArea();
            GUI.matrix = previous;
        }

        private void PauseMenu()
        {
            EnsureStyles();
            float scale = Mathf.Max(0.5f, Mathf.Min(UnityEngine.Screen.width / 1280f, UnityEngine.Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = UnityEngine.Screen.width / scale, height = UnityEngine.Screen.height / scale;
            Fill(new Rect(0, 0, width, height), new Color(0.005f, 0.012f, 0.025f, 0.8f));
            Rect panel = new Rect(width / 2 - 230, height / 2 - 165, 460, 330);
            Fill(panel, new Color(0.025f, 0.055f, 0.085f, 0.98f));
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(panel.x + 24, panel.y + 20, panel.width - 48, panel.height - 40));
            GUILayout.Label("MENU DE PARTIDA", title);
            GUILayout.Space(18);
            if (GUILayout.Button("CONTINUAR (Esc)", GUILayout.Height(48))) TogglePauseMenu();
            GUILayout.Space(12);
            if (GUILayout.Button("VOLVER AL MENU PRINCIPAL", GUILayout.Height(48))) ReturnToMainMenu();
            GUILayout.Space(14);
            GUILayout.Label(IsHost ? "Al volver al menu se cierra tu partida para todos."
                : InMultiplayer ? "El mundo sigue activo. Volver al menu te desconecta de esta partida."
                : "La partida esta pausada. Volver al menu termina la expedicion actual.", small);
            GUILayout.EndArea();
            GUI.color = previousColor;
            GUI.matrix = previous;
        }

        private void MainScreen()
        {
            if (GUILayout.Button("JUGAR SOLO", GUILayout.Height(44))) StartSolo();
            GUILayout.Space(8);
            // El mundo procedural infinito no es igual en cada PC: el multijugador es para los mapas fijos.
            if (FindObjectOfType<LunarEndlessWorld>() != null)
            {
                GUILayout.Label("El multijugador esta disponible en el mapa grande (LunaCeroMapaGrande).", small);
                return;
            }
            if (GUILayout.Button("MULTIJUGADOR EN RED LOCAL (LAN)", GUILayout.Height(44))) { screen = Screen.Lan; error = ""; finder = new LanFinder(); }
            GUILayout.Space(8);
            if (GUILayout.Button("MULTIJUGADOR ONLINE (servidor AWS)", GUILayout.Height(44))) { screen = Screen.Online; RefreshOnline(); }
            GUILayout.Space(14);
            GUILayout.Label("El que crea la sala es el anfitrion: los monstruos y los objetos se simulan en su PC. " +
                            "Si el anfitrion sale, la sala se cierra. Todos deben tener la misma version del juego.", small);
        }

        private void LanScreen()
        {
            if (GUILayout.Button("CREAR SALA LAN", GUILayout.Height(38))) HostLan();
            GUILayout.Space(6);
            GUILayout.Label("Salas encontradas en tu red:", label);
            if (finder == null || finder.Rooms.Count == 0) GUILayout.Label("  Buscando... (el anfitrion debe tener la sala creada)", small);
            else foreach (var room in finder.Rooms)
                    if (GUILayout.Button($"Unirse a {room.Name}  ({room.Players}/4)  {room.End}", GUILayout.Height(30))) JoinLan(room.End, room.Name);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("O por IP:", label, GUILayout.Width(80));
            joinAddress = GUILayout.TextField(joinAddress, 40, GUILayout.Width(200));
            if (GUILayout.Button("Unirse", GUILayout.Width(90)))
            {
                PlayerPrefs.SetString("LunaCero.IP", joinAddress);
                string[] parts = joinAddress.Trim().Split(':');
                if (IPAddress.TryParse(parts[0], out var ip)) JoinLan(new IPEndPoint(ip, parts.Length > 1 && int.TryParse(parts[1], out int p) ? p : LanHub.DefaultPort), parts[0]);
                else error = "Escribe una IP valida (por ejemplo 192.168.1.20).";
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Windows puede preguntar si permites el acceso a la red: acepta en redes privadas.", small);
            if (GUILayout.Button("Volver", GUILayout.Height(28))) { finder?.Dispose(); finder = null; screen = Screen.Main; }
        }

        private void OnlineScreen()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("CREAR SALA ONLINE", GUILayout.Height(38))) CreateOnline();
            if (GUILayout.Button("Actualizar lista", GUILayout.Height(38), GUILayout.Width(150))) RefreshOnline();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.Label(busy ? "Consultando el servidor..." : "Salas online:", label);
            foreach (var room in onlineRooms)
                if (GUILayout.Button($"Unirse a {room.name}  ({room.players}/4)", GUILayout.Height(30))) JoinOnline(room);
            GUILayout.Space(6);
            if (GUILayout.Button("Volver", GUILayout.Height(28))) { screen = Screen.Main; error = ""; status = ""; }
        }

        private void RoomBadge()
        {
            if (LunarPlayerRole.MenuOpen || Link == null) return;
            EnsureStyles();
            float scale = Mathf.Max(0.5f, Mathf.Min(UnityEngine.Screen.width / 1280f, UnityEngine.Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = UnityEngine.Screen.width / scale;
            string where = Online ? "ONLINE" : "LAN" + (lanHub != null ? " " + string.Join(", ", LanHub.LocalAddresses()) : "");
            string names = string.Join(", ", Link.Peers.Values);
            string text = $"{(IsHost ? "ANFITRION" : "INVITADO")} | {where} | {Link.Peers.Count}/4: {names}   (F10 salir)";
            var rect = new Rect(width / 2 - 300, 4, 600, 22);
            Fill(rect, new Color(0, 0, 0, 0.45f));
            GUI.Label(rect, text, small);
            GUI.matrix = previous;
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F10) Leave("Saliste de la sala.");
        }

        private void EnsureStyles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = new Color(1f, 0.8f, 0.3f);
            label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            label.normal.textColor = new Color(0.88f, 0.92f, 1f);
            small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            small.normal.textColor = new Color(0.8f, 0.85f, 0.92f);
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
