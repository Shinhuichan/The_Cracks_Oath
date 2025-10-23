using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;

#region Data Models
[System.Serializable] public struct TextTool { public string textName;  public TextMeshProUGUI text; }
[System.Serializable] public struct ImageTool { public string imageName; public Image image; }
[System.Serializable] public struct ButtonTool{ public string buttonName; public Button button; }

[System.Serializable]
public struct UITool
{
    public string uiName; // 그룹 이름(중복 불가)
    public List<TextTool>   text;
    public List<ImageTool>  image;
    public List<ButtonTool> button;
}
#endregion

public class UIManager : SingletonBehaviour<UIManager>
{
    protected override bool IsDontDestroy() => true;

    [Header("Register UI in Inspector (그룹 단위 등록)")]
    [SerializeField] private List<UITool> ui;

    [Header("중앙 갱신 루프 설정")]
    [SerializeField] private float tickInterval = 1f;

    // 중앙 이벤트
    public event Action OnTick;                 // 주기적(기본 1초)
    public event Action<float> OnFrame;         // 매 프레임(unscaledDeltaTime)

    // 그룹 → (요소 이름 → 컴포넌트)
    private class GroupMaps
    {
        public readonly Dictionary<string, TextMeshProUGUI> texts  = new();
        public readonly Dictionary<string, Image>           images = new();
        public readonly Dictionary<string, Button>          buttons= new();
    }
    private readonly Dictionary<string, GroupMaps> _groups = new();

    protected override void Awake()
    {
        base.Awake();
        _groups.Clear();

        if (ui == null) return;

        foreach (var group in ui)
        {
            if (string.IsNullOrEmpty(group.uiName))
            {
                Debug.LogWarning("[UIManager] uiName이 비었습니다. 이 그룹은 스킵됩니다.");
                continue;
            }
            if (_groups.ContainsKey(group.uiName))
            {
                Debug.LogWarning($"[UIManager] uiName '{group.uiName}' 이(가) 중복됩니다. 이 그룹은 스킵됩니다.");
                continue;
            }

            var maps = new GroupMaps();
            _groups[group.uiName] = maps;

            if (group.text != null)
                foreach (var t in group.text)
                    if (!string.IsNullOrEmpty(t.textName) && t.text) maps.texts[t.textName] = t.text;

            if (group.image != null)
                foreach (var im in group.image)
                    if (!string.IsNullOrEmpty(im.imageName) && im.image) maps.images[im.imageName] = im.image;

            if (group.button != null)
                foreach (var bt in group.button)
                    if (!string.IsNullOrEmpty(bt.buttonName) && bt.button) maps.buttons[bt.buttonName] = bt.button;
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
        while (true)
        {
            OnTick?.Invoke();
            yield return wait;
        }
    }

    private IEnumerator FrameLoop()
    {
        while (true)
        {
            OnFrame?.Invoke(Time.unscaledDeltaTime);
            yield return null;
        }
    }

    // ===== 그룹 존재 여부 =====
    public bool HasGroup(string group) => _groups.ContainsKey(group);

    // ===== Text API =====
    public TextMeshProUGUI GetText(string group, string name)
        => _groups.TryGetValue(group, out var g) && g.texts.TryGetValue(name, out var t) ? t : null;
    public bool TrySetText(string group, string name, string value)
    {
        var tmp = GetText(group, name);
        if (tmp != null) { tmp.text = value; return true; }
        Debug.LogWarning($"[UIManager] Text not found: {group}.{name}");
        return false;
    }
    public bool TrySetTextColor(string group, string name, Color color)
    {
        var tmp = GetText(group, name);
        if (tmp != null) { tmp.color = color; return true; }
        Debug.LogWarning($"[UIManager] Text not found for color: {group}.{name}");
        return false;
    }

    // ===== Image API =====
    public Image GetImage(string group, string name)
        => _groups.TryGetValue(group, out var g) && g.images.TryGetValue(name, out var i) ? i : null;
    public bool TrySetSprite(string group, string name, Sprite sprite, bool preserveNativeSize = false)
    {
        var img = GetImage(group, name);
        if (img != null) { img.sprite = sprite; if (preserveNativeSize) img.SetNativeSize(); return true; }
        Debug.LogWarning($"[UIManager] Image not found: {group}.{name}");
        return false;
    }
    public bool TrySetImageColor(string group, string name, Color color)
    {
        var img = GetImage(group, name);
        if (img != null) { img.color = color; return true; }
        Debug.LogWarning($"[UIManager] Image not found for color: {group}.{name}");
        return false;
    }

    // ===== Button API =====
    public Button GetButton(string group, string name)
        => _groups.TryGetValue(group, out var g) && g.buttons.TryGetValue(name, out var b) ? b : null;
    public bool TrySetInteractable(string group, string name, bool interactable)
    {
        var b = GetButton(group, name);
        if (b != null) { b.interactable = interactable; return true; }
        Debug.LogWarning($"[UIManager] Button not found: {group}.{name}");
        return false;
    }
    public bool TrySetOnClick(string group, string name, UnityAction action, bool clear = true)
    {
        var b = GetButton(group, name);
        if (b != null)
        {
            if (clear) b.onClick.RemoveAllListeners();
            if (action != null) b.onClick.AddListener(action);
            return true;
        }
        Debug.LogWarning($"[UIManager] Button not found for onClick: {group}.{name}");
        return false;
    }

    // ===== Common Active Toggle =====
    public bool TrySetActive(string group, string name, bool active)
    {
        if (_groups.TryGetValue(group, out var g))
        {
            if (g.texts.TryGetValue(name, out var t)) { t.gameObject.SetActive(active); return true; }
            if (g.images.TryGetValue(name, out var i)) { i.gameObject.SetActive(active); return true; }
            if (g.buttons.TryGetValue(name, out var b)) { b.gameObject.SetActive(active); return true; }
        }
        Debug.LogWarning($"[UIManager] UI element not found for active toggle: {group}.{name}");
        return false;
    }
}