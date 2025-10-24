using UnityEngine;
using System.Collections.Generic;
using GameCore;  // AgentList

[CreateAssetMenu(fileName = "AgentData", menuName = "AI/Agent Data", order = 1)]
public class AgentData : ScriptableObject
{
    public AgentList agentName;
    public List<AgentRecord> records;
    public double elo = 1500f;
    [Range(0f,100f)] public float condition = 75f; 
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