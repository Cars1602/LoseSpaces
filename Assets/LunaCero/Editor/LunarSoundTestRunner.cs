using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LunaCero.Editor
{
    [InitializeOnLoad]
    public static class LunarSoundTestRunner
    {
        static LunarSoundTestRunner() { EditorApplication.playModeStateChanged+=Changed; }
        [MenuItem("Luna Cero/Probar audio menu y explosion")]
        public static void Run()
        {
            if(EditorApplication.isPlaying) return;
            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene("Assets/Scenes/MenuPrincipal.unity");
            SessionState.SetBool("LunaSoundTest",true); EditorApplication.isPlaying=true;
        }
        static void Changed(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("LunaSoundTest",false)) return;
            SessionState.SetBool("LunaSoundTest",false); new GameObject("Prueba audio y menu",typeof(LunarSoundSmokeTest));
        }
    }
}



