using System;
using UnityEditor;
using UnityEngine;
namespace LunaCero.Editor
{
    [InitializeOnLoad]
    public static class LunarNewPropsInstaller
    {
        static LunarNewPropsInstaller() { EditorApplication.delayCall+=Once; }
        static void Once()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isUpdating) { EditorApplication.delayCall+=Once; return; }
            if(EditorPrefs.GetBool(Application.dataPath+"NewProps.v1",false)) return;
            try { Install(); EditorPrefs.SetBool(Application.dataPath+"NewProps.v1",true); } catch(Exception e) { Debug.LogError("LUNA_NEW_PROPS_FAILED: "+e); }
        }
        [MenuItem("Luna Cero/Instalar municiones y contundentes")]
        public static void Install()
        {
            LunarArsenalModelInstaller.Build("Municion pistola","ammo-pistola",0.5f,"Assets/municion pistola");
            LunarArsenalModelInstaller.Build("Municion rifle","ammo-rifle",0.65f,"Assets/municion de rifle");
            LunarArsenalModelInstaller.Build("Municion escopeta","ammo-escopeta",0.55f,"Assets/municion y cartucho de escopeta");
            LunarArsenalModelInstaller.Build("Cartucho pistola","cartridge-pistola",0.15f,"Assets/cartucho de pistola");
            LunarArsenalModelInstaller.Build("Llave inglesa","llave",0.55f,"Assets/llave inglesa",true);
            LunarArsenalModelInstaller.Build("Tubo de metal","tubo",0.85f,"Assets/tuvo de metal");
            AssetDatabase.SaveAssets(); Debug.Log("LUNA_NEW_PROPS_OK: municiones, cartucho, llave y tubo.");
        }
    }
}
