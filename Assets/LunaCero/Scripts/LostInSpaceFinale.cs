using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace LunaCero
{
    public sealed class LostInSpaceFinale : MonoBehaviour
    {
        static LostInSpaceFinale instance;
        public static bool Active=>instance!=null&&instance.started;
        bool started;LunarRepairPoint repair;
        public bool Complete=>complete;bool complete;
        void Awake(){repair=GetComponent<LunarRepairPoint>();}
        void Update()
        {
            if(started||repair==null||!repair.Repaired||repair.remote)return;
            FindObjectOfType<LunarCoop>()?.BroadcastVictory();Begin();
        }
        public void Begin()
        {
            if(started)return;started=true;instance=this;StartCoroutine(Launch());
        }
        IEnumerator Launch()
        {
            Time.timeScale=1;PlayerPrefs.SetInt("LostInSpace.MissionCompleted",1);PlayerPrefs.DeleteKey("partida_guardada");PlayerPrefs.Save();
            foreach(var p in FindObjectsOfType<LunarPlayerCombat>())
            {
                p.Drop(false);p.GetComponent<LunarHealth>().NetworkProtected=true;
                foreach(var b in p.GetComponents<MonoBehaviour>())b.enabled=false;
                var body=p.GetComponent<Rigidbody>();body.velocity=Vector3.zero;body.isKinematic=true;
                foreach(var r in p.GetComponentsInChildren<Renderer>())r.enabled=false;
            }
            foreach(var e in FindObjectsOfType<LunarEnemy>()){e.enabled=false;var agent=e.GetComponent<UnityEngine.AI.NavMeshAgent>();if(agent!=null)agent.enabled=false;}
            foreach(var d in FindObjectsOfType<LunarHordeDirector>())d.enabled=false;
            foreach(var p in FindObjectsOfType<LunarShipPart>())p.enabled=false;
            if(repair!=null)repair.enabled=false;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            // Video de final opcional (StreamingAssets/Videos/final.mp4): si existe reemplaza el despegue animado.
            bool video=false;
            // El modelo nuevo despega en la escena; el video sigue siendo alternativa para mapas antiguos.
            if(repair==null||repair.ShipVisual==null)yield return LunarEndVideo.Play("final",played=>video=played);
            float flight=video?0f:9f;
            var art=Resources.Load<LostInSpaceArt>("LostInSpaceArt");var camera=Camera.main;
            var root=new GameObject("Despegue de la Condor").transform;
            var visual=repair!=null?repair.ShipVisual:null;
            GameObject ship;
            if(visual!=null)
            {
                visual.SetProgress(1f);
                ship=visual.gameObject;
                ship.transform.SetParent(root,true);
            }
            else ship=LostInSpaceStage.Actor(art!=null?art.ship:null,root,transform.position+Vector3.up*2,3.4f,25,"",0);
            if(ship!=null)Destroy(ship.GetComponent<LostInSpaceStage>());
            var exhaust=new GameObject("Propulsores de escape");exhaust.transform.SetParent(ship!=null?ship.transform:root,false);exhaust.transform.localPosition=new Vector3(0,0.2f,0);
            var particles=exhaust.AddComponent<ParticleSystem>();var main=particles.main;main.startColor=new Color(0.2f,0.85f,1);main.startLifetime=0.8f;main.startSize=0.28f;main.startSpeed=5;var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Cone;exhaust.transform.localRotation=Quaternion.Euler(90,0,0);
            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial=LunarHud.Particle(new Color(0.2f,0.85f,1,0.5f));
            ParticleSystem secondEngine=null;
            if(visual!=null)
            {
                Bounds bounds=LunarLoot.LocalBounds(ship.transform);
                exhaust.transform.localPosition=new Vector3(-bounds.size.x*0.18f,bounds.center.y,bounds.min.z+0.15f);
                exhaust.transform.localRotation=Quaternion.Euler(0,180,0);
                main.startSize=0.4f;main.startLifetime=0.5f;main.startSpeed=9f;
                shape.angle=12f;shape.radius=0.22f;
                var other=Instantiate(exhaust,ship.transform,false);
                other.name="Motor derecho";
                other.transform.localPosition=new Vector3(bounds.size.x*0.18f,bounds.center.y,bounds.min.z+0.15f);
                secondEngine=other.GetComponent<ParticleSystem>();
            }
            var canvasGo=new GameObject("Final de mision",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));var canvas=canvasGo.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
            var scaler=canvasGo.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var title=Text(canvas.transform,font,"CONDOR / SECUENCIA DE DESPEGUE",new Vector2(0,380),30,new Color(0.5f,0.95f,1));
            var text=Text(canvas.transform,font,"Nucleo en linea. Navegacion restaurada. Tripulacion a bordo.",new Vector2(0,-400),22,Color.white);
            Vector3 basePosition=transform.position;
            Vector3 resting=ship!=null?ship.transform.position:basePosition+Vector3.up*2;
            Quaternion restingRotation=ship!=null?ship.transform.rotation:Quaternion.identity;
            float began=Time.realtimeSinceStartup;
            if(video&&ship!=null)ship.SetActive(false);
            while(Time.realtimeSinceStartup-began<flight)
            {
                float t=(Time.realtimeSinceStartup-began)/9;
                float ascent=Mathf.Max(0,t-0.25f);
                Vector3 target=resting+Vector3.up*(ascent*ascent*85);
                if(ship!=null)
                {
                    ship.transform.position=target;
                    ship.transform.rotation=restingRotation*Quaternion.Euler(-Mathf.SmoothStep(0,12,ascent),0,0);
                }
                var emission=particles.emission;emission.rateOverTime=Mathf.Lerp(12,100,Mathf.Clamp01(t*3));
                if(secondEngine!=null){var otherEmission=secondEngine.emission;otherEmission.rateOverTime=emission.rateOverTime;}
                if(camera!=null){camera.transform.position=basePosition+new Vector3(-12+t*3,6+t*15,-16);camera.transform.LookAt(target+Vector3.up);}
                title.text=t<0.3f?"CONDOR / INICIANDO PROPULSORES":"CONDOR / ABANDONANDO LA SUPERFICIE";
                yield return null;
            }
            complete=true;Destroy(title.gameObject);Destroy(text.gameObject);
            if(art!=null){var bg=LunarMainMenu.Box("Fondo de victoria",canvas.transform,new Vector2(1920,1080),Vector2.zero).gameObject.AddComponent<RawImage>();bg.texture=art.background;}
            var dark=LunarMainMenu.Box("Sombra",canvas.transform,new Vector2(4500,4500),Vector2.zero).gameObject.AddComponent<Image>();dark.color=new Color(0,0.01f,0.025f,0.8f);
            if(art!=null){var logo=LunarMainMenu.Box("Logo",canvas.transform,new Vector2(700,370),new Vector2(0,275)).gameObject.AddComponent<RawImage>();logo.texture=art.logo;}
            Text(canvas.transform,font,"MISION COMPLETADA",new Vector2(0,25),54,new Color(1,0.74f,0.32f));
            Text(canvas.transform,font,"La Condor ha despegado.\nLa tripulacion vuelve a casa.",new Vector2(0,-100),31,Color.white);
            Text(canvas.transform,font,$"{(repair!=null?repair.requiredParts:20)} / {(repair!=null?repair.requiredParts:20)} PIEZAS INSTALADAS   •   SENAL RECUPERADA",new Vector2(0,-215),22,new Color(0.45f,0.9f,1));
            var button=LunarMainMenu.Box("Volver al inicio",canvas.transform,new Vector2(600,75),new Vector2(0,-355)).gameObject.AddComponent<Image>();button.color=new Color(0.03f,0.22f,0.27f);
            button.gameObject.AddComponent<Button>().onClick.AddListener(Return);button.gameObject.AddComponent<LostInSpaceHover>();Text(button.transform,font,"VOLVER AL INICIO",Vector2.zero,26,Color.white);
            if(FindObjectOfType<UnityEngine.EventSystems.EventSystem>()==null)new GameObject("EventSystem",typeof(UnityEngine.EventSystems.EventSystem),typeof(UnityEngine.EventSystems.StandaloneInputModule));
        }
        Text Text(Transform parent,Font font,string value,Vector2 pos,int size,Color color){var t=LunarMainMenu.Box(value,parent,new Vector2(1600,140),pos).gameObject.AddComponent<Text>();t.font=font;t.text=value;t.fontSize=size;t.alignment=TextAnchor.MiddleCenter;t.color=color;t.raycastTarget=false;return t;}
        public void Return(){if(LunarSession.Instance!=null)LunarSession.Instance.ReturnToMainMenu();else SceneManager.LoadScene("MenuPrincipal");}
        void OnDestroy(){if(instance==this)instance=null;}
    }
}
