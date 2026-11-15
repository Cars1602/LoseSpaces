using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
namespace LunaCero
{
    public sealed class LunarMainMenu : MonoBehaviour
    {
        public string sceneJuego="LunaCeroMapaGrande";
        public static bool IntroShown;
        public bool IntroPlaying {get;private set;}
        public bool MenuReady {get;private set;}
        Canvas canvas;RectTransform main,modal,loadingRoot,videoRoot;Image fade;Font font;LostInSpaceArt art;bool loading;VideoPlayer video;RenderTexture videoTexture;AudioSource videoAudio;
        Color cyan=new Color(0.42f,0.95f,1); Color amber=new Color(1,0.72f,0.32f);
        public static RectTransform Box(string name,Transform parent,Vector2 size,Vector2 pos)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.sizeDelta=size;r.anchoredPosition=pos;return r;}
        Text Label(Transform parent,string value,Vector2 pos,Vector2 size,int fontSize,Color color,TextAnchor align=TextAnchor.MiddleCenter)
        {var t=Box(value,parent,size,pos).gameObject.AddComponent<Text>();t.font=font;t.text=value;t.fontSize=fontSize;t.color=color;t.alignment=align;t.supportRichText=true;return t;}
        Image Plate(Transform parent,Vector2 size,Vector2 pos,Color color)
        {var i=Box("Panel",parent,size,pos).gameObject.AddComponent<Image>();i.color=color;return i;}
        RawImage Picture(Transform parent,Texture texture,Vector2 size,Vector2 pos)
        {var r=Box("Arte",parent,size,pos).gameObject.AddComponent<RawImage>();r.texture=texture;r.raycastTarget=false;return r;}
        Button Action(Transform parent,string title,string hint,float y,UnityEngine.Events.UnityAction click,float width=570)
        {
            var i=Plate(parent,new Vector2(width,66),new Vector2(0,y),new Color(0.012f,0.035f,0.055f,0.91f));
            var b=i.gameObject.AddComponent<Button>();b.targetGraphic=i;b.onClick.AddListener(click);i.gameObject.AddComponent<LostInSpaceHover>();
            Plate(i.transform,new Vector2(3,48),new Vector2(-width/2+1,0),cyan).raycastTarget=false;
            Label(i.transform,title,new Vector2(12,8),new Vector2(width-45,32),25,Color.white);
            Label(i.transform,hint,new Vector2(12,-19),new Vector2(width-45,20),12,cyan);
            return b;
        }
        void Start()
        {
            Time.timeScale=1;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;LunarAudio.Music(false);
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");art=Resources.Load<LostInSpaceArt>("LostInSpaceArt");
            if(art!=null)LostInSpaceStage.Build(art);
            var go=new GameObject("Lost in Space UI",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=0.5f;
            if(FindObjectOfType<UnityEngine.EventSystems.EventSystem>()==null)new GameObject("EventSystem",typeof(UnityEngine.EventSystems.EventSystem),typeof(UnityEngine.EventSystems.StandaloneInputModule));
            main=Box("Menu principal",canvas.transform,new Vector2(1920,1080),Vector2.zero);
            main.gameObject.AddComponent<CanvasGroup>();
            if(art!=null)Picture(main,art.logo,new Vector2(720,380),new Vector2(0,345));
            else Label(main,"LOST IN SPACE",new Vector2(0,350),new Vector2(1000,140),80,Color.white);
            Label(main,"LA CONDOR HA CAIDO  /  POLO SUR LUNAR",new Vector2(0,155),new Vector2(800,24),16,amber);
            Action(main,"INICIAR EXPEDICION","COOPERATIVO / SOLO",90,()=>{PlayerPrefs.DeleteKey("partida_guardada");StartCoroutine(Load());});
            Action(main,"BUSCAR SALA","REUNETE CON TU TRIPULACION",10,()=>{LunarSession.OpenOnlineOnNextScene=true;StartCoroutine(Load());});
            Action(main,"TRIPULACION Y EQUIPO","CINCO ROLES. UNA OPORTUNIDAD DE VOLVER.",-70,Roster);
            Action(main,"REGISTROS DE MISION","ESCUCHA LO QUE SE OCULTA EN LAS SOMBRAS",-150,Logs);
            Action(main,"AJUSTES","AUDIO / PANTALLA / CALIDAD",-440,Settings,360);
            var quit=Action(main,"SALIR AL VACIO","CERRAR EL JUEGO",-440,()=>Application.Quit(),320);quit.GetComponent<RectTransform>().anchoredPosition=new Vector2(720,-440);
            var replay=Action(main,"REPETIR INTRO","TRANSMISION DE LA CONDOR",-440,()=>StartCoroutine(Intro()),320);replay.GetComponent<RectTransform>().anchoredPosition=new Vector2(-720,-440);
            Label(main,"RECUPERA LAS CINCO PIEZAS. REPARA LA NAVE. SOBREVIVE.",new Vector2(0,-350),new Vector2(1000,28),17,Color.white);
            Label(main,"LOST IN SPACE  /  EXPEDICION LUNAR",new Vector2(-650,497),new Vector2(550,28),13,cyan,TextAnchor.MiddleLeft);
            Label(main,"SISTEMAS EN LINEA   +   SENAL ESTABLE",new Vector2(655,497),new Vector2(550,28),13,cyan,TextAnchor.MiddleRight);
            fade=Plate(canvas.transform,new Vector2(4500,4500),Vector2.zero,Color.black);fade.raycastTarget=false;
            main.gameObject.SetActive(false);
            if(!IntroShown)StartCoroutine(Intro());else Reveal();
        }
        void Update(){if(IntroPlaying&&(Input.GetKeyDown(KeyCode.Space)||Input.GetKeyDown(KeyCode.Escape)||Input.GetKeyDown(KeyCode.Return)))SkipIntro();}
        public void SkipIntro(){if(!IntroPlaying)return;IntroPlaying=false;if(video!=null)video.Stop();}
        IEnumerator Intro()
        {
            if(IntroPlaying||loading)yield break;IntroPlaying=true;IntroShown=true;MenuReady=false;main.gameObject.SetActive(false);Close();LunarAudio.Music(false);fade.color=Color.black;
            videoRoot=Box("Transmision introductoria",canvas.transform,new Vector2(1920,1080),Vector2.zero);videoRoot.SetAsLastSibling();
            Plate(videoRoot,new Vector2(4500,4500),Vector2.zero,Color.black);
            var screen=Picture(videoRoot,null,new Vector2(1920,1080),Vector2.zero);var fitter=screen.gameObject.AddComponent<AspectRatioFitter>();fitter.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fitter.aspectRatio=16f/9;
            videoTexture=new RenderTexture(1280,720,0);videoTexture.Create();screen.texture=videoTexture;
            video=videoRoot.gameObject.AddComponent<VideoPlayer>();video.playOnAwake=false;video.isLooping=false;video.waitForFirstFrame=true;video.renderMode=VideoRenderMode.RenderTexture;video.targetTexture=videoTexture;
            video.source=VideoSource.Url;video.url=new System.Uri(Path.Combine(Application.streamingAssetsPath,"LostInSpace_Intro.mp4")).AbsoluteUri;
            videoAudio=videoRoot.gameObject.AddComponent<AudioSource>();videoAudio.volume=0.85f;video.audioOutputMode=VideoAudioOutputMode.AudioSource;video.EnableAudioTrack(0,true);video.SetTargetAudioSource(0,videoAudio);
            video.loopPointReached+=_=>IntroPlaying=false;video.errorReceived+=(_,message)=>{Debug.LogWarning("Intro de video: "+message);IntroPlaying=false;};
            var skip=Action(videoRoot,"OMITIR INTRO","ESPACIO / ESC",-470,SkipIntro,300);skip.GetComponent<RectTransform>().anchoredPosition=new Vector2(730,-470);
            video.Prepare();float deadline=Time.realtimeSinceStartup+12;
            while(IntroPlaying&&!video.isPrepared&&Time.realtimeSinceStartup<deadline)yield return null;
            if(video.isPrepared&&IntroPlaying)video.Play();else IntroPlaying=false;
            while(IntroPlaying)yield return null;
            if(video!=null)video.Stop();Destroy(videoRoot.gameObject);videoRoot=null;if(videoTexture!=null){videoTexture.Release();Destroy(videoTexture);}Reveal();
        }
        void Reveal(){MenuReady=true;main.gameObject.SetActive(true);fade.transform.SetAsLastSibling();StartCoroutine(FadeIn());LunarAudio.Music(true);}
        IEnumerator FadeIn(){for(float t=0;t<0.9f;t+=Time.unscaledDeltaTime){fade.color=new Color(0,0,0,1-t/0.9f);yield return null;}fade.color=Color.clear;}
        RectTransform Modal(string title,string subtitle)
        {
            Close();main.GetComponent<CanvasGroup>()?.SetInteractable(false);
            modal=Box("Panel de consulta",canvas.transform,new Vector2(1920,1080),Vector2.zero);
            Plate(modal,new Vector2(4500,4500),Vector2.zero,new Color(0.002f,0.009f,0.02f,0.94f));
            Label(modal,title,new Vector2(0,385),new Vector2(1400,80),42,Color.white);
            Label(modal,subtitle,new Vector2(0,315),new Vector2(1500,45),19,cyan);
            Action(modal,"VOLVER","CERRAR PANEL",-410,Close,360);return modal;
        }
        void Close(){if(modal!=null)Destroy(modal.gameObject);modal=null;if(main!=null){var g=main.GetComponent<CanvasGroup>();if(g!=null)g.interactable=true;}}
        void Roster()
        {
            var p=Modal("TRIPULACION Y EQUIPO","Ningun astronauta vuelve solo. Elige tu rol al iniciar la expedicion.");
            for(int i=0;i<LunarRoleDefinition.All.Length;i++)
            {
                var def=LunarRoleDefinition.All[i];float x=-700+i*350;
                var card=Plate(p,new Vector2(320,430),new Vector2(x,30),new Color(0.04f,0.075f,0.10f,1));
                Plate(card.transform,new Vector2(320,5),new Vector2(0,212),def.color);
                Label(card.transform,def.title.ToUpperInvariant(),new Vector2(0,155),new Vector2(290,70),25,def.color);
                Label(card.transform,def.mission,new Vector2(0,70),new Vector2(275,95),20,Color.white);
                Label(card.transform,string.Join("\n\n",def.perks),new Vector2(0,-75),new Vector2(270,210),17,new Color(0.75f,0.85f,0.9f));
            }
            Label(p,"ARSENAL  /  PISTOLA  +  FUSIL  +  ESCOPETA  +  RIEL  +  PLASMA",new Vector2(0,-270),new Vector2(1500,45),23,amber);
        }
        void Logs()
        {
            var p=Modal("REGISTROS DE MISION","La Condor se estrello en el polo sur. El hielo mantiene vivo al traje. Las criaturas protegen las sombras.");
            string[] names={"Acechador","Enjambre","Escupidor","Gigante"};
            for(int i=0;i<4;i++){string family=names[i];Action(p,"ESCUCHAR: "+family.ToUpperInvariant(),"ARCHIVO DE AUDIO RECUPERADO",180-i*90,()=>LunarAudio.Play("Monstruos/"+family+"/"+family+"_base_ambiente",Vector3.zero,0.8f,false),720);}
            Label(p,"OBJETIVO / Nucleo · Navegacion · Tanque · Motor · Comunicaciones",new Vector2(0,-240),new Vector2(1500,50),24,amber);
        }
        void Settings()
        {
            var p=Modal("AJUSTES","Configura el traje antes de salir al vacio.");
            Action(p,"VOLUMEN  -","",170,()=>{LunarAudio.SetVolume(Mathf.Clamp01(AudioListener.volume-0.1f));Settings();},400).GetComponent<RectTransform>().anchoredPosition=new Vector2(-230,170);
            Action(p,"VOLUMEN  +","",170,()=>{LunarAudio.SetVolume(Mathf.Clamp01(AudioListener.volume+0.1f));Settings();},400).GetComponent<RectTransform>().anchoredPosition=new Vector2(230,170);
            Label(p,"VOLUMEN GENERAL: "+Mathf.RoundToInt(AudioListener.volume*100)+"%",new Vector2(0,245),new Vector2(800,35),23,amber);
            Action(p,Screen.fullScreen?"PANTALLA COMPLETA: SI":"PANTALLA COMPLETA: NO","CAMBIAR MODO",60,()=>{Screen.fullScreen=!Screen.fullScreen;},860);
            Action(p,"CALIDAD: "+QualitySettings.names[QualitySettings.GetQualityLevel()].ToUpperInvariant(),"CAMBIAR NIVEL GRAFICO",-40,()=>{QualitySettings.SetQualityLevel((QualitySettings.GetQualityLevel()+1)%QualitySettings.names.Length);Settings();},860);
            Action(p,QualitySettings.vSyncCount>0?"SINCRONIZACION VERTICAL: SI":"SINCRONIZACION VERTICAL: NO","CAMBIAR",-140,()=>{QualitySettings.vSyncCount=QualitySettings.vSyncCount>0?0:1;Settings();},860);
        }
        IEnumerator Load()
        {
            if(loading)yield break;loading=true;MenuReady=false;Close();fade.transform.SetAsLastSibling();
            for(float t=0;t<0.6f;t+=Time.unscaledDeltaTime){fade.color=new Color(0,0,0,t/0.6f);yield return null;}
            main.gameObject.SetActive(false);loadingRoot=Box("Carga",canvas.transform,new Vector2(1920,1080),Vector2.zero);loadingRoot.SetAsLastSibling();
            if(art!=null){Picture(loadingRoot,art.background,new Vector2(1920,1080),Vector2.zero);Plate(loadingRoot,new Vector2(4500,4500),Vector2.zero,new Color(0,0.01f,0.025f,0.73f));Picture(loadingRoot,art.logo,new Vector2(700,370),new Vector2(0,270));}
            Label(loadingRoot,"TRANSMISION DE LA CONDOR",new Vector2(0,10),new Vector2(1400,65),36,amber);
            Label(loadingRoot,"La nave ha caido. Tu tripulacion sigue viva.\nRecupera las cinco piezas y encuentra el camino de vuelta.",new Vector2(0,-85),new Vector2(1500,120),27,Color.white);
            var bar=Plate(loadingRoot,new Vector2(900,5),new Vector2(0,-220),new Color(0.1f,0.23f,0.28f));
            var fill=Plate(bar.transform,new Vector2(0,5),Vector2.zero,cyan);fill.rectTransform.pivot=new Vector2(0,0.5f);fill.rectTransform.anchoredPosition=new Vector2(-450,0);
            var progress=Label(loadingRoot,"Preparando sistemas...",new Vector2(0,-270),new Vector2(1200,40),20,cyan);
            Label(loadingRoot,"Busca municion compatible. Cuida el oxigeno. Las estaciones medicas se agotan.",new Vector2(0,-410),new Vector2(1600,50),21,Color.white);
            yield return null;
            if(!Application.CanStreamedLevelBeLoaded(sceneJuego)){progress.text="Escena no disponible. Revisa Build Settings.";yield break;}
            var op=SceneManager.LoadSceneAsync(sceneJuego);op.allowSceneActivation=false;float start=Time.realtimeSinceStartup;
            while(op.progress<0.9f||Time.realtimeSinceStartup-start<2.5f){float t=Mathf.Clamp01(op.progress/0.9f);fill.rectTransform.sizeDelta=new Vector2(900*t,5);progress.text="CARGANDO EXPEDICION  /  "+Mathf.RoundToInt(t*100)+"%";yield return null;}
            LunarAudio.Music(false);op.allowSceneActivation=true;while(!op.isDone)yield return null;Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;
        }
    }
    static class LostCanvasExtensions{public static void SetInteractable(this CanvasGroup group,bool value){group.interactable=value;}}
}
