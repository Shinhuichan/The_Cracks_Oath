using UnityEngine;
using System.Collections.Generic;
using System;
using GameCore;  // AgentList
using System.Linq;

public class AgentManager : SingletonBehaviour<AgentManager>
{
    protected override bool IsDontDestroy() => true;

    [Header("Register Agents in Inspector")]
    public List<AgentData> currentAgent;

    public enum MatchOutcome { Win, Draw, Loss }

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
    AgentData GetData(AgentList id) => currentAgent.Find(x => x.agentName == id);

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
    public float GetCondition(AgentList id)
    {
        var data = currentAgent?.Find(x => x.agentName == id);
        return data != null ? Mathf.Clamp(data.condition, 0f, 100f) : 70f;
    }

    public void SetCondition(AgentList id, float value)
    {
        var data = currentAgent?.Find(x => x.agentName == id);
        if (data == null) return;
        data.condition = Mathf.Clamp(value, 0f, 100f);
    }

    /// <summary>
    /// condition을 바탕으로 의도적 '실수'를 주입해 난이도를 조절한다.
    /// pMistake: cond=0 -> 0.45, cond=50 -> 0.225, cond=100 -> 0.0
    /// </summary>
    public static void ApplyConditionToAgent(Agent agent, float condition)
    {
        if (agent == null) return;

        // 0~100 → 0.0~1.0
        float q = Mathf.Clamp01(condition / 100f);

        // ===== 1) 상단 메타 규칙(킬각/세이브각) 주입 =====
        // 이미 같은 레퍼런스를 여러 번 넣지 않도록 항상 새 델리게이트 생성 후 맨 앞에 삽입
        Func<DecisionInput, CardType?> metaRule = I =>
        {
            int R = Mathf.Max(1, I.s.round);

            // 즉사각: 상대 남은 양초 ≤ 라운드 수, 그리고 배신이 손에 있으면 우선시
            if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R)
                return CardType.Betrayal;

            // 세이브각: 내 양초가 위험하고 상대가 직전에 공격적이면 의심 우선
            if (I.HandHas(CardType.Doubt)
                && I.s.selfLife <= R
                && (I.s.lastOpp == CardType.Betrayal || I.s.lastOpp == CardType.Pollution))
                return CardType.Doubt;

            return null; // 다른 규칙으로 위임
        };

        // 맨 앞에 메타 규칙을 한 번만 넣기 위해 새 리스트 생성
        var newRules = new List<Func<DecisionInput, CardType?>>(capacity: agent.rules.Count + 1);
        newRules.Add(metaRule);
        newRules.AddRange(agent.rules);
        agent.rules = newRules;

        // ===== 2) 선택 드로우 품질 조정 =====
        // q가 높을수록 더 "좋은" 카드를 고르고, q가 낮을수록 실수 확률을 높인다.
        agent.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
        {
            int R = Mathf.Max(1, I.s.round);

            int Score(CardType t)
            {
                // 기본 가치
                int baseScore = t switch
                {
                    CardType.Betrayal   => 100,
                    CardType.Doubt      => 90,
                    CardType.Interrupt  => 85,
                    CardType.Pollution  => 80,
                    CardType.Cooperation=> 60,
                    CardType.Recon      => 50,
                    CardType.Chaos      => 10,
                    _ => 0
                };

                // 라운드·상황 보정
                if (t == CardType.Betrayal && I.s.oppLife <= R) baseScore += 40; // 킬각
                if (t == CardType.Doubt && I.s.selfLife <= R)   baseScore += 25; // 세이브각

                // 덱/상대패 분포 보정(분포는 DecisionInput.Ratio)
                // 방어가치: 상대 배신 확률이 높을수록 Doubt/Interrupt 보너스
                baseScore += (int)(I.Ratio(CardType.Betrayal) * (t == CardType.Doubt || t == CardType.Interrupt ? 40 : 0));

                return baseScore;
            }

            int sa = Score(a);
            int sb = Score(b);

            // 실수 확률: q가 낮을수록 더 자주 틀리게 선택
            float mistakeProb = (1f - q) * 0.6f;    // 컨디션 0 → 60% 확률로 실수, 100 → 0%

            bool pickBest = UnityEngine.Random.value >= mistakeProb;

            if (sa == sb)
            {
                // 동점이면 약한 잡음 추가: 컨디션이 좋으면 더 일관성 있게 선택
                float bias = 0.5f + 0.4f * (q - 0.5f); // q=1 → 0.7, q=0 → 0.3
                return UnityEngine.Random.value < bias ? 0 : 1; // int 반환(= int?로 암시적 변환 가능)
            }

            if (pickBest) return sa > sb ? 0 : 1;   // 최적 선택
            else          return sa > sb ? 1 : 0;   // 의도적 실수
        };
    }
}