using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

namespace GameCore
{
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
            A.rules.Add(I => I.s.round <= 2 && I.HandHas(CardType.Cooperation)
                ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt)
                ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && !I.HandHas(CardType.Doubt) && I.HandHas(CardType.Interrupt)
                ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Cooperation
                            && I.s.selfLife >= I.s.oppLife
                            && I.HandHas(CardType.Interrupt)
                ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I =>
            {
                if (!I.HandHas(CardType.Recon)) return (CardType?)null;
                if (I.s.round < 3 || I.s.round > 7) return (CardType?)null;
                if (I.s.selfLife < I.s.oppLife) return (CardType?)null;           // 뒤지면 정찰 보류
                if (!I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt)) return (CardType?)null; // 즉응 필요
                // 분포가 한쪽으로 치우치지 않을 때 사용
                float pc = I.Ratio(CardType.Cooperation), pd = I.Ratio(CardType.Doubt), pp = I.Ratio(CardType.Pollution);
                bool mixed = pc is > 0.22f and < 0.48f && pd < 0.30f && pp < 0.35f;
                return mixed ? CardType.Recon : (CardType?)null;
            });
            A.rules.Add(I => (I.Ratio(CardType.Cooperation) >= 0.33f || (!I.s.IsFirst && I.s.lastOpp == CardType.Chaos))
                            && I.HandHas(CardType.Pollution)
                ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Betrayal)
                            && (!I.s.IsFirst && I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation
                                || I.s.oppLife <= Math.Max(1, I.s.round) + 1)
                            && I.Ratio(CardType.Doubt) < 0.20f
                ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => I.s.selfLife <= 4 && I.HandHas(CardType.Chaos)
                ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => I.Ratio(CardType.Pollution) >= 0.25f && I.HandHas(CardType.Doubt)
                ? CardType.Doubt : (CardType?)null);
            A.fallback = new[]
            {
                CardType.Cooperation, CardType.Recon, CardType.Doubt,
                CardType.Interrupt, CardType.Pollution, CardType.Betrayal, CardType.Chaos
            };
            return A;
        }

        // 이수진
        static Agent Build_이수진()
        {
            var A = new Agent("이수진");
            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool notFirst = !I.s.IsFirst;
                float rc(CardType t) => I.Ratio(t); // 확인 못 한 카드 분포 추정
                int atkInHand = I.hand.Count(x => x == CardType.Betrayal || x == CardType.Pollution);
                bool oppRepeat2 = notFirst && I.s.lastOpp != CardType.None && I.s.lastOpp == I.s.last2Opp;
                bool survivalRiskNow = I.s.selfLife <= R;
                if (survivalRiskNow &&
                    ((notFirst && I.s.lastOpp == CardType.Betrayal) || rc(CardType.Betrayal) >= 0.28f) &&
                    I.HandHas(CardType.Doubt))
                    return CardType.Doubt;
                if (oppRepeat2)
                {
                    if (I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Betrayal))
                        return CardType.Betrayal;

                    if (I.s.lastOpp == CardType.Pollution)
                    {
                        if (survivalRiskNow && I.HandHas(CardType.Doubt))
                            return CardType.Doubt;
                        if (I.HandHas(CardType.Pollution))
                            return CardType.Pollution;
                    }
                }
                if (R >= 3 && notFirst &&
                    I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation &&
                    I.HandHas(CardType.Betrayal))
                    return CardType.Betrayal;
                if (rc(CardType.Cooperation) >= 0.40f && rc(CardType.Doubt) < 0.25f &&
                    I.HandHas(CardType.Betrayal))
                    return CardType.Betrayal;
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R + 1)
                    return CardType.Betrayal;
                if (R <= 2 && I.HandHas(CardType.Pollution))
                    return CardType.Pollution;
                if (R != 1 && (I.s.selfLife <= I.s.oppLife - 2 || atkInHand <= 1) && I.HandHas(CardType.Chaos))
                    return CardType.Chaos;
                if (R != 1 && (!notFirst || I.s.lastOpp != CardType.Doubt) && I.HandHas(CardType.Pollution))
                    return CardType.Pollution;
                if (I.HandHas(CardType.Recon) && !I.HandHas(CardType.Betrayal) &&
                    ((notFirst && I.s.lastOpp == CardType.Cooperation) || (I.s.selfLife < I.s.oppLife)))
                    return CardType.Recon;
                var order = new[]
                {
                    CardType.Betrayal, CardType.Pollution, CardType.Chaos,
                    CardType.Recon, CardType.Cooperation, CardType.Doubt, CardType.Interrupt
                };
                foreach (var c in order)
                    if (I.HandHas(c)) return c;
                return CardType.None;
            });
            A.fallback = new[]
            {
                CardType.Betrayal, CardType.Pollution, CardType.Chaos,
                CardType.Recon, CardType.Cooperation, CardType.Doubt, CardType.Interrupt
            };
            return A;
        }

        // 최용호
        static Agent Build_최용호()
        {
            var A = new Agent("최용호");
            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R)
                    return CardType.Betrayal;
                if (R <= 3)
                {
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }
                if (!I.s.IsFirst && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Betrayal))
                    return CardType.Betrayal;
                if (!I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;
                if (!I.s.IsFirst && I.s.lastOpp == CardType.Betrayal && I.HandHas(CardType.Interrupt))
                    return CardType.Interrupt;
                if (I.HandHas(CardType.Betrayal) && I.HandHas(CardType.Pollution))
                    return CardType.Betrayal;
                if (R % 3 == 0 && I.HandHas(CardType.Chaos))
                    return CardType.Chaos;
                var prefer = new[]
                {
                    CardType.Betrayal, CardType.Pollution, CardType.Interrupt,
                    CardType.Chaos, CardType.Doubt, CardType.Recon, CardType.Cooperation
                };
                foreach (var c in prefer)
                    if (I.HandHas(c)) return c;

                return CardType.None;
            });
            A.fallback = new[] {
                CardType.Betrayal, CardType.Pollution, CardType.Interrupt,
                CardType.Chaos, CardType.Doubt, CardType.Recon, CardType.Cooperation
            };
            return A;
        }

        // 한지혜
        static Agent Build_한지혜()
        {
            var A = new Agent("한지혜");
            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                var p = new Dictionary<CardType, float> {
                    {CardType.Cooperation, I.Ratio(CardType.Cooperation)},
                    {CardType.Doubt,       I.Ratio(CardType.Doubt)},
                    {CardType.Betrayal,    I.Ratio(CardType.Betrayal)},
                    {CardType.Chaos,       I.Ratio(CardType.Chaos)},
                    {CardType.Pollution,   I.Ratio(CardType.Pollution)},
                };
                float P(CardType t) => p.TryGetValue(t, out var v) ? v : 0f;
                if (I.s.IsFirst)
                {
                    if (I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                    if (I.HandHas(CardType.Recon)) return CardType.Recon;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }
                bool lethal = I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && P(CardType.Doubt) < 0.30f;
                if (lethal) return CardType.Betrayal;
                bool panic = (I.s.selfLife <= R || I.s.selfLife <= I.s.oppLife - 2) && P(CardType.Betrayal) >= 0.25f;
                if (panic && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                if (!I.s.IsFirst && I.s.lastOpp == I.s.last2Opp)
                {
                    var last = I.s.lastOpp;
                    if (last == CardType.Cooperation && I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    if (last == CardType.Betrayal && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    if (last == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    if (last == CardType.Doubt && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                    if (last == CardType.Chaos && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                }
                if (!I.s.IsFirst && I.s.lastOpp == CardType.Cooperation)
                {
                    if (I.s.selfLife >= I.s.oppLife && I.HandHas(CardType.Cooperation))
                        return CardType.Cooperation;
                    if ((I.s.selfLife < I.s.oppLife || P(CardType.Cooperation) >= 0.35f) && I.HandHas(CardType.Pollution))
                        return CardType.Pollution;
                }
                if (P(CardType.Betrayal) >= 0.30f && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;
                if (I.s.round <= 2 && I.HandHas(CardType.Cooperation))
                    return CardType.Cooperation;
                int attackCnt = (I.HandHas(CardType.Betrayal) ? 1 : 0) + (I.HandHas(CardType.Pollution) ? 1 : 0);
                if (!I.s.IsFirst && I.s.round >= 3 && attackCnt <= 1 && I.HandHas(CardType.Chaos))
                    return CardType.Chaos;
                float maxP = p.Values.DefaultIfEmpty(0f).Max();
                if (I.HandHas(CardType.Recon) && maxP < 0.35f && (I.s.selfLife < I.s.oppLife))
                    return CardType.Recon;
                CardType[] order = {
                    CardType.Pollution, CardType.Cooperation, CardType.Doubt,
                    CardType.Betrayal, CardType.Recon, CardType.Chaos, CardType.Interrupt
                };
                foreach (var c in order) if (I.HandHas(c)) return c;
                return CardType.None;
            });
            A.fallback = new[] { CardType.Pollution, CardType.Cooperation, CardType.Doubt, CardType.Betrayal, CardType.Recon, CardType.Chaos };
            return A;
        }

        // 박민재
        static Agent Build_박민재()
        {
            var A = new Agent("박민재");

            A.rules.Add(I =>
            {
                // 0) 상대 액션 분포 p[t] = 관찰비율(최근 히스토리) 기반. 성향 가중치 없음
                var p = new Dictionary<CardType, float>
                {
                    { CardType.Cooperation, I.Ratio(CardType.Cooperation) },
                    { CardType.Doubt,       I.Ratio(CardType.Doubt)       },
                    { CardType.Betrayal,    I.Ratio(CardType.Betrayal)    },
                    { CardType.Chaos,       I.Ratio(CardType.Chaos)       },
                    { CardType.Pollution,   I.Ratio(CardType.Pollution)   },
                    { CardType.Interrupt,   I.Ratio(CardType.Interrupt)   },
                    { CardType.Recon,       I.Ratio(CardType.Recon)       },
                };
                float sum = p.Values.Sum(); if (sum <= 0) sum = 1f;
                foreach (var k in p.Keys.ToList()) p[k] /= sum;

                // 1) 즉사/방지 계산
                int R = Math.Max(1, I.s.round);
                bool lethalWithB = I.HandHas(CardType.Betrayal) && I.s.oppLife <= R;
                if (lethalWithB && p[CardType.Doubt] < 0.33f) return CardType.Betrayal;

                // 생존: 상대 배신 확률 높고(관찰치) 내 체력 위험
                if (I.HandHas(CardType.Doubt) && I.s.selfLife <= R && p[CardType.Betrayal] >= 0.28f)
                    return CardType.Doubt;

                // 2) 정보가치 기반 Recon 사용
                // 공격 수단 부족 또는 스코어 낮은 손패일 때 사용
                int HandScore(List<CardType> h)
                {
                    int s = 0;
                    foreach (var c in h)
                    {
                        if (c == CardType.Betrayal) s += 3;
                        else if (c == CardType.Pollution) s += 2;
                        else if (c == CardType.Cooperation) s += 1;
                        else if (c == CardType.Doubt) s += 1;
                        else if (c == CardType.Recon) s += 0;
                        else if (c == CardType.Chaos) s -= 1;
                        else if (c == CardType.Interrupt) s -= 1;
                    }
                    return s;
                }
                int hs = HandScore(I.hand);
                bool poorAttack = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);
                if (I.HandHas(CardType.Recon) && (poorAttack || hs <= 1))
                    return CardType.Recon;

                // 3) EV 최적화
                int Delta(CardType a, CardType b)
                {
                    // 표는 “자신 점수 변화 − 상대 점수 변화”의 상대 차익으로 단순화(양수 유리)
                    // Cooperation
                    if (a == CardType.Cooperation && b == CardType.Cooperation) return 0;
                    if (a == CardType.Cooperation && b == CardType.Doubt) return +1;
                    if (a == CardType.Cooperation && b == CardType.Betrayal) return -(R + 1);
                    if (a == CardType.Cooperation && b == CardType.Chaos) return +1;
                    if (a == CardType.Cooperation && b == CardType.Pollution) return -2;
                    if (a == CardType.Cooperation && b == CardType.Interrupt) return +2;   // +1/-1
                    if (a == CardType.Cooperation && b == CardType.Recon) return +1;

                    // Doubt
                    if (a == CardType.Doubt && b == CardType.Cooperation) return -1;
                    if (a == CardType.Doubt && b == CardType.Doubt) return 0;
                    if (a == CardType.Doubt && b == CardType.Betrayal) return R + 1;
                    if (a == CardType.Doubt && b == CardType.Chaos) return 0;
                    if (a == CardType.Doubt && b == CardType.Pollution) return +1;
                    if (a == CardType.Doubt && b == CardType.Interrupt) return -1;
                    if (a == CardType.Doubt && b == CardType.Recon) return 0;

                    // Betrayal
                    if (a == CardType.Betrayal && b == CardType.Cooperation) return R + 1;
                    if (a == CardType.Betrayal && b == CardType.Doubt) return -(R + 1);
                    if (a == CardType.Betrayal && b == CardType.Betrayal) return -2 * R;
                    if (a == CardType.Betrayal && b == CardType.Chaos) return R + 1;
                    if (a == CardType.Betrayal && b == CardType.Pollution) return R + 1;
                    if (a == CardType.Betrayal && b == CardType.Interrupt) return R;       // +1/-1 ≈ +R
                    if (a == CardType.Betrayal && b == CardType.Recon) return R + 1;

                    // Chaos
                    if (a == CardType.Chaos && b == CardType.Cooperation) return -1;
                    if (a == CardType.Chaos && b == CardType.Doubt) return 0;
                    if (a == CardType.Chaos && b == CardType.Betrayal) return -(R + 1);
                    if (a == CardType.Chaos && b == CardType.Chaos) return 0;
                    if (a == CardType.Chaos && b == CardType.Pollution) return 0;
                    if (a == CardType.Chaos && b == CardType.Interrupt) return -1;
                    if (a == CardType.Chaos && b == CardType.Recon) return 0;

                    // Pollution
                    if (a == CardType.Pollution && b == CardType.Cooperation) return +2;
                    if (a == CardType.Pollution && b == CardType.Doubt) return -1;
                    if (a == CardType.Pollution && b == CardType.Betrayal) return -(R + 1);
                    if (a == CardType.Pollution && b == CardType.Chaos) return 0;
                    if (a == CardType.Pollution && b == CardType.Pollution) return 0;
                    if (a == CardType.Pollution && b == CardType.Interrupt) return 0;      // -1/-1
                    if (a == CardType.Pollution && b == CardType.Recon) return -1;

                    // Interrupt
                    if (a == CardType.Interrupt && b == CardType.Cooperation) return -2;   // -1/+1
                    if (a == CardType.Interrupt && b == CardType.Doubt) return +2;
                    if (a == CardType.Interrupt && b == CardType.Betrayal) return +2;
                    if (a == CardType.Interrupt && b == CardType.Chaos) return -1;
                    if (a == CardType.Interrupt && b == CardType.Pollution) return +2;
                    if (a == CardType.Interrupt && b == CardType.Interrupt) return 0;
                    if (a == CardType.Interrupt && b == CardType.Recon) return +1;

                    // Recon
                    if (a == CardType.Recon && b == CardType.Cooperation) return -1;
                    if (a == CardType.Recon && b == CardType.Doubt) return 0;
                    if (a == CardType.Recon && b == CardType.Betrayal) return -(R + 1);
                    if (a == CardType.Recon && b == CardType.Chaos) return 0;
                    if (a == CardType.Recon && b == CardType.Pollution) return -1;
                    if (a == CardType.Recon && b == CardType.Interrupt) return -1;
                    if (a == CardType.Recon && b == CardType.Recon) return 0;
                    return 0;
                }

                var unique = I.hand.Distinct().Where(I.HandHas).ToList();
                CardType best = CardType.None; float bestEV = float.NegativeInfinity;

                foreach (var a in unique)
                {
                    float ev = 0f;
                    foreach (var b in p.Keys) ev += p[b] * Delta(a, b);

                    // 막판 킬각 보정
                    if (a == CardType.Betrayal && I.s.oppLife <= R + 1) ev += p[CardType.Cooperation] * 2.5f;

                    // 생존 위험 패널티
                    if (I.s.selfLife <= R) ev -= p[CardType.Betrayal] * 3.0f;

                    if (ev > bestEV) { bestEV = ev; best = a; }
                }

                // 의심 확률 높고 배신 선택이면 회피(순수 계산가라도 리스크 관리)
                if (best == CardType.Betrayal && p[CardType.Doubt] >= 0.34f)
                {
                    var alt = unique.Where(t => t != CardType.Betrayal)
                                    .OrderByDescending(t =>
                                    { float ev = 0; foreach (var b in p.Keys) ev += p[b] * Delta(t, b); return ev; })
                                    .FirstOrDefault();
                    if (alt != CardType.None) best = alt;
                }

                return best;
            });

            // 계산가의 기본 우선순위(무규칙 시)
            A.fallback = new[]
            {
                CardType.Betrayal, CardType.Pollution, CardType.Cooperation,
                CardType.Recon, CardType.Doubt, CardType.Chaos, CardType.Interrupt
            };

            return A;
        }

        // 정다은
        static Agent Build_정다은()
        {
            var A = new Agent("정다은");

            A.rules.Add(I =>
            {
                // --- 0) 상대 액션분포 추정 p[t] ---
                var p = new Dictionary<CardType, float>
                {
                    { CardType.Cooperation, I.Ratio(CardType.Cooperation) },
                    { CardType.Doubt,       I.Ratio(CardType.Doubt)       },
                    { CardType.Betrayal,    I.Ratio(CardType.Betrayal)    },
                    { CardType.Chaos,       I.Ratio(CardType.Chaos)       },
                    { CardType.Pollution,   I.Ratio(CardType.Pollution)   },
                    { CardType.Interrupt,   I.Ratio(CardType.Interrupt)   },
                    { CardType.Recon,       I.Ratio(CardType.Recon)       },
                };
                void Boost(CardType t, float m)
                {
                    if (t == CardType.None) return;
                    if (!p.ContainsKey(t)) return;
                    p[t] *= m;
                }

                int R = Math.Max(1, I.s.round);

                // 최근 경향 보정
                if (!I.s.IsFirst)
                {
                    // 같은 카드 2연속 재현성 가중 (None 제외)
                    if (I.s.lastOpp != CardType.None && I.s.lastOpp == I.s.last2Opp)
                        Boost(I.s.lastOpp, 1.35f);

                    // 초반(1~3) 협력 경향 → 장기전 포지셔닝
                    if (R <= 3 && I.s.lastOpp == CardType.Cooperation) Boost(CardType.Cooperation, 1.18f);

                    // 직전 배신은 반복 위험 소폭
                    if (I.s.lastOpp == CardType.Betrayal) Boost(CardType.Betrayal, 1.15f);
                }

                // 체력 보정
                if (I.s.selfLife <= 3) { Boost(CardType.Betrayal, 0.92f); Boost(CardType.Doubt, 1.10f); }
                if (I.s.oppLife <= 3) { Boost(CardType.Cooperation, 1.06f); Boost(CardType.Pollution, 1.10f); }

                float sum = p.Values.Sum(); if (sum <= 0) sum = 1f;
                foreach (var k in p.Keys.ToList()) p[k] /= sum;

                // bool Have(params CardType[] ts) => ts.Any(t => I.HandHas(t));

                // --- 1) 생존 규칙 ---
                bool lethalRisk = (I.s.selfLife <= R) && (p[CardType.Betrayal] >= 0.28f || I.s.lastOpp == CardType.Betrayal);
                if (lethalRisk && I.HandHas(CardType.Doubt)) return CardType.Doubt;

                // --- 2) 패턴 카운터 ---
                if (!I.s.IsFirst && I.s.lastOpp != CardType.None && I.s.lastOpp == I.s.last2Opp)
                {
                    var x = I.s.lastOpp;
                    if (x == CardType.Cooperation && I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (x == CardType.Betrayal && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    if (x == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    if (x == CardType.Chaos && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                    if (x == CardType.Interrupt && I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // --- 3) 정보 우선(초중반 정찰) ---
                bool safeForRecon =
                    R <= 4 &&
                    I.s.selfLife >= I.s.oppLife - 1 &&
                    p[CardType.Betrayal] <= 0.26f &&
                    !I.HandHas(CardType.Betrayal);
                if (safeForRecon && I.HandHas(CardType.Recon)) return CardType.Recon;

                // --- 4) 장기전 포지션(협력/오염) ---
                if (I.HandHas(CardType.Pollution) &&
                    (p[CardType.Cooperation] >= 0.32f || (!I.s.IsFirst && I.s.lastOpp == CardType.Chaos)) &&
                    p[CardType.Doubt] <= 0.28f)
                    return CardType.Pollution;

                if (I.HandHas(CardType.Cooperation) &&
                    p[CardType.Betrayal] <= 0.22f &&
                    I.s.selfLife >= I.s.oppLife - 1)
                    return CardType.Cooperation;

                // --- 5) 킬각/역공 타이밍 ---
                bool killWindow = I.s.oppLife <= R + 1 && p[CardType.Doubt] < 0.32f;
                if (killWindow && I.HandHas(CardType.Betrayal)) return CardType.Betrayal;

                // Doubt로 상대 오염 카운터
                if (I.HandHas(CardType.Doubt) && (I.s.lastOpp == CardType.Pollution || p[CardType.Pollution] >= 0.30f))
                    return CardType.Doubt;

                // --- 6) 손패 품질 리롤/중립화 ---
                int atk = (I.HandHas(CardType.Betrayal) ? 1 : 0) + (I.HandHas(CardType.Pollution) ? 1 : 0);
                if (I.HandHas(CardType.Chaos) && (atk <= 1 || (I.s.lastOpp == I.s.last2Opp && R >= 4)))
                    return CardType.Chaos;

                if (I.HandHas(CardType.Interrupt) &&
                    p[CardType.Cooperation] <= 0.25f &&
                    p[CardType.Betrayal] + p[CardType.Pollution] >= 0.45f)
                    return CardType.Interrupt;

                // --- 7) 일반 우선순위 ---
                CardType[] prio = {
                    CardType.Pollution,
                    CardType.Cooperation,
                    CardType.Betrayal,
                    CardType.Recon,
                    CardType.Doubt,
                    CardType.Chaos,
                    CardType.Interrupt
                };
                foreach (var c in prio) if (I.HandHas(c)) return c;
                return CardType.None;
            });

            A.fallback = new[] { CardType.Pollution, CardType.Cooperation, CardType.Betrayal, CardType.Doubt, CardType.Chaos, CardType.Interrupt, CardType.Recon };
            return A;
        }

        // 오태훈
        static Agent Build_오태훈()
        {
            var A = new Agent("오태훈");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool notFirst = !I.s.IsFirst;
                int atkInHand = I.hand.Count(x => x == CardType.Betrayal || x == CardType.Pollution);

                // 킬각
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R + 1) return CardType.Betrayal;
                if (I.HandHas(CardType.Pollution) && I.s.oppLife <= R) return CardType.Pollution;

                // 초반 러시
                if (R <= 2 && I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                if (R <= 2 && I.HandHas(CardType.Pollution)) return CardType.Pollution;

                // 패턴 처벌
                if (notFirst && I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation && I.HandHas(CardType.Betrayal))
                    return CardType.Betrayal;

                // 분포 기반 하이리스크
                if (I.Ratio(CardType.Cooperation) >= 0.35f && I.Ratio(CardType.Doubt) < 0.25f && I.HandHas(CardType.Betrayal))
                    return CardType.Betrayal;

                // 혼전 전환
                if (R != 1 && (I.s.selfLife <= I.s.oppLife - 2 || atkInHand <= 1) && I.HandHas(CardType.Chaos))
                    return CardType.Chaos;

                // 공격 유지
                if (R != 1 && (!notFirst || I.s.lastOpp != CardType.Doubt) && I.HandHas(CardType.Pollution))
                    return CardType.Pollution;

                // 정찰
                if (I.HandHas(CardType.Recon) && !I.HandHas(CardType.Betrayal) &&
                    ((notFirst && I.s.lastOpp == CardType.Cooperation) || (I.s.selfLife < I.s.oppLife)))
                    return CardType.Recon;

                // 생존
                if (I.s.selfLife <= R && ((notFirst && I.s.lastOpp == CardType.Betrayal) || I.Ratio(CardType.Betrayal) >= 0.28f) && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                // 반복 카운터
                if (notFirst && I.s.lastOpp == I.s.last2Opp)
                {
                    if (I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.s.lastOpp == CardType.Pollution)
                    {
                        if (I.s.selfLife <= R && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                        if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    }
                }

                // 기본 우선순위
                CardType[] order = {
                    CardType.Betrayal, CardType.Pollution, CardType.Chaos,
                    CardType.Recon, CardType.Cooperation, CardType.Doubt, CardType.Interrupt
                };
                foreach (var c in order) if (I.HandHas(c)) return c;

                return CardType.None;
            });

            A.fallback = new[] {
                CardType.Betrayal, CardType.Pollution, CardType.Chaos,
                CardType.Recon, CardType.Cooperation, CardType.Doubt, CardType.Interrupt
            };
            return A;
        }

        // 유민정
        static Agent Build_유민정()
        {
            var A = new Agent("유민정");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool notFirst = !I.s.IsFirst;

                // 1) 즉시 카운터
                if (notFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                if (notFirst && (I.s.lastOpp == CardType.Pollution || I.s.lastOpp == CardType.Betrayal)
                            && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;

                // 2) 생존 우선
                if (I.HandHas(CardType.Doubt) && (I.s.selfLife <= R || I.Ratio(CardType.Betrayal) >= 0.28f))
                    return CardType.Doubt;

                // 3) 미러링 성향
                if (notFirst && I.HandHas(I.s.lastOpp)) return I.s.lastOpp;
                if (notFirst && I.s.lastOpp == I.s.last2Opp && I.HandHas(I.s.lastOpp)) return I.s.lastOpp;

                // 4) 뒤질 때 회복(동점 지향)
                if (I.s.selfLife < I.s.oppLife && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;

                // 5) 상대 협력 성향↑ → 안전한 오염으로 견제
                if ((I.Ratio(CardType.Cooperation) >= 0.35f || (notFirst && I.s.lastOpp == CardType.Cooperation))
                    && I.Ratio(CardType.Doubt) < 0.25f && I.HandHas(CardType.Pollution))
                    return CardType.Pollution;

                // 6) 혼돈으로 분석 교란(공격 수단 부족 또는 읽힘 심함)
                int atk = (I.HandHas(CardType.Pollution) ? 1 : 0) + (I.HandHas(CardType.Betrayal) ? 1 : 0);
                if (I.HandHas(CardType.Chaos) && (atk == 0 || (notFirst && I.s.lastOpp == I.s.last2Opp)))
                    return CardType.Chaos;

                // 7) Recon은 따라가기 용도로만 소극 사용
                if (I.HandHas(CardType.Recon) && !I.HandHas(CardType.Betrayal)
                    && (I.s.selfLife <= I.s.oppLife || I.Ratio(CardType.Cooperation) is > 0.28f and < 0.45f))
                    return CardType.Recon;

                // 8) Betrayal은 거의 금지. 정말로 킬각만 허용.
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && I.Ratio(CardType.Doubt) < 0.25f)
                    return CardType.Betrayal;

                // 방어적 기본 우선순위
                A.fallback = new[]
                {
                    CardType.Doubt, CardType.Cooperation, CardType.Interrupt,
                    CardType.Pollution, CardType.Recon, CardType.Chaos, CardType.Betrayal
                };
                return null;
            });
            return A;
        }

        // 김태양
        static Agent Build_김태양()
        {
            var A = new Agent("김태양");
            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool notFirst = !I.s.IsFirst;
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && I.Ratio(CardType.Doubt) < 0.33f)
                    return CardType.Betrayal;

                if (notFirst && I.s.lastOpp == I.s.last2Opp)
                {
                    if (UnityEngine.Random.value < 0.50f)
                    {
                        var x = I.s.lastOpp;
                        if (x == CardType.Cooperation && I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                        if (x == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                        if (x == CardType.Betrayal && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                        if (x == CardType.Doubt && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                        if (x == CardType.Interrupt && I.HandHas(CardType.Pollution)) return CardType.Pollution;
                        if (x == CardType.Chaos && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                    }
                    else if (I.HandHas(I.s.lastOpp)) return I.s.lastOpp;
                }
                if (R == 1)
                {
                    var pref = new List<CardType>();
                    if (I.HandHas(CardType.Chaos)) pref.Add(CardType.Chaos);
                    if (I.HandHas(CardType.Pollution)) pref.Add(CardType.Pollution);
                    if (I.HandHas(CardType.Betrayal)) pref.Add(CardType.Betrayal);
                    if (pref.Count > 0 && UnityEngine.Random.value < 0.70f)
                        return pref[UnityEngine.Random.Range(0, pref.Count)];
                    if (I.hand.Count > 0)
                        return I.hand[UnityEngine.Random.Range(0, I.hand.Count)];
                }
                if ((R % 3 == 0 || UnityEngine.Random.value < 0.18f) && I.HandHas(CardType.Chaos))
                    return CardType.Chaos;
                if (notFirst && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Betrayal) && UnityEngine.Random.value < 0.65f)
                    return CardType.Betrayal;
                if (notFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt) && UnityEngine.Random.value < 0.60f)
                    return CardType.Doubt;
                if (I.HandHas(CardType.Recon) &&
                    (I.s.selfLife < I.s.oppLife || R >= 2) &&
                    UnityEngine.Random.value < 0.12f)
                    return CardType.Recon;
                var atks = new List<CardType>();
                if (I.HandHas(CardType.Betrayal)) atks.Add(CardType.Betrayal);
                if (I.HandHas(CardType.Pollution)) atks.Add(CardType.Pollution);
                if (atks.Count > 0 && UnityEngine.Random.value < 0.55f)
                    return atks[UnityEngine.Random.Range(0, atks.Count)];
                if (notFirst && R % 2 == 1 && I.HandHas(I.s.lastOpp) && UnityEngine.Random.value < 0.50f)
                    return I.s.lastOpp;
                if (I.hand.Count > 0 && UnityEngine.Random.value < 0.10f)
                    return I.hand[UnityEngine.Random.Range(0, I.hand.Count)];
                return (CardType?)null;
            });
            A.fallback = new[]
            {
                CardType.Pollution, CardType.Betrayal, CardType.Interrupt,
                CardType.Doubt, CardType.Cooperation, CardType.Chaos, CardType.Recon
            };
            return A;
        }

        // 백무적
        static Agent Build_백무적()
        {
            var A = new Agent("백무적");

            A.rules.Add(I =>
            {
                // ===== 0) 상대 액션 분포 추정 =====
                var p = new Dictionary<CardType, float>
                {
                    { CardType.Cooperation, I.Ratio(CardType.Cooperation) },
                    { CardType.Doubt,       I.Ratio(CardType.Doubt)       },
                    { CardType.Betrayal,    I.Ratio(CardType.Betrayal)    },
                    { CardType.Chaos,       I.Ratio(CardType.Chaos)       },
                    { CardType.Pollution,   I.Ratio(CardType.Pollution)   },
                    { CardType.Interrupt,   I.Ratio(CardType.Interrupt)   },
                };
                void Boost(CardType t, float m) { p[t] *= m; }

                if (!I.s.IsFirst)
                {
                    if (I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation) Boost(CardType.Cooperation, 1.7f);
                    if (I.s.lastOpp == CardType.Pollution) Boost(CardType.Pollution, 1.5f);
                    if (I.s.lastOpp == CardType.Doubt) Boost(CardType.Doubt, 1.2f);
                    if (I.s.round % 3 == 0) Boost(CardType.Chaos, 1.12f);
                }
                if (I.s.oppLife <= 3) { Boost(CardType.Cooperation, 1.15f); Boost(CardType.Pollution, 1.08f); }
                if (I.s.selfLife <= 3) { Boost(CardType.Doubt, 1.10f); Boost(CardType.Chaos, 1.08f); }

                Boost(CardType.Interrupt, 1.0f + 0.4f * I.Ratio(CardType.Pollution));

                float sum = p.Values.Sum(); if (sum <= 0f) sum = 1f;
                foreach (var k in p.Keys.ToList()) p[k] /= sum;

                // ===== 1) 즉시 전술 =====
                int R = Math.Max(1, I.s.round);

                if (!I.s.IsFirst && (I.s.lastOpp == CardType.Pollution || I.s.lastOpp == CardType.Betrayal) && I.HandHas(CardType.Interrupt))
                    return CardType.Interrupt;

                if (!I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                bool lethal = I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.30f;
                if (lethal) return CardType.Betrayal;

                if (I.HandHas(CardType.Doubt) && I.s.selfLife <= R && p[CardType.Betrayal] >= 0.28f)
                    return CardType.Doubt;

                if (I.HandHas(CardType.Betrayal)
                    && (!I.s.IsFirst && I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation
                        || I.s.oppLife <= R + 1)
                    && p[CardType.Doubt] < 0.33f)
                    return CardType.Betrayal;

                if ((p[CardType.Cooperation] >= 0.33f || (!I.s.IsFirst && I.s.lastOpp == CardType.Chaos))
                    && I.HandHas(CardType.Pollution))
                    return CardType.Pollution;

                // ===== 2) 손패 품질 기반 리롤 =====
                int HandScore(List<CardType> h)
                {
                    int s = 0;
                    foreach (var c in h)
                    {
                        if (c == CardType.Doubt) s += 3;
                        else if (c == CardType.Cooperation) s += 2;
                        else if (c == CardType.Pollution) s += 1;
                        else if (c == CardType.Interrupt) s += 1;
                        else if (c == CardType.Betrayal) s += 0;
                        else if (c == CardType.Chaos) s -= 1;
                    }
                    return s;
                }
                int hs = HandScore(I.hand);
                if (I.HandHas(CardType.Chaos) && (hs <= 2 || (I.s.round % 3 == 0 && hs <= 3)))
                    return CardType.Chaos;

                // ===== 3) 기대값(EV) 최적화 =====
                int Delta(CardType a, CardType b)
                {
                    if (a == CardType.Cooperation && b == CardType.Cooperation) return 0;
                    if (a == CardType.Cooperation && b == CardType.Doubt) return +1;
                    if (a == CardType.Cooperation && b == CardType.Betrayal) return -(R + 1);
                    if (a == CardType.Cooperation && b == CardType.Chaos) return +1;
                    if (a == CardType.Cooperation && b == CardType.Pollution) return -2;
                    if (a == CardType.Cooperation && b == CardType.Interrupt) return -2;

                    if (a == CardType.Doubt && b == CardType.Cooperation) return -1;
                    if (a == CardType.Doubt && b == CardType.Doubt) return 0;
                    if (a == CardType.Doubt && b == CardType.Betrayal) return R + 1;
                    if (a == CardType.Doubt && b == CardType.Chaos) return 0;
                    if (a == CardType.Doubt && b == CardType.Pollution) return +1;
                    if (a == CardType.Doubt && b == CardType.Interrupt) return +1;

                    if (a == CardType.Betrayal && b == CardType.Cooperation) return R + 1;
                    if (a == CardType.Betrayal && b == CardType.Doubt) return -(R + 1);
                    if (a == CardType.Betrayal && b == CardType.Betrayal) return -2 * R;
                    if (a == CardType.Betrayal && b == CardType.Chaos) return R + 1;
                    if (a == CardType.Betrayal && b == CardType.Pollution) return R + 1;
                    if (a == CardType.Betrayal && b == CardType.Interrupt) return -2;

                    if (a == CardType.Chaos && b == CardType.Cooperation) return -1;
                    if (a == CardType.Chaos && b == CardType.Doubt) return 0;
                    if (a == CardType.Chaos && b == CardType.Betrayal) return -(R + 1);
                    if (a == CardType.Chaos && b == CardType.Chaos) return 0;
                    if (a == CardType.Chaos && b == CardType.Pollution) return 0;
                    if (a == CardType.Chaos && b == CardType.Interrupt) return +1;

                    if (a == CardType.Pollution && b == CardType.Cooperation) return +2;
                    if (a == CardType.Pollution && b == CardType.Doubt) return -1;
                    if (a == CardType.Pollution && b == CardType.Betrayal) return -(R + 1);
                    if (a == CardType.Pollution && b == CardType.Chaos) return 0;
                    if (a == CardType.Pollution && b == CardType.Pollution) return 0;
                    if (a == CardType.Pollution && b == CardType.Interrupt) return -2;

                    if (a == CardType.Interrupt && b == CardType.Cooperation) return +2;
                    if (a == CardType.Interrupt && b == CardType.Doubt) return -1;
                    if (a == CardType.Interrupt && b == CardType.Betrayal) return +2;
                    if (a == CardType.Interrupt && b == CardType.Chaos) return -1;
                    if (a == CardType.Interrupt && b == CardType.Pollution) return +2;
                    if (a == CardType.Interrupt && b == CardType.Interrupt) return 0;
                    return 0;
                }

                var choices = I.hand.Distinct().Where(I.HandHas).ToList();
                CardType best = CardType.None; float bestEV = float.NegativeInfinity;

                foreach (var a in choices)
                {
                    float ev = 0f; foreach (var b in p.Keys) ev += p[b] * Delta(a, b);

                    if (I.s.selfLife <= R)
                    {
                        ev -= p[CardType.Betrayal] * 3.5f;
                        if (a == CardType.Pollution) ev -= p[CardType.Betrayal] * 3.5f;
                    }
                    if (a == CardType.Chaos && hs <= 2) ev += 2.0f;
                    if (a == CardType.Betrayal && I.s.oppLife <= R + 1) ev += p[CardType.Cooperation] * 3.0f;

                    if (ev > bestEV) { bestEV = ev; best = a; }
                }

                if (best == CardType.Betrayal && p[CardType.Doubt] >= 0.30f && I.s.selfLife <= R + 1)
                {
                    var alt = choices.Where(t => t != CardType.Betrayal)
                                    .OrderByDescending(t =>
                                    { float ev = 0f; foreach (var b in p.Keys) ev += p[b] * Delta(t, b); return ev; })
                                    .FirstOrDefault();
                    if (alt != CardType.None) best = alt;
                }

                if (UnityEngine.Random.value < 0.07f)
                {
                    var mix = choices.Where(t => t != best).ToList();
                    if (mix.Count > 0) best = mix[UnityEngine.Random.Range(0, mix.Count)];
                }

                return best;
            });

            A.fallback = new[] {
                CardType.Doubt, CardType.Interrupt, CardType.Betrayal,
                CardType.Cooperation, CardType.Pollution, CardType.Chaos
            };
            return A;
        }

        // 이하린
        static Agent Build_이하린()
        {
            var A = new Agent("이하린");

            // 귀엽/예쁨 우선순위(높음 → 낮음)
            CardType[] cuteOrder = {
                CardType.Cooperation, // 반짝이는 일러스트 느낌
                CardType.Recon,       // 도구/그림 카드
                CardType.Doubt,       // 파랑 톤
                CardType.Chaos,       // 보라색 번쩍
                CardType.Pollution,   // 초록(보기 재미)
                CardType.Interrupt,   // 손바닥 모양
                CardType.Betrayal     // 무섭게 보임 → 최하
            };

            // 규칙: 손패에서 가장 "예쁜" 카드 하나만 고집
            A.rules.Add(I =>
            {
                foreach (var c in cuteOrder)
                    if (I.HandHas(c)) return c;
                return (CardType?)null;
            });

            // 예비: 위가 못 고르면 같은 순서로
            A.fallback = cuteOrder;
            return A;
        }
    }
}