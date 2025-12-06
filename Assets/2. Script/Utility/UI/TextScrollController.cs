using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems; // 마우스 오버 감지용 (선택 사항)

public class TextScrollController : MonoBehaviour
{
    [Header("UI References")]
    public RectTransform contentText;
    public RectTransform viewPort;
    public Slider scrollSlider;

    [Header("Settings")]
    public bool reverseDirection = true;
    [Tooltip("마우스 휠 감도")]
    public float scrollSensitivity = 0.1f;

    // Viewport 위에 마우스가 있는지 확인하기 위한 플래그 (선택 사항)
    // private bool isPointerOverViewport = false; 

    private void Start()
    {
        if (scrollSlider != null)
        {
            scrollSlider.onValueChanged.AddListener(OnSliderValueChanged);
            scrollSlider.value = reverseDirection ? 1 : 0;
        }
    }

    private void Update()
    {
        // Viewport가 활성화되어 있고, 슬라이더가 있을 때만 작동
        if (viewPort != null && viewPort.gameObject.activeInHierarchy && scrollSlider != null)
        {
            // 마우스 휠 입력 감지 (위로: 양수, 아래로: 음수)
            float scrollInput = Input.GetAxis("Mouse ScrollWheel");

            if (Mathf.Abs(scrollInput) > 0.001f)
            {
                // 슬라이더 값 변경 (방향에 따라 부호 조정)
                // reverseDirection(위가 1)일 때: 휠 올림 -> 값 증가 / 휠 내림 -> 값 감소
                // reverseDirection(위가 0)일 때: 휠 올림 -> 값 감소 / 휠 내림 -> 값 증가
                
                float delta = scrollInput * scrollSensitivity;
                
                if (reverseDirection)
                    scrollSlider.value += delta; 
                else
                    scrollSlider.value -= delta;
            }
        }
    }

    private void OnSliderValueChanged(float value)
    {
        if (contentText == null || viewPort == null) return;

        float contentHeight = contentText.rect.height;
        float viewportHeight = viewPort.rect.height;
        float maxScrollY = contentHeight - viewportHeight;

        if (maxScrollY <= 0)
        {
            contentText.anchoredPosition = new Vector2(contentText.anchoredPosition.x, 0);
            return;
        }

        float normalizedValue = reverseDirection ? (1 - value) : value;
        float targetY = normalizedValue * maxScrollY;

        contentText.anchoredPosition = new Vector2(contentText.anchoredPosition.x, targetY);
    }
    
    public void RefreshScroll()
    {
        OnSliderValueChanged(scrollSlider.value);
    }
}