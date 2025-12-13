using UnityEngine;
using TMPro;
using System.Collections;

public class FloatingTextManager : SingletonBehaviour<FloatingTextManager>
{
    protected override bool IsDontDestroy() => false;

    [Header("Settings")]
    public GameObject textPrefab; // TextMeshProUGUI가 포함된 프리팹
    public Transform floatingPointTransform;
    public float floatSpeed = 50f;
    public float duration = 1.0f;

    // 💰 돈 변화 연출 (Color: Green=이득, Red=손실)
    public void ShowMoneyPopup(Vector3 position, long amount)
    {
        string text = amount > 0 ? $"+{NumberUtils.ToCurrencyString(amount)}원" : $"{NumberUtils.ToCurrencyString(amount)}원";
        Color color = amount > 0 ? Color.red : Color.blue; // 주식은 빨강이 상승(한국 기준)
        ShowText(position, text, color);
    }

    // 🌟 [수정] 기본 fontSize를 36 -> 0 으로 변경 (0이면 프리팹 설정 유지)
    public void ShowText(Vector3 position, string content, Color color, int fontSize = 0)
    {
        if (textPrefab == null) return;

        GameObject obj = Instantiate(textPrefab, floatingPointTransform);
        obj.transform.position = position; 

        // 생성 직후 최상단으로 이동 (가림 방지)
        obj.transform.SetAsLastSibling();

        // 유연성을 위해 GetComponentsInChildren 사용 (부모/자식 어디든 찾음)
        TextMeshProUGUI tmp = obj.GetComponentInChildren<TextMeshProUGUI>();
        
        if (tmp != null)
        {
            tmp.text = content;
            tmp.color = color;
            
            // 🌟 [핵심 수정] fontSize가 0보다 클 때만 코드로 덮어씌움
            // (0이면 프리팹에 설정된 폰트 크기를 그대로 사용)
            if (fontSize > 0)
            {
                tmp.fontSize = fontSize;
            }

            StartCoroutine(AnimateText(obj, tmp));
        }
        else
        {
            Destroy(obj);
        }
    }

    IEnumerator AnimateText(GameObject obj, TextMeshProUGUI tmp)
    {
        float elapsed = 0f;
        Vector3 startPos = obj.transform.position;
        Color startColor = tmp.color;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime; // 일시정지 중에도 보여주려면 unscaled
            
            // 위로 이동
            obj.transform.position = startPos + Vector3.up * (floatSpeed * elapsed);
            
            // 투명도 감소
            float alpha = Mathf.Lerp(1f, 0f, elapsed / duration);
            tmp.color = new Color(startColor.r, startColor.g, startColor.b, alpha);

            yield return null;
        }

        Destroy(obj); // 최적화를 위해선 ObjectPool 사용 권장
    }
}