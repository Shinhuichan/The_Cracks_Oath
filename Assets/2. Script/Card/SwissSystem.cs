// SwissSystem.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 스위스 시스템 전용 매니저.
/// - 승 3 / 무 1 / 패 0 / 부전승 3
/// - 라운드마다 동점 그룹 내에서 우선 매칭
/// - 재대전 회피(불가 시 최소한으로 허용)
/// - 부전승은 라운드당 최대 1명, 동일 참가자 중복 금지
/// - Buchholz(상대 점수 합) 타이브레이커
/// </summary>
public class SwissSystem : MonoBehaviour
{
    // ======== 데이터 구조 ========
    [Serializable]
    public class PlayerEntry
    {
        public string name;
        public int score;                        // 누적 점수
        public List<string> opponents = new();   // 대전 이력(이름)
        public bool hadBye;                      // 부전승 경험 여부
        public int buchholz;                     // 타이브레이커(상대 점수 합)
        public int seed;                         // 초기 무작위 시드(안정 정렬용)

        public PlayerEntry(string name, int seed)
        {
            this.name = name;
            this.seed = seed;
        }
    }

    [Serializable]
    public class Pairing
    {
        public int id;
        public string A;
        public string B;     // bye면 null
        public bool bye;
    }

    public enum MatchResult { AWins, Draw, BWins }

    // ======== 인스펙터 입력 ========
    [Header("입력")]
    [Tooltip("참가자 이름 목록")]
    public List<string> participants = new();

    [Header("옵션")]
    [Tooltip("고정 시드 사용 여부(재현 가능한 페어링)")]
    public bool useFixedSeed = false;
    [Tooltip("고정 시드 값")]
    public int fixedSeed = 123456;

    // ======== 진행 상태(읽기 전용) ========
    [Header("진행 상태")]
    [SerializeField] private int currentRound = 0;
    [SerializeField] private List<PlayerEntry> table = new();     // 전체 참가자 테이블
    [SerializeField] private List<List<Pairing>> rounds = new();  // 라운드별 페어링 기록

    // 점수 규칙(필요 시 상수만 바꾸면 됨)
    public const int WinPts = 3;
    public const int DrawPts = 1;
    public const int LossPts = 0;
    public const int ByePts  = 3;

    System.Random rng;

    // ======== 초기화 ========
    [ContextMenu("Initialize Swiss")]
    public void InitializeSwiss(int? seed = null)
    {
        int s = useFixedSeed ? fixedSeed : (seed ?? UnityEngine.Random.Range(int.MinValue, int.MaxValue));
        rng = new System.Random(s);

        currentRound = 0;
        rounds.Clear();

        table = participants
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .Select(n => new PlayerEntry(n, rng.Next()))
            .ToList();

        RecomputeBuchholz(); // 전원 0으로 시작
    }

    // ======== 다음 라운드 페어링 생성 ========
    [ContextMenu("Generate Next Round")]
    public List<Pairing> GenerateNextRound()
    {
        if (table == null || table.Count == 0)
            InitializeSwiss();

        // 점수 ↓, 부흐홀츠 ↓, seed ↑ 로 정렬 → 점수 버킷 생성
        var buckets = table
            .OrderByDescending(p => p.score)
            .ThenByDescending(p => p.buchholz)
            .ThenBy(p => p.seed)
            .GroupBy(p => p.score)
            .OrderByDescending(g => g.Key)
            .Select(g => g.ToList())
            .ToList();

        List<PlayerEntry> carry = new(); // 아래 점수대로 내려보낼 대기열
        List<Pairing> pairings = new();
        int pid = 0;

        for (int bi = 0; bi < buckets.Count; bi++)
        {
            // 위에서 내려온 사람 합류
            if (carry.Count > 0)
            {
                buckets[bi].AddRange(carry);
                carry.Clear();
            }

            // 버킷 내부 무작위 섞기(같은 점수대에서 랜덤 매칭 시작점)
            Shuffle(buckets[bi]);

            var unpaired = new List<PlayerEntry>(buckets[bi]);

            while (unpaired.Count >= 2)
            {
                var a = unpaired[0];
                var b = FindOpponentAvoidingRematch(a, unpaired);

                if (b == null)
                {
                    // 재대전 회피 불가 → 다음 버킷으로 a를 내려보냄
                    carry.Add(a);
                    unpaired.RemoveAt(0);
                    continue;
                }

                pairings.Add(new Pairing { id = pid++, A = a.name, B = b.name, bye = false });
                unpaired.Remove(a);
                unpaired.Remove(b);
            }

            // 1명 남으면 다음 버킷으로 이동
            if (unpaired.Count == 1)
                carry.Add(unpaired[0]);
        }

        // 최종적으로 1명 남았으면 부전승
        if (carry.Count == 1)
        {
            var byeTarget = carry[0];

            // 이미 bye를 받은 적이 있다면 가능한 한 다른 사람으로 교체
            if (byeTarget.hadBye)
            {
                var swap = table.FirstOrDefault(p => !p.hadBye && p != byeTarget);
                if (swap != null) byeTarget = swap;
            }

            pairings.Add(new Pairing { id = pid++, A = byeTarget.name, B = null, bye = true });
        }

        rounds.Add(pairings);
        currentRound++;
        return pairings;
    }

    // ======== 결과 입력 ========
    public void ReportResult(int roundIndex, int pairingId, MatchResult result)
    {
        if (roundIndex < 0 || roundIndex >= rounds.Count)
        {
            Debug.LogError($"[Swiss] 잘못된 roundIndex: {roundIndex}");
            return;
        }

        var list = rounds[roundIndex];
        var pairing = list.FirstOrDefault(p => p.id == pairingId);
        if (pairing == null)
        {
            Debug.LogError($"[Swiss] 페어링 ID({pairingId})를 찾을 수 없음");
            return;
        }

        if (pairing.bye)
        {
            var P = table.First(x => x.name == pairing.A);
            P.score += ByePts;
            P.hadBye = true;
            // bye는 opponents 기록 없음
        }
        else
        {
            var A = table.First(x => x.name == pairing.A);
            var B = table.First(x => x.name == pairing.B);

            // 대전 이력 기록
            if (!A.opponents.Contains(B.name)) A.opponents.Add(B.name);
            if (!B.opponents.Contains(A.name)) B.opponents.Add(A.name);

            switch (result)
            {
                case MatchResult.AWins:
                    A.score += WinPts; B.score += LossPts;
                    break;
                case MatchResult.BWins:
                    B.score += WinPts; A.score += LossPts;
                    break;
                case MatchResult.Draw:
                    A.score += DrawPts; B.score += DrawPts;
                    break;
            }
        }

        RecomputeBuchholz();
    }

    // ======== 순위표 조회 ========
    public List<PlayerEntry> GetStandings()
    {
        // 점수 ↓, 부흐홀츠 ↓, seed ↑
        return table
            .OrderByDescending(p => p.score)
            .ThenByDescending(p => p.buchholz)
            .ThenBy(p => p.seed)
            .ToList();
    }

    // ======== 내부 도우미 ========
    PlayerEntry FindOpponentAvoidingRematch(PlayerEntry a, List<PlayerEntry> pool)
    {
        // 자신 제외
        var candidates = pool.Where(p => !ReferenceEquals(p, a)).ToList();

        // 1) 아직 붙지 않은 상대 우선
        var fresh = candidates.Where(p => !a.opponents.Contains(p.name)).ToList();
        if (fresh.Count > 0)
        {
            Shuffle(fresh);
            return fresh[0];
        }

        // 2) 불가피하면 재대전 허용
        if (candidates.Count > 0)
        {
            Shuffle(candidates);
            return candidates[0];
        }

        return null;
    }

    void RecomputeBuchholz()
    {
        // 상대 점수 합으로 Buchholz 재계산
        var scoreMap = table.ToDictionary(p => p.name, p => p.score);
        foreach (var p in table)
            p.buchholz = p.opponents.Sum(o => scoreMap.TryGetValue(o, out var s) ? s : 0);
    }

    void Shuffle<T>(IList<T> list)
    {
        if (list == null) return;
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ======== 사용 예시 ========
    // 1) InitializeSwiss()
    // 2) var ps = GenerateNextRound();
    //    ps의 각 매치가 끝날 때마다 ReportResult(roundIndex, pairingId, 결과)
    // 3) GetStandings()로 순위표 확인
}