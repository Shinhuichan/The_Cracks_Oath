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
    
    // condition이 0일 때 최대 실수 확률
    public float standardCondition = 0.5f;
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
    public void ApplyConditionAfterRound(AgentList who, int hpDeltaThisRound, int selfLife, int oppLife)
    {
        var data = GetAgentData(who);
        if (data == null) return;

        // 무관심한: 항상 100으로 고정
        if (data.personality == Personality.무관심한)
        {
            data.condition = 100f;
            return;
        }

        // 결과 부호
        int sign = hpDeltaThisRound == -1 ? 0 : (hpDeltaThisRound >= 0 ? +1 : -1);

        // 기본 증감(냉소적 기준)
        float good = +0.2f, bad = -0.2f, draw = 0f;

        switch (data.personality)
        {
            case Personality.냉소적인:
                good = +0.2f; bad = -0.2f; draw = 0f;
                break;

            case Personality.과몰입한:
                good = +1f; bad = -1f; draw = 0f;
                break;

            case Personality.낙천적인:
                // 좋은 결과는 크게+, 나쁜 결과도 소폭+
                good = +1f; bad = -0.2f; draw = +0.04f;
                break;

            case Personality.비관적인:
                // 좋은 결과도 소폭-, 나쁜 결과는 크게-
                good = +0.2f; bad = -1f; draw = -0.04f;
                break;

            case Personality.제멋대로:
                // 결과 무관하게 랜덤 [-3, +3]
                data.condition = Mathf.Clamp(data.condition + UnityEngine.Random.Range(-1f, 1f), 0f, 100f);
                return;

            case Personality.감정적인:
                {
                    int diff = Mathf.Abs(selfLife - oppLife);
                    bool bigGap = diff >= 5;
                    if (bigGap) { good = +1f; bad = -1f; draw = 0f; } // 과몰입 모드
                    else        { good = +0.2f; bad = -0.2f; draw = 0f;   } // 냉소 모드
                }
                break;
        }

        float delta = (sign == 0) ? draw : (sign > 0 ? good : bad);
        data.condition = Mathf.Clamp(data.condition + delta, 0f, 100f);
    }
}