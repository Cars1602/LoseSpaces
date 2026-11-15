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

        private void Awake() { Current = maximum; }

        public void Configure(float value) { maximum = Mathf.Max(1f, value); Current = maximum; }

        public void Damage(float amount)
        {
            if (IsDead || amount <= 0) return;
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
    }
}
