// File: MatchRunner.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameCore;

[RequireComponent(typeof(CardSystem))]
public class MatchRunner : MonoBehaviour
{
    public enum Mode { Single, Matchup, Survival }

    [Header("공통")]
    public Mode mode = Mode.Single;
    public int battleCount = 100;
    public List<string> players = new()
    {
        "김현수","이수진","최용호","한지혜","박민재","정다은","오태훈","유민정","김태양","류승우"
    };

    [Header("단판제")]
    public string p1Name = "김현수";
    public string p2Name = "최용호";
    public int singleMaxRounds = 10;

    [Header("서바이벌")]
    public int survivalMaxRounds = 10; // 서바이벌에서 한 경기 최대 라운드

    CardSystem sys;
    Agent p1, p2;
    RoundCtx ctx1, ctx2;

    // 코루틴 경기 결과 보관용
    struct MatchResult { public int p1Pts, p2Pts; public int rounds; }
    MatchResult lastResult;

    void Awake(){ sys = GetComponent<CardSystem>(); }

    IEnumerator Start()
    {
        yield return EnsureFreshSystem();

        switch (mode)
        {
            case Mode.Single:
                yield return RunSingle(p1Name, p2Name, singleMaxRounds);
                break;
            case Mode.Matchup:
                yield return RunMatchup(players, battleCount);
                break;
            case Mode.Survival:
                yield return RunSurvival(players, battleCount);
                break;
        }
    }

    // -------- 모드 구현 --------

    IEnumerator RunSingle(string a, string b, int maxRounds)
    {
        p1 = AgentFactory.Create(a);
        p2 = AgentFactory.Create(b);
        InitContexts();

        LogHands(0);
        yield return StartCoroutine(PlayOneMatch(maxRounds)); // 결과는 lastResult에 저장됨
    }

    IEnumerator RunMatchup(List<string> names, int count)
    {
        var score     = names.ToDictionary(n => n, _ => 0);
        var roundSum  = names.ToDictionary(n => n, _ => 0);
        var roundGame = names.ToDictionary(n => n, _ => 0);

        for (int i = 0; i < names.Count; i++)
        for (int j = i + 1; j < names.Count; j++)
        {
            for (int k = 0; k < count; k++)
            {
                yield return EnsureFreshSystem();
                string A = names[i], B = names[j];
                p1 = AgentFactory.Create(A);
                p2 = AgentFactory.Create(B);
                InitContexts();

                yield return StartCoroutine(PlayOneMatch(singleMaxRounds));
                var r = lastResult;

                score[A] += r.p1Pts; score[B] += r.p2Pts;
                roundSum[A] += r.rounds; roundGame[A] += 1;
                roundSum[B] += r.rounds; roundGame[B] += 1;
            }
        }

        var ranking = names.Select(n => new {
                name = n,
                pts  = score[n],
                avgR = roundGame[n] > 0 ? (float)roundSum[n] / roundGame[n] : 0f
            })
            .OrderByDescending(x => x.pts)
            .ThenBy(x => x.avgR)
            .ToList();

        Debug.Log("=== 매치업 결과 ===");
        for (int i = 0; i < ranking.Count; i++)
            Debug.Log($"{i + 1}위. {ranking[i].name} | 승점 {ranking[i].pts} | 평균 라운드 {ranking[i].avgR:F2}");
    }

    // ---- Survival 모드 구현 교체 ----
    IEnumerator RunSurvival(List<string> seed, int countPerPair /*미사용: 유지 호환*/)
    {
        var alive = new List<string>(seed);
        var eliminated = new List<string>();

        while (alive.Count > 1)
        {
            var score = alive.ToDictionary(n => n, _ => 0);

            // 현 참가자들 round-robin, 각 조합을 battleCount번만 진행
            for (int i = 0; i < alive.Count; i++)
            for (int j = i + 1; j < alive.Count; j++)
            {
                for (int k = 0; k < battleCount; k++)   // ← 판 수를 battleCount로 고정
                {
                    yield return EnsureFreshSystem();

                    string A = alive[i], B = alive[j];
                    p1 = AgentFactory.Create(A);
                    p2 = AgentFactory.Create(B);
                    InitContexts();

                    // 서바이벌 전용 라운드 제한 사용
                    yield return StartCoroutine(PlayOneMatch(survivalMaxRounds));
                    var r = lastResult;
                    score[A] += r.p1Pts; score[B] += r.p2Pts;
                }
                yield return null; // 긴 반복에서 한숨 돌림
            }

            // 최하위 탈락(동점 시 이름 사전순 뒤쪽 탈락)
            var last = score.OrderBy(kv => kv.Value)
                            .ThenByDescending(kv => kv.Key).First().Key;
            eliminated.Add(last);
            alive.Remove(last);
            Debug.Log($"탈락: {last} | 남은 인원 {alive.Count}");
        }

        // 최종 순위 로그 (정방향)
        Debug.Log("=== 최종 순위 ===");
        var finalOrder = new List<string>();
        finalOrder.Add(alive[0]);                                 // 1위 = 마지막까지 살아남은 사람
        finalOrder.AddRange(eliminated.AsEnumerable().Reverse()); // 2위~ = 마지막 탈락자 → 최초 탈락자

        for (int i = 0; i < finalOrder.Count; i++)
            Debug.Log($"{i + 1}위. {finalOrder[i]}");

        Debug.Log($"우승: {alive[0]}");
    }

    // -------- 한 경기 --------

    IEnumerator PlayOneMatch(int maxRounds)
    {
        int round = 1;

        while (round <= maxRounds && !sys.playerILost && !sys.playerIILost)
        {
            var unseen1 = BuildUnseen(true);
            var unseen2 = BuildUnseen(false);

            var pick1 = p1.Choose(new DecisionInput(sys.playerIHands,  ctx1, unseen1));
            var pick2 = p2.Choose(new DecisionInput(sys.playerIIHands, ctx2, unseen2));

            int i1 = IndexOfType(sys.playerIHands, pick1);
            int i2 = IndexOfType(sys.playerIIHands, pick2);
            if (i1 < 0 || i2 < 0) { Debug.LogWarning("Empty hand."); break; }

            Debug.Log($"[R{round}] Pick → {p1.name}:{pick1} | {p2.name}:{pick2}");
            sys.ResolveRoundByIndex(i1, i2);

            ctx1.last3Opp = ctx1.last2Opp; ctx2.last3Opp = ctx2.last2Opp;
            ctx1.last2Opp = ctx1.lastOpp;  ctx2.last2Opp = ctx2.lastOpp;
            ctx1.lastOpp  = pick2;         ctx2.lastOpp  = pick1;
            ctx1.lastSelf = pick1;         ctx2.lastSelf = pick2;

            ctx1.selfLife = sys.playerILife;  ctx1.oppLife = sys.playerIILife;
            ctx2.selfLife = sys.playerIILife; ctx2.oppLife = sys.playerILife;

            LogHands(round);
            round++;
            ctx1.round = round; ctx2.round = round;

            yield return null;
        }

        int p1Pts = 0, p2Pts = 0;
        if (sys.playerILost && sys.playerIILost) { p1Pts = p2Pts = 1; }
        else if (sys.playerILost) { p2Pts = 2; }
        else if (sys.playerIILost) { p1Pts = 2; }
        else
        {
            if (sys.playerILife > sys.playerIILife) p1Pts = 2;
            else if (sys.playerILife < sys.playerIILife) p2Pts = 2;
            else { p1Pts = p2Pts = 1; }
        }

        string result =
            sys.playerILost && sys.playerIILost ? "무승부(동반 탈락)" :
            sys.playerILost ? $"{p2.name} 승" :
            sys.playerIILost ? $"{p1.name} 승" :
            (p1Pts == p2Pts ? "무승부(판정)" :
             p1Pts > p2Pts ? $"{p1.name} 승(판정)" : $"{p2.name} 승(판정)");

        Debug.Log($"=== Match End: {result} | Rounds:{round - 1} | {p1.name} HP:{sys.playerILife} vs {p2.name} HP:{sys.playerIILife} ===");

        lastResult = new MatchResult { p1Pts = p1Pts, p2Pts = p2Pts, rounds = round - 1 };
        yield break;
    }

    // -------- 유틸 --------

    void InitContexts()
    {
        ctx1 = new RoundCtx
        {
            round = 1,
            selfLife = sys.startLife,
            oppLife = sys.startLife,
            lastSelf = CardType.None,
            lastOpp  = CardType.None,
            last2Opp = CardType.None,
            last3Opp = CardType.None
        };
        ctx2 = ctx1;
        LogHands(0);
    }

    IEnumerator EnsureFreshSystem()
    {
        if (sys != null) Destroy(sys);
        sys = gameObject.AddComponent<CardSystem>();
        yield return null;
        yield return new WaitUntil(() => sys.publicDeck != null && sys.publicDeck.Count > 0);
    }

    IReadOnlyDictionary<CardType,int> BuildUnseen(bool forP1)
    {
        var map = new Dictionary<CardType,int>();
        void Acc(IEnumerable<CardType> src)
        {
            foreach (var c in src)
            {
                if (c == CardType.None) continue;
                map[c] = map.ContainsKey(c) ? map[c] + 1 : 1;
            }
        }
        Acc(sys.publicDeck);
        Acc(forP1 ? sys.playerIIHands : sys.playerIHands);
        foreach (CardType t in System.Enum.GetValues(typeof(CardType)))
            if (t != CardType.None && !map.ContainsKey(t)) map[t] = 0;
        return map;
    }

    int IndexOfType(List<CardType> hand, CardType t)
    {
        for (int i = 0; i < hand.Count; i++) if (hand[i] == t) return i;
        return -1;
    }

    void LogHands(int r)
    {
        string H(List<CardType> h) => string.Join(", ", h.Select(x => x.ToString()));
        Debug.Log($"[Hand R{r}] P1({(p1!=null?p1.name:"-")}): [{H(sys.playerIHands)}] | P2({(p2!=null?p2.name:"-")}): [{H(sys.playerIIHands)}]");
    }
}