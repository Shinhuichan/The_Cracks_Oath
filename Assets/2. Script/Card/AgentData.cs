using UnityEngine;
using System.Collections.Generic;
using GameCore;  // AgentList

public enum Personality
{
    냉소적인,     // 작은 변화만
    과몰입한,     // 큰 변화
    낙천적인,     // 항상 플러스
    비관적인,     // 항상 마이너스
    제멋대로,     // 결과 무관 랜덤
    감정적인,     // 양초 차이 기준 스위치
    무관심한      // 항상 100 유지
}

[CreateAssetMenu(fileName = "AgentData", menuName = "AI/Agent Data", order = 1)]
public class AgentData : ScriptableObject
{
    public AgentList agentName;
    public List<AgentRecord> records;
    public double elo = 1500f;
    [Range(0f, 100f)] public float condition = 75f; 
    public Personality personality;
}

[System.Serializable]
public struct AgentRecord
{   
    public AgentList verseAgent;
    public int matchCount;
    public int winCount;
    public int loseCount;
    public int drawCount;
}