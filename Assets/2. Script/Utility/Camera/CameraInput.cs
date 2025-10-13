using UnityEngine;
using CustomInspector;

public class CameraInput : MonoBehaviour
{
    [SerializeField, ReadOnly] private Camera cam;

    [Header("Rotation Setup")]
    [SerializeField] private bool useRotate = true;
    [SerializeField] private float rotateAngle = 15f;   // Q/E 회전 각도
    [SerializeField] private float moveSpeed = 5f;      // WASD 이동 속도
    [SerializeField] private float rotationSpeed = 300f; // 회전 속도 (degree/sec)
    
    [Header("Zoom Setup")]
    [SerializeField] private bool useZoom = true;
    [SerializeField] private float zoomSpeed = 10f;       // 줌 속도
    [SerializeField] private float minFov = 20f;          // 최소 FOV
    [SerializeField] private float maxFov = 80f;          // 최대 FOV

    private float targetYRotation = 0f;

    private void OnEnable()
    {
        cam = Camera.main;
        targetYRotation = transform.eulerAngles.y;
    }

    private void Update()
    {
        // 회전 입력
        if (useRotate)
        {
            if (Input.GetKeyDown(KeyCode.Q)) { targetYRotation += rotateAngle; }
            if (Input.GetKeyDown(KeyCode.E)) { targetYRotation -= rotateAngle; }

            float currentYRotation = transform.eulerAngles.y;
            float newYRotation = Mathf.MoveTowardsAngle(currentYRotation, targetYRotation, rotationSpeed * Time.deltaTime);
            Vector3 currentEuler = transform.eulerAngles;
            transform.eulerAngles = new Vector3(currentEuler.x, newYRotation, currentEuler.z);
        }

        // 이동 입력
        Vector3 moveDir = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) moveDir += cam.transform.forward;
        if (Input.GetKey(KeyCode.S)) moveDir -= cam.transform.forward;
        if (Input.GetKey(KeyCode.D)) moveDir += cam.transform.right;
        if (Input.GetKey(KeyCode.A)) moveDir -= cam.transform.right;
        moveDir.y = 0f;
        transform.position += moveDir.normalized * moveSpeed * Time.deltaTime;

        // 줌 입력
        if (useZoom)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                cam.fieldOfView -= scroll * zoomSpeed;
                cam.fieldOfView = Mathf.Clamp(cam.fieldOfView, minFov, maxFov);
            }
        }
    }
}