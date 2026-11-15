using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LunaCero.Editor
{
    [InitializeOnLoad]
    public static class LunarTerrainTestRunner
    {
        static LunarTerrainTestRunner(){EditorApplication.playModeStateChanged+=Changed;}
        [MenuItem("Luna Cero/Probar laderas reales y escala")]
        public static void Run(){EditorSceneManager.SaveOpenScenes();EditorSceneManager.OpenScene("Assets/LunaCero/Scenes/LunaCeroMapaGrande.unity");SessionState.SetBool("TerrainTest",true);EditorApplication.isPlaying=true;}
        static void Changed(PlayModeStateChange s)
        {
            if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("TerrainTest",false)){SessionState.SetBool("TerrainTest",false);new GameObject("Prueba ladera real",typeof(LunarTerrainRegressionTest));}

        }
    }
}
