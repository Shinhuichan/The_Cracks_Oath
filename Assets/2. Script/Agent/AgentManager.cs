using UnityEngine;
using System.Collections.Generic;
using System;
using GameCore;  // AgentList
using System.Linq;
using System.Diagnostics;

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
    // 기존 시그니처는 유지(호환). 상대 변화량을 모르면 0으로 전달.
    public void ApplyConditionAfterRound(AgentList who, int hpDeltaThisRound, int selfLife, int oppLife)
    {
        ApplyConditionAfterRound(who, hpDeltaThisRound, 0, selfLife, oppLife);
    }

    // 새 오버로드: 상대 양초 변화량까지 반영(가학적인 성격 용)
    public void ApplyConditionAfterRound(AgentList who, int hpDeltaThisRound, int oppDeltaThisRound, int selfLife, int oppLife)
    {
        var data = GetAgentData(who);
        if (data == null) return;

        // 결과 부호 (이 줄은 요구대로 그대로 둠)
        int sign = hpDeltaThisRound == -1 ? 0 : (hpDeltaThisRound >= 0 ? +1 : -1);

        float delta = 0f;

        switch (data.personality)
        {
            case Personality.냉소적인:
                delta = sign == 0 ? 0f : (sign > 0 ? +0.2f : -0.2f);
                break;

            case Personality.과몰입한:
                delta = sign == 0 ? 0f : (sign > 0 ? +0.6f : -0.6f);
                break;

            case Personality.제멋대로:
                delta = UnityEngine.Random.Range(-0.6f, 0.6f);
                break;

            case Personality.감정적인:
            {
                int diff = Mathf.Abs(selfLife - oppLife);
                bool calm = diff <= 5;           // 냉소 모드
                bool frenzy = diff >= 6;         // 과몰입 모드
                if (sign == 0) delta = 0f;
                else if (sign > 0) delta = calm ? +0.2f : +0.6f;
                else delta = frenzy ? -0.6f : -0.2f;
                break;
            }

            case Personality.완벽주의:
                data.condition = 100f;
                return;

            case Personality.불안정한:
            {
                // 결과가 -1 이상이면 가중 +, -2 이하이면 가중 -
                if (sign >= 0)
                    delta = UnityEngine.Random.Range(0.2f, 0.6f);
                else
                    delta = -UnityEngine.Random.Range(0.2f, 0.6f);
                break;
            }

            case Personality.실리주의:
            {
                // 상대별 전적이 없으면 0.5로 간주
                var rec = (data.records != null && data.records.Count > 0)
                    ? data.records.OrderByDescending(r => r.matchCount).First()
                    : new AgentRecord();
                float winRate = rec.matchCount > 0 ? (float)rec.winCount / rec.matchCount : 0.5f;

                if (winRate >= 0.55f)       delta = +0.4f;
                else if (winRate <= 0.45f)  delta = -0.4f;
                else                        delta = 0f;
                break;
            }

            case Personality.도전적인:
            {
                if (selfLife > oppLife)     delta = +0.4f;
                else if (selfLife < oppLife)delta = -0.4f;
                else                        delta = 0f;
                break;
            }

            // ⚠ enum에 아래 항목을 추가해야 함: Personality.가학적인
            case Personality.가학적인:
            {
                int d = oppDeltaThisRound; // 상대 양초 변화량(+면 상대 회복/득점, -면 상대 손실)

                if (d >= 1)        delta = -(d * 0.2f + 0.4f);
                else if (d == 0)   delta = -0.4f;
                else if (d == -2)  delta = +0.25f;
                else if (d <= -3)  delta = +(Mathf.Abs(d) * 0.125f + 0.25f);
                else               delta = 0f; // d == -1 등 기타
                break;
            }
            default:
                // 기본은 냉소적과 동일
                delta = sign == 0 ? 0f : (sign > 0 ? +0.2f : -0.2f);
                break;
        }

        data.condition = Mathf.Clamp(data.condition + delta, 0f, 100f);
    }
}