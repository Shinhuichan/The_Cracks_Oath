using UnityEngine;
using System.Collections.Generic;
using GameCore;  // AgentList
using CustomInspector;

public enum Personality
{
    냉소적인,     // 작은 변화만
    과몰입한,     // 큰 변화만
    제멋대로,     // 결과 무관 랜덤
    감정적인,
    완벽주의,
    불안정한,
    실리주의,
    도전적인,
    가학적인
}

[CreateAssetMenu(fileName = "AgentData", menuName = "AI/Agent Data", order = 1)]
public class AgentData : ScriptableObject
{
    [ReadOnly] public AgentList agentName;
    [ReadOnly] public List<AgentRecord> records;
    [ReadOnly] public double elo = 1500f;
    [ReadOnly] [Range(0f, 100f)] public float condition = 75f; 
    public Personality personality;
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