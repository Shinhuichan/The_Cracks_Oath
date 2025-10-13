using UnityEngine;
using CustomInspector;

[RequireComponent(typeof(Collider))] // 마우스 이벤트용
public class DragNDrop : MonoBehaviour
{
    [Tooltip("해당 Object를 클릭했을 때 일시적으로 올릴 Y Offset")]
    public float clickedReactY = 1f;

    [SerializeField, ReadOnly] private Camera cam;

    // 내부 상태
    private bool dragging;
    private float baseY;        // 시작 높이
    private float dragY;        // 드래그 중 유지할 높이 (baseY + clickedReactY)
    private Vector3 grabOffset; // 잡은 지점 오프셋(오브젝트 중심 - 평면 교점)
    private Plane dragPlane;    // 드래그에 사용할 평면 (수평: y = dragY)

    private void Start() { cam = cam != null ? cam : Camera.main; }

    private void OnMouseDown()
    {
        if (cam == null) return;

        baseY = transform.position.y;
        dragY = baseY + clickedReactY;

        // 클릭 반응: 살짝 띄우기
        transform.position = new Vector3(transform.position.x, dragY, transform.position.z);

        // 드래그 평면: y = dragY
        dragPlane = new Plane(Vector3.up, new Vector3(0f, dragY, 0f));

        // 현재 마우스 레이와 평면의 교점 계산
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (dragPlane.Raycast(ray, out var dist))
        {
            var hitPoint = ray.GetPoint(dist);
            grabOffset = transform.position - hitPoint; // 잡은 지점 유지용
            dragging = true;
        }
    }

    private void OnMouseDrag()
    {
        if (!dragging || cam == null) return;

        // 같은 평면과의 교점 계속 추적
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (dragPlane.Raycast(ray, out var dist))
        {
            var hitPoint = ray.GetPoint(dist);
            var target = hitPoint + grabOffset;

            // Y는 드래그 중 항상 dragY로 고정
            transform.position = new Vector3(target.x, dragY, target.z);
        }
    }

    private void OnMouseUp()
    {
        dragging = false;

        // 원래 높이로 복귀
        transform.position = new Vector3(transform.position.x, baseY, transform.position.z);
    }
}