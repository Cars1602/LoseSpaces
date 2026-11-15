#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace LunaCero
{
    // Added only by the editor test command. It is excluded from player builds.
    public sealed class LunarCombatSmokeTest : MonoBehaviour
    {
        private IEnumerator Start()
        {
            IEnumerator test = Run();
            while (true)
            {
                object next;
                try
                {
                    if (!test.MoveNext()) break;
                    next = test.Current;
                }
                catch (Exception error)
                {
                    Debug.LogError("LUNA_COMBAT_SMOKE_FAILED: " + error);
                    EditorApplication.isPlaying = false;
                    yield break;
                }
                yield return next;
            }
            Debug.Log("LUNA_COMBAT_SMOKE_OK: navegacion y persecucion, ataque enemigo, curacion, inventario, cargar/soltar, disparos y cobertura, recarga, muerte de enemigos y jugador.");
            EditorApplication.isPlaying = false;
        }

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(0.6f);
            var player = FindObjectOfType<LunarPlayerCombat>();
            var hp = player.GetComponent<LunarHealth>();
            var arena = FindObjectOfType<LunarCombatArena>();
            Check(arena.NavigationReady, "No se construyo la navegacion.");
            var enemies = FindObjectsOfType<LunarEnemy>();
            Check(enemies.Length == 12 && enemies.All(enemy => enemy.GetComponent<NavMeshAgent>().isOnNavMesh), "Enemigos fuera de la navegacion.");
            var closest = enemies.OrderBy(enemy => Vector3.Distance(enemy.transform.position, player.transform.position)).First();
            float initialDistance = Vector3.Distance(closest.transform.position, player.transform.position);
            yield return new WaitForSeconds(4f);
            Check(Vector3.Distance(closest.transform.position, player.transform.position) < initialDistance - 0.5f, "Los bots no persiguen al jugador.");
            Check(hp.Current < hp.Maximum, "Los bots no infligen dano al jugador.");
            foreach (var enemy in enemies)
            {
                enemy.enabled = false;
                enemy.StopAllCoroutines();
                enemy.GetComponent<NavMeshAgent>().isStopped = true;
            }
            hp.Damage(20);
            int kits = player.Medkits;
            float beforeHeal = hp.Current;
            Check(player.TryHeal() && hp.Current > beforeHeal && player.Medkits == kits - 1, "Fallo la curacion o su consumo.");
            var medkit = Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/Medkit.prefab"), player.transform.position + Vector3.forward, Quaternion.identity).GetComponent<LunarPickup>();
            Check(medkit.Use(player) && player.Medkits == kits, "No se recoge el botiquin.");
            var crate = Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/Carryable.prefab"), player.transform.position + Vector3.forward, Quaternion.identity).GetComponent<LunarPickup>();
            Check(crate.Use(player) && player.HeldObject == crate && crate.IsHeld && !crate.GetComponent<Collider>().enabled, "No se carga el objeto.");
            Check(player.Drop(true) && !crate.IsHeld && crate.GetComponent<Collider>().enabled && crate.GetComponent<Rigidbody>().velocity.magnitude > 1, "No se lanza el objeto.");
            Destroy(crate.gameObject);
            var target = Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/BotDebil.prefab"), player.transform.position + Vector3.right * 3, Quaternion.identity);
            target.GetComponent<LunarEnemy>().enabled = false;
            var targetHealth = target.GetComponent<LunarHealth>();
            yield return null;
            Physics.SyncTransforms();
            Ray aim = new Ray(player.AimCamera.transform.position, (target.transform.position + Vector3.up - player.AimCamera.transform.position).normalized);
            int ammo = player.Ammo;
            Check(player.TryFire(aim), "No se dispara.");
            Check(targetHealth.Current == 35 && player.Ammo == ammo - 1, "El disparo no causa dano o no consume municion.");
            yield return new WaitForSeconds(0.22f);
            Check(player.TryFire(aim), "Segundo disparo bloqueado.");
            yield return new WaitForSeconds(0.22f);
            Check(player.TryFire(aim) && targetHealth.IsDead && !target.GetComponent<Collider>().enabled, "No muere el bot.");
            int reserve = player.ReserveAmmo;
            Check(player.TryReload() && player.Reloading, "No inicia la recarga.");
            yield return new WaitForSeconds(1.4f);
            Check(player.Ammo == ammo && player.ReserveAmmo == reserve - 3 && !player.Reloading, "La recarga no conserva municion.");
            var covered = Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/BotDebil.prefab"), player.transform.position + Vector3.right * 3, Quaternion.identity);
            covered.GetComponent<LunarEnemy>().enabled = false;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = player.transform.position + new Vector3(1.6f, 1.1f, 0);
            wall.transform.localScale = new Vector3(0.3f, 3, 3);
            Physics.SyncTransforms();
            player.TryFire(aim);
            Check(covered.GetComponent<LunarHealth>().Current == 60, "Los disparos atraviesan paredes.");
            Destroy(wall);
            foreach (var boss in enemies.Where(enemy => enemy.isBoss))
            {
                boss.GetComponent<LunarHealth>().Damage(10000);
                Check(boss.IsDead && !boss.GetComponent<Collider>().enabled, "No muere un jefe.");
            }
            hp.Damage(10000);
            Check(!player.CanAct && !player.GetComponent<LunarPlayerController>().enabled && !player.TryHeal() && !player.TryFire(aim), "El jugador muerto conserva acciones.");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
