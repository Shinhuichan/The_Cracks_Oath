using UnityEngine;
using System.Collections.Generic;
using GameCore;
using CustomInspector;

public enum Personality
{
    Static, Pragmatic, Aggressive, Defensive, Specialist, Erratic, Emotional
}

public enum ThreatLevel {Prey, Unstable, Gamblers, Challengers, Masters, Grandmasters, Absolute}

[System.Serializable]
public struct AgentStats
{
    [Range(0, 100), Tooltip("공격성, 킬각 결정력")]
    public int Lethality;       // 살상력
    
    [Range(0, 100), Tooltip("방어 능력, 위기 관리")]
    public int Survivability;   // 생존력
    
    [Range(0, 100), Tooltip("기댓값(EV) 계산 및 수 싸움")]
    public int Calculation;     // 연산력
    
    [Range(0, 100), Tooltip("데이터 학습 및 유연함")]
    public int Adaptability;    // 적응력
    
    [Range(0, 100), Tooltip("상대 패턴 파악 및 심리전")]
    public int Insight;         // 통찰력
    
    [Range(0, 100), Tooltip("변수 창출 및 종잡을 수 없음")]
    public int Unpredictability;// 의외성
}

[CreateAssetMenu(fileName = "AgentData", menuName = "AI/Agent Data", order = 1)]
public class AgentData : ScriptableObject
{
    [Header("Identity")]
    [ReadOnly] public AgentList agentName;
    public string title;        // 예: "심리전의 여왕"
    [TextArea(2, 5)] public string description; // 소개 문구
    public ThreatLevel threatLevel; // 종합 위험도

    [Header("Core Stats (0-100)")]
    public AgentStats stats;
    
    [Header("Learning & Personality")]
    public Personality personality;
    public Dictionary<CardType, float> weights;

    [Header("Records")]
    [ReadOnly] public double elo = 1500f;
    [ReadOnly] public List<AgentRecord> records;

    // 가중치 초기화 (기존 기능 유지)
    public void InitializeWeights()
    {
        weights = new Dictionary<CardType, float>();
        foreach (CardType card in System.Enum.GetValues(typeof(CardType)))
        {
            if (card == CardType.None) continue;
            weights[card] = 1.0f;
        }
    }
}

[System.Serializable]
public struct AgentRecord
{   
    [ReadOnly] public AgentList verseAgent;
    [ReadOnly] public int matchCount;
    [ReadOnly] public int winCount;
    [ReadOnly] public int loseCount;
    [ReadOnly] public int drawCount;
}