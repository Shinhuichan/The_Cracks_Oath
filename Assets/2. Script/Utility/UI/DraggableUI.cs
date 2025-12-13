using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Events;

[RequireComponent(typeof(CanvasGroup))] // 투명도 조절을 위해 필수
public class DraggableUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
{
    [Header("Target Setting")]
    [Tooltip("이동시킬 오브젝트. 비워두면 본인이 됩니다.")]
    [SerializeField] private RectTransform targetRectTransform;

    [Header("Boundary Settings")]
    [Tooltip("화면 밖으로 나가지 못하게 막습니다.")]
    [SerializeField] private bool keepInScreen = true;
    [Tooltip("화면 여백 (Padding)")]
    [SerializeField] private float screenPadding = 10f;

    [Header("Drag Options")]
    [Tooltip("드래그 중일 때 투명도 (0~1)")]
    [Range(0.1f, 1f)] [SerializeField] private float dragAlpha = 0.8f;
    [SerializeField] private bool lockX = false; // X축 고정
    [SerializeField] private bool lockY = false; // Y축 고정

    [Header("Scale Settings")]
    [SerializeField] private float minScale = 0.5f;
    [SerializeField] private float maxScale = 2.0f;
    [SerializeField] private Button scaleUpButton;
    [SerializeField] private Button scaleDownButton;
    [SerializeField] private Button resetButton;

    [Header("Events")]
    public UnityEvent onBeginDrag;
    public UnityEvent onEndDrag;

    // 내부 변수
    private Canvas _canvas;
    private CanvasGroup _canvasGroup;
    private Vector3 _originalScale;
    private Vector2 _pointerOffset; // 마우스 포인터와 UI 중심 간의 간격 보정

    private void Awake()
    {
        InitializeComponents();
        RegisterButtonEvents();
    }

    private void InitializeComponents()
    {
        // 1. 타겟 설정
        if (targetRectTransform == null)
            targetRectTransform = GetComponent<RectTransform>();

        _originalScale = targetRectTransform.localScale;

        // 2. 캔버스 찾기 (최상위 부모 캔버스까지 탐색)
        _canvas = GetComponentInParent<Canvas>();
        if (_canvas != null && _canvas.rootCanvas != null)
        {
            _canvas = _canvas.rootCanvas; // 가장 바깥쪽 캔버스를 기준으로 잡음
        }

        // 3. CanvasGroup (투명도 제어용)
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private void RegisterButtonEvents()
    {
        if (scaleUpButton) scaleUpButton.onClick.AddListener(() => ChangeScale(0.1f));
        if (scaleDownButton) scaleDownButton.onClick.AddListener(() => ChangeScale(-0.1f));
        if (resetButton) resetButton.onClick.AddListener(ResetScale);
    }

    private void OnEnable()
    {
        // 켜질 때 맨 앞으로
        transform.SetAsLastSibling();
    }

    // =========================================================
    // 🖱️ 인터페이스 구현 (PointerDown, Drag)
    // =========================================================

    // 1. 클릭만 해도 맨 앞으로 가져오기 (UX 핵심)
    public void OnPointerDown(PointerEventData eventData)
    {
        targetRectTransform.SetAsLastSibling();
        
        // 드래그 시작 전, 마우스 클릭 지점과 UI 앵커 간의 오프셋 계산
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            targetRectTransform, 
            eventData.position, 
            _canvas.worldCamera, 
            out _pointerOffset
        );
    }

    // 2. 드래그 시작
    public void OnBeginDrag(PointerEventData eventData)
    {
        _canvasGroup.alpha = dragAlpha; // 반투명하게
        _canvasGroup.blocksRaycasts = false; // 드래그 중 뒤에 있는 UI 가리지 않도록 (선택사항)
        
        onBeginDrag?.Invoke();
    }

    // 3. 드래그 중 (이동 로직)
    public void OnDrag(PointerEventData eventData)
    {
        if (targetRectTransform == null || _canvas == null) return;

        Vector2 localPointerPosition;
        
        // 부모(Canvas 등) 기준으로 마우스 좌표를 변환
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            targetRectTransform.parent as RectTransform,
            eventData.position,
            _canvas.worldCamera,
            out localPointerPosition
        ))
        {
            // 오프셋을 적용하여 자연스러운 이동 (UI 중심이 마우스로 튀지 않음)
            Vector2 targetPosition = localPointerPosition - (_pointerOffset * targetRectTransform.localScale.x);
            // *주의: pointerOffset에 scale을 곱해야 확대/축소 상태에서도 정확히 잡힘

            Vector2 finalPos = targetRectTransform.anchoredPosition;

            if (!lockX) finalPos.x = targetPosition.x;
            if (!lockY) finalPos.y = targetPosition.y;

            targetRectTransform.anchoredPosition = finalPos;

            // 화면 밖 이탈 방지
            if (keepInScreen) ClampToWindow();
        }
    }

    // 4. 드래그 종료
    public void OnEndDrag(PointerEventData eventData)
    {
        _canvasGroup.alpha = 1.0f; // 불투명 복귀
        _canvasGroup.blocksRaycasts = true;

        onEndDrag?.Invoke();
    }

    // =========================================================
    // 🚧 화면 가두기 (Clamping Logic) - 핵심 기능
    // =========================================================
    private void ClampToWindow()
    {
        Vector3[] corners = new Vector3[4];
        targetRectTransform.GetWorldCorners(corners); // 현재 UI의 월드 좌표 4개

        // RectTransform의 크기
        float width = (corners[2].x - corners[0].x);
        float height = (corners[2].y - corners[0].y);

        // 캔버스 사이즈 (Screen Space - Overlay 기준)
        // Camera 모드라면 worldCamera.ViewportToWorldPoint 등을 써야 하지만
        // 주식 게임 UI는 보통 Overlay 모드이므로 Screen.width/height 사용이 효율적
        float minX = screenPadding;
        float maxX = Screen.width - screenPadding;
        float minY = screenPadding;
        float maxY = Screen.height - screenPadding;

        Vector3 pos = targetRectTransform.position;

        // X축 보정 (왼쪽/오른쪽)
        if (pos.x - width * targetRectTransform.pivot.x < minX)
            pos.x = minX + width * targetRectTransform.pivot.x;
        else if (pos.x + width * (1 - targetRectTransform.pivot.x) > maxX)
            pos.x = maxX - width * (1 - targetRectTransform.pivot.x);

        // Y축 보정 (아래/위)
        if (pos.y - height * targetRectTransform.pivot.y < minY)
            pos.y = minY + height * targetRectTransform.pivot.y;
        else if (pos.y + height * (1 - targetRectTransform.pivot.y) > maxY)
            pos.y = maxY - height * (1 - targetRectTransform.pivot.y);

        targetRectTransform.position = pos;
    }

    // =========================================================
    // 🔍 스케일 조절 (Scale Control)
    // =========================================================
    private void ChangeScale(float delta)
    {
        if (targetRectTransform == null) return;

        Vector3 newScale = targetRectTransform.localScale + Vector3.one * delta;
        
        // 최소/최대 크기 제한 (Clamp)
        newScale.x = Mathf.Clamp(newScale.x, minScale, maxScale);
        newScale.y = Mathf.Clamp(newScale.y, minScale, maxScale);
        newScale.z = 1f;

        targetRectTransform.localScale = newScale;
        
        // 크기가 변했으니 화면 밖으로 나갔는지 체크
        if (keepInScreen) ClampToWindow(); 
    }

    private void ResetScale()
    {
        if (targetRectTransform != null)
        {
            targetRectTransform.localScale = _originalScale;
            if (keepInScreen) ClampToWindow();
        }
    }
}