using System.Collections.Generic;
using UnityEngine;

namespace LunaCero
{
    /// <summary>Zona presurizada (base, habitat): recarga el oxigeno del jugador.</summary>
    public sealed class LunarOxygenZone : MonoBehaviour
    {
        public static readonly List<LunarOxygenZone> All = new List<LunarOxygenZone>();
        public float radius = 8f;
        public float height = 6f;

        private void OnEnable() { All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        public bool Contains(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            return new Vector2(offset.x, offset.z).sqrMagnitude <= radius * radius && offset.y > -2f && offset.y < height;
        }

        public static bool InsideAny(Vector3 position)
        {
            foreach (var zone in All) if (zone != null && zone.Contains(position)) return true;
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
