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
    // 싱글톤 설정
    protected override bool IsDontDestroy() => true;

    [Header("Register Agents in Inspector")]
    public List<AgentData> currentAgent;
    [SerializeField] private string saveFileName = "agent_learning_data.json";

    [Header("ELO Settings")]
    [Tooltip("기본 시작 ELO 점수입니다.")]
    public double startingElo = 1500.0;

    [Tooltip("변동폭 계수(K-Factor). 값이 클수록 한 판의 결과가 점수에 큰 영향을 줍니다. (기본 24 -> 32~40 추천)")]
    public double kFactor = 16.0;

    [Tooltip("승리 시 점수 획득 배율 (1.0 = 표준). 낮추면(0.8) 점수 올리기가 더 힘들어집니다.")]
    public double winWeight = 0.5f;

    [Tooltip("패배 시 점수 차감 배율 (1.0 = 표준). 높이면(1.2) 패배 시 점수가 더 많이 깎입니다.")]
    public double loseWeight = 0.75;
    // [변수 추가] 인스펙터에서 설정 가능하도록 public으로 선언
    [Header("Advanced ELO Settings")]
    [Tooltip("업셋/쉴드 가중치가 적용되는 기준 ELO 차이입니다. (예: 100점 차이마다 가중치 적용)")]
    public double standardElo = 100.0;

    [Tooltip("역배(Upset) 시 점수 변동폭 증가율 (기본 0.05 = 5%). 격차가 클수록 증폭됩니다.")]
    public double upsetWeight = 0.05;

    [Tooltip("정배(Shield) 시 점수 변동폭 감소율 (기본 0.05 = 5%). 격차가 클수록 많이 감소합니다.")]
    public double shieldWeight = 0.05;

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
    // ★ [신규 기능] 초기화 메서드 (Context Menu & Public)
    // ============================================================

    /// <summary>
    /// 모든 에이전트의 ELO와 전적 기록을 초기화합니다.
    /// 에디터에서 AgentManager 컴포넌트를 우클릭하여 실행할 수 있습니다.
    /// </summary>
    [ContextMenu("Reset All Records & ELO")]
    public void ResetAllRecordsAndElo()
    {
        if (currentAgent == null) return;

        foreach (var agent in currentAgent)
        {
            if (agent == null) continue;
            
            // ELO 초기화
            agent.elo = startingElo;
            
            // 전적 리스트 초기화
            if (agent.records != null)
            {
                agent.records.Clear();
            }
            else
            {
                agent.records = new List<AgentRecord>();
            }
        }

        // 파일에도 즉시 저장하여 반영
        SaveLearningData(); 
        
        Debug.Log($"[AgentManager] 모든 참가자의 ELO가 {startingElo}으로, 전적이 0으로 초기화되었습니다.");
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

    // ▼ [수정됨] 인스펙터 설정을 사용하는 단일 매치 결과 적용
    // k 파라미터를 -1로 주면 인스펙터의 kFactor를 사용합니다.
    public void ApplyMatchResult(AgentList a, AgentList b, MatchOutcome outcomeA, double k = -1.0)
    {
        CalculateAndSetElo(a, b, outcomeA, k);

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

    // ▼ [수정됨] 배치 결과 개별 반영 (정확도 향상)
    public void ApplyBatchResult(AgentList a, AgentList b, int winsA, int winsB, int draws, double k = -1.0)
    {
        // 1. 전체 경기 수 및 결과 배열 생성
        int totalPlayed = winsA + winsB + draws;
        
        // 결과 목록 생성 (0:무, 1:A승, 2:B승)
        // 메모리 할당 최적화를 위해 int 배열 사용
        int[] results = new int[totalPlayed];
        int idx = 0;
        
        for (int i = 0; i < winsA; i++) results[idx++] = 1;
        for (int i = 0; i < winsB; i++) results[idx++] = 2;
        for (int i = 0; i < draws; i++) results[idx++] = 0;

        // 2. 결과 셔플 (Fisher-Yates Shuffle)
        // 실제 경기 순서를 알 수 없으므로, 랜덤하게 섞어서 시뮬레이션해야 
        // ELO의 '연승/연패' 왜곡을 줄이고 평균적인 변화를 반영할 수 있음.
        for (int i = 0; i < totalPlayed; i++)
        {
            int r = UnityEngine.Random.Range(i, totalPlayed);
            int temp = results[i];
            results[i] = results[r];
            results[r] = temp;
        }

        // 3. 개별 게임 ELO 반영 (Loop)
        // 각 판마다 ELO가 변동되고, 그 변동된 ELO가 다음 판의 기대 승률에 영향을 줌 -> 정확성 UP
        for (int i = 0; i < totalPlayed; i++)
        {
            MatchOutcome outcome;
            if (results[i] == 1) outcome = MatchOutcome.Win;
            else if (results[i] == 2) outcome = MatchOutcome.Loss;
            else outcome = MatchOutcome.Draw;

            // 개별 게임이므로 K값을 그대로 사용하면 변동폭이 너무 클 수 있음.
            // 하지만 사용자가 '개별 반영'을 원했으므로 설정된 K값을 그대로 적용.
            // (너무 크다면 인스펙터에서 K-Factor를 줄이는 것을 권장)
            CalculateAndSetElo(a, b, outcome, k);
        }

        // 4. 전적(Record) 누적 (이건 합산해서 한 번에 해도 됨)
        var da = GetData(a);
        var db = GetData(b);
        var recA = GetOrCreateRecord(da, b);
        var recB = GetOrCreateRecord(db, a);

        recA.matchCount += totalPlayed;
        recA.winCount += winsA;
        recA.loseCount += winsB;
        recA.drawCount += draws;

        recB.matchCount += totalPlayed;
        recB.winCount += winsB;
        recB.loseCount += winsA; 
        recB.drawCount += draws;

        SaveRecord(da, recA);
        SaveRecord(db, recB);
    }
    
    // ▼ [수정됨] ELO 계산 로직 (비례 가중치 적용)
    private void CalculateAndSetElo(AgentList a, AgentList b, MatchOutcome outcomeA, double kOverride)
    {
        // 1. 기본 설정
        double baseK = (kOverride > 0) ? kOverride : kFactor;
        var ra = GetElo(a);
        var rb = GetElo(b);

        // 2. 기대 승률 계산
        double ea = 1.0 / (1.0 + Math.Pow(10.0, (rb - ra) / 400.0));
        double eb = 1.0 - ea; 

        // 3. 실제 결과
        double sa = outcomeA == MatchOutcome.Win ? 1.0 : outcomeA == MatchOutcome.Draw ? 0.5 : 0.0;
        double sb = 1.0 - sa;

        // 4. 기본 점수 변동량 (Delta)
        double deltaA = baseK * (sa - ea);
        double deltaB = baseK * (sb - eb);

        // 5. ★ [핵심] 격차 기반 동적 가중치 적용
        double eloGap = Math.Abs(ra - rb);
        
        // 격차 비율 (몇 배의 standardElo 차이인가?)
        // 예: 차이 200, 기준 100 -> ratio 2.0
        double gapRatio = eloGap / Math.Max(1.0, standardElo); 

        // 비선형 스케일링 (가파르게 만들기 위해 제곱 등 적용 가능, 여기선 선형+추가 보정)
        // 격차가 클수록 효과를 극대화하기 위해 ratio를 그대로 사용하거나 제곱 사용
        // double scaleFactor = Math.Pow(gapRatio, 1.5); // (선택사항: 더 가파르게 하려면 사용)
        double scaleFactor = gapRatio; 

        // [상황 판별]
        // Upset (역배): (강자가 짐) OR (약자가 이김)
        bool isUpset = (ra > rb && outcomeA == MatchOutcome.Loss) || (ra < rb && outcomeA == MatchOutcome.Win);
        
        // Shield (정배): (강자가 이김) OR (약자가 짐)
        bool isShield = (ra > rb && outcomeA == MatchOutcome.Win) || (ra < rb && outcomeA == MatchOutcome.Loss);

        double modifier = 1.0;

        if (isUpset)
        {
            // 역배: 변동폭 증가 (Boost)
            // scaleFactor * upsetWeight 만큼 추가 (예: 2배 격차 * 5% = 10% 증가)
            // 1.0 + (2.0 * 0.05) = 1.1배
            modifier = 1.0 + (scaleFactor * upsetWeight);
        }
        else if (isShield)
        {
            // 정배: 변동폭 감소 (Dampen)
            // scaleFactor * shieldWeight 만큼 감소 (최소 10%는 남김)
            // 1.0 - (2.0 * 0.05) = 0.9배
            modifier = Math.Max(0.1, 1.0 - (scaleFactor * shieldWeight));
        }

        // 가중치 적용
        deltaA *= modifier;
        deltaB *= modifier;

        // 6. 승패 기본 가중치 적용 (인스펙터 설정)
        if (deltaA > 0) deltaA *= winWeight; else deltaA *= loseWeight;
        if (deltaB > 0) deltaB *= winWeight; else deltaB *= loseWeight;

        // 7. 최종 반영
        ra += deltaA;
        rb += deltaB;

        SetElo(a, ra);
        SetElo(b, rb);
    }

    public double GetElo(AgentList id)
    {
        if (currentAgent.Find(x => x.agentName == id) is not AgentData data)
            return 1500.0; // 기본값
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

    // ▼ [추가됨] 라운드 학습 메서드 (오류 해결용)
    public void LearnFromRound(AgentList agentId, CardType playedCard, int hpDelta, int currentHp, int oppHp)
    {
        // 1. 데이터 로드
        string key = agentId.ToString();
        if (!runtimeWeights.ContainsKey(key))
        {
            // 없으면 초기화
            var newDict = new Dictionary<CardType, float>();
            foreach (CardType c in Enum.GetValues(typeof(CardType)))
                if (c != CardType.None) newDict[c] = 1.0f;
            runtimeWeights[key] = newDict;
        }
        var weights = runtimeWeights[key];

        // 2. 학습 로직 (간단한 강화학습)
        // hpDelta > 0 이면 좋은 수 -> 가중치 증가
        // hpDelta < 0 이면 나쁜 수 -> 가중치 감소
        
        float learningRate = 0.05f; // 학습률
        float reward = 0f;

        if (hpDelta > 0) reward = 1.0f;       // 이득
        else if (hpDelta < 0) reward = -1.0f; // 손해
        else reward = 0.1f;                   // 무승부는 약간 긍정 (버티기)

        // 가중치 업데이트
        if (weights.ContainsKey(playedCard))
        {
            weights[playedCard] += learningRate * reward;
            
            // 가중치 범위 제한 (0.1 ~ 5.0)
            weights[playedCard] = Mathf.Clamp(weights[playedCard], 0.1f, 5.0f);
        }
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