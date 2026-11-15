using UnityEditor;
using UnityEngine;

namespace LunaCero.Editor
{
    [InitializeOnLoad]
    public static class LunarCombatTestRunner
    {
        static LunarCombatTestRunner() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem("Luna Cero/Probar combate automaticamente")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) return;
            if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path != LunarCombatBuilder.ScenePath)
            {
                Debug.LogError("Abre LunaCeroCombat antes de probar.");
                return;
            }
            SessionState.SetBool("LunaCombatSmoke", true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("LunaSurvivalSmoke", false))
            {
                SessionState.SetBool("LunaSurvivalSmoke", false);
                new GameObject("Prueba temporal de supervivencia").AddComponent<LunarSurvivalSmokeTest>();
                return;
            }
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("LunaCombatSmoke", false)) return;
            SessionState.SetBool("LunaCombatSmoke", false);
            new GameObject("Prueba de combate temporal").AddComponent<LunarCombatSmokeTest>();
        }

        [MenuItem("Luna Cero/Probar supervivencia automaticamente")]
        public static void RunSurvival()
        {
            if (EditorApplication.isPlaying) return;
            if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path != LunarSurvivalBuilder.ScenePath)
            {
                Debug.LogError("Abre LunaCeroSurvival antes de probar.");
                return;
            }
            SessionState.SetBool("LunaSurvivalSmoke", true);
            EditorApplication.isPlaying = true;
        }
    }
}
