using UnityEngine;

namespace LunaCero
{
    /// <summary>
    /// Objetos repartidos por el mapa: armas de fuego, objetos contundentes, municion y botiquines.
    /// Se construyen con primitivas provisionales que brillan un poco (el mapa esta a oscuras);
    /// asigna 'pickupModel' en el arma para usar un modelo 3D. Funciona en el editor y en juego.
    /// </summary>
    public static class LunarLoot
    {
        public static GameObject Weapon(Transform parent, LunarWeaponData data, Vector3 position, float yaw)
        {
            if (data.IsMelee) return Melee(parent, data, position, yaw);
            GameObject item;
            if (data.pickupModel != null)
            {
                item = Object.Instantiate(data.pickupModel, parent);
                if (item.GetComponentInChildren<Collider>() == null) item.AddComponent<BoxCollider>();
            }
            else
            {
                item = new GameObject();
                item.transform.SetParent(parent, false);
                Piece(item.transform, PrimitiveType.Cube, Vector3.zero, data.viewSize * 1.6f, Quaternion.identity, data.color);
                Piece(item.transform, PrimitiveType.Cube, new Vector3(0, -data.viewSize.y * 1.1f, -data.viewSize.z * 0.35f),
                    new Vector3(data.viewSize.x * 1.2f, data.viewSize.y * 1.4f, 0.12f), Quaternion.Euler(-15, 0, 0), new Color(0.12f, 0.12f, 0.14f));
                var box = item.AddComponent<BoxCollider>();
                box.size = new Vector3(data.viewSize.x * 2.2f, data.viewSize.y * 3.5f, data.viewSize.z * 1.7f);
                box.center = new Vector3(0, -data.viewSize.y * 0.5f, 0);
            }
            Finish(item, "Arma " + data.displayName, position + Vector3.up * 0.35f, yaw);
            var pickup = item.AddComponent<LunarPickup>();
            pickup.kind = LunarPickupKind.Weapon;
            pickup.weaponId = data.id;
            pickup.displayName = data.displayName;
            pickup.verb = "tomar";
            return item;
        }

        /// <summary>Objeto contundente (llave, tubo, extintor, barra...) tirado en el suelo.</summary>
        public static GameObject Melee(Transform parent, LunarWeaponData data, Vector3 position, float yaw)
        {
            var item = new GameObject();
            item.transform.SetParent(parent, false);
            var t = item.transform;
            Color c = data.color;
            switch (data.id)
            {
                case "llave":
                    Piece(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.05f, 0.025f, 0.42f), Quaternion.identity, c);
                    Piece(t, PrimitiveType.Cube, new Vector3(0, 0, 0.23f), new Vector3(0.14f, 0.03f, 0.07f), Quaternion.identity, c);
                    Piece(t, PrimitiveType.Cube, new Vector3(0.045f, 0, 0.28f), new Vector3(0.04f, 0.03f, 0.08f), Quaternion.identity, c);
                    break;
                case "tubo":
                    Piece(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.06f, 0.42f, 0.06f), Quaternion.Euler(90, 0, 0), c);
                    Piece(t, PrimitiveType.Cylinder, new Vector3(0, 0, 0.4f), new Vector3(0.085f, 0.03f, 0.085f), Quaternion.Euler(90, 0, 0), c * 0.8f);
                    break;
                case "extintor":
                    Piece(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.16f, 0.25f, 0.16f), Quaternion.Euler(90, 0, 0), c);
                    Piece(t, PrimitiveType.Cube, new Vector3(0, 0, 0.29f), new Vector3(0.06f, 0.06f, 0.1f), Quaternion.identity, new Color(0.15f, 0.15f, 0.17f));
                    Piece(t, PrimitiveType.Cube, new Vector3(0, 0.05f, 0.3f), new Vector3(0.03f, 0.03f, 0.14f), Quaternion.Euler(-25, 0, 0), new Color(0.15f, 0.15f, 0.17f));
                    break;
                case "barra":
                    Piece(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.06f, 0.035f, 1f), Quaternion.identity, c);
                    Piece(t, PrimitiveType.Cube, new Vector3(0, 0, 0.42f), new Vector3(0.07f, 0.045f, 0.12f), Quaternion.identity, new Color(0.2f, 0.2f, 0.22f));
                    break;
                default:
                    Piece(t, PrimitiveType.Cube, Vector3.zero, data.viewSize, Quaternion.Euler(0, 0, 12), c);
                    break;
            }
            var box = item.AddComponent<BoxCollider>();
            Bounds bounds = LocalBounds(item.transform);
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size + Vector3.one * 0.12f, new Vector3(0.3f, 0.25f, 0.3f));
            Finish(item, data.displayName, position + Vector3.up * 0.06f, yaw);
            var pickup = item.AddComponent<LunarPickup>();
            pickup.kind = LunarPickupKind.Weapon;
            pickup.weaponId = data.id;
            pickup.displayName = data.displayName;
            pickup.verb = "agarrar";
            pickup.useOwnModel = true; // en la mano se ve igual que en el suelo
            return item;
        }

        /// <summary>Convierte un objeto existente (p. ej. un escombro de la nave) en arma contundente.</summary>
        public static LunarPickup MakeMelee(GameObject item, string weaponId, string displayName)
        {
            if (item.GetComponentInChildren<Collider>() == null)
            {
                var box = item.AddComponent<BoxCollider>();
                Bounds bounds = LocalBounds(item.transform);
                box.center = bounds.center;
                box.size = Vector3.Max(bounds.size, Vector3.one * 0.25f);
            }
            var pickup = item.AddComponent<LunarPickup>();
            pickup.kind = LunarPickupKind.Weapon;
            pickup.weaponId = weaponId;
            pickup.displayName = displayName;
            pickup.verb = "agarrar";
            pickup.useOwnModel = true;
            item.isStatic = false;
            return pickup;
        }

        public static GameObject Ammo(Transform parent, Vector3 position, int quantity, float yaw)
        {
            var item = new GameObject();
            item.transform.SetParent(parent, false);
            Piece(item.transform, PrimitiveType.Cube, new Vector3(0, 0.16f, 0), new Vector3(0.55f, 0.32f, 0.38f), Quaternion.identity, new Color(0.15f, 0.5f, 1f));
            Piece(item.transform, PrimitiveType.Cube, new Vector3(0, 0.33f, 0), new Vector3(0.58f, 0.04f, 0.4f), Quaternion.identity, new Color(1f, 0.8f, 0.2f));
            item.AddComponent<BoxCollider>().center = new Vector3(0, 0.18f, 0);
            item.GetComponent<BoxCollider>().size = new Vector3(0.6f, 0.4f, 0.42f);
            Finish(item, "Caja de municion", position, yaw);
            var pickup = item.AddComponent<LunarPickup>();
            pickup.kind = LunarPickupKind.Ammo;
            pickup.quantity = quantity;
            pickup.displayName = "caja de municion";
            pickup.verb = "recoger";
            return item;
        }

        public static GameObject Medkit(Transform parent, Vector3 position, float yaw)
        {
            var item = new GameObject();
            item.transform.SetParent(parent, false);
            Piece(item.transform, PrimitiveType.Cube, new Vector3(0, 0.13f, 0), new Vector3(0.42f, 0.26f, 0.3f), Quaternion.identity, Color.white);
            Piece(item.transform, PrimitiveType.Cube, new Vector3(0, 0.13f, -0.152f), new Vector3(0.22f, 0.06f, 0.01f), Quaternion.identity, new Color(0.9f, 0.1f, 0.1f));
            Piece(item.transform, PrimitiveType.Cube, new Vector3(0, 0.13f, -0.152f), new Vector3(0.06f, 0.2f, 0.01f), Quaternion.identity, new Color(0.9f, 0.1f, 0.1f));
            var box = item.AddComponent<BoxCollider>();
            box.center = new Vector3(0, 0.13f, 0);
            box.size = new Vector3(0.45f, 0.3f, 0.34f);
            Finish(item, "Botiquin", position, yaw);
            var pickup = item.AddComponent<LunarPickup>();
            pickup.kind = LunarPickupKind.Medkit;
            pickup.quantity = 1;
            pickup.displayName = "botiquin";
            pickup.verb = "recoger";
            return item;
        }

        private static void Finish(GameObject item, string name, Vector3 position, float yaw)
        {
            item.name = name;
            item.transform.position = position;
            item.transform.rotation = Quaternion.Euler(0, yaw, 0);
        }

        private static void Piece(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Quaternion rotation, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            Kill(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = LunarRuntimeMaterials.Glow(color, 0.35f);
        }

        public static void Kill(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target); else Object.DestroyImmediate(target);
        }

        public static Bounds LocalBounds(Transform space)
        {
            bool first = true;
            var bounds = new Bounds();
            foreach (var filter in space.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || filter.name.EndsWith("_Contorno")) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = space.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (first) { bounds = new Bounds(local, Vector3.zero); first = false; } else bounds.Encapsulate(local);
                }
            }
            return first ? new Bounds(Vector3.zero, Vector3.one * 0.3f) : bounds;
        }
    }
}
