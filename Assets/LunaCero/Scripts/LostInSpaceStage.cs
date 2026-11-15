using UnityEngine;
namespace LunaCero
{
    public sealed class LostInSpaceStage : MonoBehaviour
    {
        public static GameObject Actor(GameObject prefab,Transform parent,Vector3 feet,float height,float yaw,string state,float speed)
        {
            if(prefab==null)return null;
            var root=new GameObject(prefab.name+" exposicion");root.transform.SetParent(parent,false);
            var model=Instantiate(prefab,root.transform);model.SetActive(true);
            foreach(var b in model.GetComponentsInChildren<MonoBehaviour>())Destroy(b);
            foreach(var c in model.GetComponentsInChildren<Collider>())Destroy(c);
            foreach(var r in model.GetComponentsInChildren<Rigidbody>())Destroy(r);
            var renderers=model.GetComponentsInChildren<Renderer>();if(renderers.Length==0)return root;
            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            float scale=height/Mathf.Max(0.01f,bounds.size.y);root.transform.localScale=Vector3.one*scale;
            model.transform.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*scale;
            root.transform.localPosition=feet;root.transform.localRotation=Quaternion.Euler(0,yaw,0);
            foreach(var r in renderers){r.enabled=true;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
            var animator=model.GetComponentInChildren<Animator>();if(animator!=null){animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;if(animator.HasState(0,Animator.StringToHash(state)))animator.Play(state,0,Random.value);animator.speed=speed;}
            var idle=root.AddComponent<LostInSpaceStage>();idle.rest=feet;idle.phase=Random.value*6;idle.presentation=model.transform;idle.surfaces=renderers;
            if(animator!=null){animator.Update(0);idle.rig=animator;foreach(var bone in animator.GetComponentsInChildren<Transform>())if(bone.name.ToLowerInvariant().Contains("hips")||bone.name.ToLowerInvariant().Contains("pelvis")){idle.hips=bone;idle.hipsRest=animator.transform.InverseTransformPoint(bone.position);break;}}
            return root;
        }
        Vector3 rest;float phase;Animator rig;Transform hips;Vector3 hipsRest;Transform presentation;Renderer[] surfaces;
        void LateUpdate()
        {
            if(hips!=null&&rig!=null){var p=rig.transform.InverseTransformPoint(hips.position);p.x=hipsRest.x;p.z=hipsRest.z;hips.position=rig.transform.TransformPoint(p);}
            if(presentation!=null&&surfaces!=null&&surfaces.Length>0)
            {
                var bounds=surfaces[0].bounds;foreach(var r in surfaces)bounds.Encapsulate(r.bounds);
                presentation.position+=new Vector3(transform.position.x-bounds.center.x,transform.position.y-bounds.min.y,transform.position.z-bounds.center.z);
            }
        }
        void Update(){transform.localPosition=rest+Vector3.up*Mathf.Sin(Time.unscaledTime*1.4f+phase)*0.025f;}
        public static void Build(LostInSpaceArt art)
        {
            var root=new GameObject("Escenario 3D de inicio").transform;
            var camera=Camera.main;if(camera!=null){camera.transform.SetPositionAndRotation(new Vector3(0,0,-15),Quaternion.identity);camera.orthographic=true;camera.orthographicSize=5.625f;camera.nearClipPlane=0.1f;camera.farClipPlane=80;camera.backgroundColor=Color.black;}
            var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);Destroy(quad.GetComponent<Collider>());quad.transform.SetParent(root);quad.transform.position=new Vector3(0,0,14);quad.transform.localScale=new Vector3(20,11.25f,1);
            var mat=new Material(art.backdropShader!=null?art.backdropShader:Shader.Find("Unlit/Texture"));mat.mainTexture=art.background;quad.GetComponent<Renderer>().material=mat;
            RenderSettings.ambientLight=new Color(0.5f,0.58f,0.7f);RenderSettings.fog=false;
            Light("Luz principal",new Vector3(-3,6,-6),new Color(0.72f,0.86f,1),1.5f,18);
            Light("Contorno naranja",new Vector3(-7,-1,2),new Color(1,0.38f,0.08f),3,12);
            Light("Contorno violeta",new Vector3(8,2,0),new Color(0.65f,0.35f,1),2.5f,12);
            var sun=new GameObject("Luz lunar").AddComponent<Light>();sun.type=LightType.Directional;sun.color=new Color(0.65f,0.8f,1);sun.intensity=0.8f;sun.transform.rotation=Quaternion.Euler(25,-25,0);
            for(int i=0;i<4;i++)
            {
                var crew=Actor(art.astronaut,root,new Vector3(-3.5f+i*2.3f,-4.65f,-0.5f+i*0.08f),3.05f,165+i*8,"Reposo",0.65f);
                if(crew==null)continue;
                Color tint=new[]{new Color(0.65f,0.82f,1),new Color(1,0.72f,0.74f),new Color(0.9f,0.98f,0.65f),new Color(0.65f,0.85f,0.74f)}[i];
                foreach(var r in crew.GetComponentsInChildren<Renderer>()) foreach(var material in r.materials) if(material.HasProperty("_Color")&&material.color.maxColorComponent>0.4f)material.color*=tint;
            }
            Actor(art.giant,root,new Vector3(7,-4.35f,2),6.6f,205,"Correr",0.13f);
            Actor(art.stalker,root,new Vector3(-7.8f,-4.3f,1),3.3f,140,"Acechar",0.18f);
            Actor(art.spitter,root,new Vector3(5.3f,-3.9f,3),3.1f,205,"Correr",0.12f);
            Actor(art.common,root,new Vector3(-8.3f,-4.9f,-0.3f),1.55f,160,"Correr",0.22f);
            Actor(art.common,root,new Vector3(-5.8f,-4.9f,0),1.35f,190,"Correr",0.18f);
        }
        static void Light(string name,Vector3 p,Color color,float intensity,float range){var l=new GameObject(name).AddComponent<Light>();l.transform.position=p;l.color=color;l.intensity=intensity;l.range=range;}
    }
}
