using UnityEngine;

namespace LunaCero
{
    /// <summary>Soporte vital del astronauta: oxigeno y energia (estamina para correr).</summary>
    [RequireComponent(typeof(LunarHealth))]
    public sealed class LunarVitals : MonoBehaviour
    {
        [Header("Oxigeno")]
        [SerializeField] private float maxOxygen = 100f;
        [SerializeField] private float oxygenDrain = 0.4f;          // por segundo en reposo/caminando
        [SerializeField] private float sprintOxygenMultiplier = 2.5f;
        [SerializeField] private float stationOxygenRefill = 25f;   // por segundo dentro de una estacion
        [SerializeField] private float suffocationDamage = 6f;      // vida por segundo sin oxigeno
        [Header("Energia")]
        [SerializeField] private float maxStamina = 100f;
        [SerializeField] private float sprintStaminaDrain = 22f;
        [SerializeField] private float staminaRegen = 18f;
        [SerializeField] private float staminaRegenDelay = 0.8f;
        [SerializeField] private float exhaustedThreshold = 25f;

        private LunarHealth health;
        private LunarPlayerController controller;
        private LunarHealingStation[] stations = new LunarHealingStation[0];
        private float nextStationScan;
        private float staminaRegenAt;

        public float Oxygen { get; private set; }
        public float MaxOxygen => maxOxygen;
        public float Stamina { get; private set; }
        public float MaxStamina => maxStamina * Role.stamina;
        private LunarRoleDefinition Role => LunarPlayerRole.Of(this);
        public bool Exhausted { get; private set; }
        public bool Refilling { get; private set; }
        public bool Suffocating => Oxygen <= 0f;
        public bool CanSprint => !Exhausted && Stamina > 0f;

        private void Awake()
        {
            health = GetComponent<LunarHealth>();
            controller = GetComponent<LunarPlayerController>();
            Oxygen = maxOxygen;
            Stamina = maxStamina;
            Fuel = maxFuel;
        }

        private void Update()
        {
            if (health.IsDead) return;
            float dt = Time.deltaTime;
            bool sprinting = controller != null && controller.IsSprinting;

            // Energia
            if (sprinting)
            {
                Stamina = Mathf.Max(0f, Stamina - sprintStaminaDrain * dt);
                staminaRegenAt = Time.time + staminaRegenDelay;
                if (Stamina <= 0f) Exhausted = true;
            }
            else if (Time.time >= staminaRegenAt)
            {
                Stamina = Mathf.Min(MaxStamina, Stamina + staminaRegen * dt);
            }
            if (Exhausted && Stamina >= exhaustedThreshold) Exhausted = false;

            // Oxigeno: las estaciones medicas tambien recargan los tanques.
            if (Time.time >= nextStationScan)
            {
                stations = FindObjectsOfType<LunarHealingStation>();
                nextStationScan = Time.time + 1f;
            }
            Refilling = LunarShip.InsideAny(transform.position + Vector3.up) || LunarOxygenZone.InsideAny(transform.position);
            if (!Refilling) foreach (var station in stations)
                if (station != null && station.Contains(transform.position)) { Refilling = true; break; }

            // Sin estaciones en la escena (arena de combate) no hay donde recargar: no se consume.
            if (Refilling) Oxygen = Mathf.Min(maxOxygen, Oxygen + stationOxygenRefill * dt);
            else if (stations.Length > 0 || LunarShip.All.Count > 0 || LunarOxygenZone.All.Count > 0) Oxygen = Mathf.Max(0f, Oxygen - oxygenDrain * Role.oxygenDrain * (sprinting ? sprintOxygenMultiplier : 1f) * dt);

            if (Suffocating) health.Damage(suffocationDamage * dt);
        }

        [Header("Propulsor (combustible propio, no gasta la energia de correr)")]
        [SerializeField] private float maxFuel = 100f;
        [SerializeField] private float fuelRegen = 22f;
        [SerializeField] private float fuelRegenDelay = 0.6f;
        private float fuelRegenAt;
        public float Fuel { get; private set; } = 100f;
        public float MaxFuel => maxFuel * Role.fuel;

        public bool TryUseFuel(float amount)
        {
            if (Fuel <= 0f) return false;
            Fuel = Mathf.Max(0f, Fuel - amount);
            fuelRegenAt = Time.time + fuelRegenDelay;
            return true;
        }

        private void LateUpdate()
        {
            // El combustible se recarga al tocar el suelo.
            if (controller != null && controller.IsGrounded && Time.time >= fuelRegenAt)
                Fuel = Mathf.Min(MaxFuel, Fuel + fuelRegen * Role.fuelRegen * Time.deltaTime);
            // Al elegir el rol cambia la capacidad: se empieza con todo lleno.
            if (!roleFilled && TryGetComponent(out LunarPlayerRole role) && role.HasRole)
            {
                roleFilled = true;
                Stamina = MaxStamina;
                Fuel = MaxFuel;
            }
        }
        private bool roleFilled;

        /// <summary>Gasta energia de correr. Devuelve false si no queda.</summary>
        public bool TryUseStamina(float amount)
        {
            if (Exhausted || Stamina <= 0f) return false;
            Stamina = Mathf.Max(0f, Stamina - amount);
            staminaRegenAt = Time.time + staminaRegenDelay;
            if (Stamina <= 0f) Exhausted = true;
            return true;
        }

        public void AddOxygen(float amount) { Oxygen = Mathf.Clamp(Oxygen + amount, 0f, maxOxygen); }

        /// <summary>Al reaparecer en multijugador: tanques llenos.</summary>
        public void Refill()
        {
            Oxygen = maxOxygen;
            Stamina = MaxStamina;
            Fuel = MaxFuel;
            Exhausted = false;
        }
    }
}
