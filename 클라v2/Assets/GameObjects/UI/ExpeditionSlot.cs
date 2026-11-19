using UnityEngine;
using UnityEngine.EventSystems;

public sealed class ExpeditionSlot:MonoBehaviour,IPointerClickHandler,IPointerEnterHandler,IPointerExitHandler
{
    [SerializeField] ExpeditionUI owner;
    [SerializeField] int index;
    public void Bind(ExpeditionUI view,int slot){owner=view;index=slot;}
    public void OnPointerClick(PointerEventData data){owner.Select(index);if(data.button==PointerEventData.InputButton.Right)owner.UseSelected();}
    public void OnPointerEnter(PointerEventData data){owner.Hover(index,true);}
    public void OnPointerExit(PointerEventData data){owner.Hover(index,false);}
}
