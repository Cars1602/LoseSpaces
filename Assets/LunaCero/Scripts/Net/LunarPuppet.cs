using UnityEngine;

namespace LunaCero
{
    /// <summary>
    /// Astronauta de otro jugador: copia visual del astronauta local sin controles ni camara.
    /// Se mueve hacia la ultima posicion recibida, reproduce sus animaciones, enciende su linterna
    /// y muestra su nombre. En el anfitrion es tambien un objetivo para los monstruos.
    /// </summary>
    public sealed class LunarPuppet : MonoBehaviour
    {
        public int Id;
        public string DisplayName = "Astronauta";
        public Color RoleColor = Color.white;
        public float LastUpdate;
        private Animator animator;
        private LunarHealth health;
        private Light lamp;
        private Transform hips;
        private Vector3 hipsRest;
        private string current;
        private Vector3 targetPosition;
        private float yaw, pitch, speed;
        private bool grounded = true, dead, flashlight;
        public bool Emoting { get; set; }
        private float shotUntil;
        private bool placed;
        public LunarHealth Health => health;
        private uint lastSequence;
        private string weaponId;
        private Transform weaponAnchor;
        public bool AcceptSequence(uint sequence) { if(sequence<=lastSequence) return false; lastSequence=sequence; return true; }
        public void SetWeapon(string id)
        {
            if(weaponId==id) return;
            weaponId=id;
            if(weaponAnchor!=null) Destroy(weaponAnchor.gameObject);
            foreach(var r in GetComponentsInChildren<Renderer>(true))
                if(animator==null || !r.transform.IsChildOf(animator.transform)) r.enabled=false;
            weaponAnchor=new GameObject("Arma remota").transform; weaponAnchor.SetParent(transform,false);
            weaponAnchor.localPosition=new Vector3(0.45f,1.15f,0.6f);
            var model=Resources.Load<GameObject>("LunarWeapons/"+id);
            if(model!=null) { var copy=Instantiate(model,weaponAnchor); foreach(var c in copy.GetComponentsInChildren<Collider>()) Destroy(c); }
        }

        private void Awake()
        {
            health = GetComponent<LunarHealth>();
            animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (var t in animator.GetComponentsInChildren<Transform>(true))
                    if (t.name.ToLowerInvariant().Contains("hips")) { hips = t; break; }
                if (hips != null) hipsRest = animator.transform.InverseTransformPoint(hips.position);
            }
            var lampObject = new GameObject("Linterna remota");
            lampObject.transform.SetParent(transform, false);
            lampObject.transform.localPosition = Vector3.up * 1.6f;
            lamp = lampObject.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.range = 26f;
            lamp.spotAngle = 58f;
            lamp.intensity = 3.2f;
            lamp.color = new Color(1f, 0.96f, 0.86f);
            lamp.shadows = LightShadows.None; // las sombras de linternas ajenas costarian demasiado
            lamp.enabled = false;
            LunarCrew.Add(health);
        }

        private void OnDestroy() { LunarCrew.Remove(health); }

        public void Apply(Vector3 position, float yawDegrees, float pitchDegrees, float horizontalSpeed, bool isGrounded, bool isDead, bool lightOn, bool shooting, float hp, float maxHp)
        {
            targetPosition = position;
            yaw = yawDegrees;
            pitch = pitchDegrees;
            speed = horizontalSpeed;
            grounded = isGrounded;
            dead = isDead;
            flashlight = lightOn;
            if (shooting) shotUntil = Time.time + 0.25f;
            if (health != null) health.SetRemote(hp, maxHp);
            LastUpdate = Time.unscaledTime;
            if (!placed) { transform.position = position; placed = true; }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if ((targetPosition - transform.position).sqrMagnitude > 64f) transform.position = targetPosition; // salto grande: teletransporte
            else transform.position = Vector3.Lerp(transform.position, targetPosition, 1f - Mathf.Exp(-14f * dt));
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, yaw, 0), 1f - Mathf.Exp(-14f * dt));
            lamp.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            if(weaponAnchor!=null) weaponAnchor.localRotation=Quaternion.Euler(pitch,0,0);
            lamp.enabled = flashlight && !dead;
            string state = dead ? "Muerte" : !grounded ? "Saltar" : Time.time < shotUntil ? "Disparar" : speed > 4.2f ? "Correr" : speed > 0.15f ? "Caminar" : Emoting ? "Emote" : "Reposo";
            if (animator != null && state != current && animator.HasState(0, Animator.StringToHash(state)))
            {
                animator.CrossFadeInFixedTime(state, 0.12f);
                current = state;
            }
        }

        private Transform rightHand;
        private bool handSearched;

        private void LateUpdate()
        {
            // El arma del companero sigue a su mano derecha (antes flotaba delante del cuerpo).
            if (weaponAnchor != null)
            {
                if (!handSearched) { handSearched = true; rightHand = LunarHandHold.FindRightHand(transform); }
                if (rightHand != null)
                {
                    bool melee = LunarWeaponData.Find(weaponId)?.IsMelee ?? false;
                    Quaternion look = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up), Vector3.up);
                    weaponAnchor.SetPositionAndRotation(rightHand.position, melee ? look * Quaternion.Euler(-65f, 0f, 0f) : look);
                }
            }
            if (hips == null || animator == null || current == "Muerte") return;
            Transform root = animator.transform;
            Vector3 local = root.InverseTransformPoint(hips.position);
            local.x = hipsRest.x;
            local.z = hipsRest.z;
            hips.position = root.TransformPoint(local);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || LunarPlayerRole.MenuOpen) return;
            var view = Camera.main;
            if (view == null) return;
            Vector3 head = transform.position + Vector3.up * 2.1f;
            float distance = Vector3.Distance(view.transform.position, head);
            if (distance > 80f) return;
            Vector3 screen = view.WorldToScreenPoint(head);
            if (screen.z <= 0) return;
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 };
            style.normal.textColor = dead ? new Color(1f, 0.4f, 0.4f) : RoleColor;
            GUI.Label(new Rect(screen.x / scale - 110, (Screen.height - screen.y) / scale - 12, 220, 22), dead ? DisplayName + " (caido)" : $"{DisplayName}  {distance:0} m", style);
            GUI.matrix = previous;
        }
    }
}
