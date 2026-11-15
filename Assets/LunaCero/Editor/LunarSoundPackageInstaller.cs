using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LunaCero.Editor
{
    [InitializeOnLoad]
    public static class LunarSoundPackageInstaller
    {
        const string Package="Assets/Luna_Cero_Paquete_Completo_v4/";
        static LunarSoundPackageInstaller() { EditorApplication.delayCall+=Once; }
        static void Once()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isUpdating) { EditorApplication.delayCall+=Once; return; }
            if(EditorPrefs.GetBool(Application.dataPath+"SoundPackage.v1",false)) return;
            try { Install(); EditorPrefs.SetBool(Application.dataPath+"SoundPackage.v1",true); } catch(Exception e) { Debug.LogError("LUNA_SOUND_FAILED: "+e); }
        }
        [MenuItem("Luna Cero/Instalar sonidos y menu")]
        public static void Install()
        {
            var clips=AssetDatabase.FindAssets("t:AudioClip",new[]{Package.TrimEnd('/')}).Select(AssetDatabase.GUIDToAssetPath).ToArray();
            if(clips.Length==0) throw new Exception("No hay clips importados.");
            var bank=AssetDatabase.LoadAssetAtPath<LunarAudioBank>("Assets/LunaCero/Resources/LunarAudioBank.asset");
            if(bank==null) { bank=ScriptableObject.CreateInstance<LunarAudioBank>(); AssetDatabase.CreateAsset(bank,"Assets/LunaCero/Resources/LunarAudioBank.asset"); }
            bank.entries=clips.Select(path=>new LunarAudioBank.Entry{key=path.Substring(Package.Length,path.Length-Package.Length-4),clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path)}).ToArray(); EditorUtility.SetDirty(bank);
            const string menuPath="Assets/Scenes/MenuPrincipal.unity";
            if(!System.IO.File.Exists(menuPath))
            {
                var active=EditorSceneManager.GetActiveScene();
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
                var camera=new GameObject("Camara menu",typeof(Camera),typeof(AudioListener)); camera.tag="MainCamera";
                camera.GetComponent<Camera>().backgroundColor=new Color(0.006f,0.012f,0.03f); camera.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor;
                new GameObject("Menu principal Luna Cero",typeof(LunarMainMenu));
                EditorSceneManager.SaveScene(scene,menuPath); EditorSceneManager.CloseScene(scene,true); UnityEngine.SceneManagement.SceneManager.SetActiveScene(active);
            }
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(menuPath,true)}.Concat(EditorBuildSettings.scenes.Where(s=>s.path!=menuPath)).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("LUNA_SOUND_PACKAGE_OK: "+bank.entries.Length+" sonidos conectados; menu en posicion 0, carga asincrona y escena de juego conservada.");
        }
    }
}
