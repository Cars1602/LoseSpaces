#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace LunaCero
{
    public sealed class LunarSurvivalSmokeTest : MonoBehaviour
    {
        private LunarPlayerCombat player;
        private LunarHealth hp;
        private LunarPlayerStatus status;
        private LunarEndlessWorld world;
        private LunarHordeDirector director;
        private readonly Vector3 home = new Vector3(0, 0.1f, -18);

        private IEnumerator Start()
        {
            var stack = new System.Collections.Generic.Stack<IEnumerator>();
            stack.Push(Run());
            while (stack.Count > 0)
            {
                object next;
                try
                {
                    var current = stack.Peek();
                    if (!current.MoveNext()) { stack.Pop(); continue; }
                    next = current.Current;
                }
                catch (Exception error)
                {
                    Debug.LogError("LUNA_SURVIVAL_SMOKE_FAILED: " + error);
                    EditorApplication.isPlaying = false;
                    yield break;
                }
                if (next is IEnumerator nested) stack.Push(nested);
                else yield return next;
            }
            Debug.Log("LUNA_SURVIVAL_SMOKE_OK: mundo y navegacion al viajar 240m, persistencia de balas, reservas y recarga, curacion solo en estacion e interrupcion, persecucion, ocho especiales, agarres y liberacion, acido/humo y cuatro jefes.");
            EditorApplication.isPlaying = false;
        }

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(0.7f);
            player = FindObjectOfType<LunarPlayerCombat>();
            hp = player.GetComponent<LunarHealth>();
            status = player.GetComponent<LunarPlayerStatus>();
            world = FindObjectOfType<LunarEndlessWorld>();
            director = FindObjectOfType<LunarHordeDirector>();
            director.enabled = false;
            Check(world.NavigationReady && world.LoadedChunks > 20 && world.LoadedChunks < 60, "No se genero el mundo circular navegable.");
            Check(Mathf.Abs(player.transform.position.y) < 0.2f, "El suelo circular no sostiene al jugador.");
            Check(player.StationHealingOnly && player.ReserveAmmo == 0, "La expedicion debe comenzar sin balas de reserva.");
            player.TryFire(new Ray(player.AimCamera.transform.position, Vector3.up));
            Check(player.Ammo == 11 && !player.TryReload(), "Recargar sin balas debe fallar.");
            var ammo = FindObjectsOfType<LunarPickup>().First(item => item.persistenceKey == "inicio-ammo");
            Check(ammo.Use(player) && player.ReserveAmmo == 18 && world.WasCollected("inicio-ammo"), "No se recogen o registran las balas.");
            Check(player.TryReload(), "No se recarga despues de recoger balas.");
            yield return new WaitForSeconds(1.4f);
            Check(player.Ammo == 12 && player.ReserveAmmo == 17, "La recarga debe transferir solo las balas que faltan.");
            hp.Damage(40);
            Check(!player.TryHeal(), "No se debe poder sanar lejos de la estacion.");
            var station = world.NearestStation(player.transform.position);
            Move(station.transform.position + new Vector3(0, 0.1f, -1));
            yield return new WaitForSeconds(0.25f);
            Check(player.TryHeal(), "No se inicia la curacion en la estacion.");
            yield return new WaitForSeconds(0.3f);
            hp.Damage(1);
            yield return new WaitForSeconds(0.2f);
            Check(!player.Healing && hp.Current == 59, "El dano debe interrumpir la curacion.");
            Check(player.TryHeal(), "No se puede volver a intentar sanar.");
            yield return new WaitForSeconds(3.2f);
            Check(hp.Current == hp.Maximum && director.StationsReached == 1 && !station.Ready, "La estacion no cura o no registra la visita.");
            Move(home + Vector3.right * 240);
            yield return WaitForNavigation();
            Check(world.ground.position.x > 200 && world.LoadedChunks < 60, "El mundo no se desplaza o crece sin limite al viajar.");
            Move(home);
            yield return WaitForNavigation();
            Check(!FindObjectsOfType<LunarPickup>().Any(item => item.persistenceKey == "inicio-ammo"), "Las balas recogidas reaparecen al regresar.");
            Debug.Log("LUNA_SURVIVAL_TEST_PROGRESS: mundo, balas y estaciones OK; probando habilidades.");

            var common = Spawn(LunarAlienKind.Common, 8);
            yield return new WaitForSeconds(1);
            Check(Vector3.Distance(common.transform.position, player.transform.position) < 7, "Los aliens comunes no persiguen al jugador.");
            Kill(common);
            var dormant = Spawn(LunarAlienKind.Dormant, 8);
            Vector3 sleeping = dormant.transform.position;
            yield return new WaitForSeconds(0.7f);
            Check(!dormant.IsAwakened && Vector3.Distance(sleeping, dormant.transform.position) < 0.2f, "La Durmiente ataca sin provocacion.");
            dormant.GetComponent<LunarHealth>().Damage(1);
            yield return new WaitForSeconds(0.5f);
            Check(dormant.IsAwakened, "La Durmiente no reacciona al dano.");
            Kill(dormant);

            var bile = Spawn(LunarAlienKind.Bile, 5);
            yield return new WaitForSeconds(0.2f);
            bile.enabled = false;
            LunarAlienProjectile.Fire(bile, LunarProjectileKind.Bile, player.transform.position + Vector3.up);
            yield return new WaitForSeconds(1.1f);
            Check(status.IsBiled && director.ActiveEnemies > 0, "La bilis no marca al jugador o no atrae horda.");
            Kill(bile);
            foreach (var enemy in FindObjectsOfType<LunarEnemy>()) if (!enemy.IsDead) Kill(enemy);
            hp.Heal(100);
            Move(home);

            var tongue = Spawn(LunarAlienKind.Tongue, 8);
            yield return new WaitForSeconds(3.1f);
            Check(status.Control == LunarControlEffect.Tongue, "El Tentaculo no atrapa al jugador.");
            Vector3 caught = player.transform.position;
            yield return new WaitForSeconds(0.4f);
            Check(Vector3.Distance(caught, player.transform.position) > 0.25f, "El tentaculo no arrastra al jugador.");
            yield return Escape();
            Kill(tongue);
            Check(FindObjectsOfType<LunarHazardZone>().Any(zone => zone.smoke), "El Tentaculo no deja humo al morir.");
            Move(tongue.transform.position + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.15f);
            Check(status.SmokeUntil > Time.time, "El humo no reduce la vision.");
            ClearHazards();
            Move(home);
            hp.Heal(100);
            yield return new WaitForSeconds(3.1f);

            var hunter = Spawn(LunarAlienKind.Pouncer, 8);
            yield return new WaitForSeconds(3.4f);
            Check(status.Control == LunarControlEffect.Pin && !player.CanAct, "El Saltador no salta e inmoviliza.");
            Kill(hunter);
            Check(!status.IsControlled, "Matar al captor no libera al jugador.");
            hp.Heal(100);
            Move(home);
            yield return new WaitForSeconds(3.1f);

            var rider = Spawn(LunarAlienKind.Rider, 7);
            yield return new WaitForSeconds(3.4f);
            Check(status.Control == LunarControlEffect.Ride, "El Parasito no se monta en el jugador.");
            Vector3 ridden = player.transform.position;
            yield return new WaitForSeconds(0.4f);
            Check(Vector3.Distance(ridden, player.transform.position) > 0.3f, "El Parasito no fuerza el movimiento.");
            yield return Escape();
            Kill(rider);
            hp.Heal(100);
            Move(home);
            yield return new WaitForSeconds(3.1f);

            var charger = Spawn(LunarAlienKind.Charger, 8);
            Check(!charger.Stagger(1), "El Embestidor no debe ser vulnerable al empujon.");
            yield return new WaitForSeconds(3.4f);
            Check(status.Control == LunarControlEffect.Charge, "El Embestidor no carga y atrapa.");
            Kill(charger);
            hp.Heal(100);
            Move(home);

            var brute = Spawn(LunarAlienKind.Brute, 8);
            Check(!brute.Stagger(1), "El Coloso no debe ser vulnerable al empujon.");
            yield return new WaitForSeconds(3.3f);
            Check(hp.Current < hp.Maximum, "El Coloso no lanza una roca que cause dano.");
            Kill(brute);
            hp.Heal(100);
            Move(home);

            var acid = Spawn(LunarAlienKind.Acid, 7);
            yield return new WaitForSeconds(3.3f);
            Check(FindObjectsOfType<LunarHazardZone>().Any(zone => !zone.smoke) && hp.Current < hp.Maximum, "El Escupidor no crea acido danino.");
            Kill(acid);
            Move(home + Vector3.right * 12);
            float escapedHealth = hp.Current;
            yield return new WaitForSeconds(0.7f);
            Check(hp.Current == escapedHealth, "El acido hace dano fuera del charco.");
            Check(director.BossKindsDefeated == 4, "No se registran los cuatro tipos de jefe.");
        }

        private LunarEnemy Spawn(LunarAlienKind kind, float distance)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/Aliens/" + kind + ".prefab");
            Vector3 wanted = player.transform.position + Vector3.forward * distance;
            Check(NavMesh.SamplePosition(wanted, out var hit, 2, NavMesh.AllAreas), "Falta navegacion para " + kind);
            return Instantiate(prefab, hit.position, Quaternion.LookRotation(Vector3.back)).GetComponent<LunarEnemy>();
        }

        private void Move(Vector3 position)
        {
            var body = player.GetComponent<Rigidbody>();
            body.position = position;
            body.velocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        private static void Kill(LunarEnemy enemy) { enemy.GetComponent<LunarHealth>().Damage(10000); }
        private static void ClearHazards() { foreach (var zone in FindObjectsOfType<LunarHazardZone>()) Destroy(zone.gameObject); }
        private IEnumerator Escape()
        {
            for (int i = 0; i < 3; i++) { status.Struggle(); yield return new WaitForSeconds(0.24f); }
            Check(!status.IsControlled, "No se puede escapar pulsando Q tres veces.");
        }

        private IEnumerator WaitForNavigation()
        {
            yield return new WaitForSeconds(0.3f);
            float deadline = Time.time + 10;
            while (!world.NavigationReady && Time.time < deadline) yield return null;
            Check(world.NavigationReady, "No termina la actualizacion de navegacion.");
        }

        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
#endif
