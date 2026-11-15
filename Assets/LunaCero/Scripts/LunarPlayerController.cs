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
        [SerializeField] private float jetpackThrust = 3.4f;
        [SerializeField] private float jetpackMaxRise = 3.5f;
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
        [SerializeField] private float respawnBelow = -15f;

        private readonly RaycastHit[] groundHits = new RaycastHit[16];
        private Rigidbody body;
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

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            health = GetComponent<LunarHealth>();
            status = GetComponent<LunarPlayerStatus>();
            vitals = GetComponent<LunarVitals>();
            body.useGravity = false;
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
            IsGrounded = ProbeGround() && body.velocity.y <= 0.1f;
            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            body.MoveRotation(heading);
            if (role == null) TryGetComponent(out role);
            float roleSpeed = role != null && role.HasRole ? role.Definition.moveSpeed : 1f;
            Vector3 desired = heading * new Vector3(movement.x, 0f, movement.y) * (sprint ? sprintSpeed : walkSpeed) * roleSpeed;
            if (status != null && status.GetForcedMovement(out Vector3 forced)) desired = forced;
            Vector3 horizontal = new Vector3(body.velocity.x, 0f, body.velocity.z);
            horizontal = Vector3.MoveTowards(horizontal, desired, (IsGrounded ? acceleration : airAcceleration) * Time.fixedDeltaTime);
            float vertical = body.velocity.y;
            if (IsGrounded && jumpRequestedUntil >= Time.time)
            {
                vertical = Mathf.Sqrt(2f * gravity * jumpHeight);
                jumpRequestedUntil = -1f;
                IsGrounded = false;
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
            body.AddForce(Vector3.down * gravity, ForceMode.Acceleration);
            if (body.position.y < respawnBelow)
            {
                if (health != null) health.Damage(health.Maximum);
                else Respawn();
            }
        }

        private bool ProbeGround()
        {
            // The root is at the soles. Ignore all four colliders belonging to this body.
            int count = Physics.SphereCastNonAlloc(body.position + Vector3.up * 0.28f, 0.18f,
                Vector3.down, groundHits, 0.1f + groundProbeDistance, groundLayers, QueryTriggerInteraction.Ignore);
            float minNormalY = Mathf.Cos(maxSlope * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
                if (groundHits[i].rigidbody != body && groundHits[i].normal.y >= minNormalY) return true;
            return false;
        }

        /// <summary>Multijugador: cada jugador aparece en un sitio distinto junto a la base.</summary>
        public void SetSpawn(Vector3 position)
        {
            spawnPosition = position;
            Respawn();
        }

        public void Respawn()
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = spawnPosition;
            body.rotation = spawnRotation;
            yaw = spawnRotation.eulerAngles.y;
            pitch = 0f;
            jumpRequestedUntil = -1f;
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
            GUI.Box(new Rect(12, 12, 490, 80), "LUNA CERO | Prototipo de movimiento");
            GUI.Label(new Rect(24, 38, 460, 24), "WASD: mover | Shift: correr | Espacio: salto lunar");
            GUI.Label(new Rect(24, 60, 460, 24), "Raton: mirar | Esc: liberar | Clic: capturar | R: reiniciar");
            GUI.Label(new Rect(Screen.width / 2f - 5, Screen.height / 2f - 10, 20, 20), "+");
        }
    }
}
