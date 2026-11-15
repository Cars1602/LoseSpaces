using System.Collections.Generic;
using System.Linq;
using LunaCero.Net;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace LunaCero
{
    /// <summary>
    /// Partida cooperativa en la escena. El anfitrion (jugador 0) simula monstruos, hordas, objetos y
    /// piezas; cada jugador mueve a su propio astronauta y envia sus acciones:
    /// - posiciones de los astronautas (15/s) y de los monstruos (10/s), sin confirmacion;
    /// - recoger objetos, impactos a monstruos, dano recibido, soltar piezas: mensajes fiables.
    /// Los objetos del mapa se identifican por su orden (todos cargan la misma escena).
    /// </summary>
    public sealed class LunarCoop : MonoBehaviour
    {
        // Mensajes del anfitrion
        private const byte MsgAccept = 1, MsgPlayers = 2, MsgEnemies = 3, MsgRemoved = 4, MsgGrant = 5, MsgDeny = 6,
            MsgParts = 7, MsgHurt = 8, MsgEffect = 9, MsgHazard = 10, MsgProjectile = 11, MsgHud = 12, MsgKick = 13, MsgLobby = 14, MsgDropAck = 15, MsgVictory = 16;
        // Mensajes de los invitados
        private const byte MsgJoin = 20, MsgState = 21, MsgHit = 22, MsgStagger = 23, MsgUse = 24, MsgDrop = 25, MsgShot = 26;
        private const byte NoHolder = 255;

        private sealed class Remote
        {
            public int Id;
            public ReliableChannel Channel = new ReliableChannel();
            public bool Joined;
            public LunarPuppet Puppet;
            public byte Role = 255;
            public float ProtectedUntil;
            /// <summary>Ultimo estado recibido (se reenvia tal cual a los demas invitados).</summary>
            public byte[] LastState;
        }
        private const int StateSize = 35; // rol, posicion, giro, mirada, velocidad, banderas, vida, vida maxima

        private sealed class Replica
        {
            public LunarEnemy Enemy;
            public LunarAlienAnimation Animation;
            public LunarRiderAnimation Rider;
            public Vector3 Position;
            public Vector3 Velocity; // estimada entre paquetes: se predice el movimiento (sin tirones)
            public float Yaw;
            public float Seen;
            public int AttackCount;
            public bool Dead;
        }

        private LunarSession session;
        private HubClient Link => session != null ? session.Link : null;
        private bool host;
        private LunarPlayerCombat player;
        private LunarPlayerController controller;
        private LunarHealth health;
        private LunarPlayerStatus status;
        private LunarPlayerRole role;
        private Rigidbody body;
        private LunarHordeDirector director;
        private LunarRepairPoint repair;
        private LunarPickup[] pickups = new LunarPickup[0];
        private Transform[] pickupTransforms = new Transform[0];
        private LunarShipPart[] parts = new LunarShipPart[0];
        private readonly HashSet<ushort> removed = new HashSet<ushort>();
        private readonly Dictionary<ushort, int> holders = new Dictionary<ushort, int>();
        private readonly HashSet<ushort> pendingUse = new HashSet<ushort>();
        private readonly Dictionary<int, Remote> remotes = new Dictionary<int, Remote>();
        private readonly Dictionary<int, LunarPuppet> puppets = new Dictionary<int, LunarPuppet>();
        private readonly Dictionary<uint, Replica> replicas = new Dictionary<uint, Replica>();
        private readonly ReliableChannel hostChannel = new ReliableChannel();
        private uint nextEnemyId = 1;
        private float nextPlayers, nextEnemies, nextParts, nextHud, nextJoin;
        private uint stateSequence, partsSequence;
        private readonly Dictionary<ushort,uint> partsSeen=new Dictionary<ushort,uint>();
        private readonly HashSet<ushort> pendingDrops = new HashSet<ushort>();
        private bool accepted;
        private float lastShot;
        private Transform hiddenRoot;
        private float nextLobby;
        private int missionPlayers;
        private void ApplyMissionPlayers(int count)
        {
            count = Mathf.Clamp(count, 1, 4);
            if (missionPlayers == count) return;
            // Tras iniciar solo aumenta: una desconexion nunca resta piezas al objetivo.
            if (session.MatchStarted && missionPlayers > count) return;
            missionPlayers = count;
            if (repair != null) repair.SetMissionPlayers(count);
        }
        private readonly Dictionary<int, byte> lobbyRoles = new Dictionary<int, byte>();
        private GUIStyle lobbyTitle, lobbyText;

        private bool CanStartMatch => host && role != null && role.ReadyForMatch && Link.Peers.Count > 0 &&
            Link.Peers.Keys.All(id => id == Link.MyId || (remotes.TryGetValue(id, out var remote) && remote.Joined && remote.Role < LunarRoleDefinition.All.Length));

        private Packet LobbyPacket()
        {
            var packet = new Packet(MsgLobby).Bool(session.MatchStarted).Byte(Mathf.Max(1, missionPlayers)).Byte(Link.Peers.Count);
            foreach (int id in Link.Peers.Keys.OrderBy(id => id))
            {
                byte selected = id == Link.MyId ? (role != null && role.ReadyForMatch ? (byte)role.Role : (byte)255)
                    : remotes.TryGetValue(id, out var remote) && remote.Joined ? remote.Role : (byte)255;
                packet.Byte(id).Byte(selected);
            }
            return packet;
        }

        private void StartMatch()
        {
            if (!CanStartMatch || session.MatchStarted) return;
            session.ApplyMatchStarted(true);
            SendAll(LobbyPacket(), true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            player.Notify("Partida iniciada. Buena suerte, tripulacion!");
        }

        private void OnGUI()
        {
            if (session == null || Link == null || !LunarSession.WaitingForMatch || LunarPlayerRole.ChoosingRole || LunarMainMenu.ManualOpen) return;
            if (lobbyTitle == null)
            {
                lobbyTitle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                lobbyTitle.normal.textColor = Color.white;
                lobbyText = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = false };
                lobbyText.normal.textColor = Color.white;
            }
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            Color previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = Screen.width / scale, height = Screen.height / scale;
            GUI.color = new Color(0.015f, 0.025f, 0.045f, 0.97f);
            GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(width / 2f - 300, Mathf.Max(20, height / 2f - 235), 600, 470));
            GUILayout.Label("SALA DE ESPERA", lobbyTitle);
            GUILayout.Label(session.RoomName, lobbyTitle);
            GUILayout.Space(16);
            GUILayout.Label("Todos deben elegir su rol y terminar el manual antes de iniciar.", lobbyText);
            GUILayout.Label($"Objetivo: {(repair != null ? repair.requiredParts : 5)} piezas (5 por jugador).", lobbyText);
            GUILayout.Space(12);
            foreach (var peer in Link.Peers.OrderBy(p => p.Key))
            {
                byte selected = 255;
                if (peer.Key == Link.MyId && role != null && role.ReadyForMatch) selected = (byte)role.Role;
                else if (host && remotes.TryGetValue(peer.Key, out var remote) && remote.Joined) selected = remote.Role;
                else if (!host && lobbyRoles.TryGetValue(peer.Key, out byte value)) selected = value;
                string readiness = selected < LunarRoleDefinition.All.Length ? LunarRoleDefinition.All[selected].title + " - LISTO" : "Eligiendo rol / leyendo manual...";
                GUILayout.Label($"{peer.Value}{(peer.Key == 0 ? " (anfitrion)" : "")}\n{readiness}", lobbyText, GUILayout.Height(58));
            }
            GUILayout.FlexibleSpace();
            if (host)
            {
                GUI.enabled = CanStartMatch;
                if (GUILayout.Button("INICIAR PARTIDA", GUILayout.Height(42))) StartMatch();
                GUI.enabled = previousEnabled;
            }
            else GUILayout.Label(accepted ? "Esperando a que el anfitrion inicie la partida..." : "Sincronizando con el anfitrion...", lobbyText);
            if (GUILayout.Button("Salir de la sala", GUILayout.Height(30))) session.Leave("Saliste de la sala.");
            GUILayout.EndArea();
            GUI.color = previousColor;
            GUI.enabled = previousEnabled;
            GUI.matrix = previous;
        }

        private void Start()
        {
            session = LunarSession.Instance;
            host = LunarSession.IsHost;
            player = FindObjectOfType<LunarPlayerCombat>();
            director = FindObjectOfType<LunarHordeDirector>();
            repair = FindObjectOfType<LunarRepairPoint>();
            if (player == null || session == null || Link == null) { enabled = false; return; }
            controller = player.GetComponent<LunarPlayerController>();
            health = player.GetComponent<LunarHealth>();
            status = player.GetComponent<LunarPlayerStatus>();
            role = player.GetComponent<LunarPlayerRole>();
            body = player.GetComponent<Rigidbody>();
            hiddenRoot = new GameObject("Plantillas de red (inactivas)").transform;
            hiddenRoot.gameObject.SetActive(false);
            RegisterPickups();

            // Cada jugador aparece en un sitio distinto junto a la base.
            int id = Link.MyId;
            if (id > 0)
            {
                Vector3 side = player.transform.right * ((id % 2 == 1 ? 1.6f : -1.6f) * ((id + 1) / 2));
                controller.SetSpawn(player.transform.position + side);
            }

            LunarCombatEffects.Emitted += OnEffect;
            if (host)
            {
                LunarPickup.Consumed = OnHostConsumed;
                LunarPickup.Held = p => { if (p.NetId >= 0) holders[(ushort)p.NetId] = 0; };
                LunarPickup.Released = p => { if (p.NetId >= 0) holders.Remove((ushort)p.NetId); };
                LunarHazardZone.Created += OnHazard;
                LunarAlienProjectile.Fired += OnProjectile;
            }
            else
            {
                if (director != null) director.remote = true;
                if (repair != null) repair.remote = true;
                foreach (var enemy in LunarEnemy.All.ToArray()) Destroy(enemy.gameObject); // los monstruos los decide el anfitrion
                foreach (var p in pickups) if (p != null && p.kind == LunarPickupKind.Carryable) p.ForceKinematic = true;
                LunarPickup.RequestUse = RequestUse;
                LunarPickup.Released = OnClientReleased;
            }
        }

        private void OnDestroy()
        {
            LunarCombatEffects.Emitted -= OnEffect;
            LunarHazardZone.Created -= OnHazard;
            LunarAlienProjectile.Fired -= OnProjectile;
            LunarPickup.RequestUse = null;
            LunarPickup.Consumed = LunarPickup.Held = LunarPickup.Released = null;
        }

        /// <summary>Mismo orden en todas las PCs: nombre + posicion redondeada.</summary>
        private void RegisterPickups()
        {
            var all = FindObjectsOfType<LunarPickup>().Where(p => !p.IsHeld)
                .OrderBy(p => p.name, System.StringComparer.Ordinal)
                .ThenBy(p => Mathf.RoundToInt(p.transform.position.x * 10))
                .ThenBy(p => Mathf.RoundToInt(p.transform.position.z * 10))
                .ThenBy(p => Mathf.RoundToInt(p.transform.position.y * 10)).ToArray();
            pickups = all;
            pickupTransforms = all.Select(p => p.transform).ToArray();
            parts = all.Select(p => p.GetComponent<LunarShipPart>()).ToArray();
            for (int i = 0; i < all.Length; i++) all[i].NetId = i;
        }

        // ------------------------------------------------------------ envio
        private void SendTo(int target, Packet packet, bool reliable)
        {
            var data = packet.ToArray();
            if (host)
            {
                if (!remotes.TryGetValue(target, out var remote)) return;
                Link.Send(target, reliable ? remote.Channel.Wrap(data) : data);
            }
            else Link.Send(0, reliable ? hostChannel.Wrap(data) : data);
        }

        private void SendAll(Packet packet, bool reliable, int except = -1)
        {
            if (!host) return;
            if (!reliable && except < 0) { Link.Send(HubProtocol.Broadcast, packet.ToArray()); return; }
            foreach (var remote in remotes.Values) if (remote.Joined && remote.Id != except) SendTo(remote.Id, packet, reliable);
        }

        // ------------------------------------------------------------ bucle
        private void Update()
        {
            if (Link == null) return;
            if (host) ApplyMissionPlayers(Mathf.Max(1, Link.Peers.Count));
            while (Link.Received.Count > 0)
            {
                var (from, payload) = Link.Received.Dequeue();
                if (host)
                {
                    if (!remotes.TryGetValue(from, out var remote)) remotes[from] = remote = new Remote { Id = from };
                    var message = remote.Channel.Receive(payload, ack => Link.Send(from, ack));
                    if (message != null && message.Length > 0)
                    {
                        if (message[0] == MsgState && message.Length == StateSize + 1) remote.LastState = message;
                        HostReceive(remote, new Packet(message));
                    }
                }
                else if (from == 0)
                {
                    var message = hostChannel.Receive(payload, ack => Link.Send(0, ack));
                    if (message != null && message.Length > 0) ClientReceive(new Packet(message));
                }
            }
            if (host)
            {
                foreach (var remote in remotes.Values) { int id = remote.Id; remote.Channel.Resend(p => Link.Send(id, p)); }
                DropMissingRemotes();
                HostBroadcast();
            }
            else
            {
                hostChannel.Resend(p => Link.Send(0, p));
                ClientTick();
            }
            AnimateReplicas();
            FollowCarriedObjects();
        }

        private void DropMissingRemotes()
        {
            foreach (var remote in remotes.Values.ToArray())
            {
                if (Link.Peers.ContainsKey(remote.Id) || Link.Peers.Count == 0) continue;
                // Se fue: suelta lo que llevaba donde estaba su astronauta.
                foreach (var pair in holders.Where(h => h.Value == remote.Id).ToArray())
                {
                    holders.Remove(pair.Key);
                    var pickup = pickups[pair.Key];
                    if (pickup == null) continue;
                    pickup.SetRemoteHeld(false);
                    if (remote.Puppet != null) pickup.transform.position = remote.Puppet.transform.position + Vector3.up;
                }
                if (remote.Puppet != null) Destroy(remote.Puppet.gameObject);
                puppets.Remove(remote.Id);
                remotes.Remove(remote.Id);
            }
        }

        // ------------------------------------------------------------ anfitrion
        private void HostReceive(Remote remote, Packet packet)
        {
            if ((packet.Type == MsgHit || packet.Type == MsgStagger || packet.Type == MsgShot || packet.Type == MsgUse || packet.Type == MsgEffect) &&
                (!session.MatchStarted || !remote.Joined || remote.Role >= LunarRoleDefinition.All.Length)) return;
            switch (packet.Type)
            {
                case MsgJoin:
                {
                    string protocol = packet.ReadText();
                    if(protocol != LunarSession.GameProtocol) { SendTo(remote.Id,new Packet(MsgKick).Text("Version de partida incompatible. Actualiza ambos jugadores a " + LunarSession.GameProtocol),true); return; }
                    string scene = packet.ReadText();
                    if(packet.ReadText() != PickupSignature()) { SendTo(remote.Id,new Packet(MsgKick).Text("Los mapas u objetos no coinciden. Usa la misma compilacion en ambas PCs."),true); return; }
                    if (scene != SceneManager.GetActiveScene().name)
                    {
                        SendTo(remote.Id, new Packet(MsgKick).Text("La sala juega en otro mapa (" + SceneManager.GetActiveScene().name + ")."), true);
                        return;
                    }
                    bool first = !remote.Joined;
                    remote.Joined = true;
                    if (remote.Puppet == null) remote.Puppet = CreatePuppet(remote.Id, host: true);
                    remote.Puppet.Health.NetworkProtected = true;
                    if (!first) return;
                    var accept = new Packet(MsgAccept).UShort(removed.Count);
                    foreach (ushort id in removed) accept.UShort(id);
                    SendTo(remote.Id, accept, true);
                    SendTo(remote.Id, LobbyPacket(), true);
                    break;
                }
                case MsgState when remote.Joined && remote.Puppet != null:
                    ReadState(packet, remote.Puppet, out byte selectedRole);
                    if (remote.Role == 255 && selectedRole < LunarRoleDefinition.All.Length)
                    {
                        remote.Role = selectedRole;
                        remote.ProtectedUntil = Time.unscaledTime + 3f;
                    }
                    remote.Puppet.Health.NetworkProtected = !session.MatchStarted || remote.Role == 255 || Time.unscaledTime < remote.ProtectedUntil;
                    break;
                case MsgHit:
                {
                    uint id = packet.ReadUInt();
                    float amount = Mathf.Clamp(packet.ReadFloat(), 0f, 500f);
                    var enemy = LunarEnemy.All.FirstOrDefault(e => e != null && e.NetId == id);
                    if (enemy != null && !enemy.IsDead) enemy.GetComponent<LunarHealth>().Damage(amount);
                    break;
                }
                case MsgStagger:
                {
                    uint id = packet.ReadUInt();
                    float seconds = Mathf.Clamp(packet.ReadFloat(), 0f, 3f);
                    var enemy = LunarEnemy.All.FirstOrDefault(e => e != null && e.NetId == id);
                    if (enemy != null && !enemy.IsDead) enemy.Stagger(seconds);
                    break;
                }
                case MsgShot:
                    if (director != null) director.NotifyShot(packet.ReadVec());
                    break;
                case MsgUse:
                    HostUse(remote, packet.ReadUShort());
                    break;
                case MsgDrop:
                {
                    ushort id = packet.ReadUShort();
                    Vector3 position = packet.ReadVec();
                    Quaternion rotation = packet.ReadRot();
                    Vector3 velocity = Vector3.ClampMagnitude(packet.ReadVec(), 20f);
                    if (!holders.TryGetValue(id, out int holder) || holder != remote.Id) return;
                    holders.Remove(id);
                    var pickup = id < pickups.Length ? pickups[id] : null;
                    if (pickup == null) return;
                    pickup.transform.SetPositionAndRotation(position, rotation);
                    pickup.SetRemoteHeld(false);
                    if (pickup.Body != null) { pickup.Body.position=position; pickup.Body.rotation=rotation; if(!pickup.Body.isKinematic) pickup.Body.velocity = velocity; }
                    SendTo(remote.Id,new Packet(MsgDropAck).UShort(id).UInt(partsSequence),true);
                    nextParts=0;
                    break;
                }
                case MsgEffect:
                    // Rayo o explosion de un invitado: se ve en la PC del anfitrion y se reenvia al resto.
                    ApplyEffect(packet);
                    SendAll(new Packet(MsgEffect).Byte(effectKind).Vec(effectFrom).Vec(effectTo).Col(effectColor).Float(effectRadius), false, remote.Id);
                    break;
            }
        }

        private void HostUse(Remote remote, ushort id)
        {
            var pickup = id < pickups.Length ? pickups[id] : null;
            bool near = pickup != null && remote.Puppet != null && Vector3.Distance(remote.Puppet.transform.position, pickup.transform.position) < 6f;
            if (pickup == null || removed.Contains(id) || pickup.IsHeld || holders.ContainsKey(id) || !near || !pickup.IsShared)
            {
                SendTo(remote.Id, new Packet(MsgDeny).UShort(id), true);
                return;
            }
            if (pickup.kind == LunarPickupKind.Carryable)
            {
                holders[id] = remote.Id;
                pickup.SetRemoteHeld(true);
                SendTo(remote.Id, new Packet(MsgGrant).UShort(id), true);
                return;
            }
            removed.Add(id);
            SendTo(remote.Id, new Packet(MsgGrant).UShort(id), true);
            SendAll(new Packet(MsgRemoved).UShort(id), true, remote.Id);
            Destroy(pickup.gameObject);
        }

        private void OnHostConsumed(LunarPickup pickup)
        {
            if (pickup.NetId < 0) return;
            ushort id = (ushort)pickup.NetId;
            if (removed.Add(id)) SendAll(new Packet(MsgRemoved).UShort(id), true);
        }

        private void HostBroadcast()
        {
            float now = Time.unscaledTime;
            if (now >= nextLobby)
            {
                nextLobby = now + 0.5f;
                SendAll(LobbyPacket(), false);
            }
            if (remotes.Count == 0) return;
            if (now >= nextPlayers)
            {
                nextPlayers = now + 1f / 15f;
                var packet = new Packet(MsgPlayers);
                var joined = remotes.Values.Where(r => r.Joined && r.Puppet != null && r.LastState != null).ToList();
                packet.Byte(joined.Count + 1);
                WriteState(packet.Byte(0));
                foreach (var remote in joined) packet.Byte(remote.Id).Raw(remote.LastState, 1, StateSize);
                SendAll(packet, false);
            }
            if (now >= nextEnemies)
            {
                nextEnemies = now + 0.1f;
                var list = LunarEnemy.All.Where(e => e != null && e.NetPrefab >= 0).Take(50).ToList();
                var packet = new Packet(MsgEnemies).Byte(list.Count);
                foreach (var enemy in list)
                {
                    if (enemy.NetId == 0) enemy.NetId = nextEnemyId++;
                    var agent = enemy.GetComponent<NavMeshAgent>();
                    float speed = agent != null && agent.enabled && agent.isOnNavMesh ? agent.velocity.magnitude : 0f;
                    var hp = enemy.GetComponent<LunarHealth>();
                    enemy.TryGetComponent(out LunarAlienAnimation animation);
                    packet.UInt(enemy.NetId).Byte(enemy.NetPrefab).Vec(enemy.transform.position).Angle(enemy.transform.eulerAngles.y)
                        .Byte(Mathf.Clamp(Mathf.RoundToInt(speed * 20f), 0, 255)).Byte(Mathf.RoundToInt(100f * hp.Current / hp.Maximum))
                        .Byte(enemy.IsDead ? 1 : 0).Byte(animation != null ? animation.AttackCount & 255 : 0);
                }
                SendAll(packet, false);
            }
            if (now >= nextParts)
            {
                nextParts = now + 0.2f;
                var carried = new List<int>();
                for (int i = 0; i < pickups.Length; i++)
                    if (parts[i] != null || (pickups[i] != null && pickups[i].kind == LunarPickupKind.Carryable)) carried.Add(i);
                for(int offset=0;offset<carried.Count;offset+=30)
                {
                var batch=carried.Skip(offset).Take(30);
                var packet=new Packet(MsgParts).UInt(++partsSequence).Byte(Mathf.Max(1, missionPlayers)).Byte(System.Math.Min(30,carried.Count-offset));
                foreach (int i in batch)
                {
                    var t = pickupTransforms[i];
                    bool installed = parts[i] != null && parts[i].Installed;
                    int holder = holders.TryGetValue((ushort)i, out int h) ? h : NoHolder;
                    packet.UShort(i).Vec(t != null ? t.position : Vector3.zero).Rot(t != null ? t.rotation : Quaternion.identity).Byte(holder).Bool(installed).Bool(t == null);
                }
                SendAll(packet, false);
                }
            }
            if (now >= nextHud && director != null)
            {
                nextHud = now + 1f;
                SendAll(new Packet(MsgHud).UShort(director.Wave).UShort(director.Kills).Byte(director.ActiveEnemies).Float(director.NextWaveIn).Text(director.Announcement), false);
            }
        }

        private void OnHazard(Vector3 position, bool smoke, float radius) => SendAll(new Packet(MsgHazard).Vec(position).Bool(smoke).Float(radius), true);
        private void OnProjectile(LunarProjectileKind kind, Vector3 start, Vector3 velocity) => SendAll(new Packet(MsgProjectile).Byte((byte)kind).Vec(start).Vec(velocity), false);

        // ------------------------------------------------------------ invitado
        private void ClientTick()
        {
            float now = Time.unscaledTime;
            if (!accepted)
            {
                if (now >= nextJoin) { SendTo(0, new Packet(MsgJoin).Text(LunarSession.GameProtocol).Text(SceneManager.GetActiveScene().name).Text(PickupSignature()), true); nextJoin = now + 3f; }
                return;
            }
            if (now >= nextPlayers)
            {
                nextPlayers = now + 1f / 15f;
                SendTo(0, WriteState(new Packet(MsgState)), false);
            }
            // Pieza en las manos dentro del anillo de reparacion: se suelta alli (la instala el anfitrion).
            var held = player.HeldObject;
            if (held != null && repair != null && held.GetComponent<LunarShipPart>() != null)
            {
                Vector3 offset = player.transform.position - repair.transform.position;
                if (new Vector2(offset.x, offset.z).magnitude <= repair.radius * player.Role.repairRadius) player.Drop(false);
            }
            foreach (var pair in puppets.ToArray())
                if (pair.Value == null || now - pair.Value.LastUpdate > 4f) { if (pair.Value != null) Destroy(pair.Value.gameObject); puppets.Remove(pair.Key); }
        }

        public void BroadcastVictory(){if(host)SendAll(new Packet(MsgVictory),true);}
        private void ClientReceive(Packet packet)
        {
            switch (packet.Type)
            {
                case MsgVictory:
                    if(repair!=null)repair.GetComponent<LostInSpaceFinale>()?.Begin();
                    break;
                case MsgLobby:
                {
                    bool wasStarted = session.MatchStarted;
                    session.ApplyMatchStarted(packet.ReadBool());
                    ApplyMissionPlayers(packet.ReadByte());
                    lobbyRoles.Clear();
                    int count = packet.ReadByte();
                    for (int i = 0; i < count; i++) lobbyRoles[packet.ReadByte()] = packet.ReadByte();
                    if (!wasStarted && session.MatchStarted && role != null && role.ReadyForMatch)
                    {
                        Cursor.lockState = CursorLockMode.Locked;
                        Cursor.visible = false;
                        player.Notify("Partida iniciada. Buena suerte, tripulacion!");
                    }
                    break;
                }
                case MsgAccept:
                {
                    accepted = true;
                    int count = packet.ReadUShort();
                    for (int i = 0; i < count; i++) RemoveLocal(packet.ReadUShort());
                    player.Notify("Conectado a la sala. Busca a tu tripulacion!");
                    break;
                }
                case MsgKick:
                    session.Leave(packet.ReadText());
                    break;
                case MsgPlayers:
                {
                    int count = packet.ReadByte();
                    for (int i = 0; i < count; i++)
                    {
                        int id = packet.ReadByte();
                        if (id == Link.MyId) { ReadState(packet, null, out _); continue; }
                        if (!puppets.TryGetValue(id, out var puppet) || puppet == null) puppets[id] = puppet = CreatePuppet(id, host: false);
                        ReadState(packet, puppet, out _);
                    }
                    break;
                }
                case MsgEnemies:
                    ReadEnemies(packet);
                    break;
                case MsgRemoved:
                    RemoveLocal(packet.ReadUShort());
                    break;
                case MsgGrant:
                {
                    ushort id = packet.ReadUShort();
                    pendingUse.Remove(id);
                    var pickup = id < pickups.Length ? pickups[id] : null;
                    if (pickup == null) return;
                    pickup.Granted = true;
                    if (!pickup.Use(player))
                    {
                        pickup.Granted = false;
                        if (pickup.kind == LunarPickupKind.Carryable) SendTo(0, new Packet(MsgDrop).UShort(id).Vec(pickup.transform.position).Rot(pickup.transform.rotation).Vec(Vector3.zero), true);
                    }
                    break;
                }
                case MsgDeny:
                {
                    ushort id = packet.ReadUShort();
                    pendingUse.Remove(id);
                    player.Notify("Otro tripulante lo tomo primero.");
                    break;
                }
                case MsgDropAck:
                    ushort dropped=packet.ReadUShort();
                    pendingDrops.Remove(dropped);
                    partsSeen[dropped]=packet.ReadUInt();
                    break;
                case MsgParts:
                    ReadParts(packet);
                    break;
                case MsgHurt:
                    health.Damage(Mathf.Clamp(packet.ReadFloat(), 0f, 200f));
                    break;
                case MsgEffect:
                    ApplyEffect(packet);
                    break;
                case MsgHazard:
                    LunarHazardZone.Create(packet.ReadVec(), packet.ReadBool(), packet.ReadFloat());
                    break;
                case MsgProjectile:
                    LunarAlienProjectile.FireVisual((LunarProjectileKind)packet.ReadByte(), packet.ReadVec(), packet.ReadVec());
                    break;
                case MsgHud:
                    if (director != null) director.ApplyRemote(packet.ReadUShort(), packet.ReadUShort(), packet.ReadByte(), packet.ReadFloat(), packet.ReadText());
                    break;
            }
        }

        private bool RequestUse(LunarPickup pickup, LunarPlayerCombat user)
        {
            if (pickup.NetId < 0 || user != player) return false;
            ushort id = (ushort)pickup.NetId;
            if (pendingUse.Add(id)) SendTo(0, new Packet(MsgUse).UShort(id), true);
            return true;
        }

        private void OnClientReleased(LunarPickup pickup)
        {
            if (pickup.NetId < 0) return;
            pendingDrops.Add((ushort)pickup.NetId);
            SendTo(0, new Packet(MsgDrop).UShort(pickup.NetId).Vec(pickup.transform.position).Rot(pickup.transform.rotation).Vec(pickup.LastReleaseVelocity), true);
            pickup.ForceKinematic = true;
        }

        private void RemoveLocal(ushort id)
        {
            if (id < pickups.Length && pickups[id] != null && !pickups[id].IsHeld) Destroy(pickups[id].gameObject);
        }

        private void ReadParts(Packet packet)
        {
            uint sequence=packet.ReadUInt();
            ApplyMissionPlayers(packet.ReadByte());
            int count = packet.ReadByte();
            for (int n = 0; n < count; n++)
            {
                int id = packet.ReadUShort();
                Vector3 position = packet.ReadVec();
                Quaternion rotation = packet.ReadRot();
                int holder = packet.ReadByte();
                bool installed = packet.ReadBool();
                bool gone = packet.ReadBool();
                if (id >= pickups.Length || gone || pendingDrops.Contains((ushort)id) || (partsSeen.TryGetValue((ushort)id,out uint seen) && sequence<=seen)) continue;
                partsSeen[(ushort)id]=sequence;
                var part = parts[id];
                if (installed && part != null)
                {
                    if (!part.Installed && repair != null)
                    {
                        if (pickups[id] != null && pickups[id].IsHeld) player.Drop(false);
                        repair.ApplyInstalled(part, position, rotation);
                    }
                    continue;
                }
                var pickup = pickups[id];
                if (pickup == null || pendingDrops.Contains((ushort)id) || holder == Link.MyId || pickup.IsHeld) continue;
                if (holder != NoHolder) { holders[(ushort)id]=holder; if (!pickup.RemotelyHeld) pickup.SetRemoteHeld(true); continue; }
                holders.Remove((ushort)id);
                if (pickup.RemotelyHeld) pickup.SetRemoteHeld(false);
                var t = pickup.transform;
                if ((t.position - position).sqrMagnitude > 25f) t.SetPositionAndRotation(position, rotation);
                else t.SetPositionAndRotation(Vector3.Lerp(t.position, position, 0.6f), Quaternion.Slerp(t.rotation, rotation, 0.6f));
            }
        }

        private static readonly string[] weaponIds={"pistola","rifle","escopeta","riel","plasma","llave","tubo","extintor","barra","escombro"};
        public static byte WeaponCode(string id) { int index=System.Array.IndexOf(weaponIds,id); return index<0?(byte)255:(byte)index; }
        public static string WeaponId(byte code) => code<weaponIds.Length?weaponIds[code]:"";
        private string PickupSignature()
        {
            string text=string.Join("|",pickups.Select(p=>p.name+":"+(int)p.kind+":"+p.weaponId));
            using(var hash=System.Security.Cryptography.SHA256.Create()) return System.BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)));
        }
        private void FollowCarriedObjects()
        {
            foreach(var pair in holders)
            {
                if(pair.Value==Link.MyId || pair.Key>=pickups.Length) continue;
                var pickup=pickups[pair.Key];
                if(pickup==null || pickup.IsHeld || pendingDrops.Contains(pair.Key)) continue;
                if(puppets.TryGetValue(pair.Value,out var puppet) && puppet!=null) pickup.FollowRemote(puppet.transform.TransformPoint(new Vector3(-0.6f,1.05f,0.6f)),puppet.transform.rotation);
            }
        }
        // ------------------------------------------------------------ astronautas
        private Packet WriteState(Packet packet)
        {
            byte flags = (byte)((controller.IsGrounded ? 1 : 0) | (health.IsDead ? 2 : 0) | (status != null && status.FlashlightOn ? 4 : 0) | (Time.unscaledTime - lastShot < 0.2f ? 8 : 0));
            float yaw = controller.View != null ? controller.View.eulerAngles.y : player.transform.eulerAngles.y;
            float speed = new Vector2(body.velocity.x, body.velocity.z).magnitude;
            byte roleIndex = role != null && role.ReadyForMatch ? (byte)role.Role : (byte)255;
            return packet.UInt(++stateSequence).Byte(WeaponCode(player.CurrentWeapon?.Data.id)).Byte(roleIndex).Vec(player.transform.position).Angle(yaw).Angle(controller.Pitch).Float(speed).Byte(flags)
                .Float(health.Current).Float(health.Maximum);
        }

        private void ReadState(Packet packet, LunarPuppet puppet, out byte roleIndex)
        {
            uint sequence=packet.ReadUInt();
            byte weapon=packet.ReadByte();
            roleIndex = packet.ReadByte();
            Vector3 position = packet.ReadVec();
            float yaw = packet.ReadAngle(), pitch = packet.ReadAngle();
            if (pitch > 180f) pitch -= 360f;
            float speed = packet.ReadFloat();
            byte flags = packet.ReadByte();
            float hp = packet.ReadFloat(), max = packet.ReadFloat();
            if (puppet == null || !puppet.AcceptSequence(sequence)) return;
            puppet.SetWeapon(WeaponId(weapon));
            puppet.Apply(position, yaw, pitch, speed, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, (flags & 8) != 0, hp, max);
            if (roleIndex < LunarRoleDefinition.All.Length) puppet.RoleColor = LunarRoleDefinition.All[roleIndex].color;
            if (Link.Peers.TryGetValue(puppet.Id, out string name)) puppet.DisplayName = name + (roleIndex < LunarRoleDefinition.All.Length ? " (" + LunarRoleDefinition.All[roleIndex].title + ")" : "");
        }

        /// <summary>Copia del astronauta local sin controles, camara ni armas (se crea desactivada: no corre su Awake).</summary>
        private LunarPuppet CreatePuppet(int id, bool host)
        {
            var copy = Instantiate(player.gameObject, hiddenRoot);
            copy.name = "Astronauta remoto " + id;
            foreach (var pickup in copy.GetComponentsInChildren<LunarPickup>(true)) DestroyImmediate(pickup.gameObject);
            foreach (var camera in copy.GetComponentsInChildren<Camera>(true)) DestroyImmediate(camera.gameObject);
            foreach (var light in copy.GetComponentsInChildren<Light>(true)) DestroyImmediate(light);
            DestroyAll<LunarPlayerAnimation>(copy); DestroyAll<LunarPlayerCombat>(copy); DestroyAll<LunarViewModel>(copy);
            DestroyAll<LunarRadar>(copy); DestroyAll<LunarPlayerStatus>(copy); DestroyAll<LunarPlayerRole>(copy);
            DestroyAll<LunarPerformance>(copy); DestroyAll<LunarVitals>(copy); DestroyAll<LunarPlayerController>(copy);
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(behaviour is LunarHealth)) DestroyImmediate(behaviour);
            foreach (var listener in copy.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(listener);
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                // La copia puede crearse mientras el jugador local esta en primera persona y su
                // cuerpo esta oculto. El astronauta remoto siempre debe volver a ser visible.
                renderer.enabled = true;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            if (copy.TryGetComponent(out Rigidbody rigid)) { rigid.isKinematic = true; rigid.interpolation = RigidbodyInterpolation.None; }
            var puppet = copy.AddComponent<LunarPuppet>();
            puppet.Id = id;
            if (Link.Peers.TryGetValue(id, out string name)) puppet.DisplayName = name;
            copy.transform.SetParent(null, true);
            var hp = copy.GetComponent<LunarHealth>();
            // Anfitrion: el dano que los monstruos hacen a esta copia se envia a su jugador.
            if (hp != null) hp.Redirect = host ? (System.Action<float>)(amount => SendTo(id, new Packet(MsgHurt).Float(amount), true)) : (_ => { });
            puppets[id] = puppet;
            return puppet;
        }

        private static void DestroyAll<T>(GameObject root) where T : Component
        {
            foreach (var component in root.GetComponentsInChildren<T>(true)) DestroyImmediate(component);
        }

        // ------------------------------------------------------------ monstruos (copias en el invitado)
        private void ReadEnemies(Packet packet)
        {
            int count = packet.ReadByte();
            float now = Time.unscaledTime;
            for (int n = 0; n < count; n++)
            {
                uint id = packet.ReadUInt();
                int prefab = packet.ReadByte();
                Vector3 position = packet.ReadVec();
                float yaw = packet.ReadAngle();
                float speed = packet.ReadByte() / 20f;
                int hp = packet.ReadByte();
                bool dead = packet.ReadByte() == 1;
                int attacks = packet.ReadByte();
                if (!replicas.TryGetValue(id, out var replica))
                {
                    if (dead) continue;
                    replica = CreateReplica(id, prefab, position, yaw);
                    if (replica == null) continue;
                    replica.AttackCount = attacks;
                    replicas[id] = replica;
                }
                if (replica.Enemy == null) { replicas.Remove(id); continue; }
                float gap = now - replica.Seen;
                if (gap > 0.02f && gap < 0.5f)
                {
                    Vector3 measured = (position - replica.Position) / gap;
                    replica.Velocity = measured.sqrMagnitude > 400f ? Vector3.zero : Vector3.Lerp(replica.Velocity, measured, 0.6f);
                }
                else replica.Velocity = Vector3.zero;
                replica.Position = position;
                replica.Yaw = yaw;
                replica.Seen = now;
                var life = replica.Enemy.GetComponent<LunarHealth>();
                life.SetRemote(dead ? 0f : Mathf.Max(1f, hp), 100f);
                if (replica.Animation != null) replica.Animation.ExternalSpeed = speed;
                if (replica.Rider != null) replica.Rider.ExternalSpeed = speed;
                if (attacks != replica.AttackCount)
                {
                    replica.AttackCount = attacks;
                    if (replica.Animation != null && !dead) replica.Animation.Attack();
                }
                if (dead && !replica.Dead)
                {
                    replica.Dead = true;
                    replica.Enemy.ShowRemoteDeath();
                }
            }
        }

        private Replica CreateReplica(uint id, int prefabIndex, Vector3 position, float yaw)
        {
            var prefab = director != null ? director.PrefabAt(prefabIndex) : null;
            if (prefab == null) return null;
            var copy = Instantiate(prefab, position, Quaternion.Euler(0, yaw, 0), hiddenRoot);
            var enemy = copy.GetComponent<LunarEnemy>();
            enemy.enabled = false; // sin IA: la decide el anfitrion
            if (copy.TryGetComponent(out NavMeshAgent agent)) agent.enabled = false;
            copy.transform.SetParent(null, true);
            var life = copy.GetComponent<LunarHealth>();
            life.Redirect = amount => SendTo(0, new Packet(MsgHit).UInt(id).Float(amount), true);
            enemy.StaggerRedirect = seconds => SendTo(0, new Packet(MsgStagger).UInt(id).Float(seconds), true);
            return new Replica
            {
                Enemy = enemy, Position = position, Yaw = yaw, Seen = Time.unscaledTime,
                Animation = copy.GetComponent<LunarAlienAnimation>(), Rider = copy.GetComponent<LunarRiderAnimation>()
            };
        }

        private void AnimateReplicas()
        {
            if (host || replicas.Count == 0) return;
            float dt = Time.deltaTime, now = Time.unscaledTime;
            foreach (var pair in replicas.ToArray())
            {
                var replica = pair.Value;
                if (replica.Enemy == null) { replicas.Remove(pair.Key); continue; }
                if (!replica.Dead && now - replica.Seen > 1.5f) { Destroy(replica.Enemy.gameObject); replicas.Remove(pair.Key); continue; }
                if (replica.Dead) continue;
                var t = replica.Enemy.transform;
                // Prediccion: entre paquetes (10 por segundo) sigue avanzando con su ultima velocidad.
                Vector3 predicted = replica.Position + replica.Velocity * Mathf.Min(now - replica.Seen, 0.25f);
                if ((t.position - predicted).sqrMagnitude > 100f) t.position = predicted;
                else t.position = Vector3.Lerp(t.position, predicted, 1f - Mathf.Exp(-9f * dt));
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0, replica.Yaw, 0), 1f - Mathf.Exp(-8f * dt));
            }
        }

        // ------------------------------------------------------------ efectos
        private byte effectKind;
        private Vector3 effectFrom, effectTo;
        private Color effectColor;
        private float effectRadius;

        private void OnEffect(byte kind, Vector3 from, Vector3 to, Color color, float radius)
        {
            if (kind == 0 && Vector3.Distance(from, player.transform.position) < 3f)
            {
                lastShot = Time.unscaledTime;
                if (!host) SendTo(0, new Packet(MsgShot).Vec(player.transform.position), false);
            }
            var packet = new Packet(MsgEffect).Byte(kind).Vec(from).Vec(to).Col(color).Float(radius);
            if (host) SendAll(packet, false);
            else SendTo(0, packet, false);
        }

        private void ApplyEffect(Packet packet)
        {
            effectKind = packet.ReadByte();
            effectFrom = packet.ReadVec();
            effectTo = packet.ReadVec();
            effectColor = packet.ReadCol();
            effectRadius = Mathf.Clamp(packet.ReadFloat(), 0f, 12f);
            LunarCombatEffects.Muted = true;
            try
            {
                if (effectKind == 0) LunarCombatEffects.Beam(effectFrom, effectTo, effectColor);
                else LunarCombatEffects.Explosion(effectFrom, effectRadius, effectColor);
            }
            finally { LunarCombatEffects.Muted = false; }
        }
    }
}
