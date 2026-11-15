using System.Collections.Generic;
using UnityEngine;

namespace LunaCero
{
    /// <summary>
    /// Avisa cuando el astronauta recibe dano:
    /// - sonido de golpe contra el traje (mas fuerte cuanto mayor el dano),
    /// - arco rojo alrededor de la mira apuntando hacia el monstruo que ataco (o el proyectil),
    /// - con vida baja: pitido de alarma del traje y bordes rojos que laten.
    /// Funciona con cualquier origen de dano (tambien el que llega por la red), porque mira la vida.
    /// </summary>
    public sealed class LunarDamageFeedback : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.8f;
        public float lowHealth = 0.3f;

        private LunarHealth health;
        private LunarPlayerCombat combat;
        private AudioSource source;
        private float lastHealth;
        private float nextHitSound, nextAlarm, pending;
        private static AudioClip hitClip, heavyClip, alarmClip, breathClip;

        private sealed class Indicator { public Vector3 from; public float until, strength; }
        private readonly List<Indicator> indicators = new List<Indicator>();

        private void Awake()
        {
            health = GetComponent<LunarHealth>();
            combat = GetComponent<LunarPlayerCombat>();
            source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 0f; // dentro del casco: se oye igual por los dos lados
            source.playOnAwake = false;
            if (hitClip == null) BuildClips();
        }

        private void Start() { lastHealth = health.Current; }

        private void Update()
        {
            float now = health.Current;
            float lost = lastHealth - now;
            lastHealth = now;
            if (health.IsDead) { indicators.Clear(); return; }
            if (lost > 0.01f)
            {
                pending += lost;
                RegisterDirection(lost);
            }
            // Los danos continuos (asfixia, acido, mordidas) se juntan: un golpe sonoro cada 0,35 s como maximo.
            if (pending >= 1.5f && Time.time >= nextHitSound)
            {
                float strength = Mathf.Clamp01(pending / 25f);
                source.PlayOneShot(pending >= 15f ? heavyClip : hitClip, volume * Mathf.Lerp(0.55f, 1f, strength));
                nextHitSound = Time.time + 0.35f;
                pending = 0f;
            }
            else if (Time.time >= nextHitSound) pending = Mathf.Max(0f, pending - Time.deltaTime * 2f);

            // Vida baja: alarma del traje (mas rapida cuanto menos vida queda).
            float fraction = now / Mathf.Max(1f, health.Maximum);
            if (fraction < lowHealth && Time.time >= nextAlarm)
            {
                source.PlayOneShot(alarmClip, volume * 0.45f);
                source.PlayOneShot(breathClip, volume * 0.35f);
                nextAlarm = Time.time + Mathf.Lerp(0.7f, 1.6f, fraction / lowHealth);
            }
            indicators.RemoveAll(i => Time.time > i.until);
        }

        /// <summary>De donde vino el golpe: el monstruo vivo mas cercano (o el proyectil mas cercano).</summary>
        private void RegisterDirection(float lost)
        {
            Vector3 me = transform.position;
            Transform best = null;
            float bestDistance = 30f * 30f;
            foreach (var enemy in FindObjectsOfType<LunarEnemy>())
            {
                if (enemy.IsDead) continue;
                float d = (enemy.transform.position - me).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = enemy.transform; }
            }
            foreach (var projectile in FindObjectsOfType<LunarAlienProjectile>())
            {
                float d = (projectile.transform.position - me).sqrMagnitude;
                if (d < 9f && d < bestDistance) { bestDistance = d; best = projectile.transform; }
            }
            if (best == null) return; // asfixia o acido del suelo: sin direccion (ya hay sonido y destello)
            foreach (var indicator in indicators)
                if (Vector3.Angle(indicator.from - me, best.position - me) < 25f)
                {
                    indicator.from = best.position;
                    indicator.until = Time.time + 1.4f;
                    indicator.strength = Mathf.Clamp01(indicator.strength + lost / 20f);
                    return;
                }
            indicators.Add(new Indicator { from = best.position, until = Time.time + 1.4f, strength = Mathf.Clamp01(0.4f + lost / 20f) });
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || health == null || health.IsDead || LunarPlayerRole.MenuOpen || LostInSpaceFinale.Active) return;
            var size = LunarHud.Begin(out var previous);
            float width = size.x, height = size.y;

            // Bordes rojos que laten con la vida baja.
            float fraction = health.Current / Mathf.Max(1f, health.Maximum);
            if (fraction < lowHealth)
            {
                float pulse = (Mathf.Sin(Time.time * Mathf.Lerp(9f, 5f, fraction / lowHealth)) + 1f) * 0.5f;
                Color red = new Color(0.9f, 0.05f, 0.05f, Mathf.Lerp(0.12f, 0.3f, pulse) * (1f - fraction / lowHealth * 0.5f));
                const float edge = 26f;
                LunarHud.Fill(new Rect(0, 0, width, edge), red);
                LunarHud.Fill(new Rect(0, height - edge, width, edge), red);
                LunarHud.Fill(new Rect(0, edge, edge, height - edge * 2), red);
                LunarHud.Fill(new Rect(width - edge, edge, edge, height - edge * 2), red);
            }

            // Arcos de direccion alrededor de la mira.
            var view = combat != null && combat.AimCamera != null ? combat.AimCamera.transform : transform;
            Vector2 center = new Vector2(width / 2f, height / 2f);
            foreach (var indicator in indicators)
            {
                Vector3 to = Vector3.ProjectOnPlane(indicator.from - transform.position, Vector3.up);
                Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
                if (to.sqrMagnitude < 0.01f || forward.sqrMagnitude < 0.01f) continue;
                float angle = Vector3.SignedAngle(forward, to, Vector3.up); // 0 = delante, 90 = derecha
                float fade = Mathf.Clamp01((indicator.until - Time.time) / 0.6f);
                Matrix4x4 before = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, center * LunarHud.Scale); // el pivote va en pixeles de pantalla
                LunarHud.Icon(new Rect(center.x - 90f, center.y - 150f, 180f, 56f), LunarHud.DamageArc,
                    new Color(1f, 0.12f, 0.08f, (0.45f + 0.55f * indicator.strength) * fade));
                GUI.matrix = before;
            }
            GUI.matrix = previous;
        }

        // ------------------------------------------------------------ sonidos sintetizados (no hay de dano en el paquete)
        private static void BuildClips()
        {
            hitClip = Synth("Golpe al traje", 0.32f, 900, (t, rnd, state) =>
            {
                // Golpe sordo (grave que cae) + crujido corto del casco
                float body = Mathf.Sin(2f * Mathf.PI * (95f * t - 60f * t * t)) * Mathf.Exp(-t * 14f);
                float crack = (float)(rnd.NextDouble() * 2 - 1) * Mathf.Exp(-t * 45f) * 0.6f;
                return (body * 0.9f + crack) * Mathf.Min(1f, t * 600f);
            });
            heavyClip = Synth("Golpe fuerte", 0.5f, 901, (t, rnd, state) =>
            {
                float body = Mathf.Sin(2f * Mathf.PI * (70f * t - 40f * t * t)) * Mathf.Exp(-t * 8f);
                state[0] = Mathf.Lerp(state[0], (float)(rnd.NextDouble() * 2 - 1), 0.25f);
                float rumble = state[0] * Mathf.Exp(-t * 10f) * 0.8f;
                float crack = (float)(rnd.NextDouble() * 2 - 1) * Mathf.Exp(-t * 35f) * 0.5f;
                return (body + rumble + crack) * Mathf.Min(1f, t * 600f) * 0.85f;
            });
            alarmClip = Synth("Alarma del traje", 0.34f, 902, (t, rnd, state) =>
            {
                float f = t < 0.16f ? 880f : 660f;
                float local = t < 0.16f ? t : t - 0.18f;
                if (t > 0.16f && t < 0.18f) return 0f;
                float env = Mathf.Min(1f, local * 200f) * Mathf.Clamp01((0.16f - local) * 60f);
                return Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * t)) * 0.18f * env + Mathf.Sin(2f * Mathf.PI * f * t) * 0.25f * env;
            });
            breathClip = Synth("Respiracion agitada", 0.55f, 903, (t, rnd, state) =>
            {
                state[0] = Mathf.Lerp(state[0], (float)(rnd.NextDouble() * 2 - 1), 0.08f);
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.55f));
                return state[0] * env * env * 0.9f;
            });
        }

        private static AudioClip Synth(string name, float seconds, int seed, System.Func<float, System.Random, float[], float> sample)
        {
            const int rate = 22050;
            int count = Mathf.CeilToInt(seconds * rate);
            var data = new float[count];
            var random = new System.Random(seed);
            var state = new float[2];
            for (int i = 0; i < count; i++) data[i] = Mathf.Clamp(sample(i / (float)rate, random, state), -1f, 1f);
            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
