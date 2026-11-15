using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LunaCero.Editor
{
    [InitializeOnLoad]
    public static class LostInSpaceInstaller
    {
        static LostInSpaceInstaller(){EditorApplication.delayCall+=Once;}
        static void Once()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isUpdating){EditorApplication.delayCall+=Once;return;}
            if(EditorPrefs.GetBool(Application.dataPath+"LostInSpaceLaunch.v2",false))return;
            try{Install();EditorPrefs.SetBool(Application.dataPath+"LostInSpaceLaunch.v2",true);}catch(Exception e){Debug.LogError("LOST_MENU_FAILED: "+e);}
        }
        [MenuItem("Lost in Space/Instalar presentacion final")]
        public static void Install()
        {
            var art=AssetDatabase.LoadAssetAtPath<LostInSpaceArt>("Assets/LunaCero/Resources/LostInSpaceArt.asset");
            if(art==null){art=ScriptableObject.CreateInstance<LostInSpaceArt>();AssetDatabase.CreateAsset(art,"Assets/LunaCero/Resources/LostInSpaceArt.asset");}
            art.backdropShader=Shader.Find("Unlit/Texture");
            art.background=Texture("Assets/LunaCero/Art/Menu/LostInSpace_Background.png");art.logo=Texture("Assets/LunaCero/Art/Menu/LostInSpace_Logo.png");
            art.astronaut=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/LunarPlayer.prefab").transform.Find("Modelo astronauta").gameObject;
            art.common=Visual("Common");art.stalker=Visual("Rider");art.spitter=Visual("Bile");art.giant=Visual("Brute");
            art.ship=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/esenario/props/Nave_Condor_Rota.fbx");
            EditorUtility.SetDirty(art);PlayerSettings.productName="Lost in Space";PlayerSettings.companyName="Condor Crew";
            AssetDatabase.SaveAssets();
            Debug.Log("LOST_MENU_ASSETS_OK: fondo, logo, personajes 3D y video integrado; Lost in Space.");
        }
        static GameObject Visual(string id)=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/Aliens/"+id+".prefab").transform.Find("Visual").gameObject;
        static Texture2D Texture(string path)
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);if(importer==null)throw new Exception("Falta "+path);
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.maxTextureSize=2048;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
