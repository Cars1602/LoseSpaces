#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
namespace LunaCero
{
    public sealed class LostInSpaceSmokeTest:MonoBehaviour
    {
        public static bool Passed;
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0)
            {
                object next;try{var e=stack.Peek();if(!e.MoveNext()){stack.Pop();continue;}next=e.Current;}catch(Exception e){Debug.LogError("LOST_FINAL_TEST_FAILED: "+e);EditorApplication.isPlaying=false;yield break;}
                if(next is IEnumerator nested)stack.Push(nested);else yield return next;
            }
            Passed=true;Debug.Log("LOST_FINAL_TEST_OK: intro recortada reproduce, omitir, menu 3D, paneles, carga al mapa, cinco piezas, despegue, victoria y regreso al menu.");
            EditorApplication.isPlaying=false;
        }
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        IEnumerator Run()
        {
            yield return null;var menu=FindObjectOfType<LunarMainMenu>();Check(menu!=null,"Menu ausente");
            float timeout=Time.realtimeSinceStartup+15;
            while(Time.realtimeSinceStartup<timeout){var v=FindObjectOfType<VideoPlayer>();if(v!=null&&v.isPlaying&&v.time>0.2)break;yield return null;}
            var video=FindObjectOfType<VideoPlayer>();Check(video!=null&&video.isPlaying,"No reproduce el video");Check(video.length>33&&video.length<34,"Recorte incorrecto: "+video.length);
            menu.SkipIntro();yield return new WaitForSecondsRealtime(1.2f);Check(menu.MenuReady&&!menu.IntroPlaying,"Omitir intro");
            Check(GameObject.Find("Escenario 3D de inicio")!=null&&FindObjectsOfType<Animator>().Length>=8,"Faltan personajes 3D");
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            foreach(string panel in new[]{"Roster","Settings","Logs"}){typeof(LunarMainMenu).GetMethod(panel,flags).Invoke(menu,null);yield return null;Check(GameObject.Find("Panel de consulta")!=null,"Panel "+panel);typeof(LunarMainMenu).GetMethod("Close",flags).Invoke(menu,null);yield return null;}
            Debug.Log("LOST_FINAL_TEST_PROGRESS: video, omitir, arte, modelos y paneles OK.");
            menu.StartCoroutine((IEnumerator)typeof(LunarMainMenu).GetMethod("Load",flags).Invoke(menu,null));
            timeout=Time.realtimeSinceStartup+40;while(SceneManager.GetActiveScene().name!="LunaCeroMapaGrande"&&Time.realtimeSinceStartup<timeout)yield return null;
            Check(SceneManager.GetActiveScene().name=="LunaCeroMapaGrande","No carga mapa");yield return null;Time.timeScale=1;
            var repair=FindObjectOfType<LunarRepairPoint>();Check(repair!=null,"Sin reparacion");
            var parts=FindObjectsOfType<LunarShipPart>().Where(p=>!p.Installed).Take(5).ToArray();Check(parts.Length==5,"Faltan cinco piezas");
            foreach(var part in parts)typeof(LunarRepairPoint).GetMethod("Install",flags).Invoke(repair,new object[]{part});
            yield return null;Check(repair.Repaired&&LostInSpaceFinale.Active,"No empieza despegue");
            var finale=repair.GetComponent<LostInSpaceFinale>();timeout=Time.realtimeSinceStartup+13;
            while(!finale.Complete&&Time.realtimeSinceStartup<timeout)yield return null;
            Check(finale.Complete&&PlayerPrefs.GetInt("LostInSpace.MissionCompleted",0)==1,"Final de mision");
            finale.Return();yield return null;yield return new WaitForSecondsRealtime(1.2f);
            Check(SceneManager.GetActiveScene().name=="MenuPrincipal"&&FindObjectOfType<LunarMainMenu>().MenuReady,"Regreso al menu");
        }
    }
}
#endif
