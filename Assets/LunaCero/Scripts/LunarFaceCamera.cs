using UnityEngine;

namespace LunaCero
{
    /// <summary>Gira un letrero (TextMesh) hacia la camara solo en horizontal, para leerlo desde cualquier lado.</summary>
    public sealed class LunarFaceCamera : MonoBehaviour
    {
        private void LateUpdate()
        {
            var camera = Camera.main;
            if (camera == null) return;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - camera.transform.position, Vector3.up);
            if (away.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(away);
        }
    }
}
