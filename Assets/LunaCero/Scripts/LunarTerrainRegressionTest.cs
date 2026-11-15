#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
namespace LunaCero
{
    [DefaultExecutionOrder(1100)]
    public sealed class LunarTerrainRegressionTest : MonoBehaviour
    {
        LunarPlayerController controller; Vector3 end; bool walking;
        static void Check(bool ok,string why) { if(!ok) throw new Exception(why); }
        void Update()
        {
            if(!walking||controller==null) return;
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            Vector3 delta=end-controller.transform.position; delta.y=0;
            typeof(LunarPlayerController).GetField("yaw",flags).SetValue(controller,Quaternion.LookRotation(delta).eulerAngles.y);
            typeof(LunarPlayerController).GetField("movement",flags).SetValue(controller,delta.magnitude>0.4f?Vector2.up:Vector2.zero);
        }
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0)
            {
                object next;
                try { var e=stack.Peek();if(!e.MoveNext()){stack.Pop();continue;}next=e.Current; }
                catch(Exception e){Debug.LogError("LUNA_TERRAIN_TEST_FAILED: "+e);EditorApplication.isPlaying=false;yield break;}
                if(next is IEnumerator nested)stack.Push(nested);else yield return next;
            }
            Debug.Log("LUNA_TERRAIN_TEST_OK: escala de llave y vista, jugador con cuatro colliders y bot suben ladera del mapa real.");
            EditorApplication.isPlaying=false;
        }
        IEnumerator Run()
        {
            yield return null; Time.timeScale=1;
            var director=FindObjectOfType<LunarHordeDirector>();director.enabled=false;
            var player=FindObjectOfType<LunarPlayerCombat>(); controller=player.GetComponent<LunarPlayerController>();
            var terrain=FindObjectOfType<Terrain>(); var data=terrain.terrainData;
            var origin=terrain.transform.position;
            Vector3 start=Vector3.zero; bool found=false;
            // Find a previously steep region near the central crater, with a clear 10m uphill route.
            for(float z=-35;z<40&&!found;z+=2)
            for(float x=-35;x<40&&!found;x+=2)
            for(int d=0;d<8&&!found;d++)
            {
                Vector3 a=new Vector3(x,0,z),b=a+new Vector3(Mathf.Cos(d*Mathf.PI/4),0,Mathf.Sin(d*Mathf.PI/4))*10;
                a.y=terrain.SampleHeight(a)+origin.y;b.y=terrain.SampleHeight(b)+origin.y;
                if(b.y-a.y<2 || b.y-a.y>5.5f)continue;
                if(!NavMesh.SamplePosition(a,out var na,0.5f,NavMesh.AllAreas)||!NavMesh.SamplePosition(b,out var nb,0.5f,NavMesh.AllAreas))continue;
                var path=new NavMeshPath();if(!NavMesh.CalculatePath(na.position,nb.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)continue;
                bool clear=true;
                for(int i=0;i<=10;i++)
                {
                    var p=Vector3.Lerp(a,b,i/10f);p.y=terrain.SampleHeight(p)+origin.y;
                    if(Physics.OverlapCapsule(p+Vector3.up*0.4f,p+Vector3.up*1.5f,0.3f).Any(c=>!(c is TerrainCollider)&&!c.isTrigger)) {clear=false;break;}
                }
                if(!clear)continue; start=a;end=b;found=true;
            }
            Check(found,"No hay una ruta transitable real de 10m con subida de 2m");
            Debug.Log("LUNA_TERRAIN_ROUTE: "+start+" -> "+end);
            var rigid=player.GetComponent<Rigidbody>();rigid.position=start+Vector3.up*0.12f;rigid.velocity=Vector3.zero;Physics.SyncTransforms();
            walking=true;yield return new WaitForSeconds(6);walking=false;
            Check((Vector3.ProjectOnPlane(player.transform.position-end,Vector3.up)).magnitude<2,"Jugador bloqueado en ladera: "+player.transform.position);
            Check(player.transform.position.y>start.y+1.5f,"Jugador no gana altura");
            var enemy=Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LunaCero/Prefabs/Aliens/Common.prefab"),start,Quaternion.identity).GetComponent<LunarEnemy>();
            yield return new WaitForSeconds(7);
            Check(enemy!=null && Vector3.Distance(enemy.transform.position,end)<3.5f,"Bot bloqueado en ladera: "+(enemy!=null?enemy.transform.position.ToString():"null"));
            var wrench=Resources.Load<GameObject>("LunarWeapons/llave");var copy=Instantiate(wrench);var bounds=LunarLoot.LocalBounds(copy.transform);
            Check(Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z)<0.6f,"Llave mundial gigante: "+bounds.size);Destroy(copy);
            player.AddWeapon(player.FindWeapon("llave"));
            var view=player.GetComponent<LunarViewModel>();
            typeof(LunarViewModel).GetMethod("RebuildWeapon",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(view,new object[]{player.FindWeapon("llave")});
            var root=(Transform)typeof(LunarViewModel).GetField("weaponRoot",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(view);
            var dimensions=LunarLoot.LocalBounds(root);
            Check(Mathf.Max(dimensions.size.x,dimensions.size.y,dimensions.size.z)<0.42f,"Llave gigante en manos: "+dimensions.size);
        }
    }
}
#endif
