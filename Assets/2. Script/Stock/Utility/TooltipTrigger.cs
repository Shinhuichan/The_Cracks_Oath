using UnityEngine;
using UnityEngine.EventSystems;

public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public string header;
    [TextArea(3, 10)] public string content;

    // 마우스가 들어왔을 때
    public void OnPointerEnter(PointerEventData eventData)
    {
        TooltipManager.I.Show(content, header);
    }

    // 마우스가 나갔을 때
    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipManager.I.Hide();
    }
}