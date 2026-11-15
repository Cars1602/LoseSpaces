using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LunaCero.Editor
{
    public static class LunarSlopeRepair
    {
        static void Once()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isUpdating) return;
            if(EditorPrefs.GetBool(Application.dataPath+"SlopeRepair.v1",false)) return;
            try { Repair(); EditorPrefs.SetBool(Application.dataPath+"SlopeRepair.v1",true); } catch(Exception e) { Debug.LogError("LUNA_SLOPE_REPAIR_FAILED: "+e); }
        }
        [MenuItem("Luna Cero/Corregir escala y pendientes del mapa")]
        public static void Repair()
        {
            if(EditorApplication.isPlaying) throw new Exception("Salir de Play primero");
            EditorSceneManager.SaveOpenScenes();
            var scene=EditorSceneManager.OpenScene("Assets/LunaCero/Scenes/LunaCeroMapaGrande.unity");
            LunarNewPropsInstaller.Install();
            var terrain=UnityEngine.Object.FindObjectOfType<Terrain>(); var data=terrain.terrainData;
            string path=AssetDatabase.GetAssetPath(data), backup=path.Replace(".asset","_AntesPendientes.asset");
            if(!System.IO.File.Exists(backup)) AssetDatabase.CopyAsset(path,backup);
            var old=UnityEngine.Object.Instantiate(data);
            int n=data.heightmapResolution; var heights=data.GetHeights(0,0,n,n);
            float spacing=data.size.x/(n-1);
            // Restrict both axial gradients to 22 degrees, bounding combined slope near 30.
            float limit=Mathf.Tan(22*Mathf.Deg2Rad)*spacing/data.size.y;
            for(int pass=0;pass<100;pass++)
            {
                for(int z=0;z<n;z++) for(int x=0;x<n;x++)
                {
                    if(x+1<n) Relax(heights,z,x,z,x+1,limit);
                    if(z+1<n) Relax(heights,z,x,z+1,x,limit);
                }
            }
            data.SetHeights(0,0,heights); EditorUtility.SetDirty(data); terrain.Flush();
            float Delta(Vector3 p)
            {
                float u=Mathf.Clamp01((p.x-terrain.transform.position.x)/data.size.x),v=Mathf.Clamp01((p.z-terrain.transform.position.z)/data.size.z);
                return data.GetInterpolatedHeight(u,v)-old.GetInterpolatedHeight(u,v);
            }
            void Reposition(Transform t)
            {
                if(t.GetComponent<LunarPlayerController>()!=null) { t.position+=Vector3.up*Delta(t.position); return; }
                if(t.GetComponent<Renderer>()!=null || t.GetComponent<Collider>()!=null || t.GetComponent<LunarPickup>()!=null || t.GetComponent<LunarHealingStation>()!=null || t.GetComponent<LunarRepairPoint>()!=null || t.childCount==0)
                { t.position+=Vector3.up*Delta(t.position); return; }
                foreach(Transform child in t) Reposition(child);
            }
            foreach(var root in scene.GetRootGameObjects())
            {
                string name=root.name;
                if(root.GetComponent<Terrain>()!=null || root.GetComponent<LunarLevelNavigation>()!=null || name.Contains("Rampas del crater") || name.Contains("Cielo") || name.Contains("Sol") || name.Contains("Fondo") || name.Contains("Limites")) continue;
                Reposition(root.transform);
            }
            // Old bridge colliders no longer match the newly smoothed terrain.
            foreach(var root in scene.GetRootGameObjects()) if(root.name=="Rampas del crater") root.SetActive(false);
            UnityEngine.Object.DestroyImmediate(old);
            Physics.SyncTransforms();
            var nav=UnityEngine.Object.FindObjectOfType<LunarLevelNavigation>();
            var baked=LunarLevelNavigation.Build(nav.bounds,nav.agentRadius,nav.agentHeight,0.18f);
            string navPath=AssetDatabase.GetAssetPath(nav.baked);
            if(nav.baked!=null) { EditorUtility.CopySerialized(baked,nav.baked); UnityEngine.Object.DestroyImmediate(baked); EditorUtility.SetDirty(nav.baked); }
            else { AssetDatabase.CreateAsset(baked,"Assets/LunaCero/Materials/Escenario/NavegacionSuave.asset"); nav.baked=baked; }
            EditorUtility.SetDirty(nav); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            Debug.Log("LUNA_SLOPE_REPAIR_OK: terreno suavizado, objetos reajustados, rampas anteriores desactivadas y navegacion horneada; copia original: "+backup);
        }
        static void Relax(float[,] h,int z,int x,int zz,int xx,float limit)
        {
            float d=h[z,x]-h[zz,xx]; if(Mathf.Abs(d)<=limit) return;
            float shift=(Mathf.Abs(d)-limit)*0.5f*Mathf.Sign(d); h[z,x]-=shift; h[zz,xx]+=shift;
        }
    }
}

