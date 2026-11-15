using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace LunaCero
{
    public sealed class LostInSpaceHover : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,ISelectHandler,IDeselectHandler
    {
        bool over;Vector3 scale=Vector3.one;Image image;
        void Awake(){image=GetComponent<Image>();}
        public void OnPointerEnter(PointerEventData e){over=true;}
        public void OnPointerExit(PointerEventData e){over=false;}
        public void OnSelect(BaseEventData e){over=true;}
        public void OnDeselect(BaseEventData e){over=false;}
        void Update(){transform.localScale=Vector3.Lerp(transform.localScale,over?Vector3.one*1.025f:Vector3.one,Time.unscaledDeltaTime*14);if(image!=null)image.color=Color.Lerp(image.color,over?new Color(0.08f,0.28f,0.34f,0.98f):new Color(0.012f,0.035f,0.055f,0.91f),Time.unscaledDeltaTime*12);}
    }
}
