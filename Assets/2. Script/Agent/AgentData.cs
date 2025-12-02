using UnityEngine;
using System.Collections.Generic;
using GameCore;
using CustomInspector;

public enum Personality
{
    Static, Pragmatic, Aggressive, Defensive, Specialist, Erratic, Emotional
}

public enum ThreatLevel {Prey, Unstable, Variables, Challengers, Masters, Grandmasters, Absolute}

[System.Serializable]
public struct AgentStats
{
    [Range(0, 100), Tooltip("현재 상황에서 최적의 수(EV)를 찾아내는 지능. (S티어의 핵심)")]
    public int Judgment;       // 판단력
    
    [Range(0, 100), Tooltip("상대를 타격하고 킬각을 잡는 결정력. (메타 파괴력)")]
    public int Aggressive;   // 공격력
    
    [Range(0, 100), Tooltip("위기 상황에서 생존하고 피해를 최소화하는 능력.")]
    public int Defensive;     // 방어력
    
    [Range(0, 100), Tooltip("감정이나 확률에 휘둘리지 않고 꾸준히 제 성능을 내는 능력. (낮으면 트롤 가능성 높음)")]
    public int Stability;    // 안정성
    
    [Range(0, 100), Tooltip("Investment나 Sacrifice 스택을 쌓아 후반 밸류를 창출하는 운영 능력.")]
    public int Growth;         // 성장력
    
    [Range(0, 100), Tooltip("정보(Recon)를 선점하거나 상대의 흐름(Interrupt)을 끊어 판을 주도하는 능력.")]
    public int Control;         // 통제력
}

[CreateAssetMenu(fileName = "AgentData", menuName = "AI/Agent Data", order = 1)]
public class AgentData : ScriptableObject
{
    [Header("Identity")]
    public AgentList agentName;
    public string title;        // 예: "심리전의 여왕"
    [Preview] public Sprite icon;
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