// File: TournamentRunner.cs
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using GameCore;

[RequireComponent(typeof(CardSystem))]
public class TournamentRunner : MonoBehaviour
{
    public int gamesPerPair = 100;
    public int roundsPerGame = 10;

    CardSystem sys;

    string[] roster = new[]{
        "김현수","이수진","최용호","한지혜","박민재",
        "정다은","오태훈","유민정","김태양","류승우",
        "백무적","이하린"
    };

    void Awake() { sys = GetComponent<CardSystem>(); }

    IEnumerator Start()
    {
        yield return null; // CardSystem.Start 보장
        var alive = new List<string>(roster);
        var elim  = new List<string>();      // 탈락 순서(꼴등 -> 준우승 직전)

        int stage = 1;
        while (alive.Count > 1)
        {
            var score = new Dictionary<string, int>();
            foreach (var n in alive) score[n] = 0;

            // 라운드 로빈
            for (int i = 0; i < alive.Count; i++)
                for (int j = i + 1; j < alive.Count; j++)
                {
                    var a = alive[i]; var b = alive[j];
                    var s = PlayPair(a, b);
                    score[a] += s.a; score[b] += s.b;
                    yield return null;
                }

            // 최하위 탈락
            string loser = null; int min = int.MaxValue;
            foreach (var kv in score) if (kv.Value < min) { min = kv.Value; loser = kv.Key; }

            Debug.Log($"=== Stage {stage} 결과 ===");
            foreach (var kv in SortDesc(score)) Debug.Log($"{kv.Key}: {kv.Value}");
            Debug.Log($"탈락: {loser}");

            elim.Add(loser);
            alive.Remove(loser);
            stage++;
            yield return null;
        }

        // 최종 순위: 1위=우승자, 이후는 역탈락 순서
        var ranking = new List<string>(alive); // winner only
        for (int i = elim.Count - 1; i >= 0; i--) ranking.Add(elim[i]);

        Debug.Log("=== 최종 순위 ===");
        for (int i = 0; i < ranking.Count; i++)
            Debug.Log($"{i + 1}위. {ranking[i]}");

        Debug.Log($"우승: {alive[0]}");
    }

    // a–b 매치업
    (int a, int b) PlayPair(string A, string B)
    {
        int sa = 0, sb = 0;
        for (int g = 0; g < gamesPerPair; g++)
        {
            ResetSystem(seed: (A.GetHashCode() ^ B.GetHashCode() ^ g));

            var p1 = AgentFactory.Create(A);
            var p2 = AgentFactory.Create(B);

            var c1 = NewCtx(sys.startLife);
            var c2 = NewCtx(sys.startLife);

            int r = 1;
            while (r <= roundsPerGame && !sys.playerILost && !sys.playerIILost)
            {
                Debug.Log($"[G{g + 1} R{r}] {A} Hand: {string.Join(",", sys.playerIHands)} | {B} Hand: {string.Join(",", sys.playerIIHands)}");

                var unseen1 = BuildUnseen(true);
                var unseen2 = BuildUnseen(false);

                var pick1 = p1.Choose(new DecisionInput(sys.playerIHands,  c1, unseen1));
                var pick2 = p2.Choose(new DecisionInput(sys.playerIIHands, c2, unseen2));
                int i1 = IndexOf(sys.playerIHands,  pick1); if (i1 < 0) i1 = 0;
                int i2 = IndexOf(sys.playerIIHands, pick2); if (i2 < 0) i2 = 0;

                Debug.Log($"[G{g + 1} R{r}] {A}:{pick1} vs {B}:{pick2}");
                sys.ResolveRoundByIndex(i1, i2);

                c1.last3Opp = c1.last2Opp; c1.last2Opp = c1.lastOpp; c1.lastOpp = pick2; c1.lastSelf = pick1;
                c2.last3Opp = c2.last2Opp; c2.last2Opp = c2.lastOpp; c2.lastOpp = pick1; c2.lastSelf = pick2;
                c1.selfLife = sys.playerILife;  c1.oppLife = sys.playerIILife; c1.round++;
                c2.selfLife = sys.playerIILife; c2.oppLife = sys.playerILife;  c2.round++;
                r++;
            }

            if (sys.playerILost && sys.playerIILost) { sa += 1; sb += 1; }
            else if (sys.playerILost) { sb += 2; }
            else if (sys.playerIILost) { sa += 2; }
            else
            {
                if (sys.playerILife > sys.playerIILife) sa += 2;
                else if (sys.playerILife < sys.playerIILife) sb += 2;
                else { sa += 1; sb += 1; }
            }
        }
        return (sa, sb);
    }

    // ---------- 보조 ----------
    void ResetSystem(int seed)
    {
        Random.InitState(seed);
        sys.publicDeck.Clear(); sys.playerIHands.Clear(); sys.playerIIHands.Clear(); sys.discardCards.Clear();
        sys.ClearLoseFlags();

        Add(sys.publicDeck, CardType.Cooperation, sys.cooperationCount);
        Add(sys.publicDeck, CardType.Doubt,       sys.doubtCount);
        Add(sys.publicDeck, CardType.Betrayal,    sys.betrayalCount);
        Add(sys.publicDeck, CardType.Chaos,       sys.chaosCount);
        Add(sys.publicDeck, CardType.Pollution,   sys.pollutionCount);
        sys.publicDeck.Shuffle();

        Draw(sys.publicDeck, sys.playerIHands,  sys.startingHand);
        Draw(sys.publicDeck, sys.playerIIHands, sys.startingHand);
        sys.playerILife  = sys.startLife;
        sys.playerIILife = sys.startLife;
    }

    RoundCtx NewCtx(int life) => new RoundCtx
    {
        round = 1, selfLife = life, oppLife = life,
        lastSelf = CardType.None, lastOpp = CardType.None,
        last2Opp = CardType.None, last3Opp = CardType.None
    };

    IReadOnlyDictionary<CardType,int> BuildUnseen(bool forP1)
    {
        var map = new Dictionary<CardType,int>{
            {CardType.Cooperation,0},{CardType.Doubt,0},{CardType.Betrayal,0},
            {CardType.Chaos,0},{CardType.Pollution,0}
        };
        void Acc(List<CardType> src){ foreach(var c in src) map[c] = map[c] + 1; }
        Acc(sys.publicDeck);
        Acc(forP1 ? sys.playerIIHands : sys.playerIHands);
        return map;
    }

    static int IndexOf(List<CardType> hand, CardType t){ for(int i=0;i<hand.Count;i++) if(hand[i]==t) return i; return -1; }
    static void Add(List<CardType> l, CardType t, int n){ for(int i=0;i<n;i++) l.Add(t); }
    static CardType DrawOne(List<CardType> d){ var c=d[0]; d.RemoveAt(0); return c; }
    static void Draw(List<CardType> d, List<CardType> h, int n){ for(int i=0;i<n;i++) h.Add(DrawOne(d)); }

    static IEnumerable<KeyValuePair<string,int>> SortDesc(Dictionary<string,int> m)
    {
        var list = new List<KeyValuePair<string,int>>(m);
        list.Sort((a,b)=>b.Value.CompareTo(a.Value));
        return list;
    }
}