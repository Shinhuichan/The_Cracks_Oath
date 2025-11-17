using System;
using System.Collections.Generic;
using GameCore;

namespace GameCore.Learning
{
    [Serializable]
    public class LearningDatabase
    {
        public List<AgentMemory> memories = new List<AgentMemory>();
    }

    [Serializable]
    public class AgentMemory
    {
        public string agentId; // AgentList.ToString()
        public List<CardWeightInfo> cardWeights = new List<CardWeightInfo>();
        
        // ▼ [추가됨] 상대별 학습된 패턴 목록
        public List<OpponentPattern> opponentPatterns = new List<OpponentPattern>();
    }

    [Serializable]
    public class CardWeightInfo
    {
        public CardType card;
        public float weight;
    }

    // ▼ [추가됨] 특정 상대에 대한 패턴 데이터
    [Serializable]
    public class OpponentPattern
    {
        public string opponentId;       // 상대방 이름
        public List<PatternSequence> patterns = new List<PatternSequence>();
    }

    [Serializable]
    public class PatternSequence
    {
        public List<CardType> sequence = new List<CardType>();
        public int notSeenCount = 0;
        
        // ▼ [추가됨] 이 패턴이 몇 번이나 등장했는가? (기본값 1)
        public int frequency = 1; 
    }
}