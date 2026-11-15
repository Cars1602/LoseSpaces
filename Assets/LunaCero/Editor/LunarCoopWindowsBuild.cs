using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace LunaCero.Editor
{
    public static class LunarCoopWindowsBuild
    {
        [MenuItem("Luna Cero/Exportar Windows cooperativo")]
        public static void Build()
        {
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),
                target=BuildTarget.StandaloneWindows64,
                locationPathName="Builds/LostInSpace/LostInSpace.exe", options=BuildOptions.None
            });
            if(result.summary.result==BuildResult.Succeeded) Debug.Log("LUNA_COOP_WINDOWS_OK: Builds/LostInSpace/LostInSpace.exe; distribuir la carpeta completa a los dos jugadores.");
            else Debug.LogError("LUNA_COOP_WINDOWS_FAILED: "+result.summary.result);
        }
    }
}
