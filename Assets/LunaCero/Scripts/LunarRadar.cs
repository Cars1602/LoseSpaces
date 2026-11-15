using System.Collections.Generic;
using UnityEngine;

namespace LunaCero
{
    /// <summary>
    /// Radar circular (arriba a la izquierda). Gira con el jugador: arriba = hacia donde mira.
    /// Rojo = enemigos, punto de calor grande y palpitante = jefes, azul = piezas de la nave,
    /// verde = punto de reparacion.
    /// </summary>
    public sealed class LunarRadar : MonoBehaviour
    {
        public float range = 45f;
        public float size = 180f;
        public Vector2 position = new Vector2(16, 12);
        public float scanInterval = 0.25f;

        private readonly List<LunarEnemy> enemies = new List<LunarEnemy>();
        private readonly List<LunarShipPart> parts = new List<LunarShipPart>();
        private LunarRepairPoint repair;
        private LunarPlayerController controller;
        private float nextScan;
        private static Texture2D disc, ring, dot;

        public Rect Area => new Rect(position.x, position.y, size, size);

        private void Awake() { controller = GetComponent<LunarPlayerController>(); }

        private void Update()
        {
            if (Time.time < nextScan) return;
            nextScan = Time.time + scanInterval;
            enemies.Clear(); enemies.AddRange(FindObjectsOfType<LunarEnemy>());
            parts.Clear(); parts.AddRange(FindObjectsOfType<LunarShipPart>());
            if (repair == null) repair = FindObjectOfType<LunarRepairPoint>();
            var definition = LunarPlayerRole.Of(this);
            range = definition.radarRange;
            showLoot = definition.radarLoot;
            loot.Clear();
            if (showLoot)
                foreach (var pickup in FindObjectsOfType<LunarPickup>())
                    if (!pickup.IsHeld && (pickup.kind == LunarPickupKind.Weapon || pickup.kind == LunarPickupKind.Ammo || pickup.kind == LunarPickupKind.Medkit))
                        loot.Add(pickup);
        }

        private readonly List<LunarPickup> loot = new List<LunarPickup>();
        private bool showLoot;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || LunarPlayerRole.MenuOpen) return; // solo dibuja: no hay botones
            EnsureTextures();
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            Color old = GUI.color;
            Rect area = Area;
            Vector2 center = area.center;
            float radius = size / 2f;

            // Fondo, anillos y barrido
            Draw(disc, area, new Color(0.02f, 0.12f, 0.08f, 0.78f));
            Draw(ring, area, new Color(0.25f, 0.9f, 0.5f, 0.75f));
            Draw(ring, Inset(area, size * 0.17f), new Color(0.25f, 0.9f, 0.5f, 0.3f));
            Draw(ring, Inset(area, size * 0.34f), new Color(0.25f, 0.9f, 0.5f, 0.25f));
            Draw(Texture2D.whiteTexture, new Rect(center.x - 0.5f, area.y + 6, 1, size - 12), new Color(0.25f, 0.9f, 0.5f, 0.15f));
            Draw(Texture2D.whiteTexture, new Rect(area.x + 6, center.y - 0.5f, size - 12, 1), new Color(0.25f, 0.9f, 0.5f, 0.15f));
            Matrix4x4 beforeSweep = GUI.matrix;
            GUIUtility.RotateAroundPivot(Time.time * 120f % 360f, center * scale);
            Draw(Texture2D.whiteTexture, new Rect(center.x, center.y - 1, radius - 4, 2), new Color(0.4f, 1f, 0.6f, 0.45f));
            GUI.matrix = beforeSweep;

            float yaw = controller != null && controller.View != null ? controller.View.eulerAngles.y : transform.eulerAngles.y;
            Quaternion toLocal = Quaternion.Euler(0, -yaw, 0);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);

            // Explorador: armas (amarillo), municion (celeste) y botiquines (blanco), solo dentro del alcance.
            if (showLoot)
                foreach (var pickup in loot)
                {
                    if (pickup == null || pickup.IsHeld) continue;
                    Color color = pickup.kind == LunarPickupKind.Weapon ? new Color(1f, 0.85f, 0.2f) : pickup.kind == LunarPickupKind.Ammo ? new Color(0.4f, 0.9f, 1f) : Color.white;
                    Blip(pickup.transform.position, toLocal, center, radius, false, 5, color);
                }
            // Reparacion (verde) y piezas (azul): se muestran en el borde si estan lejos.
            if (repair != null && !repair.Repaired)
                Blip(repair.transform.position, toLocal, center, radius, true, 12, new Color(0.3f, 1f, 0.45f));
            foreach (var part in parts)
                if (part != null && !part.Installed)
                    Blip(part.transform.position, toLocal, center, radius, true, 10, new Color(0.25f, 0.6f, 1f));
            // Enemigos (rojo) y jefes (calor)
            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.IsDead) continue;
                if (enemy.isBoss)
                {
                    Vector2? p = Project(enemy.transform.position, toLocal, center, radius, true);
                    if (p == null) continue;
                    float halo = 22f + 10f * pulse;
                    Draw(dot, new Rect(p.Value.x - halo / 2, p.Value.y - halo / 2, halo, halo), new Color(1f, 0.25f, 0.05f, 0.35f + 0.25f * pulse));
                    Draw(dot, new Rect(p.Value.x - 7, p.Value.y - 7, 14, 14), new Color(1f, 0.55f, 0.1f));
                    Draw(dot, new Rect(p.Value.x - 3.5f, p.Value.y - 3.5f, 7, 7), new Color(1f, 0.95f, 0.5f));
                }
                else Blip(enemy.transform.position, toLocal, center, radius, false, 7, new Color(1f, 0.15f, 0.12f));
            }
            // Jugador
            Draw(dot, new Rect(center.x - 4, center.y - 4, 8, 8), Color.white);
            Draw(Texture2D.whiteTexture, new Rect(center.x - 1, center.y - 12, 2, 8), Color.white);

            GUI.color = old;
            GUI.Label(new Rect(area.x + 6, area.yMax - 18, size, 18), $"RADAR {range:0} m");
            GUI.matrix = previous;
        }

        private Vector2? Project(Vector3 world, Quaternion toLocal, Vector2 center, float radius, bool clampToEdge)
        {
            Vector3 offset = toLocal * Vector3.ProjectOnPlane(world - transform.position, Vector3.up);
            Vector2 flat = new Vector2(offset.x, -offset.z) / range * (radius - 8);
            if (flat.magnitude > radius - 8)
            {
                if (!clampToEdge) return null;
                flat = flat.normalized * (radius - 8);
            }
            return center + flat;
        }

        private void Blip(Vector3 world, Quaternion toLocal, Vector2 center, float radius, bool clamp, float diameter, Color color)
        {
            Vector2? p = Project(world, toLocal, center, radius, clamp);
            if (p == null) return;
            Draw(dot, new Rect(p.Value.x - diameter / 2, p.Value.y - diameter / 2, diameter, diameter), color);
        }

        private static Rect Inset(Rect r, float amount) => new Rect(r.x + amount, r.y + amount, r.width - amount * 2, r.height - amount * 2);

        private static void Draw(Texture texture, Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, texture);
        }

        private static void EnsureTextures()
        {
            if (disc != null) return;
            disc = Circle(128, 0f, 1f, false);
            ring = Circle(128, 0.94f, 1f, false);
            dot = Circle(32, 0f, 1f, true);
        }

        private static Texture2D Circle(int resolution, float inner, float outer, bool soft)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            float half = resolution / 2f;
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    float d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                    float a = d <= outer && d >= inner ? 1f : 0f;
                    if (soft) a = Mathf.Clamp01((1f - d) * 3f);
                    else a *= Mathf.Clamp01((outer - d) * half) ;
                    texture.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            texture.Apply();
            return texture;
        }
    }
}
