#!/usr/bin/python3
"""Relevo UDP de una sala online de Luna Cero (build losesspaces-4).

Lo lanza lobby.py (servicio cubos-online) como si fuera el binario del juego:
  relay.py -batchmode -nographics --lunar-relay --room ID --port P --name-b64 N --status FILE -logFile LOG
El secreto de la sala llega en la variable CUBOS_ROOM_SECRET y sirve para comprobar los tickets
(id|creador|vence|nonce|firma HMAC-SHA256) que entrega la API al crear o unirse.

No simula el juego: el creador de la sala (jugador 0) es el anfitrion y este proceso solo reenvia
paquetes entre los jugadores (maximo 4). Mismo protocolo que LanHub en LunarNet.cs.
"""
import hashlib, hmac, json, os, socket, sys, time

HELLO, WELCOME, REJECT = 1, 2, 3
DATA, DELIVER = 0x10, 0x11
PING, PONG, BYE = 0x20, 0x21, 0x30
PEERS, CLOSED = 0x40, 0x41
BROADCAST, MAX_PLAYERS, TIMEOUT = 255, 4, 10.0


def arg(name, default=None):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv[:-1] else default


ROOM = arg('--room', '')
PORT = int(arg('--port', '7800'))
STATUS = arg('--status')
LOG = arg('-logFile')
SECRET = os.environ.get('CUBOS_ROOM_SECRET', '').encode()


def log(text):
    line = time.strftime('%H:%M:%S ') + text
    print(line, flush=True)
    if LOG:
        try:
            with open(LOG, 'a', encoding='utf-8') as f: f.write(line + '\n')
        except OSError: pass


def check_ticket(ticket, used):
    """Devuelve (es_creador, nonce) o lanza ValueError."""
    parts = ticket.split('|')
    if len(parts) != 5: raise ValueError('Ticket invalido.')
    room, owner, expires, nonce, signature = parts
    text = '|'.join(parts[:4])
    expected = hmac.new(SECRET, text.encode(), hashlib.sha256).hexdigest()
    if not SECRET or not hmac.compare_digest(expected, signature): raise ValueError('Ticket no valido para esta sala.')
    if room != ROOM: raise ValueError('El ticket es de otra sala.')
    if int(expires) < time.time(): raise ValueError('El ticket vencio. Vuelve a unirte.')
    if nonce in used: raise ValueError('Ticket ya usado.')
    return owner == '1', nonce


class Relay:
    def __init__(self):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(('0.0.0.0', PORT))
        self.sock.settimeout(0.5)
        self.peers = {}        # id -> dict(addr, name, seen)
        self.by_addr = {}      # addr -> id
        self.used = set()
        self.rates = {}        # addr -> [ventana, paquetes]
        self.started = time.time()
        self.last_people = time.time()
        self.closed = False
        self.next_status = 0

    def send(self, data, addr):
        try: self.sock.sendto(data, addr)
        except OSError: pass

    def reject(self, addr, reason):
        self.send(bytes([REJECT]) + reason.encode()[:200], addr)

    def welcome(self, pid):
        self.send(bytes([WELCOME, pid, 1 if pid == 0 else 0]), self.peers[pid]['addr'])

    def broadcast_peers(self):
        body = bytearray([PEERS, len(self.peers)])
        for pid, peer in sorted(self.peers.items()):
            name = peer['name'].encode()[:40]
            body += bytes([pid, len(name)]) + name
        for peer in self.peers.values(): self.send(bytes(body), peer['addr'])

    def remove(self, pid, why):
        peer = self.peers.pop(pid, None)
        if not peer: return
        self.by_addr.pop(peer['addr'], None)
        log(f'sale {pid} ({why})')
        if pid == 0:
            for other in self.peers.values(): self.send(bytes([CLOSED]), other['addr'])
            self.closed = True
        else:
            self.broadcast_peers()

    def limited(self, addr, now):
        window = self.rates.setdefault(addr, [now, 0])
        if now - window[0] > 1: window[0], window[1] = now, 0
        window[1] += 1
        if len(self.rates) > 256: self.rates = {a: w for a, w in self.rates.items() if now - w[0] < 5}
        return window[1] > 900

    def handle(self, data, addr, now):
        if not data or len(data) > 1500 or self.limited(addr, now): return
        pid = self.by_addr.get(addr)
        if pid is not None: self.peers[pid]['seen'] = now
        kind = data[0]
        if kind == HELLO:
            if pid is not None: return self.welcome(pid)
            try:
                ticket, _, name = data[1:].decode('utf-8', 'replace').partition('\n')
                owner, nonce = check_ticket(ticket.strip(), self.used)
            except (ValueError, UnicodeError) as error:
                return self.reject(addr, str(error))
            if owner and 0 in self.peers: return self.reject(addr, 'La sala ya tiene anfitrion.')
            if not owner and 0 not in self.peers: return self.reject(addr, 'El anfitrion aun no entro a la sala.')
            if len(self.peers) >= MAX_PLAYERS: return self.reject(addr, 'La sala esta llena (4 jugadores).')
            new_id = 0 if owner else next(i for i in range(1, MAX_PLAYERS) if i not in self.peers)
            clean = ''.join(c for c in name if c.isprintable())[:20] or 'Astronauta'
            self.used.add(nonce)
            self.peers[new_id] = dict(addr=addr, name=clean, seen=now)
            self.by_addr[addr] = new_id
            log(f'entra {new_id} {clean!r} desde {addr[0]}')
            self.welcome(new_id)
            self.broadcast_peers()
        elif kind == DATA and pid is not None and len(data) >= 2:
            packet = bytes([DELIVER, pid]) + data[2:]
            target = data[1] if pid == 0 else 0  # los invitados solo hablan con el anfitrion
            if target == BROADCAST:
                for other, peer in self.peers.items():
                    if other != pid: self.send(packet, peer['addr'])
            elif target in self.peers:
                self.send(packet, self.peers[target]['addr'])
        elif kind == PING and pid is not None:
            self.send(bytes([PONG]), addr)
        elif kind == BYE and pid is not None:
            self.remove(pid, 'salio')

    def write_status(self, now):
        if not STATUS or now < self.next_status: return
        self.next_status = now + 1
        temp = STATUS + '.tmp'
        try:
            with open(temp, 'w') as f: json.dump(dict(ready=True, players=len(self.peers), started=False), f)
            os.replace(temp, STATUS)
        except OSError: pass

    def run(self):
        log(f'relevo de la sala {ROOM} en UDP {PORT}')
        while not self.closed:
            now = time.time()
            try:
                data, addr = self.sock.recvfrom(2048)
                self.handle(data, addr, time.time())
            except socket.timeout: pass
            except OSError: pass
            now = time.time()
            for pid, peer in list(self.peers.items()):
                if now - peer['seen'] > TIMEOUT: self.remove(pid, 'sin respuesta')
            if self.peers: self.last_people = now
            # Sin jugadores: se cierra (75 s; 120 s si nadie llego a entrar).
            if now - self.last_people > (75 if now - self.started > 120 else 120): break
            self.write_status(now)
        log('relevo cerrado')


if __name__ == '__main__':
    Relay().run()
