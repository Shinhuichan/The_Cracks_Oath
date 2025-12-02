// AgentManager.cs (수정됨)
using UnityEngine;
using System.Collections.Generic;
using System;
using GameCore;
using GameCore.Learning; // 위에서 만든 네임스페이스
using System.Linq;
using System.IO;

public class AgentManager : SingletonBehaviour<AgentManager>
{
    protected override bool IsDontDestroy() => true;
    // ... (Elo 및 Record 관련 코드는 동일) ...
    [Header("Register Agents in Inspector")]
    [Header("Settings")]
    public List<AgentData> currentAgent;
    [SerializeField] private string saveFileName = "agent_learning_data.json";

    // 런타임 고속 접근용 캐시: [AgentName][CardType] = Weight
    private Dictionary<string, Dictionary<CardType, float>> runtimeWeights = new Dictionary<string, Dictionary<CardType, float>>();

    private string SavePath => Path.Combine(Application.persistentDataPath, saveFileName);

    // ▼ [수정됨] 런타임 패턴 구조 변경 (List<CardType> -> PatternSequence 객체 자체를 저장)
    // [MyId][OppId] -> List<PatternSequence>
    private Dictionary<string, Dictionary<string, List<PatternSequence>>> runtimePatterns 
        = new Dictionary<string, Dictionary<string, List<PatternSequence>>>();

    // 현재 매치 기록용 임시 버퍼: [MyName] -> 상대가 낸 카드 리스트
    private Dictionary<string, List<CardType>> currentMatchOpponentMoves 
        = new Dictionary<string, List<CardType>>();

    public enum MatchOutcome { Win, Draw, Loss }
    
    private void Start()
    {
        LoadLearningData();
    }

    private void OnApplicationQuit()
    {
        SaveLearningData();
    }

    // ============================================================
    // 1. 파일 입출력 (Save & Load) - [기능 추가됨]
    // ============================================================

    public void LoadLearningData()
    {
        runtimeWeights.Clear();
        runtimePatterns.Clear(); // 초기화

        if (File.Exists(SavePath))
        {
            try
            {
                string json = File.ReadAllText(SavePath);
                LearningDatabase db = JsonUtility.FromJson<LearningDatabase>(json);

                if (db != null)
                {
                    foreach (var mem in db.memories)
                    {
                        // 1. 가중치 로드
                        var dict = new Dictionary<CardType, float>();
                        foreach (var w in mem.cardWeights) dict[w.card] = w.weight;
                        runtimeWeights[mem.agentId] = dict;

                        // 2. ▼ [추가됨] 패턴 로드
                        if (mem.opponentPatterns != null)
                        {
                            var oppDict = new Dictionary<string, List<PatternSequence>>();
                            foreach (var op in mem.opponentPatterns)
                            {
                                oppDict[op.opponentId] = new List<PatternSequence>(op.patterns);
                            }
                            runtimePatterns[mem.agentId] = oppDict;
                        }
                    }
                    Debug.Log($"[AgentManager] Loaded data (Weights & Patterns) from {SavePath}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AgentManager] Load failed: {e.Message}");
            }
        }
    }

    public void SaveLearningData()
    {
        LearningDatabase db = new LearningDatabase();

        // 모든 에이전트 키 수집 (가중치 또는 패턴이 있는 모든 에이전트)
        HashSet<string> allAgents = new HashSet<string>(runtimeWeights.Keys);
        foreach(var key in runtimePatterns.Keys) allAgents.Add(key);

        foreach (var agentId in allAgents)
        {
            AgentMemory mem = new AgentMemory { agentId = agentId };

            // 1. 가중치 저장
            if (runtimeWeights.ContainsKey(agentId))
            {
                foreach (var kvp in runtimeWeights[agentId])
                    mem.cardWeights.Add(new CardWeightInfo { card = kvp.Key, weight = kvp.Value });
            }

            // 2. ▼ [추가됨] 패턴 저장 (이 부분이 누락되어 있었음)
            if (runtimePatterns.ContainsKey(agentId))
            {
                foreach (var oppKvp in runtimePatterns[agentId])
                {
                    OpponentPattern op = new OpponentPattern { opponentId = oppKvp.Key };
                    op.patterns = new List<PatternSequence>(oppKvp.Value); // 객체 리스트 복사
                    mem.opponentPatterns.Add(op);
                }
            }

            db.memories.Add(mem);
        }

        string json = JsonUtility.ToJson(db, true);
        File.WriteAllText(SavePath, json);
    }
    
    // ▼ [추가됨] 가중치 초기화 (게임 시작 시 호출)
    public void InitializeAllWeights()
    {
        foreach (var data in currentAgent)
        {
            data.InitializeWeights();
        }
    }

    // ============================================================
    // 2. 외부 접근 (Getter)
    // ============================================================

    /// <summary>
    /// 특정 에이전트의 특정 카드에 대한 가중치를 가져옵니다. (기본값 1.0)
    /// </summary>
    public float GetWeight(AgentList agentId, CardType card)
    {
        string key = agentId.ToString();
        if (runtimeWeights.ContainsKey(key) && runtimeWeights[key].ContainsKey(card))
        {
            return runtimeWeights[key][card];
        }
        return 1.0f; // 데이터가 없으면 기본값
    }

    /// <summary>
    /// 특정 에이전트의 모든 가중치 딕셔너리를 가져옵니다.
    /// </summary>
    public Dictionary<CardType, float> GetWeights(AgentList agentId)
    {
        string key = agentId.ToString();
        if (!runtimeWeights.ContainsKey(key))
        {
            // 없으면 새로 생성해서 등록
            var newDict = new Dictionary<CardType, float>();
            foreach (CardType c in System.Enum.GetValues(typeof(CardType)))
                if(c != CardType.None) newDict[c] = 1.0f;
            
            runtimeWeights[key] = newDict;
        }
        return runtimeWeights[key];
    }

    // ============================================================
    // ★ [신규 기능] Threat Level 자동 산정 시스템
    // ============================================================

    /// <summary>
    /// 현재 등록된 모든 에이전트의 ELO를 기준으로 ThreatLevel(티어)을 재분배합니다.
    /// (시뮬레이션 리그 종료 후 호출 권장)
    /// </summary>
    public void RecalculateThreatLevels()
    {
        if (currentAgent == null || currentAgent.Count == 0) return;

        // 1. ELO 기준 오름차순 정렬 (낮은 점수 -> 높은 점수)
        // sortedList[0] = 꼴찌, sortedList[Last] = 1등
        var sortedList = currentAgent.OrderBy(a => a.elo).ToList();
        int totalCount = sortedList.Count;

        for (int i = 0; i < totalCount; i++)
        {
            // 2. 백분위 계산 (하위 몇 %인지)
            // (i + 1)을 사용하여 0%가 나오지 않도록 함
            float percentile = (float)(i + 1) / totalCount * 100f;
            
            ThreatLevel newTier;

            // 3. 티어 분배 로직 (사용자 정의 비율)
            if (percentile <= 10f)      newTier = ThreatLevel.Prey;         // 하위 0 ~ 10%
            else if (percentile <= 25f) newTier = ThreatLevel.Unstable;     // 하위 10 ~ 25%
            else if (percentile <= 40f) newTier = ThreatLevel.Variables;    // 하위 25 ~ 40%
            else if (percentile <= 60f) newTier = ThreatLevel.Challengers;  // 하위 40 ~ 60%
            else if (percentile <= 75f) newTier = ThreatLevel.Masters;      // 하위 60 ~ 75%
            else if (percentile <= 90f) newTier = ThreatLevel.Grandmasters; // 하위 75 ~ 90%
            else                        newTier = ThreatLevel.Absolute;     // 하위 90 ~ 100% (상위 10%)

            // 4. 데이터 적용
            sortedList[i].threatLevel = newTier;
        }

        Debug.Log($"[AgentManager] Threat Levels Recalculated based on ELO distribution (Total: {totalCount})");
    }

    /// <summary>
    /// 라운드 결과에 따라 AI의 카드 가중치를 '성격'에 맞게 업데이트(학습)합니다.
    /// </summary>
    /// <param name="who">학습할 AI</param>
    /// <param name="selfCard">AI가 낸 카드</param>
    /// <param name="oppCard">상대가 낸 카드</param>
    /// <param name="hpDeltaThisRound">AI의 이번 라운드 총 양초 변화량</param>
    /// <param name="selfLife">AI의 현재 양초</param>
    /// <param name="oppLife">상대의 현재 양초</param>
    // ============================================================
    // 3. 핵심 학습 로직 (Learning Logic)
    // ============================================================
    public void LearnFromRound(AgentList agentId, CardType playedCard, int hpDelta, int currentHp, int oppHp)
    {
        // if (playedCard == CardType.None) return;

        // // 1) 에이전트의 성향(AgentData) 가져오기
        // AgentData data = GetAgentData(agentId);
        // Personality personality = (data != null) ? data.personality : Personality.Static;

        // if (personality == Personality.Static) return;

        // // 2) 현재 가중치 가져오기
        // var weights = GetWeights(agentId);
        
        // // [안전 접근] 키가 없으면 1.0f
        // float currentW = weights.ContainsKey(playedCard) ? weights[playedCard] : 1.0f;

        // // 3) 학습률(Learning Rate) 및 보상(Reward) 결정
        // float changeRate = 1.0f;
        
        // bool success = hpDelta > 0;
        // bool fail = hpDelta < 0;
        
        // switch (personality)
        // {
        //     case Personality.Pragmatic: 
        //         if (success) changeRate = 1.02f;      
        //         else if (fail) changeRate = 0.98f;    
        //         break;

        //     case Personality.Aggressive: 
        //         bool isAtk = (playedCard == CardType.Betrayal || playedCard == CardType.Pollution);
        //         if (isAtk && success) changeRate = 1.10f;      
        //         else if (isAtk && fail) changeRate = 0.90f;    
        //         else if (fail) changeRate = 0.99f;             
        //         break;

        //     case Personality.Defensive: 
        //         bool isDef = (playedCard == CardType.Doubt || playedCard == CardType.Interrupt);
        //         if (isDef && fail) changeRate = 0.85f;         
        //         else if (success) changeRate = 1.03f;          
        //         break;
            
        //     case Personality.Emotional: 
        //         bool losing = currentHp < oppHp;
        //         float strength = losing ? 0.15f : 0.05f; 
        //         if (success) changeRate = 1.0f + strength;
        //         else if (fail) changeRate = 1.0f - strength;
        //         break;

        //      case Personality.Erratic: 
        //         changeRate = UnityEngine.Random.Range(0.9f, 1.1f);
        //         break;
                
        //      case Personality.Specialist: 
        //          changeRate = success ? 1.05f : 0.95f;
        //          break;
        // }

        // // 4) 가중치 적용 (changeRate가 1.0f이므로 변화 없음)
        // float finalW = Mathf.Clamp(currentW * changeRate, 0.1f, 3.0f);
        // weights[playedCard] = finalW;
    }

    public void ApplyMatchResult(AgentList a, AgentList b, MatchOutcome outcomeA, double k = 24.0)
    {
        // 1) ELO 양쪽 갱신
        var ra = GetElo(a);
        var rb = GetElo(b);
        double ea = 1.0 / (1.0 + Math.Pow(10.0, (rb - ra) / 400.0));
        double sa = outcomeA == MatchOutcome.Win ? 1.0 : outcomeA == MatchOutcome.Draw ? 0.5 : 0.0;
        double sb = 1.0 - sa;

        ra = ra + k * (sa - ea);
        rb = rb + k * (sb - (1.0 - ea));

        SetElo(a, ra);
        SetElo(b, rb);

        // 2) 상대별 전적 집계
        var da = GetData(a);
        var db = GetData(b);
        var recA = GetOrCreateRecord(da, b);
        var recB = GetOrCreateRecord(db, a);

        recA.matchCount++;
        recB.matchCount++;

        if (outcomeA == MatchOutcome.Win) { recA.winCount++; recB.loseCount++; }
        else if (outcomeA == MatchOutcome.Draw) { recA.drawCount++; recB.drawCount++; }
        else { recA.loseCount++; recB.winCount++; }

        SaveRecord(da, recA);
        SaveRecord(db, recB);
    }

    // ★ [추가] 시뮬레이션 결과(수천 판)를 한 번에 전적에 반영하는 메서드
    public void ApplyBatchResult(AgentList a, AgentList b, int winsA, int winsB, int draws, double k = 24.0)
    {
        // 1) ELO 갱신 (배치 단위에서는 평균 승률로 1회만 갱신하거나, 가중치를 줄여서 반영)
        // 여기서는 '전체 승률'을 기반으로 1회 갱신하되 K값을 매치 수에 비례하게 조정하는 방식 등 다양하지만,
        // 단순하게 '다수결 승자' 기준으로 1회만 반영하는 것이 ELO 인플레이션 방지에 좋습니다.
        // (수천 번 ELO를 갱신하면 값이 폭주할 수 있음)
        
        MatchOutcome outcome = MatchOutcome.Draw;
        if (winsA > winsB) outcome = MatchOutcome.Win;
        else if (winsB > winsA) outcome = MatchOutcome.Loss;
        
        ApplyMatchResult(a, b, outcome, k); // ELO는 대표 결과로 1회만

        var da = GetData(a);
        var db = GetData(b);
        var recA = GetOrCreateRecord(da, b);
        var recB = GetOrCreateRecord(db, a);

        int totalPlayed = winsA + winsB + draws;
        recA.matchCount += totalPlayed;
        recB.matchCount += totalPlayed;
        recA.winCount += winsA;
        recA.loseCount += winsB;
        recA.drawCount += draws;
        recB.winCount += winsB;
        recB.loseCount += winsA;
        recB.drawCount += draws;

        SaveRecord(da, recA);
        SaveRecord(db, recB);
        
        // ★ [선택 사항] 배치 결과가 반영될 때마다 티어를 갱신하고 싶다면 여기서 호출
        // 하지만 매번 부르면 성능/로그 낭비가 심하므로, 보통은 리그 종료 시점에 호출하는 것을 권장합니다.
        // RecalculateThreatLevels();
    }

    public double GetElo(AgentList id)
    {
        if (currentAgent.Find(x => x.agentName == id) is not AgentData data)
            return 1000.0; // 기본값
        return data.elo;
    }

    public void SetElo(AgentList id, double rating)
    {
        if (currentAgent.Find(x => x.agentName == id) is not AgentData data)
            return;
        data.elo = rating;
    }

    public void ApplyEloResult(AgentList a, AgentList b, MatchOutcome outcomeA, double k = 24.0)
    {
        var ra = GetElo(a);
        var rb = GetElo(b);

        double ea = 1.0 / (1.0 + Math.Pow(10.0, (rb - ra) / 400.0));
        double eb = 1.0 - ea;

        double sa = outcomeA == MatchOutcome.Win ? 1.0 :
                    outcomeA == MatchOutcome.Draw ? 0.5 : 0.0;
        double sb = 1.0 - sa;

        ra = ra + k * (sa - ea);
        rb = rb + k * (sb - eb);

        SetElo(a, ra);
        SetElo(b, rb);
    }

    public MatchOutcome OutcomeFromMatchPoints(int myPts, int oppPts)
    {
        if (myPts > oppPts) return MatchOutcome.Win;
        if (myPts < oppPts) return MatchOutcome.Loss;
        return MatchOutcome.Draw;
    }
    public AgentData GetAgentData(AgentList id)
    {
        // 등록 목록에서 해당 에이전트 데이터를 반환. 없으면 null
        return currentAgent?.Find(x => x.agentName == id);
    }
    AgentData GetData(AgentList id) 
    {
        var found = currentAgent.Find(x => x.agentName == id);
        
        // ★ [디버그 추가] 민도형인데 데이터를 못 찾으면 경고 띄우기
        if (found == null && id.ToString() == "민도형")
        {
            Debug.LogError($"[AgentManager] '민도형' 데이터를 찾을 수 없습니다! CurrentAgent 리스트에 등록된 {currentAgent.Count}명을 확인하세요.");
            foreach(var a in currentAgent)
            {
                if(a == null) Debug.LogWarning(" -> 빈 슬롯(Null)이 발견되었습니다.");
                else Debug.Log($" -> 등록됨: {a.agentName}");
            }
        }
        
        return found;
    }

    // 상대별 레코드 가져오기/없으면 추가
    AgentRecord GetOrCreateRecord(AgentData data, AgentList versus)
    {
        if (data == null) return default;
        if (data.records == null) data.records = new List<AgentRecord>();

        int idx = data.records.FindIndex(r => r.verseAgent == versus);
        if (idx >= 0) return data.records[idx];

        var rec = new AgentRecord { verseAgent = versus, matchCount = 0, winCount = 0, loseCount = 0, drawCount = 0 };
        data.records.Add(rec);
        return rec;
    }

    void SaveRecord(AgentData data, AgentRecord rec)
    {
        if (data == null) return;
        int idx = data.records.FindIndex(r => r.verseAgent == rec.verseAgent);
        if (idx >= 0) data.records[idx] = rec;
        else data.records.Add(rec);
    }



    #region 서유리 전용 (PatternBreaker)

    public void ObserveOpponentMove(AgentList observer, AgentList target, CardType move)
    {
        string key = observer.ToString();
        if (!currentMatchOpponentMoves.ContainsKey(key))
            currentMatchOpponentMoves[key] = new List<CardType>();
        currentMatchOpponentMoves[key].Add(move);
    }

    /// <summary>
    /// 게임 종료 시 호출: 패턴 분석 + 저장 + [NEW] 안 쓰이는 패턴 삭제
    /// </summary>
    public void AnalyzeMatchPatterns(AgentList observer, AgentList target)
    {
        string obsKey = observer.ToString();
        string tarKey = target.ToString();
        
        if (!currentMatchOpponentMoves.ContainsKey(obsKey)) return;
        List<CardType> history = currentMatchOpponentMoves[obsKey];

        // ---------------------------------------------------------
        // 1. [NEW] 망각(Forgetting) 로직: 기존 패턴 검증
        // ---------------------------------------------------------
        if (runtimePatterns.ContainsKey(obsKey) && runtimePatterns[obsKey].ContainsKey(tarKey))
        {
            var knownPatterns = runtimePatterns[obsKey][tarKey];
            
            // 리스트 역순 순회 (삭제를 위해)
            for (int i = knownPatterns.Count - 1; i >= 0; i--)
            {
                var pat = knownPatterns[i];
                
                // 이번 게임 기록(history) 안에 이 패턴(pat.sequence)이 등장했는가?
                bool appeared = ContainsSequence(history, pat.sequence);

                if (appeared)
                {
                    pat.notSeenCount = 0; // 등장했으면 카운터 초기화 (강화)
                }
                else
                {
                    pat.notSeenCount++;   // 등장 안 했으면 카운터 증가
                }

                // 5경기 연속 미등장 시 삭제
                if (pat.notSeenCount >= 5)
                {
                    // Debug.Log($"[PatternBreaker] {observer} forgot pattern of {target} (Not seen for 10 matches)");
                    knownPatterns.RemoveAt(i);
                }
            }
        }

        // ---------------------------------------------------------
        // 2. 신규 패턴 학습 (기존 로직 유지)
        // ---------------------------------------------------------
        if (history.Count >= 15) 
        {
            for (int len = 5; len <= 10; len++)
            {
                for (int i = 0; i <= history.Count - len; i++)
                {
                    var candidate = history.GetRange(i, len);
                    int count = 0;
                    
                    for (int j = 0; j <= history.Count - len; j++)
                    {
                        bool match = true;
                        for (int k = 0; k < len; k++)
                            if (history[j + k] != candidate[k]) { match = false; break; }
                        if (match) { count++; j += len - 1; }
                    }

                    if (count >= 3)
                    {
                        AddKnownPattern(observer, target, candidate);
                    }
                }
            }
        }
        currentMatchOpponentMoves[obsKey].Clear();
    }

    // 헬퍼 함수: 역사 속에 특정 시퀀스가 존재하는지 확인
    private bool ContainsSequence(List<CardType> fullHistory, List<CardType> subSeq)
    {
        if (subSeq.Count > fullHistory.Count) return false;
        for (int i = 0; i <= fullHistory.Count - subSeq.Count; i++)
        {
            bool match = true;
            for (int j = 0; j < subSeq.Count; j++)
            {
                if (fullHistory[i + j] != subSeq[j]) { match = false; break; }
            }
            if (match) return true;
        }
        return false;
    }

    void AddKnownPattern(AgentList observer, AgentList target, List<CardType> patternSeq)
    {
        string obsKey = observer.ToString();
        string tarKey = target.ToString();

        if (!runtimePatterns.ContainsKey(obsKey))
            runtimePatterns[obsKey] = new Dictionary<string, List<PatternSequence>>();
        
        if (!runtimePatterns[obsKey].ContainsKey(tarKey))
            runtimePatterns[obsKey][tarKey] = new List<PatternSequence>();

        var list = runtimePatterns[obsKey][tarKey];

        // 중복 체크
        foreach (var exist in list)
        {
            if (exist.sequence.Count == patternSeq.Count && Enumerable.SequenceEqual(exist.sequence, patternSeq)) 
            {
                exist.notSeenCount = 0; // 리프레시
                exist.frequency++;      // ▼ [추가됨] 빈도 증가 (이게 핵심입니다!)
                return;
            }
        }

        // 새 패턴 등록
        // ▼ [수정됨] frequency = 1 로 초기화
        list.Add(new PatternSequence { sequence = patternSeq, notSeenCount = 0, frequency = 1 });
    }

    public CardType? PredictNextCard(AgentList observer, AgentList target, RoundCtx ctx)
    {
        if (ctx.round < 4) return null;

        string obsKey = observer.ToString();
        string tarKey = target.ToString();

        if (!runtimePatterns.ContainsKey(obsKey) || !runtimePatterns[obsKey].ContainsKey(tarKey))
            return null;

        var knownList = runtimePatterns[obsKey][tarKey];
        var recent = new List<CardType> { ctx.last3Opp, ctx.last2Opp, ctx.lastOpp };

        // ▼ [수정됨] 빈도 기반 예측 로직 (Weighted Prediction)
        // "다음 수"가 무엇이 될지 후보별 점수(빈도)를 매깁니다.
        Dictionary<CardType, int> candidates = new Dictionary<CardType, int>();
        int bestFreq = 0;

        foreach (var patObj in knownList)
        {
            var pat = patObj.sequence;
            if (pat.Count < 4) continue;

            // 현재 상황(recent 3장)과 패턴의 앞부분이 일치하는가?
            bool match = true;
            for (int i = 0; i < 3; i++)
            {
                if (pat[i] != recent[i]) { match = false; break; }
            }

            if (match && pat.Count > 3)
            {
                CardType nextMove = pat[3]; // 패턴상의 다음 수
                
                if (!candidates.ContainsKey(nextMove)) candidates[nextMove] = 0;
                
                // 해당 패턴의 역사적 등장 횟수만큼 가점을 줍니다.
                candidates[nextMove] += patObj.frequency; 
            }
        }

        // 후보가 없으면 null
        if (candidates.Count == 0) return null;

        // 가장 점수(빈도 합계)가 높은 카드 선택
        CardType bestPrediction = CardType.None;
        int maxScore = -1;

        foreach (var kvp in candidates)
        {
            if (kvp.Value > maxScore)
            {
                maxScore = kvp.Value;
                bestPrediction = kvp.Key;
            }
        }

        // (선택 사항) 확신이 너무 낮으면 예측 안 함 (예: 빈도 1~2회는 무시)
        if (maxScore < 2) return null; 

        return bestPrediction;
    }
    #endregion

    #region 백무적 전용 (Omni-Computation)
    /// <summary>
    /// [백무적 전용] 상대방이 다음에 낼 카드의 '확률 분포'를 예측합니다.
    /// 패턴이 있으면 패턴을 따르고, 없으면 과거 통계를 기반으로 추론합니다.
    /// </summary>
    public Dictionary<CardType, float> GetPredictedProbabilities(AgentList observer, AgentList target, RoundCtx ctx)
    {
        var probs = new Dictionary<CardType, float>();
        foreach (CardType c in Enum.GetValues(typeof(CardType))) 
            if(c != CardType.None) probs[c] = 0.05f; // 기본 확률 (약간의 불확실성)

        string obsKey = observer.ToString();
        string tarKey = target.ToString();

        // 1. 패턴 기반 예측 (가장 강력한 단서)
        if (runtimePatterns.ContainsKey(obsKey) && runtimePatterns[obsKey].ContainsKey(tarKey))
        {
            var knownList = runtimePatterns[obsKey][tarKey];
            var recent = new List<CardType> { ctx.last3Opp, ctx.last2Opp, ctx.lastOpp };
            
            foreach (var patObj in knownList)
            {
                // 패턴 매칭 확인
                bool match = true;
                if (patObj.sequence.Count < 4) match = false;
                else
                {
                    for (int i = 0; i < 3; i++)
                        if (patObj.sequence[i] != recent[i]) { match = false; break; }
                }

                if (match)
                {
                    CardType next = patObj.sequence[3];
                    if (probs.ContainsKey(next))
                        probs[next] += (patObj.frequency * 5.0f); // 패턴 빈도에 높은 가중치 부여
                }
            }
        }

        // 2. 정규화 (확률의 합을 1.0으로 맞춤)
        float total = probs.Values.Sum();
        if (total > 0)
        {
            foreach (var key in probs.Keys.ToList()) probs[key] /= total;
        }

        return probs;
    }
    #endregion
}