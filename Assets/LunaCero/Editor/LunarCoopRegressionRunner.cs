using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LunaCero.Editor
{
    [InitializeOnLoad]
    public static class LunarCoopRegressionRunner
    {
        static LunarCoopRegressionRunner() { EditorApplication.playModeStateChanged+=Changed; }
        [MenuItem("Luna Cero/Probar correcciones multijugador")]
        public static void Run()
        {
            if(EditorApplication.isPlaying) return;
            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene("Assets/LunaCero/Scenes/LunaCeroMapaGrande.unity");
            SessionState.SetBool("LunarCoopRegression",true); EditorApplication.isPlaying=true;
        }
        static void Changed(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool("LunarCoopRegression",false)) return;
            SessionState.SetBool("LunarCoopRegression",false); new GameObject("Prueba red movimiento",typeof(LunarCoopRegressionTest));
        }
    }
}


