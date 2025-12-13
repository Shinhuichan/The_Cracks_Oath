using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TimeManager : SingletonBehaviour<TimeManager>
{
    protected override bool IsDontDestroy() => false;

    [Header("UI References")]
    public TextMeshProUGUI txtSpeed;
    public Image pauseIcon;
    public Image playIcon;

    private float previousTimeScale = 1f;
    private int speedIndex = 0;
    private readonly float[] speeds = { 1f, 2f, 4f, 10f }; // 배속 단계

    void Start()
    {
        UpdateUI();
    }

    void Update()
    {
        // 단축키 입력 (Space: 일시정지 토글, Tab: 배속 변경)
        if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
        if (Input.GetKeyDown(KeyCode.Tab)) CycleSpeed();
        
        // 숫자키 1, 2, 3으로 배속 직접 지정
        if (Input.GetKeyDown(KeyCode.Alpha1)) SetSpeed(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SetSpeed(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SetSpeed(2);
    }

    public void TogglePause()
    {
        if (Time.timeScale > 0)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0;
        }
        else
        {
            Time.timeScale = previousTimeScale;
        }
        UpdateUI();
    }

    public void CycleSpeed()
    {
        if (Time.timeScale == 0) Time.timeScale = previousTimeScale; // 일시정지 해제

        speedIndex = (speedIndex + 1) % speeds.Length;
        Time.timeScale = speeds[speedIndex];
        previousTimeScale = Time.timeScale;
        UpdateUI();
    }

    public void SetSpeed(int index)
    {
        if (index < 0 || index >= speeds.Length) return;
        speedIndex = index;
        Time.timeScale = speeds[index];
        previousTimeScale = Time.timeScale;
        UpdateUI();
    }

    void UpdateUI()
    {
        if (txtSpeed) txtSpeed.text = Time.timeScale == 0 ? "PAUSED" : $"x{Time.timeScale}";
        
        // 아이콘 교체 (선택 사항)
        if (pauseIcon && playIcon)
        {
            bool isPaused = Time.timeScale == 0;
            pauseIcon.enabled = !isPaused;
            playIcon.enabled = isPaused;
        }
    }
}