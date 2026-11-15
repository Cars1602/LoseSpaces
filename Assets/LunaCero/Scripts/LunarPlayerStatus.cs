using UnityEngine;

namespace LunaCero
{
    public enum LunarControlEffect { None, Tongue, Pin, Ride, Charge }

    public sealed class LunarPlayerStatus : MonoBehaviour
    {
        public float BileUntil { get; private set; }
        public float SmokeUntil { get; private set; }
        public LunarControlEffect Control { get; private set; }
        public LunarEnemy Captor { get; private set; }
        public int EscapePresses { get; private set; }
        public bool IsControlled => Control != LunarControlEffect.None;
        public bool BlocksActions => Control == LunarControlEffect.Pin || Control == LunarControlEffect.Charge;
        public bool FlashlightOn => flashlight != null && flashlight.enabled;
        public bool IsBiled => Time.time < BileUntil;
        private Light flashlight;
        private float releaseAt;
        private float immunityUntil;
        private float knockbackUntil;
        private Vector3 forcedVelocity;
        private float nextStruggle;

        private void Start()
        {
            var camera = GetComponentInChildren<Camera>();
            var lamp = new GameObject("Linterna");
            lamp.transform.SetParent(camera.transform, false);
            flashlight = lamp.AddComponent<Light>();
            flashlight.type = LightType.Spot;
            // La luna esta casi a oscuras: la linterna es la luz principal (empieza encendida, T alterna).
            flashlight.range = 32;
            flashlight.spotAngle = 62;
            flashlight.innerSpotAngle = 30;
            flashlight.intensity = 4.5f;
            flashlight.color = new Color(1f, 0.96f, 0.86f);
            flashlight.shadows = LightShadows.Hard; // las sombras suaves cuestan bastante mas por fotograma
            flashlight.enabled = true;
        }

        private void Update()
        {
            if (GetComponent<LunarHealth>().IsDead) { Release(); return; }
            if (Input.GetKeyDown(KeyCode.T) && Cursor.lockState == CursorLockMode.Locked) flashlight.enabled = !flashlight.enabled;
            if (IsControlled && (Captor == null || Captor.IsDead || Time.time >= releaseAt)) Release();
            if (IsControlled && Input.GetKeyDown(KeyCode.Q) && Cursor.lockState == CursorLockMode.Locked) Struggle();
        }

        public void ApplyBile(float seconds)
        {
            BileUntil = Mathf.Max(BileUntil, Time.time + seconds);
            FindObjectOfType<LunarHordeDirector>()?.AttractHorde();
        }

        public void ApplySmoke(float seconds) { SmokeUntil = Mathf.Max(SmokeUntil, Time.time + seconds); }

        public bool Capture(LunarEnemy source, LunarControlEffect effect, float seconds, Vector3 direction)
        {
            if (source == null || source.IsDead || GetComponent<LunarHealth>().IsDead || IsControlled || Time.time < immunityUntil) return false;
            Captor = source;
            Control = effect;
            releaseAt = Time.time + seconds;
            forcedVelocity = Vector3.ProjectOnPlane(direction, Vector3.up).normalized * (effect == LunarControlEffect.Ride ? 3f : 6f);
            EscapePresses = 0;
            return true;
        }

        public void Struggle()
        {
            if (!IsControlled || Time.time < nextStruggle) return;
            nextStruggle = Time.time + 0.22f;
            EscapePresses++;
            if (EscapePresses < 3) return;
            var enemy = Captor;
            Release();
            if (enemy != null) enemy.Stagger(2f);
        }

        public void Release()
        {
            if (!IsControlled) return;
            Captor = null;
            Control = LunarControlEffect.None;
            immunityUntil = Time.time + 3f;
            EscapePresses = 0;
        }

        public void Knockback(Vector3 velocity)
        {
            forcedVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
            knockbackUntil = Time.time + 0.55f;
            var body = GetComponent<Rigidbody>();
            body.velocity = new Vector3(forcedVelocity.x, Mathf.Max(body.velocity.y, velocity.y), forcedVelocity.z);
        }

        public bool GetForcedMovement(out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (Time.time < knockbackUntil) { velocity = forcedVelocity; return true; }
            if (!IsControlled || Captor == null) return false;
            if (Control == LunarControlEffect.Tongue)
            {
                Vector3 direction = Vector3.ProjectOnPlane(Captor.transform.position - transform.position, Vector3.up);
                velocity = direction.magnitude > 1.7f ? direction.normalized * 2.5f : Vector3.zero;
            }
            if (Control == LunarControlEffect.Ride) velocity = forcedVelocity;
            if (Control == LunarControlEffect.Charge && releaseAt - Time.time > 3f) velocity = forcedVelocity;
            return true;
        }

        private void OnGUI()
        {
            float alpha = IsBiled ? 0.22f : Time.time < SmokeUntil ? 0.48f : 0;
            if (alpha > 0)
            {
                GUI.color = IsBiled ? new Color(0.3f, 0.55f, 0.05f, alpha) : new Color(0.25f, 0.25f, 0.3f, alpha);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float center = Screen.width / scale / 2;
            if (IsControlled) GUI.Box(new Rect(center - 220, 200, 440, 60), $"ATRAPADO: {Control}\nPulsa Q tres veces para liberarte ({EscapePresses}/3)");
            else if (IsBiled) GUI.Box(new Rect(center - 160, 200, 320, 35), "MARCADO: la horda te esta buscando");
            else if (Time.time < SmokeUntil) GUI.Box(new Rect(center - 160, 200, 320, 35), "HUMO: sal de la nube");
            GUI.matrix = previous;
        }
    }
}
