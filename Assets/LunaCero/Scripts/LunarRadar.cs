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
        private float stableYaw;
        private Vector3 stableOrigin;
        private static Texture2D disc, ring, dot, arrow;
        private enum RadarIcon { Enemy, Boss, Resource, Repair, Weapon, Ammo, Medkit }
        private static Texture2D[] icons;
        public static Texture2D EnemyIcon { get { EnsureTextures(); return icons[(int)RadarIcon.Enemy]; } }
        public static Texture2D ResourceIcon { get { EnsureTextures(); return icons[(int)RadarIcon.Resource]; } }
        public static Texture2D RepairIcon { get { EnsureTextures(); return icons[(int)RadarIcon.Repair]; } }
        public static Texture2D PlayerIcon { get { EnsureTextures(); return arrow; } }
        private GUIStyle crewStyle, markerStyle;

        public Rect Area => new Rect(position.x, position.y, size, size + (crew.Count > 0 ? 24 + crew.Count * 34 : 0));

        private void Awake() { controller = GetComponent<LunarPlayerController>(); stableOrigin=transform.position; stableYaw=transform.eulerAngles.y; }
        private void LateUpdate()
        {
            float yaw=controller!=null && controller.View!=null?controller.View.eulerAngles.y:transform.eulerAngles.y;
            stableYaw=Mathf.LerpAngle(stableYaw,yaw,1-Mathf.Exp(-18*Time.unscaledDeltaTime));
            stableOrigin=transform.position;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + scanInterval;
            enemies.Clear(); enemies.AddRange(FindObjectsOfType<LunarEnemy>());
            parts.Clear(); parts.AddRange(FindObjectsOfType<LunarShipPart>());
            if (repair == null) repair = FindObjectOfType<LunarRepairPoint>();
            crew.Clear();
            if (LunarSession.InMultiplayer) crew.AddRange(FindObjectsOfType<LunarPuppet>());
            crew.Sort((a, b) => a.Id.CompareTo(b.Id));
            var definition = LunarPlayerRole.Of(this);
            range = definition.radarRange;
            showLoot = definition.radarLoot;
            loot.Clear();
            if (showLoot)
                foreach (var pickup in FindObjectsOfType<LunarPickup>())
                    if (!pickup.IsHeld && !pickup.RemotelyHeld && (pickup.kind == LunarPickupKind.Weapon || pickup.kind == LunarPickupKind.Ammo || pickup.kind == LunarPickupKind.Medkit))
                        loot.Add(pickup);
        }

        private readonly List<LunarPickup> loot = new List<LunarPickup>();
        private readonly List<LunarPuppet> crew = new List<LunarPuppet>();
        private bool showLoot;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || LunarPlayerRole.MenuOpen) return; // solo dibuja: no hay botones
            EnsureTextures();
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            Color old = GUI.color;
            Rect area = new Rect(position.x, position.y, size, size);
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
            GUIUtility.RotateAroundPivot(Time.time * 120f % 360f, center);
            Draw(Texture2D.whiteTexture, new Rect(center.x, center.y - 1, radius - 4, 2), new Color(0.4f, 1f, 0.6f, 0.45f));
            GUI.matrix = beforeSweep;

            float yaw = stableYaw;
            Quaternion toLocal = Quaternion.Euler(0, -yaw, 0);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);

            // Explorador: armas (amarillo), municion (celeste) y botiquines (blanco), solo dentro del alcance.
            if (showLoot)
                foreach (var pickup in loot)
                {
                    if (pickup == null || pickup.IsHeld || pickup.RemotelyHeld) continue;
                    Color color = pickup.kind == LunarPickupKind.Weapon ? new Color(1f, 0.85f, 0.2f) : pickup.kind == LunarPickupKind.Ammo ? new Color(0.4f, 0.9f, 1f) : Color.white;
                    RadarIcon icon = pickup.kind == LunarPickupKind.Weapon ? RadarIcon.Weapon
                        : pickup.kind == LunarPickupKind.Ammo ? RadarIcon.Ammo : RadarIcon.Medkit;
                    Blip(pickup.transform.position, toLocal, center, radius, false, 8, color, icon);
                }
            // Reparacion (verde) y piezas (azul): se muestran en el borde si estan lejos.
            if (repair != null && !repair.Repaired)
                Blip(repair.transform.position, toLocal, center, radius, true, 11, new Color(0.3f, 1f, 0.45f), RadarIcon.Repair);
            foreach (var part in parts)
                if (part != null && !part.Installed)
                    Blip(part.transform.position, toLocal, center, radius, true, 9, new Color(0.25f, 0.6f, 1f), RadarIcon.Resource);
            // Enemigos (rojo) y jefes (calor)
            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.IsDead) continue;
                if (enemy.isBoss)
                {
                    Vector2? p = Project(enemy.transform.position, toLocal, center, radius, true);
                    if (p == null) continue;
                    float halo = 14f + 4f * pulse;
                    Draw(dot, new Rect(p.Value.x - halo / 2, p.Value.y - halo / 2, halo, halo), new Color(1f, 0.25f, 0.05f, 0.35f + 0.25f * pulse));
                    DrawIcon(RadarIcon.Boss, p.Value, 11, new Color(1f, 0.55f, 0.1f));
                }
                else Blip(enemy.transform.position, toLocal, center, radius, false, 8, new Color(1f, 0.15f, 0.12f), RadarIcon.Enemy);
            }
            // Dibujar al final para que enemigos y piezas no oculten a los companeros.
            DrawCrew(toLocal, center, radius, yaw, area);
            // Jugador
            Draw(dot, new Rect(center.x - 4, center.y - 4, 8, 8), Color.white);
            Draw(Texture2D.whiteTexture, new Rect(center.x - 1, center.y - 12, 2, 8), Color.white);

            GUI.color = old;
            GUI.Label(new Rect(area.x + 6, area.yMax - 18, size, 18), $"RADAR {range:0} m");
            GUI.matrix = previous;
        }

        private void DrawCrew(Quaternion toLocal, Vector2 center, float radius, float yaw, Rect area)
        {
            if (crew.Count == 0) return;
            if (crewStyle == null)
            {
                crewStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip, richText = false };
                markerStyle = new GUIStyle(crewStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            }
            Draw(Texture2D.whiteTexture, new Rect(area.x, area.yMax + 4, size, 20 + crew.Count * 34), new Color(0.015f, 0.035f, 0.055f, 0.9f));
            GUI.color = Color.white;
            crewStyle.normal.textColor = new Color(0.65f, 0.95f, 1f);
            GUI.Label(new Rect(area.x + 6, area.yMax + 6, size - 12, 18), "TRIPULACION  |  TU: CENTRO", crewStyle);
            for (int i = 0; i < crew.Count; i++)
            {
                var mate = crew[i];
                if (mate == null) continue;
                Vector3 offset = mate.transform.position - transform.position;
                float distance = offset.magnitude;
                bool outside = Vector3.ProjectOnPlane(offset, Vector3.up).magnitude > Mathf.Max(1f, range);
                bool fallen = mate.Health != null && mate.Health.IsDead;
                Color color = fallen ? new Color(1f, 0.5f, 0.3f) : Color.Lerp(mate.RoleColor, Color.white, 0.3f);
                Vector2 point = Project(mate.transform.position, toLocal, center, radius, true).Value;
                Draw(dot, new Rect(point.x - 8, point.y - 8, 16, 16), Color.black);
                if (outside) Draw(ring, new Rect(point.x - 8, point.y - 8, 16, 16), color);
                if (!fallen)
                {
                    Matrix4x4 before = GUI.matrix;
                    GUIUtility.RotateAroundPivot(Mathf.DeltaAngle(yaw, mate.transform.eulerAngles.y), point);
                    Draw(arrow, new Rect(point.x - 6, point.y - 8, 12, 16), color);
                    GUI.matrix = before;
                }
                GUI.color = Color.white;
                markerStyle.normal.textColor = fallen ? color : Color.white;
                GUI.Label(new Rect(point.x - 9, point.y - 8, 18, 18), fallen ? "X" : (i + 1).ToString(), markerStyle);
                float rowY = area.yMax + 26 + i * 34;
                crewStyle.normal.textColor = color;
                GUI.Label(new Rect(area.x + 6, rowY, size - 12, 17), $"{i + 1}. {mate.DisplayName}", crewStyle);
                string height = offset.y > 3f ? " | ARRIBA" : offset.y < -3f ? " | ABAJO" : "";
                string state = fallen ? " | CAIDO" : outside ? " | FUERA" : "";
                crewStyle.normal.textColor = Color.white;
                GUI.Label(new Rect(area.x + 16, rowY + 15, size - 22, 17), $"{distance:0} m{height}{state}", crewStyle);
            }
        }

        private Vector2? Project(Vector3 world, Quaternion toLocal, Vector2 center, float radius, bool clampToEdge)
        {
            Vector3 offset = toLocal * Vector3.ProjectOnPlane(world - stableOrigin, Vector3.up);
            Vector2 flat = new Vector2(offset.x, -offset.z) / Mathf.Max(1f, range) * (radius - 8);
            if (flat.magnitude > radius - 8)
            {
                if (!clampToEdge) return null;
                flat = flat.normalized * (radius - 8);
            }
            return center + flat;
        }

        private void Blip(Vector3 world, Quaternion toLocal, Vector2 center, float radius, bool clamp, float diameter, Color color, RadarIcon icon)
        {
            Vector2? p = Project(world, toLocal, center, radius, clamp);
            if (p == null) return;
            DrawIcon(icon, p.Value, diameter, color);
        }

        private static void DrawIcon(RadarIcon icon, Vector2 point, float diameter, Color color)
        {
            Texture2D texture = icons[(int)icon];
            // Borde oscuro fino para conservar contraste sobre anillos y barrido.
            Draw(texture, new Rect(point.x - diameter / 2 - 1, point.y - diameter / 2 - 1, diameter + 2, diameter + 2), new Color(0, 0, 0, 0.9f));
            Draw(texture, new Rect(point.x - diameter / 2, point.y - diameter / 2, diameter, diameter), color);
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
            arrow = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float halfWidth = (31f - y) * 0.46f;
                    bool inside = y >= 3 && Mathf.Abs(x - 15.5f) <= halfWidth;
                    arrow.SetPixel(x, y, inside ? Color.white : Color.clear);
                }
            arrow.Apply();
            icons = new Texture2D[7];
            for (int i = 0; i < icons.Length; i++) icons[i] = CreateIcon((RadarIcon)i);
        }

        // Simbolos geometricos simples, sin fuentes ni archivos externos; se crean una sola vez.
        private static Texture2D CreateIcon(RadarIcon icon)
        {
            const int resolution = 48;
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "Radar " + icon, wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    float u = (x + 0.5f) / resolution * 2f - 1f;
                    float v = (y + 0.5f) / resolution * 2f - 1f;
                    float ax = Mathf.Abs(u), ay = Mathf.Abs(v);
                    bool ink;
                    switch (icon)
                    {
                        case RadarIcon.Enemy: // Rombo de amenaza.
                            ink = ax + ay < 0.9f && ax + ay > 0.43f;
                            break;
                        case RadarIcon.Boss: // Corona de tres puntas.
                            ink = ax < 0.86f && v > -0.65f && v < 0.65f &&
                                (v < -0.08f || ax > 0.58f || ax < 0.19f);
                            break;
                        case RadarIcon.Resource: // Caja con division central.
                            ink = ax < 0.8f && ay < 0.75f && (ax > 0.53f || ay > 0.49f || ax < 0.12f);
                            break;
                        case RadarIcon.Repair: // Engranaje con hueco central.
                            float d = Mathf.Sqrt(u * u + v * v);
                            ink = d > 0.29f && (d < 0.61f || (d < 0.9f &&
                                (ax < 0.18f || ay < 0.18f || Mathf.Abs(ax - ay) < 0.17f)));
                            break;
                        case RadarIcon.Weapon: // Silueta de pistola.
                            ink = (u > -0.8f && u < 0.85f && v > 0.06f && v < 0.58f) ||
                                (u > -0.64f && u < -0.15f && v > -0.72f && v < 0.18f);
                            break;
                        case RadarIcon.Ammo: // Dos cartuchos.
                            float cartridgeX = Mathf.Abs(ax - 0.4f);
                            ink = cartridgeX < 0.23f && v > -0.74f && v < 0.78f - cartridgeX * 1.8f;
                            break;
                        default: // Cruz medica.
                            ink = (ax < 0.24f && ay < 0.8f) || (ay < 0.24f && ax < 0.8f);
                            break;
                    }
                    texture.SetPixel(x, y, ink ? Color.white : Color.clear);
                }
            texture.Apply();
            return texture;
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
