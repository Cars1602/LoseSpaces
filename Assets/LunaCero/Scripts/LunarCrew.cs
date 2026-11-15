using System.Collections.Generic;
using UnityEngine;

namespace LunaCero
{
    /// <summary>
    /// Tripulacion: el astronauta local y los de los otros jugadores (marionetas en red).
    /// Los monstruos eligen al tripulante vivo mas cercano y las armas no hieren a la tripulacion.
    /// </summary>
    public static class LunarCrew
    {
        public static readonly List<LunarHealth> Members = new List<LunarHealth>();

        public static void Add(LunarHealth member) { if (member != null && !Members.Contains(member)) Members.Add(member); }
        public static void Remove(LunarHealth member) { Members.Remove(member); }
        public static bool IsCrew(LunarHealth health) => health != null && Members.Contains(health);

        public static bool AnyAlive
        {
            get
            {
                Clean();
                foreach (var member in Members) if (!member.IsDead) return true;
                return false;
            }
        }

        public static LunarHealth Nearest(Vector3 position)
        {
            Clean();
            LunarHealth best = null;
            float distance = float.MaxValue;
            foreach (var member in Members)
            {
                if (member.IsDead || member.IsInvulnerable) continue;
                float d = (member.transform.position - position).sqrMagnitude;
                if (d < distance) { distance = d; best = member; }
            }
            return best;
        }

        public static float NearestDistance(Vector3 position)
        {
            var nearest = Nearest(position);
            return nearest != null ? Vector3.Distance(nearest.transform.position, position) : float.MaxValue;
        }

        /// <summary>Un tripulante vivo al azar (para repartir las hordas entre los jugadores).</summary>
        public static LunarHealth RandomAlive()
        {
            Clean();
            var alive = Members.FindAll(m => !m.IsDead && !m.IsInvulnerable);
            return alive.Count > 0 ? alive[Random.Range(0, alive.Count)] : null;
        }

        private static void Clean() { Members.RemoveAll(m => m == null); }
    }
}
