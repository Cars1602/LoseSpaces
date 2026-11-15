using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace LunaCero.Net
{
    /// <summary>
    /// Protocolo del "relevo" (hub). Todos los jugadores se conectan a un relevo que reenvia los paquetes:
    /// en LAN el relevo va dentro del juego del anfitrion; online es relay.py en el servidor de AWS.
    /// El jugador 0 es el anfitrion: simula el mundo (monstruos, objetos, piezas) y los demas le envian
    /// sus acciones. Formato igual en C# y en Python (relay.py).
    /// </summary>
    public static class HubProtocol
    {
        public const byte Hello = 1, Welcome = 2, Reject = 3;
        public const byte Data = 0x10, Deliver = 0x11;
        public const byte Ping = 0x20, Pong = 0x21, Bye = 0x30;
        public const byte Peers = 0x40, Closed = 0x41;
        public const byte Broadcast = 255;
        public const int MaxPlayers = 4;
        public const int MaxPacket = 1400;
        public const float Timeout = 10f;
    }

    /// <summary>Socket UDP con un hilo de recepcion; los paquetes se procesan en el hilo principal.</summary>
    public sealed class UdpLink : IDisposable
    {
        private readonly UdpClient udp;
        private readonly Thread thread;
        private readonly ConcurrentQueue<(IPEndPoint from, byte[] data)> inbox = new ConcurrentQueue<(IPEndPoint, byte[])>();
        private volatile bool running = true;
        public int Port { get; }

        public UdpLink(int port, bool broadcast = false)
        {
            udp = new UdpClient(AddressFamily.InterNetwork);
            if (broadcast) udp.EnableBroadcast = true;
            // Windows: un ICMP "puerto inalcanzable" no debe romper la recepcion.
            try { udp.Client.IOControl(-1744830452, new byte[] { 0 }, null); } catch { }
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            Port = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
            thread = new Thread(Receive) { IsBackground = true, Name = "Luna UDP " + Port };
            thread.Start();
        }

        private void Receive()
        {
            while (running)
            {
                try
                {
                    IPEndPoint from = null;
                    byte[] data = udp.Receive(ref from);
                    if (data.Length > 0 && data.Length <= 4096) inbox.Enqueue((from, data));
                }
                catch (SocketException) { if (!running) break; }
                catch (ObjectDisposedException) { break; }
            }
        }

        public bool TryReceive(out IPEndPoint from, out byte[] data)
        {
            if (inbox.TryDequeue(out var item)) { from = item.from; data = item.data; return true; }
            from = null; data = null;
            return false;
        }

        public void Send(byte[] data, int length, IPEndPoint to)
        {
            try { udp.Send(data, length, to); } catch (SocketException) { } catch (ObjectDisposedException) { }
        }

        public void Send(byte[] data, IPEndPoint to) => Send(data, data.Length, to);

        public void Dispose()
        {
            running = false;
            try { udp.Close(); } catch { }
        }
    }

    /// <summary>Conexion de un jugador al relevo (LAN u online).</summary>
    public sealed class HubClient : IDisposable
    {
        private readonly UdpLink link;
        private readonly IPEndPoint hub;
        private readonly byte[] hello;
        private float nextHello, nextPing, lastHeard, startedAt;
        public int MyId { get; private set; } = -1;
        public bool Owner { get; private set; }
        public bool Connected => MyId >= 0;
        public string Error { get; private set; }
        public bool Closed { get; private set; }
        public readonly Dictionary<int, string> Peers = new Dictionary<int, string>();
        public readonly Queue<(int from, byte[] payload)> Received = new Queue<(int, byte[])>();
        public event Action PeersChanged;

        public HubClient(IPEndPoint hubAddress, string token, string playerName)
        {
            hub = hubAddress;
            link = new UdpLink(0);
            string text = (token ?? "lan") + "\n" + Clean(playerName);
            var bytes = Encoding.UTF8.GetBytes(text);
            hello = new byte[bytes.Length + 1];
            hello[0] = HubProtocol.Hello;
            Buffer.BlockCopy(bytes, 0, hello, 1, bytes.Length);
            startedAt = lastHeard = Time.unscaledTime;
        }

        public static string Clean(string name)
        {
            var builder = new StringBuilder();
            foreach (char c in name ?? "") if (!char.IsControl(c) && c != '\n' && builder.Length < 20) builder.Append(c);
            return builder.Length > 0 ? builder.ToString() : "Astronauta";
        }

        public void Update()
        {
            float now = Time.unscaledTime;
            if (Closed || Error != null) return;
            if (!Connected)
            {
                if (now >= nextHello) { link.Send(hello, hub); nextHello = now + 0.5f; }
                if (now - startedAt > 8f) Error = "No responde la sala (" + hub + "). Revisa la IP, el firewall o que siga abierta.";
            }
            else
            {
                if (now >= nextPing) { link.Send(new[] { HubProtocol.Ping }, hub); nextPing = now + 1f; }
                if (now - lastHeard > HubProtocol.Timeout) Error = "Se perdio la conexion con la sala.";
            }
            while (link.TryReceive(out var from, out var data))
            {
                if (!from.Address.Equals(hub.Address) || from.Port != hub.Port) continue;
                lastHeard = now;
                switch (data[0])
                {
                    case HubProtocol.Welcome when data.Length >= 3:
                        MyId = data[1];
                        Owner = data[2] == 1;
                        break;
                    case HubProtocol.Reject:
                        Error = Encoding.UTF8.GetString(data, 1, data.Length - 1);
                        break;
                    case HubProtocol.Deliver when data.Length >= 2 && Connected:
                        var payload = new byte[data.Length - 2];
                        Buffer.BlockCopy(data, 2, payload, 0, payload.Length);
                        Received.Enqueue((data[1], payload));
                        break;
                    case HubProtocol.Peers:
                        ReadPeers(data);
                        break;
                    case HubProtocol.Closed:
                        Closed = true;
                        break;
                }
            }
        }

        private void ReadPeers(byte[] data)
        {
            Peers.Clear();
            int count = data.Length > 1 ? data[1] : 0, i = 2;
            for (int n = 0; n < count && i + 2 <= data.Length; n++)
            {
                int id = data[i++], length = data[i++];
                if (i + length > data.Length) break;
                Peers[id] = Encoding.UTF8.GetString(data, i, length);
                i += length;
            }
            PeersChanged?.Invoke();
        }

        /// <summary>Envia datos a un jugador (o a todos con Broadcast). Los clientes solo pueden enviar al anfitrion (0).</summary>
        public void Send(int target, byte[] payload)
        {
            if (!Connected || payload.Length > HubProtocol.MaxPacket) return;
            var packet = new byte[payload.Length + 2];
            packet[0] = HubProtocol.Data;
            packet[1] = (byte)target;
            Buffer.BlockCopy(payload, 0, packet, 2, payload.Length);
            link.Send(packet, hub);
        }

        public void Dispose()
        {
            if (Connected) { var bye = new[] { HubProtocol.Bye }; link.Send(bye, hub); link.Send(bye, hub); }
            link.Dispose();
        }
    }

    /// <summary>
    /// Relevo LAN dentro del juego del anfitrion (misma logica que relay.py en AWS) y respuesta
    /// a las busquedas de salas en la red local.
    /// </summary>
    public sealed class LanHub : IDisposable
    {
        public const int DefaultPort = 7778, DiscoveryPort = 47778;
        public const string Query = "LUNACERO4?", Answer = "LUNACERO4!";
        private sealed class Peer { public int Id; public IPEndPoint End; public string Name; public float LastSeen; }
        private readonly UdpLink link;
        private readonly UdpLink discovery;
        private readonly Dictionary<int, Peer> peers = new Dictionary<int, Peer>();
        public string RoomName { get; }
        public int Port => link.Port;
        public int Count => peers.Count;

        public LanHub(string roomName, int port = DefaultPort)
        {
            RoomName = HubClient.Clean(roomName);
            link = new UdpLink(port);
            try { discovery = new UdpLink(DiscoveryPort, true); }
            catch (SocketException) { Debug.LogWarning("Otro programa usa el puerto de busqueda LAN " + DiscoveryPort + "; la sala no aparecera en la lista (unete por IP)."); }
        }

        public void Update()
        {
            float now = Time.unscaledTime;
            while (link.TryReceive(out var from, out var data)) Handle(from, data, now);
            if (discovery != null)
                while (discovery.TryReceive(out var from, out var data))
                    if (Encoding.ASCII.GetString(data) == Query)
                        discovery.Send(Encoding.UTF8.GetBytes($"{Answer}|{Port}|{peers.Count}|{RoomName}"), from);
            var silent = new List<int>();
            foreach (var peer in peers.Values) if (now - peer.LastSeen > HubProtocol.Timeout) silent.Add(peer.Id);
            foreach (int id in silent) Remove(id);
        }

        private Peer Find(IPEndPoint end)
        {
            foreach (var peer in peers.Values) if (peer.End.Equals(end)) return peer;
            return null;
        }

        private void Handle(IPEndPoint from, byte[] data, float now)
        {
            var sender = Find(from);
            if (sender != null) sender.LastSeen = now;
            switch (data[0])
            {
                case HubProtocol.Hello:
                    if (sender != null) { SendWelcome(sender); return; }
                    string[] parts = Encoding.UTF8.GetString(data, 1, data.Length - 1).Split('\n');
                    bool owner = IPAddress.IsLoopback(from.Address) && !peers.ContainsKey(0);
                    if (!owner && !peers.ContainsKey(0)) { Reject(from, "La sala aun no esta lista."); return; }
                    if (peers.Count >= HubProtocol.MaxPlayers) { Reject(from, "La sala esta llena (4 jugadores)."); return; }
                    int id = 0;
                    if (!owner) for (id = 1; peers.ContainsKey(id); id++) { }
                    var peer = new Peer { Id = id, End = from, Name = HubClient.Clean(parts.Length > 1 ? parts[1] : ""), LastSeen = now };
                    peers[id] = peer;
                    SendWelcome(peer);
                    SendPeers();
                    break;
                case HubProtocol.Data when sender != null && data.Length >= 2:
                    Forward(sender, data[1], data);
                    break;
                case HubProtocol.Ping when sender != null:
                    link.Send(new[] { HubProtocol.Pong }, from);
                    break;
                case HubProtocol.Bye when sender != null:
                    Remove(sender.Id);
                    break;
            }
        }

        private void Forward(Peer sender, int target, byte[] data)
        {
            var packet = new byte[data.Length];
            Buffer.BlockCopy(data, 0, packet, 0, data.Length);
            packet[0] = HubProtocol.Deliver;
            packet[1] = (byte)sender.Id;
            if (sender.Id != 0) target = 0; // los clientes solo hablan con el anfitrion
            if (target == HubProtocol.Broadcast)
            {
                foreach (var peer in peers.Values) if (peer.Id != sender.Id) link.Send(packet, peer.End);
            }
            else if (peers.TryGetValue(target, out var to)) link.Send(packet, to.End);
        }

        private void SendWelcome(Peer peer) => link.Send(new[] { HubProtocol.Welcome, (byte)peer.Id, (byte)(peer.Id == 0 ? 1 : 0) }, peer.End);

        private void Reject(IPEndPoint to, string reason)
        {
            var text = Encoding.UTF8.GetBytes(reason);
            var packet = new byte[text.Length + 1];
            packet[0] = HubProtocol.Reject;
            Buffer.BlockCopy(text, 0, packet, 1, text.Length);
            link.Send(packet, to);
        }

        private void Remove(int id)
        {
            if (!peers.Remove(id)) return;
            if (id == 0) { foreach (var peer in peers.Values) link.Send(new[] { HubProtocol.Closed }, peer.End); peers.Clear(); return; }
            SendPeers();
        }

        private void SendPeers()
        {
            var stream = new MemoryStream();
            stream.WriteByte(HubProtocol.Peers);
            stream.WriteByte((byte)peers.Count);
            foreach (var peer in peers.Values)
            {
                var name = Encoding.UTF8.GetBytes(peer.Name);
                stream.WriteByte((byte)peer.Id);
                stream.WriteByte((byte)name.Length);
                stream.Write(name, 0, name.Length);
            }
            var packet = stream.ToArray();
            foreach (var peer in peers.Values) link.Send(packet, peer.End);
        }

        /// <summary>IPs IPv4 de esta PC en la red local (para decirselas a los demas).</summary>
        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var address in nic.GetIPProperties().UnicastAddresses)
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(address.Address.ToString());
                }
            }
            catch { }
            return list;
        }

        public void Dispose()
        {
            foreach (var peer in peers.Values) link.Send(new[] { HubProtocol.Closed }, peer.End);
            link.Dispose();
            discovery?.Dispose();
        }
    }

    /// <summary>Busca salas LAN enviando una consulta por difusion y recogiendo las respuestas.</summary>
    public sealed class LanFinder : IDisposable
    {
        public sealed class Room { public string Name; public IPEndPoint End; public int Players; public float Seen; }
        private readonly UdpLink link = new UdpLink(0, true);
        public readonly List<Room> Rooms = new List<Room>();
        private float nextQuery;

        public void Update()
        {
            float now = Time.unscaledTime;
            if (now >= nextQuery)
            {
                nextQuery = now + 1.5f;
                var query = Encoding.ASCII.GetBytes(LanHub.Query);
                link.Send(query, new IPEndPoint(IPAddress.Broadcast, LanHub.DiscoveryPort));
                link.Send(query, new IPEndPoint(IPAddress.Loopback, LanHub.DiscoveryPort));
                foreach (var subnet in SubnetBroadcasts()) link.Send(query, new IPEndPoint(subnet, LanHub.DiscoveryPort));
            }
            while (link.TryReceive(out var from, out var data))
            {
                string[] parts = Encoding.UTF8.GetString(data).Split(new[] { '|' }, 4);
                if (parts.Length < 4 || parts[0] != LanHub.Answer || !int.TryParse(parts[1], out int port) || !int.TryParse(parts[2], out int players)) continue;
                // La IP sale del remitente de la respuesta (no de lo que diga el paquete).
                var end = new IPEndPoint(IPAddress.IsLoopback(from.Address) ? IPAddress.Loopback : from.Address, port);
                var room = Rooms.Find(r => r.End.Equals(end));
                if (room == null) Rooms.Add(room = new Room { End = end });
                room.Name = HubClient.Clean(parts[3]);
                room.Players = players;
                room.Seen = now;
            }
            Rooms.RemoveAll(r => now - r.Seen > 5f);
        }

        private static IEnumerable<IPAddress> SubnetBroadcasts()
        {
            var list = new List<IPAddress>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null) continue;
                        byte[] ip = unicast.Address.GetAddressBytes(), mask = unicast.IPv4Mask.GetAddressBytes();
                        for (int i = 0; i < 4; i++) ip[i] = (byte)(ip[i] | ~mask[i]);
                        list.Add(new IPAddress(ip));
                    }
                }
            }
            catch { }
            return list;
        }

        public void Dispose() => link.Dispose();
    }

    /// <summary>
    /// Mensajes fiables sobre UDP (recogidas, impactos, piezas): se reenvian hasta recibir confirmacion
    /// y se descartan duplicados. Los estados frecuentes (posiciones) van sin esta capa.
    /// </summary>
    public sealed class ReliableChannel
    {
        public const byte Wrapped = 0xF0, Ack = 0xF1;
        private ushort nextSequence = 1;
        private readonly Dictionary<ushort, (byte[] packet, float sent)> pending = new Dictionary<ushort, (byte[], float)>();
        private readonly HashSet<ushort> seen = new HashSet<ushort>();
        private readonly Queue<ushort> seenOrder = new Queue<ushort>();

        public byte[] Wrap(byte[] message)
        {
            ushort sequence = nextSequence++;
            if (nextSequence == 0) nextSequence = 1;
            var packet = new byte[message.Length + 3];
            packet[0] = Wrapped;
            packet[1] = (byte)sequence;
            packet[2] = (byte)(sequence >> 8);
            Buffer.BlockCopy(message, 0, packet, 3, message.Length);
            pending[sequence] = (packet, Time.unscaledTime);
            return packet;
        }

        public void Resend(Action<byte[]> send)
        {
            float now = Time.unscaledTime;
            var due = new List<ushort>();
            foreach (var pair in pending) if (now - pair.Value.sent > 0.25f) due.Add(pair.Key);
            foreach (ushort sequence in due)
            {
                var item = pending[sequence];
                pending[sequence] = (item.packet, now);
                send(item.packet);
            }
        }

        /// <summary>Devuelve el mensaje a procesar (o null si era una confirmacion o un duplicado).</summary>
        public byte[] Receive(byte[] payload, Action<byte[]> send)
        {
            if (payload.Length >= 3 && payload[0] == Ack)
            {
                pending.Remove((ushort)(payload[1] | payload[2] << 8));
                return null;
            }
            if (payload.Length < 3 || payload[0] != Wrapped) return payload;
            ushort sequence = (ushort)(payload[1] | payload[2] << 8);
            send(new[] { Ack, payload[1], payload[2] });
            if (!seen.Add(sequence)) return null;
            seenOrder.Enqueue(sequence);
            if (seenOrder.Count > 2048) seen.Remove(seenOrder.Dequeue());
            var message = new byte[payload.Length - 3];
            Buffer.BlockCopy(payload, 3, message, 0, message.Length);
            return message;
        }
    }

    /// <summary>Escritura y lectura binaria compacta de los mensajes del juego.</summary>
    public sealed class Packet
    {
        private readonly MemoryStream stream;
        private readonly BinaryWriter writer;
        private readonly BinaryReader reader;

        public Packet(byte type) { stream = new MemoryStream(); writer = new BinaryWriter(stream); writer.Write(type); }
        public Packet(byte[] data) { stream = new MemoryStream(data); reader = new BinaryReader(stream); Type = reader.ReadByte(); }
        public byte Type { get; }
        public bool More => stream.Position < stream.Length;
        public byte[] ToArray() => stream.ToArray();
        public int Length => (int)stream.Length;

        public Packet Raw(byte[] data, int offset, int count) { writer.Write(data, offset, count); return this; }
        public Packet Byte(int v) { writer.Write((byte)v); return this; }
        public Packet Short(int v) { writer.Write((short)v); return this; }
        public Packet UShort(int v) { writer.Write((ushort)v); return this; }
        public Packet Int(int v) { writer.Write(v); return this; }
        public Packet UInt(uint v) { writer.Write(v); return this; }
        public Packet Float(float v) { writer.Write(v); return this; }
        public Packet Bool(bool v) { writer.Write(v); return this; }
        public Packet Text(string v) { writer.Write(v ?? ""); return this; }
        public Packet Vec(Vector3 v) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); return this; }
        public Packet Rot(Quaternion q) { writer.Write(q.x); writer.Write(q.y); writer.Write(q.z); writer.Write(q.w); return this; }
        public Packet Col(Color c) { writer.Write((byte)(Mathf.Clamp01(c.r) * 255)); writer.Write((byte)(Mathf.Clamp01(c.g) * 255)); writer.Write((byte)(Mathf.Clamp01(c.b) * 255)); return this; }
        public Packet Angle(float degrees) { writer.Write((ushort)(Mathf.Repeat(degrees, 360f) / 360f * 65535f)); return this; }

        public byte ReadByte() => reader.ReadByte();
        public short ReadShort() => reader.ReadInt16();
        public ushort ReadUShort() => reader.ReadUInt16();
        public int ReadInt() => reader.ReadInt32();
        public uint ReadUInt() => reader.ReadUInt32();
        public float ReadFloat() => Finite(reader.ReadSingle());
        public bool ReadBool() => reader.ReadBoolean();
        public string ReadText() => reader.ReadString();
        public Vector3 ReadVec() => new Vector3(ReadFloat(), ReadFloat(), ReadFloat());
        public Quaternion ReadRot()
        {
            var q = new Quaternion(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 0.01f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }
        public Color ReadCol() => new Color(reader.ReadByte() / 255f, reader.ReadByte() / 255f, reader.ReadByte() / 255f);
        public float ReadAngle() => reader.ReadUInt16() / 65535f * 360f;
        private static float Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : Mathf.Clamp(v, -100000f, 100000f);
    }
}
