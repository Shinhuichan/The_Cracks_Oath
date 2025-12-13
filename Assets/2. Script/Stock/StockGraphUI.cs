using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class StockGraphUI : MonoBehaviour
{
    [Header("Settings")]
    public RectTransform graphContainer;
    
    [Header("Candle Colors")]
    public Color upColor = new Color(1f, 0.2f, 0.2f); // 양봉 (빨강)
    public Color downColor = new Color(0.2f, 0.2f, 1f); // 음봉 (파랑)
    public Color wickColor = Color.white; // 꼬리 색상 (흰색 또는 회색)

    [Header("Size Settings")]
    public float bodyWidthRatio = 0.8f; // 캔들 몸통 너비 비율 (간격 대비)
    public float wickThickness = 2f;    // 꼬리 두께

    private List<GameObject> graphObjects = new List<GameObject>();

    // 🕯️ [수정] 매개변수 타입 변경: StockMarketManager.StockCandle -> StockCandle
    public void ShowCandleGraph(List<StockCandle> candles)
    {
        // 1. 기존 그래프 삭제
        foreach (GameObject obj in graphObjects) Destroy(obj);
        graphObjects.Clear();

        if (candles == null || candles.Count == 0) return;

        // 2. Y축 범위(최저가~최고가) 계산
        float maxVal = float.MinValue;
        float minVal = float.MaxValue;
        
        foreach (var candle in candles)
        {
            if (candle.high > maxVal) maxVal = candle.high;
            if (candle.low < minVal) minVal = candle.low;
        }

        // 범위 보정
        float diff = maxVal - minVal;
        if (diff <= 0) diff = 1f;
        maxVal += diff * 0.1f;
        minVal -= diff * 0.1f;

        float graphWidth = graphContainer.sizeDelta.x;
        float graphHeight = graphContainer.sizeDelta.y;
        
        // X축 간격 계산
        float xSize = graphWidth / Mathf.Max(candles.Count, 1); 
        float bodyWidth = xSize * bodyWidthRatio;

        // 3. 캔들 그리기
        for (int i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];
            float xPosition = i * xSize + (xSize / 2f); 

            float GetY(float price) => ((price - minVal) / (maxVal - minVal)) * graphHeight;

            float yHigh = GetY(candle.high);
            float yLow = GetY(candle.low);
            float yOpen = GetY(candle.open);
            float yClose = GetY(candle.close);

            bool isUp = candle.close >= candle.open;
            Color bodyColor = isUp ? upColor : downColor;

            // A. 꼬리 (Wick)
            GameObject wickObj = CreateRect("Wick", wickColor);
            RectTransform wickRect = wickObj.GetComponent<RectTransform>();
            wickRect.sizeDelta = new Vector2(wickThickness, Mathf.Max(1f, yHigh - yLow)); // 최소 높이 보장
            wickRect.anchoredPosition = new Vector2(xPosition, yLow + (yHigh - yLow) / 2f);
            graphObjects.Add(wickObj);

            // B. 몸통 (Body)
            GameObject bodyObj = CreateRect("Body", bodyColor);
            RectTransform bodyRect = bodyObj.GetComponent<RectTransform>();
            
            float bodyHeight = Mathf.Abs(yClose - yOpen);
            if (bodyHeight < 1f) bodyHeight = 1f; 
            
            float bodyCenterY = Mathf.Min(yOpen, yClose) + bodyHeight / 2f;

            bodyRect.sizeDelta = new Vector2(bodyWidth, bodyHeight);
            bodyRect.anchoredPosition = new Vector2(xPosition, bodyCenterY);
            graphObjects.Add(bodyObj);
        }
    }

    // 사각형(Image) 생성 헬퍼
    private GameObject CreateRect(string name, Color color)
    {
        GameObject gameObject = new GameObject(name, typeof(Image));
        gameObject.transform.SetParent(graphContainer, false);
        Image image = gameObject.GetComponent<Image>();
        image.color = color;
        
        RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0, 0);
        rectTransform.anchorMax = new Vector2(0, 0);
        rectTransform.pivot = new Vector2(0.5f, 0.5f); // 중심 기준
        
        return gameObject;
    }
}