#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using LunaCero.Net;
using UnityEditor;
using UnityEngine;
namespace LunaCero
{
    [DefaultExecutionOrder(1000)]
    public sealed class LunarCoopRegressionTest : MonoBehaviour
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        LanHub hub; HubClient a,b; LunarPlayerController climber; bool climbing;
        static void Check(bool value,string detail) { if(!value) throw new Exception(detail); }
        static void Set(object o,string field,object value) => o.GetType().GetField(field,Flags).SetValue(o,value);
        static object Call(object o,string method,params object[] args) => o.GetType().GetMethod(method,Flags).Invoke(o,args);
        void Update() { if(climbing && climber!=null) Set(climber,"movement",Vector2.up); }
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>(); stack.Push(Run());
            while(stack.Count>0)
            {
                object next;
                try { var current=stack.Peek(); if(!current.MoveNext()) { stack.Pop(); continue; } next=current.Current; }
                catch(Exception e) { Debug.LogError("LUNA_COOP_REGRESSION_FAILED: "+e); Cleanup(); EditorApplication.isPlaying=false; yield break; }
                if(next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            Debug.Log("LUNA_COOP_REGRESSION_OK: UDP dos clientes, arma remota, paquetes fuera de orden, carga visible, suelta en altura, municion especifica, rampa.");
            Cleanup(); EditorApplication.isPlaying=false;
        }
        void Cleanup() { a?.Dispose(); b?.Dispose(); hub?.Dispose(); }
        IEnumerator Run()
        {
            yield return null; Time.timeScale=1;
            var director=FindObjectOfType<LunarHordeDirector>(); if(director!=null) director.enabled=false;
            hub=new LanHub("Regression",0); var endpoint=new IPEndPoint(IPAddress.Loopback,hub.Port);
            a=new HubClient(endpoint,"lan","Player1"); b=new HubClient(endpoint,"lan","Player2");
            float timeout=Time.unscaledTime+4;
            while((!a.Connected||!b.Connected) && Time.unscaledTime<timeout) { hub.Update(); a.Update(); b.Update(); yield return null; }
            Check(a.Connected&&b.Connected,"UDP handshake");
            a.Send(b.MyId,new Packet(21).Text("test").ToArray());
            timeout=Time.unscaledTime+3;
            while(b.Received.Count==0 && Time.unscaledTime<timeout) { hub.Update(); a.Update(); b.Update(); yield return null; }
            Check(b.Received.Count>0,"Relay delivery");
            var pawn=new GameObject("Remote test",typeof(LunarHealth));
            var puppet=pawn.AddComponent<LunarPuppet>();
            puppet.SetWeapon("rifle"); Check(pawn.transform.Find("Arma remota").GetComponentsInChildren<Renderer>().Length>0,"Remote rifle missing");
            puppet.SetWeapon("escopeta"); yield return null;
            Check(puppet.AcceptSequence(10)&&!puppet.AcceptSequence(9),"Out of order player states");
            var crate=GameObject.CreatePrimitive(PrimitiveType.Cube); var rigid=crate.AddComponent<Rigidbody>();
            var pickup=crate.AddComponent<LunarPickup>(); pickup.kind=LunarPickupKind.Carryable;
            pickup.SetRemoteHeld(true); pickup.FollowRemote(new Vector3(40,30,20),Quaternion.identity);
            Check(crate.GetComponent<Renderer>().enabled&&!crate.GetComponent<Collider>().enabled,"Carried object invisible/colliding");
            Check((crate.transform.position-new Vector3(40,30,20)).sqrMagnitude<0.01f,"Remote carry position");
            pickup.SetRemoteHeld(false);
            var socket=new GameObject("Socket").transform; socket.position=new Vector3(200,70,10);
            pickup.Hold(socket); pickup.Release(new Vector3(201,71,11),Vector3.forward*5);
            Check((rigid.position-new Vector3(201,71,11)).sqrMagnitude<0.01f,"Rigid body returned to old pickup origin");
            // Replay snapshots through the actual client decoder; stale states must not move the crate back.
            var session=FindObjectOfType<LunarSession>();
            Set(session,"<Link>k__BackingField",b);
            var coop=new GameObject("Coop decoder").AddComponent<LunarCoop>(); coop.enabled=false;
            Set(coop,"session",session); Set(coop,"pickups",new[]{pickup}); Set(coop,"parts",new LunarShipPart[]{null});
            var localPlayer=FindObjectOfType<LunarPlayerCombat>();
            Set(coop,"player",localPlayer);
            Set(coop,"controller",localPlayer.GetComponent<LunarPlayerController>());
            Set(coop,"body",localPlayer.GetComponent<Rigidbody>());
            Set(coop,"health",localPlayer.GetComponent<LunarHealth>());
            var wirePawn=new GameObject("Wire weapon test",typeof(LunarHealth)); var wirePuppet=wirePawn.AddComponent<LunarPuppet>();
            foreach(string id in new[]{"rifle","escopeta"})
            {
                localPlayer.AddWeapon(localPlayer.FindWeapon(id));
                for(int i=0;i<localPlayer.Weapons.Count;i++) if(localPlayer.Weapons[i].Data.id==id) localPlayer.Equip(i,false);
                var state=(Packet)Call(coop,"WriteState",new Packet(21));
                Check(state.Length==36,"Player state byte count changed");
                Call(coop,"ReadState",new Packet(state.ToArray()),wirePuppet,(byte)255);
                yield return null;
                Check(wirePawn.transform.Find("Arma remota").GetChild(0).name.ToLowerInvariant().Contains(id=="rifle"?"rifle":"escopeta"),"Equipped weapon did not cross packet");
            }
            var pending=(HashSet<ushort>)typeof(LunarCoop).GetField("pendingDrops",Flags).GetValue(coop);
            pending.Add(0); Vector3 pendingPosition=crate.transform.position;
            Call(coop,"ReadParts",Parts(8,Vector3.zero)); Check(crate.transform.position==pendingPosition,"Drop awaiting ack snapped back");
            Call(coop,"ClientReceive",new Packet(new Packet(15).UShort(0).UInt(9).ToArray()));
            Check(!pending.Contains(0),"Drop ack not received");
            Destroy(wirePawn);
            Call(coop,"ReadParts",Parts(11,new Vector3(210,70,10)));
            Vector3 latest=crate.transform.position;
            Call(coop,"ReadParts",Parts(10,Vector3.zero)); Check(crate.transform.position==latest,"Stale snapshot moved released object");
            Set(session,"<Link>k__BackingField",null);
            var player=FindObjectOfType<LunarPlayerCombat>();
            int reserve=player.ReserveAmmo;
            Check(!player.AddAmmoForWeapon("missing",20)&&player.ReserveAmmo==reserve,"Wrong ammo consumed");
            Check(Resources.Load<GameObject>("LunarWeapons/llave")!=null&&Resources.Load<GameObject>("LunarWeapons/ammo-rifle")!=null,"New assets missing");
            Destroy(coop.gameObject); Destroy(crate); Destroy(socket.gameObject); Destroy(pawn);
            // Physical slope test uses the production controller and compound box colliders.
            var ramp=GameObject.CreatePrimitive(PrimitiveType.Cube); ramp.transform.position=new Vector3(10000,100,0); ramp.transform.localScale=new Vector3(10,0.5f,25); ramp.transform.rotation=Quaternion.Euler(-35,0,0);
            var walker=new GameObject("Slope walker"); walker.transform.position=new Vector3(10000,100-Mathf.Tan(35*Mathf.Deg2Rad)*8+0.6f,-8);
            walker.AddComponent<Rigidbody>();
            for(int i=0;i<4;i++) { var box=walker.AddComponent<BoxCollider>(); box.size=new Vector3(0.42f,0.45f,0.42f); box.center=Vector3.up*(0.225f+i*0.45f); }
            climber=walker.AddComponent<LunarPlayerController>(); climbing=true;
            float startY=walker.transform.position.y; float startZ=walker.transform.position.z;
            yield return new WaitForSeconds(3.5f);
            Check(walker.transform.position.y>startY+1 && walker.transform.position.z>startZ+2,"Cannot climb 35 degree ramp: "+walker.transform.position);
            climbing=false; Destroy(walker); Destroy(ramp);
        }
        Packet Parts(uint sequence,Vector3 position) => new Packet(new Packet(7).UInt(sequence).Byte(1).UShort(0).Vec(position).Rot(Quaternion.identity).Byte(255).Bool(false).Bool(false).ToArray());
    }
}
#endif

