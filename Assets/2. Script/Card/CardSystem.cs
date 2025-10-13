// File: GameCore.cs
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

namespace GameCore
{
    // ===== 공용 타입 =====
    public enum CardType { None = 0, Cooperation, Doubt, Betrayal, Chaos, Pollution }

    // ===== 라운드 컨텍스트 =====
    [Serializable]
    public struct RoundCtx
    {
        public int round;
        public int selfLife, oppLife;
        public CardType lastSelf, lastOpp;   // t-1
        public CardType last2Opp;            // t-2
        public CardType last3Opp;            // t-3

        public bool IsFirst => round == 1;
        public bool IsEarly(int r) => round <= r;

        public bool OppNoDoubtInLast3()
            => lastOpp != CardType.Doubt
            && last2Opp != CardType.Doubt
            && last3Opp != CardType.Doubt;
    }

    // ===== 규칙 입력 =====
    public readonly struct DecisionInput
    {
        public readonly List<CardType> hand;
        public readonly RoundCtx s;

        // “아직 확인 못 한 카드” = 덱 + 상대 패
        public readonly IReadOnlyDictionary<CardType, int> unseen;
        public readonly int unseenTotal;

        public DecisionInput(List<CardType> hand, RoundCtx s,
                             IReadOnlyDictionary<CardType, int> unseen)
        {
            this.hand = hand;
            this.s = s;
            this.unseen = unseen ?? EmptyCounts;
            this.unseenTotal = this.unseen.Values.Sum();
        }

        static readonly IReadOnlyDictionary<CardType, int> EmptyCounts =
            new Dictionary<CardType, int>
            {
                {CardType.Cooperation,0},{CardType.Doubt,0},{CardType.Betrayal,0},
                {CardType.Chaos,0},{CardType.Pollution,0}
            };

        public bool HandHas(CardType t, int n = 1) => hand.Count(x => x == t) >= n;
        public CardType FirstOrNone() => hand.Count > 0 ? hand[0] : CardType.None;
        public float Ratio(CardType t)
        {
            if (unseenTotal == 0) return 0f;
            return unseen.TryGetValue(t, out var n) ? (float)n / unseenTotal : 0f;
        }
    }


    // ===== 에이전트 =====
    public sealed class Agent
    {
        public string name;
        public List<Func<DecisionInput, CardType?>> rules = new();
        public CardType[] fallback =
            { CardType.Cooperation, CardType.Doubt, CardType.Pollution, CardType.Betrayal, CardType.Chaos };

        public Agent(string name){ this.name = name; }

        public CardType Choose(DecisionInput I)
        {
            foreach (var r in rules)
            {
                var pick = r(I);
                if (pick.HasValue) return pick.Value;
            }
            foreach (var t in fallback)
                if (I.HandHas(t)) return t;
            return I.FirstOrNone();
        }
    }

    // ===== 에이전트 팩토리(최신 10인 로직) =====
    public static class AgentFactory
    {
        public static Agent Create(string who) => who switch
        {
            "김현수" => Build_김현수(),
            "이수진" => Build_이수진(),
            "최용호" => Build_최용호(),
            "한지혜" => Build_한지혜(),
            "박민재" => Build_박민재(),
            "정다은" => Build_정다은(),
            "오태훈" => Build_오태훈(),
            "유민정" => Build_유민정(),
            "김태양" => Build_김태양(),
            "백무적" => Build_백무적(),
            "이하린" => Build_이하린(),
            _ => Build_Default(who),
        };

        static Agent Build_Default(string name)
        {
            var A = new Agent(name);
            A.fallback = new[] { CardType.Cooperation, CardType.Doubt, CardType.Pollution, CardType.Betrayal, CardType.Chaos };
            return A;
        }

        // 김현수
        static Agent Build_김현수()
        {
            var A = new Agent("김현수");
            A.rules.Add(I => I.s.round <= 2 && I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => I.s.selfLife <= 4 && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.selfLife < I.s.oppLife && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.selfLife >= I.s.oppLife && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.fallback = new[] { CardType.Cooperation, CardType.Doubt, CardType.Pollution, CardType.Betrayal, CardType.Chaos };
            return A;
        }

        // 이수진
        static Agent Build_이수진()
        {
            var A = new Agent("이수진");
            A.rules.Add(I => I.s.round <= 3 && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.s.round >= 3 && I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => I.hand.Count(t => t == CardType.Cooperation) >= 2 && (I.s.selfLife <= 4 || I.s.round >= 7) && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => I.s.selfLife >= I.s.oppLife && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.fallback = new[] { CardType.Pollution, CardType.Betrayal, CardType.Doubt, CardType.Cooperation, CardType.Chaos };
            return A;
        }

        // 최용호
        static Agent Build_최용호()
        {
            var A = new Agent("최용호");
            A.rules.Add(I => I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.HandHas(CardType.Doubt) && I.HandHas(CardType.Cooperation) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => I.s.selfLife <= 4 && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.fallback = new[] { CardType.Chaos, CardType.Cooperation, CardType.Pollution, CardType.Doubt, CardType.Betrayal };
            return A;
        }

        // 한지혜
        static Agent Build_한지혜()
        {
            var A = new Agent("한지혜");
            A.rules.Add(I => I.s.round <= 2 && I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.selfLife >= I.s.oppLife && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.s.selfLife <= 4 && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => I.s.round >= 9 && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.fallback = new[] { CardType.Cooperation, CardType.Doubt, CardType.Pollution, CardType.Betrayal, CardType.Chaos };
            return A;
        }

        // 박민재
        static Agent Build_박민재()
        {
            var A = new Agent("박민재");
            A.rules.Add(I => I.Ratio(CardType.Cooperation) >= 0.33f && I.HandHas(CardType.Doubt) && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp != CardType.Pollution && I.Ratio(CardType.Pollution) >= 0.16f && I.HandHas(CardType.Doubt) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && (I.Ratio(CardType.Cooperation) >= 0.33f || I.s.lastOpp == CardType.Chaos) && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.Ratio(CardType.Doubt) >= 0.33f && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => (I.s.round % 3 == 0 || I.s.selfLife >= 2) && !I.HandHas(CardType.Betrayal) && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => I.s.round >= 3 && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.fallback = new[] { CardType.Betrayal, CardType.Pollution, CardType.Cooperation, CardType.Doubt, CardType.Chaos };
            return A;
        }

        // 정다은
        static Agent Build_정다은()
        {
            var A = new Agent("정다은");
            A.rules.Add(I => I.s.round == 1 && I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => I.s.round >= 4
                              && I.s.OppNoDoubtInLast3()
                              && I.s.selfLife >= I.s.oppLife
                              && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.HandHas(I.s.lastOpp) ? I.s.lastOpp : (CardType?)null); // 미러
            A.rules.Add(I => ((I.s.round % 3 == 0) || I.s.selfLife >= 2) && !I.HandHas(CardType.Betrayal) && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.selfLife >= I.s.oppLife && I.s.lastOpp != CardType.Doubt && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.fallback = new[] { CardType.Pollution, CardType.Doubt, CardType.Cooperation, CardType.Betrayal, CardType.Chaos };
            return A;
        }

        // 오태훈
        static Agent Build_오태훈()
        {
            var A = new Agent("오태훈");
            A.rules.Add(I => I.s.round <= 2 && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => I.Ratio(CardType.Cooperation) >= 0.50f && I.s.selfLife >= I.s.oppLife - 1 && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.s.round >= 9 && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => I.s.round >= 6 && I.s.lastOpp != I.s.last2Opp && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.fallback = new[] { CardType.Doubt, CardType.Cooperation, CardType.Pollution, CardType.Betrayal, CardType.Chaos };
            return A;
        }

        // 유민정
        static Agent Build_유민정()
        {
            var A = new Agent("유민정");
            A.rules.Add(I => !I.s.IsFirst && (I.s.lastOpp == CardType.Pollution || I.s.lastOpp == CardType.Betrayal) && I.HandHas(CardType.Doubt) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => I.s.selfLife < I.s.oppLife && I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.selfLife > I.s.oppLife && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.s.round >= 9 && I.s.selfLife >= I.s.oppLife && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => I.s.round >= 6 && I.s.lastOpp != I.s.last2Opp && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.fallback = new[] { CardType.Doubt, CardType.Cooperation, CardType.Pollution, CardType.Betrayal, CardType.Chaos };
            return A;
        }

        // 김태양
        static Agent Build_김태양()
        {
            var A = new Agent("김태양");
            A.rules.Add(I => {
                if (I.s.round == 1 && I.hand.Count > 0)
                {
                    int idx = UnityEngine.Random.Range(0, I.hand.Count);
                    return I.hand[idx];
                }
                return (CardType?)null;
            });
            A.rules.Add(I => I.s.round >= 3 && I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation && I.s.selfLife >= I.s.oppLife && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && (I.s.lastOpp == CardType.Cooperation || I.s.round % 2 == 0) && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.s.round % 3 == 0 && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => {
                if (I.HandHas(CardType.Betrayal) && I.HandHas(CardType.Pollution))
                    return UnityEngine.Random.Range(0, 2) == 0 ? CardType.Betrayal : CardType.Pollution;
                return (CardType?)null;
            });
            A.rules.Add(I => !I.s.IsFirst && I.HandHas(I.s.lastOpp) ? I.s.lastOpp : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.fallback = new[] { CardType.Betrayal, CardType.Pollution, CardType.Doubt, CardType.Cooperation, CardType.Chaos };
            return A;
        }
        static Agent Build_백무적()
        {
            var A = new Agent("백무적");

            A.rules.Add(I =>
            {
                // 1) 상대 액션 분포 추정: “덱 + 상대 패” 비율을 기반으로 시작
                var p = new Dictionary<CardType, float>
                {
                    { CardType.Cooperation, I.Ratio(CardType.Cooperation) },
                    { CardType.Doubt,       I.Ratio(CardType.Doubt)       },
                    { CardType.Betrayal,    I.Ratio(CardType.Betrayal)    },
                    { CardType.Chaos,       I.Ratio(CardType.Chaos)       },
                    { CardType.Pollution,   I.Ratio(CardType.Pollution)   },
                };

                // 2) 최근 패턴 보정
                void Boost(CardType t, float m) { p[t] *= m; }
                if (!I.s.IsFirst)
                {
                    // 연속 Cooperation 유도 → 배신 각 노림
                    if (I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation) Boost(CardType.Cooperation, 1.6f);
                    // 직전 Pollution 반복 경향
                    if (I.s.lastOpp == CardType.Pollution) Boost(CardType.Pollution, 1.5f);
                    // 직전 Doubt 후 재연속 약함
                    if (I.s.lastOpp == CardType.Doubt) Boost(CardType.Doubt, 1.2f);
                    // 라운드 3의 배수에서 혼돈 가중(메타용 미세 보정)
                    if (I.s.round % 3 == 0) Boost(CardType.Chaos, 1.15f);
                }

                // 3) 생존 상태 보정
                if (I.s.oppLife <= 3) { Boost(CardType.Cooperation, 1.15f); Boost(CardType.Pollution, 1.10f); Boost(CardType.Betrayal, 0.85f); }
                if (I.s.selfLife <= 3) { Boost(CardType.Doubt, 1.10f); Boost(CardType.Chaos, 1.10f); }

                // 정규화
                float sum = p.Values.Sum(); if (sum <= 0f) sum = 1f;
                foreach (var k in p.Keys.ToList()) p[k] /= sum;

                // 4) 효과표: Δ = (내 증감 − 상대 증감). 라운드 종료 −1은 상쇄되므로 제외.
                int Delta(CardType a, CardType b)
                {
                    // 표 기반
                    if (a == CardType.Cooperation && b == CardType.Cooperation) return 0;   // +1,+1
                    if (a == CardType.Cooperation && b == CardType.Doubt) return +1;  // +1,0
                    if (a == CardType.Cooperation && b == CardType.Betrayal) return -11; // -10,+1
                    if (a == CardType.Cooperation && b == CardType.Chaos) return +1;  // +1,0(상대 패교체)
                    if (a == CardType.Cooperation && b == CardType.Pollution) return -2;  // -1,+1

                    if (a == CardType.Doubt && b == CardType.Cooperation) return -1;  // 0,+1
                    if (a == CardType.Doubt && b == CardType.Doubt) return 0;   // 0,0
                    if (a == CardType.Doubt && b == CardType.Betrayal) return +11; // +1,-10
                    if (a == CardType.Doubt && b == CardType.Chaos) return 0;   // 0,0(상대 패교체)
                    if (a == CardType.Doubt && b == CardType.Pollution) return +1;  // 0,-1

                    if (a == CardType.Betrayal && b == CardType.Cooperation) return +11; // +1,-10
                    if (a == CardType.Betrayal && b == CardType.Doubt) return -11; // -10,+1
                    if (a == CardType.Betrayal && b == CardType.Betrayal) return 0;   // -10,-10
                    if (a == CardType.Betrayal && b == CardType.Chaos) return +11; // +1,-10
                    if (a == CardType.Betrayal && b == CardType.Pollution) return +11; // +1,-10

                    if (a == CardType.Chaos && b == CardType.Cooperation) return -1;  // 0,+1(자신 패교체)
                    if (a == CardType.Chaos && b == CardType.Doubt) return 0;   // 0,0(자신 패교체)
                    if (a == CardType.Chaos && b == CardType.Betrayal) return -11; // -10,+1(자신 패교체)
                    if (a == CardType.Chaos && b == CardType.Chaos) return 0;   // 0,0(양측 패교체)
                    if (a == CardType.Chaos && b == CardType.Pollution) return 0;   // 0,0(자신 패교체)

                    if (a == CardType.Pollution && b == CardType.Cooperation) return +2;  // +1,-1
                    if (a == CardType.Pollution && b == CardType.Doubt) return -1;  // -1,0
                    if (a == CardType.Pollution && b == CardType.Betrayal) return -11; // -10,+1
                    if (a == CardType.Pollution && b == CardType.Chaos) return 0;   // 0,0(상대 패교체)
                    if (a == CardType.Pollution && b == CardType.Pollution) return 0;   // -1,-1
                    return 0;
                }

                // 5) 손패 품질 점수(혼돈 가치 산출용)
                int HandScore(List<CardType> h)
                {
                    // 안전도 기준: Doubt(대 P 응징) 높음, Cooperation 중간, Pollution 저중, Chaos 낮음, Betrayal 변동 큼
                    int s = 0;
                    foreach (var c in h)
                    {
                        if (c == CardType.Doubt) s += 3;
                        else if (c == CardType.Cooperation) s += 2;
                        else if (c == CardType.Pollution) s += 1;
                        else if (c == CardType.Betrayal) s += 0;      // 상황의존
                        else if (c == CardType.Chaos) s += -1;
                    }
                    return s;
                }

                // 6) 각 후보 카드의 기대값 계산
                var unique = I.hand.Distinct().ToList();
                CardType best = CardType.None; float bestEV = float.NegativeInfinity;

                foreach (var a in unique)
                {
                    // 쓸 수 없는 카드 방지
                    if (!I.HandHas(a)) continue;

                    // 근사 EV
                    float ev = 0f;
                    foreach (var b in p.Keys)
                        ev += p[b] * Delta(a, b);

                    // 생존 리스크 가중치: 내 체력 낮을수록 큰 음수 리스크 억제
                    if (I.s.selfLife <= 3)
                    {
                        // Cooperation이 Betrayal에 터질 확률 차감
                        ev -= p[CardType.Betrayal] * 6f;
                        // Pollution이 Betrayal에 터질 확률 차감
                        if (a == CardType.Pollution) ev -= p[CardType.Betrayal] * 6f;
                    }

                    // 혼돈 보정: 손패가 나쁘면 리롤 가치 +
                    if (a == CardType.Chaos)
                    {
                        int hs = HandScore(I.hand);
                        if (hs <= 2) ev += 3f;          // 패가 쓰레기면 과감히 리롤
                        if (I.s.selfLife <= 3) ev += 1f; // 막판 방어적 리롤 가산
                    }

                    // 막판 킬각 가중: 상대 체력 낮고 Cooperation 확률 높으면 배신 가중
                    if (a == CardType.Betrayal && I.s.oppLife <= 3)
                        ev += p[CardType.Cooperation] * 5f;

                    if (ev > bestEV) { bestEV = ev; best = a; }
                }

                // 7) 안전장치: 배신 리스크 캡
                if (best == CardType.Betrayal && p[CardType.Doubt] >= 0.25f && I.s.selfLife <= 4)
                {
                    // Doubt 확률이 높고 내 체력 낮으면 배신 회피 → 차선책
                    var alt = unique.Where(t => t != CardType.Betrayal).OrderByDescending(t =>
                    {
                        float ev2 = 0f; foreach (var b in p.Keys) ev2 += p[b] * Delta(t, b);
                        return ev2;
                    }).FirstOrDefault();
                    if (alt != CardType.None) best = alt;
                }

                // 8) 소량 랜덤화(읽힘 방지)
                if (UnityEngine.Random.value < 0.08f)
                {
                    var mix = unique.Where(t => t != best).ToList();
                    if (mix.Count > 0) best = mix[UnityEngine.Random.Range(0, mix.Count)];
                }

                return best;
            });
            A.fallback = new[] { CardType.Doubt, CardType.Betrayal, CardType.Cooperation, CardType.Pollution, CardType.Chaos };

            return A;
        }
        static Agent Build_이하린()
        {
            var A = new Agent("이하린");
            A.rules.Add(I => I.s.round == 1 && I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => I.s.round % 2 == 0 && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.HandHas(I.s.lastOpp) ? I.s.lastOpp : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Doubt) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation, 2) && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.fallback = new[] { CardType.Cooperation, CardType.Doubt, CardType.Pollution, CardType.Betrayal, CardType.Chaos };
            return A;
        }
    }

    // ===== 카드 시스템 =====
    public class CardSystem : MonoBehaviour
    {
        public List<CardType> publicDeck = new();
        public List<CardType> playerIHands = new();
        public List<CardType> playerIIHands = new();
        public List<CardType> discardCards = new();

        [Header("덱 구성")]
        public int cooperationCount = 20, doubtCount = 20, betrayalCount = 3, chaosCount = 7, pollutionCount = 10;

        [Header("게임 설정")]
        public int startingHand = 3, startLife = 10;
        public int playerILife, playerIILife;
        public bool playerILost { get; private set; }
        public bool playerIILost { get; private set; }

        struct Effect { public int self, opp; public bool repSelf, repOpp;
            public Effect(int s,int o,bool rs=false,bool ro=false){ self=s; opp=o; repSelf=rs; repOpp=ro; } }
        Dictionary<string, Effect> E;

        void Start()
        {
            Add(publicDeck, CardType.Cooperation, cooperationCount);
            Add(publicDeck, CardType.Doubt,       doubtCount);
            Add(publicDeck, CardType.Betrayal,    betrayalCount);
            Add(publicDeck, CardType.Chaos,       chaosCount);
            Add(publicDeck, CardType.Pollution,   pollutionCount);
            Shuffle(publicDeck);

            Draw(publicDeck, playerIHands,  startingHand);
            Draw(publicDeck, playerIIHands, startingHand);

            playerILife = playerIILife = startLife;
            BuildEffects();
        }

        // 라운드 자동 진행: 에이전트의 무지 모델 = 덱+상대패
        public void ResolveRoundAuto(Agent p1, Agent p2, RoundCtx ctxP1, RoundCtx ctxP2)
        {
            if (playerILost || playerIILost) return;

            var unseenForP1 = BuildUnseen(isP1:true);
            var unseenForP2 = BuildUnseen(isP1:false);

            var pick1 = p1.Choose(new DecisionInput(playerIHands,  ctxP1, unseenForP1));
            var pick2 = p2.Choose(new DecisionInput(playerIIHands, ctxP2, unseenForP2));

            int idx1 = IndexOfFirst(playerIHands, pick1);
            int idx2 = IndexOfFirst(playerIIHands, pick2);
            ResolveRoundByIndex(idx1, idx2);
        }

        // 라운드 처리(인덱스)
        public void ResolveRoundByIndex(int p1Index, int p2Index)
        {
            if (playerILost || playerIILost) return;
            var a = UseCard(playerIHands, p1Index);
            var b = UseCard(playerIIHands, p2Index);
            if (a == CardType.None || b == CardType.None) return;

            var ef = E[$"{a}-{b}"];
            playerILife += ef.self; playerIILife += ef.opp;

            if (ef.repSelf) ReplaceHand(playerIHands);
            if (ef.repOpp) ReplaceHand(playerIIHands);

            // 라운드 종료 -1, 상한 = startLife
            playerILife--; playerIILife--;
            playerILife = Mathf.Clamp(playerILife, 0, startLife);
            playerIILife = Mathf.Clamp(playerIILife, 0, startLife);

            if (playerILife <= 0) playerILost = true;
            if (playerIILife <= 0) playerIILost = true;

            DrawToThree(playerIHands);
            DrawToThree(playerIIHands);
        }
        public void ClearLoseFlags()
        {
            playerILost = false;
            playerIILost = false;
        }

        // “덱 + 상대 손패” 집계
        public Dictionary<CardType,int> BuildUnseen(bool isP1)
        {
            var unseen = new Dictionary<CardType,int>
            {
                {CardType.Cooperation,0},{CardType.Doubt,0},{CardType.Betrayal,0},
                {CardType.Chaos,0},{CardType.Pollution,0}
            };
            void Acc(IEnumerable<CardType> src)
            {
                foreach (var c in src)
                {
                    if (c == CardType.None) continue;
                    unseen[c] = unseen.TryGetValue(c, out var v) ? v + 1 : 1;
                }
            }
            Acc(publicDeck);
            Acc(isP1 ? playerIIHands : playerIHands);
            return unseen;
        }

        static int IndexOfFirst(List<CardType> hand, CardType t)
        {
            for (int i = 0; i < hand.Count; i++) if (hand[i] == t) return i;
            return hand.Count > 0 ? 0 : -1;
        }

        // 유틸
        static void Add(List<CardType> list, CardType t, int n){ for(int i=0;i<n;i++) list.Add(t); }
        CardType DrawOne(List<CardType> deck)
        {
            if (deck.Count == 0)
            {
                if (discardCards.Count == 0) return CardType.None;
                deck.AddRange(discardCards); discardCards.Clear(); Shuffle(deck);
            }
            var top = deck[0]; deck.RemoveAt(0); return top;
        }
        void Draw(List<CardType> deck, List<CardType> hand, int n){ for(int i=0;i<n;i++){ var c=DrawOne(deck); if(c==CardType.None) break; hand.Add(c);} }
        void DrawToThree(List<CardType> hand){ int need = 3 - hand.Count; if (need > 0) Draw(publicDeck, hand, need); }
        void ReplaceHand(List<CardType> hand){ discardCards.AddRange(hand); hand.Clear(); Draw(publicDeck, hand, 3); }
        CardType UseCard(List<CardType> hand, int index){ if(index<0||index>=hand.Count) return CardType.None; var c=hand[index]; hand.RemoveAt(index); discardCards.Add(c); return c; }
        static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        void BuildEffects()
        {
            E = new Dictionary<string, Effect>();
            var C=CardType.Cooperation; var D=CardType.Doubt; var B=CardType.Betrayal; var X=CardType.Chaos; var P=CardType.Pollution;

            // 최신 매트릭스 반영(Chaos–Pollution, Pollution–Chaos = 0, 교체만)
            E[$"{C}-{C}"]=new(+1,+1);  E[$"{C}-{D}"]=new(+1,0);   E[$"{C}-{B}"]=new(-10,+1); E[$"{C}-{X}"]=new(+1,0,false,true);  E[$"{C}-{P}"]=new(-1,+1);
            E[$"{D}-{C}"]=new(0,+1);   E[$"{D}-{D}"]=new(0,0);    E[$"{D}-{B}"]=new(+1,-10); E[$"{D}-{X}"]=new(0,0,false,true);   E[$"{D}-{P}"]=new(0,-1);
            E[$"{B}-{C}"]=new(+1,-10); E[$"{B}-{D}"]=new(-10,+1); E[$"{B}-{B}"]=new(-10,-10);E[$"{B}-{X}"]=new(+1,-10);          E[$"{B}-{P}"]=new(+1,-10);
            E[$"{X}-{C}"]=new(0,+1,true,false); E[$"{X}-{D}"]=new(0,0,true,false); E[$"{X}-{B}"]=new(-10,+1,true,false); E[$"{X}-{X}"]=new(0,0,true,true); E[$"{X}-{P}"]=new(0,0,true,false);
            E[$"{P}-{C}"]=new(+1,-1);  E[$"{P}-{D}"]=new(-1,0);   E[$"{P}-{B}"]=new(-10,+1); E[$"{P}-{X}"]=new(0,0,false,true);  E[$"{P}-{P}"]=new(-1,-1);
        }
    }
}