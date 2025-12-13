using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class TooltipManager : SingletonBehaviour<TooltipManager>
{
    protected override bool IsDontDestroy() => false;

    public RectTransform tooltipRect;
    public TextMeshProUGUI headerText;
    public TextMeshProUGUI contentText;
    public CanvasGroup canvasGroup; // 페이드 효과용

    [Header("Settings")]
    // 🖱️ [신규] 마우스 커서와 툴팁 사이의 간격 (적당히 20~30 추천)
    public Vector2 offset = new Vector2(25f, 25f);

    private void Start()
    {
        Hide();
    }

    private void Update()
    {
        if (canvasGroup.alpha > 0)
        {
            Vector2 mousePos = Input.mousePosition;
            
            // 1. 화면 비율에 따른 Pivot 계산 (기존 로직)
            // (화면 왼쪽이면 Pivot X=0, 오른쪽이면 X=1이 되어 툴팁이 화면 안쪽으로 자라남)
            float pivotX = mousePos.x / Screen.width;
            float pivotY = mousePos.y / Screen.height;
            tooltipRect.pivot = new Vector2(pivotX, pivotY);

            // 2. 🌟 [핵심 수정] 마우스 커서 크기만큼 띄우기 (Smart Offset)
            // 화면 왼쪽에 있을 땐 오른쪽으로(+), 오른쪽에 있을 땐 왼쪽으로(-) 밀어야 함
            float offsetX = (pivotX < 0.5f) ? offset.x : -offset.x;
            float offsetY = (pivotY < 0.5f) ? offset.y : -offset.y;

            // 3. 최종 위치 적용
            tooltipRect.transform.position = mousePos + new Vector2(offsetX, offsetY);
        }
    }

    public void Show(string content, string header = "")
    {
        // 툴팁을 최상단으로 (가림 방지)
        tooltipRect.transform.SetAsLastSibling();

        if (string.IsNullOrEmpty(header))
        {
            headerText.gameObject.SetActive(false);
        }
        else
        {
            headerText.gameObject.SetActive(true);
            headerText.text = header;
        }

        contentText.text = content;

        LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);

        canvasGroup.alpha = 1f;
    }

    public void Hide()
    {
        canvasGroup.alpha = 0f;
    }
}