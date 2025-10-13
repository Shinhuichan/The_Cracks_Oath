using UnityEngine;

public class UIFollowCamera : MonoBehaviour
{
    public Camera mainCamera;
    [Tooltip("앞/뒤 반전이 필요하면 체크")]
    public bool invertFacing = true;  // ← 뒤집혀 보이면 true, 정상이면 false

    void Start()
    {
        if (!mainCamera) mainCamera = Camera.main;
        if (!mainCamera) Debug.LogError("UIFollowCamera | mainCamera가 Null입니다.");
    }

    void LateUpdate()
    {
        if (!mainCamera) return;

        // 카메라 쪽을 바라보는 수평 방향만 사용 (Pitch/Roll 제거)
        Vector3 toCam = mainCamera.transform.position - transform.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 1e-6f) return;

        float yaw = Quaternion.LookRotation(toCam, Vector3.up).eulerAngles.y;
        if (invertFacing) yaw += 180f;  // 앞/뒤 보정

        // X, Z는 고정, Y만 덮어쓰기
        Vector3 e = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(e.x, yaw, e.z);
    }
}
