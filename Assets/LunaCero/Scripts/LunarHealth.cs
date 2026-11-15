using System;
using UnityEngine;

namespace LunaCero
{
    public sealed class LunarHealth : MonoBehaviour
    {
        [SerializeField] private float maximum = 100f;
        public float Maximum => maximum;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;
        public float LastDamageTime { get; private set; } = -100f;
        public event Action<LunarHealth> Died;
        /// <summary>
        /// Multijugador: si se asigna, el dano no se aplica aqui sino que se envia a quien manda sobre
        /// esta vida (el monstruo real en el anfitrion, o el astronauta del otro jugador).
        /// </summary>
        public Action<float> Redirect;

        private void Awake() { Current = maximum; }

        public void Configure(float value) { maximum = Mathf.Max(1f, value); Current = maximum; }

        public void Damage(float amount)
        {
            if (IsDead || amount <= 0) return;
            if (Redirect != null) { LastDamageTime = Time.time; Redirect(amount); return; }
            Current = Mathf.Max(0f, Current - amount);
            LastDamageTime = Time.time;
            if (IsDead) Died?.Invoke(this);
        }

        public bool Heal(float amount)
        {
            if (IsDead || Current >= maximum || amount <= 0) return false;
            Current = Mathf.Min(maximum, Current + amount);
            return true;
        }

        /// <summary>Valor recibido por red (no dispara la muerte local).</summary>
        public void SetRemote(float current, float max)
        {
            maximum = Mathf.Max(1f, max);
            Current = Mathf.Clamp(current, 0f, maximum);
        }

        /// <summary>Reaparecer en multijugador: vida completa sin recargar la escena.</summary>
        public void Revive() { Current = maximum; LastDamageTime = -100f; }
    }
}
