using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace LunaCero
{
    public sealed class LunarHordeDirector : MonoBehaviour
    {
        public GameObject commonPrefab;
        public GameObject[] specialPrefabs;
        public int maximumEnemies = 28;
        [Tooltip("Puntos de aparicion del escenario (opcional). Sin ellos aparecen alrededor del jugador.")]
        public Transform[] spawnPoints;
        public float minimumSpawnDistance = 14f;
        [Tooltip("0 = sin limite. En el mapa grande evita que aparezcan a cientos de metros.")]
        public float maximumSpawnDistance;
        [Header("Ritmo: relleno frecuente, jefes ocasionales")]
        public float waveInterval = 38f;
        public int commonsPerWave = 8;
        public float patrolInterval = 10f;
        public int commonsPerPatrol = 3;
        [Tooltip("Primera horda con jefe y cada cuantas hordas vuelve a salir uno")]
        public int firstBossWave = 2;
        public int bossEveryWaves = 2;
        [Tooltip("Jefes vivos a la vez como maximo")]
        public int maxBossesAlive = 2;
        [Range(0, 1)] public float doubleBossChance = 0.35f;
        public int Wave { get; private set; }
        public int Kills { get; private set; }
        public int StationsReached => stations.Count;
        public int BossKindsDefeated => defeatedBosses.Count;
        public int ActiveEnemies => enemies.Count(enemy => enemy != null && !enemy.IsDead);
        private readonly List<LunarEnemy> enemies = new List<LunarEnemy>();
        private readonly HashSet<string> stations = new HashSet<string>();
        private readonly HashSet<LunarAlienKind> defeatedBosses = new HashSet<LunarAlienKind>();
        private LunarPlayerCombat player;
        private LunarEndlessWorld world;
        private float nextWave;
        private float nextPatrol;
        private float nextAttract;
        private string announcement = "Busca balas azules y llega a la estacion medica.";
        private float announceUntil = 12;

        private void Start()
        {
            player = FindObjectOfType<LunarPlayerCombat>();
            world = FindObjectOfType<LunarEndlessWorld>();
            nextWave = Time.time + 18;
            nextPatrol = Time.time + 10;
        }

        private void Update()
        {
            if (player == null || player.GetComponent<LunarHealth>().IsDead || !NavigationReady) return;
            enemies.RemoveAll(enemy => enemy == null);
            foreach (var enemy in enemies.ToArray())
                if (Vector3.Distance(enemy.transform.position, player.transform.position) > 100) { enemies.Remove(enemy); Destroy(enemy.gameObject); }
            if (Time.time >= nextWave)
            {
                Wave++;
                // El relleno (Emjambre) es la base de cada horda; los jefes llegan cada pocas hordas.
                SpawnCommons(Mathf.Min(commonsPerWave + Wave * 2, maximumEnemies));
                var bosses = BossPrefabs();
                bool bossWave = bosses.Length > 0 && Wave >= firstBossWave && (Wave - firstBossWave) % bossEveryWaves == 0;
                int spawned = 0;
                if (bossWave)
                {
                    int wanted = Wave >= firstBossWave + bossEveryWaves && Random.value < doubleBossChance ? 2 : 1;
                    wanted = Mathf.Min(wanted, maxBossesAlive - ActiveBosses);
                    for (int i = 0; i < wanted; i++)
                    {
                        MakeRoom();
                        var alive = enemies.Where(e => e != null && !e.IsDead && e.isBoss).Select(e => e.alienKind).ToList();
                        var options = bosses.Where(p => !alive.Contains(p.GetComponent<LunarEnemy>().alienKind)).ToArray();
                        var boss = Spawn((options.Length > 0 ? options : bosses)[Random.Range(0, (options.Length > 0 ? options : bosses).Length)]);
                        if (boss != null) spawned++;
                    }
                }
                if (spawned == 2) Announce($"HORDA {Wave}: DOS JEFES se acercan. Busca cobertura!");
                else if (spawned == 1) Announce($"HORDA {Wave}: un JEFE se acerca con la horda.");
                else Announce("HORDA " + Wave + ": busca cobertura y administra las balas.");
                nextWave = Time.time + waveInterval;
            }
            if (Time.time >= nextPatrol)
            {
                SpawnCommons(commonsPerPatrol);
                nextPatrol = Time.time + patrolInterval;
            }
        }

        public int ActiveBosses => enemies.Count(enemy => enemy != null && !enemy.IsDead && enemy.isBoss);

        private GameObject[] BossPrefabs()
        {
            var bosses = specialPrefabs.Where(p => p != null && p.GetComponent<LunarEnemy>() != null && p.GetComponent<LunarEnemy>().isBoss).ToArray();
            return bosses.Length > 0 ? bosses : specialPrefabs.Where(p => p != null).ToArray();
        }

        // Si se alcanzo el limite, retira al relleno mas lejano para dejar sitio a un jefe.
        private void MakeRoom()
        {
            if (ActiveEnemies < maximumEnemies) return;
            var distant = enemies.Where(enemy => enemy != null && enemy.alienKind == LunarAlienKind.Common)
                .OrderByDescending(enemy => Vector3.Distance(enemy.transform.position, player.transform.position)).FirstOrDefault();
            if (distant != null) { enemies.Remove(distant); Destroy(distant.gameObject); }
        }

        public void NotifyShot(Vector3 position)
        {
            foreach (var enemy in enemies)
                if (enemy != null && Vector3.Distance(position, enemy.transform.position) < 45) enemy.Alert();
        }

        public void AttractHorde()
        {
            foreach (var enemy in enemies) if (enemy != null) enemy.Alert();
            if (Time.time < nextAttract) return;
            nextAttract = Time.time + 10;
            SpawnCommons(6);
            Announce("Contaminacion: llegan mas aliens a tu posicion.");
        }

        public void ReachStation(string key)
        {
            if (stations.Add(key)) Announce("Estacion alcanzada. Sigue explorando y derrota a los cuatro jefes.");
        }

        public void EnemyKilled(LunarEnemy enemy)
        {
            Kills++;
            if (enemy.isBoss) defeatedBosses.Add(enemy.alienKind);
        }

        private void Announce(string text) { announcement = text; announceUntil = Time.time + 5; }
        public void SpawnCommons(int amount)
        {
            for (int i = 0; i < amount && ActiveEnemies < maximumEnemies; i++) Spawn(commonPrefab);
        }

        private bool NavigationReady => world != null ? world.NavigationReady : LunarLevelNavigation.Ready;

        private LunarHealingStation NearestStation(Vector3 position)
        {
            if (world != null) return world.NearestStation(position);
            LunarHealingStation nearest = null;
            float best = float.MaxValue;
            foreach (var station in FindObjectsOfType<LunarHealingStation>())
            {
                if (station.Exhausted) continue;
                float distance = (station.transform.position - position).sqrMagnitude;
                if (distance < best) { best = distance; nearest = station; }
            }
            return nearest;
        }

        private Vector3 SpawnCandidate()
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                // Mapa grande: solo puntos a una distancia razonable (ni encima del jugador ni a cientos de metros).
                var eligible = new List<Transform>();
                foreach (var point in spawnPoints)
                {
                    if (point == null) continue;
                    float distance = Vector3.Distance(point.position, player.transform.position);
                    if (distance < minimumSpawnDistance || (maximumSpawnDistance > 0 && distance > maximumSpawnDistance)) continue;
                    eligible.Add(point);
                }
                if (eligible.Count > 0)
                {
                    var point = eligible[Random.Range(0, eligible.Count)];
                    Vector2 jitter = Random.insideUnitCircle * 2.5f;
                    return point.position + new Vector3(jitter.x, 0, jitter.y);
                }
            }
            float angle = Random.value * Mathf.PI * 2;
            return player.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * Random.Range(22f, 34f);
        }

        public LunarEnemy Spawn(GameObject prefab)
        {
            if (prefab == null || player == null || ActiveEnemies >= maximumEnemies) return null;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                Vector3 wanted = SpawnCandidate();
                if (!NavMesh.SamplePosition(wanted, out var hit, 4, NavMesh.AllAreas)) continue;
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(hit.position, player.transform.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                var instance = Instantiate(prefab, hit.position, Quaternion.identity);
                var enemy = instance.GetComponent<LunarEnemy>();
                enemy.relentless = true;
                enemies.Add(enemy);
                return enemy;
            }
            return null;
        }

        private void OnGUI()
        {
            if (player == null) return;
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = Screen.width / scale;
            var station = NearestStation(player.transform.position);
            string stationText = station == null ? "Explora para encontrar una estacion" : $"Estacion medica: {Vector3.Distance(station.transform.position, player.transform.position):0} m";
            GUI.Box(new Rect(width - 320, 15, 300, 105), $"{stationText}\nHorda: {Wave} | Proxima: {Mathf.Max(0, nextWave - Time.time):0}s\nAliens: {ActiveEnemies}/{maximumEnemies} | Bajas: {Kills}\nEstaciones: {StationsReached} | Jefes: {BossKindsDefeated}/4");
            if (Time.time < announceUntil) GUI.Box(new Rect(width / 2 - 280, 175, 560, 35), announcement);
            if (BossKindsDefeated >= 4) GUI.Box(new Rect(width / 2 - 220, 270, 440, 50), "CUATRO JEFES ELIMINADOS\nLa exploracion y las hordas continuan.");
            if (station != null)
            {
                Vector3 direction = station.transform.position - player.transform.position;
                float angle = Vector3.SignedAngle(player.transform.forward, Vector3.ProjectOnPlane(direction, Vector3.up), Vector3.up);
                string guidance = Mathf.Abs(angle) < 20 ? "ADELANTE" : angle > 0 ? "DERECHA" : "IZQUIERDA";
                GUI.Label(new Rect(width / 2 - 130, Screen.height / scale - 125, 360, 25), $"+ Estacion: {guidance} ({direction.magnitude:0} m)");
            }
            GUI.matrix = previous;
        }
    }
}
