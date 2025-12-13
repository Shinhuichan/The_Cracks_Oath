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
[System.Serializable] public struct InputTool { public string inputName; public TMP_InputField input; }
[System.Serializable] public struct SliderTool { public string sliderName; public Slider slider; }
[System.Serializable] public struct ObjectTool { public string objectName; public GameObject obj; }

[System.Serializable]
public struct UITool
{
    public string uiName; 
    public List<TextTool> text;
    public List<ImageTool> image;
    public List<ButtonTool> button;
    public List<InputTool> input;   
    public List<ObjectTool> obj;    
    public List<SliderTool> slider; 
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

    [Header("Toast Settings")]
    [SerializeField] private GameObject toastPrefab; // 🍞 프리팹 연결 필요 (Text가 포함된 패널)
    [SerializeField] private Transform toastParent;  // 토스트가 생성될 부모 Transform

    private class GroupMaps
    {
        public readonly Dictionary<string, TextMeshProUGUI> texts = new();
        public readonly Dictionary<string, Image> images = new();
        public readonly Dictionary<string, Button> buttons = new();
        public readonly Dictionary<string, TMP_InputField> inputs = new(); 
        public readonly Dictionary<string, GameObject> objects = new();    
        public readonly Dictionary<string, Slider> sliders = new(); 
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

            if (group.text != null) foreach (var t in group.text) if (t.text) maps.texts[t.textName] = t.text;
            if (group.image != null) foreach (var im in group.image) if (im.image) maps.images[im.imageName] = im.image;
            if (group.button != null) foreach (var bt in group.button) if (bt.button) maps.buttons[bt.buttonName] = bt.button;
            if (group.input != null) foreach (var ipt in group.input) if (!string.IsNullOrEmpty(ipt.inputName) && ipt.input) maps.inputs[ipt.inputName] = ipt.input;
            if (group.obj != null) foreach (var o in group.obj) if (!string.IsNullOrEmpty(o.objectName) && o.obj) maps.objects[o.objectName] = o.obj;
            if (group.slider != null) foreach (var s in group.slider) if (!string.IsNullOrEmpty(s.sliderName) && s.slider) maps.sliders[s.sliderName] = s.slider;
        }
    }

    private void Start()
    {
        StartCoroutine(SecondLoop());
        StartCoroutine(FrameLoop());
    }

    private IEnumerator SecondLoop() { var wait = new WaitForSeconds(tickInterval); while (true) { OnTick?.Invoke(); yield return wait; } }
    private IEnumerator FrameLoop() { while (true) { OnFrame?.Invoke(Time.unscaledDeltaTime); yield return null; } }

    public bool HasGroup(string group) => _groups.ContainsKey(group);

    public bool TrySetActive(string group, string name, bool active)
    {
        if (_groups.TryGetValue(group, out var g))
        {
            if (g.objects.TryGetValue(name, out var obj)) { obj.SetActive(active); return true; }
            if (g.texts.TryGetValue(name, out var t)) { t.gameObject.SetActive(active); return true; }
            if (g.images.TryGetValue(name, out var i)) { i.gameObject.SetActive(active); return true; }
            if (g.buttons.TryGetValue(name, out var b)) { b.gameObject.SetActive(active); return true; }
            if (g.inputs.TryGetValue(name, out var ipt)) { ipt.gameObject.SetActive(active); return true; }
        }
        return false;
    }

    // ===== Slider API =====
    public bool TrySetSliderValue(string group, string name, float value) { if (_groups.TryGetValue(group, out var g) && g.sliders.TryGetValue(name, out var s)) { s.value = value; return true; } return false; }
    public bool TrySetSliderMinMax(string group, string name, float min, float max) { if (_groups.TryGetValue(group, out var g) && g.sliders.TryGetValue(name, out var s)) { s.minValue = min; s.maxValue = max; return true; } return false; }
    public bool TrySetSliderOnValueChanged(string group, string name, UnityAction<float> action) { if (_groups.TryGetValue(group, out var g) && g.sliders.TryGetValue(name, out var s)) { s.onValueChanged.RemoveAllListeners(); if (action != null) s.onValueChanged.AddListener(action); return true; } return false; }
    public float GetSliderValue(string group, string name) { if (_groups.TryGetValue(group, out var g) && g.sliders.TryGetValue(name, out var s)) return s.value; return 0f; }

    // ===== Text API =====
    public bool TrySetText(string group, string name, string value) { if (_groups.TryGetValue(group, out var g) && g.texts.TryGetValue(name, out var t)) { t.text = value; return true; } return false; }
    public bool TrySetTextColor(string group, string name, Color color) { if (_groups.TryGetValue(group, out var g) && g.texts.TryGetValue(name, out var t)) { t.color = color; return true; } return false; }

    // 🌟 [추가됨] 텍스트 가져오기 (컴파일 에러 해결)
    public string GetText(string group, string name)
    {
        if (_groups.TryGetValue(group, out var g) && g.texts.TryGetValue(name, out var t))
        {
            return t.text;
        }
        return "";
    }

    // ===== Image/Button/Input API =====
    public bool TrySetSprite(string group, string name, Sprite sprite, bool nativeSize = false) { if (_groups.TryGetValue(group, out var g) && g.images.TryGetValue(name, out var i)) { i.sprite = sprite; if (nativeSize) i.SetNativeSize(); return true; } return false; }
    public bool TrySetOnClick(string group, string name, UnityAction action) { if (_groups.TryGetValue(group, out var g) && g.buttons.TryGetValue(name, out var b)) { b.onClick.RemoveAllListeners(); if (action != null) b.onClick.AddListener(action); return true; } return false; }
    public bool TrySetInteractable(string group, string name, bool interactable) { if (_groups.TryGetValue(group, out var g) && g.buttons.TryGetValue(name, out var b)) { b.interactable = interactable; return true; } return false; }
    public string GetInputValue(string group, string name) { if (_groups.TryGetValue(group, out var g) && g.inputs.TryGetValue(name, out var ipt)) return ipt.text; return ""; }
    public long GetInputValueInt(string group, string name) { string val = GetInputValue(group, name); if (long.TryParse(val, out long result)) return result; return 0; }
    public bool TrySetInputValue(string group, string name, string value) { if (_groups.TryGetValue(group, out var g) && g.inputs.TryGetValue(name, out var ipt)) { ipt.text = value; return true; } return false; }
    public bool TrySetInputOnEndEdit(string group, string name, UnityAction<string> action) { if (_groups.TryGetValue(group, out var g) && g.inputs.TryGetValue(name, out var ipt)) { ipt.onEndEdit.RemoveAllListeners(); if (action != null) ipt.onEndEdit.AddListener(action); return true; } return false; }

    // ✨ [신규] 토스트 메시지 출력 기능
    public void ShowToast(string message, float duration = 2.0f)
    {
        if (toastPrefab == null || toastParent == null) return;

        GameObject toast = Instantiate(toastPrefab, toastParent);
        // 🛠️ [추가] 생성되자마자 부모(Canvas) 내에서 가장 맨 앞으로 순서 변경
        toast.transform.SetAsLastSibling();
        TextMeshProUGUI txt = toast.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) txt.text = message;

        StartCoroutine(FadeOutAndDestroy(toast, duration));
    }

    private IEnumerator FadeOutAndDestroy(GameObject target, float duration)
    {
        CanvasGroup cg = target.GetComponent<CanvasGroup>();
        if (cg == null) cg = target.AddComponent<CanvasGroup>();

        // 1. 잠시 대기
        yield return new WaitForSeconds(duration * 0.7f);

        // 2. 페이드 아웃
        float fadeTime = duration * 0.3f;
        float start = 1f;
        float elapsed = 0f;

        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(start, 0f, elapsed / fadeTime);
            yield return null;
        }

        Destroy(target);
    }

    // ✨ [신규] CanvasGroup을 이용한 UI 페이드 제어 (부드러운 전환)
    public void FadeUI(string group, string name, bool fadeIn, float time = 0.5f)
    {
        if (_groups.TryGetValue(group, out var g) && g.objects.TryGetValue(name, out var obj))
        {
            StartCoroutine(FadeRoutine(obj, fadeIn, time));
        }
    }

    private IEnumerator FadeRoutine(GameObject obj, bool fadeIn, float time)
    {
        CanvasGroup cg = obj.GetComponent<CanvasGroup>();
        if (cg == null) cg = obj.AddComponent<CanvasGroup>();

        obj.SetActive(true);
        float start = cg.alpha;
        float end = fadeIn ? 1f : 0f;
        float elapsed = 0f;

        while(elapsed < time)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(start, end, elapsed / time);
            yield return null;
        }
        
        cg.alpha = end;
        if (!fadeIn) obj.SetActive(false); // 페이드 아웃 끝나면 비활성화
    }

    // 💡 [신규] 특정 UI에 툴팁 설정/변경 기능
    public void TrySetTooltip(string group, string name, string content, string header = "")
    {
        GameObject target = null;

        // 1. 해당 그룹 찾기
        if (_groups.TryGetValue(group, out var g))
        {
            // 2. 오브젝트, 버튼, 텍스트, 이미지 순으로 검색하여 대상 찾기
            if (g.objects.TryGetValue(name, out var obj)) target = obj;
            else if (g.buttons.TryGetValue(name, out var btn)) target = btn.gameObject;
            else if (g.texts.TryGetValue(name, out var txt)) target = txt.gameObject;
            else if (g.images.TryGetValue(name, out var img)) target = img.gameObject;
        }

        // 3. TooltipTrigger 컴포넌트 제어
        if (target != null)
        {
            TooltipTrigger trigger = target.GetComponent<TooltipTrigger>();
            if (trigger == null) trigger = target.AddComponent<TooltipTrigger>(); // 없으면 붙임

            trigger.content = content;
            trigger.header = header;
        }
    }
}