using UnityEngine;

namespace LunaCero
{
    [RequireComponent(typeof(LunarPlayerController), typeof(Rigidbody))]
    public sealed class LunarPlayerAnimation : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [Tooltip("Hueso raiz (cadera). Si se deja vacio se busca automaticamente.")]
        [SerializeField] private Transform hips;
        [Tooltip("Mantiene la cadera centrada para que los clips que no son 'in place' no avancen y salten hacia atras al reiniciar el ciclo.")]
        [SerializeField] private bool lockHipsInPlace = true;
        private Rigidbody body;
        private LunarPlayerController controller;
        private LunarPlayerCombat combat;
        private LunarHealth health;
        private LunarPlayerStatus status;
        private string current;
        private float shotUntil;
        private Vector3 hipsRest;
        [Tooltip("Baja/sube el modelo para que los pies toquen el suelo en cada animacion")]
        [SerializeField] private bool groundFeet = true;
        private Transform[] feet = new Transform[0];
        private float restFootHeight;
        private float footOffset;
        private Vector3 modelBaseLocal;
        public void Shot() { shotUntil = Time.time + 0.25f; }
        public void Die() { Play("Muerte", 0.08f); }
        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            controller = GetComponent<LunarPlayerController>();
            combat = GetComponent<LunarPlayerCombat>();
            health = GetComponent<LunarHealth>();
            status = GetComponent<LunarPlayerStatus>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.applyRootMotion = false;
            if (hips == null) hips = FindHips(animator.transform);
            if (hips != null) hipsRest = animator.transform.InverseTransformPoint(hips.position);
            // Pies en la pose de reposo del prefab (donde el modelo esta apoyado en el suelo).
            feet = System.Array.FindAll(animator.GetComponentsInChildren<Transform>(true), t =>
            {
                string n = t.name.ToLowerInvariant();
                return n.Contains("foot") || n.Contains("toebase");
            });
            if (feet.Length > 0) restFootHeight = LowestFoot() - transform.position.y;
            modelBaseLocal = animator.transform.localPosition;
        }

        private float LowestFoot()
        {
            float lowest = float.MaxValue;
            foreach (var foot in feet) if (foot != null) lowest = Mathf.Min(lowest, foot.position.y);
            return lowest;
        }

        /// <summary>
        /// Los clips no siempre mantienen los pies a la altura de la pose de reposo y el personaje
        /// parece flotar. Tras animar, se compara el pie mas bajo con la altura de reposo y se
        /// desplaza el modelo (suavizado) para que vuelva a tocar el suelo.
        /// </summary>
        private void GroundFeet()
        {
            if (!groundFeet || feet.Length == 0 || animator == null) return;
            Transform model = animator.transform;
            bool apply = current != "Muerte" && current != "Saltar" && controller.IsGrounded;
            if (apply)
            {
                model.localPosition = modelBaseLocal; // medir sin la correccion anterior
                float raw = LowestFoot() - transform.position.y;
                float target = Mathf.Clamp(restFootHeight - raw, -0.6f, 0.3f);
                footOffset = Mathf.Lerp(footOffset, target, 1f - Mathf.Exp(-18f * Time.deltaTime));
            }
            Vector3 shift = model.parent != null ? model.parent.InverseTransformVector(Vector3.up * footOffset) : Vector3.up * footOffset;
            model.localPosition = modelBaseLocal + shift;
        }
        private void LateUpdate()
        {
            if (animator == null) return;
            if (health != null && health.IsDead) { Die(); return; }
            float speed = new Vector2(body.velocity.x, body.velocity.z).magnitude;
            bool free = combat == null || (combat.CanAct && !combat.Reloading && !combat.Healing);
            string state = !controller.IsGrounded ? "Saltar" :
                free && Time.time < shotUntil ? "Disparar" :
                free && (status == null || !status.IsControlled) && speed < 0.15f &&
                Cursor.lockState == CursorLockMode.Locked && Input.GetKey(KeyCode.C) ? "Cavar" :
                speed > 4.2f ? "Correr" : speed > 0.15f ? "Caminar" : "Reposo";
            Play(state, 0.12f);
            PinHips();
            GroundFeet();
        }
        // The Animator has already posed the skeleton this frame. The run/walk clips carry
        // forward translation on the hips (no root motion node on the Generic rig), so the
        // body drifts forward and snaps back each loop. Cancel the horizontal drift here.
        private void PinHips()
        {
            if (!lockHipsInPlace || hips == null || current == "Muerte") return;
            Transform root = animator.transform;
            Vector3 local = root.InverseTransformPoint(hips.position);
            local.x = hipsRest.x;
            local.z = hipsRest.z;
            hips.position = root.TransformPoint(local);
        }
        private static Transform FindHips(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("hip") || n.Contains("pelvis")) return t;
            }
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.rootBone != null && skin.rootBone != root) return skin.rootBone;
            return null;
        }
        private void Play(string state, float blend)
        {
            if (animator == null || current == state) return;
            if (!animator.HasState(0, Animator.StringToHash(state))) return;
            animator.CrossFadeInFixedTime(state, blend);
            current = state;
        }
    }
}
