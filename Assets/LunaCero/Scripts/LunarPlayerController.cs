using UnityEngine;

namespace LunaCero
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class LunarPlayerController : MonoBehaviour
    {
        [Header("Movimiento")]
        [SerializeField] private float walkSpeed = 3.5f;
        [SerializeField] private float sprintSpeed = 5.5f;
        [SerializeField] private float acceleration = 18f;
        [SerializeField] private float airAcceleration = 3f;
        [Tooltip("Gravedad de juego (la lunar real es 1.62, demasiado flotante)")]
        [SerializeField] private float gravity = 3.2f;
        [SerializeField] private float jumpHeight = 0.85f;
        [Header("Propulsor (mantener Espacio en el aire)")]
        [SerializeField] private float jetpackThrust = 2f;
        [SerializeField] private float jetpackMaxRise = 2.5f;
        [SerializeField] private float jetpackStaminaPerSecond = 28f;
        [SerializeField] private float jetpackDelay = 0.05f;
        [Header("Camara")]
        [SerializeField] private Transform view;
        [SerializeField] private float mouseSensitivity = 2f;
        [SerializeField] private float pitchLimit = 85f;
        [Tooltip("Desmarcado = primera persona. V alterna en juego.")]
        [SerializeField] private bool thirdPerson = false;
        [SerializeField] private float eyeHeight = 1.6f;
        [SerializeField] private float cameraDistance = 4f;
        [SerializeField] private float shoulderOffset = 0.65f;
        [Header("Suelo")]
        [SerializeField] private LayerMask groundLayers = ~0;
        [SerializeField] private float groundProbeDistance = 0.12f;
        [SerializeField] private float maxSlope = 50f;
        [Tooltip("Altura maxima de un escalon que puede subir caminando")]
        [SerializeField] private float stepHeight = 0.35f;
        [SerializeField] private float stepReach = 0.12f;
        [SerializeField] private float respawnBelow = -15f;
        [Header("Respawn seguro")]
        [SerializeField] private float respawnSearchRadius = 5f;
        [SerializeField] private float fallDamage = 20f;

        private readonly RaycastHit[] groundHits = new RaycastHit[16];
        private readonly Collider[] respawnHits = new Collider[32];
        private Rigidbody body;
        private Collider[] movementColliders;
        private Vector3 groundNormal = Vector3.up;
        private float ignoreGroundUntil;
        private Vector2 movement;
        private bool sprint;
        private float jumpRequestedUntil = -1f;
        private float pitch = 12f;
        private float yaw;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        public bool IsGrounded { get; private set; }
        public bool Thrusting { get; private set; }
        private bool jumpHeld;
        private bool jetpackArmed;
        private float airborneSince;
        // Solo se corre (y se gasta energia) con los pies en el suelo; saltar no consume energia.
        public bool IsSprinting => sprint && IsGrounded && movement.sqrMagnitude > 0.01f && enabled;
        private LunarHealth health;
        private LunarPlayerStatus status;
        private LunarVitals vitals;
        private LunarPlayerRole role;
        private float nextFallRecovery;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            movementColliders = GetComponentsInChildren<Collider>();
            health = GetComponent<LunarHealth>();
            status = GetComponent<LunarPlayerStatus>();
            vitals = GetComponent<LunarVitals>();
            body.useGravity = false;
            var walkingMaterial=new PhysicMaterial("Lunar movement") { staticFriction=0, dynamicFriction=0, frictionCombine=PhysicMaterialCombine.Minimum, bounciness=0 };
            foreach(var c in GetComponentsInChildren<Collider>()) if(c.attachedRigidbody==body) c.material=walkingMaterial;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            spawnPosition = body.position;
            spawnRotation = body.rotation;
            yaw = transform.eulerAngles.y;
            SetCursor(true);
        }

        private void Update()
        {
            if (LunarPlayerRole.MenuOpen) SetCursor(false); // eligiendo rol: el clic es para los botones
            else
            {
                if (Input.GetKeyDown(KeyCode.Escape)) SetCursor(false);
                if (Input.GetMouseButtonDown(0)) SetCursor(true);
            }
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                movement = Vector2.zero;
                sprint = false;
                jumpRequestedUntil = -1f;
                jumpHeld = false;
                return;
            }
            // El propulsor se activa con una SEGUNDA pulsacion en el aire (mantener), no al mantener el salto.
            if (IsGrounded) jetpackArmed = false;
            else if (Input.GetButtonDown("Jump") && Time.time - airborneSince > 0.1f) jetpackArmed = true;
            if (!Input.GetButton("Jump")) jetpackArmed = false;
            jumpHeld = jetpackArmed;
            movement = Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
            if (vitals == null) vitals = GetComponent<LunarVitals>();
            sprint = Input.GetKey(KeyCode.LeftShift) && (vitals == null || vitals.CanSprint);
            if (Input.GetButtonDown("Jump")) jumpRequestedUntil = Time.time + 0.15f;
            if (Input.GetKeyDown(KeyCode.V)) thirdPerson = !thirdPerson;
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, thirdPerson ? -25f : -pitchLimit, thirdPerson ? 70f : pitchLimit);
        }

        public bool FirstPerson => !thirdPerson;
        public Transform View => view;
        public float Pitch => pitch;

        private void LateUpdate()
        {
            if (view == null) return;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            view.rotation = rotation;
            if (!thirdPerson)
            {
                // Primera persona: la camara va en los ojos del astronauta.
                view.position = transform.position + Vector3.up * eyeHeight;
                return;
            }
            Vector3 target = transform.position + Vector3.up * 1.45f;
            Vector3 offset = rotation * new Vector3(shoulderOffset, 0.25f, -cameraDistance);
            float distance = offset.magnitude;
            foreach (var hit in Physics.SphereCastAll(target, 0.2f, offset.normalized, distance, ~0, QueryTriggerInteraction.Ignore))
                if (hit.rigidbody != body) distance = Mathf.Min(distance, Mathf.Max(0.15f, hit.distance - 0.1f));
            view.position = target + offset.normalized * distance;
        }

        private void FixedUpdate()
        {
            if (LunarSession.InMultiplayer && LunarPlayerRole.MenuOpen)
            {
                body.velocity = Vector3.zero;
                movement = Vector2.zero;
                jumpHeld = false;
                Thrusting = false;
                return;
            }
            bool supported = ProbeGround();
            IsGrounded = supported && Time.time >= ignoreGroundUntil && Vector3.Dot(body.velocity, groundNormal) <= 1.2f;
            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            body.MoveRotation(heading);
            if (role == null) TryGetComponent(out role);
            float roleSpeed = role != null && role.HasRole ? role.Definition.moveSpeed : 1f;
            Vector3 desired = heading * new Vector3(movement.x, 0f, movement.y) * (sprint ? sprintSpeed : walkSpeed) * roleSpeed;
            if (status != null && status.GetForcedMovement(out Vector3 forced)) desired = forced;
            Vector3 horizontal = new Vector3(body.velocity.x, 0f, body.velocity.z);
            if (IsGrounded)
            {
                desired = Vector3.ProjectOnPlane(desired, groundNormal).normalized * desired.magnitude;
                // Preserve tangential Y velocity while accelerating up ramps.
                horizontal = Vector3.ProjectOnPlane(body.velocity, groundNormal);
            }
            horizontal = Vector3.MoveTowards(horizontal, desired, (IsGrounded ? acceleration : airAcceleration) * Time.fixedDeltaTime);
            float vertical = IsGrounded ? horizontal.y : body.velocity.y;
            if (IsGrounded && jumpRequestedUntil < Time.time) TryStep(desired);
            if (IsGrounded && jumpRequestedUntil >= Time.time)
            {
                vertical = Mathf.Sqrt(2f * gravity * jumpHeight);
                jumpRequestedUntil = -1f;
                IsGrounded = false;
                ignoreGroundUntil = Time.time + 0.2f;
            }
            if (IsGrounded) airborneSince = Time.time;
            Thrusting = !IsGrounded && jumpHeld && Time.time - airborneSince > jetpackDelay && vertical < jetpackMaxRise &&
                        (vitals == null || vitals.TryUseFuel(jetpackStaminaPerSecond * Time.fixedDeltaTime));
            if (Thrusting)
            {
                vertical = Mathf.Min(vertical + (jetpackThrust + gravity) * Time.fixedDeltaTime, jetpackMaxRise);
                // Permite avanzar con control normal mientras propulsa para salir de huecos.
                horizontal = Vector3.MoveTowards(horizontal, desired, acceleration * 0.5f * Time.fixedDeltaTime);
            }
            body.velocity = new Vector3(horizontal.x, vertical, horizontal.z);
            // Sobre una rampa la presion va hacia el suelo, sin empujar cuesta abajo.
            body.AddForce((IsGrounded ? -groundNormal : Vector3.down) * gravity, ForceMode.Acceleration);
            if (body.position.y < respawnBelow && Time.time >= nextFallRecovery) RecoverFromFall();
        }

        private bool ProbeGround()
        {
            // The root is at the soles. Ignore all four colliders belonging to this body.
            int count = Physics.SphereCastNonAlloc(body.position + Vector3.up * 0.28f, 0.18f,
                Vector3.down, groundHits, 0.1f + Mathf.Max(groundProbeDistance,0.25f), groundLayers, QueryTriggerInteraction.Ignore);
            float minNormalY = Mathf.Cos(Mathf.Max(maxSlope,55f) * Mathf.Deg2Rad);
            float nearest = float.MaxValue;
            groundNormal = Vector3.up;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.rigidbody == body || hit.transform.IsChildOf(transform) || hit.distance <= 0f || hit.normal.y < minNormalY || hit.distance >= nearest) continue;
                nearest = hit.distance;
                groundNormal = hit.normal;
            }
            return nearest < float.MaxValue;
        }

        private void TryStep(Vector3 desired)
        {
            Vector3 direction = Vector3.ProjectOnPlane(desired, Vector3.up).normalized;
            if (direction.sqrMagnitude < 0.01f || stepHeight <= 0f) return;
            // Medir el ancho real del cuerpo: funciona con los cuatro colliders del astronauta.
            float reach = stepReach + desired.magnitude * Time.fixedDeltaTime;
            foreach (var collider in movementColliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger || collider.attachedRigidbody != body) continue;
                Vector3 extent = collider.bounds.extents;
                reach = Mathf.Max(reach, Mathf.Abs(direction.x) * extent.x + Mathf.Abs(direction.z) * extent.z + stepReach);
            }
            Vector3 feet = body.position;
            bool blocked = false;
            foreach (var hit in Physics.RaycastAll(feet + Vector3.up * 0.08f, direction, reach, groundLayers, QueryTriggerInteraction.Ignore))
                if (hit.rigidbody != body && !hit.transform.IsChildOf(transform) && hit.normal.y < Mathf.Cos(Mathf.Max(maxSlope,55f) * Mathf.Deg2Rad)) blocked = true;
            if (!blocked) return;

            Vector3 ahead = feet + direction * reach;
            RaycastHit landing = default;
            float nearest = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(ahead + Vector3.up * (stepHeight + 0.05f), Vector3.down, stepHeight + 0.1f, groundLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody == body || hit.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
                landing = hit;
                nearest = hit.distance;
            }
            if (nearest == float.MaxValue || landing.normal.y < Mathf.Cos(Mathf.Max(maxSlope,55f) * Mathf.Deg2Rad)) return;
            if (landing.rigidbody != null && !landing.rigidbody.isKinematic) return;
            float rise = landing.point.y - feet.y;
            if (rise < 0.04f || rise > stepHeight) return;
            Vector3 lift = Vector3.up * (rise + 0.025f);
            // Comprobar todo el volumen y el recorrido ascendente, tambien bajo techos.
            foreach (var hit in body.SweepTestAll(Vector3.up, lift.y, QueryTriggerInteraction.Ignore))
                if (hit.collider != null && !hit.transform.IsChildOf(transform)) return;
            foreach (var collider in movementColliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger || collider.attachedRigidbody != body) continue;
                Bounds bounds = collider.bounds;
                Vector3 extents = Vector3.Max(bounds.extents - Vector3.one * 0.015f, Vector3.one * 0.01f);
                foreach (var obstacle in Physics.OverlapBox(bounds.center + lift + direction * stepReach, extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    if (obstacle.attachedRigidbody != body && !obstacle.transform.IsChildOf(transform)) return;
            }
            body.position += lift;
        }

        /// <summary>Multijugador: cada jugador aparece en un sitio distinto junto a la base.</summary>
        public void SetSpawn(Vector3 position)
        {
            spawnPosition = position;
            Respawn();
        }

        public void Respawn()
        {
            Vector3 safePosition = FindSafeRespawn(spawnPosition);
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = safePosition;
            body.rotation = spawnRotation;
            yaw = spawnRotation.eulerAngles.y;
            pitch = 0f;
            jumpRequestedUntil = -1f;
            movement = Vector2.zero;
            sprint = false;
            jumpHeld = false;
            jetpackArmed = false;
            Thrusting = false;
            status?.Release();
            Physics.SyncTransforms();
        }

        private void RecoverFromFall()
        {
            nextFallRecovery = Time.time + 1f;
            Respawn();
            // Caer fuera del mapa penaliza, pero nunca encadena una muerte sin posibilidad de reaccionar.
            if (health != null && !health.IsDead)
            {
                float damage = Mathf.Min(fallDamage, Mathf.Max(0f, health.Current - 1f));
                if (damage > 0f) health.Damage(damage);
            }
        }

        private Vector3 FindSafeRespawn(Vector3 wanted)
        {
            if (TryRespawnPoint(wanted, out Vector3 safe)) return safe;

            const int directions = 12;
            float step = 1f;
            int rings = Mathf.Max(1, Mathf.CeilToInt(respawnSearchRadius / step));
            for (int ring = 1; ring <= rings; ring++)
            {
                float radius = Mathf.Min(respawnSearchRadius, ring * step);
                for (int i = 0; i < directions; i++)
                {
                    float angle = (i + ring * 0.5f) * Mathf.PI * 2f / directions;
                    Vector3 candidate = wanted + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    if (TryRespawnPoint(candidate, out safe)) return safe;
                }
            }

            // Ultimo recurso: aparece por encima del punto original para no quedar dentro del obstaculo.
            return wanted + Vector3.up * 2f;
        }

        private bool TryRespawnPoint(Vector3 candidate, out Vector3 safe)
        {
            safe = candidate;
            float bestDistance = float.MaxValue;
            bool foundGround = false;
            foreach (var hit in Physics.RaycastAll(candidate + Vector3.up * 5f, Vector3.down, 12f, groundLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody == body || hit.transform.IsChildOf(transform)) continue;
                if (hit.normal.y < Mathf.Cos(Mathf.Max(maxSlope,55f) * Mathf.Deg2Rad) || hit.distance >= bestDistance) continue;
                bestDistance = hit.distance;
                safe = hit.point + Vector3.up * 0.05f;
                foundGround = true;
            }
            if (!foundGround) return false;

            // Volumen aproximado del astronauta completo, desde los pies hasta la cabeza.
            Vector3 bottom = safe + Vector3.up * 0.37f;
            Vector3 top = safe + Vector3.up * 1.5f;
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, 0.32f, respawnHits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider obstacle = respawnHits[i];
                if (obstacle == null || obstacle.attachedRigidbody == body || obstacle.transform.IsChildOf(transform)) continue;
                return false;
            }
            return true;
        }

        private static void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) SetCursor(false);
        }

        private void OnDisable() { SetCursor(false); }

        private void OnGUI()
        {
            if (GetComponent<LunarPlayerCombat>() != null) return;
            GUI.Box(new Rect(12, 12, 490, 80), "LOST IN SPACE | Prototipo de movimiento");
            GUI.Label(new Rect(24, 38, 460, 24), "WASD: mover | Shift: correr | Espacio: salto lunar");
            GUI.Label(new Rect(24, 60, 460, 24), "Raton: mirar | Esc: liberar | Clic: capturar | R: reiniciar");
            GUI.Label(new Rect(Screen.width / 2f - 5, Screen.height / 2f - 10, 20, 20), "+");
        }
    }
}
