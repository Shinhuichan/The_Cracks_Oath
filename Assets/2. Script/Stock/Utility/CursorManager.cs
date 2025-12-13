using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct CursorInfo
{
    public string cursorName;   // 호출할 이름 (예: "Hand", "Normal")
    public Texture2D texture;   // 커서 이미지
    public Vector2 hotspot;     // 클릭 지점 (이미지의 좌상단 기준 픽셀 좌표)
}

public class CursorManager : SingletonBehaviour<CursorManager>
{
    protected override bool IsDontDestroy() => true;

    [Header("Settings")]
    public List<CursorInfo> cursorList;
    [Tooltip("게임 시작 시 기본으로 설정될 커서 이름")]
    public string defaultCursorName = "Normal";

    private Dictionary<string, CursorInfo> _cursorMap = new Dictionary<string, CursorInfo>();

    protected override void Awake()
    {
        base.Awake();
        InitializeCursorMap();
    }

    private void Start()
    {
        SetCursor(defaultCursorName);
    }

    private void InitializeCursorMap()
    {
        _cursorMap.Clear();
        foreach (var info in cursorList)
        {
            if (!_cursorMap.ContainsKey(info.cursorName))
            {
                _cursorMap.Add(info.cursorName, info);
            }
        }
    }

    // 🖱️ 커서 변경 함수
    public void SetCursor(string cursorName)
    {
        if (_cursorMap.TryGetValue(cursorName, out CursorInfo info))
        {
            // CursorMode.Auto: 하드웨어 커서 사용 (반응 빠름)
            // CursorMode.ForceSoftware: 소프트웨어 렌더링 (이미지가 크거나 특수효과 필요시)
            Cursor.SetCursor(info.texture, info.hotspot, CursorMode.Auto);
        }
        else
        {
            // 찾는 커서가 없으면 시스템 기본 커서로 초기화
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            // Debug.LogWarning($"커서 '{cursorName}'를 찾을 수 없습니다.");
        }
    }

    // 기본 커서로 복귀
    public void SetDefault()
    {
        SetCursor(defaultCursorName);
    }
}