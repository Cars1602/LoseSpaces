using System;
using UnityEngine;

namespace LunaCero
{
    public enum LunarFireMode
    {
        Hitscan,    // un rayo por proyectil (pistola, rifle, escopeta con varios perdigones)
        Piercing,   // atraviesa varios enemigos (rifle de riel)
        Explosive,  // impacto con dano en area (lanzador de plasma)
        Melee       // objeto contundente: golpe cuerpo a cuerpo, sin balas (se gasta con el uso)
    }

    /// <summary>
    /// Definicion editable de un arma. Los cubos son provisionales: asigna 'model' (vista en mano)
    /// y 'pickupModel' (vista en el armero de la nave) para reemplazarlos por modelos 3D sin tocar codigo.
    /// </summary>
    [Serializable]
    public sealed class LunarWeaponData
    {
        public string id = "arma";
        public string displayName = "Arma";
        public LunarFireMode mode = LunarFireMode.Hitscan;
        [Header("Disparo")]
        public float damage = 25f;
        public float fireInterval = 0.2f;
        public bool automatic = true;
        [Min(1)] public int pellets = 1;
        [Tooltip("Dispersion en grados")] public float spread;
        public float range = 100f;
        [Tooltip("Enemigos que atraviesa (modo Piercing)")] [Min(1)] public int pierce = 1;
        [Tooltip("Radio de explosion (modo Explosive)")] public float explosionRadius = 4f;
        [Tooltip("0 = sin zoom. Clic derecho para apuntar.")] public float zoomFov;
        [Header("Municion")]
        [Min(1)] public int magazineSize = 12;
        public float reloadDuration = 1.25f;
        public int startReserve;
        [Tooltip("Multiplicador de las cajas de municion azules para esta arma")] public float ammoPickupScale = 1f;
        [Header("Aspecto provisional (cubos)")]
        public Color color = new Color(0.3f, 0.3f, 0.35f);
        public Color beamColor = Color.cyan;
        public Vector3 viewSize = new Vector3(0.16f, 0.18f, 0.65f);
        [Header("Modelos 3D (opcional, reemplazan los cubos)")]
        public GameObject model;
        public GameObject pickupModel;
        [Header("Cuerpo a cuerpo (modo Melee)")]
        [Tooltip("Golpes antes de romperse. 0 = no se rompe.")] public int durability;
        [Tooltip("Segundos que el enemigo queda aturdido por golpe")] public float stagger = 0.5f;

        public bool IsMelee => mode == LunarFireMode.Melee;

        public LunarWeaponData Clone() => (LunarWeaponData)MemberwiseClone();

        private static LunarWeaponData[] catalog;
        /// <summary>Todas las armas conocidas (de fuego y contundentes), para recogerlas por id en el mapa.</summary>
        public static LunarWeaponData[] Catalog
        {
            get
            {
                if (catalog == null)
                {
                    var guns = Defaults();
                    var melee = MeleeDefaults();
                    catalog = new LunarWeaponData[guns.Length + melee.Length];
                    guns.CopyTo(catalog, 0);
                    melee.CopyTo(catalog, guns.Length);
                }
                return catalog;
            }
        }

        public static LunarWeaponData Find(string id)
        {
            foreach (var data in Catalog) if (data.id == id) return data;
            return null;
        }

        /// <summary>Objetos contundentes: cualquier cosa de la nave sirve para golpear.</summary>
        public static LunarWeaponData[] MeleeDefaults() => new[]
        {
            Melee("llave", "Llave inglesa", 34f, 0.55f, 2.4f, 40, 0.5f, new Color(0.62f, 0.64f, 0.7f), new Vector3(0.07f, 0.05f, 0.5f)),
            Melee("tubo", "Tubo de metal", 42f, 0.75f, 2.8f, 30, 0.7f, new Color(0.5f, 0.52f, 0.55f), new Vector3(0.06f, 0.06f, 0.85f)),
            Melee("extintor", "Extintor", 55f, 1.0f, 2.3f, 20, 1.1f, new Color(0.85f, 0.12f, 0.1f), new Vector3(0.16f, 0.16f, 0.55f)),
            Melee("barra", "Barra de la nave", 48f, 0.85f, 3.0f, 28, 0.8f, new Color(0.9f, 0.7f, 0.15f), new Vector3(0.07f, 0.04f, 1f)),
            Melee("escombro", "Escombro de la nave", 38f, 0.8f, 2.4f, 14, 0.6f, new Color(0.35f, 0.36f, 0.4f), new Vector3(0.2f, 0.16f, 0.45f))
        };

        private static LunarWeaponData Melee(string id, string name, float damage, float interval, float range, int durability, float stagger, Color color, Vector3 size) =>
            new LunarWeaponData
            {
                id = id, displayName = name, mode = LunarFireMode.Melee, damage = damage, fireInterval = interval, automatic = true,
                range = range, magazineSize = 1, reloadDuration = 0, startReserve = 0, ammoPickupScale = 0, durability = durability,
                stagger = stagger, color = color, beamColor = Color.white, viewSize = size
            };

        public static LunarWeaponData[] Defaults() => new[]
        {
            new LunarWeaponData
            {
                model = Resources.Load<GameObject>("LunarWeapons/rifle"), pickupModel = Resources.Load<GameObject>("LunarWeapons/rifle"),
                id = "rifle", displayName = "Rifle de asalto", damage = 16f, fireInterval = 0.09f, automatic = true,
                spread = 1.2f, range = 110f, magazineSize = 30, reloadDuration = 1.8f, startReserve = 60, ammoPickupScale = 1.5f,
                color = new Color(0.85f, 0.45f, 0.1f), beamColor = new Color(1f, 0.75f, 0.3f), viewSize = new Vector3(0.14f, 0.2f, 0.95f)
            },
            new LunarWeaponData
            {
                model = Resources.Load<GameObject>("LunarWeapons/escopeta"), pickupModel = Resources.Load<GameObject>("LunarWeapons/escopeta"),
                id = "escopeta", displayName = "Escopeta", damage = 11f, fireInterval = 0.85f, automatic = false, pellets = 9,
                spread = 7f, range = 28f, magazineSize = 6, reloadDuration = 2.3f, startReserve = 12, ammoPickupScale = 0.5f,
                color = new Color(0.55f, 0.3f, 0.15f), beamColor = new Color(1f, 0.9f, 0.5f), viewSize = new Vector3(0.22f, 0.22f, 0.85f)
            },
            new LunarWeaponData
            {
                model = Resources.Load<GameObject>("LunarWeapons/riel"), pickupModel = Resources.Load<GameObject>("LunarWeapons/riel"),
                id = "riel", displayName = "Rifle de riel", mode = LunarFireMode.Piercing, damage = 140f, fireInterval = 1.3f,
                automatic = false, range = 250f, pierce = 4, zoomFov = 25f, magazineSize = 4, reloadDuration = 2.5f, startReserve = 8,
                ammoPickupScale = 0.3f, color = new Color(0.2f, 0.5f, 0.95f), beamColor = new Color(0.4f, 0.8f, 1f),
                viewSize = new Vector3(0.12f, 0.16f, 1.25f)
            },
            new LunarWeaponData
            {
                model = Resources.Load<GameObject>("LunarWeapons/plasma"), pickupModel = Resources.Load<GameObject>("LunarWeapons/plasma"),
                id = "plasma", displayName = "Lanzador de plasma", mode = LunarFireMode.Explosive, damage = 110f, fireInterval = 1f,
                automatic = false, range = 60f, explosionRadius = 4.5f, magazineSize = 3, reloadDuration = 2.6f, startReserve = 4,
                ammoPickupScale = 0.2f, color = new Color(0.55f, 0.2f, 0.85f), beamColor = new Color(0.85f, 0.4f, 1f),
                viewSize = new Vector3(0.3f, 0.3f, 0.8f)
            }
        };
    }

    /// <summary>Estado en tiempo de juego de un arma que lleva el jugador.</summary>
    public sealed class LunarWeaponSlot
    {
        public readonly LunarWeaponData Data;
        public int Ammo;
        public int Reserve;
        /// <summary>En armas contundentes 'Ammo' son los golpes que le quedan antes de romperse.</summary>
        public LunarWeaponSlot(LunarWeaponData data, int reserve)
        {
            Data = data;
            Ammo = data.IsMelee ? Mathf.Max(0, data.durability) : data.magazineSize;
            Reserve = data.IsMelee ? 0 : reserve;
        }
    }
}

