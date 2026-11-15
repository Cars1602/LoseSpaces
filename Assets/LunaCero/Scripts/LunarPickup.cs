using UnityEngine;

namespace LunaCero
{
    // Los valores nuevos van al final para no alterar los prefabs ya serializados.
    public enum LunarPickupKind { Medkit, Ammo, Carryable, Weapon, AmmoLocker, Ice }

    public sealed class LunarPickup : MonoBehaviour
    {
        public LunarPickupKind kind;
        public int quantity = 1;
        public string displayName = "Objeto";
        public string persistenceKey;
        [Tooltip("Texto de la accion en el aviso: 'E: <verbo> <nombre>'")]
        public string verb = "recoger";
        [Tooltip("Solo para Weapon: id del arma en el arsenal del jugador")]
        public string weaponId;
        [Tooltip("Solo para AmmoLocker: segundos entre usos")]
        public float cooldown = 45f;
        [Tooltip("Al cargarlo conserva su orientacion en lugar de alinearse con el punto de carga")]
        public bool keepOrientation;
        [Tooltip("Solo para Weapon: en la mano se usa la forma de este objeto (objetos contundentes, escombros)")]
        public bool useOwnModel;
        public bool IsHeld { get; private set; }
        public float ReadyAt { get; private set; }
        private Rigidbody body;
        private Collider[] colliders;
        private bool originalKinematic;
        private bool originalGravity;
        private Transform originalParent;
        private bool[] colliderStates;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            colliders = GetComponentsInChildren<Collider>();
        }

        private void Start()
        {
            if (kind != LunarPickupKind.Weapon || useOwnModel) return;
            var prefab = Resources.Load<GameObject>("LunarWeapons/" + weaponId);
            if (prefab == null) return;
            // Also replace visuals baked into previously saved maps, keeping their interaction colliders.
            foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            var visual = Instantiate(prefab, transform);
            visual.name = "Modelo 3D del arma";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            Vector3 scale = transform.lossyScale;
            visual.transform.localScale = new Vector3(1f / Mathf.Max(0.001f, Mathf.Abs(scale.x)), 1f / Mathf.Max(0.001f, Mathf.Abs(scale.y)), 1f / Mathf.Max(0.001f, Mathf.Abs(scale.z)));
        }

        // ---------------------------------------------------------------- Multijugador
        /// <summary>
        /// Cliente en red: antes de recoger se pide permiso al anfitrion (dos jugadores no pueden
        /// llevarse la misma arma). Devuelve true si la peticion se envio y hay que esperar.
        /// </summary>
        public static System.Func<LunarPickup, LunarPlayerCombat, bool> RequestUse;
        /// <summary>Se avisa al consumir, cargar o soltar un objeto (para enviarlo a los demas).</summary>
        public static System.Action<LunarPickup> Consumed, Held, Released;
        /// <summary>El anfitrion ya concedio este objeto: se usa sin volver a preguntar.</summary>
        [System.NonSerialized] public bool Granted;
        [System.NonSerialized] public int NetId = -1;
        public bool IsShared => kind != LunarPickupKind.AmmoLocker;

        public bool Use(LunarPlayerCombat player)
        {
            if (player == null || !player.CanAct || IsHeld) return false;
            if (!Granted && IsShared && RequestUse != null && RequestUse(this, player)) return false;
            Granted = false;
            switch (kind)
            {
                case LunarPickupKind.Carryable:
                    bool carried = player.PickUp(this);
                    if (carried) Held?.Invoke(this);
                    return carried;
                case LunarPickupKind.AmmoLocker:
                    if (Time.time < ReadyAt) { player.Notify($"Casillero recargandose: {Mathf.CeilToInt(ReadyAt - Time.time)} s"); return false; }
                    if (!player.RefillAllWeapons()) return false;
                    ReadyAt = Time.time + cooldown * player.LockerCooldownScale;
                    return true;
                case LunarPickupKind.Weapon:
                    var data = player.FindWeapon(weaponId);
                    if (data == null) { player.Notify("Arma desconocida: " + weaponId); return false; }
                    if (useOwnModel)
                    {
                        data = data.Clone();
                        data.displayName = string.IsNullOrEmpty(displayName) ? data.displayName : char.ToUpperInvariant(displayName[0]) + displayName.Substring(1);
                        data.model = BuildHandModel(player.transform);
                    }
                    player.AddWeapon(data);
                    break;
                case LunarPickupKind.Medkit:
                    player.AddMedkits(quantity);
                    break;
                case LunarPickupKind.Ice:
                    player.AddIce(quantity);
                    break;
                default:
                    player.AddAmmo(quantity);
                    break;
            }
            if (!string.IsNullOrEmpty(persistenceKey)) FindObjectOfType<LunarEndlessWorld>()?.MarkCollected(persistenceKey);
            Consumed?.Invoke(this);
            Destroy(gameObject);
            return true;
        }

        /// <summary>
        /// Copia solo la forma (mallas y materiales) del objeto para llevarlo en la mano. El eje mas largo
        /// queda hacia delante (+Z). Se guarda desactivada bajo el jugador y se instancia al equiparla.
        /// </summary>
        private static Transform handModels;

        private GameObject BuildHandModel(Transform owner)
        {
            // Fuera del jugador: la vista en primera persona cambia las sombras de todo lo que cuelga de el.
            if (handModels == null) handModels = new GameObject("Modelos en mano (plantillas)").transform;
            var template = new GameObject("Modelo en mano " + displayName);
            template.SetActive(false);
            template.transform.SetParent(handModels, false);
            var shape = new GameObject("Forma").transform;
            shape.SetParent(template.transform, false);
            foreach (var filter in GetComponentsInChildren<MeshFilter>())
            {
                var source = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || source == null) continue;
                var part = new GameObject(filter.name);
                part.transform.SetParent(shape, false);
                Matrix4x4 local = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                part.transform.localPosition = local.GetColumn(3);
                part.transform.localRotation = Quaternion.Inverse(transform.rotation) * filter.transform.rotation;
                part.transform.localScale = Vector3.Scale(filter.transform.lossyScale, new Vector3(1f / transform.lossyScale.x, 1f / transform.lossyScale.y, 1f / transform.lossyScale.z));
                part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;
            }
            shape.localScale = transform.lossyScale;
            Bounds bounds = LunarLoot.LocalBounds(shape);
            Vector3 size = Vector3.Scale(bounds.size, shape.localScale);
            if (size.x >= size.y && size.x > size.z) shape.localRotation = Quaternion.Euler(0, 90, 0);
            else if (size.y > size.x && size.y > size.z) shape.localRotation = Quaternion.Euler(90, 0, 0);
            return template;
        }

        public void Hold(Transform socket)
        {
            IsHeld = true;
            originalParent = transform.parent;
            colliderStates = new bool[colliders.Length];
            for (int i = 0; i < colliders.Length; i++)
            {
                colliderStates[i] = colliders[i].enabled;
                colliders[i].enabled = false;
            }
            if (body != null)
            {
                originalKinematic = body.isKinematic;
                originalGravity = body.useGravity;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
            Quaternion relative = Quaternion.Inverse(socket.rotation) * transform.rotation;
            transform.SetParent(socket, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = keepOrientation ? relative : Quaternion.identity;
        }

        public void Release(Vector3 position, Vector3 velocity)
        {
            transform.SetParent(originalParent, true);
            transform.position = position;
            IsHeld = false;
            for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = colliderStates[i];
            if (body != null)
            {
                body.isKinematic = originalKinematic;
                body.useGravity = originalGravity;
                if (!body.isKinematic) body.velocity = velocity;
            }
            LastReleaseVelocity = velocity;
            Released?.Invoke(this);
        }
        public Vector3 LastReleaseVelocity { get; private set; }

        /// <summary>Red: otro jugador lo lleva (se oculta) o lo solto (vuelve a verse y a colisionar).</summary>
        public void SetRemoteHeld(bool heldByOther)
        {
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = !heldByOther;
            foreach (var c in colliders) if (c != null) c.enabled = !heldByOther;
            if (body != null) body.isKinematic = heldByOther || ForceKinematic; // sin colision no debe caer
            RemotelyHeld = heldByOther;
        }
        /// <summary>Clientes: la fisica de las piezas la simula el anfitrion; aqui solo se colocan.</summary>
        public bool ForceKinematic
        {
            get => forceKinematic;
            set { forceKinematic = value; if (body != null && !IsHeld) body.isKinematic = value; }
        }
        private bool forceKinematic;
        public Rigidbody Body => body;
        public bool RemotelyHeld { get; private set; }
    }
}

