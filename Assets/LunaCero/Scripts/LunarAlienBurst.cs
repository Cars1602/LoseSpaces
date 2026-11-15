using System.Collections;
using UnityEngine;
namespace LunaCero
{
    public sealed class LunarAlienBurst : MonoBehaviour
    {
        public static bool StartDeath(LunarEnemy enemy)
        {
            if(enemy.isBoss) return false;
            if(enemy.GetComponent<LunarAlienBurst>() != null) return true;
            enemy.gameObject.AddComponent<LunarAlienBurst>(); return true;
        }
        IEnumerator Start()
        {
            foreach(var animator in GetComponentsInChildren<Animator>()) animator.enabled=false;
            foreach(var driver in GetComponents<LunarAlienAnimation>()) driver.enabled=false;
            foreach(var driver in GetComponents<LunarRiderAnimation>()) driver.enabled=false;
            var visual=transform.Find("Visual"); if(visual==null) visual=transform;
            Vector3 original=visual.localScale; float start=Time.time;
            while(Time.time-start<0.22f) { float t=(Time.time-start)/0.22f; visual.localScale=Vector3.Scale(original,new Vector3(1+t*0.25f,1-t*0.25f,1+t*0.25f)); yield return null; }
            var bounds=new Bounds(transform.position+Vector3.up,Vector3.one);
            foreach(var r in GetComponentsInChildren<Renderer>()) { bounds.Encapsulate(r.bounds); r.enabled=false; }
            Vector3 center=bounds.center;
            LunarAudio.Burst(center);
            LunarCombatEffects.Explosion(center,Mathf.Clamp(bounds.size.magnitude*0.35f,0.6f,1.6f),new Color(0.4f,1f,0.7f));
            var go=new GameObject("Fragmentos alien (sin dano)"); go.transform.position=center;
            var particles=go.AddComponent<ParticleSystem>(); particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=particles.main; main.loop=false; main.duration=0.1f; main.startLifetime=new ParticleSystem.MinMaxCurve(0.8f,1.8f); main.startSpeed=new ParticleSystem.MinMaxCurve(1.5f,4f); main.startSize=new ParticleSystem.MinMaxCurve(0.06f,0.2f); main.startColor=new Color(0.25f,0.8f,0.5f); main.gravityModifier=0.165f; main.maxParticles=28;
            var emission=particles.emission; emission.rateOverTime=0; emission.SetBursts(new[]{new ParticleSystem.Burst(0,24)});
            var shape=particles.shape; shape.shapeType=ParticleSystemShapeType.Sphere; shape.radius=0.2f;
            var size=particles.sizeOverLifetime; size.enabled=true; size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,1,1,0));
            var renderer=particles.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=LunarRuntimeMaterials.Get(new Color(0.25f,0.8f,0.5f));
            particles.Play(); Destroy(go,2.5f); Destroy(gameObject,2);
        }
    }
}
