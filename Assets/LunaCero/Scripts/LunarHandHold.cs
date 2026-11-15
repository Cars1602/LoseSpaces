using UnityEngine;

namespace LunaCero
{
    /// <summary>
    /// Tercera persona: el arma sigue a la mano derecha del astronauta en vez de flotar al lado del cuerpo.
    /// Cada cuadro se coloca en la posicion del hueso de la mano y apunta hacia donde mira el jugador
    /// (los objetos contundentes se sujetan por el mango, con la cabeza hacia arriba).
    /// </summary>
    public sealed class LunarHandHold : MonoBehaviour
    {
        private Transform hand, anchor, aim;
        private bool melee;
        private float nextSearch;

        /// <summary>Hueso de la mano derecha del modelo (humanoide o por nombre). Null si no hay esqueleto.</summary>
        public static Transform FindRightHand(Transform root)
        {
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (animator.isHuman)
                {
                    var bone = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    if (bone != null) return bone;
                }
                foreach (var t in animator.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace(".", "");
                    if (!n.Contains("hand") && !n.Contains("mano")) continue;
                    if (n.Contains("left") || n.Contains("izq") || n.EndsWith("l") || n.Contains("handl")) continue;
                    if (n.Contains("right") || n.Contains("der") || n.EndsWith("r") || n.Contains("handr") || n.Contains("rhand")) return t;
                }
            }
            return null;
        }

        /// <summary>Coloca las piezas del arma (modelo y caja con la boca de disparo) en la mano.</summary>
        public void Attach(Transform weaponVisual, GameObject model, bool isMelee, Transform aimSource)
        {
            aim = aimSource;
            melee = isMelee;
            if (hand == null) hand = FindRightHand(transform);
            if (hand == null) return; // sin esqueleto: se mantiene la posicion de siempre
            if (anchor == null)
            {
                anchor = new GameObject("Arma en la mano").transform;
                anchor.SetParent(transform, false);
            }
            Place();
            if (weaponVisual != null)
            {
                weaponVisual.SetParent(anchor, false);
                weaponVisual.localRotation = Quaternion.identity;
                weaponVisual.localPosition = new Vector3(0f, 0.02f, weaponVisual.localScale.z * 0.3f);
            }
            if (model == null) return;
            model.transform.SetParent(anchor, false);
            model.transform.localRotation = Quaternion.identity;
            model.transform.localPosition = Vector3.zero;
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = new Bounds(anchor.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
            foreach (var r in renderers)
                for (int i = 0; i < 8; i++)
                    bounds.Encapsulate(anchor.InverseTransformPoint(r.bounds.center + Vector3.Scale(r.bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            // La empunadura queda en la mano: armas por detras del centro; contundentes por el extremo del mango.
            float grip = melee ? bounds.min.z + bounds.size.z * 0.12f : bounds.min.z + bounds.size.z * 0.3f;
            model.transform.localPosition = -new Vector3(bounds.center.x, melee ? bounds.center.y : bounds.min.y + bounds.size.y * 0.35f, grip);
        }

        private void LateUpdate()
        {
            if (anchor == null) return;
            if (hand == null)
            {
                if (Time.unscaledTime < nextSearch) return;
                nextSearch = Time.unscaledTime + 1f;
                hand = FindRightHand(transform);
                if (hand == null) return;
            }
            Place();
        }

        private void Place()
        {
            if (hand == null || anchor == null) return;
            Vector3 forward = aim != null ? aim.forward : transform.forward;
            Vector3 flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (Vector3.Dot(forward, flat) < 0.2f) forward = flat; // la camara orbitando no gira el arma hacia atras
            Quaternion look = Quaternion.LookRotation(forward, Vector3.up);
            anchor.SetPositionAndRotation(hand.position, melee ? look * Quaternion.Euler(-65f, 0f, 0f) : look);
        }

        private void OnDestroy() { if (anchor != null) Destroy(anchor.gameObject); }
    }
}
