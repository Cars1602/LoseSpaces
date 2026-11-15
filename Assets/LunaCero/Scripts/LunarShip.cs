using System.Collections.Generic;
using UnityEngine;

namespace LunaCero
{
    /// <summary>
    /// Nave de partida construida con cubos provisionales. Contiene el armero (una arma por pedestal),
    /// un casillero de municion y una atmosfera presurizada que recarga el oxigeno.
    /// </summary>
    public sealed class LunarShip : MonoBehaviour
    {
        public static readonly List<LunarShip> All = new List<LunarShip>();
        public Vector3 interiorSize = new Vector3(10f, 4f, 14f);
        public float lockerCooldown = 45f;

        private void OnEnable() { All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        public bool Contains(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            return Mathf.Abs(local.x) <= interiorSize.x / 2 && Mathf.Abs(local.z) <= interiorSize.z / 2 && local.y > -1 && local.y < interiorSize.y;
        }

        public static bool InsideAny(Vector3 position)
        {
            foreach (var ship in All) if (ship != null && ship.Contains(position)) return true;
            return false;
        }

        /// <summary>Zona aproximada (con margen) que ocupa la nave, para no generar rocas encima.</summary>
        public static bool Footprint(Vector3 shipCenter, Vector3 position, float margin = 2.5f) =>
            Mathf.Abs(position.x - shipCenter.x) < 10.5f + margin && position.z - shipCenter.z < 10f + margin && position.z - shipCenter.z > -11f - margin;

        public static LunarShip Build(Transform parent, Vector3 center, IList<LunarWeaponData> weapons)
        {
            var root = new GameObject("Nave de partida");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            var ship = root.AddComponent<LunarShip>();
            var t = root.transform;

            Color hull = new Color(0.72f, 0.74f, 0.78f);
            Color dark = new Color(0.22f, 0.24f, 0.28f);
            Color accent = new Color(0.95f, 0.75f, 0.15f);
            float w = ship.interiorSize.x, h = ship.interiorSize.y, l = ship.interiorSize.z;

            // Casco: paredes, techo y puerta delantera (abierta hacia +Z)
            Block("Pared trasera", t, new Vector3(0, h / 2, -l / 2 - 0.2f), new Vector3(w + 0.8f, h, 0.4f), hull);
            Block("Pared izquierda", t, new Vector3(-w / 2 - 0.2f, h / 2, 0), new Vector3(0.4f, h, l + 0.8f), hull);
            Block("Pared derecha", t, new Vector3(w / 2 + 0.2f, h / 2, 0), new Vector3(0.4f, h, l + 0.8f), hull);
            float doorWidth = 4f, side = (w + 0.8f - doorWidth) / 2;
            Block("Frente izquierdo", t, new Vector3(-(doorWidth + side) / 2, h / 2, l / 2 + 0.2f), new Vector3(side, h, 0.4f), hull);
            Block("Frente derecho", t, new Vector3((doorWidth + side) / 2, h / 2, l / 2 + 0.2f), new Vector3(side, h, 0.4f), hull);
            Block("Dintel", t, new Vector3(0, h - 0.5f, l / 2 + 0.2f), new Vector3(doorWidth, 1f, 0.4f), hull);
            Block("Techo", t, new Vector3(0, h + 0.15f, 0), new Vector3(w + 1f, 0.3f, l + 1f), hull);
            Block("Piso", t, new Vector3(0, 0.03f, 0), new Vector3(w, 0.06f, l), dark, false);
            Block("Rampa", t, new Vector3(0, 0.04f, l / 2 + 1.8f), new Vector3(doorWidth, 0.08f, 3f), accent, false);
            Block("Franja puerta", t, new Vector3(0, h - 0.05f, l / 2 + 0.42f), new Vector3(doorWidth, 0.1f, 0.05f), accent, false);

            // Cabina, alas y motores (decorativos pero solidos)
            Block("Cabina", t, new Vector3(0, 1.8f, -l / 2 - 1.9f), new Vector3(7f, 3f, 3f), hull);
            Block("Parabrisas", t, new Vector3(0, 2.6f, -l / 2 - 3.42f), new Vector3(5f, 1f, 0.06f), new Color(0.2f, 0.75f, 1f), false);
            foreach (float s in new[] { -1f, 1f })
            {
                Block("Ala", t, new Vector3(s * (w / 2 + 2.9f), 1.3f, -1.5f), new Vector3(5f, 0.3f, 6f), hull);
                Block("Motor", t, new Vector3(s * (w / 2 + 2.9f), 1.3f, -5.5f), new Vector3(1.8f, 1.8f, 2.6f), dark);
                Block("Tobera", t, new Vector3(s * (w / 2 + 2.9f), 1.3f, -6.85f), new Vector3(1.3f, 1.3f, 0.1f), new Color(1f, 0.45f, 0.1f), false);
            }

            // Iluminacion interior
            var lamp = new GameObject("Luz interior").AddComponent<Light>();
            lamp.transform.SetParent(t, false);
            lamp.transform.localPosition = new Vector3(0, h - 0.4f, 0);
            lamp.type = LightType.Point;
            lamp.range = 14f;
            lamp.intensity = 1.6f;
            lamp.color = new Color(0.85f, 0.92f, 1f);
            Label(t, "NAVE AURORA", new Vector3(0, h + 0.6f, l / 2 + 0.45f), 0.35f, accent, Quaternion.Euler(0, 180, 0));

            // Armero: un pedestal por arma, pegado a la pared izquierda
            int count = weapons?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var data = weapons[i];
                float z = l / 2 - 2.5f - i * 2.6f;
                Block("Pedestal " + data.displayName, t, new Vector3(-w / 2 + 0.7f, 0.5f, z), new Vector3(1f, 1f, 1.4f), dark);
                CreateWeaponPickup(t, data, new Vector3(-w / 2 + 0.7f, 1.25f, z));
                Label(t, data.displayName, new Vector3(-w / 2 + 0.7f, 2.05f, z), 0.12f, Color.white, Quaternion.Euler(0, -90, 0));
            }
            Label(t, "ARMERO", new Vector3(-w / 2 + 0.05f, h - 0.6f, 0), 0.25f, accent, Quaternion.Euler(0, -90, 0));

            // Casillero de municion en la pared derecha
            var locker = Block("Casillero de municion", t, new Vector3(w / 2 - 0.6f, 1f, 0), new Vector3(1f, 2f, 1.6f), new Color(0.15f, 0.55f, 0.9f));
            var lockerPickup = locker.AddComponent<LunarPickup>();
            lockerPickup.kind = LunarPickupKind.AmmoLocker;
            lockerPickup.displayName = "casillero de municion";
            lockerPickup.verb = "usar";
            lockerPickup.cooldown = ship.lockerCooldown;
            Label(t, "MUNICION", new Vector3(w / 2 - 0.05f, 2.4f, 0), 0.18f, Color.white, Quaternion.Euler(0, 90, 0));
            Label(t, "O2 PRESURIZADO", new Vector3(0, h - 0.6f, -l / 2 + 0.05f), 0.2f, new Color(0.3f, 0.85f, 1f), Quaternion.Euler(0, 180, 0));
            return ship;
        }

        public static void CreateWeaponPickup(Transform parent, LunarWeaponData data, Vector3 localPosition)
        {
            GameObject item;
            if (data.pickupModel != null)
            {
                item = Instantiate(data.pickupModel, parent);
                if (item.GetComponentInChildren<Collider>() == null) item.AddComponent<BoxCollider>();
            }
            else
            {
                item = GameObject.CreatePrimitive(PrimitiveType.Cube);
                item.transform.SetParent(parent, false);
                item.transform.localScale = data.viewSize * 1.6f;
                item.GetComponent<Renderer>().sharedMaterial = LunarRuntimeMaterials.Get(data.color);
            }
            item.name = "Arma " + data.displayName;
            item.transform.localPosition = localPosition;
            item.transform.localRotation = Quaternion.Euler(0, 90, 0);
            var pickup = item.AddComponent<LunarPickup>();
            pickup.kind = LunarPickupKind.Weapon;
            pickup.weaponId = data.id;
            pickup.displayName = data.displayName;
            pickup.verb = "tomar";
        }

        private static GameObject Block(string name, Transform parent, Vector3 localPosition, Vector3 size, Color color, bool collider = true)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = size;
            if (!collider) Destroy(box.GetComponent<Collider>());
            box.GetComponent<Renderer>().sharedMaterial = LunarRuntimeMaterials.Get(color);
            return box;
        }

        public static void Label(Transform parent, string text, Vector3 localPosition, float size, Color color, Quaternion? rotation = null)
        {
            var go = new GameObject("Texto " + text);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            // TextMesh se lee de frente mirando hacia +Z; lo giramos para que mire hacia fuera.
            go.transform.localRotation = rotation ?? Quaternion.identity;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = size;
            mesh.fontSize = 48;
            mesh.color = color;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                mesh.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
        }
    }
}
