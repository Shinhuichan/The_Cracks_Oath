// AgentData.cs (수정됨)
using UnityEngine;
using System.Collections.Generic;
using GameCore;
using CustomInspector;

// Personality를 '학습 성향'으로 재정의
public enum Personality
{
    // 학습 안 함
    Static,       // 완벽주의(백무적), 규칙 없음(이하린)
    
    // 학습함
    Pragmatic,    // 실리주의 (모든 성공/실패에서 느리게 학습)
    Aggressive,   // 공격적 (공격 성공/실패에 민감하게 학습)
    Defensive,    // 방어적 (방어 성공/실패에 민감하게 학습)
    Specialist,   // 전문가 (자신의 특정 카드에만 민감하게 학습)
    Erratic,      // 변덕스러움 (결과와 상관없이 무작위/과대 학습)
    Emotional     // 감정적 (HP 격차에 따라 학습률 변경)
}

[CreateAssetMenu(fileName = "AgentData", menuName = "AI/Agent Data", order = 1)]
public class AgentData : ScriptableObject
{
    [ReadOnly] public AgentList agentName;
    [ReadOnly] public List<AgentRecord> records;
    [ReadOnly] public double elo = 1500f;
    
    public Personality personality;

    // ▼ [추가됨] 카드별 적응형 가중치
    // (참고: Unity는 기본 Dictionary를 인스펙터에 노출하지 않지만, ScriptableObject 내에서는
    //  직렬화가 가능합니다. 만약 문제가 발생하면 List<WeightEntry>로 변경해야 합니다.)
    public Dictionary<CardType, float> weights;

    // ▼ [추가됨] 가중치 초기화 메서드
    public void InitializeWeights()
    {
        weights = new Dictionary<CardType, float>();
        foreach (CardType card in System.Enum.GetValues(typeof(CardType)))
        {
            if (card == CardType.None) continue;
            weights[card] = 1.0f; // 모든 카드의 가중치를 1.0으로 시작
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