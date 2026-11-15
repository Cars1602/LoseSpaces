using System;
using UnityEngine;

namespace LunaCero
{
    /// <summary>
    /// Iconos y piezas de interfaz del HUD (corazones, burbujas de oxigeno, rayo, propulsor, linterna,
    /// siluetas de armas, paneles redondeados y la tecla de interaccion). Se dibujan por codigo una sola
    /// vez (con suavizado) para no depender de imagenes externas; se tinen con el color que haga falta.
    /// </summary>
    public static class LunarHud
    {
        public static readonly Color Cyan = new Color(0.35f, 0.88f, 1f);
        public static readonly Color Gold = new Color(1f, 0.78f, 0.25f);
        public static readonly Color Text = new Color(0.92f, 0.96f, 1f);

        private static Texture2D heart, bubble, ring, bolt, jetpack, flashlight, rifle, pistol, wrench, segment, panel, panelGold, keycap, shield;
        private static GUIStyle panelStyle, goldStyle, keyStyle;
        public static GUIStyle Label, Small, Value, Big, Center, KeyLetter, Title;

        // ------------------------------------------------------------ iconos
        public static Texture2D Heart => heart ?? (heart = Mask(64, 56, (x, y) =>
        {
            float px = (x - 0.5f) * 2.7f, py = (y - 0.47f) * 2.8f;
            float a = px * px + py * py - 1f;
            return a * a * a - px * px * py * py * py <= 0f ? 1f : 0f;
        }));

        public static Texture2D Bubble => bubble ?? (bubble = Mask(48, 48, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f));
            if (d > 0.47f) return 0f;
            if (d > 0.38f) return 1f;                                            // borde
            float shine = Vector2.Distance(new Vector2(x, y), new Vector2(0.36f, 0.64f)) < 0.1f ? 1f : 0.72f; // brillo
            return shine;
        }));

        public static Texture2D Ring => ring ?? (ring = Mask(48, 48, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f));
            return d <= 0.47f && d > 0.37f ? 1f : 0f;
        }));

        public static Texture2D Bolt => bolt ?? (bolt = Mask(48, 64, (x, y) => Inside(x, y,
            new Vector2(0.55f, 1f), new Vector2(0.14f, 0.43f), new Vector2(0.45f, 0.43f), new Vector2(0.34f, 0f),
            new Vector2(0.86f, 0.6f), new Vector2(0.55f, 0.6f), new Vector2(0.76f, 1f)) ? 1f : 0f));

        public static Texture2D Jetpack => jetpack ?? (jetpack = Mask(56, 64, (x, y) =>
        {
            if (RoundRect(x, y, 0.12f, 0.28f, 0.42f, 0.92f, 0.12f) || RoundRect(x, y, 0.58f, 0.28f, 0.88f, 0.92f, 0.12f)) return 1f;
            if (x > 0.42f && x < 0.58f && y > 0.45f && y < 0.8f) return 1f;                       // union
            if ((x > 0.18f && x < 0.36f || x > 0.64f && x < 0.82f) && y > 0.2f && y < 0.29f) return 1f; // toberas
            // llamas
            if (Inside(x, y, new Vector2(0.19f, 0.19f), new Vector2(0.35f, 0.19f), new Vector2(0.27f, 0f))) return 0.75f;
            if (Inside(x, y, new Vector2(0.65f, 0.19f), new Vector2(0.81f, 0.19f), new Vector2(0.73f, 0f))) return 0.75f;
            return 0f;
        }));

        public static Texture2D Flashlight => flashlight ?? (flashlight = Mask(72, 48, (x, y) =>
        {
            if (RoundRect(x, y, 0.03f, 0.36f, 0.42f, 0.64f, 0.06f)) return 1f;
            if (Inside(x, y, new Vector2(0.42f, 0.36f), new Vector2(0.56f, 0.24f), new Vector2(0.56f, 0.76f), new Vector2(0.42f, 0.64f))) return 1f;
            // haz de luz con tres rayos
            foreach (float angle in new[] { -0.32f, 0f, 0.32f })
            {
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 rel = new Vector2(x - 0.62f, (y - 0.5f) * 0.66f);
                float along = Vector2.Dot(rel, dir);
                float across = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
                if (along > 0.04f && along < 0.36f && across < 0.035f) return 0.9f;
            }
            return 0f;
        }));

        public static Texture2D Rifle => rifle ?? (rifle = Mask(160, 56, (x, y) =>
        {
            if (Inside(x, y, new Vector2(0.02f, 0.3f), new Vector2(0.06f, 0.66f), new Vector2(0.22f, 0.7f), new Vector2(0.22f, 0.48f), new Vector2(0.08f, 0.3f))) return 1f; // culata
            if (x > 0.2f && x < 0.66f && y > 0.48f && y < 0.72f) return 1f;  // cuerpo
            if (x > 0.66f && x < 0.97f && y > 0.57f && y < 0.66f) return 1f; // canon
            if (x > 0.66f && x < 0.8f && y > 0.5f && y < 0.58f) return 1f;  // guardamanos
            if (Inside(x, y, new Vector2(0.27f, 0.5f), new Vector2(0.34f, 0.5f), new Vector2(0.31f, 0.2f), new Vector2(0.24f, 0.2f))) return 1f; // empunadura
            if (Inside(x, y, new Vector2(0.42f, 0.5f), new Vector2(0.52f, 0.5f), new Vector2(0.55f, 0.16f), new Vector2(0.46f, 0.16f))) return 1f; // cargador
            if (x > 0.36f && x < 0.58f && y > 0.74f && y < 0.86f) return 1f; // mira
            if (x > 0.42f && x < 0.46f && y > 0.7f && y < 0.75f) return 1f;
            return 0f;
        }));

        public static Texture2D Pistol => pistol ?? (pistol = Mask(96, 64, (x, y) =>
        {
            if (x > 0.12f && x < 0.9f && y > 0.56f && y < 0.82f) return 1f;
            if (Inside(x, y, new Vector2(0.16f, 0.57f), new Vector2(0.42f, 0.57f), new Vector2(0.34f, 0.06f), new Vector2(0.1f, 0.06f))) return 1f;
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.52f));
            return d < 0.13f && d > 0.08f && y < 0.56f ? 1f : 0f; // guardamonte
        }));

        public static Texture2D Wrench => wrench ?? (wrench = Mask(64, 64, (x, y) =>
        {
            Vector2 p = new Vector2(x, y), a = new Vector2(0.2f, 0.2f), b = new Vector2(0.72f, 0.72f);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / (b - a).sqrMagnitude);
            if (Vector2.Distance(p, a + (b - a) * t) < 0.07f) return 1f;
            if (Vector2.Distance(p, new Vector2(0.18f, 0.18f)) < 0.1f) return 1f;
            bool head = Vector2.Distance(p, new Vector2(0.76f, 0.76f)) < 0.2f;
            bool notch = Vector2.Distance(p, new Vector2(0.86f, 0.86f)) < 0.11f;
            return head && !notch ? 1f : 0f;
        }));

        public static Texture2D Shield => shield ?? (shield = Mask(48, 56, (x, y) =>
            Inside(x, y, new Vector2(0.5f, 0.98f), new Vector2(0.92f, 0.82f), new Vector2(0.86f, 0.38f), new Vector2(0.5f, 0.02f), new Vector2(0.14f, 0.38f), new Vector2(0.08f, 0.82f)) ? 1f : 0f));

        /// <summary>Arco rojo del indicador de dano (se gira hacia el atacante alrededor de la mira).</summary>
        public static Texture2D DamageArc => damageArc ?? (damageArc = Mask(128, 40, (x, y) =>
        {
            float px = x * 128f, py = y * 40f;
            float d = Vector2.Distance(new Vector2(px, py), new Vector2(64f, -150f));
            if (d < 172f || d > 190f) return 0f;
            float edge = 1f - Mathf.Abs(d - 181f) / 9f;
            return Mathf.Clamp01(edge * 2f) * Mathf.Clamp01(1f - Mathf.Abs(px - 64f) / 64f) * 1.4f;
        }));
        private static Texture2D damageArc;

        /// <summary>Punto difuso (humo, chispas y brillos de particulas).</summary>
        public static Texture2D SoftDot => softDot ?? (softDot = Mask(32, 32, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2f;
            return Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d);
        }));
        private static Texture2D softDot;
        private static readonly System.Collections.Generic.Dictionary<Color, Material> particleMaterials = new System.Collections.Generic.Dictionary<Color, Material>();

        /// <summary>Material de particula suave (Sprites/Default siempre se incluye en la compilacion).</summary>
        public static Material Particle(Color color)
        {
            if (particleMaterials.TryGetValue(color, out var material) && material != null) return material;
            material = new Material(Shader.Find("Sprites/Default")) { mainTexture = SoftDot, color = color };
            particleMaterials[color] = material;
            return material;
        }

        /// <summary>Segmento inclinado de las barras de energia y propulsor.</summary>
        public static Texture2D Segment => segment ?? (segment = Mask(32, 20, (x, y) => Inside(x, y,
            new Vector2(0.22f, 1f), new Vector2(1f, 1f), new Vector2(0.78f, 0f), new Vector2(0f, 0f)) ? 1f : 0f));

        public static Texture2D WeaponIcon(LunarWeaponData data)
        {
            if (data == null) return Rifle;
            if (data.IsMelee) return Wrench;
            return data.id == "pistola" ? Pistol : Rifle;
        }

        // ------------------------------------------------------------ dibujo
        public static void Icon(Rect area, Texture texture, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }

        public static void Fill(Rect area, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        /// <summary>Panel oscuro translucido con esquinas redondeadas y borde de color.</summary>
        public static void Panel(Rect area, bool gold = false)
        {
            EnsureStyles();
            if (Event.current.type == EventType.Repaint) (gold ? goldStyle : panelStyle).Draw(area, false, false, false, false);
        }

        /// <summary>Barra de segmentos inclinados (como en el panel de energia).</summary>
        public static void Segments(Rect area, float fraction, int count, Color color, Color empty)
        {
            float gap = 3f, width = (area.width - gap * (count - 1)) / count;
            float filled = Mathf.Clamp01(fraction) * count;
            for (int i = 0; i < count; i++)
            {
                var r = new Rect(area.x + i * (width + gap), area.y, width, area.height);
                float part = Mathf.Clamp01(filled - i);
                Icon(r, Segment, empty);
                if (part > 0f) Icon(r, Segment, new Color(color.r, color.g, color.b, color.a * (0.35f + 0.65f * part)));
            }
        }

        /// <summary>Tecla (por ejemplo E) con borde claro.</summary>
        public static void Key(Rect area, string key)
        {
            EnsureStyles();
            if (Event.current.type == EventType.Repaint) keyStyle.Draw(area, false, false, false, false);
            GUI.Label(area, key, KeyLetter);
        }

        /// <summary>Aviso de interaccion: [tecla] titulo + linea secundaria, centrado en x.</summary>
        public static void Prompt(float centerX, float y, string key, string title, string subtitle, bool gold = false)
        {
            EnsureStyles();
            float titleWidth = Label.CalcSize(new GUIContent(title)).x;
            float subWidth = string.IsNullOrEmpty(subtitle) ? 0f : Small.CalcSize(new GUIContent(subtitle)).x;
            bool hasKey = !string.IsNullOrEmpty(key);
            float left = hasKey ? 58f : 16f;
            float width = Mathf.Max(titleWidth, subWidth) + left + 18f;
            float height = string.IsNullOrEmpty(subtitle) ? 44f : 56f;
            var area = new Rect(centerX - width / 2f, y, width, height);
            Panel(area, gold);
            if (hasKey) Key(new Rect(area.x + 10, area.y + (height - 36f) / 2f, 36, 36), key);
            GUI.Label(new Rect(area.x + left, area.y + (string.IsNullOrEmpty(subtitle) ? 11 : 6), width - left, 24), title, Label);
            if (!string.IsNullOrEmpty(subtitle)) GUI.Label(new Rect(area.x + left, area.y + 30, width - left, 20), subtitle, Small);
        }

        /// <summary>Marcador ligero sobre un objeto del mundo: un punto de color y un texto corto con sombra, sin panel.</summary>
        public static void Marker(float centerX, float y, string text, Color color)
        {
            EnsureStyles();
            Icon(new Rect(centerX - 6, y, 12, 12), Bubble, color);
            var area = new Rect(centerX - 120, y + 13, 240, 20);
            Color previous = Center.normal.textColor;
            Center.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(area.x + 1, area.y + 1, area.width, area.height), text, Center);
            Center.normal.textColor = color;
            GUI.Label(area, text, Center);
            Center.normal.textColor = previous;
        }

        /// <summary>Escala del HUD (base 1280x720) y matriz aplicada; devuelve el tamano virtual.</summary>
        public static Vector2 Begin(out Matrix4x4 previous)
        {
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            EnsureStyles();
            return new Vector2(Screen.width / scale, Screen.height / scale);
        }

        public static float Scale => Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));

        public static void EnsureStyles()
        {
            if (Label != null && panel != null) return;
            panel = RoundedPanel(new Color(0.02f, 0.05f, 0.08f, 0.74f), new Color(0.35f, 0.88f, 1f, 0.85f));
            panelGold = RoundedPanel(new Color(0.12f, 0.08f, 0.01f, 0.78f), new Color(1f, 0.78f, 0.25f, 0.95f));
            keycap = RoundedPanel(new Color(0.05f, 0.07f, 0.1f, 0.95f), new Color(0.95f, 0.97f, 1f, 0.95f));
            panelStyle = new GUIStyle { border = new RectOffset(12, 12, 12, 12) };
            panelStyle.normal.background = panel;
            goldStyle = new GUIStyle { border = new RectOffset(12, 12, 12, 12) };
            goldStyle.normal.background = panelGold;
            keyStyle = new GUIStyle { border = new RectOffset(12, 12, 12, 12) };
            keyStyle.normal.background = keycap;

            Label = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
            Label.normal.textColor = Text;
            Title = new GUIStyle(Label) { fontSize = 18 };
            Small = new GUIStyle(GUI.skin.label) { fontSize = 13, clipping = TextClipping.Overflow };
            Small.normal.textColor = new Color(0.82f, 0.88f, 0.95f);
            Value = new GUIStyle(Label) { alignment = TextAnchor.MiddleRight };
            Big = new GUIStyle(GUI.skin.label) { fontSize = 38, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Big.normal.textColor = Color.white;
            Center = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            KeyLetter = new GUIStyle(Label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        }

        // ------------------------------------------------------------ generacion de texturas
        /// <summary>Mascara blanca con alfa = cobertura (4x4 muestras por pixel: bordes suaves).</summary>
        private static Texture2D Mask(int width, int height, Func<float, float, float> shape)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float sum = 0f;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            sum += shape((x + (sx + 0.5f) / 4f) / width, (y + (sy + 0.5f) / 4f) / height);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(sum / 16f) * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D RoundedPanel(Color fill, Color border)
        {
            const int size = 32;
            const float radius = 10f, line = 1.6f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    Vector2 q = new Vector2(Mathf.Max(Mathf.Abs(p.x - size / 2f) - (size / 2f - radius), 0f), Mathf.Max(Mathf.Abs(p.y - size / 2f) - (size / 2f - radius), 0f));
                    float distance = q.magnitude - radius; // <0 dentro
                    float outside = Mathf.Clamp01(distance + 0.5f);
                    float edge = Mathf.Clamp01(line - Mathf.Abs(distance + line * 0.5f) + 0.5f);
                    Color c = Color.Lerp(fill, border, edge);
                    c.a *= 1f - outside;
                    texture.SetPixel(x, y, c);
                }
            texture.Apply();
            return texture;
        }

        private static bool RoundRect(float x, float y, float x0, float y0, float x1, float y1, float r)
        {
            float cx = Mathf.Clamp(x, x0 + r, x1 - r), cy = Mathf.Clamp(y, y0 + r, y1 - r);
            if (x < x0 || x > x1 || y < y0 || y > y1) return false;
            return (new Vector2(x - cx, y - cy)).sqrMagnitude <= r * r;
        }

        private static bool Inside(float x, float y, params Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if ((polygon[i].y > y) != (polygon[j].y > y) &&
                    x < (polygon[j].x - polygon[i].x) * (y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            return inside;
        }
    }
}
