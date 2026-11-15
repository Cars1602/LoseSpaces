using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LunaCero
{
    /// <summary>
    /// Vista en primera persona: brazos del traje con guantes que sujetan el arma con las dos manos
    /// (mano derecha en la empunadura, izquierda bajo el canon). Balanceo al caminar, inercia con el
    /// raton, retroceso al disparar, bajada al recargar y posicion de apuntado.
    /// El cuerpo del astronauta solo proyecta sombra mientras la vista esta activa.
    /// </summary>
    public sealed class LunarViewModel : MonoBehaviour
    {
        public Vector3 hipPosition = new Vector3(0.2f, -0.2f, 0.42f);
        public Vector3 aimPosition = new Vector3(0f, -0.13f, 0.36f);
        public Vector3 rightShoulder = new Vector3(0.36f, -0.55f, -0.05f);
        public Vector3 leftShoulder = new Vector3(-0.32f, -0.55f, 0.0f);
        public Color suitColor = new Color(0.92f, 0.92f, 0.9f);
        public Color bandColor = new Color(1f, 0.45f, 0.1f);
        public Color gloveColor = new Color(0.2f, 0.21f, 0.24f);

        private LunarPlayerController controller;
        private LunarPlayerCombat combat;
        private Rigidbody body;
        private Camera viewCamera;
        private Transform root, holder, weaponRoot, muzzle, rightGrip, leftGrip;
        private Transform rightArm, leftArm, rightGlove, leftGlove, rightBand, leftBand;
        private LunarWeaponData shownWeapon;
        private bool active;
        private float defaultNear;
        private Vector3 sway, swayVelocity;
        private float kick, bob;
        private struct RendererState
        {
            public bool enabled;
            public ShadowCastingMode shadowCastingMode;

            public RendererState(Renderer renderer)
            {
                enabled = renderer.enabled;
                shadowCastingMode = renderer.shadowCastingMode;
            }
        }

        private readonly Dictionary<Renderer, RendererState> hidden = new Dictionary<Renderer, RendererState>();
        private float nextBodyScan;

        private void Start()
        {
            controller = GetComponent<LunarPlayerController>();
            combat = GetComponent<LunarPlayerCombat>();
            body = GetComponent<Rigidbody>();
            viewCamera = combat != null ? combat.AimCamera : GetComponentInChildren<Camera>();
            if (viewCamera == null) { enabled = false; return; }
            defaultNear = viewCamera.nearClipPlane;
            Build();
            Apply(controller != null && controller.FirstPerson);
        }

        public void Kick() { kick = Mathf.Min(kick + 1f, 1.5f); }

        private float swingStart = -10f, swingDuration = 0.5f;
        /// <summary>Golpe con un objeto contundente: impulso hacia atras y barrido hacia delante.</summary>
        public void Swing(float interval)
        {
            swingStart = Time.time;
            swingDuration = Mathf.Clamp(interval * 0.8f, 0.3f, 0.7f);
        }

        private void Build()
        {
            root = new GameObject("Vista primera persona").transform;
            root.SetParent(viewCamera.transform, false);
            holder = new GameObject("Arma en manos").transform;
            holder.SetParent(root, false);
            holder.localPosition = hipPosition;
            rightArm = Part(PrimitiveType.Capsule, "Brazo derecho", suitColor);
            leftArm = Part(PrimitiveType.Capsule, "Brazo izquierdo", suitColor);
            rightBand = Part(PrimitiveType.Cylinder, "Puno derecho", bandColor);
            leftBand = Part(PrimitiveType.Cylinder, "Puno izquierdo", bandColor);
            rightGlove = Part(PrimitiveType.Sphere, "Guante derecho", gloveColor);
            leftGlove = Part(PrimitiveType.Sphere, "Guante izquierdo", gloveColor);
            rightGlove.localScale = new Vector3(0.085f, 0.075f, 0.11f);
            leftGlove.localScale = new Vector3(0.085f, 0.07f, 0.11f);
        }

        private Transform Part(PrimitiveType type, string name, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = LunarRuntimeMaterials.Get(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        private void RebuildWeapon(LunarWeaponData data)
        {
            shownWeapon = data;
            if (weaponRoot != null) Destroy(weaponRoot.gameObject);
            weaponRoot = new GameObject("Arma " + data.displayName).transform;
            weaponRoot.SetParent(holder, false);
            float length = Mathf.Clamp(data.viewSize.z * 0.5f, 0.24f, 0.6f);
            Bounds bounds;
            if (data.model != null)
            {
                var model = Instantiate(data.model, weaponRoot);
                model.SetActive(true); // las plantillas de objetos recogidos se guardan desactivadas
                foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
                var renderers = model.GetComponentsInChildren<Renderer>();
                bounds = LocalBounds(weaponRoot, renderers);
                float factor = length / Mathf.Max(0.01f, bounds.size.z);
                if(data.IsMelee) factor=Mathf.Min(0.38f/Mathf.Max(0.01f,Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z)),0.13f/Mathf.Max(0.01f,bounds.size.x));
                model.transform.localScale *= factor;
                bounds = LocalBounds(weaponRoot, renderers);
                // La empunadura queda en el origen: canon hacia delante, parte baja a la altura de la mano.
                model.transform.localPosition -= new Vector3(bounds.center.x, bounds.center.y, bounds.min.z + bounds.size.z * 0.3f);
                foreach (var r in renderers) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
                bounds = LocalBounds(weaponRoot, renderers);
            }
            else
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(box.GetComponent<Collider>());
                box.transform.SetParent(weaponRoot, false);
                Vector3 size = data.viewSize / data.viewSize.z * length;
                box.transform.localScale = size;
                box.transform.localPosition = new Vector3(0, 0.02f, length * 0.2f);
                var renderer = box.GetComponent<Renderer>();
                renderer.sharedMaterial = LunarRuntimeMaterials.Get(data.color);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // Empunadura provisional
                var grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(grip.GetComponent<Collider>());
                grip.transform.SetParent(weaponRoot, false);
                grip.transform.localScale = new Vector3(size.x * 0.8f, 0.11f, 0.06f);
                grip.transform.localPosition = new Vector3(0, -0.05f, -0.02f);
                grip.transform.localRotation = Quaternion.Euler(-15, 0, 0);
                grip.GetComponent<Renderer>().sharedMaterial = LunarRuntimeMaterials.Get(new Color(0.12f, 0.12f, 0.14f));
                bounds = new Bounds(box.transform.localPosition, size);
            }
            muzzle = new GameObject("Boca").transform;
            muzzle.SetParent(weaponRoot, false);
            muzzle.localPosition = new Vector3(0, bounds.center.y + bounds.extents.y * 0.3f, bounds.max.z + 0.02f);
            rightGrip = new GameObject("Agarre derecho").transform;
            rightGrip.SetParent(weaponRoot, false);
            rightGrip.localPosition = new Vector3(0.005f, bounds.min.y + 0.01f, -0.01f);
            leftGrip = new GameObject("Agarre izquierdo").transform;
            leftGrip.SetParent(weaponRoot, false);
            leftGrip.localPosition = new Vector3(-0.02f, bounds.min.y - 0.005f, Mathf.Min(bounds.max.z - 0.05f, bounds.center.z + bounds.extents.z * 0.35f));
            if (data.IsMelee)
            {
                // Objeto contundente: agarrado por el mango (las dos manos juntas) y levantado como un bate.
                rightGrip.localPosition = new Vector3(0.01f, bounds.center.y, bounds.min.z + 0.03f);
                leftGrip.localPosition = new Vector3(-0.01f, bounds.center.y, bounds.min.z + 0.13f);
                weaponRoot.localRotation = Quaternion.Euler(-25f, 12f, 0f);
                weaponRoot.localPosition = new Vector3(0.07f, -0.10f, 0.10f);
            }
            if (combat != null) combat.MuzzleOverride = active ? muzzle : null;
        }

        private void Apply(bool firstPerson)
        {
            active = firstPerson;
            root.gameObject.SetActive(firstPerson);
            viewCamera.nearClipPlane = firstPerson ? 0.03f : defaultNear;
            if (combat != null) combat.MuzzleOverride = firstPerson ? muzzle : null;
            SetBodyVisible(!firstPerson);
        }

        // El cuerpo real se apaga por completo en primera persona. Los brazos y el arma de la
        // vista se mantienen porque pertenecen a "Vista primera persona". Tambien se elimina
        // su sombra local para evitar la silueta grande y extrana delante de la camara.
        private void SetBodyVisible(bool visible)
        {
            if (visible)
            {
                foreach (var pair in hidden)
                {
                    if (pair.Key == null) continue;
                    pair.Key.enabled = pair.Value.enabled;
                    pair.Key.shadowCastingMode = pair.Value.shadowCastingMode;
                }
                hidden.Clear();
                return;
            }
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.transform.IsChildOf(root) || renderer.GetComponentInParent<LunarPickup>() != null) continue;
                if (viewCamera != null && renderer.transform.IsChildOf(viewCamera.transform)) continue;
                if (!hidden.ContainsKey(renderer)) hidden[renderer] = new RendererState(renderer);

                renderer.enabled = false;
            }
        }

        private void OnEnable()
        {
            if (root != null && controller != null) Apply(controller.FirstPerson);
        }

        private void OnDisable()
        {
            if (root != null) root.gameObject.SetActive(false);
            SetBodyVisible(true);
            active = false;
        }

        private void OnDestroy() { SetBodyVisible(true); }

        private void LateUpdate()
        {
            if (controller == null || root == null) return;
            if (controller.FirstPerson != active) Apply(controller.FirstPerson);
            if (!active || combat == null) return;
            // Algunos modelos se instalan despues de Start. Se detectan sin dejar que aparezcan
            // dentro de la camara durante el resto de la partida.
            if (Time.unscaledTime >= nextBodyScan)
            {
                nextBodyScan = Time.unscaledTime + 0.25f;
                SetBodyVisible(false);
            }
            var data = combat.CurrentWeapon != null ? combat.CurrentWeapon.Data : null;
            if (data != null && data != shownWeapon)
            {
                RebuildWeapon(data);
                SetBodyVisible(false); // el arma del cuerpo puede haber cambiado de modelo
            }
            if (weaponRoot != null) weaponRoot.gameObject.SetActive(combat.HeldObject == null);

            float dt = Time.deltaTime;
            // Inercia con el raton
            Vector3 targetSway = new Vector3(-Input.GetAxis("Mouse X") * 0.012f, -Input.GetAxis("Mouse Y") * 0.012f, 0);
            sway = Vector3.SmoothDamp(sway, Vector3.ClampMagnitude(targetSway, 0.05f), ref swayVelocity, 0.08f);
            // Balanceo al caminar
            float speed = body != null ? new Vector2(body.velocity.x, body.velocity.z).magnitude : 0f;
            bool grounded = controller.IsGrounded;
            bob += dt * (grounded ? speed * 1.8f : 0f);
            float bobAmount = grounded ? Mathf.Clamp01(speed / 5.5f) : 0f;
            Vector3 bobOffset = new Vector3(Mathf.Sin(bob) * 0.012f, Mathf.Abs(Mathf.Cos(bob)) * -0.014f, 0) * bobAmount;
            // Retroceso
            kick = Mathf.MoveTowards(kick, 0, dt * 7f);
            Vector3 recoil = new Vector3(0, 0.01f, -0.06f) * kick;
            Vector3 basePosition = combat.Aiming ? aimPosition : hipPosition;
            if (combat.Reloading || combat.Healing) basePosition += new Vector3(0, -0.16f, -0.05f);
            if (combat.HeldObject != null) basePosition += new Vector3(0.05f, -0.22f, -0.1f);
            // Golpe: echa el brazo atras (0-35%) y barre en diagonal hacia delante (35-100%).
            float swingT = (Time.time - swingStart) / swingDuration;
            Vector3 swingOffset = Vector3.zero;
            Vector3 swingAngles = Vector3.zero;
            if (swingT >= 0f && swingT < 1f)
            {
                float windUp = Mathf.Clamp01(swingT / 0.35f), strike = Mathf.Clamp01((swingT - 0.35f) / 0.4f);
                float back = Mathf.Sin(windUp * Mathf.PI * 0.5f) * (1f - strike);
                float through = Mathf.Sin(strike * Mathf.PI);
                swingAngles = new Vector3(-25f * back + 75f * through, 30f * back - 35f * through, -15f * through);
                swingOffset = new Vector3(0.06f * back - 0.14f * through, 0.05f * back - 0.03f * through, -0.05f * back + 0.12f * through);
            }
            holder.localPosition = Vector3.Lerp(holder.localPosition, basePosition + sway + bobOffset + recoil + swingOffset, 1f - Mathf.Exp(-(swingT < 1f ? 30f : 16f) * dt));
            Quaternion targetRotation = Quaternion.Euler(-kick * 6f + (combat.Reloading ? 25f : 0f) + swingAngles.x, sway.x * 60f + swingAngles.y, (combat.Reloading ? -20f : 0f) + swingAngles.z);
            holder.localRotation = Quaternion.Slerp(holder.localRotation, targetRotation, 1f - Mathf.Exp(-(swingT < 1f ? 28f : 14f) * dt));

            // Brazos: del hombro (fuera de camara) al guante en cada agarre.
            if (rightGrip != null) PlaceArm(rightArm, rightBand, rightGlove, rightShoulder, root.InverseTransformPoint(rightGrip.position), holder.localRotation);
            if (leftGrip != null) PlaceArm(leftArm, leftBand, leftGlove, leftShoulder, root.InverseTransformPoint(leftGrip.position), holder.localRotation);
        }

        private static void PlaceArm(Transform arm, Transform band, Transform glove, Vector3 shoulder, Vector3 hand, Quaternion handRotation)
        {
            Vector3 along = hand - shoulder;
            float length = along.magnitude;
            Vector3 direction = along / Mathf.Max(0.001f, length);
            Quaternion orient = Quaternion.FromToRotation(Vector3.up, direction);
            arm.localPosition = shoulder + along * 0.45f;
            arm.localRotation = orient;
            arm.localScale = new Vector3(0.12f, length * 0.48f, 0.12f);
            band.localPosition = hand - direction * 0.07f;
            band.localRotation = orient;
            band.localScale = new Vector3(0.115f, 0.025f, 0.115f);
            glove.localPosition = hand;
            glove.localRotation = handRotation;
        }

        private static Bounds LocalBounds(Transform space, Renderer[] renderers)
        {
            bool first = true;
            var bounds = new Bounds();
            foreach (var renderer in renderers)
            {
                // Limites de la malla en su propio espacio (exactos aunque la camara este inclinada).
                Bounds b;
                if (renderer is SkinnedMeshRenderer skinned) b = skinned.localBounds;
                else if (renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) b = filter.sharedMesh.bounds;
                else continue;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = space.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first) { bounds = new Bounds(local, Vector3.zero); first = false; } else bounds.Encapsulate(local);
                }
            }
            return first ? new Bounds(Vector3.zero, Vector3.one * 0.1f) : bounds;
        }
    }
}
