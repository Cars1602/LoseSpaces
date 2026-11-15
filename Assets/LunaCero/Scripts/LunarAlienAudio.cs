using UnityEngine;
namespace LunaCero
{
    public sealed class LunarAlienAudio : MonoBehaviour
    {
        LunarEnemy enemy; LunarAlienAnimation animationDriver; int attacks; float next;
        string Family => enemy.alienKind == LunarAlienKind.Brute || enemy.alienKind == LunarAlienKind.Charger ? "Gigante" : enemy.alienKind == LunarAlienKind.Common ? "Enjambre" : enemy.alienKind == LunarAlienKind.Bile || enemy.alienKind == LunarAlienKind.Acid ? "Escupidor" : "Acechador";
        void Awake() { enemy=GetComponent<LunarEnemy>(); animationDriver=GetComponent<LunarAlienAnimation>(); next=Time.time+Random.Range(3f,9f); }
        public void Death() { LunarAudio.Play("Monstruos/"+Family+"/"+Family+"_muerte",transform.position); }
        void Update()
        {
            if(enemy == null || enemy.IsDead) return;
            if(animationDriver != null && animationDriver.AttackCount != attacks) { attacks=animationDriver.AttackCount; LunarAudio.Play("Monstruos/"+Family+"/"+Family+"_ataque_enojo",transform.position,0.6f); }
            if(Time.time<next) return; next=Time.time+Random.Range(8f,15f);
            if(Camera.main != null && (Camera.main.transform.position-transform.position).sqrMagnitude<900) LunarAudio.Play("Monstruos/"+Family+"/"+Family+"_base_ambiente",transform.position,0.35f);
        }
    }
}
