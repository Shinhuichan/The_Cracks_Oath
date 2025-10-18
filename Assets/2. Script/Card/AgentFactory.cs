using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
// === 참가자 목록 ===
public enum AgentList
{
    김현수 = 1,
    이수진,
    최용호,
    한지혜,
    박민재,
    정다은,
    오태훈,
    유민정,
    김태양,
    이하린,
    백무적,
}

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
            "이하린" => Build_이하린(),
            "백무적" => Build_백무적(),
            _ => Build_Default(who),
        };

        static Agent Build_Default(string name)
        {
            var A = new Agent(name);
            A.fallback = new[] { CardType.Cooperation, CardType.Doubt, CardType.Pollution, CardType.Interrupt, CardType.Recon, CardType.Betrayal, CardType.Chaos };
            return A;
        }

        static Agent Build_김현수()
        {
            var A = new Agent("김현수");

            // --- 플레이(제출) 규칙: 기존 그대로 ---
            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                bool highB = I.Ratio(CardType.Betrayal) >= 0.28f || (nf && I.s.lastOpp == CardType.Betrayal);
                if (I.HandHas(CardType.Doubt) && I.s.selfLife <= R && highB)
                    return CardType.Doubt;

                if (nf && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                if (nf && I.s.lastOpp == CardType.Betrayal  && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;

                if (nf && I.s.lastOpp == I.s.last2Opp && I.s.lastOpp != CardType.None)
                {
                    var x = I.s.lastOpp;
                    if (x == CardType.Cooperation)
                    {
                        if (I.s.selfLife >= I.s.oppLife && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                        if (I.HandHas(CardType.Pollution) && I.Ratio(CardType.Doubt) <= 0.28f) return CardType.Pollution;
                    }
                    if (x == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    if (x == CardType.Doubt     && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                }

                if (I.HandHas(CardType.Recon) && R is >= 2 and <= 5 && I.s.selfLife >= I.s.oppLife - 1)
                {
                    float pc = I.Ratio(CardType.Cooperation), pd = I.Ratio(CardType.Doubt), pp = I.Ratio(CardType.Pollution);
                    bool mixed = pc < 0.45f && pd < 0.35f && pp < 0.35f;
                    if (mixed) return CardType.Recon;
                }

                if (I.HandHas(CardType.Pollution) &&
                (I.Ratio(CardType.Cooperation) >= 0.33f || (nf && I.s.lastOpp == CardType.Cooperation)) &&
                    I.Ratio(CardType.Doubt) <= 0.28f)
                    return CardType.Pollution;

                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && I.Ratio(CardType.Doubt) < 0.32f)
                    return CardType.Betrayal;

                int atk = (I.HandHas(CardType.Betrayal) ? 1 : 0) + (I.HandHas(CardType.Pollution) ? 1 : 0);
                if (I.HandHas(CardType.Chaos) && (atk == 0 || (nf && I.s.lastOpp == I.s.last2Opp && I.s.selfLife < I.s.oppLife)))
                    return CardType.Chaos;

                CardType[] order = {
                    CardType.Cooperation, CardType.Doubt, CardType.Recon,
                    CardType.Pollution, CardType.Interrupt, CardType.Betrayal, CardType.Chaos
                };
                foreach (var c in order) if (I.HandHas(c)) return c;
                return CardType.None;
            });

            A.fallback = new[] {
                CardType.Cooperation, CardType.Doubt, CardType.Recon,
                CardType.Pollution, CardType.Interrupt, CardType.Betrayal, CardType.Chaos
            };

            A.chooseFromTwo = (a, b, I) =>
            {
                int R = Math.Max(1, I.s.round);

                bool HasAtk() => I.hand.Contains(CardType.Betrayal) || I.hand.Contains(CardType.Pollution);
                bool HasDef() => I.hand.Contains(CardType.Doubt) || I.hand.Contains(CardType.Interrupt);
                int idxOf(CardType t) => (a == t ? 0 : (b == t ? 1 : -1));

                // 0) 즉사 위험 시 방어 우선
                bool lethalRisk = I.s.selfLife <= R && (I.Ratio(CardType.Betrayal) >= 0.28f || I.s.lastOpp == CardType.Betrayal);
                if (lethalRisk)
                {
                    int i = idxOf(CardType.Doubt); if (i >= 0) return i;
                    i = idxOf(CardType.Interrupt); if (i >= 0) return i;
                }

                // 1) 초·중반 안전할 때 Recon 선호(김현수 컨셉)
                bool safe = I.s.selfLife >= I.s.oppLife - 1;
                bool mixed = I.Ratio(CardType.Cooperation) < 0.45f && I.Ratio(CardType.Doubt) < 0.35f && I.Ratio(CardType.Pollution) < 0.35f;
                if (R >= 2 && R <= 5 && safe && mixed)
                {
                    int i = idxOf(CardType.Recon); if (i >= 0) return i;
                }

                // 2) 공격수단이 없으면 공격 카드 선호(가능하면 Pollution)
                if (!HasAtk())
                {
                    int i = idxOf(CardType.Pollution); if (i >= 0) return i;
                    i = idxOf(CardType.Betrayal);  if (i >= 0) return i;
                }

                // 3) 방어수단이 없으면 Doubt/Interrupt 확보
                if (!HasDef())
                {
                    int i = idxOf(CardType.Doubt);      if (i >= 0) return i;
                    i = idxOf(CardType.Interrupt);      if (i >= 0) return i;
                }

                // 4) 킬각이면 Betrayal 확보(상대 Doubt 낮을 때)
                if (I.s.oppLife <= R && I.Ratio(CardType.Doubt) < 0.32f)
                {
                    int i = idxOf(CardType.Betrayal); if (i >= 0) return i;
                }

                // 5) 상대가 협력 성향↑ + 의심 낮음 → Pollution 확보
                if ((I.Ratio(CardType.Cooperation) >= 0.33f || I.s.lastOpp == CardType.Cooperation) && I.Ratio(CardType.Doubt) <= 0.28f)
                {
                    int i = idxOf(CardType.Pollution); if (i >= 0) return i;
                }

                // 6) 기본 선호도(무난·분석형): Coop > Doubt > Recon > Pollution > Interrupt > Betrayal
                int Score(CardType t) => t switch
                {
                    CardType.Cooperation => 100,
                    CardType.Doubt       => 90,
                    CardType.Recon       => 80,
                    CardType.Pollution   => 70,
                    CardType.Interrupt   => 60,
                    CardType.Betrayal    => 50,
                    _ => 0
                };
                int sa = Score(a), sb = Score(b);
                if (sa != sb) return sa > sb ? 0 : 1;

                // 7) 동점이면 가벼운 무작위성
                return UnityEngine.Random.value < 0.5f ? 0 : 1;
            };
            return A;
        }

        // 이수진 — 모험가·즉흥형(하이리스크/하이리턴)
        static Agent Build_이수진()
        {
            var A = new Agent("이수진");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                // 상대 액션 분포(라플라스 스무딩 + 최근 반복 가중)
                var p = new Dictionary<CardType, float>()
                {
                    {CardType.Cooperation, 0.06f + I.Ratio(CardType.Cooperation)},
                    {CardType.Doubt,       0.06f + I.Ratio(CardType.Doubt)},
                    {CardType.Betrayal,    0.06f + I.Ratio(CardType.Betrayal)},
                    {CardType.Chaos,       0.06f + I.Ratio(CardType.Chaos)},
                    {CardType.Pollution,   0.06f + I.Ratio(CardType.Pollution)},
                    {CardType.Interrupt,   0.06f + I.Ratio(CardType.Interrupt)},
                    {CardType.Recon,       0.06f + I.Ratio(CardType.Recon)}
                };
                if (nf && I.s.lastOpp != CardType.None && I.s.lastOpp == I.s.last2Opp)
                    p[I.s.lastOpp] *= 1.35f; // 패턴 집착 읽고 베팅
                float S = p.Values.Sum(); foreach (var k in p.Keys.ToList()) p[k] /= S;

                // 0) 하이리스크 트리거: 내가 뒤지거나(R-우위 손해) 손패가 빈약하면 변동성↑
                bool losing = I.s.selfLife < I.s.oppLife;
                bool poorAtk = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);

                // 1) 즉사 회피는 최소한만
                if (I.HandHas(CardType.Doubt) && I.s.selfLife <= R - 1 && p[CardType.Betrayal] >= 0.30f)
                    return CardType.Doubt;

                // 2) 초반 러시 또는 킬각은 과감히 배신
                if (I.HandHas(CardType.Betrayal) && (R <= 2 || I.s.oppLife <= R))
                    if (p[CardType.Doubt] < 0.36f) return CardType.Betrayal;

                // 3) 협력 반복은 강하게 처벌, 오염 반복은 Doubt
                if (nf && I.s.lastOpp == I.s.last2Opp)
                {
                    var x = I.s.lastOpp;
                    if (x == CardType.Cooperation && I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (x == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    if (x == CardType.Betrayal && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                }

                // 4) 상대 협력 성향↑이면 오염으로 압박
                if (I.HandHas(CardType.Pollution) &&
                    (p[CardType.Cooperation] >= 0.33f || (nf && I.s.lastOpp == CardType.Cooperation)) &&
                    p[CardType.Doubt] <= 0.30f)
                    return CardType.Pollution;

                // 5) 손패 리셋(가챠 감성): 공격수단 없거나 지는 중이면 과감히
                int atkCnt = (I.HandHas(CardType.Betrayal) ? 1 : 0) + (I.HandHas(CardType.Pollution) ? 1 : 0);
                if (I.HandHas(CardType.Chaos) && (poorAtk || losing || (nf && I.s.lastOpp == I.s.last2Opp)))
                    return CardType.Chaos;

                // 6) Recon은 드물게: 초중반, 내가 열세거나 손패 쓰레기일 때만
                if (I.HandHas(CardType.Recon) && R <= 4 && (losing || poorAtk))
                    return CardType.Recon;

                // 7) 하이롤 우선 기본 우선순위
                CardType[] order =
                {
                    CardType.Betrayal,   // 터지면 이득 최대
                    CardType.Pollution,  // 꾸준 압박
                    CardType.Chaos,      // 변동성 확보
                    CardType.Interrupt,  // 틈새 역전
                    CardType.Cooperation,// 숨 고르기
                    CardType.Doubt,      // 최소 방어
                    CardType.Recon       // 마지막 수단 정보
                };
                foreach (var c in order) if (I.HandHas(c)) return c;

                return CardType.None;
            });

            A.fallback = new[] {
                CardType.Betrayal, CardType.Pollution, CardType.Chaos,
                CardType.Interrupt, CardType.Cooperation, CardType.Doubt, CardType.Recon
            };
            // 이수진 - 선택 드로우(공격 선호, 하이리스크/하이리턴)
            A.chooseFromTwo = (a, b, I) =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                // 상대 분포(라플라스 + 최근 반복 가중)
                var p = new Dictionary<CardType, float> {
                    {CardType.Cooperation, 0.06f + I.Ratio(CardType.Cooperation)},
                    {CardType.Doubt,       0.06f + I.Ratio(CardType.Doubt)},
                    {CardType.Betrayal,    0.06f + I.Ratio(CardType.Betrayal)},
                    {CardType.Chaos,       0.06f + I.Ratio(CardType.Chaos)},
                    {CardType.Pollution,   0.06f + I.Ratio(CardType.Pollution)},
                    {CardType.Interrupt,   0.06f + I.Ratio(CardType.Interrupt)},
                    {CardType.Recon,       0.06f + I.Ratio(CardType.Recon)},
                };
                if (nf && I.s.lastOpp != CardType.None && I.s.lastOpp == I.s.last2Opp)
                    p[I.s.lastOpp] *= 1.35f;
                float S = p.Values.Sum(); foreach (var k in p.Keys.ToList()) p[k] /= (S <= 0 ? 1f : S);

                bool losing   = I.s.selfLife < I.s.oppLife;
                bool lethal   = I.s.oppLife <= R;
                bool inDanger = I.s.selfLife <= R-1 && p[CardType.Betrayal] >= 0.30f;
                bool oppCoop  = p[CardType.Cooperation] >= 0.33f || (nf && I.s.lastOpp == CardType.Cooperation);
                bool oppDOT   = nf && I.s.lastOpp == CardType.Pollution;

                bool needAtk = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);

                float Score(CardType c)
                {
                    float s = 0f;
                    if (c == CardType.Betrayal)  s += (R <= 2 ? 3.5f : 2.0f) + (lethal ? 4.0f : 0f) - p[CardType.Doubt]*2.0f;
                    if (c == CardType.Pollution) s += 2.2f + (oppCoop ? 1.5f : 0f) - p[CardType.Doubt]*0.8f;
                    if (c == CardType.Chaos)     s += (losing || needAtk ? 2.2f : 0.8f);
                    if (c == CardType.Interrupt) s += (nf && (I.s.lastOpp == CardType.Betrayal || I.s.lastOpp == CardType.Pollution)) ? 2.5f : 0.6f;
                    if (c == CardType.Doubt)     s += inDanger ? 2.8f : (oppDOT ? 1.2f : 0.2f);
                    if (c == CardType.Recon)     s += (R <= 4 && (losing || needAtk)) ? 1.2f : 0.0f;
                    if (c == CardType.Cooperation) s += losing ? 0.2f : 0.6f;
                    if (needAtk && (c == CardType.Betrayal || c == CardType.Pollution)) s += 1.2f;
                    return s;
                }

                float sa = Score(a), sb = Score(b);

                // 기본 선택
                int pick = sa >= sb ? 0 : 1;

                // 동점이면 공격 쪽 60% 편향
                if (Mathf.Approximately(sa, sb))
                {
                    bool aOff = (a == CardType.Betrayal || a == CardType.Pollution || a == CardType.Chaos);
                    bool bOff = (b == CardType.Betrayal || b == CardType.Pollution || b == CardType.Chaos);
                    if (aOff != bOff)
                        pick = aOff ? (UnityEngine.Random.value < 0.60f ? 0 : 1)
                                    : (UnityEngine.Random.value < 0.40f ? 0 : 1);
                    else
                        pick = UnityEngine.Random.value < 0.5f ? 0 : 1;
                }

                return pick; // 0이면 a 선택, 1이면 b 선택
            };
            return A;
        }

        // 최용호 — 빠른 템포·단기결전·노계산
        static Agent Build_최용호()
        {
            var A = new Agent("최용호");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                // 0) 킬각은 즉시
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R) return CardType.Betrayal;

                // 1) 초반 러시(1~3라): 배신>오염>혼돈
                if (R <= 3)
                {
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    if (I.HandHas(CardType.Chaos) && UnityEngine.Random.value < 0.40f) return CardType.Chaos;
                }

                // 2) 뒤지면 더 세게 밟는다
                if (I.s.selfLife < I.s.oppLife)
                {
                    if (I.HandHas(CardType.Betrayal) && UnityEngine.Random.value < 0.70f) return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    if (I.HandHas(CardType.Chaos) && UnityEngine.Random.value < 0.35f) return CardType.Chaos;
                }

                // 3) 간단한 즉응(아주 낮은 확률의 방어만 허용)
                if (nf && I.s.lastOpp == CardType.Betrayal && I.HandHas(CardType.Interrupt) && UnityEngine.Random.value < 0.10f)
                    return CardType.Interrupt;
                if (nf && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt) && UnityEngine.Random.value < 0.10f)
                    return CardType.Doubt;

                // 4) 공격 카드 없으면 가끔 리롤
                int atk = (I.HandHas(CardType.Betrayal) ? 1 : 0) + (I.HandHas(CardType.Pollution) ? 1 : 0);
                if (I.HandHas(CardType.Chaos) && (atk == 0 || (R % 2 == 0 && UnityEngine.Random.value < 0.25f)))
                    return CardType.Chaos;

                // 5) 기본 우선순위: 배신 > 오염 > 혼돈 > 인터럽트 > 협력 > 의심 > 정찰(거의 사용 안 함)
                CardType[] order = {
                    CardType.Betrayal, CardType.Pollution, CardType.Chaos,
                    CardType.Interrupt, CardType.Cooperation, CardType.Doubt, CardType.Recon
                };
                foreach (var c in order) if (I.HandHas(c)) return c;

                return CardType.None;
            });

            A.fallback = new[] {
                CardType.Betrayal, CardType.Pollution, CardType.Chaos,
                CardType.Interrupt, CardType.Cooperation, CardType.Doubt, CardType.Recon
            };
            // ---------- 선택 드로우(2장 중 1장) ----------
            A.chooseFromTwo = (a, b, I) =>
            {
                // Chaos는 선택 드로우 대상에서 제외되지만, 혹시 대비
                if (a == CardType.Chaos && b != CardType.Chaos) return 1;
                if (b == CardType.Chaos && a != CardType.Chaos) return 0;

                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var last = I.s.lastOpp;

                int Score(CardType x)
                {
                    int baseScore = x switch
                    {
                        CardType.Betrayal    => 100,
                        CardType.Pollution   => 80,
                        CardType.Interrupt   => 60,
                        CardType.Doubt       => 45,
                        CardType.Cooperation => 30,
                        CardType.Recon       => 10,
                        _ => 0
                    };

                    // 킬각/생존 보정
                    if (x == CardType.Betrayal && I.s.oppLife <= R + 1) baseScore += 25;
                    if (x == CardType.Doubt    && I.s.selfLife <= R)     baseScore += 20;

                    // 직전 행동 카운터 보정
                    if (nf)
                    {
                        if (last == CardType.Cooperation && x == CardType.Betrayal) baseScore += 25;
                        if (last == CardType.Pollution   && x == CardType.Doubt)     baseScore += 18;
                        if (last == CardType.Betrayal    && x == CardType.Interrupt) baseScore += 22;
                    }

                    // 손패에 공격 카드가 없으면 공격 우대
                    int atkInHand = (I.HandHas(CardType.Betrayal) ? 1 : 0) + (I.HandHas(CardType.Pollution) ? 1 : 0);
                    if ((x == CardType.Betrayal || x == CardType.Pollution) && atkInHand == 0) baseScore += 12;

                    return baseScore;
                }

                int sA = Score(a);
                int sB = Score(b);
                if (sA > sB) return 0;
                if (sB > sA) return 1;

                // 동점이면 배신/오염 우선, 그다음 임의
                if (a == CardType.Betrayal || a == CardType.Pollution) return 0;
                if (b == CardType.Betrayal || b == CardType.Pollution) return 1;
                return UnityEngine.Random.value < 0.5f ? 0 : 1;
            };
            return A;
        }

        // 한지혜 — 안정과 기회의 균형
        static Agent Build_한지혜()
        {
            var A = new Agent("한지혜");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                // 상대 분포(최근 히스토리 기반, 가벼운 스무딩)
                var p = new Dictionary<CardType, float> {
                    {CardType.Cooperation, 0.05f + I.Ratio(CardType.Cooperation)},
                    {CardType.Doubt,       0.05f + I.Ratio(CardType.Doubt)},
                    {CardType.Betrayal,    0.05f + I.Ratio(CardType.Betrayal)},
                    {CardType.Chaos,       0.05f + I.Ratio(CardType.Chaos)},
                    {CardType.Pollution,   0.05f + I.Ratio(CardType.Pollution)},
                    {CardType.Interrupt,   0.05f + I.Ratio(CardType.Interrupt)},
                    {CardType.Recon,       0.05f + I.Ratio(CardType.Recon)},
                };
                float S = p.Values.Sum(); foreach (var k in p.Keys.ToList()) p[k] /= S;

                // 0) 생존 우선(과도하지 않게)
                bool lethalRisk = I.s.selfLife <= R && (p[CardType.Betrayal] >= 0.27f || (nf && I.s.lastOpp == CardType.Betrayal));
                if (lethalRisk && I.HandHas(CardType.Doubt)) return CardType.Doubt;

                // 1) 반복 패턴 카운터
                if (nf && I.s.lastOpp == I.s.last2Opp && I.s.lastOpp != CardType.None)
                {
                    var x = I.s.lastOpp;
                    if (x == CardType.Cooperation && I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    if (x == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    if (x == CardType.Betrayal && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                    if (x == CardType.Doubt && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                    if (x == CardType.Chaos && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                }

                // 2) 초중반(1~4R): 정보/포지셔닝 반반
                if (R <= 4)
                {
                    // 안전 범위이며 분포가 섞여 있으면 Recon
                    bool safeInfo = I.s.selfLife >= I.s.oppLife - 1 && p[CardType.Betrayal] <= 0.26f;
                    bool mixed = p.Values.Max() < 0.40f;
                    if (I.HandHas(CardType.Recon) && safeInfo && mixed) return CardType.Recon;

                    // 협력 성향↑ & 의심 낮음 → Pollution로 장기 압박
                    if (I.HandHas(CardType.Pollution) &&
                        (p[CardType.Cooperation] >= 0.32f || (nf && I.s.lastOpp == CardType.Cooperation)) &&
                        p[CardType.Doubt] <= 0.28f)
                        return CardType.Pollution;

                    // 초반 안정 수급
                    if (I.HandHas(CardType.Cooperation) && p[CardType.Betrayal] <= 0.24f)
                        return CardType.Cooperation;
                }

                // 3) 중후반: 상황 균형 선택
                // 내가 앞서면 안전(협력/오염), 뒤지면 변동성(혼돈) 혹은 역전(배신/오염)
                bool leading = I.s.selfLife >= I.s.oppLife + 1;
                int atk = (I.HandHas(CardType.Betrayal) ? 1 : 0) + (I.HandHas(CardType.Pollution) ? 1 : 0);

                if (leading)
                {
                    if (I.HandHas(CardType.Pollution) && p[CardType.Doubt] <= 0.30f) return CardType.Pollution;
                    if (I.HandHas(CardType.Cooperation) && p[CardType.Betrayal] <= 0.26f) return CardType.Cooperation;
                }
                else
                {
                    // 손패 빈약 or 읽힘 반복 → 제한적 Chaos
                    if (I.HandHas(CardType.Chaos) && (atk <= 1 || (nf && I.s.lastOpp == I.s.last2Opp)))
                        return CardType.Chaos;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 4) 킬각만 배신
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.32f)
                    return CardType.Betrayal;

                // 5) 일반 우선순위(균형형)
                CardType[] order = {
                    CardType.Pollution, CardType.Cooperation, CardType.Doubt,
                    CardType.Betrayal,  CardType.Recon,       CardType.Chaos,
                    CardType.Interrupt
                };
                foreach (var c in order) if (I.HandHas(c)) return c;

                return CardType.None;
            });

            // 균형형 예비 우선순위
            A.fallback = new[] {
                CardType.Pollution, CardType.Cooperation, CardType.Doubt,
                CardType.Betrayal,  CardType.Recon,       CardType.Chaos,
                CardType.Interrupt
            };
            // ─────────────────────────────────────────────────────────────
            // [선택 드로우] 한지혜는 초반 안정/정보, 상대 협력엔 오염, 위험 땐 방어를 선호
            //   서명: Func<DecisionInput, CardType?, CardType?, CardType?>
            //   프로젝트에서 사용 중인 델리게이트명이 다르면 동일 서명으로 교체해 연결하세요.
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                int R = Math.Max(1, I.s.round);

                int Score(CardType c)
                {
                    if (c == CardType.Chaos) return -3;                         // 패 채움 단계에서 Chaos 기피
                    if (c == CardType.Cooperation) return (R <= 6 ? 3 : 2);     // 초중반 선호
                    if (c == CardType.Doubt) return 2;                           // 안정
                    if (c == CardType.Pollution) return 1;                       // 견제
                    if (c == CardType.Interrupt) return 1;                       // 상황용
                    if (c == CardType.Betrayal) return (I.s.oppLife <= R ? 3 : 0); // 킬각만 가점
                    if (c == CardType.Recon) return (R <= 5 ? 1 : 0);            // 초중반만 약간 가점
                    return 0;
                }

                // 손패 균형 보정
                bool needAtk = !(I.HandHas(CardType.Betrayal) || I.HandHas(CardType.Pollution));
                bool needDef = !(I.HandHas(CardType.Doubt) || I.HandHas(CardType.Interrupt));

                int sa = Score(a);
                int sb = Score(b);

                if (sa == sb)
                {
                    if (needAtk && ((a == CardType.Betrayal || a == CardType.Pollution) ||
                                    (b == CardType.Betrayal || b == CardType.Pollution)))
                        return (a == CardType.Betrayal || a == CardType.Pollution) ? 0 : 1;

                    if (needDef && ((a == CardType.Doubt || a == CardType.Interrupt) ||
                                    (b == CardType.Doubt || b == CardType.Interrupt)))
                        return (a == CardType.Doubt || a == CardType.Interrupt) ? 0 : 1;
                }

                return sa >= sb ? 0 : 1;   // 0이면 a, 1이면 b 선택
            };
            // ─────────────────────────────────────────────────────────────
            return A;
        }

        // 박민재 — 순수 계산형 + 재해(변동성) 인지 보정
        static Agent Build_박민재()
        {
            var A = new Agent("박민재");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);

                // --- 0) 재해 리스크 휴리스틱 ---
                // 블록 전환 라운드(5의 배수)와 직후 1턴은 변동성 ↑ 로 간주
                bool highVol = (R % 5 == 0) || (R % 5 == 1);

                // --- 1) 상대 분포 추정 p[t] ---
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
                float S = p.Values.Sum(); if (S <= 0) S = 1f;
                foreach (var k in p.Keys.ToList()) p[k] /= S;

                // --- 2) 즉사/생존 ---
                bool lethalNow = I.HandHas(CardType.Betrayal) && I.s.oppLife <= R;
                if (lethalNow && p[CardType.Doubt] < 0.33f) return CardType.Betrayal;

                bool lethalRisk = I.s.selfLife <= R && p[CardType.Betrayal] >= 0.28f;
                if (lethalRisk && I.HandHas(CardType.Doubt)) return CardType.Doubt;

                // --- 3) 손패 품질/정보가치 ---
                int HandScore(List<CardType> h)
                {
                    int s = 0;
                    foreach (var c in h)
                    {
                        if (c == CardType.Betrayal) s += 3;
                        else if (c == CardType.Pollution) s += 2;
                        else if (c == CardType.Cooperation || c == CardType.Doubt) s += 1;
                        else if (c == CardType.Interrupt) s += 0;
                        else if (c == CardType.Recon) s += 0;
                        else if (c == CardType.Chaos) s -= 1;
                    }
                    return s;
                }
                int hs = HandScore(I.hand);
                bool poorAttack = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);

                // 재해 변동성↑ 구간에서는 Chaos 가치를 낮춘다(강풍/한파 리스크 대비)
                if (I.HandHas(CardType.Recon) && (poorAttack || hs <= 1))
                    return CardType.Recon;

                if (I.HandHas(CardType.Chaos) && !highVol && (hs <= 1 || poorAttack || (!I.s.IsFirst && I.s.lastOpp == I.s.last2Opp)))
                    return CardType.Chaos;

                // --- 4) 기대값 최대화(상대-행동 분포 p에 대한 EV) ---
                int Delta(CardType a, CardType b)
                {
                    int r = R;
                    // Cooperation
                    if (a == CardType.Cooperation && b == CardType.Cooperation) return 0;
                    if (a == CardType.Cooperation && b == CardType.Doubt) return +1;
                    if (a == CardType.Cooperation && b == CardType.Betrayal) return -(r + 1);
                    if (a == CardType.Cooperation && b == CardType.Chaos) return +1;
                    if (a == CardType.Cooperation && b == CardType.Pollution) return -2;
                    if (a == CardType.Cooperation && b == CardType.Interrupt) return +2;
                    if (a == CardType.Cooperation && b == CardType.Recon) return +1;

                    // Doubt
                    if (a == CardType.Doubt && b == CardType.Cooperation) return -1;
                    if (a == CardType.Doubt && b == CardType.Doubt) return 0;
                    if (a == CardType.Doubt && b == CardType.Betrayal) return r + 1;
                    if (a == CardType.Doubt && b == CardType.Chaos) return 0;
                    if (a == CardType.Doubt && b == CardType.Pollution) return +1;
                    if (a == CardType.Doubt && b == CardType.Interrupt) return -1;
                    if (a == CardType.Doubt && b == CardType.Recon) return 0;

                    // Betrayal
                    if (a == CardType.Betrayal && b == CardType.Cooperation) return r + 1;
                    if (a == CardType.Betrayal && b == CardType.Doubt) return -(r + 1);
                    if (a == CardType.Betrayal && b == CardType.Betrayal) return -2 * r;
                    if (a == CardType.Betrayal && b == CardType.Chaos) return r + 1;
                    if (a == CardType.Betrayal && b == CardType.Pollution) return r + 1;
                    if (a == CardType.Betrayal && b == CardType.Interrupt) return r;
                    if (a == CardType.Betrayal && b == CardType.Recon) return r + 1;

                    // Chaos
                    if (a == CardType.Chaos && b == CardType.Cooperation) return -1;
                    if (a == CardType.Chaos && b == CardType.Doubt) return 0;
                    if (a == CardType.Chaos && b == CardType.Betrayal) return -(r + 1);
                    if (a == CardType.Chaos && b == CardType.Chaos) return 0;
                    if (a == CardType.Chaos && b == CardType.Pollution) return 0;
                    if (a == CardType.Chaos && b == CardType.Interrupt) return -1;
                    if (a == CardType.Chaos && b == CardType.Recon) return 0;

                    // Pollution
                    if (a == CardType.Pollution && b == CardType.Cooperation) return +2;
                    if (a == CardType.Pollution && b == CardType.Doubt) return -1;
                    if (a == CardType.Pollution && b == CardType.Betrayal) return -(r + 1);
                    if (a == CardType.Pollution && b == CardType.Chaos) return 0;
                    if (a == CardType.Pollution && b == CardType.Pollution) return 0;
                    if (a == CardType.Pollution && b == CardType.Interrupt) return 0;
                    if (a == CardType.Pollution && b == CardType.Recon) return -1;

                    // Interrupt
                    if (a == CardType.Interrupt && b == CardType.Cooperation) return -2;
                    if (a == CardType.Interrupt && b == CardType.Doubt) return +2;
                    if (a == CardType.Interrupt && b == CardType.Betrayal) return +2;
                    if (a == CardType.Interrupt && b == CardType.Chaos) return -1;
                    if (a == CardType.Interrupt && b == CardType.Pollution) return +2;
                    if (a == CardType.Interrupt && b == CardType.Interrupt) return 0;
                    if (a == CardType.Interrupt && b == CardType.Recon) return +1;

                    // Recon
                    if (a == CardType.Recon && b == CardType.Cooperation) return -1;
                    if (a == CardType.Recon && b == CardType.Doubt) return 0;
                    if (a == CardType.Recon && b == CardType.Betrayal) return -(r + 1);
                    if (a == CardType.Recon && b == CardType.Chaos) return 0;
                    if (a == CardType.Recon && b == CardType.Pollution) return -1;
                    if (a == CardType.Recon && b == CardType.Interrupt) return -1;
                    if (a == CardType.Recon && b == CardType.Recon) return 0;
                    return 0;
                }

                var cand = I.hand.Distinct().Where(I.HandHas).ToList();
                CardType best = CardType.None; float bestEV = float.NegativeInfinity;

                foreach (var a in cand)
                {
                    float ev = 0f; foreach (var b in p.Keys) ev += p[b] * Delta(a, b);

                    // 막판 보정
                    if (a == CardType.Betrayal && I.s.oppLife <= R + 1) ev += p[CardType.Cooperation] * 2.5f;

                    // 생존/재해 리스크 패널티
                    if (I.s.selfLife <= R) ev -= p[CardType.Betrayal] * 3.0f;
                    if (highVol && a == CardType.Betrayal) ev -= 0.6f;         // 강풍/낙뢰 등 변동성에 취약
                    if (highVol && a == CardType.Chaos) ev -= 0.8f;         // 한파·강풍 리스크 고려
                    if (highVol && a == CardType.Pollution && p[CardType.Doubt] >= 0.30f) ev -= 0.4f;

                    if (ev > bestEV) { bestEV = ev; best = a; }
                }

                // 읽힘 회피: 2순위가 근접하면 소폭 스왑
                var alt = cand.Where(t => t != best)
                            .OrderByDescending(t => { float e = 0; foreach (var b in p.Keys) e += p[b] * Delta(t, b); return e; })
                            .ToList();
                if (alt.Count > 0)
                {
                    float secondEV = 0; foreach (var b in p.Keys) secondEV += p[b] * Delta(alt[0], b);
                    if (secondEV > bestEV - 0.55f && UnityEngine.Random.value < 0.15f) best = alt[0];
                }

                return best;
            });

            A.fallback = new[]
            {
                CardType.Betrayal, CardType.Pollution, CardType.Cooperation,
                CardType.Recon, CardType.Doubt, CardType.Chaos, CardType.Interrupt
            };
            // 선택 드로우: 두 후보(a,b) 중 기대가치가 높은 쪽을 선택
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                int R = Math.Max(1, I.s.round);
                bool losing = I.s.selfLife < I.s.oppLife;
                bool poorAtk = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);

                // 관찰분포(라플라스 0.06)로 상대 성향 추정
                float P(CardType t) =>
                    0.06f + I.Ratio(t);
                float norm =
                    P(CardType.Cooperation)+P(CardType.Doubt)+P(CardType.Betrayal)+
                    P(CardType.Chaos)+P(CardType.Pollution)+P(CardType.Interrupt)+P(CardType.Recon);
                float Q(CardType t) => P(t) / (norm > 0 ? norm : 1f);

                // 카드 기대가치 휴리스틱(계산형)
                float V(CardType c)
                {
                    switch (c)
                    {
                        case CardType.Betrayal:
                            return (I.s.oppLife <= R ? 7f : 3f) - 4f * Q(CardType.Doubt);   // 킬각 가중, 의심 확률 패널티
                        case CardType.Pollution:
                            return 3f + 2f * (1f - Q(CardType.Doubt));                      // 상대 의심 낮을수록 가치↑
                        case CardType.Doubt:
                            return 6f * Q(CardType.Betrayal) - 0.5f;                        // 배신 확률 방어
                        case CardType.Cooperation:
                            return 2f - 2f * Q(CardType.Betrayal);                          // 배신 가능성에 취약
                        case CardType.Interrupt:
                            return 1f + 3f * Q(CardType.Betrayal);                          // 카운터 수단
                        case CardType.Recon:
                            return (poorAtk || losing) ? 2.5f : 0.5f;                       // 손패/상황이 나쁠 때만 정보가치↑
                        case CardType.Chaos:
                            return (losing ? 1.0f : -1.5f) + (R >= 3 ? 0.3f : -0.3f);       // 변동성은 뒤질 때만 제한적으로
                        default:
                            return 0f;
                    }
                }

                float va = V(a), vb = V(b);
                if (Math.Abs(va - vb) < 0.001f) return UnityEngine.Random.value < 0.5f ? 0 : 1;
                return va >= vb ? 0 : 1;
            };
            return A;
        }

        // 정다은 — 심리전 마스터: 패턴 파악 → 카운터 → 결정타
        static Agent Build_정다은()
        {
            var A = new Agent("정다은");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                // 0) 위험 최소: 즉사 위험이면 Doubt
                if (I.HandHas(CardType.Doubt))
                {
                    bool lethalRisk = I.s.selfLife <= R &&
                                    (I.Ratio(CardType.Betrayal) >= 0.28f || (nf && I.s.lastOpp == CardType.Betrayal));
                    if (lethalRisk) return CardType.Doubt;
                }

                // 1) 상대 패턴 모델링: 분포 + 연속 사용 가중
                var p = new Dictionary<CardType, float>
                {
                    {CardType.Cooperation, 0.06f + I.Ratio(CardType.Cooperation)},
                    {CardType.Doubt,       0.06f + I.Ratio(CardType.Doubt)},
                    {CardType.Betrayal,    0.06f + I.Ratio(CardType.Betrayal)},
                    {CardType.Chaos,       0.06f + I.Ratio(CardType.Chaos)},
                    {CardType.Pollution,   0.06f + I.Ratio(CardType.Pollution)},
                    {CardType.Interrupt,   0.06f + I.Ratio(CardType.Interrupt)},
                    {CardType.Recon,       0.06f + I.Ratio(CardType.Recon)}
                };
                if (nf && I.s.lastOpp != CardType.None)
                {
                    // 같은 카드 2연속이면 그 카드 재현성 상승
                    if (I.s.lastOpp == I.s.last2Opp) p[I.s.lastOpp] *= 1.40f;
                    // 초반 협력 성향은 추가 가중
                    if (R <= 3 && I.s.lastOpp == CardType.Cooperation) p[CardType.Cooperation] *= 1.18f;
                }
                float S = p.Values.Sum(); foreach (var k in p.Keys.ToList()) p[k] /= S;

                // 2) 즉시 카운터: 연속 패턴은 강하게 응징
                if (nf && I.s.lastOpp == I.s.last2Opp && I.s.lastOpp != CardType.None)
                {
                    var x = I.s.lastOpp;
                    if (x == CardType.Cooperation && I.HandHas(CardType.Betrayal)) return CardType.Betrayal; // 허 찌르기
                    if (x == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    if (x == CardType.Betrayal && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                    if (x == CardType.Doubt && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                    if (x == CardType.Chaos && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                }

                // 3) 정보 우선: 초중반 안전할 때 Recon으로 상대 성향 고정
                bool safeInfo = R <= 4 && I.s.selfLife >= I.s.oppLife - 1 && p[CardType.Betrayal] <= 0.26f;
                bool mixed = p.Values.Max() < 0.42f; // 한쪽 치우침이 약함
                if (I.HandHas(CardType.Recon) && safeInfo && mixed) return CardType.Recon;

                // 4) 장기 포지셔닝: 협력 성향↑ + 의심 낮음 → 오염으로 압박
                if (I.HandHas(CardType.Pollution) &&
                    (p[CardType.Cooperation] >= 0.33f || (nf && I.s.lastOpp == CardType.Cooperation)) &&
                    p[CardType.Doubt] <= 0.28f)
                    return CardType.Pollution;

                // 5) 결정타 타이밍: 의심 확률이 낮고 상대 체력이 라운드 이내면 배신
                if (I.HandHas(CardType.Betrayal) &&
                    I.s.oppLife <= R &&
                    p[CardType.Doubt] < 0.32f)
                    return CardType.Betrayal;

                // 6) 손패가 빈약하거나 읽힘이 심하면 제한적 Chaos로 패턴 교란
                int atk = (I.HandHas(CardType.Betrayal) ? 1 : 0) + (I.HandHas(CardType.Pollution) ? 1 : 0);
                if (I.HandHas(CardType.Chaos) && (atk == 0 || (nf && I.s.lastOpp == I.s.last2Opp)))
                    return CardType.Chaos;

                // 7) 기본 우선순위: 카운터 중심의 균형
                CardType[] prio = {
                    CardType.Pollution,   // 장기 압박
                    CardType.Cooperation, // 안전 수급
                    CardType.Doubt,       // 방어
                    CardType.Betrayal,    // 기회 포착
                    CardType.Interrupt,   // 틈새 역공
                    CardType.Recon,       // 정보
                    CardType.Chaos        // 최후 교란
                };
                foreach (var c in prio) if (I.HandHas(c)) return c;
                
                return CardType.None;
            });

            A.fallback = new[] {
                CardType.Pollution, CardType.Cooperation, CardType.Doubt,
                CardType.Betrayal,  CardType.Interrupt,   CardType.Recon, CardType.Chaos
            };

            // 서명: (CardType a, CardType b, DecisionInput I) => int  // 0이면 a, 1이면 b
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                int idxOf(CardType t) => (a == t ? 0 : (b == t ? 1 : -1));
                bool HasAtk() => I.hand.Contains(CardType.Betrayal) || I.hand.Contains(CardType.Pollution);
                bool HasDef() => I.hand.Contains(CardType.Doubt) || I.hand.Contains(CardType.Interrupt);

                // Chaos는 선택 드로우 제외(후보 한쪽만 Chaos면 다른 쪽 선택)
                if (a == CardType.Chaos && b != CardType.Chaos) return 1;
                if (b == CardType.Chaos && a != CardType.Chaos) return 0;

                // 상대 분포 추정
                var p = new Dictionary<CardType, float>
                {
                    {CardType.Cooperation, 0.06f + I.Ratio(CardType.Cooperation)},
                    {CardType.Doubt,       0.06f + I.Ratio(CardType.Doubt)},
                    {CardType.Betrayal,    0.06f + I.Ratio(CardType.Betrayal)},
                    {CardType.Chaos,       0.06f + I.Ratio(CardType.Chaos)},
                    {CardType.Pollution,   0.06f + I.Ratio(CardType.Pollution)},
                    {CardType.Interrupt,   0.06f + I.Ratio(CardType.Interrupt)},
                    {CardType.Recon,       0.06f + I.Ratio(CardType.Recon)}
                };
                if (nf && I.s.lastOpp != CardType.None && I.s.lastOpp == I.s.last2Opp)
                    p[I.s.lastOpp] *= 1.35f;
                float S = p.Values.Sum(); foreach (var k in p.Keys.ToList()) p[k] /= (S <= 0 ? 1f : S);

                // 0) 즉사 위험 시 방어 확보
                bool lethalRisk = I.s.selfLife <= R && (p[CardType.Betrayal] >= 0.28f || (nf && I.s.lastOpp == CardType.Betrayal));
                if (lethalRisk)
                {
                    int i = idxOf(CardType.Doubt);     if (i >= 0) return i;
                    i = idxOf(CardType.Interrupt);     if (i >= 0) return i;
                }

                // 1) 연속 패턴 카운터 카드 우선 확보
                if (nf && I.s.lastOpp == I.s.last2Opp && I.s.lastOpp != CardType.None)
                {
                    var x = I.s.lastOpp;
                    int want = -1;
                    if (x == CardType.Cooperation) want = idxOf(CardType.Betrayal);
                    else if (x == CardType.Pollution) want = idxOf(CardType.Doubt);
                    else if (x == CardType.Betrayal)  want = idxOf(CardType.Interrupt);
                    else if (x == CardType.Doubt || x == CardType.Chaos) want = idxOf(CardType.Cooperation);
                    if (want >= 0) return want;
                }

                // 2) 초중반 안전 시 정보 선호
                bool safeInfo = R <= 4 && I.s.selfLife >= I.s.oppLife - 1 && p[CardType.Betrayal] <= 0.26f;
                if (safeInfo)
                {
                    int i = idxOf(CardType.Recon); if (i >= 0) return i;
                }

                // 3) 협력 성향↑ + 의심 낮음 → 오염 확보
                if ((p[CardType.Cooperation] >= 0.33f || (nf && I.s.lastOpp == CardType.Cooperation)) &&
                    p[CardType.Doubt] <= 0.28f)
                {
                    int i = idxOf(CardType.Pollution); if (i >= 0) return i;
                }

                // 4) 킬각 대비 카드 확보
                if (I.s.oppLife <= R && p[CardType.Doubt] < 0.32f)
                {
                    int i = idxOf(CardType.Betrayal); if (i >= 0) return i;
                }

                // 5) 손패 균형 보정
                if (!HasAtk())
                {
                    int i = idxOf(CardType.Pollution); if (i >= 0) return i;
                    i = idxOf(CardType.Betrayal);      if (i >= 0) return i;
                }
                if (!HasDef())
                {
                    int i = idxOf(CardType.Doubt);     if (i >= 0) return i;
                    i = idxOf(CardType.Interrupt);     if (i >= 0) return i;
                }

                // 6) 기본 선호 스코어(장기 압박/안전/카운터 지향)
                float Score(CardType c)
                {
                    float s = 0f;
                    if (c == CardType.Pollution)   s += 2.6f - 1.2f * p[CardType.Doubt] + 1.0f * p[CardType.Cooperation];
                    if (c == CardType.Cooperation) s += 1.6f - 1.0f * p[CardType.Betrayal];
                    if (c == CardType.Doubt)       s += 1.8f * p[CardType.Betrayal];
                    if (c == CardType.Interrupt)   s += 1.2f * p[CardType.Betrayal];
                    if (c == CardType.Betrayal)    s += (I.s.oppLife <= R ? 2.4f : 0.8f) - 1.8f * p[CardType.Doubt];
                    if (c == CardType.Recon)       s += (R <= 5 ? 0.8f : 0.0f);
                    return s;
                }
                float sa = Score(a), sb = Score(b);
                if (!Mathf.Approximately(sa, sb)) return sa > sb ? 0 : 1;

                // 동점: 약점 보완 우선 → 그다음 임의
                if (!HasAtk()) return (a == CardType.Betrayal || a == CardType.Pollution) ? 0 : 1;
                if (!HasDef()) return (a == CardType.Doubt || a == CardType.Interrupt) ? 0 : 1;
                return UnityEngine.Random.value < 0.5f ? 0 : 1;
            };
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
            // 오태훈 — 선택 드로우(공격 성향, 리스크 선호)
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                // 선택 드로우 대상에서 Chaos는 제외(둘 중 하나만 Chaos면 다른 쪽 선택)
                if (a == CardType.Chaos && b != CardType.Chaos) return 1;
                if (b == CardType.Chaos && a != CardType.Chaos) return 0;

                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                bool NeedAtk() => !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);
                bool Losing()  => I.s.selfLife < I.s.oppLife;
                bool LethalRisk() => I.s.selfLife <= R;

                // 최근 패턴 보정
                var last  = I.s.lastOpp;
                var last2 = I.s.last2Opp;
                bool repeat = nf && last != CardType.None && last == last2;

                int Score(CardType x)
                {
                    int s = x switch
                    {
                        CardType.Betrayal    => 100,
                        CardType.Pollution   => 80,
                        CardType.Recon       => 35,
                        CardType.Cooperation => 25,
                        CardType.Interrupt   => 20,
                        CardType.Doubt       => 10,
                        _ => 0
                    };

                    // 초반 러시/킬각 가중
                    if (x == CardType.Betrayal)
                    {
                        if (R <= 2) s += 18;
                        if (I.s.oppLife <= R + 1) s += 28;
                    }
                    if (x == CardType.Pollution && R <= 2) s += 10;

                    // 손패에 공격수단이 없으면 가중
                    if (NeedAtk() && (x == CardType.Betrayal || x == CardType.Pollution)) s += 16;

                    // 지는 중이면 공격 선호, 협력/수비 패널티
                    if (Losing())
                    {
                        if (x == CardType.Betrayal || x == CardType.Pollution) s += 12;
                        if (x == CardType.Cooperation) s -= 6;
                        if (x == CardType.Recon) s += 6;
                    }

                    // 즉사 위험 시 최소 방어 허용
                    if (LethalRisk() && x == CardType.Doubt) s += 40;

                    // 반복 패턴 강한 처벌
                    if (repeat)
                    {
                        if (last == CardType.Cooperation && x == CardType.Betrayal) s += 25;
                        if (last == CardType.Pollution && x == CardType.Doubt) s += 14;
                        if (last == CardType.Betrayal && x == CardType.Interrupt) s += 18;
                    }

                    return s;
                }

                int sa = Score(a);
                int sb = Score(b);
                if (sa != sb) return sa > sb ? 0 : 1;

                // 동점이면 공격 카드 우선 → 그다음 임의
                bool aOff = (a == CardType.Betrayal || a == CardType.Pollution);
                bool bOff = (b == CardType.Betrayal || b == CardType.Pollution);
                if (aOff != bOff) return aOff ? 0 : 1;
                return UnityEngine.Random.value < 0.5f ? 0 : 1;
            };
            return A;
        }

        // 유민정 — 초수비 / 상대 따라가기
        static Agent Build_유민정()
        {
            var A = new Agent("유민정");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool second = !I.s.IsFirst;

                // 0) 생존 최우선: 위기면 Doubt
                if (I.HandHas(CardType.Doubt) &&
                    (I.s.selfLife <= R || I.s.selfLife + 1 < I.s.oppLife))
                    return CardType.Doubt;

                // 1) 직전 대응(수비 카운터만 사용)
                if (second)
                {
                    if (I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt))
                        return CardType.Doubt;                       // 오염엔 Doubt
                    if (I.s.lastOpp == CardType.Betrayal && I.HandHas(CardType.Interrupt))
                        return CardType.Interrupt;                   // 배신엔 Interrupt
                    if (I.s.lastOpp == CardType.Interrupt && I.HandHas(CardType.Cooperation))
                        return CardType.Cooperation;                 // 인터럽트엔 회복
                    // 미러링(배신은 제외)
                    if (I.HandHas(I.s.lastOpp) && I.s.lastOpp != CardType.Betrayal)
                        return I.s.lastOpp;
                }

                // 2) 뒤질 때 회복 성향
                if (I.s.selfLife < I.s.oppLife && I.HandHas(CardType.Cooperation))
                    return CardType.Cooperation;

                // 3) 상대가 협력 성향↑ → 안전 견제(Pollution)
                if ((I.Ratio(CardType.Cooperation) >= 0.35f ||
                    (second && I.s.lastOpp == CardType.Cooperation)) &&
                    I.Ratio(CardType.Doubt) < 0.28f && I.HandHas(CardType.Pollution))
                    return CardType.Pollution;

                // 4) 정보 수집만 하는 소극적 Recon
                if (I.HandHas(CardType.Recon) &&
                    !I.HandHas(CardType.Doubt) && !I.HandHas(CardType.Cooperation) &&
                    !I.HandHas(CardType.Interrupt))
                    return CardType.Recon;

                // 5) 손패가 완전 막힘 → 드물게 Chaos로 리셋
                if (I.HandHas(CardType.Chaos) &&
                    !I.HandHas(CardType.Doubt) && !I.HandHas(CardType.Cooperation) &&
                    !I.HandHas(CardType.Interrupt) && !I.HandHas(CardType.Recon))
                    return CardType.Chaos;

                // 6) 진짜 킬각일 때만 Betrayal 허용
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && I.s.selfLife > 1)
                    return CardType.Betrayal;

                // 7) 기본 우선순위(초수비)
                A.fallback = new[]
                {
                    CardType.Doubt, CardType.Cooperation, CardType.Interrupt,
                    CardType.Pollution, CardType.Recon, CardType.Chaos, CardType.Betrayal
                };
                return null;
            });
            // 선택 드로우(두 장 중 1장)
            A.chooseFromTwo = (c0, c1, I) =>
            {
                int Score(CardType t)
                {
                    int s = t switch
                    {
                        CardType.Doubt        => 50,   // 수비 최우선
                        CardType.Cooperation  => 45,   // 추종·유지
                        CardType.Interrupt    => 30,   // 배신 카운터
                        CardType.Recon        => 25,   // 초중반 드물게
                        CardType.Pollution    => 15,   // 가벼운 견제
                        CardType.Betrayal     => 8,    // 거의 안 씀
                        CardType.Chaos        => 0,
                        _ => 0
                    };

                    // 상황 보정
                    if (I.s.selfLife < I.s.oppLife && t == CardType.Doubt) s += 10;               // 열세면 더 수비
                    if (!I.s.IsFirst && I.s.lastOpp == CardType.Cooperation && t == CardType.Cooperation) s += 6; // 추종
                    if (!I.s.IsFirst && I.s.lastOpp == CardType.Betrayal && (t == CardType.Doubt || t == CardType.Interrupt)) s += 12; // 배신 대응
                    if (I.s.oppLife <= Math.Max(1, I.s.round) && t == CardType.Betrayal) s += 12; // 확실한 킬각만 배신
                    return s;
                }

                int s0 = Score(c0), s1 = Score(c1);
                if (s0 == s1)
                {
                    // 동점이면 더 안전한 쪽 우선
                    int safe(CardType t) => t switch
                    {
                        CardType.Doubt => 3, CardType.Cooperation => 2, CardType.Interrupt => 1, _ => 0
                    };
                    return safe(c0) >= safe(c1) ? 0 : 1;
                }
                return s0 > s1 ? 0 : 1;
            };
            return A;
        }

        // 김태양 — 즉흥/무작위 성향, 상대 분석 안 함
        static Agent Build_김태양()
        {
            var A = new Agent("김태양");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool notFirst = !I.s.IsFirst;

                // 0) 아주 드물게 즉흥적으로 손패 아무 카드
                if (I.hand.Count > 0 && UnityEngine.Random.value < 0.06f)
                    return I.hand[UnityEngine.Random.Range(0, I.hand.Count)];

                // 1) 단순 킬각만 본다(계산 최소)
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R) return CardType.Betrayal;

                // 2) 가벼운 카운터들만 허용
                if (notFirst)
                {
                    if (I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Betrayal) && UnityEngine.Random.value < 0.55f)
                        return CardType.Betrayal;
                    if (I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt) && UnityEngine.Random.value < 0.55f)
                        return CardType.Doubt;
                    if (I.s.lastOpp == CardType.Betrayal && I.HandHas(CardType.Interrupt) && UnityEngine.Random.value < 0.55f)
                        return CardType.Interrupt;

                    // 같은 카드가 연속으로 보이면 장난치듯 뒤집기 or 따라하기 중 하나
                    if (I.s.lastOpp == I.s.last2Opp)
                    {
                        if (UnityEngine.Random.value < 0.50f)
                        {
                            // 단순 뒤집기 우선순위
                            var x = I.s.lastOpp;
                            if (x == CardType.Cooperation && I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                            if (x == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                            if (x == CardType.Doubt && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                            if (x == CardType.Interrupt && I.HandHas(CardType.Pollution)) return CardType.Pollution;
                        }
                        else if (I.HandHas(I.s.lastOpp)) return I.s.lastOpp; // 따라하기
                    }
                }

                // 3) 라운드 초반에는 공격적 무작위
                if (R <= 3 && UnityEngine.Random.value < 0.70f)
                {
                    var pool = new List<CardType>();
                    if (I.HandHas(CardType.Chaos)) pool.Add(CardType.Chaos);
                    if (I.HandHas(CardType.Pollution)) pool.Add(CardType.Pollution);
                    if (I.HandHas(CardType.Betrayal)) pool.Add(CardType.Betrayal);
                    if (pool.Count > 0) return pool[UnityEngine.Random.Range(0, pool.Count)];
                }

                // 4) 주기적으로 혼돈을 섞어 판 흔들기
                if (I.HandHas(CardType.Chaos) && (R % 3 == 0 || UnityEngine.Random.value < 0.20f))
                    return CardType.Chaos;

                // 5) 간단한 생존 반사 신경
                if (I.s.selfLife <= R && I.HandHas(CardType.Doubt) && UnityEngine.Random.value < 0.60f)
                    return CardType.Doubt;

                // 6) 남은 카드에서 무작위 가중치 선택(공격 성향 가중)
                {
                    var bag = new List<CardType>();
                    void Push(CardType t, int w)
                    {
                        if (!I.HandHas(t)) return;
                        for (int k = 0; k < w; ++k) bag.Add(t);
                    }
                    Push(CardType.Betrayal, 4);
                    Push(CardType.Pollution, 4);
                    Push(CardType.Chaos, 3);
                    Push(CardType.Interrupt, 2);
                    Push(CardType.Cooperation, 1);
                    Push(CardType.Doubt, 1);
                    Push(CardType.Recon, 1);

                    if (bag.Count > 0 && UnityEngine.Random.value < 0.75f)
                        return bag[UnityEngine.Random.Range(0, bag.Count)];
                }

                // 7) 마지막 보험: 손패 무작위 1장
                if (I.hand.Count > 0)
                    return I.hand[UnityEngine.Random.Range(0, I.hand.Count)];

                return (CardType?)null;
            });

            // 기본 낙하 우선순위(공격적)
            A.fallback = new[]
            {
                CardType.Betrayal, CardType.Pollution, CardType.Chaos,
                CardType.Interrupt, CardType.Doubt, CardType.Cooperation, CardType.Recon
            };
            // 선택 드로우(2장 중 1장): 무작위 편향 + 교란
            // 반환: 0 => a 선택, 1 => b 선택
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                bool losing = I.s.selfLife < I.s.oppLife;
                bool needAtk = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);

                // Chaos가 한쪽만 제시되면 40% 확률로 Chaos 선택해 교란
                if (a == CardType.Chaos && b != CardType.Chaos)
                    return UnityEngine.Random.value < 0.40f ? 0 : 1;
                if (b == CardType.Chaos && a != CardType.Chaos)
                    return UnityEngine.Random.value < 0.40f ? 1 : 0;

                int Score(CardType x)
                {
                    int s = x switch
                    {
                        CardType.Betrayal    => 70,
                        CardType.Pollution   => 55,
                        CardType.Cooperation => 30,
                        CardType.Doubt       => 25,
                        CardType.Interrupt   => 22,
                        CardType.Recon       => 18,
                        CardType.Chaos       => 15,
                        _ => 0
                    };

                    // 지면 공격 선호, 회복 패널티
                    if (losing)
                    {
                        if (x == CardType.Betrayal || x == CardType.Pollution) s += 18;
                        if (x == CardType.Cooperation) s -= 8;
                    }

                    // 공격수단이 없으면 공격 가중
                    if (needAtk && (x == CardType.Betrayal || x == CardType.Pollution)) s += 16;

                    // 직전 패턴 약한 카운터(확률적 가중)
                    if (nf)
                    {
                        if (I.s.lastOpp == CardType.Cooperation && x == CardType.Betrayal) s += 12;
                        if (I.s.lastOpp == CardType.Pollution && x == CardType.Doubt) s += 8;
                        if (I.s.lastOpp == CardType.Betrayal && x == CardType.Interrupt) s += 10;
                    }

                    // 초반 러시 편향
                    if (R <= 2)
                    {
                        if (x == CardType.Betrayal) s += 10;
                        if (x == CardType.Pollution) s += 6;
                    }
                    return s;
                }

                int sa = Score(a);
                int sb = Score(b);

                // 10% 확률로 의도적 역선택(읽기 방지)
                if (UnityEngine.Random.value < 0.10f)
                    return sa <= sb ? 0 : 1;

                if (sa == sb)
                {
                    // 동점이면 공격 카드가 있으면 그쪽 60%
                    bool aOff = (a == CardType.Betrayal || a == CardType.Pollution);
                    bool bOff = (b == CardType.Betrayal || b == CardType.Pollution);
                    if (aOff != bOff) return UnityEngine.Random.value < 0.60f ? (aOff ? 0 : 1) : (aOff ? 1 : 0);
                    return UnityEngine.Random.value < 0.5f ? 0 : 1;
                }
                return sa > sb ? 0 : 1;
            };
            return A;
        }

        // 이하린 — 단순·모방·비효율
        static Agent Build_이하린()
        {
            var A = new Agent("이하린");

            // 귀엽/예쁨 우선순위(높음 → 낮음)
            CardType[] cuteOrder = {
                CardType.Cooperation, // 반짝이는 느낌
                CardType.Recon,       // 도구/그림
                CardType.Doubt,       // 파랑 톤
                CardType.Chaos,       // 보라 번쩍
                CardType.Pollution,   // 초록(보기 재미)
                CardType.Interrupt,   // 손바닥
                CardType.Betrayal     // 무서움 → 최하
            };

            A.rules.Add(I =>
            {
                // 0) 게임 상황/자연재해/체력/라운드 전부 무시

                // 1) 모방: 25% 확률로 직전 상대 카드 따라 하기
                if (!I.s.IsFirst && I.HandHas(I.s.lastOpp) && UnityEngine.Random.value < 0.25f)
                    return I.s.lastOpp;

                // 2) 예쁜 카드 고집: 손패에서 cuteOrder 순으로 첫 카드 선택
                foreach (var c in cuteOrder)
                    if (I.HandHas(c)) return c;

                // 3) 그래도 없으면 손패 임의 카드
                if (I.hand.Count > 0)
                    return I.hand[UnityEngine.Random.Range(0, I.hand.Count)];

                return (CardType?)null;
            });

            // 실패 시도 시에도 같은 순서로 소모
            A.fallback = cuteOrder;

            // 선택 드로우: 단순/모방/비효율. 0이면 a, 1이면 b
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var last = I.s.lastOpp;

                int idx(CardType t) => a == t ? 0 : (b == t ? 1 : -1);

                // 배신은 거의 피함
                if (a == CardType.Betrayal && b != CardType.Betrayal) return 1;
                if (b == CardType.Betrayal && a != CardType.Betrayal) return 0;

                // 위기면 약간 수비 우선
                bool danger = I.s.selfLife <= R - 1 &&
                            (I.Ratio(CardType.Betrayal) >= 0.28f || (nf && last == CardType.Betrayal));
                if (danger)
                {
                    int i = idx(CardType.Doubt); if (i >= 0) return i;
                    i = idx(CardType.Interrupt); if (i >= 0) return i;
                }

                // 직전 카드 따라가기(배신 제외)
                if (nf && last != CardType.None && last != CardType.Betrayal)
                {
                    int i = idx(last);
                    if (i >= 0) return i;
                }

                // 점수: 협력/정찰 선호, 혼돈은 가끔, 오염은 낮게
                float Score(CardType c)
                {
                    float s = c switch
                    {
                        CardType.Cooperation => 3.5f,
                        CardType.Recon       => 3.0f,
                        CardType.Interrupt   => 2.0f,
                        CardType.Doubt       => 1.8f,
                        CardType.Pollution   => 1.0f,
                        CardType.Chaos       => 0.8f,
                        CardType.Betrayal    => -1.0f,
                        _ => 0f
                    };
                    // 직전 카드와 같으면 가점
                    if (nf && c == last && c != CardType.Betrayal) s += 1.2f;
                    // 무작위성 소량
                    s += UnityEngine.Random.Range(-0.2f, 0.2f);
                    return s;
                }

                float sa = Score(a), sb = Score(b);
                if (Mathf.Approximately(sa, sb)) return UnityEngine.Random.value < 0.5f ? 0 : 1;
                return sa >= sb ? 0 : 1;
            };
            return A;
        }

        // 백무적 v3 — 베이지안+최근패턴+상대유형추정+EV⊕미니맥스 혼합+킬/생존/재해/읽힘 보정
        static Agent Build_백무적()
        {
            var A = new Agent("백무적");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                // 0) 즉시 생존(직전 위험 플래그)
                if (nf && I.s.lastOpp != CardType.None)
                {
                    bool oppAtk = (I.s.lastOpp == CardType.Betrayal || I.s.lastOpp == CardType.Pollution);
                    if (oppAtk && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                    if (I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                }

                // 1) 상대 분포 p[b]: 라플라스 + 최근 2턴 패턴 + 위기/재해 보정
                var p = new Dictionary<CardType, float> {
                    {CardType.Cooperation, 0.09f + I.Ratio(CardType.Cooperation)},
                    {CardType.Doubt,       0.09f + I.Ratio(CardType.Doubt)},
                    {CardType.Betrayal,    0.09f + I.Ratio(CardType.Betrayal)},
                    {CardType.Chaos,       0.09f + I.Ratio(CardType.Chaos)},
                    {CardType.Pollution,   0.09f + I.Ratio(CardType.Pollution)},
                    {CardType.Interrupt,   0.09f + I.Ratio(CardType.Interrupt)},
                    {CardType.Recon,       0.09f + I.Ratio(CardType.Recon)},
                };
                void W(CardType t, float m) { p[t] *= m; }

                if (nf && I.s.lastOpp != CardType.None)
                {
                    if (I.s.lastOpp == I.s.last2Opp) W(I.s.lastOpp, 1.60f);            // 2연속 재현
                    else                              W(I.s.lastOpp, 1.15f);            // 단발 재현
                    // 반응 경향(일반 메타) 보정
                    if (I.s.lastOpp == CardType.Cooperation){ W(CardType.Cooperation,1.12f); W(CardType.Betrayal,1.08f); W(CardType.Pollution,1.06f); }
                    if (I.s.lastOpp == CardType.Betrayal)   { W(CardType.Doubt,1.10f); W(CardType.Interrupt,1.05f); }
                    if (I.s.lastOpp == CardType.Pollution)  { W(CardType.Doubt,1.12f); }
                    if (I.s.lastOpp == CardType.Chaos)      { W(CardType.Interrupt,1.06f); W(CardType.Betrayal,1.05f); }
                }

                // 내 체력 위험시: 상대의 공격 확률을 과대평가 → 수비 EV가 자연히 올라가게 함
                if (I.s.selfLife <= R) { W(CardType.Betrayal,1.18f); W(CardType.Pollution,1.12f); }
                // 상대 저체력시: 상대의 방어/회피 경향을 약간 과대평가
                if (I.s.oppLife <= R)  { W(CardType.Doubt,1.06f); W(CardType.Interrupt,1.06f); }

                // 재해 갱신 라운드(5의 배수)는 공격성 억제
                if (R % 5 == 0){ W(CardType.Betrayal,0.84f); W(CardType.Chaos,0.90f); W(CardType.Doubt,1.10f); }

                // 상대 유형 추정(6턴 이후): 수비형/공격형/랜덤형
                float sum0 = p.Values.Sum(); foreach (var k in p.Keys.ToList()) p[k] = Mathf.Clamp01(p[k]/(sum0<=0?1:sum0));
                float atkLik = p[CardType.Betrayal] + p[CardType.Pollution];
                float defLik = p[CardType.Doubt] + p[CardType.Interrupt];
                float entropy = -p.Sum(kv => kv.Value > 0 ? kv.Value * Mathf.Log(kv.Value) : 0f); // nats

                if (R >= 6){
                    if (defLik >= 0.50f) { W(CardType.Doubt,1.08f); W(CardType.Interrupt,1.06f); }        // 수비형
                    if (atkLik >= 0.50f) { W(CardType.Betrayal,1.08f); W(CardType.Pollution,1.06f); }     // 공격형
                    if (entropy >= 1.8f) { W(CardType.Chaos,1.05f); W(CardType.Recon,1.05f); }            // 랜덤형
                }
                float S = p.Values.Sum(); foreach (var k in p.Keys.ToList()) p[k] = Mathf.Clamp01(p[k]/(S<=0?1:S));

                // 2) 손패 진단 + 불확실성 대응(Chaos/Recon 게이팅)
                int HandScore(List<CardType> h){
                    int s=0;
                    foreach(var c in h)
                        s += c switch{ CardType.Betrayal=>3, CardType.Pollution=>2, CardType.Interrupt=>1, CardType.Cooperation=>1, CardType.Doubt=>1, CardType.Chaos=>-1, _=>0 };
                    return s;
                }
                int atkCnt = (I.HandHas(CardType.Betrayal)?1:0) + (I.HandHas(CardType.Pollution)?1:0);
                if (I.HandHas(CardType.Chaos)){
                    bool poor = HandScore(I.hand) <= 1 || atkCnt == 0;
                    bool oppPredictable = entropy < 1.2f;
                    if (poor && !oppPredictable) return CardType.Chaos;
                }
                // 초반 안전 탐색
                if (R <= 3 && entropy >= 1.7f && I.HandHas(CardType.Recon)) return CardType.Recon;

                // 3) 킬각 최우선
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.33f) return CardType.Betrayal;

                // 4) 보상행렬
                int V(CardType a, CardType b){
                    int r=R;
                    return (a,b) switch{
                        (CardType.Cooperation, CardType.Cooperation)=>0,
                        (CardType.Cooperation, CardType.Doubt)=>+1,
                        (CardType.Cooperation, CardType.Betrayal)=>-(r+1),
                        (CardType.Cooperation, CardType.Chaos)=>+1,
                        (CardType.Cooperation, CardType.Pollution)=>-2,
                        (CardType.Cooperation, CardType.Interrupt)=>-2,

                        (CardType.Doubt, CardType.Cooperation)=>-1,
                        (CardType.Doubt, CardType.Doubt)=>0,
                        (CardType.Doubt, CardType.Betrayal)=> r+1,
                        (CardType.Doubt, CardType.Pollution)=>+1,
                        (CardType.Doubt, CardType.Interrupt)=>+1,

                        (CardType.Betrayal, CardType.Cooperation)=> r+1,
                        (CardType.Betrayal, CardType.Doubt)=>-(r+1),
                        (CardType.Betrayal, CardType.Betrayal)=>-2*r,
                        (CardType.Betrayal, CardType.Chaos)=> r+1,
                        (CardType.Betrayal, CardType.Pollution)=> r+1,
                        (CardType.Betrayal, CardType.Interrupt)=>-2,

                        (CardType.Chaos, CardType.Cooperation)=>-1,
                        (CardType.Chaos, CardType.Betrayal)=>-(r+1),
                        (CardType.Chaos, CardType.Interrupt)=>+1,

                        (CardType.Pollution, CardType.Cooperation)=>+2,
                        (CardType.Pollution, CardType.Doubt)=>-1,
                        (CardType.Pollution, CardType.Betrayal)=>-(r+1),
                        (CardType.Pollution, CardType.Interrupt)=>-2,

                        (CardType.Interrupt, CardType.Cooperation)=>+2,
                        (CardType.Interrupt, CardType.Doubt)=>-1,
                        (CardType.Interrupt, CardType.Betrayal)=>+2,
                        (CardType.Interrupt, CardType.Pollution)=>+2,
                        _=>0
                    };
                }

                // 5) EV + 미니맥스 혼합 + 전술 보정으로 점수화
                var cand = I.hand.Distinct().Where(I.HandHas).ToList();
                CardType best = CardType.None; float bestScore = float.NegativeInfinity;

                // 미니맥스용 상위 후보 b 집합
                var topB = p.OrderByDescending(kv=>kv.Value).Take(4).Select(kv=>kv.Key).ToArray();

                foreach (var a in cand)
                {
                    float ev = 0f; foreach (var kv in p) ev += kv.Value * V(a, kv.Key);
                    float worstTop = topB.Min(b => (float)V(a,b));
                    float score = ev + 0.35f * worstTop;

                    // 전술 보정
                    float pC = p[CardType.Cooperation], pD = p[CardType.Doubt];
                    float pB = p[CardType.Betrayal],    pP = p[CardType.Pollution];
                    float pA = pB + pP;

                    if (a == CardType.Interrupt && pA >= 0.46f) score += 1.8f;
                    if (a == CardType.Doubt     && pB >= 0.32f) score += 1.2f;

                    if (a == CardType.Pollution && pC >= 0.35f && pD <= 0.28f) score += 1.4f;
                    if (a == CardType.Betrayal  && pC >= 0.40f && I.s.oppLife <= R+1) score += 1.2f;

                    if (R % 5 == 0 && (a == CardType.Betrayal || a == CardType.Chaos)) score -= 0.6f; // 재해 직전 보수성

                    // 내 생존 저점 보정
                    if (I.s.selfLife <= R){
                        if (a == CardType.Betrayal) score -= 3.3f;
                        if (a == CardType.Pollution) score -= pD * 1.6f;
                    }

                    if (score > bestScore){ bestScore = score; best = a; }
                }

                // 6) 읽힘 방지: 12% 확률로 2순위 채택(근소 차이일 때만)
                var ranked = cand
                    .Select(a => new { a, s = p.Sum(kv => kv.Value * V(a, kv.Key)) })
                    .OrderByDescending(x => x.s).ToList();
                if (ranked.Count >= 2 && ranked[1].s > bestScore - 0.50f && UnityEngine.Random.value < 0.12f)
                    best = ranked[1].a;
                
                return best;
            });

            A.fallback = new[]{
                CardType.Doubt, CardType.Interrupt, CardType.Betrayal,
                CardType.Pollution, CardType.Cooperation, CardType.Chaos, CardType.Recon
            };
            // 선택 드로우: 두 후보의 기대가치 비교(동률 시 상황 가중)
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                bool leading = I.s.selfLife >= I.s.oppLife + 1;
                bool poorAtk = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);

                // 분포 추정
                var p = new Dictionary<CardType, float>
                {
                    {CardType.Cooperation, 0.06f + I.Ratio(CardType.Cooperation)},
                    {CardType.Doubt,       0.06f + I.Ratio(CardType.Doubt)},
                    {CardType.Betrayal,    0.06f + I.Ratio(CardType.Betrayal)},
                    {CardType.Chaos,       0.06f + I.Ratio(CardType.Chaos)},
                    {CardType.Pollution,   0.06f + I.Ratio(CardType.Pollution)},
                    {CardType.Interrupt,   0.06f + I.Ratio(CardType.Interrupt)},
                    {CardType.Recon,       0.06f + I.Ratio(CardType.Recon)},
                };
                if (nf && I.s.lastOpp != CardType.None && I.s.lastOpp == I.s.last2Opp) p[I.s.lastOpp] *= 1.35f;
                float S = p.Values.Sum(); foreach (var k in p.Keys.ToList()) p[k] /= (S <= 0 ? 1f : S);

                int Payoff(CardType x, CardType y)
                {
                    int r = Math.Max(1, I.s.round);
                    return (x, y) switch
                    {
                        (CardType.Cooperation, CardType.Cooperation) => 0,
                        (CardType.Cooperation, CardType.Doubt)       => +1,
                        (CardType.Cooperation, CardType.Betrayal)    => -(r + 1),
                        (CardType.Cooperation, CardType.Chaos)       => +1,
                        (CardType.Cooperation, CardType.Pollution)   => -2,
                        (CardType.Cooperation, CardType.Interrupt)   => -2,

                        (CardType.Doubt, CardType.Cooperation)       => -1,
                        (CardType.Doubt, CardType.Doubt)             => 0,
                        (CardType.Doubt, CardType.Betrayal)          =>  r + 1,
                        (CardType.Doubt, CardType.Pollution)         => +1,
                        (CardType.Doubt, CardType.Interrupt)         => +1,

                        (CardType.Betrayal, CardType.Cooperation)    =>  r + 1,
                        (CardType.Betrayal, CardType.Doubt)          => -(r + 1),
                        (CardType.Betrayal, CardType.Betrayal)       => -2 * r,
                        (CardType.Betrayal, CardType.Chaos)          =>  r + 1,
                        (CardType.Betrayal, CardType.Pollution)      =>  r + 1,
                        (CardType.Betrayal, CardType.Interrupt)      => -2,

                        (CardType.Chaos, CardType.Cooperation)       => -1,
                        (CardType.Chaos, CardType.Betrayal)          => -(r + 1),
                        (CardType.Chaos, CardType.Interrupt)         => +1,

                        (CardType.Pollution, CardType.Cooperation)   => +2,
                        (CardType.Pollution, CardType.Doubt)         => -1,
                        (CardType.Pollution, CardType.Betrayal)      => -(r + 1),
                        (CardType.Pollution, CardType.Interrupt)     => -2,

                        (CardType.Interrupt, CardType.Cooperation)   => +2,
                        (CardType.Interrupt, CardType.Doubt)         => -1,
                        (CardType.Interrupt, CardType.Betrayal)      => +2,
                        (CardType.Interrupt, CardType.Pollution)     => +2,
                        _ => 0
                    };
                }

                float EV(CardType x)
                {
                    float ev = 0f; foreach (var y in p.Keys) ev += p[y] * Payoff(x, y);
                    if (x == CardType.Chaos)     ev += (poorAtk || !leading ? 0.5f : -1.0f);
                    if (x == CardType.Betrayal)  ev += (I.s.oppLife <= R ? 2.0f : 0f) - 1.1f * p[CardType.Doubt];
                    if (x == CardType.Pollution) ev += (p[CardType.Cooperation] - 0.7f * p[CardType.Doubt]) * 1.2f;
                    if (x == CardType.Doubt && (I.s.selfLife <= R || p[CardType.Betrayal] >= 0.30f)) ev += 1.6f;
                    return ev;
                }

                float ea = EV(a), eb = EV(b);
                if (!Mathf.Approximately(ea, eb)) return ea > eb ? 0 : 1;

                // 동률: 손패 밸런스 보정 → 안전/공격 순
                bool needAtk = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);
                bool needDef = !I.HandHas(CardType.Doubt) && !I.HandHas(CardType.Interrupt);
                if (needAtk && (a==CardType.Betrayal || a==CardType.Pollution || b==CardType.Betrayal || b==CardType.Pollution))
                    return (a==CardType.Betrayal || a==CardType.Pollution) ? 0 : 1;
                if (needDef && (a==CardType.Doubt || a==CardType.Interrupt || b==CardType.Doubt || b==CardType.Interrupt))
                    return (a==CardType.Doubt || a==CardType.Interrupt) ? 0 : 1;

                // 최종 무작위 소량
                return UnityEngine.Random.value < 0.5f ? 0 : 1;
            };
            return A;
        }    
    }
}