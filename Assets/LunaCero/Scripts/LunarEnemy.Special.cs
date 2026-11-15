using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace LunaCero
{
    public enum LunarAlienKind { Common, Bile, Tongue, Pouncer, Dormant, Brute, Charger, Acid, Rider }

    public sealed partial class LunarEnemy
    {
        public LunarAlienKind alienKind;
        public bool relentless;
        private float nextAbility = 3f;
        private float staggerUntil;
        private bool busy;
        private bool awakened;
        public bool IsAwakened => awakened;
        public bool IsBusy => busy;

        public void Alert() { if (alienKind != LunarAlienKind.Dormant) alerted = true; }

        private bool SpecialUpdate(float distance)
        {
            if (busy || Time.time < staggerUntil) { Stop(); return true; }
            if (alienKind == LunarAlienKind.Dormant && !awakened)
            {
                // Despierta si lo alumbra la linterna (solo se comprueba con el astronauta local del anfitrion).
                bool lit = false;
                if (target.TryGetComponent(out LunarPlayerCombat player) && player.AimCamera != null && target.TryGetComponent(out LunarPlayerStatus status))
                {
                    Vector3 toward = (transform.position + Vector3.up - player.AimCamera.transform.position).normalized;
                    lit = status.FlashlightOn && distance < 14 && Vector3.Dot(player.AimCamera.transform.forward, toward) > 0.9f && CanSeeTarget();
                }
                if (distance < 3.8f || health.Current < health.Maximum || lit)
                {
                    awakened = true;
                    alerted = true;
                    agent.speed = 6f;
                }
                else { Stop(); return true; }
            }
            bool visible = CanSeeTarget();
            if (Time.time < nextAbility)
            {
                bool preparing = distance > 4 && distance < 12 && alienKind != LunarAlienKind.Common && alienKind != LunarAlienKind.Dormant;
                if (preparing && visible) { Stop(); return true; }
                return false;
            }
            if (!visible) return false;
            switch (alienKind)
            {
                case LunarAlienKind.Bile:
                    if (distance < 11) { nextAbility = Time.time + 7; StartCoroutine(ThrowWithAnimation(LunarProjectileKind.Bile, Vector3.up)); }
                    break;
                case LunarAlienKind.Tongue:
                    if (distance > 2 && distance < 14) { nextAbility = Time.time + 9; StartCoroutine(TongueAttack()); return true; }
                    break;
                case LunarAlienKind.Pouncer:
                case LunarAlienKind.Rider:
                    if (distance > 2.5f && distance < 12)
                    {
                        nextAbility = Time.time + 8;
                        if (TryGetComponent(out LunarAlienAnimation leap)) leap.Attack();
                        StartCoroutine(LeapAttack());
                        return true;
                    }
                    break;
                case LunarAlienKind.Charger:
                    if (distance > 3 && distance < 14) { nextAbility = Time.time + 9; StartCoroutine(ChargeAttack()); return true; }
                    break;
                case LunarAlienKind.Acid:
                    if (distance < 17)
                    {
                        nextAbility = Time.time + 7;
                        StartCoroutine(ThrowWithAnimation(LunarProjectileKind.Acid, Vector3.zero));
                    }
                    break;
                case LunarAlienKind.Brute:
                    if (distance > 4 && distance < 18)
                    {
                        nextAbility = Time.time + 5;
                        StartCoroutine(ThrowWithAnimation(LunarProjectileKind.Rock, Vector3.up * 0.6f));
                    }
                    break;
            }
            return false;
        }

        /// <summary>
        /// Reproduce la animacion de ataque y lanza el proyectil en el momento del lanzamiento
        /// (a mitad del clip), apuntando a donde esta el jugador en ese instante.
        /// </summary>
        private IEnumerator ThrowWithAnimation(LunarProjectileKind kind, Vector3 offset)
        {
            float release = 0f;
            if (TryGetComponent(out LunarAlienAnimation animation) && animation.animator != null)
            {
                animation.Attack();
                release = Mathf.Clamp(animation.attackDuration * 0.45f, 0.15f, 0.8f);
            }
            if (release > 0f)
            {
                busy = true;
                Stop();
                yield return new WaitForSeconds(release);
                busy = false;
            }
            if (IsDead || target == null || target.IsDead) yield break;
            LunarAlienProjectile.Fire(this, kind, target.transform.position + offset);
        }

        private void PerformMelee()
        {
            if (TryGetComponent(out LunarAlienAnimation animation)) animation.Attack();
            target.Damage(attackDamage);
            if (alienKind == LunarAlienKind.Brute)
            {
                Vector3 direction = (target.transform.position - transform.position).normalized;
                if (target.TryGetComponent(out LunarPlayerStatus knocked)) knocked.Knockback(direction * 9f + Vector3.up * 3f);
                foreach (var collider in Physics.OverlapSphere(transform.position + transform.forward, 3f))
                {
                    var body = collider.attachedRigidbody;
                    if (body != null && !body.isKinematic && body.GetComponent<LunarPlayerCombat>() == null)
                        body.AddForce(direction * 8 + Vector3.up * 3, ForceMode.VelocityChange);
                }
            }
            if (alienKind == LunarAlienKind.Dormant && !target.IsDead)
            {
                nextAbility = Time.time + 10;
                StartCoroutine(CapturedAttack(LunarControlEffect.Pin, 6, Vector3.zero));
            }
        }

        private IEnumerator TongueAttack()
        {
            busy = true;
            Stop();
            LunarCombatEffects.Beam(transform.position + Vector3.up * 1.4f, target.transform.position + Vector3.up, Color.yellow);
            yield return new WaitForSeconds(0.4f);
            if (target != null && !target.IsDead && CanSeeTarget() && Vector3.Distance(transform.position, target.transform.position) < 15)
                yield return CapturedAttack(LunarControlEffect.Tongue, 6, Vector3.zero);
            busy = false;
        }

        private IEnumerator LeapAttack()
        {
            busy = true;
            Stop();
            Vector3 start = transform.position;
            Vector3 end = target.transform.position;
            agent.enabled = false;
            float elapsed = 0;
            while (elapsed < 0.75f && !health.IsDead)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / 0.75f);
                Vector3 next = Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 2.4f);
                Vector3 step = next - transform.position;
                bool blocked = false;
                foreach (var hit in Physics.SphereCastAll(transform.position + Vector3.up * 0.7f, 0.35f, step.normalized, step.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(transform) || LunarCrew.IsCrew(hit.collider.GetComponentInParent<LunarHealth>())) continue;
                    if (hit.normal.y > 0.6f) continue;
                    blocked = true;
                    break;
                }
                if (blocked) break;
                transform.position = next;
                yield return null;
            }
            RestoreAgent();
            if (!target.IsDead && Vector3.Distance(transform.position, target.transform.position) < 2 + (Reach - attackRange) && CanSeeTarget())
            {
                target.Damage(8);
                yield return CapturedAttack(alienKind == LunarAlienKind.Rider ? LunarControlEffect.Ride : LunarControlEffect.Pin, 7, transform.forward);
            }
            busy = false;
        }

        private IEnumerator ChargeAttack()
        {
            busy = true;
            Stop();
            Vector3 direction = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up).normalized;
            transform.rotation = Quaternion.LookRotation(direction);
            LunarCombatEffects.Beam(transform.position + Vector3.up * 0.3f, transform.position + direction * 12 + Vector3.up * 0.3f, Color.red);
            yield return new WaitForSeconds(0.55f);
            float until = Time.time + 1f;
            while (Time.time < until && !health.IsDead && agent.enabled && agent.isOnNavMesh)
            {
                Vector3 before = agent.nextPosition;
                agent.Move(direction * 13f * Time.deltaTime);
                if (Vector3.Distance(transform.position, target.transform.position) < 1.8f)
                {
                    target.Damage(12);
                    yield return CapturedAttack(LunarControlEffect.Charge, 4f, direction);
                    break;
                }
                if ((agent.nextPosition - before).magnitude < Time.deltaTime * 2) break;
                yield return null;
            }
            busy = false;
        }

        private IEnumerator CapturedAttack(LunarControlEffect effect, float seconds, Vector3 direction)
        {
            // Los astronautas de otros jugadores no tienen estado de captura: solo reciben el dano.
            if (!target.TryGetComponent(out LunarPlayerStatus status) || !status.Capture(this, effect, seconds, direction)) yield break;
            busy = true;
            Stop();
            bool attached = effect == LunarControlEffect.Ride || effect == LunarControlEffect.Charge;
            if (attached)
            {
                agent.enabled = false;
                GetComponent<Collider>().enabled = false;
            }
            float nextDamage = Time.time + 0.6f;
            while (!health.IsDead && !target.IsDead && status.Captor == this)
            {
                if (attached) transform.position = target.transform.position + (effect == LunarControlEffect.Ride ? Vector3.up * 1.1f - target.transform.forward * 0.3f : direction * 1.2f);
                if (effect == LunarControlEffect.Tongue)
                {
                    if (!CanSeeTarget()) { status.Release(); break; }
                    LunarCombatEffects.Beam(transform.position + Vector3.up * 1.4f, target.transform.position + Vector3.up, new Color(0.8f, 0.2f, 0.7f));
                }
                if (Time.time >= nextDamage) { target.Damage(effect == LunarControlEffect.Tongue ? 3 : 5); nextDamage = Time.time + 0.65f; }
                yield return null;
            }
            if (attached && !health.IsDead) { GetComponent<Collider>().enabled = true; RestoreAgent(); }
            busy = false;
        }

        private void RestoreAgent()
        {
            if (health.IsDead) return;
            if (NavMesh.SamplePosition(transform.position, out var point, 5, NavMesh.AllAreas))
            {
                transform.position = point.position;
                agent.enabled = true;
                agent.Warp(point.position);
            }
        }

        public bool Stagger(float seconds)
        {
            if (StaggerRedirect != null) { StaggerRedirect(seconds); return true; } // copia de red
            if (IsDead || alienKind == LunarAlienKind.Brute || alienKind == LunarAlienKind.Charger) return false;
            if (target != null && target.TryGetComponent(out LunarPlayerStatus held) && held.Captor == this) held.Release();
            StopAllCoroutines();
            busy = false;
            staggerUntil = Time.time + seconds;
            GetComponent<Collider>().enabled = true;
            RestoreAgent();
            Stop();
            if (target != null && agent.isOnNavMesh) agent.Move((transform.position - target.transform.position).normalized * 0.7f);
            return true;
        }

        private void SpecialDeath()
        {
            if (target == null) return;
            target.TryGetComponent(out LunarPlayerStatus status);
            if (status != null && status.Captor == this) status.Release();
            if (alienKind == LunarAlienKind.Bile && status != null && Vector3.Distance(transform.position, target.transform.position) < 6 && CanSeeTarget()) status.ApplyBile(10);
            if (alienKind == LunarAlienKind.Tongue) LunarHazardZone.Create(transform.position, true, 4);
            if (alienKind == LunarAlienKind.Acid) LunarHazardZone.Create(transform.position, false, 3);
            FindObjectOfType<LunarHordeDirector>()?.EnemyKilled(this);
        }
    }
}
