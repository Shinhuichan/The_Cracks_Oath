using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Graphic))] // UI 요소(Image, Text 등)에만 붙일 수 있음
public class CursorTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Cursor Settings")]
    public string hoverCursor = "Hand";   // 마우스 올렸을 때
    public string clickCursor = "Click";  // 클릭 누르고 있을 때 (선택 사항)

    private bool isHovering = false;

    // 마우스 들어옴
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        CursorManager.I.SetCursor(hoverCursor);
    }

    // 마우스 나감
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        CursorManager.I.SetDefault();
    }

    // 클릭 누름
    public void OnPointerDown(PointerEventData eventData)
    {
        if (!string.IsNullOrEmpty(clickCursor))
        {
            CursorManager.I.SetCursor(clickCursor);
        }
    }

    // 클릭 뗌
    public void OnPointerUp(PointerEventData eventData)
    {
        if (isHovering)
        {
            CursorManager.I.SetCursor(hoverCursor); // 아직 위에 있으면 호버 커서로
        }
        else
        {
            CursorManager.I.SetDefault(); // 밖이면 기본 커서로
        }
    }

    // 오브젝트가 비활성화될 때 커서 꼬임 방지
    private void OnDisable()
    {
        if (CursorManager.I != null)
        {
            CursorManager.I.SetDefault();
        }
    }
}