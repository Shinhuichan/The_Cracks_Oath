using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;

#region Data Models
[System.Serializable] public struct TextTool { public string textName; public TextMeshProUGUI text; }
[System.Serializable] public struct ImageTool { public string imageName; public Image image; }
[System.Serializable] public struct ButtonTool { public string buttonName; public Button button; }

// ➕ [추가] 입력창 (InputField)
[System.Serializable] public struct InputTool { public string inputName; public TMP_InputField input; }

// ➕ [추가] 통짜 오브젝트 (패널, 부모 객체 등)
[System.Serializable] public struct ObjectTool { public string objectName; public GameObject obj; }

[System.Serializable]
public struct UITool
{
    public string uiName; // 그룹 이름
    public List<TextTool> text;
    public List<ImageTool> image;
    public List<ButtonTool> button;
    public List<InputTool> input;   // ➕ 추가됨
    public List<ObjectTool> obj;    // ➕ 추가됨
}
#endregion

public class UIManager : SingletonBehaviour<UIManager>
{
    protected override bool IsDontDestroy() => true;

    [Header("Register UI in Inspector")]
    [SerializeField] private List<UITool> ui;

    [Header("Loop Settings")]
    [SerializeField] private float tickInterval = 1f;

    public event Action OnTick;
    public event Action<float> OnFrame;

    private class GroupMaps
    {
        public readonly Dictionary<string, TextMeshProUGUI> texts = new();
        public readonly Dictionary<string, Image> images = new();
        public readonly Dictionary<string, Button> buttons = new();
        public readonly Dictionary<string, TMP_InputField> inputs = new(); // ➕
        public readonly Dictionary<string, GameObject> objects = new();    // ➕
    }
    private readonly Dictionary<string, GroupMaps> _groups = new();

    protected override void Awake()
    {
        base.Awake();
        _groups.Clear();

        if (ui == null) return;

        foreach (var group in ui)
        {
            if (string.IsNullOrEmpty(group.uiName) || _groups.ContainsKey(group.uiName)) continue;

            var maps = new GroupMaps();
            _groups[group.uiName] = maps;

            // 기존 매핑
            if (group.text != null) foreach (var t in group.text) if (t.text) maps.texts[t.textName] = t.text;
            if (group.image != null) foreach (var im in group.image) if (im.image) maps.images[im.imageName] = im.image;
            if (group.button != null) foreach (var bt in group.button) if (bt.button) maps.buttons[bt.buttonName] = bt.button;

            // ➕ [추가] InputField 매핑
            if (group.input != null)
                foreach (var ipt in group.input)
                    if (!string.IsNullOrEmpty(ipt.inputName) && ipt.input) maps.inputs[ipt.inputName] = ipt.input;

            // ➕ [추가] GameObject 매핑
            if (group.obj != null)
                foreach (var o in group.obj)
                    if (!string.IsNullOrEmpty(o.objectName) && o.obj) maps.objects[o.objectName] = o.obj;
        }
    }

    private void Start()
    {
        StartCoroutine(SecondLoop());
        StartCoroutine(FrameLoop());
    }

    private IEnumerator SecondLoop()
    {
        var wait = new WaitForSeconds(tickInterval);
        while (true) { OnTick?.Invoke(); yield return wait; }
    }
    private IEnumerator FrameLoop()
    {
        while (true) { OnFrame?.Invoke(Time.unscaledDeltaTime); yield return null; }
    }

    // ===== Common Methods =====
    public bool HasGroup(string group) => _groups.ContainsKey(group);

    // ➕ [수정] 통합 SetActive: Object -> Text -> Image -> Button -> Input 순으로 찾아서 끄고 킴
    public bool TrySetActive(string group, string name, bool active)
    {
        if (_groups.TryGetValue(group, out var g))
        {
            // 1순위: 통짜 오브젝트 리스트에서 확인
            if (g.objects.TryGetValue(name, out var obj)) { obj.SetActive(active); return true; }
            
            // 2순위: 컴포넌트들 확인
            if (g.texts.TryGetValue(name, out var t)) { t.gameObject.SetActive(active); return true; }
            if (g.images.TryGetValue(name, out var i)) { i.gameObject.SetActive(active); return true; }
            if (g.buttons.TryGetValue(name, out var b)) { b.gameObject.SetActive(active); return true; }
            if (g.inputs.TryGetValue(name, out var ipt)) { ipt.gameObject.SetActive(active); return true; }
        }
        Debug.LogWarning($"[UIManager] Target not found for SetActive: {group}.{name}");
        return false;
    }

    // ===== Text API =====
    public bool TrySetText(string group, string name, string value)
    {
        if (_groups.TryGetValue(group, out var g) && g.texts.TryGetValue(name, out var t))
        {
            // 한글 조사 처리 기능이 있다면 사용, 없으면 그냥 대입
            t.text = value; // value.CorrectJosa() 사용 시 복구 필요
            return true;
        }
        return false;
    }
    public bool TrySetTextColor(string group, string name, Color color)
    {
        if (_groups.TryGetValue(group, out var g) && g.texts.TryGetValue(name, out var t)) { t.color = color; return true; }
        return false;
    }

    // ===== Image API =====
    public bool TrySetSprite(string group, string name, Sprite sprite, bool nativeSize = false)
    {
        if (_groups.TryGetValue(group, out var g) && g.images.TryGetValue(name, out var i))
        {
            i.sprite = sprite;
            if (nativeSize) i.SetNativeSize();
            return true;
        }
        return false;
    }

    // ===== Button API =====
    public bool TrySetOnClick(string group, string name, UnityAction action)
    {
        if (_groups.TryGetValue(group, out var g) && g.buttons.TryGetValue(name, out var b))
        {
            b.onClick.RemoveAllListeners();
            if (action != null) b.onClick.AddListener(action);
            return true;
        }
        return false;
    }

    // ➕ [추가] 버튼 활성화/비활성화 (Interactable) 제어
    public bool TrySetInteractable(string group, string name, bool interactable)
    {
        if (_groups.TryGetValue(group, out var g) && g.buttons.TryGetValue(name, out var b))
        {
            b.interactable = interactable;
            return true;
        }
        return false;
    }

    // ===== ➕ [추가] InputField API =====
    
    // 입력된 텍스트 가져오기 (문자열 반환, 실패 시 빈 문자열)
    public string GetInputValue(string group, string name)
    {
        if (_groups.TryGetValue(group, out var g) && g.inputs.TryGetValue(name, out var ipt))
            return ipt.text;
        return "";
    }

    // 입력된 텍스트를 숫자로 가져오기 (실패 시 0)
    public int GetInputValueInt(string group, string name)
    {
        string val = GetInputValue(group, name);
        if (int.TryParse(val, out int result)) return result;
        return 0;
    }

    // 입력창 텍스트 강제 설정
    public bool TrySetInputValue(string group, string name, string value)
    {
        if (_groups.TryGetValue(group, out var g) && g.inputs.TryGetValue(name, out var ipt))
        {
            ipt.text = value;
            return true;
        }
        return false;
    }
}