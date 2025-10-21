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
    류성우,
    서유리,
    강은호,
    전아람
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
            "류성우" => Build_류성우(),
            "서유리" => Build_서유리(),
            "강은호" => Build_강은호(),
            "전아람" => Build_전아람(),
            _ => Build_Default(who),
        };

        static Agent Build_Default(string name)
        {
            var A = new Agent(name);
            A.fallback = new[] { CardType.Cooperation, CardType.Doubt, CardType.Pollution, CardType.Interrupt, CardType.Recon, CardType.Betrayal, CardType.Chaos };
            return A;
        }

        
        // 김현수v2 — 신중·분석형(소폭 강화)
        static Agent Build_김현수()
        {
            var A = new Agent("김현수");

            // --- 라운드 카드 선택 ---
            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                // 0) 생존 우선
                bool highBetrayal = I.Ratio(CardType.Betrayal) >= 0.28f || (nf && I.s.lastOpp == CardType.Betrayal);
                if (I.HandHas(CardType.Doubt) && I.s.selfLife <= R && highBetrayal)
                    return CardType.Doubt;

                // 1) 직전 반복 패턴 간단 카운터(가치 보정 ↑)
                if (nf && I.s.lastOpp == I.s.last2Opp && I.s.lastOpp != CardType.None)
                {
                    var x = I.s.lastOpp;
                    if (x == CardType.Cooperation)
                    {
                        if (I.HandHas(CardType.Pollution) && I.Ratio(CardType.Doubt) <= 0.30f) return CardType.Pollution;
                        if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R + 1) return CardType.Betrayal;
                    }
                    if (x == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    if (x == CardType.Betrayal  && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                    if (x == CardType.Doubt     && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                }

                // 2) Recon 활용 강화: 초·중반, 안전·분포가 섞인 상황에서만
                if (I.HandHas(CardType.Recon))
                {
                    bool safe = I.s.selfLife >= I.s.oppLife - 1 && !highBetrayal;
                    bool mixed =
                        I.Ratio(CardType.Cooperation) < 0.45f &&
                        I.Ratio(CardType.Doubt)       < 0.38f &&
                        I.Ratio(CardType.Pollution)   < 0.38f;
                    if (R >= 2 && R <= 6 && safe && mixed) return CardType.Recon;
                }

                // 3) 장기 압박: 협력 성향↑ + 의심 낮음 → Pollution
                if (I.HandHas(CardType.Pollution) &&
                    (I.Ratio(CardType.Cooperation) >= 0.33f || (nf && I.s.lastOpp == CardType.Cooperation)) &&
                    I.Ratio(CardType.Doubt) <= 0.30f)
                    return CardType.Pollution;

                // 4) 확실한 킬각만 배신
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && I.Ratio(CardType.Doubt) < 0.33f)
                    return CardType.Betrayal;

                // 5) 읽힘·손패 막힘 시 제한적 Chaos
                int atk = (I.HandHas(CardType.Betrayal) ? 1 : 0) + (I.HandHas(CardType.Pollution) ? 1 : 0);
                if (I.HandHas(CardType.Chaos) && (atk == 0 || (nf && I.s.lastOpp == I.s.last2Opp && I.s.selfLife < I.s.oppLife)))
                    return CardType.Chaos;

                // 6) 기본 선호도(무난+안전)
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

            // --- 선택 드로우(두 장 중 1장) ---
            A.chooseFromTwo = (a, b, I) =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                bool HasAtk() => I.hand.Contains(CardType.Betrayal) || I.hand.Contains(CardType.Pollution);
                bool HasDef() => I.hand.Contains(CardType.Doubt) || I.hand.Contains(CardType.Interrupt);
                int idx(CardType t) => a == t ? 0 : (b == t ? 1 : -1);

                // 0) 즉사 위험 시 방어 우선
                bool lethalRisk = I.s.selfLife <= R && (I.Ratio(CardType.Betrayal) >= 0.28f || (nf && I.s.lastOpp == CardType.Betrayal));
                if (lethalRisk)
                {
                    int i = idx(CardType.Doubt); if (i >= 0) return i;
                    i = idx(CardType.Interrupt); if (i >= 0) return i;
                }

                // 1) 초·중반 안전하면 Recon 선호
                bool safe = I.s.selfLife >= I.s.oppLife - 1 && I.Ratio(CardType.Betrayal) < 0.27f;
                bool mixed = I.Ratio(CardType.Cooperation) < 0.45f && I.Ratio(CardType.Doubt) < 0.38f && I.Ratio(CardType.Pollution) < 0.38f;
                if (R >= 2 && R <= 6 && safe && mixed)
                {
                    int i = idx(CardType.Recon); if (i >= 0) return i;
                }

                // 2) 공격수단 없으면 확보(Pollution 우선)
                if (!HasAtk())
                {
                    int i = idx(CardType.Pollution); if (i >= 0) return i;
                    i = idx(CardType.Betrayal);      if (i >= 0) return i;
                }

                // 3) 방어수단 없으면 Doubt/Interrupt 확보
                if (!HasDef())
                {
                    int i = idx(CardType.Doubt);     if (i >= 0) return i;
                    i = idx(CardType.Interrupt);     if (i >= 0) return i;
                }

                // 4) 킬각이면 Betrayal 확보
                if (I.s.oppLife <= R && I.Ratio(CardType.Doubt) < 0.33f)
                {
                    int i = idx(CardType.Betrayal); if (i >= 0) return i;
                }

                // 5) 협력↑ + 의심 낮음 → Pollution 확보
                if ((I.Ratio(CardType.Cooperation) >= 0.33f || (nf && I.s.lastOpp == CardType.Cooperation)) && I.Ratio(CardType.Doubt) <= 0.30f)
                {
                    int i = idx(CardType.Pollution); if (i >= 0) return i;
                }

                // 6) 기본 점수로 결정
                int Score(CardType t) => t switch
                {
                    CardType.Cooperation => 100,
                    CardType.Doubt       => 92,
                    CardType.Recon       => 86,
                    CardType.Pollution   => 78,
                    CardType.Interrupt   => 66,
                    CardType.Betrayal    => 58,
                    _ => 0
                };
                int sa = Score(a), sb = Score(b);
                if (sa != sb) return sa > sb ? 0 : 1;

                // 7) 동점이면 소폭 무작위
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

        // 박민재v2 — 강화된 계산 중심 Agent (상대 분석 배제, 상황 가치 극대화)
        static Agent Build_박민재()
        {
            var A = new Agent("박민재");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool highVol = (R % 5 == 0) || (R % 5 == 1);


                // 즉사 가능 or 생존 방어 우선 판단
                bool canKill = I.HandHas(CardType.Betrayal) && I.s.oppLife <= R;
                bool mustDefend = I.s.selfLife <= R && I.HandHas(CardType.Doubt);
                if (canKill) return CardType.Betrayal;
                if (mustDefend) return CardType.Doubt;


                // 손패의 정보가치/행동력 평가
                int Eval(CardType c)
                {
                    int score = 0;
                    if (c == CardType.Betrayal) score = I.s.oppLife <= R + 1 ? 7 : 3;
                    else if (c == CardType.Pollution) score = 3;
                    else if (c == CardType.Cooperation) score = 2;
                    else if (c == CardType.Doubt) score = I.s.selfLife <= R + 1 ? 4 : 1;
                    else if (c == CardType.Interrupt) score = 2;
                    else if (c == CardType.Recon) score = (I.s.selfLife < I.s.oppLife || I.hand.Count(h => h == CardType.Betrayal || h == CardType.Pollution) == 0) ? 2 : 0;
                    else if (c == CardType.Chaos) score = (I.s.selfLife < I.s.oppLife && !highVol) ? 1 : -1;
                    return score;
                }


                CardType best = CardType.None;
                int bestScore = int.MinValue;
                foreach (var c in I.hand.Distinct().Where(I.HandHas))
                {
                    int sc = Eval(c);
                    // 변동성 리스크 패널티 보정
                    if (highVol && c == CardType.Chaos) sc -= 2;
                    if (highVol && c == CardType.Pollution) sc -= 1;
                    if (sc > bestScore) { bestScore = sc; best = c; }
                }


                return best;
            });


            A.fallback = new[]
            {
                CardType.Betrayal, CardType.Pollution, CardType.Cooperation,
                CardType.Doubt, CardType.Interrupt, CardType.Recon, CardType.Chaos
            };


            // 강화된 선택 드로우 로직
            A.chooseFromTwo = (a, b, I) =>
            {
                int R = Math.Max(1, I.s.round);
                bool losing = I.s.selfLife < I.s.oppLife;
                bool noAtk = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);


                float V(CardType c)
                {
                    if (c == CardType.Betrayal) return (I.s.oppLife <= R ? 7f : 3f);
                    if (c == CardType.Pollution) return 3f;
                    if (c == CardType.Doubt) return (I.s.selfLife <= R + 1 ? 5f : 1f);
                    if (c == CardType.Cooperation) return 2f;
                    if (c == CardType.Interrupt) return 2.5f;
                    if (c == CardType.Recon) return (noAtk || losing) ? 2.5f : 0.5f;
                    if (c == CardType.Chaos) return (losing && R > 3) ? 1.0f : -2f;
                    return 0f;
                }


                float va = V(a), vb = V(b);
                if (Math.Abs(va - vb) < 0.1f) return UnityEngine.Random.value < 0.5f ? 0 : 1;
                return va >= vb ? 0 : 1;
            };


            return A;
        }

        // 정다은 — 패턴 분석·카운터형 (HistoryOpponent 없이 동작)
        static Agent Build_정다은()
        {
            var A = new Agent("정다은");

            // ① 라운드 카드 선택
            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);

                // 상대 행동 분포 추정 p[t] (덱+상대패 기반 비율)
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

                // 최근 2수의 모드(가장 자주 낸 카드)로 소폭 가중
                var recent = new[] { I.s.lastOpp, I.s.last2Opp }
                    .Where(t => t != CardType.None)
                    .ToArray();
                if (recent.Length > 0)
                {
                    var mode = recent.GroupBy(t => t)
                                    .OrderByDescending(g => g.Count())
                                    .First().Key;
                    p[mode] *= 1.25f;
                }

                // 정규화
                float sum = p.Values.Sum(); if (sum <= 0) sum = 1f;
                foreach (var k in p.Keys.ToList()) p[k] /= sum;

                // 즉사 각 / 생존 각
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.33f)
                    return CardType.Betrayal;
                if (I.s.selfLife <= R && p[CardType.Betrayal] >= 0.28f && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                // 기대값 행렬
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
                    if (a == CardType.Recon && b == CardType.Betrayal) return -(R + 1);
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

                    // 읽힘 회피: 직전 내가 낸 카드 반복 페널티
                    if (I.s.lastSelf == a) ev -= 0.2f;

                    if (ev > bestEV) { bestEV = ev; best = a; }
                }
                return best;
            });

            // ② 선택 드로우(두 장 중 선택) — 상대 분포 재계산 후 EV 높은 쪽 선택
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                int R = Math.Max(1, I.s.round);

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

                int V(CardType x, CardType y)
                {
                    int r = R;
                    // 필요 최소 케이스만 사용 (정다은 선택 드로우 휴리스틱)
                    if (x == CardType.Betrayal && y == CardType.Doubt) return -(r + 1);
                    if (x == CardType.Betrayal && y != CardType.Doubt) return  (r + 1);
                    if (x == CardType.Doubt     && y == CardType.Betrayal) return  (r + 1);
                    if (x == CardType.Pollution && y == CardType.Cooperation) return +2;
                    if (x == CardType.Interrupt && (y == CardType.Betrayal || y == CardType.Doubt || y == CardType.Pollution)) return +2;
                    if (x == CardType.Cooperation && y == CardType.Betrayal) return -(r + 1);
                    if (x == CardType.Chaos) return 0;
                    return 0;
                }

                float Score(CardType x) => p.Sum(kv => kv.Value * V(x, kv.Key));
                float sa = Score(a), sb = Score(b);
                if (System.Math.Abs(sa - sb) < 0.001f) return UnityEngine.Random.value < 0.5f ? 0 : 1;
                return sa >= sb ? 0 : 1;
            };

            // ③ 기본 우선순위
            A.fallback = new[]
            {
                CardType.Doubt, CardType.Interrupt, CardType.Betrayal,
                CardType.Pollution, CardType.Cooperation, CardType.Chaos, CardType.Recon
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

        // 백무적V4 — 초월 메타 적응형(확률 최적화 + 카운터 + 읽힘 회피)
        static Agent Build_백무적()
        {
            var A = new Agent("백무적");

            // ── ① 라운드 카드 선택 ─────────────────────────────────────────
            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                // 라운드 경계의 변동성(5의 배수 블록 전환)을 약하게 인식
                bool highVol = (R % 5 == 0) || (R % 5 == 1);

                // 상대 분포 추정 p[t] = 라플라스(0.06) + unseen 비율, 최근 2수 가중
                var p = new Dictionary<CardType, float>
                {
                    { CardType.Cooperation, 0.06f + I.Ratio(CardType.Cooperation) },
                    { CardType.Doubt,       0.06f + I.Ratio(CardType.Doubt)       },
                    { CardType.Betrayal,    0.06f + I.Ratio(CardType.Betrayal)    },
                    { CardType.Chaos,       0.06f + I.Ratio(CardType.Chaos)       },
                    { CardType.Pollution,   0.06f + I.Ratio(CardType.Pollution)   },
                    { CardType.Interrupt,   0.06f + I.Ratio(CardType.Interrupt)   },
                    { CardType.Recon,       0.06f + I.Ratio(CardType.Recon)       },
                };
                if (nf && I.s.lastOpp != CardType.None)
                {
                    p[I.s.lastOpp] *= 1.15f;                         // 직전 행동 가중
                    if (I.s.lastOpp == I.s.last2Opp) p[I.s.lastOpp] *= 1.20f; // 반복시 추가
                }
                float S = p.Values.Sum(); foreach (var k in p.Keys.ToList()) p[k] /= (S <= 0 ? 1f : S);

                // 정보량(엔트로피 근사)로 혼전/정찰 타이밍 판단
                float entropy = 0f; foreach (var v in p.Values) if (v > 0) entropy += -v * (float)System.Math.Log(v + 1e-6);

                // ── 0) 즉사 / 생존 ──
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.35f)
                    return CardType.Betrayal;
                bool lethalRisk = I.s.selfLife <= R && p[CardType.Betrayal] >= 0.28f;
                if (lethalRisk && I.HandHas(CardType.Doubt)) return CardType.Doubt;

                // ── 1) 반복 패턴 강 카운터 ──
                if (nf && I.s.lastOpp == I.s.last2Opp && I.s.lastOpp != CardType.None)
                {
                    var x = I.s.lastOpp;
                    if (x == CardType.Cooperation && I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    if (x == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    if (x == CardType.Betrayal && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                    if (x == CardType.Doubt && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                }

                // ── 2) 정보/포지셔닝 ──
                bool safe = I.s.selfLife >= I.s.oppLife - 1 && p[CardType.Betrayal] <= 0.26f;
                bool mixed = p.Values.Max() < 0.42f;              // 상대 혼합 플레이
                if (I.HandHas(CardType.Recon) && R <= 5 && safe && (mixed || entropy > 1.7f))
                    return CardType.Recon;

                // ── 3) EV 행렬로 기대값 최대화 + 리스크 보정 ──
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
                    if (a == CardType.Recon && b == CardType.Betrayal) return -(R + 1);
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

                    // 막판 킬각 보정
                    if (a == CardType.Betrayal && I.s.oppLife <= R + 1) ev += p[CardType.Cooperation] * 2.5f;

                    // 리스크 보정
                    if (highVol && a == CardType.Chaos) ev -= 0.7f;
                    if (highVol && a == CardType.Betrayal) ev -= 0.4f;

                    // 읽힘 회피: 직전 내가 낸 카드 반복 페널티
                    if (I.s.lastSelf == a) ev -= 0.2f;

                    if (ev > bestEV) { bestEV = ev; best = a; }
                }

                // 2순위 근접 시 확률적 스왑(읽힘 방지)
                var alt = cand.Where(t => t != best)
                            .OrderByDescending(t => { float e = 0; foreach (var b in p.Keys) e += p[b] * Delta(t, b); return e; })
                            .ToList();
                if (alt.Count > 0)
                {
                    float secondEV = 0; foreach (var b in p.Keys) secondEV += p[b] * Delta(alt[0], b);
                    if (secondEV > bestEV - 0.45f && UnityEngine.Random.value < 0.18f) best = alt[0];
                }

                return best;
            });

            // ── ② 선택 드로우(2장 중 1장) ────────────────────────────────
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                int R = Math.Max(1, I.s.round);
                bool losing = I.s.selfLife < I.s.oppLife;

                // 간단 균형 체크
                bool needAtk = !(I.HandHas(CardType.Betrayal) || I.HandHas(CardType.Pollution));
                bool needDef = !(I.HandHas(CardType.Doubt) || I.HandHas(CardType.Interrupt));

                // EV 근사 점수
                float Score(CardType x)
                {
                    float s = 0f;
                    if (x == CardType.Betrayal) s += (I.s.oppLife <= R ? 7f : 3.2f) - 1.8f * I.Ratio(CardType.Doubt);
                    if (x == CardType.Pollution) s += 3.0f + 1.2f * (I.Ratio(CardType.Cooperation));
                    if (x == CardType.Doubt) s += (I.s.selfLife <= R ? 4.8f : 1.2f);
                    if (x == CardType.Interrupt) s += 2.6f + 1.4f * I.Ratio(CardType.Betrayal);
                    if (x == CardType.Cooperation) s += losing ? 1.0f : 2.0f;
                    if (x == CardType.Recon) s += (R <= 5 && (losing || needAtk)) ? 2.2f : 0.6f;
                    if (x == CardType.Chaos) s += (losing && R >= 3 ? 0.8f : -1.5f);
                    if (needAtk && (x == CardType.Betrayal || x == CardType.Pollution)) s += 1.5f;
                    if (needDef && (x == CardType.Doubt || x == CardType.Interrupt)) s += 1.3f;
                    return s;
                }

                float sa = Score(a), sb = Score(b);
                int pick = sa >= sb ? 0 : 1;

                // 동점 혹은 근접 시, 공격카드/수비카드 필요 여부로 편향
                if (System.Math.Abs(sa - sb) < 0.15f)
                {
                    bool aOff = (a == CardType.Betrayal || a == CardType.Pollution);
                    bool bOff = (b == CardType.Betrayal || b == CardType.Pollution);
                    bool aDef = (a == CardType.Doubt || a == CardType.Interrupt);
                    bool bDef = (b == CardType.Doubt || b == CardType.Interrupt);

                    if (needAtk && aOff != bOff) pick = aOff ? 0 : 1;
                    else if (needDef && aDef != bDef) pick = aDef ? 0 : 1;
                    else pick = UnityEngine.Random.value < 0.5f ? 0 : 1;
                }
                return pick;
            };

            // ── ③ 기본 낙하 우선순위 ─────────────────────────────────────
            A.fallback = new[]
            {
                CardType.Pollution, CardType.Betrayal, CardType.Doubt,
                CardType.Interrupt, CardType.Cooperation, CardType.Recon, CardType.Chaos
            };
            return A;
        }

        // ──────────────────────────────────────────────
        // 류성우 — 리스크 조절형(앞서면 보수, 뒤지면 공세)
        // ──────────────────────────────────────────────
        static Agent Build_류성우()
        {
            var A = new Agent("류성우");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);
                bool losing = I.s.selfLife < I.s.oppLife;

                // 상대 행동 분포(덱+상대패 비율)
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

                // 즉사/생존
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.33f)
                    return CardType.Betrayal;
                if (I.s.selfLife <= R && p[CardType.Betrayal] >= 0.28f && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                // 기대값 매트릭스
                int Delta(CardType a, CardType b)
                {
                    int r = R;
                    if (a == CardType.Cooperation && b == CardType.Cooperation) return 0;
                    if (a == CardType.Cooperation && b == CardType.Doubt) return +1;
                    if (a == CardType.Cooperation && b == CardType.Betrayal) return -(r + 1);
                    if (a == CardType.Cooperation && b == CardType.Chaos) return +1;
                    if (a == CardType.Cooperation && b == CardType.Pollution) return -2;
                    if (a == CardType.Cooperation && b == CardType.Interrupt) return +2;
                    if (a == CardType.Cooperation && b == CardType.Recon) return +1;

                    if (a == CardType.Doubt && b == CardType.Cooperation) return -1;
                    if (a == CardType.Doubt && b == CardType.Doubt) return 0;
                    if (a == CardType.Doubt && b == CardType.Betrayal) return r + 1;
                    if (a == CardType.Doubt && b == CardType.Chaos) return 0;
                    if (a == CardType.Doubt && b == CardType.Pollution) return +1;
                    if (a == CardType.Doubt && b == CardType.Interrupt) return -1;
                    if (a == CardType.Doubt && b == CardType.Recon) return 0;

                    if (a == CardType.Betrayal && b == CardType.Cooperation) return r + 1;
                    if (a == CardType.Betrayal && b == CardType.Doubt) return -(r + 1);
                    if (a == CardType.Betrayal && b == CardType.Betrayal) return -2 * r;
                    if (a == CardType.Betrayal && b == CardType.Chaos) return r + 1;
                    if (a == CardType.Betrayal && b == CardType.Pollution) return r + 1;
                    if (a == CardType.Betrayal && b == CardType.Interrupt) return r;
                    if (a == CardType.Betrayal && b == CardType.Recon) return r + 1;

                    if (a == CardType.Chaos && b == CardType.Cooperation) return -1;
                    if (a == CardType.Chaos && b == CardType.Doubt) return 0;
                    if (a == CardType.Chaos && b == CardType.Betrayal) return -(r + 1);
                    if (a == CardType.Chaos && b == CardType.Chaos) return 0;
                    if (a == CardType.Chaos && b == CardType.Pollution) return 0;
                    if (a == CardType.Chaos && b == CardType.Interrupt) return -1;
                    if (a == CardType.Chaos && b == CardType.Recon) return 0;

                    if (a == CardType.Pollution && b == CardType.Cooperation) return +2;
                    if (a == CardType.Pollution && b == CardType.Doubt) return -1;
                    if (a == CardType.Pollution && b == CardType.Betrayal) return -(r + 1);
                    if (a == CardType.Pollution && b == CardType.Chaos) return 0;
                    if (a == CardType.Pollution && b == CardType.Pollution) return 0;
                    if (a == CardType.Pollution && b == CardType.Interrupt) return 0;
                    if (a == CardType.Pollution && b == CardType.Recon) return -1;

                    if (a == CardType.Interrupt && b == CardType.Cooperation) return -2;
                    if (a == CardType.Interrupt && b == CardType.Doubt) return +2;
                    if (a == CardType.Interrupt && b == CardType.Betrayal) return +2;
                    if (a == CardType.Interrupt && b == CardType.Chaos) return -1;
                    if (a == CardType.Interrupt && b == CardType.Pollution) return +2;
                    if (a == CardType.Interrupt && b == CardType.Interrupt) return 0;
                    if (a == CardType.Interrupt && b == CardType.Recon) return +1;

                    if (a == CardType.Recon && b == CardType.Cooperation) return -1;
                    if (a == CardType.Recon && b == CardType.Doubt) return 0;
                    if (a == CardType.Recon && b == CardType.Betrayal) return -(r + 1);
                    if (a == CardType.Recon && b == CardType.Chaos) return 0;
                    if (a == CardType.Recon && b == CardType.Pollution) return -1;
                    if (a == CardType.Recon && b == CardType.Interrupt) return -1;
                    if (a == CardType.Recon && b == CardType.Recon) return 0;
                    return 0;
                }

                // 후보 중 EV 최대. 지고 있으면 공격카드(배신/오염)에 가중, 앞서면 방어카드(의심/차단)에 가중
                var cand = I.hand.Distinct().Where(I.HandHas).ToList();
                CardType best = CardType.None; float bestEV = float.NegativeInfinity;

                foreach (var a in cand)
                {
                    float ev = 0f; foreach (var b in p.Keys) ev += p[b] * Delta(a, b);
                    if (losing && (a == CardType.Betrayal || a == CardType.Pollution)) ev += 0.6f;
                    if (!losing && (a == CardType.Doubt || a == CardType.Interrupt)) ev += 0.5f;
                    if (ev > bestEV) { bestEV = ev; best = a; }
                }
                return best;
            });

            A.chooseFromTwo = (a, b, I) =>
            {
                int R = Math.Max(1, I.s.round);
                bool losing = I.s.selfLife < I.s.oppLife;

                float P(CardType t) => I.Ratio(t);
                float Z = new[]{
                    P(CardType.Cooperation),P(CardType.Doubt),P(CardType.Betrayal),
                    P(CardType.Chaos),P(CardType.Pollution),P(CardType.Interrupt),P(CardType.Recon)
                }.Sum(); if (Z <= 0) Z = 1f;
                float Q(CardType t) => P(t) / Z;

                float V(CardType c)
                {
                    switch (c)
                    {
                        case CardType.Betrayal: return (losing ? 2.2f : 1.0f) * (I.s.oppLife <= R ? 3.0f : 1.0f) - 3.5f * Q(CardType.Doubt);
                        case CardType.Pollution: return (losing ? 1.8f : 1.0f) + 1.5f * (1f - Q(CardType.Doubt));
                        case CardType.Doubt: return (!losing ? 2.2f : 1.0f) * (2.5f * Q(CardType.Betrayal));
                        case CardType.Interrupt: return (!losing ? 2.0f : 1.0f) * (2.0f * (Q(CardType.Betrayal) + Q(CardType.Doubt)));
                        case CardType.Cooperation: return 1.0f - 1.5f * Q(CardType.Betrayal);
                        case CardType.Recon: return (losing ? 1.2f : 0.6f);
                        case CardType.Chaos: return losing ? 0.3f : -0.8f;
                    }
                    return 0f;
                }

                float va = V(a), vb = V(b);
                if (Math.Abs(va - vb) < 0.001f) return UnityEngine.Random.value < 0.5f ? 0 : 1;
                return va >= vb ? 0 : 1;
            };

            A.fallback = new[] { CardType.Doubt, CardType.Interrupt, CardType.Pollution, CardType.Cooperation, CardType.Betrayal, CardType.Recon, CardType.Chaos };
            return A;
        }
        // ──────────────────────────────────────────────
        // 서유리 — 템포 스위처(반복 패턴 저격, 루프 깨기)
        // ──────────────────────────────────────────────
        static Agent Build_서유리()
        {
            var A = new Agent("서유리");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);

                // 상대가 같은 패턴 반복 중이면 강하게 카운터
                bool oppLoop = (I.s.lastOpp != CardType.None) && (I.s.lastOpp == I.s.last2Opp);

                // 분포
                var p = new Dictionary<CardType, float>{
                    {CardType.Cooperation,I.Ratio(CardType.Cooperation)},
                    {CardType.Doubt,      I.Ratio(CardType.Doubt)},
                    {CardType.Betrayal,   I.Ratio(CardType.Betrayal)},
                    {CardType.Chaos,      I.Ratio(CardType.Chaos)},
                    {CardType.Pollution,  I.Ratio(CardType.Pollution)},
                    {CardType.Interrupt,  I.Ratio(CardType.Interrupt)},
                    {CardType.Recon,      I.Ratio(CardType.Recon)}
                };
                float s = p.Values.Sum(); if (s <= 0) s = 1f; foreach (var k in p.Keys.ToList()) p[k] /= s;

                // 즉사/생존
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.35f)
                    return CardType.Betrayal;
                if (I.s.selfLife <= R && p[CardType.Betrayal] >= 0.30f && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                // 루프 저격 규칙
                if (oppLoop)
                {
                    switch (I.s.lastOpp)
                    {
                        case CardType.Betrayal: if (I.HandHas(CardType.Doubt)) return CardType.Doubt; break;
                        case CardType.Doubt: if (I.HandHas(CardType.Pollution)) return CardType.Pollution; break;
                        case CardType.Cooperation: if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt; break;
                        case CardType.Pollution: if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt; break;
                    }
                }

                // 기본 우선순위: 템포 전환(공→수 또는 수→공)로 읽힘 회피
                bool myLoop = (I.s.lastSelf != CardType.None) && (I.s.lastSelf == I.s.lastOpp);
                if (myLoop && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;

                // 정보/재정비
                bool poorAttack = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);
                if (poorAttack && I.HandHas(CardType.Recon)) return CardType.Recon;

                // 가벼운 EV
                int r = R;
                float EV(CardType a)
                {
                    float e = 0;
                    foreach (var kv in p)
                    {
                        var b = kv.Key; float q = kv.Value;
                        int d = 0;
                        if (a == CardType.Pollution && b == CardType.Cooperation) d = +2;
                        else if (a == CardType.Interrupt && (b == CardType.Betrayal || b == CardType.Doubt || b == CardType.Pollution)) d = +2;
                        else if (a == CardType.Betrayal && b == CardType.Cooperation) d = r + 1;
                        else if (a == CardType.Doubt && b == CardType.Betrayal) d = r + 1;
                        else if (a == CardType.Cooperation && b == CardType.Betrayal) d = -(r + 1);
                        e += q * d;
                    }
                    // 템포 전환 가중
                    if (a != I.s.lastSelf) e += 0.25f;
                    return e;
                }

                var cand = I.hand.Distinct().Where(I.HandHas).OrderByDescending(EV).ToList();
                return cand[0];
            });

            A.chooseFromTwo = (a, b, I) =>
            {
                // 읽힘 회피: 직전 내가 낸 것과 다른 쪽에 소폭 보정
                float bonusA = (I.s.lastSelf != a) ? 0.15f : 0f;
                float bonusB = (I.s.lastSelf != b) ? 0.15f : 0f;

                float baseV(CardType c)
                {
                    switch (c)
                    {
                        case CardType.Interrupt: return 1.5f;
                        case CardType.Pollution: return 1.2f;
                        case CardType.Doubt: return 1.1f;
                        case CardType.Betrayal: return 1.0f;
                        case CardType.Cooperation: return 0.6f;
                        case CardType.Recon: return 0.7f;
                        case CardType.Chaos: return 0.0f;
                    }
                    return 0f;
                }

                float va = baseV(a) + bonusA, vb = baseV(b) + bonusB;
                if (Math.Abs(va - vb) < 0.001f) return UnityEngine.Random.value < 0.5f ? 0 : 1;
                return va >= vb ? 0 : 1;
            };

            A.fallback = new[] { CardType.Interrupt, CardType.Pollution, CardType.Doubt, CardType.Betrayal, CardType.Cooperation, CardType.Recon, CardType.Chaos };
            return A;
        }
        // ──────────────────────────────────────────────
        // 강은호 — 확률 회계사(미확인 카드 분포에 충실, 혼돈 기피)
        // ──────────────────────────────────────────────
        static Agent Build_강은호()
        {
            var A = new Agent("강은호");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);

                // 분포(그대로 신뢰)
                float P(CardType t) => I.Ratio(t);
                float Z = P(CardType.Cooperation) + P(CardType.Doubt) + P(CardType.Betrayal) + P(CardType.Chaos) + P(CardType.Pollution) + P(CardType.Interrupt) + P(CardType.Recon);
                if (Z <= 0) Z = 1f;
                float Q(CardType t) => P(t) / Z;

                // 안전우선: Chaos는 거의 사용 안 함
                if (!I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution) && I.HandHas(CardType.Recon))
                    return CardType.Recon;

                // 즉사/생존
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && Q(CardType.Doubt) < 0.33f)
                    return CardType.Betrayal;
                if (I.s.selfLife <= R && Q(CardType.Betrayal) >= 0.28f && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                // 기대값 근사
                float V(CardType c)
                {
                    switch (c)
                    {
                        case CardType.Pollution: return 2.0f * (1f - Q(CardType.Doubt)) - 0.2f * Q(CardType.Interrupt);
                        case CardType.Interrupt: return 1.8f * (Q(CardType.Betrayal) + Q(CardType.Doubt) + Q(CardType.Pollution));
                        case CardType.Doubt: return 2.2f * Q(CardType.Betrayal);
                        case CardType.Betrayal: return (I.s.oppLife <= R ? 3.2f : 1.6f) - 3.8f * Q(CardType.Doubt);
                        case CardType.Cooperation: return 0.8f - 1.2f * Q(CardType.Betrayal);
                        case CardType.Recon: return 0.9f;
                        case CardType.Chaos: return -0.6f;
                    }
                    return 0f;
                }

                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(V).FirstOrDefault();
            });

            A.chooseFromTwo = (a, b, I) =>
            {
                float V(CardType c)
                {
                    switch (c)
                    {
                        case CardType.Pollution: return 1.4f;
                        case CardType.Interrupt: return 1.2f;
                        case CardType.Doubt: return 1.1f;
                        case CardType.Betrayal: return 1.0f;
                        case CardType.Cooperation: return 0.6f;
                        case CardType.Recon: return 0.7f;
                        case CardType.Chaos: return 0.0f;
                    }
                    return 0f;
                }
                float va = V(a), vb = V(b);
                if (Math.Abs(va - vb) < 0.001f) return UnityEngine.Random.value < 0.5f ? 0 : 1;
                return va >= vb ? 0 : 1;
            };

            A.fallback = new[] { CardType.Pollution, CardType.Interrupt, CardType.Doubt, CardType.Betrayal, CardType.Cooperation, CardType.Recon, CardType.Chaos };
            return A;
        }
        // ──────────────────────────────────────────────
        // 전아람 — 정보 포식자(정보→일격, Recon 활용 극대화)
        // ──────────────────────────────────────────────
        static Agent Build_전아람()
        {
            var A = new Agent("전아람");

            A.rules.Add(I =>
            {
                int R = Math.Max(1, I.s.round);

                // 손패가 공격적으로 빈약하거나 초중반이면 우선 정찰
                bool poor = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);
                if ((R<=4 || poor) && I.HandHas(CardType.Recon))
                    return CardType.Recon;

                // 상대 경향(간단 추정)
                var p = new Dictionary<CardType,float>{
                    {CardType.Cooperation,I.Ratio(CardType.Cooperation)},
                    {CardType.Doubt,      I.Ratio(CardType.Doubt)},
                    {CardType.Betrayal,   I.Ratio(CardType.Betrayal)},
                    {CardType.Chaos,      I.Ratio(CardType.Chaos)},
                    {CardType.Pollution,  I.Ratio(CardType.Pollution)},
                    {CardType.Interrupt,  I.Ratio(CardType.Interrupt)},
                    {CardType.Recon,      I.Ratio(CardType.Recon)}
                };
                float s = p.Values.Sum(); if (s<=0) s=1f; foreach(var k in p.Keys.ToList()) p[k]/=s;

                // 즉사/생존
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.35f)
                    return CardType.Betrayal;
                if (I.s.selfLife <= R && p[CardType.Betrayal] >= 0.28f && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                // 읽힘 회피: 내 최근 카드 반복 회피
                CardType avoid = I.s.lastSelf;

                // 간단 EV + 회피 보정
                int r = R;
                float Score(CardType a)
                {
                    float e=0;
                    foreach(var kv in p)
                    {
                        var b=kv.Key; float q=kv.Value; int d=0;
                        if (a==CardType.Betrayal && b==CardType.Cooperation) d=r+1;
                        else if (a==CardType.Pollution && b==CardType.Cooperation) d=+2;
                        else if (a==CardType.Doubt && b==CardType.Betrayal) d=r+1;
                        else if (a==CardType.Interrupt && (b==CardType.Betrayal||b==CardType.Doubt||b==CardType.Pollution)) d=+2;
                        else if (a==CardType.Cooperation && b==CardType.Betrayal) d=-(r+1);
                        e += q*d;
                    }
                    if (a!=avoid) e += 0.2f;
                    return e;
                }

                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            A.chooseFromTwo = (a,b,I) =>
            {
                int R = Math.Max(1, I.s.round);
                bool losing = I.s.selfLife < I.s.oppLife;

                float baseV(CardType c)
                {
                    switch(c)
                    {
                        case CardType.Recon:     return losing?1.6f:1.0f;
                        case CardType.Betrayal:  return (I.s.oppLife<=R?3.0f:1.2f);
                        case CardType.Pollution: return 1.4f;
                        case CardType.Doubt:     return 1.2f;
                        case CardType.Interrupt: return 1.1f;
                        case CardType.Cooperation:return 0.7f;
                        case CardType.Chaos:     return 0.1f;
                    }
                    return 0f;
                }

                float va = baseV(a) + (I.s.lastSelf!=a?0.15f:0f);
                float vb = baseV(b) + (I.s.lastSelf!=b?0.15f:0f);
                if (Math.Abs(va-vb)<0.001f) return UnityEngine.Random.value<0.5f?0:1;
                return va>=vb?0:1;
            };

            A.fallback = new[]{ CardType.Recon, CardType.Pollution, CardType.Doubt, CardType.Betrayal, CardType.Interrupt, CardType.Cooperation, CardType.Chaos };
            return A;
        }
    }
}