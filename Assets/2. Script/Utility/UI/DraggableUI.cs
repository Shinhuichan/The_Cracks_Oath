using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Events; // ➕ UnityEvent 사용을 위해 추가

public class DraggableUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Target Setting")]
    [Tooltip("이동시킬 오브젝트를 연결하세요. 비워두면 이 스크립트가 붙은 오브젝트가 이동합니다.")]
    [SerializeField] private GameObject targetObject;

    [Header("Scale Control Buttons (Optional)")]
    [SerializeField] private Button scaleUpButton;   // 1.5배
    [SerializeField] private Button scaleDownButton; // 0.75배
    [SerializeField] private Button resetButton;     // 1.0배 (원래 크기)

    private RectTransform targetRectTransform;
    private Canvas parentCanvas;
    private Vector3 originalScale; // 초기 크기 저장용

    [Tooltip("드래그가 시작될 때 호출될 이벤트")]
    public UnityEvent onBeginDrag; // ➕ [신규] 외부에서 기능을 연결할 이벤트

    private void Awake()
    {
        // 1. 타겟 오브젝트 설정
        if (targetObject == null)
        {
            targetObject = this.gameObject;
        }

        // 2. RectTransform 컴포넌트 가져오기
        targetRectTransform = targetObject.GetComponent<RectTransform>();
        if (targetRectTransform == null)
        {
            Debug.LogError("타겟 오브젝트에 RectTransform이 없습니다! UI 오브젝트인지 확인하세요.");
            return;
        }

        // 초기 크기 저장
        originalScale = targetRectTransform.localScale;

        // 3. 부모 캔버스 찾기 (드래그 좌표 계산용)
        parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            Debug.LogError("이 오브젝트는 Canvas 하위에 있어야 합니다.");
        }

        // 4. 버튼 이벤트 연결
        if (scaleUpButton != null)   scaleUpButton.onClick.AddListener(OnScaleUp);
        if (scaleDownButton != null) scaleDownButton.onClick.AddListener(OnScaleDown);
        if (resetButton != null)     resetButton.onClick.AddListener(OnResetScale);
    }

    // ➕ [추가] UI가 활성화될 때마다 맨 앞으로 이동
    private void OnEnable()
    {
        if (targetRectTransform != null)
        {
            targetRectTransform.SetAsLastSibling();
        }
    }

    // === 버튼 기능 구현 ===
    private void OnScaleUp()
    {
        if (targetRectTransform != null)
            targetRectTransform.localScale = originalScale * 1.5f;
    }

    private void OnScaleDown()
    {
        if (targetRectTransform != null)
            targetRectTransform.localScale = originalScale * 0.75f;
    }

    private void OnResetScale()
    {
        if (targetRectTransform != null)
            targetRectTransform.localScale = originalScale;
    }

    // === 드래그 인터페이스 구현 ===

    // 1. 드래그 시작 시 호출
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (targetRectTransform != null)
        {
            targetRectTransform.SetAsLastSibling();
        }

        // ➕ [신규] 드래그 시작 이벤트 호출
        onBeginDrag?.Invoke();
    }

    // 2. 드래그 중 계속 호출 (이동 로직)
    public void OnDrag(PointerEventData eventData)
    {
        if (targetRectTransform != null && parentCanvas != null)
        {
            // 마우스 이동량(delta)을 캔버스 스케일로 보정하여 이동
            targetRectTransform.anchoredPosition += eventData.delta / parentCanvas.scaleFactor;
        }
    }

    // 3. 드래그 종료 시 호출 (필요 시 구현)
    public void OnEndDrag(PointerEventData eventData)
    {
        // 드래그 끝났을 때 할 일이 있다면 여기에 작성
    }
}