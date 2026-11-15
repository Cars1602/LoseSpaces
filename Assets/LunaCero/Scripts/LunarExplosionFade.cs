using UnityEngine;

namespace LunaCero
{
    public sealed class LunarExplosionFade : MonoBehaviour
    {
        public float radius = 4f;
        public float duration = 0.35f;
        private float start;
        private Light flash;
        private void Start() { start = Time.time; flash = GetComponent<Light>(); }
        private void Update()
        {
            float t = (Time.time - start) / duration;
            if (t >= 1f) { Destroy(gameObject); return; }
            transform.localScale = Vector3.one * Mathf.Lerp(0.3f, radius * 2f, Mathf.Sqrt(t));
            if (flash != null) flash.intensity = Mathf.Lerp(4f, 0f, t);
        }
    }
}
