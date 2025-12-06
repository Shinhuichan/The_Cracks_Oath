using UnityEngine;
using CustomInspector;

[CreateAssetMenu(fileName = "New Stock Data", menuName = "Stock Game/Stock Data", order = 1)]
public class StockData : ScriptableObject
{
    [Header("기본 정보")]
    public string stockName;        
    public string symbol;           
    [TextArea(5, 10)] public string description;      
    public Sprite icon;             

    [Header("시장 데이터")]
    public int startPrice;          
    [Range(0.01f, 0.2f)] public float volatility = 0.05f; 
    public StockSector sector;      

    [Header("수량 설정")]
    public int totalShares = 10000; 

    [Header("이벤트 설정")]
    [Range(0.1f, 3.0f)] public float eventPotential = 1.0f;
    [Range(1f, 10f)] public float eventWeight = 1.0f;

    // ➕ [신규] 주당 배당금 (턴마다 지급)
    [Header("배당금 설정")]
    [Tooltip("1주당 매 턴 지급되는 배당금 (0이면 무배당)")]
    public int dividendPerShare = 0;
}

public enum StockSector
{
    IT,
    Bio,
    Automotive,
    Food,
    Energy,
    Game
}