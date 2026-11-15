#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace LunaCero
{
    public sealed class LunarSoundSmokeTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return new WaitForSecondsRealtime(0.6f);
            var bank=Resources.Load<LunarAudioBank>("LunarAudioBank");
            if(bank==null || bank.entries.Length<100 || FindObjectOfType<LunarMainMenu>()==null) { Fail("Menu/banco ausente"); yield break; }
            var menu=FindObjectOfType<LunarMainMenu>();
            var buttons=FindObjectsOfType<UnityEngine.UI.Button>();
            if(buttons.Length<7) { Fail("Botones del menu"); yield break; }
            menu.SkipIntro();
            Debug.Log("LUNA_SOUND_TEST_PROGRESS: banco="+bank.entries.Length+", menu Lost in Space; probando carga.");
            var load=(IEnumerator)typeof(LunarMainMenu).GetMethod("Load",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(menu,null);
            menu.StartCoroutine(load);
            float timeout=Time.realtimeSinceStartup+45;
            while(SceneManager.GetActiveScene().name!="LunaCeroMapaGrande" && Time.realtimeSinceStartup<timeout) yield return null;
            if(SceneManager.GetActiveScene().name!="LunaCeroMapaGrande") { Fail("Carga no termino"); yield break; }
            yield return null;
            if(Cursor.lockState!=CursorLockMode.Locked && !LunarPlayerRole.MenuOpen) { Fail("Cursor sin bloquear"); yield break; }
            Time.timeScale=1; // The role/session selection intentionally pauses the world; resume for the death test.
            var director=FindObjectOfType<LunarHordeDirector>(); if(director!=null) director.enabled=false;
            var normal=Spawn("Common"); var boss=Spawn("Brute");
            normal.GetComponent<LunarHealth>().Damage(100000); boss.GetComponent<LunarHealth>().Damage(100000);
            if(normal.GetComponent<LunarAlienBurst>()==null || boss.GetComponent<LunarAlienBurst>()!=null) { Fail("Filtro de jefes"); yield break; }
            yield return new WaitForSecondsRealtime(0.5f);
            if(normal.GetComponentsInChildren<Renderer>().Any(r=>r.enabled)) { Fail("Alien no estallo"); yield break; }
            if(!FindObjectsOfType<AudioSource>().Any(a=>a.clip!=null && a.clip.name=="Explosion sintetizada")) { Fail("Audio explosion ausente"); yield break; }
            Debug.Log("LUNA_SOUND_SMOKE_OK: banco, botones, carga menu a mapa, explosion comun con audio, jefe sin explosion.");
            EditorApplication.isPlaying=false;
        }
        LunarEnemy Spawn(string name)
        {
            var go=Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/Aliens/"+name+".prefab"),Vector3.zero,Quaternion.identity);
            var enemy=go.GetComponent<LunarEnemy>(); enemy.enabled=false; return enemy;
        }
        void Fail(string message) { Debug.LogError("LUNA_SOUND_SMOKE_FAILED: "+message); EditorApplication.isPlaying=false; }
    }
}
#endif


