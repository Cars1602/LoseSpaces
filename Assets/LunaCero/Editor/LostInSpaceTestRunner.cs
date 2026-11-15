using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LunaCero.Editor
{
    [InitializeOnLoad]
    public static class LostInSpaceTestRunner
    {
        static LostInSpaceTestRunner(){EditorApplication.playModeStateChanged+=Changed;}
        [MenuItem("Lost in Space/Probar introduccion y mision completa")]
        public static void Run(){EditorSceneManager.SaveOpenScenes();EditorSceneManager.OpenScene("Assets/Scenes/MenuPrincipal.unity");LunarMainMenu.IntroShown=false;SessionState.SetBool("LostFinalTest",true);EditorApplication.isPlaying=true;}
        static void Changed(PlayModeStateChange s)
        {
            if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("LostFinalTest",false)){SessionState.SetBool("LostFinalTest",false);new GameObject("Prueba Lost in Space",typeof(LostInSpaceSmokeTest));}
        }
    }
}

