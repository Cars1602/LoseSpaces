using System.Collections;
using UnityEngine;
namespace LunaCero
{
    public sealed class LunarAudio : MonoBehaviour
    {
        static LunarAudio instance;
        LunarAudioBank bank;
        AudioClip explosion;
        AudioSource music;
        public static string Weapon(string id) => id == "pistola" ? "Pistola_Lunar" : id == "escopeta" ? "Escopeta_de_Pulsos" : id == "riel" ? "Rifle_de_Precision" : "Fusil_Lunar";
        static LunarAudio Service
        {
            get
            {
                if(instance != null) return instance;
                instance = new GameObject("Audio Luna Cero").AddComponent<LunarAudio>();
                DontDestroyOnLoad(instance.gameObject);
                instance.bank = Resources.Load<LunarAudioBank>("LunarAudioBank");
                AudioListener.volume = PlayerPrefs.GetFloat("LunaCero.Volume",0.7f);
                return instance;
            }
        }
        public static void Play(string key, Vector3 position, float volume = 0.7f, bool spatial = true)
        {
            var service = Service;
            var clip = service.bank != null ? service.bank.Find(key) : null;
            if(clip != null) service.Emit(clip,position,volume,spatial);
        }
        void Emit(AudioClip clip, Vector3 position, float volume, bool spatial)
        {
            var go = new GameObject("Sonido " + clip.name); go.transform.position = position;
            var audio = go.AddComponent<AudioSource>(); audio.clip=clip; audio.volume=volume;
            audio.spatialBlend=spatial?1:0; audio.minDistance=3; audio.maxDistance=45; audio.rolloffMode=AudioRolloffMode.Linear;
            audio.Play(); Destroy(go,clip.length+0.2f);
        }
        public static void Music(bool menu)
        {
            var s=Service;
            if(s.music != null) Destroy(s.music);
            if(!menu || s.bank == null) return;
            s.music=s.gameObject.AddComponent<AudioSource>(); s.music.clip=s.bank.Find("Lobby/Lobby_Luna_Cero_Musica_LOOP_96s");
            s.music.loop=true; s.music.volume=0.3f; s.music.Play();
        }
        public static void SetVolume(float volume) { AudioListener.volume=volume; PlayerPrefs.SetFloat("LunaCero.Volume",volume); }
        public static void Burst(Vector3 position)
        {
            var s=Service;
            if(s.explosion==null)
            {
                const int rate=22050; float[] data=new float[rate]; var random=new System.Random(741);
                float low=0;
                for(int i=0;i<data.Length;i++) { float t=(float)i/rate; low=Mathf.Lerp(low,(float)random.NextDouble()*2-1,0.18f); data[i]=(low*0.8f+Mathf.Sin(2*Mathf.PI*(85*t-28*t*t))*0.35f)*Mathf.Exp(-t*6)*Mathf.Min(1,t*500); }
                s.explosion=AudioClip.Create("Explosion sintetizada",rate,1,rate,false); s.explosion.SetData(data,0);
            }
            s.Emit(s.explosion,position,0.8f,true);
        }
    }
}
