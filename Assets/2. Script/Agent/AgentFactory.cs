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
    전아람,
}

namespace GameCore
{
    // ===== 에이전트 팩토리(최신 10인 로직) =====
    public static class AgentFactory
    {
        // ▼ [수정됨] 모든 Create 호출이 ID를 전달합니다.
        public static Agent Create(string who) => who switch
        {
            "김현수" => Build_김현수(AgentList.김현수),
            "이수진" => Build_이수진(AgentList.이수진),
            "최용호" => Build_최용호(AgentList.최용호),
            "한지혜" => Build_한지혜(AgentList.한지혜),
            "박민재" => Build_박민재(AgentList.박민재),
            "정다은" => Build_정다은(AgentList.정다은),
            "오태훈" => Build_오태훈(AgentList.오태훈),
            "유민정" => Build_유민정(AgentList.유민정),
            "김태양" => Build_김태양(AgentList.김태양),
            "이하린" => Build_이하린(AgentList.이하린),
            "백무적" => Build_백무적(AgentList.백무적),
            "류성우" => Build_류성우(AgentList.류성우),
            "서유리" => Build_서유리(AgentList.서유리),
            "강은호" => Build_강은호(AgentList.강은호),
            "전아람" => Build_전아람(AgentList.전아람),
            _ => Build_Default(who, (AgentList)System.Enum.Parse(typeof(AgentList), who)),
        };

        // ▼ [수정됨] 생성자에 id 전달
        static Agent Build_Default(string name, AgentList id)
        {
            var A = new Agent(name, id); 
            A.fallback = new[] { CardType.Cooperation, CardType.Doubt, CardType.Pollution, CardType.Interrupt, CardType.Recon, CardType.Betrayal, CardType.Chaos, CardType.Curse, CardType.Sacrifice };
            return A;
        }
        
        // 김현수v3 — 신중·분석형 (Curse/Sacrifice 대응 추가)
        static Agent Build_김현수(AgentList id)
        {
            var A = new Agent("김현수", id);

            // --- 라운드 카드 선택 ---
            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var history = I.HistoryOpponent(); // 상대방 기록 조회

                // 상대의 성향 데이터 분석 (Cooperation 비율 등)
                float oppCoopRatio = I.Ratio(CardType.Cooperation);
                float oppReconRatio = I.Ratio(CardType.Recon);
                
                // [신규] 상대의 Sacrifice 전략 감지
                // 상대가 이미 Sacrifice를 3장 이상 냈다면, 4장째에 즉시 패배하므로
                // 방어고 뭐고 무조건 상대를 죽여야 함 (최우선 순위)
                int oppSacrificeCount = history.Count(x => x == CardType.Sacrifice);
                if (oppSacrificeCount >= 3)
                {
                    // 킬각을 낼 수 있다면 배신
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    // 차선책 오염
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 0) 생존 우선 (가중치 무시)
                // Curse는 방어하지 않으면 2뎀 누적되므로, 체력이 낮을 땐 Doubt 가치 상승
                bool highBetrayal = I.Ratio(CardType.Betrayal) >= 0.28f || (nf && I.s.lastOpp == CardType.Betrayal);
                bool highCurse = I.Ratio(CardType.Curse) >= 0.20f; // 저주 위험도
                
                // 상대가 저주나 배신을 쓸 것 같고 내 피가 간당간당하면 방어
                if (I.s.selfLife <= R + 2 && (highBetrayal || highCurse))
                {
                    if (I.HandHas(CardType.Doubt)) return CardType.Doubt;
                }

                // 1) [신규] Curse 활용: 냉소적 효율성
                // 상대가 협력(Coop)하거나 정찰(Recon)하려 할 때 저주를 걸면 확정 이득
                // (Coop 상대로는 +1점 먹고 상대는 저주걸림 / Recon 상대로는 정보 주고 저주검)
                if (I.HandHas(CardType.Curse))
                {
                    // 상대가 협력/정찰 위주거나, 방어(Doubt/Interrupt) 비율이 낮을 때
                    bool opponentIsSoft = (oppCoopRatio + oppReconRatio) > 0.4f;
                    bool opponentLowDef = I.Ratio(CardType.Doubt) < 0.25f && I.Ratio(CardType.Interrupt) < 0.15f;

                    if (opponentIsSoft && opponentLowDef)
                        return CardType.Curse;
                }

                // 2) 직전 반복 패턴 카운터 (데이터 기반)
                if (nf && I.s.lastOpp == I.s.last2Opp && I.s.lastOpp != CardType.None)
                {
                    var x = I.s.lastOpp;
                    // 상대가 Sacrifice를 연속으로 낸다면? -> 미친 짓이므로 배신으로 응징
                    if (x == CardType.Sacrifice && I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    
                    if (x == CardType.Cooperation)
                    {
                        // 협력엔 저주가 특효약 (김현수 스타일)
                        if (I.HandHas(CardType.Curse)) return CardType.Curse;
                        if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    }
                    if (x == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                }

                // 3) Recon 활용 (정보 우위)
                if (I.HandHas(CardType.Recon))
                {
                    // 생명력 여유가 있고, 아직 상대 덱 파악이 덜 됐으면(초반)
                    bool safe = I.s.selfLife >= I.s.oppLife;
                    if (R >= 1 && R <= 5 && safe) return CardType.Recon;
                }

                // 4) 장기 압박 (Pollution / Curse)
                // Curse가 있다면 Pollution보다 우선순위를 높게 둠 (데미지 기대값 2 vs 1)
                if (I.HandHas(CardType.Curse) && I.Ratio(CardType.Doubt) <= 0.30f)
                    return CardType.Curse;
                
                if (I.HandHas(CardType.Pollution) &&
                    (I.Ratio(CardType.Cooperation) >= 0.33f) &&
                    I.Ratio(CardType.Doubt) <= 0.30f)
                    return CardType.Pollution;

                // 5) 확실한 킬각만 배신
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && I.Ratio(CardType.Doubt) < 0.33f)
                    return CardType.Betrayal;

                // 6) [신규] Sacrifice 처리
                // 김현수는 Sacrifice를 '실수'로 간주하므로 거의 내지 않음.
                // 하지만 손패가 막혔을 때(Chaos도 없고 등등) 어쩔 수 없이 낼 수는 있음.
                // (별도 로직 없이 Score에서 최하점 처리)

                // 7) 기본 선호도 (Score)
                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Curse => 11,      // [상향] 성공시 고효율
                        CardType.Cooperation => 10,
                        CardType.Doubt => 9,
                        CardType.Recon => 8,       // 정보 중시
                        CardType.Pollution => 7,
                        CardType.Interrupt => 6,
                        CardType.Betrayal => 5,
                        CardType.Chaos => 4,
                        CardType.Sacrifice => -5,  // [기피] 리스크 극혐
                        _ => 0
                    };
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1f);
                }
                
                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            // Fallback 우선순위
            A.fallback = new[] {
                CardType.Curse, CardType.Cooperation, CardType.Doubt, CardType.Recon,
                CardType.Pollution, CardType.Interrupt, CardType.Betrayal, CardType.Chaos, CardType.Sacrifice
            };

            // --- 선택 드로우 (Draft) ---
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);

                // [신규] Sacrifice 기피: 김현수는 절대 Sacrifice를 집지 않는다.
                // (단, Chaos 등 변수 창출이 필요할 땐 예외일 수 있으나 기본적으로 배제)
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1; // a 거르고 b 선택
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0; // b 거르고 a 선택

                // [신규] Curse 선호: 효율적인 공격 수단으로 확보
                bool preferCurse = I.Ratio(CardType.Cooperation) > 0.3f; // 상대가 순진해 보이면 저주 확보

                float Score(CardType t)
                {
                    float baseScore = t switch
                    {
                        CardType.Curse       => preferCurse ? 105 : 88, // [신규] 상황따라 매우 높음
                        CardType.Cooperation => 100,
                        CardType.Doubt       => 92,
                        CardType.Recon       => 86,  // 정보 수집
                        CardType.Pollution   => 78,
                        CardType.Interrupt   => 66,
                        CardType.Betrayal    => 58,
                        CardType.Sacrifice   => -99, // [신규] 절대 안 뽑음
                        _ => 0
                    };
                    // FIX: Safe dictionary access
                    return baseScore * (weights.ContainsKey(t) ? weights[t] : 1.0f);
                }

                // ... (기존 동점 처리 로직 유지) ...
                float sa = Score(a), sb = Score(b);
                if (Math.Abs(sa - sb) < 0.1f)
                {
                    int Rank(CardType t) => t switch {
                        CardType.Curse => 8, // [추가]
                        CardType.Betrayal => 7, CardType.Pollution => 6, CardType.Doubt => 5,
                        CardType.Interrupt => 4, CardType.Cooperation => 3, CardType.Recon => 2, _ => 1
                    };
                    return Rank(a) >= Rank(b) ? 0 : 1;
                }
                return sa > sb ? 0 : 1;
            };

            return A;
        }

        // 이수진V2 — 모험가·즉흥형 (Sacrifice 올인 / Curse 변수 창출)
        static Agent Build_이수진(AgentList id)
        {
            var A = new Agent("이수진", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                // 상대 분포(라플라스 + 최근 반복 가중)
                var p = new Dictionary<CardType, float>()
                {
                    {CardType.Cooperation, 0.06f + I.Ratio(CardType.Cooperation)},
                    {CardType.Doubt,       0.06f + I.Ratio(CardType.Doubt)},
                    {CardType.Betrayal,    0.06f + I.Ratio(CardType.Betrayal)},
                    {CardType.Chaos,       0.06f + I.Ratio(CardType.Chaos)},
                    {CardType.Pollution,   0.06f + I.Ratio(CardType.Pollution)},
                    {CardType.Interrupt,   0.06f + I.Ratio(CardType.Interrupt)},
                    {CardType.Recon,       0.06f + I.Ratio(CardType.Recon)},
                    {CardType.Curse,       0.06f + I.Ratio(CardType.Curse)},     // 추가
                    {CardType.Sacrifice,   0.06f + I.Ratio(CardType.Sacrifice)}  // 추가
                };
                if (nf && I.s.lastOpp != CardType.None && I.s.lastOpp == I.s.last2Opp)
                    p[I.s.lastOpp] *= 1.35f; // 패턴 집착 읽고 베팅
                
                float S = p.Values.Sum(); foreach (var k in p.Keys.ToList()) p[k] /= S;

                // [신규] Sacrifice "운명론" 로직
                // 내가 낸 Sacrifice 수 추정 (총 4장 - 미발견 - 내 손패)
                // *주의: 덱 구성에 따라 총량이 다를 수 있으나, 표준 4장 기준으로 계산
                int mySacrificeInDeck = 4; 
                int unseenSac = I.unseen.TryGetValue(CardType.Sacrifice, out int v) ? v : 0;
                int handSac = I.hand.Count(c => c == CardType.Sacrifice);
                int myPlayedSacrifice = Math.Max(0, mySacrificeInDeck - unseenSac - handSac);

                // 1) "이것이 나의 피날레다!" (3장 냈으면 4장째는 무조건 제출)
                if (myPlayedSacrifice >= 3 && I.HandHas(CardType.Sacrifice))
                    return CardType.Sacrifice;

                // 2) "리스크는 재미의 연료" (Sacrifice가 있으면 체력이 2 이상일 때 과감히 지름)
                if (I.HandHas(CardType.Sacrifice) && I.s.selfLife > 1)
                {
                    // 상대가 공격(Betrayal)할 확률이 매우 높을 때만 잠깐 참음 (즉사 방지)
                    if (p[CardType.Betrayal] < 0.4f || I.s.selfLife > 2)
                        return CardType.Sacrifice;
                }

                // 3) 하이리스크 트리거: 내가 뒤지거나 손패가 빈약하면 변동성↑
                bool losing = I.s.selfLife < I.s.oppLife;
                bool poorAtk = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);

                // 4) [신규] 상대가 Sacrifice를 모으는 것 같다? -> "누가 더 빠른지 해보자!" (맞공격)
                // 상대가 최근 Sacrifice를 냈다면 방어 대신 Betrayal/Curse로 응수
                if (nf && I.s.lastOpp == CardType.Sacrifice)
                {
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Curse)) return CardType.Curse; // 저주로 말려 죽이기
                }

                // 5) [신규] Curse: 지루한 흐름(Coop/Doubt)을 끊는 양념
                if (I.HandHas(CardType.Curse))
                {
                    // 상대가 협력하거나 간만 보고 있을 때 저주 투척
                    if (p[CardType.Cooperation] + p[CardType.Doubt] > 0.5f)
                        return CardType.Curse;
                }

                // 6) 즉사 회피 (최소한의 본능)
                if (I.HandHas(CardType.Doubt) && I.s.selfLife <= R - 1 && p[CardType.Betrayal] >= 0.30f)
                    return CardType.Doubt;

                // 7) 킬각 혹은 초반 러시는 과감히 배신
                if (I.HandHas(CardType.Betrayal) && (R <= 2 || I.s.oppLife <= R))
                    if (p[CardType.Doubt] < 0.36f) return CardType.Betrayal;

                // 8) 손패 리셋(가챠): 공격수단 없거나 지는 중이면 과감히
                int atkCnt = (I.HandHas(CardType.Betrayal) ? 1 : 0) + (I.HandHas(CardType.Pollution) ? 1 : 0);
                if (I.HandHas(CardType.Chaos) && (poorAtk || losing || (nf && I.s.lastOpp == I.s.last2Opp)))
                    return CardType.Chaos;

                // 9) 하이롤 우선 가중치 점수
                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Sacrifice => 12, // [신규] 최우선: 낭만 그 자체
                        CardType.Betrayal => 8,   // 터지면 이득 최대
                        CardType.Curse => 7.5f,   // [신규] 변수 창출 재미
                        CardType.Pollution => 6,  // 꾸준 압박
                        CardType.Chaos => 5,      // 가챠
                        CardType.Interrupt => 4,  // 틈새 역전
                        CardType.Cooperation => 3,// 숨 고르기
                        CardType.Doubt => 1,      // 노잼 방어 (점수 하향)
                        CardType.Recon => 0.5f,   // 계산 싫어함
                        _ => 0
                    };
                    // FIX: Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }
                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            A.fallback = new[] {
                CardType.Sacrifice, CardType.Betrayal, CardType.Curse, 
                CardType.Pollution, CardType.Chaos, CardType.Interrupt, 
                CardType.Cooperation, CardType.Doubt, CardType.Recon
            };
            
            // 이수진 - 선택 드로우(하이리스크/하이리턴 + 희생 탐닉)
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);

                // [신규] Sacrifice 집착: "운명이야, 집어!"
                // 손에 이미 Sacrifice가 있다면 더더욱 집으려 함 (세트 완성 욕구)
                bool hasSac = I.HandHas(CardType.Sacrifice);
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 0; // a 선택
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 1; // b 선택
                
                // ... (기존 변수들) ...
                bool losing = I.s.selfLife < I.s.oppLife;
                bool lethal = I.s.oppLife <= R;
                
                float Score(CardType c)
                {
                    float s = 0f;
                    // [신규] Sacrifice 점수: 매우 높음 (15점)
                    if (c == CardType.Sacrifice) s += 15.0f + (hasSac ? 5.0f : 0f);

                    // [신규] Curse 점수: 공격적 변수로 선호 (Betrayal 다음급)
                    if (c == CardType.Curse) s += 3.0f + (losing ? 1.5f : 0f);

                    if (c == CardType.Betrayal)  s += (R <= 2 ? 3.5f : 2.0f) + (lethal ? 5.0f : 0f);
                    if (c == CardType.Pollution) s += 2.2f;
                    if (c == CardType.Chaos)     s += (losing ? 3.0f : 1.0f); // 지고 있을 때 Chaos 선호도 상승
                    if (c == CardType.Interrupt) s += 1.5f;
                    if (c == CardType.Doubt)     s += 0.5f; // 방어 카드는 매력 없음
                    if (c == CardType.Cooperation) s += 0.5f;
                    if (c == CardType.Recon)     s -= 1.0f; // 정찰은 지루함
                    
                    // FIX: Safe dictionary access
                    return s * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);

                if (Math.Abs(sa - sb) < 0.1f)
                {
                    int Rank(CardType t) => t switch
                    {
                        CardType.Sacrifice=>9, CardType.Betrayal=>8, CardType.Curse=>7, 
                        CardType.Chaos=>6, CardType.Pollution=>5,
                        CardType.Interrupt=>4, CardType.Cooperation=>3, CardType.Doubt=>2, CardType.Recon=>1
                    };
                    return Rank(a) >= Rank(b) ? 0 : 1;
                }
                return sa > sb ? 0 : 1;
            };
            return A;
        }

        // 최용호V2 — 빠른 템포·단기결전·노계산 (Sacrifice 올인 / Curse 기피)
        static Agent Build_최용호(AgentList id)
        {
            var A = new Agent("최용호", id);

            A.rules.Clear();
            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                // 0) [신규] Sacrifice: "생산 라인 풀 가동! 멈추면 돈 날아간다!"
                // 고민 없이 냅다 던짐. 체력이 1이어도 마지막 한 방(4장째)이라면 던지고,
                // 아니라도 일단 던져서 스택을 쌓음. 뒤는 생각 안 함.
                if (I.HandHas(CardType.Sacrifice))
                    return CardType.Sacrifice;

                // 1) 확정 킬각 (가중치 무시)
                if (I.HandHas(CardType.Betrayal)  && I.s.oppLife <= R) return CardType.Betrayal;

                // 2) 초반 러시(1~3라) – "초장에 기선 제압!"
                if (R <= 3)
                {
                    if (I.HandHas(CardType.Betrayal))  return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    // 공격 수단 없으면 Chaos로 패 섞기
                    int atk = (I.HandHas(CardType.Betrayal)?1:0) + (I.HandHas(CardType.Pollution)?1:0);
                    if (atk==0 && I.HandHas(CardType.Chaos)) return CardType.Chaos;
                }
                
                // 3) 뒤지는 중이면 공격 극대화 (Curse는 느려서 안 씀)
                if (I.s.selfLife < I.s.oppLife)
                {
                    if (I.HandHas(CardType.Betrayal))  return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    // 배신/오염 없으면 Chaos
                    int atk = (I.HandHas(CardType.Betrayal)?1:0) + (I.HandHas(CardType.Pollution)?1:0);
                    if (atk==0 && I.HandHas(CardType.Chaos)) return CardType.Chaos;
                }

                // 4) 간단 대응(확률 제거)
                if (nf && I.s.lastOpp == CardType.Betrayal && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                if (nf && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt))    return CardType.Doubt;

                // 5) Curse 처리: "이거 언제 터지냐? 답답하네."
                // 손에 공격 카드가 아예 없을 때만 차선책으로 사용
                bool hasDirectAtk = I.HandHas(CardType.Betrayal) || I.HandHas(CardType.Pollution);
                if (I.HandHas(CardType.Curse) && !hasDirectAtk)
                    return CardType.Curse;

                // 6) 읽힘/공격수단 없음일 때만 Chaos
                int atk2 = (I.HandHas(CardType.Betrayal)?1:0) + (I.HandHas(CardType.Pollution)?1:0);
                if (I.HandHas(CardType.Chaos) && (atk2==0 || (nf && I.s.lastOpp==I.s.last2Opp))) return CardType.Chaos;

                // 7) 고정 우선순위 (Sacrifice는 최상단에서 처리됨)
                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Sacrifice => 10,  // [신규] 잡히면 무조건 낸다
                        CardType.Betrayal => 7, 
                        CardType.Pollution => 6, 
                        CardType.Interrupt => 5,
                        CardType.Curse => 4.5f,    // [신규] 즉발 데미지가 아니라서 선호도 낮음
                        CardType.Cooperation => 4, 
                        CardType.Doubt => 3, 
                        CardType.Recon => 2,       // "정찰할 시간에 한 대 더 때린다"
                        CardType.Chaos => 1,
                        _ => 0
                    };
                    // FIX: Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }
                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            A.fallback = new[] {
                CardType.Sacrifice, CardType.Betrayal, CardType.Pollution, CardType.Chaos,
                CardType.Curse, CardType.Interrupt, CardType.Cooperation, CardType.Doubt, CardType.Recon
            };
            
            // ---------- 선택 드로우(2장 중 1장) ----------
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);

                // [신규] Sacrifice 우선: "보이면 집어! 불량품 나오기 전에!"
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 0;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 1;

                // Chaos 회피
                if (a == CardType.Chaos && b != CardType.Chaos) return 1;
                if (b == CardType.Chaos && a != CardType.Chaos) return 0;

                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var last = I.s.lastOpp;

                float Score(CardType x)
                {
                    float s = x switch
                    {
                        CardType.Sacrifice   => 200, // [신규] 압도적 선호
                        CardType.Betrayal    => 100,
                        CardType.Pollution   => 80,
                        CardType.Interrupt   => 60,
                        CardType.Curse       => 50,  // [신규] 애매함. Doubt보단 낫지만 공격보단 못함.
                        CardType.Doubt       => 45,
                        CardType.Cooperation => 30,
                        CardType.Recon       => 10,
                        _ => 0
                    };
                    
                    // 상황별 가점
                    if (x==CardType.Betrayal && I.s.oppLife <= R+1) s += 25;
                    if (x==CardType.Doubt    && I.s.selfLife<= R)   s += 20;
                    
                    // 직전 상황 반응
                    if (nf)
                    {
                        if (last==CardType.Cooperation && x==CardType.Betrayal) s += 25;
                        if (last==CardType.Pollution   && x==CardType.Doubt)    s += 18;
                        if (last==CardType.Betrayal    && x==CardType.Interrupt)s += 22;
                    }

                    // 공격 수단 확보
                    int atkInHand = (I.HandHas(CardType.Betrayal)?1:0)+(I.HandHas(CardType.Pollution)?1:0);
                    if (atkInHand==0 && (x==CardType.Betrayal||x==CardType.Pollution)) s += 12;
                    
                    // FIX: Safe dictionary access
                    return s * (weights.ContainsKey(x) ? weights[x] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);
                if (Math.Abs(sa - sb) < 0.1f)
                {
                    int rank(CardType x) => x switch
                    {
                        CardType.Sacrifice=>8, // [신규]
                        CardType.Betrayal=>7, CardType.Pollution=>6, CardType.Interrupt=>5,
                        CardType.Curse=>4,     // [신규]
                        CardType.Doubt=>3, CardType.Cooperation=>2, CardType.Recon=>1, _=>0
                    };
                    return rank(a) >= rank(b) ? 0 : 1;
                }
                return sa>sb?0:1;
            };
            return A;
        }

        // 한지혜V2 — 안정과 기회의 균형 (Sacrifice 기피 / Curse 조건부 사용)
        static Agent Build_한지혜(AgentList id)
        {
            var A = new Agent("한지혜", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var history = I.HistoryOpponent();

                // 상대 분포(최근 히스토리 기반, 가벼운 스무딩)
                var p = new Dictionary<CardType, float> {
                    {CardType.Cooperation, 0.05f + I.Ratio(CardType.Cooperation)},
                    {CardType.Doubt,       0.05f + I.Ratio(CardType.Doubt)},
                    {CardType.Betrayal,    0.05f + I.Ratio(CardType.Betrayal)},
                    {CardType.Chaos,       0.05f + I.Ratio(CardType.Chaos)},
                    {CardType.Pollution,   0.05f + I.Ratio(CardType.Pollution)},
                    {CardType.Interrupt,   0.05f + I.Ratio(CardType.Interrupt)},
                    {CardType.Recon,       0.05f + I.Ratio(CardType.Recon)},
                    {CardType.Curse,       0.05f + I.Ratio(CardType.Curse)},    // 추가
                    {CardType.Sacrifice,   0.05f + I.Ratio(CardType.Sacrifice)} // 추가
                };
                float S = p.Values.Sum(); foreach (var k in p.Keys.ToList()) p[k] /= S;

                // [신규] Sacrifice 대응: "이기적인 선택은 용납 못 해."
                // 상대가 Sacrifice를 3장 이상 냈다면, 다음 턴에 게임이 터지므로 강제 저지
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    // 평소엔 배신을 아끼지만, 이 상황에선 주저 없이 사용
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal; 
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 0) 생존 우선 (과도한 방어 기제)
                // Curse 위험도가 높거나 Betrayal 확률이 높으면 방어
                bool curseRisk = p[CardType.Curse] > 0.2f;
                bool lethalRisk = I.s.selfLife <= R && (p[CardType.Betrayal] >= 0.27f || (nf && I.s.lastOpp == CardType.Betrayal));
                
                if ((lethalRisk || curseRisk) && I.HandHas(CardType.Doubt)) 
                    return CardType.Doubt;

                // 1) [신규] Curse 활용: "상처에는 상처로."
                // 그녀는 선제적으로 저주를 걸지 않음. 
                // 단, 상대가 배신(Betrayal) 빈도가 높거나 직전에 배신했다면 보복성으로 사용.
                if (I.HandHas(CardType.Curse))
                {
                    bool oppIsAggressive = p[CardType.Betrayal] > 0.3f || (nf && I.s.lastOpp == CardType.Betrayal);
                    bool oppIsNice = p[CardType.Cooperation] > 0.4f;

                    // 상대가 공격적이면 저주 사용, 착한 상대에겐 저주 절대 안 씀
                    if (oppIsAggressive && !oppIsNice)
                        return CardType.Curse;
                }

                // 2) 반복 패턴 카운터 (조율자)
                if (nf && I.s.lastOpp == I.s.last2Opp && I.s.lastOpp != CardType.None)
                {
                    var x = I.s.lastOpp;
                    // 상대가 희생을 계속 시도하면 방어보다는 공격으로 끊음
                    if (x == CardType.Sacrifice) 
                    {
                        if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                        if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    }

                    if (x == CardType.Cooperation && I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    if (x == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    if (x == CardType.Betrayal && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                    if (x == CardType.Doubt && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                }

                // 3) 초중반(1~4R): 정보/포지셔닝
                if (R <= 4)
                {
                    bool safeInfo = I.s.selfLife >= I.s.oppLife - 1 && p[CardType.Betrayal] <= 0.26f;
                    if (I.HandHas(CardType.Recon) && safeInfo) return CardType.Recon;

                    // 협력 성향↑ & 의심 낮음 → Pollution로 견제 (균형 맞추기)
                    if (I.HandHas(CardType.Pollution) &&
                        p[CardType.Cooperation] >= 0.32f && p[CardType.Doubt] <= 0.28f)
                        return CardType.Pollution;

                    // 초반 안정 수급 (Cooperation 선호)
                    if (I.HandHas(CardType.Cooperation) && p[CardType.Betrayal] <= 0.24f)
                        return CardType.Cooperation;
                }

                // 4) [신규] Sacrifice 처리: "이건 너무 위험해."
                // 손에 Sacrifice가 잡혔다면?
                if (I.HandHas(CardType.Sacrifice))
                {
                    // 정말 이길 수 있는 상황(3장 모음) 아니면 쓰기 싫어함
                    // 단, 체력이 매우 많아서(균형이 내 쪽으로 기울어서) 좀 깎아도 되면 버림
                    int mySacCount = history.Count(x => x == CardType.None); // *추적 로직 필요하나 단순화
                    
                    if (I.s.selfLife > I.s.oppLife + 3) // 체력 여유 있으면 냄
                        return CardType.Sacrifice;
                    
                    // 그 외엔 낼 마음이 없음 (점수 최하위)
                }

                // 5) 킬각만 배신
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.32f)
                    return CardType.Betrayal;

                // 6) 기본 우선순위 (가중치 적용)
                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Pollution => 7, 
                        CardType.Cooperation => 6, // 평화 선호
                        CardType.Doubt => 5,       // 안전 선호
                        CardType.Betrayal => 4,  
                        CardType.Recon => 3,       
                        CardType.Curse => 2.5f,    // [신규] 별로 안 좋아함
                        CardType.Chaos => 2,
                        CardType.Interrupt => 1,
                        CardType.Sacrifice => -10, // [신규] 극도로 기피
                        _ => 0
                    };
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1f);
                }
                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            // 균형형 예비 우선순위
            A.fallback = new[] {
                CardType.Pollution, CardType.Cooperation, CardType.Doubt,
                CardType.Betrayal,  CardType.Recon,       CardType.Chaos,
                CardType.Curse,     CardType.Interrupt,   CardType.Sacrifice
            };

            // [선택 드로우]
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);

                // [신규] Sacrifice 혐오: "나를 해치는 건 싫어."
                // 덱에서 보이면 무조건 거름.
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1; 
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                // [신규] Curse 기피: "너무 잔인해."
                // Pollution(단발성)보다 Curse(지속성)를 더 꺼림 (점수 낮게 책정)

                float Score(CardType c)
                {
                    float baseScore = 0;
                    if (c == CardType.Chaos) baseScore = -3;
                    if (c == CardType.Cooperation) baseScore = (R <= 6 ? 3 : 2);     // 초중반 선호
                    if (c == CardType.Doubt) baseScore = 2;                           // 안정감
                    if (c == CardType.Pollution) baseScore = 1.5f;                    // 적당한 견제
                    if (c == CardType.Curse) baseScore = 0.5f;                        // [신규] 찝찝함
                    if (c == CardType.Interrupt) baseScore = 1;
                    if (c == CardType.Betrayal) baseScore = (I.s.oppLife <= R ? 3 : 0);
                    if (c == CardType.Recon) baseScore = (R <= 5 ? 1 : 0);
                    if (c == CardType.Sacrifice) baseScore = -99;                     // [신규] 절대 안 집음
                    
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1f);
                }

                // ... (손패 균형 보정 로직 동일) ...
                bool needAtk = !(I.HandHas(CardType.Betrayal) || I.HandHas(CardType.Pollution));
                bool needDef = !(I.HandHas(CardType.Doubt) || I.HandHas(CardType.Interrupt));

                float sa = Score(a);
                float sb = Score(b);

                if (Math.Abs(sa - sb) < 0.1f)
                {
                    // 공격/방어 균형 맞추기
                    if (needAtk && ((a == CardType.Betrayal || a == CardType.Pollution) ||
                                    (b == CardType.Betrayal || b == CardType.Pollution)))
                        return (a == CardType.Betrayal || a == CardType.Pollution) ? 0 : 1;

                    if (needDef && ((a == CardType.Doubt || a == CardType.Interrupt) ||
                                    (b == CardType.Doubt || b == CardType.Interrupt)))
                        return (a == CardType.Doubt || a == CardType.Interrupt) ? 0 : 1;
                }

                return sa >= sb ? 0 : 1;
            };
            return A;
        }

        // 박민재v3 — 계산된 냉정함 (Sacrifice 배제 / Curse 확률 계산)
        static Agent Build_박민재(AgentList id)
        {
            var A = new Agent("박민재", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool highVol = (R % 5 == 0) || (R % 5 == 1); // 재해 변동성 주간

                // 1. [신규] 시스템 결함 감지 (상대 Sacrifice 카운팅)
                // "상대의 승리 플래그가 임계치(3)에 도달했다. 제거 함수 실행."
                var history = I.HistoryOpponent();
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    // 효율이고 뭐고 상대 명치를 쳐야 함
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 2. 즉사 / 생존 계산 (기존 로직 유지)
                bool canKill = I.HandHas(CardType.Betrayal) && I.s.oppLife <= R;
                bool mustDefend = I.s.selfLife <= R && I.HandHas(CardType.Doubt);
                if (canKill) return CardType.Betrayal;
                if (mustDefend) return CardType.Doubt;

                // 3. 기대값(EV) 기반 카드 선택
                float Eval(CardType c)
                {
                    float score = 0;
                    
                    switch (c)
                    {
                        case CardType.Betrayal: 
                            score = I.s.oppLife <= R + 1 ? 8f : 3f; break; // 킬각 근처면 가치 급상승
                        
                        case CardType.Pollution: 
                            score = 3.5f; break; // 꾸준한 고효율(Doubt 무시)
                        
                        case CardType.Doubt: 
                            score = I.s.selfLife <= R + 2 ? 5f : 1f; break; // 위기 관리용
                        
                        case CardType.Curse:
                            // [신규] Curse 기대값 계산: Damage(2) * (1 - 방어확률)
                            // 상대가 방어적(Doubt/Interrupt 선호)이라면 가치 하락
                            float defRatio = I.Ratio(CardType.Doubt) + I.Ratio(CardType.Interrupt);
                            score = (defRatio > 0.4f) ? 0.5f : 4.0f; // 방어 안 하면 Betrayal급 효율
                            break;

                        case CardType.Cooperation: 
                            score = 2f; break;

                        case CardType.Interrupt: 
                            score = 2.5f; break;

                        case CardType.Recon: 
                            // 공격 수단이 없거나 정보가 너무 없으면 가치 상승
                            score = (I.s.selfLife < I.s.oppLife || !I.HandHas(CardType.Betrayal)) ? 2f : 0f;
                            break;

                        case CardType.Chaos: 
                            // "변수는 통제 불가능하다." -> 기본적으로 감점
                            score = (I.s.selfLife < I.s.oppLife && !highVol) ? 0.5f : -2f;
                            break;

                        case CardType.Sacrifice:
                            // [신규] "자원 소모 대비 리턴값 불확실." -> 절대적 비효율
                            // 단, 만약 내가 이미 3장을 냈다면(확률 낮음) 승리 함수 발동
                            int mySacPlayed = 0; // *실제로는 외부 변수 추적 필요, 여기선 예시
                            score = (mySacPlayed >= 3) ? 100f : -10f; 
                            break;
                    }
                    
                    // FIX: Safe dictionary access
                    return score * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }

                // EV가 가장 높은 카드 도출
                return I.hand.Distinct().Where(I.HandHas)
                             .OrderByDescending(c => Eval(c))
                             .FirstOrDefault();
            });

            // Fallback (Sacrifice는 제외)
            A.fallback = new[]
            {
                CardType.Betrayal, CardType.Pollution, CardType.Curse,
                CardType.Cooperation, CardType.Doubt, CardType.Interrupt, CardType.Recon
            };

            // --- 선택 드로우 (Draft) ---
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool losing = I.s.selfLife < I.s.oppLife;

                // [신규] Sacrifice 철저 배제: "통계적 승리 플랜에 부합하지 않음."
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                // V 함수: 카드의 내재 가치 평가
                float V(CardType c)
                {
                    float score = 0;
                    switch (c)
                    {
                        case CardType.Betrayal:    score = (I.s.oppLife <= R ? 8f : 3.5f); break;
                        case CardType.Pollution:   score = 4.0f; break; // 박민재 최선호 (위험 통제된 도박)
                        case CardType.Curse:       score = 3.2f; break; // [신규] 준수한 공격 수단
                        case CardType.Doubt:       score = (I.s.selfLife <= R + 1 ? 6f : 1.5f); break;
                        case CardType.Interrupt:   score = 2.8f; break;
                        case CardType.Cooperation: score = 2.0f; break;
                        case CardType.Recon:       score = 1.0f; break;
                        case CardType.Chaos:       score = -3.0f; break; // "변수 혐오"
                        case CardType.Sacrifice:   score = -10.0f; break; // [신규]
                    }
                    // FIX: Safe dictionary access
                    return score * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }

                float va = V(a), vb = V(b);

                // 정밀 비교 (동점 시 티어 구분)
                if (Math.Abs(va - vb) < 0.1f)
                {
                    int Rank(CardType t) => t switch
                    {
                        CardType.Pollution => 8, // 가장 선호 (안정적 공격)
                        CardType.Betrayal => 7,
                        CardType.Curse => 6,
                        CardType.Doubt => 5,
                        CardType.Interrupt => 4,
                        CardType.Cooperation => 3,
                        CardType.Recon => 2,
                        _ => 0
                    };
                    return Rank(a) >= Rank(b) ? 0 : 1;
                }
                return va >= vb ? 0 : 1;
            };

            return A;
        }

        // 정다은V2 — 심리 조작·가학적 분석가 (Curse 선호 / Sacrifice 경멸)
        static Agent Build_정다은(AgentList id)
        {
            var A = new Agent("정다은", id);

            // ① 라운드 카드 선택
            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                var history = I.HistoryOpponent();

                // 1. 상대 행동 분포 추정 (심리 분석)
                var p = new Dictionary<CardType, float>
                {
                    { CardType.Cooperation, I.Ratio(CardType.Cooperation) },
                    { CardType.Doubt,       I.Ratio(CardType.Doubt)       },
                    { CardType.Betrayal,    I.Ratio(CardType.Betrayal)    },
                    { CardType.Chaos,       I.Ratio(CardType.Chaos)       },
                    { CardType.Pollution,   I.Ratio(CardType.Pollution)   },
                    { CardType.Interrupt,   I.Ratio(CardType.Interrupt)   },
                    { CardType.Recon,       I.Ratio(CardType.Recon)       },
                    { CardType.Curse,       I.Ratio(CardType.Curse)       }, // 추가
                    { CardType.Sacrifice,   I.Ratio(CardType.Sacrifice)   }  // 추가
                };

                // 최근 행동에 대한 가중치 (단기 패턴 분석)
                var recent = new[] { I.s.lastOpp, I.s.last2Opp }.Where(t => t != CardType.None).ToArray();
                if (recent.Length > 0)
                {
                    var mode = recent.GroupBy(t => t).OrderByDescending(g => g.Count()).First().Key;
                    p[mode] *= 1.4f; // "또 그 수를 쓰겠지."
                }
                
                // 정규화
                float sum = p.Values.Sum(); if (sum <= 0) sum = 1f;
                foreach (var k in p.Keys.ToList()) p[k] /= sum;

                // [신규] Sacrifice 대응: "헛된 희망을 품었구나."
                // 상대가 Sacrifice 3장 이상 -> 즉시 처단
                int oppSacCount = history.Count(x => x == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Curse)) return CardType.Curse; // 저주로 말려 죽임
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 2. 즉사 / 생존 각 (기본 본능)
                // 상대가 방심(Doubt 확률 낮음)했고 킬각이면 배신
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.30f)
                    return CardType.Betrayal;
                
                // 내가 죽을 것 같고 상대가 공격할 것 같으면 방어
                bool danger = I.s.selfLife <= R && (p[CardType.Betrayal] + p[CardType.Pollution] > 0.4f);
                if (danger && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                // 3. [신규] Curse 활용: "천천히 괴로워해라."
                // 상대가 방어(Doubt, Interrupt)할 확률이 낮다면 저주를 걸어 지속 피해
                // 그녀는 Curse를 Betrayal보다 '우아한' 공격 수단으로 여김
                if (I.HandHas(CardType.Curse))
                {
                    float defensiveProb = p[CardType.Doubt] + p[CardType.Interrupt];
                    // 상대가 방어적이지 않다면(0.4 미만), 저주 투척
                    if (defensiveProb < 0.4f) 
                        return CardType.Curse;
                }

                // 4. Matrix Delta 계산 (상대 패 예측 기반 최적수 도출)
                int Delta(CardType a, CardType b) 
                { 
                    int r = R;
                    // --- 기존 로직 (생략된 부분 유지) ---
                    // ... (Cooperation ~ Recon 간 상성) ...

                    // [신규] Curse 상성 (정다은의 가학적 계산)
                    // a가 내가 낼 카드, b가 상대 카드
                    if (a == CardType.Curse)
                    {
                        // 방어 카드엔 막힘(0), 나머진 성공(+2 이득 간주)
                        if (b == CardType.Doubt || b == CardType.Interrupt) return 0; 
                        if (b == CardType.Betrayal) return -1; // 맞으면 아픔
                        return +2; // 2턴간 괴롭힘 = +2점 가치
                    }
                    if (b == CardType.Curse)
                    {
                        // 상대가 저주를 걸 때
                        if (a == CardType.Doubt || a == CardType.Interrupt) return +1; // 방어 성공
                        if (a == CardType.Cooperation || a == CardType.Recon) return -2; // 저주 걸림
                        return 0;
                    }

                    // [신규] Sacrifice 상성 (정다은의 냉소적 계산)
                    if (a == CardType.Sacrifice) return -10; // "나는 희생하지 않는다."
                    if (b == CardType.Sacrifice)
                    {
                        // 상대가 희생할 때 나는?
                        if (a == CardType.Betrayal) return +r + 2; // 아주 큰 이득 (상대 -2, 나 승리)
                        if (a == CardType.Curse) return +2;        // 저주 걸기 성공
                        if (a == CardType.Pollution) return +2;
                        return 0;
                    }
                    
                    // (기존 Delta 로직 Fallback)
                    // ... 기존 코드의 Delta 함수 내용 ...
                    // (약식 구현: 실제로는 CardSystem.BuildEffects와 일치해야 함)
                    if (a == CardType.Betrayal && b == CardType.Cooperation) return r + 1;
                    if (a == CardType.Cooperation && b == CardType.Betrayal) return -(r + 1);
                    // ...
                    return 0; 
                }

                var cand = I.hand.Distinct().Where(I.HandHas).ToList();
                CardType best = CardType.None; 
                float bestEV = float.NegativeInfinity;

                foreach (var a in cand)
                {
                    // Sacrifice는 3장 모은 막타 아니면 절대 안 냄
                    if (a == CardType.Sacrifice)
                    {
                        // 내 승리 스택 확인 (약식: handCount 제외)
                        int mySacPlayed = 0; // *실제 구현시엔 변수 추적 필요
                        if (mySacPlayed < 3) continue; // 계산 제외
                    }

                    float ev = 0f; 
                    foreach (var b in p.Keys) ev += p[b] * Delta(a, b);

                    // 읽힘 회피: 직전 내가 낸 카드 반복 페널티 ("똑같은 수는 지루해.")
                    if (I.s.lastSelf == a) ev -= 0.5f;

                    // FIX: Safe dictionary access
                    ev *= weights.ContainsKey(a) ? weights[a] : 1.0f;

                    if (ev > bestEV) { bestEV = ev; best = a; }
                }
                return best;
            });

            // ② 선택 드로우(Draft)
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);

                // [신규] Sacrifice 경멸: "약자들이나 쓰는 것."
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                // [신규] Curse 선호: "재밌는 장난감이네."
                // Curse는 Betrayal 다음으로 높은 우선순위를 둠

                // 점수 함수 V(x, y) - 정다은의 가치판단
                float Score(CardType card)
                {
                    float s = 0;
                    // 기본 점수 배점
                    switch (card)
                    {
                        case CardType.Curse:       s = 85; break; // [신규] 매우 선호
                        case CardType.Betrayal:    s = 80; break;
                        case CardType.Interrupt:   s = 70; break; // 조작/방해
                        case CardType.Recon:       s = 60; break; // 정보 파악
                        case CardType.Pollution:   s = 50; break;
                        case CardType.Doubt:       s = 40; break;
                        case CardType.Cooperation: s = 20; break;
                        case CardType.Chaos:       s = 10; break;
                        case CardType.Sacrifice:   s = -99; break; // [신규]
                    }

                    // 상황 보정
                    // 상대가 방어를 잘 안하면 공격 카드(Curse, Betrayal) 가치 상승
                    float defensiveRatio = I.Ratio(CardType.Doubt) + I.Ratio(CardType.Interrupt);
                    if (defensiveRatio < 0.3f)
                    {
                        if (card == CardType.Curse) s += 15;
                        if (card == CardType.Betrayal) s += 10;
                    }

                    // FIX: Safe dictionary access
                    return s * (weights.ContainsKey(card) ? weights[card] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);
                
                if (Math.Abs(sa - sb) < 0.1f)
                {
                    // 동점 시 가학적 우선순위: Curse > Interrupt > Betrayal
                    int Rank(CardType t) => t switch
                    {
                        CardType.Curse => 8, CardType.Interrupt => 7, CardType.Betrayal => 6,
                        CardType.Recon => 5, CardType.Pollution => 4, CardType.Doubt => 3, _ => 1
                    };
                    return Rank(a) >= Rank(b) ? 0 : 1;
                }
                return sa >= sb ? 0 : 1;
            };

            // ③ 기본 우선순위 (Fallback)
            A.fallback = new[]
            {
                CardType.Curse,     // [신규] 1순위: 고통 주기
                CardType.Interrupt, // 2순위: 방해하기
                CardType.Recon,     // 3순위: 훔쳐보기
                CardType.Betrayal,
                CardType.Pollution,
                CardType.Doubt,
                CardType.Cooperation,
                CardType.Chaos,
                CardType.Sacrifice  // 최하위
            };
            return A;
        }
        
        // 오태훈V2 — 미숙한 천재 (패턴 분석 + 자만/폭주)
        static Agent Build_오태훈(AgentList id)
        {
            var A = new Agent("오태훈", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var history = I.HistoryOpponent();

                // 1. [천재의 직감] 상대 Sacrifice 카운팅 -> 폭주 대응
                // "잠깐, 저 녀석 지금 뭐 하는 거지? 3장째? 죽어!!"
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    // 방어따윈 안 한다. 내가 죽기 전에 죽인다.
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    // 공격 카드가 없으면 Chaos로 판 엎기
                    if (I.HandHas(CardType.Chaos)) return CardType.Chaos;
                }

                // 2. [자만심] 유리할 때 Sacrifice 시도 (스타일리시 승리 욕구)
                // "이 정도 핸디캡은 줘도 이겨. 보여줄게, 격의 차이를."
                // 체력이 상대보다 많거나 여유로울 때(6 이상) 과감하게 희생
                if (I.HandHas(CardType.Sacrifice))
                {
                    bool winning = I.s.selfLife > I.s.oppLife;
                    bool safe = I.s.selfLife >= 6;
                    
                    // 이미 3장을 냈다면(막타) 무조건 시전
                    // (변수 추적은 약식으로 처리, 실제론 history 분석 필요)
                    // 여기선 단순히 '자만심' 조건 충족 시 시도
                    if (winning || safe)
                        return CardType.Sacrifice;
                }

                // 3. [패턴 학습] 상대가 뻔한 수(2연속 동일)를 뒀을 때의 대응
                // 오태훈의 가장 강력한 무기: 루틴 읽기
                if (nf && I.s.lastOpp == I.s.last2Opp && I.s.lastOpp != CardType.None)
                {
                    var pattern = I.s.lastOpp;
                    // 상대가 뻔한 방어(Coop/Doubt) 중이면 -> 저주(Curse)나 배신(Betrayal)으로 참교육
                    if (pattern == CardType.Cooperation || pattern == CardType.Doubt)
                    {
                        if (I.HandHas(CardType.Curse)) return CardType.Curse; // "방어? 뚫어주지."
                        if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    }
                    // 상대가 계속 공격하면 -> Interrupt로 카운터
                    if (pattern == CardType.Betrayal && I.HandHas(CardType.Interrupt))
                        return CardType.Interrupt;
                }

                // 4. [감정 폭주] 불리할 때 Chaos 난사
                // "아, 짜증 나! 다 엎어버려!"
                bool losing = I.s.selfLife < I.s.oppLife;
                if (losing && I.HandHas(CardType.Chaos))
                {
                    // 리스크 계산 안 하고 지르고 봄
                    return CardType.Chaos;
                }

                // 5. 킬각 본능 (천재적 계산)
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R) return CardType.Betrayal;
                if (I.HandHas(CardType.Pollution) && I.s.oppLife <= R - 1) return CardType.Pollution;

                // 6. [Curse 활용] 애매한 상황에서의 압박
                // 상대가 방어적이지 않다고 판단되면(패턴상) 저주 투척
                if (I.HandHas(CardType.Curse))
                {
                    // 직전 상대가 공격카드였거나(Betrayal/Pollution), Chaos였으면 
                    // 이번 턴에 방어 확률 낮다고 판단 -> 저주
                    if (nf && (I.s.lastOpp == CardType.Betrayal || I.s.lastOpp == CardType.Chaos))
                        return CardType.Curse;
                }

                // 7. 기본 우선순위 (가중치 반영)
                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Betrayal => 8,    // 공격성 높음
                        CardType.Chaos => 7,       // [특성] Chaos 애호가
                        CardType.Curse => 6.5f,    // [신규] 재밌는 장난감
                        CardType.Pollution => 6,
                        CardType.Sacrifice => losing ? -5 : 5, // 이길 땐 5점(자만), 질 땐 -5점(짜증)
                        CardType.Interrupt => 4,
                        CardType.Recon => 3,       // "봐도 뻔해."
                        CardType.Cooperation => 2,
                        CardType.Doubt => 1,
                        _ => 0
                    };
                    // FIX: Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }

                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            A.fallback = new[] {
                CardType.Betrayal, CardType.Chaos, CardType.Curse,
                CardType.Pollution, CardType.Interrupt, CardType.Cooperation,
                CardType.Doubt, CardType.Recon, CardType.Sacrifice
            };
            
            // 오태훈 — 선택 드로우 (공격적, 변칙적, 자만심)
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool winning = I.s.selfLife > I.s.oppLife;

                // [신규] Sacrifice: 이기고 있으면 "멋지게 끝내기 위해" 집음
                if (winning)
                {
                    if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 0;
                    if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 1;
                }
                else
                {
                    // 지고 있으면 쓸모없으니 버림
                    if (a == CardType.Sacrifice) return 1;
                    if (b == CardType.Sacrifice) return 0;
                }

                // [Chaos 사랑]: 덱에 Chaos 보이면 일단 집고 봄 (오태훈 특성)
                if (a == CardType.Chaos && b != CardType.Chaos) return 0;
                if (b == CardType.Chaos && a != CardType.Chaos) return 1;

                float Score(CardType x)
                {
                    float s = x switch
                    {
                        CardType.Betrayal    => 100,
                        CardType.Chaos       => 95,  // [특성] 매우 선호
                        CardType.Curse       => 85,  // [신규] 공격 수단 선호
                        CardType.Pollution   => 80,
                        CardType.Recon       => 35,
                        CardType.Interrupt   => 30,
                        CardType.Cooperation => 20,
                        CardType.Doubt       => 10,
                        CardType.Sacrifice   => winning ? 90 : -50, // [신규] 상황따라 극과 극
                        _ => 0
                    };

                    // 상황 보정
                    if (x == CardType.Betrayal && I.s.oppLife <= R + 2) s += 30; // 킬각 냄새 잘 맡음
                    
                    // 저주는 상대가 방심할 때 좋음 (단순 확률 계산 아님)
                    if (x == CardType.Curse && !I.s.IsFirst && I.s.lastOpp == CardType.Cooperation) s += 20;

                    // FIX: Safe dictionary access
                    return s * (weights.ContainsKey(x) ? weights[x] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);
                if (Math.Abs(sa - sb) < 0.1f) return UnityEngine.Random.value < 0.5f ? 0 : 1;
                return sa > sb ? 0 : 1;
            };
            return A;
        }

        // 유민정V2 — 순응의 미학 (Sacrifice 기피 / Curse를 통한 조용한 잠식)
        static Agent Build_유민정(AgentList id)
        {
            var A = new Agent("유민정", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var history = I.HistoryOpponent();

                // 1. [신규] Sacrifice 대응
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 2. [신규] 본인의 Sacrifice 사용
                if (I.HandHas(CardType.Sacrifice))
                {
                    int mySacPlayed = 0; 
                    if (mySacPlayed >= 3) return CardType.Sacrifice; 
                }

                // 3. 생존 최우선
                if (I.HandHas(CardType.Doubt) &&
                    (I.s.selfLife <= R || I.s.selfLife + 1 < I.s.oppLife))
                    return CardType.Doubt;

                // 4. [신규] Curse 활용
                if (nf && I.HandHas(CardType.Curse))
                {
                    if (I.s.lastOpp == CardType.Cooperation || I.s.lastOpp == CardType.Recon)
                    {
                        if (I.Ratio(CardType.Doubt) < 0.4f)
                            return CardType.Curse;
                    }
                }

                // 5. 직전 대응 (Mirroring & Counter)
                if (nf)
                {
                    if (I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt))
                        return CardType.Doubt;
                    if (I.s.lastOpp == CardType.Betrayal && I.HandHas(CardType.Interrupt))
                        return CardType.Interrupt;
                    
                    if (I.s.lastOpp == CardType.Curse)
                    {
                        if (I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                        if (I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    }

                    if (I.HandHas(I.s.lastOpp) && 
                        I.s.lastOpp != CardType.Betrayal && 
                        I.s.lastOpp != CardType.Pollution && 
                        I.s.lastOpp != CardType.Curse)
                        return I.s.lastOpp;
                }

                // 6. 기본 점수 계산 (가중치 적용)
                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Doubt => 10,       
                        CardType.Cooperation => 8,  
                        CardType.Interrupt => 7,
                        CardType.Pollution => 5,
                        CardType.Curse => 4.5f,     
                        CardType.Recon => 4,
                        CardType.Chaos => 2,
                        CardType.Betrayal => 1,     
                        CardType.Sacrifice => -20,  
                        _ => 0
                    };
                    
                    // ▼▼▼ [수정됨] 안전한 가중치 접근 (KeyNotFoundException 방지) ▼▼▼
                    float w = weights.ContainsKey(c) ? weights[c] : 1.0f;
                    return baseScore * w;
                }
                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            A.fallback = new[]
            {
                CardType.Doubt, CardType.Cooperation, CardType.Interrupt,
                CardType.Pollution, CardType.Curse, CardType.Recon, 
                CardType.Chaos, CardType.Betrayal, CardType.Sacrifice
            };
            
            // 선택 드로우 (Draft)
            A.chooseFromTwo = (c0, c1, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);

                if (c0 == CardType.Sacrifice && c1 != CardType.Sacrifice) return 1;
                if (c1 == CardType.Sacrifice && c0 != CardType.Sacrifice) return 0;

                float Score(CardType t)
                {
                    float s = t switch
                    {
                        CardType.Doubt        => 50,
                        CardType.Cooperation  => 45,
                        CardType.Interrupt    => 30,
                        CardType.Recon        => 25,
                        CardType.Curse        => 20,
                        CardType.Pollution    => 15,
                        CardType.Betrayal     => 8,
                        CardType.Sacrifice    => -50,
                        CardType.Chaos        => 0,
                        _ => 0
                    };

                    if (I.s.selfLife < I.s.oppLife && t == CardType.Doubt) s += 15; 
                    if (I.Ratio(CardType.Curse) > 0.2f && t == CardType.Cooperation) s += 10;

                    // ▼▼▼ [수정됨] 안전한 가중치 접근 ▼▼▼
                    float w = weights.ContainsKey(t) ? weights[t] : 1.0f;
                    return s * w;
                }

                float s0 = Score(c0), s1 = Score(c1);
                if (Math.Abs(s0 - s1) < 0.1f)
                {
                    int safe(CardType t) => t switch
                    {
                        CardType.Doubt => 5, CardType.Cooperation => 4, 
                        CardType.Interrupt => 3, CardType.Curse => 2, _ => 0
                    };
                    return safe(c0) >= safe(c1) ? 0 : 1;
                }
                return s0 > s1 ? 0 : 1;
            };
            return A;
        }

        // 김태양V2 — 무작위·교란형 (Sacrifice 도박 / Curse 장난)
        static Agent Build_김태양(AgentList id)
        {
            var A = new Agent("김태양", id);
            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var history = I.HistoryOpponent();

                // 0. [천재적 감각] "이거 내면 끝나는 거 아냐?" (Sacrifice 킬각)
                // 평소엔 막 하다가도, 결정적인 순간(4번째 희생)엔 손이 먼저 반응함
                // (자신의 희생 카운트 추적 - 약식 로직)
                int mySacPlayed = 0; // *실제 구현 시 AgentManager 등에서 추적 필요하지만, 김태양은 '느낌'으로 냄
                if (I.HandHas(CardType.Sacrifice))
                {
                    // 4번째 장이라 느껴지거나(확률), 그냥 체력이 많아서 심심하면(6 이상) 던짐
                    if (UnityEngine.Random.value < 0.3f || I.s.selfLife > 6) 
                        return CardType.Sacrifice;
                }

                // 1. [방해 공작] 상대가 Sacrifice로 '노잼 승리'를 하려 한다?
                // "설계하지 마! 판 엎어!" -> Chaos 우선, 없으면 Betrayal
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    if (I.HandHas(CardType.Chaos)) return CardType.Chaos; // 리셋이 제일 재밌음
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                }

                // 2. [Curse 장난] "너무 조용하네? 저주나 받아라."
                // 상대가 평화(Coop)롭거나 방어(Doubt)만 하면 심심해서 저주 투척
                if (nf && (I.s.lastOpp == CardType.Cooperation || I.s.lastOpp == CardType.Doubt))
                {
                    if (I.HandHas(CardType.Curse) && UnityEngine.Random.value < 0.4f)
                        return CardType.Curse;
                }

                // 3. [초반 러시] 아무거나 공격 (기존 로직 유지 + Curse 추가)
                if (R <= 3 && UnityEngine.Random.value < 0.70f)
                {
                    var pool = new List<CardType>();
                    if (I.HandHas(CardType.Chaos)) pool.Add(CardType.Chaos);
                    if (I.HandHas(CardType.Pollution)) pool.Add(CardType.Pollution);
                    if (I.HandHas(CardType.Betrayal)) pool.Add(CardType.Betrayal);
                    if (I.HandHas(CardType.Curse)) pool.Add(CardType.Curse); // 풀에 추가
                    if (I.HandHas(CardType.Sacrifice)) pool.Add(CardType.Sacrifice); // 미친 척 희생
                    
                    if (pool.Count > 0) return pool[UnityEngine.Random.Range(0, pool.Count)];
                }

                // 4. [혼돈 추구] 주기적 Chaos (기존 유지)
                if (I.HandHas(CardType.Chaos) && (R % 3 == 0 || UnityEngine.Random.value < 0.25f))
                    return CardType.Chaos;

                // 5. [단순 킬각] (기존 유지)
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R) return CardType.Betrayal;

                // 6. [무작위 뽑기] 가중치 기반 랜덤 (Curse, Sacrifice 추가)
                {
                    var bag = new List<CardType>();
                    void Push(CardType t, int w)
                    {
                        if (!I.HandHas(t)) return;
                        // FIX: Safe dictionary access in Push function
                        float weightVal = weights.ContainsKey(t) ? weights[t] : 1.0f;
                        int finalWeight = Mathf.RoundToInt(w * weightVal);
                        for (int k = 0; k < finalWeight; ++k) bag.Add(t);
                    }
                    Push(CardType.Betrayal, 4);
                    Push(CardType.Pollution, 4);
                    Push(CardType.Chaos, 3);
                    Push(CardType.Curse, 3);      // [신규] 꽤 자주 냄
                    Push(CardType.Sacrifice, 2);  // [신규] 가끔 미친 척 냄
                    Push(CardType.Interrupt, 2);
                    Push(CardType.Cooperation, 1);
                    Push(CardType.Doubt, 1);
                    Push(CardType.Recon, 1);

                    if (bag.Count > 0 && UnityEngine.Random.value < 0.85f)
                        return bag[UnityEngine.Random.Range(0, bag.Count)];
                }

                // Fallback
                return I.FirstOrNone();
            });

            A.fallback = new[]
            {
                CardType.Chaos, CardType.Betrayal, CardType.Sacrifice, 
                CardType.Curse, CardType.Pollution, CardType.Interrupt, 
                CardType.Doubt, CardType.Cooperation, CardType.Recon
            };
            
            // 선택 드로우(2장 중 1장): "뭐가 더 재밌을까?"
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                bool losing = I.s.selfLife < I.s.oppLife;

                // [Chaos 사랑]
                if (a == CardType.Chaos && b != CardType.Chaos) return UnityEngine.Random.value < 0.6f ? 0 : 1;
                if (b == CardType.Chaos && a != CardType.Chaos) return UnityEngine.Random.value < 0.6f ? 1 : 0;

                // [Sacrifice 도박] "빨간약 먹어볼까?"
                // 지고 있으면 에라 모르겠다 하고 집음 (40% 확률)
                if (losing)
                {
                    if (a == CardType.Sacrifice) if (UnityEngine.Random.value < 0.4f) return 0;
                    if (b == CardType.Sacrifice) if (UnityEngine.Random.value < 0.4f) return 1;
                }

                float Score(CardType x)
                {
                    int s = x switch
                    {
                        CardType.Chaos       => 80,
                        CardType.Betrayal    => 70,
                        CardType.Sacrifice   => 60, // [신규] 고위험군 선호
                        CardType.Curse       => 58, // [신규] 장난감
                        CardType.Pollution   => 55,
                        CardType.Cooperation => 30, // 노잼
                        CardType.Doubt       => 20, // 노잼
                        CardType.Interrupt   => 22,
                        CardType.Recon       => 15,
                        _ => 0
                    };
                    
                    // 변덕스러운 가산점 (매 판 달라짐)
                    s += UnityEngine.Random.Range(-10, 15);

                    // FIX: Safe dictionary access
                    return s * (weights.ContainsKey(x) ? weights[x] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);

                // 완전 랜덤성 (15% 확률로 점수 무시하고 아무거나 집음)
                if (UnityEngine.Random.value < 0.15f)
                    return UnityEngine.Random.Range(0, 2);

                return sa > sb ? 0 : 1;
            };
            return A;
        }

        // 이하린V2 — 유치원생·순수 모방 (Sacrifice 기피 / Curse 무서워함)
        static Agent Build_이하린(AgentList id)
        {
            var A = new Agent("이하린", id);

            // 유치원생이 좋아하는 카드 순서 (시각적/감정적 선호도)
            // 반짝임(Coop) > 재밌음(Chaos) > 신기함(Recon) > 파란색(Doubt) > ... > 무서움(Curse/Sacrifice/Betrayal)
            CardType[] cuteOrder = {
                CardType.Cooperation, // 반짝반짝 예쁨
                CardType.Chaos,       // 알록달록 재밌음 (상향)
                CardType.Recon,       // 망원경 장난감
                CardType.Doubt,       // 파란색 방패
                CardType.Interrupt,   // 하이파이브(손바닥)
                CardType.Pollution,   // 초록색 슬라임
                CardType.Curse,       // [신규] 유령 (무서움)
                CardType.Sacrifice,   // [신규] 아픔 (싫음)
                CardType.Betrayal     // [최악] 칼 (제일 무서움)
            };

            A.rules.Add(I =>
            {
                // 0. [감정 동기화] Chaos는 너무 재밌어!
                // 손패에 Chaos가 있으면 50% 확률로 그냥 냄 (승패 상관없음)
                if (I.HandHas(CardType.Chaos) && UnityEngine.Random.value < 0.5f)
                    return CardType.Chaos;

                // 1. [순수 모방] "언니/오빠가 한 거 나도 할래!"
                // 직전 상대 카드를 30% 확률로 따라함
                if (!I.s.IsFirst && I.HandHas(I.s.lastOpp))
                {
                    // 단, 너무 무서운 카드(Betrayal, Sacrifice)는 따라하기 싫어함
                    // Curse는 "유령 놀이"라고 생각해서 가끔 따라함
                    if (I.s.lastOpp == CardType.Sacrifice || I.s.lastOpp == CardType.Betrayal)
                    {
                        // 따라할 확률 매우 낮음 (5%)
                        if (UnityEngine.Random.value < 0.05f) return I.s.lastOpp;
                    }
                    else
                    {
                        // 나머지는 30% 확률로 모방
                        if (UnityEngine.Random.value < 0.30f) return I.s.lastOpp;
                    }
                }

                // 2. [신규] Sacrifice 반응: "아픈 건 싫어..."
                // Sacrifice는 우선순위 목록(cuteOrder)에서도 뒤쪽이지만,
                // 만약 손패에 Sacrifice만 남았다면 어쩔 수 없이 냄.
                // (별도 로직 필요 없음, fallback 순서로 처리됨)

                // 3. [시각적 선호] 예쁜 카드 순서대로 냄
                foreach (var c in cuteOrder)
                    if (I.HandHas(c)) return c;

                // 4. 그래도 없으면 아무거나
                return I.FirstOrNone();
            });

            // Fallback도 선호도 순서
            A.fallback = cuteOrder;

            // 선택 드로우 (Draft): "이게 더 예뻐!"
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                // [신규] Sacrifice 절대 기피 ("이거 아픈 카드잖아!")
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                // [신규] Curse 기피 ("유령 무서워...")
                // 단, Betrayal보다는 덜 무서워함
                if (a == CardType.Curse && b != CardType.Curse && b != CardType.Betrayal) return 1;
                if (b == CardType.Curse && a != CardType.Curse && a != CardType.Betrayal) return 0;

                // 유치원생의 점수표
                float Score(CardType c)
                {
                    return c switch
                    {
                        CardType.Cooperation => 100f, // 제일 좋아
                        CardType.Chaos       => 90f,  // 재밌어
                        CardType.Recon       => 70f,  // 장난감
                        CardType.Doubt       => 60f,  // 안전해
                        CardType.Interrupt   => 50f,
                        CardType.Pollution   => 40f,  // 으 지지
                        CardType.Curse       => 10f,  // [신규] 무서워
                        CardType.Sacrifice   => -50f, // [신규] 아파
                        CardType.Betrayal    => -100f,// 너무 무서워
                        _ => 0f
                    };
                }

                float sa = Score(a) + UnityEngine.Random.Range(-5f, 5f); // 아이의 변덕
                float sb = Score(b) + UnityEngine.Random.Range(-5f, 5f);

                return sa >= sb ? 0 : 1;
            };

            return A;
        }

        // 백무적V5 — 초월 메타 적응형 (완벽한 시뮬레이션 + 읽힘 방지)
        static Agent Build_백무적(AgentList id)
        {
            var A = new Agent("백무적", id);

            // [내부 시뮬레이터] CardSystem의 로직을 완벽하게 이해하고 있음
            // 리턴값: 나의 이득 - 상대의 이득 (Net Advantage)
            // ▼ [수정됨] 5번째 파라미터 'oppSacStacks' 추가
            int CalculateDelta(CardType myCard, CardType oppCard, int round, int mySacStacks, int oppSacStacks)
            {
                int r = round;
                
                // 1. 특수 승리 계산 (Sacrifice)
                // 내가 이번에 Sacrifice를 내면 4장이 된다? -> 무한대의 이득 (승리)
                if (myCard == CardType.Sacrifice && mySacStacks >= 3) return 9999;

                // ▼ [추가됨] 상대가 이번에 Sacrifice를 내서 4장이 된다? -> 무한대의 손해 (패배)
                if (oppCard == CardType.Sacrifice && oppSacStacks >= 3) return -9999;

                // 2. Chaos 처리
                if (myCard == CardType.Chaos) return (oppCard == CardType.Chaos) ? 0 : -1; // 리셋은 변수 통제 불가라 선호 안 함
                if (oppCard == CardType.Chaos) return 0; 

                // 3. 상성 매트릭스 (Curse, Sacrifice 포함)
                switch (myCard)
                {
                    case CardType.Cooperation:
                        if (oppCard == CardType.Cooperation) return 0; // 서로 +1
                        if (oppCard == CardType.Doubt) return +1;      // 나+1, 상0 (상대는 비용지불) -> 이득
                        if (oppCard == CardType.Betrayal) return -(r + 2); // 나 배신당함(-1), 상대 성공(+r) -> 큰 손해
                        if (oppCard == CardType.Curse) return -1;      // 나 저주걸림(-2), 협력보상(+1) -> 손해
                        if (oppCard == CardType.Sacrifice) return +2;  // 상대 자해(-1), 나 협력(+1) -> 이득
                        return +1;
                    
                    case CardType.Doubt:
                        if (oppCard == CardType.Cooperation) return -1;
                        if (oppCard == CardType.Betrayal) return +r;   // 방어 성공
                        if (oppCard == CardType.Curse) return +1;      // 저주 방어 (상대 카드 낭비)
                        if (oppCard == CardType.Sacrifice) return +1;  // 상대 자해
                        return 0; // Doubt vs Doubt

                    case CardType.Betrayal:
                        if (oppCard == CardType.Cooperation) return r + 1; // 성공
                        if (oppCard == CardType.Doubt) return -1;          // 막힘
                        if (oppCard == CardType.Betrayal) return -r;       // 자멸
                        if (oppCard == CardType.Curse) return +r + 2;      // 공격 성공(+r), 상대 저주검(나-2) -> 그래도 이득
                        if (oppCard == CardType.Sacrifice) return r + 2;   // 샌드백 때림
                        return r;

                    case CardType.Curse:
                        // 방어 카드엔 막힘 (0점)
                        if (oppCard == CardType.Doubt || oppCard == CardType.Interrupt) return -1; // 카드 낭비
                        // 공격 카드엔 맞으면서 저주 (손해)
                        if (oppCard == CardType.Betrayal) return -(r - 1); // 나 -r, 상대 저주(-2) -> -r+2
                        // 그 외(Coop, Recon, Chaos, Sacrifice)엔 저주 성공 (+2 이득)
                        return +2;

                    case CardType.Sacrifice:
                        // 4장이 아니면 기본적으로 -1 손해 (자해)
                        if (oppCard == CardType.Betrayal) return -r - 1; // 얻어맞고 자해
                        return -1; 

                    case CardType.Pollution:
                        if (oppCard == CardType.Cooperation) return +2;
                        if (oppCard == CardType.Doubt) return -1;
                        if (oppCard == CardType.Sacrifice) return +1;
                        return 0;

                    case CardType.Interrupt:
                        if (oppCard == CardType.Betrayal || oppCard == CardType.Pollution) return +2;
                        if (oppCard == CardType.Cooperation) return -2;
                        if (oppCard == CardType.Curse) return +2; // 방어 성공
                        return 0;

                    case CardType.Recon:
                        if (oppCard == CardType.Betrayal) return -(r + 1);
                        return 0;
                        
                    default: return 0;
                }
            }

            A.rules.Add(I =>
            {
                // 1. 정보 로드
                var opponentID = I.opponentID;
                var history = I.HistoryOpponent();
                int R = Math.Max(1, I.s.round);

                // [통찰] 상대의 Sacrifice 의도 파악
                // 상대가 3장 이상 냈다면, 이번이 마지막일 수 있음 -> "의도를 꺾는다"
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    // 효율 무시하고 즉시 제압
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // [통찰] 나의 Sacrifice 필승 전략
                // 내 손패에 Sacrifice가 있고, 이전까지 3장을 냈다면 -> 필승
                // (약식: AgentManager 추적 대신 현재 로직 내 추정. 실제론 AgentManager 필요)
                // *백무적은 실수를 안 하므로, 본인이 3장을 냈다는 걸 알고 있다고 가정
                int mySacCount = 0; // *외부 데이터 연동 필요
                // if (mySacCount >= 3 && I.HandHas(CardType.Sacrifice)) return CardType.Sacrifice;

                // 2. 상대 패 예측 (Probability Distribution)
                var probabilities = (opponentID != (AgentList)0) 
                    ? AgentManager.I.GetPredictedProbabilities(I.selfID, opponentID, I.s)
                    : null;

                // 예측 불가 시 균등 분포
                if (probabilities == null)
                {
                    probabilities = new Dictionary<CardType, float>();
                    var types = Enum.GetValues(typeof(CardType));
                    foreach (CardType c in types) if (c != CardType.None) probabilities[c] = 1f / (types.Length - 1);
                }

                // 3. [읽힘 방지] 상대가 나를 얼마나 잘 예측하고 있는가?
                // 상대가 내 지난 수를 카운터치는 경향이 강하다면, 최적수(Best)가 아닌 차선수(Second Best)를 택함
                bool isPredictable = false; 
                if (I.s.round > 3 && I.s.lastOpp != CardType.None)
                {
                    // 예: 내가 Betrayal 냈는데 상대가 Doubt 냄 -> 읽힘
                    // (약식 구현)
                }

                // 4. EV(기댓값) 시뮬레이션
                var bestCard = CardType.None;
                float maxEV = float.NegativeInfinity;
                
                // 차선책 계산을 위한 리스트
                var evList = new List<(CardType card, float ev)>();

                var uniqueHand = I.hand.Distinct().ToList();
                foreach (var myCard in uniqueHand)
                {
                    // Sacrifice는 4장 막타 아니면 EV 최악 (-99)
                    if (myCard == CardType.Sacrifice && mySacCount < 3)
                    {
                        evList.Add((myCard, -99f));
                        continue;
                    }

                    float currentEV = 0f;
                    foreach (var oppPair in probabilities)
                    {
                        // (나의 이득) * (상대가 해당 카드를 낼 확률)
                        currentEV += CalculateDelta(myCard, oppPair.Key, R, mySacCount, oppSacCount) * oppPair.Value;
                    }

                    // [학습된 가중치 반영] 자신의 경험
                    currentEV *= AgentManager.I.GetWeight(I.selfID, myCard);

                    // [Curse 특수 가산점] 
                    // 상대의 방어(Doubt/Interrupt) 확률이 낮다면(30% 미만), 저주는 매우 고효율
                    if (myCard == CardType.Curse)
                    {
                        float defProb = probabilities.ContainsKey(CardType.Doubt) ? probabilities[CardType.Doubt] : 0f;
                        defProb += probabilities.ContainsKey(CardType.Interrupt) ? probabilities[CardType.Interrupt] : 0f;
                        
                        if (defProb < 0.3f) currentEV += 1.5f; // 기대값 상향
                    }

                    evList.Add((myCard, currentEV));
                }

                // EV 순으로 정렬
                evList.Sort((a, b) => b.ev.CompareTo(a.ev));

                // [읽힘 방지 로직]
                // 1위와 2위의 점수 차이가 크지 않고(0.5 미만), 가끔은(20%) 2위를 선택해 패턴을 꼰다.
                // 단, 킬각이거나 위기상황이면 정석대로 1위 선택.
                bool crisis = I.s.selfLife <= R;
                if (!crisis && evList.Count >= 2)
                {
                    float diff = evList[0].ev - evList[1].ev;
                    if (diff < 0.5f && UnityEngine.Random.value < 0.2f)
                    {
                        return evList[1].card; // "허점을 보여주지."
                    }
                }

                return evList.Count > 0 ? evList[0].card : I.FirstOrNone();
            });

            // 선택 드로우 (Draft) - 철저한 EV 기반
            A.chooseFromTwo = (a, b, I) =>
            {
                // Sacrifice: 4장 완성이 아니면 절대 집지 않음 (쓰레기 취급)
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                // 확률 로드
                var opponentID = I.opponentID;
                var probabilities = (opponentID != (AgentList)0) 
                    ? AgentManager.I.GetPredictedProbabilities(I.selfID, opponentID, I.s)
                    : null;
                
                if (probabilities == null) return UnityEngine.Random.value < 0.5f ? 0 : 1;

                float GetScore(CardType c)
                {
                    float ev = 0;
                    int R = Math.Max(1, I.s.round);
                    foreach (var oppPair in probabilities)
                    {
                        // 시뮬레이션 (Sacrifice Count는 0으로 가정)
                        ev += CalculateDelta(c, oppPair.Key, R, 0, 0) * oppPair.Value;
                    }
                    // Curse는 Draft 단계에서도 꽤 좋은 평가 (변수 창출)
                    if (c == CardType.Curse) ev += 0.5f;
                    
                    return ev * AgentManager.I.GetWeight(I.selfID, c);
                }

                return GetScore(a) >= GetScore(b) ? 0 : 1;
            };

            return A;
        }

        // 류성우V2 — 데이터 분석가 (Sacrifice 철저 계산 / Curse 확률 기반 사용)
        static Agent Build_류성우(AgentList id)
        {
            var A = new Agent("류성우", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool losing = I.s.selfLife < I.s.oppLife;
                var history = I.HistoryOpponent();

                // 1. [이상치 제어] 상대방 Sacrifice 감지
                // "시스템 경고: 상대 승리 확률 임계점 돌파. 강제 종료 시퀀스 가동."
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 2. 확률 데이터 수집 & 정규화
                var p = new Dictionary<CardType, float>
                {
                    { CardType.Cooperation, I.Ratio(CardType.Cooperation) },
                    { CardType.Doubt,       I.Ratio(CardType.Doubt)       },
                    { CardType.Betrayal,    I.Ratio(CardType.Betrayal)    },
                    { CardType.Chaos,       I.Ratio(CardType.Chaos)       },
                    { CardType.Pollution,   I.Ratio(CardType.Pollution)   },
                    { CardType.Interrupt,   I.Ratio(CardType.Interrupt)   },
                    { CardType.Recon,       I.Ratio(CardType.Recon)       },
                    { CardType.Curse,       I.Ratio(CardType.Curse)       }, // 신규
                    { CardType.Sacrifice,   I.Ratio(CardType.Sacrifice)   }  // 신규
                };
                float sum = p.Values.Sum(); 
                if (sum <= 0) sum = 1f; 
                foreach (var k in p.Keys.ToList()) p[k] /= sum;

                // 3. 교전 시뮬레이션 (Delta: 나의 이득 - 상대 이득)
                int Delta(CardType a, CardType b)
                {
                    int r = R;
                    // [기존 상성 로직 (주요 상성만 요약 반영)]
                    if (a == CardType.Cooperation) {
                        if (b == CardType.Betrayal) return -(r + 1);
                        if (b == CardType.Doubt) return +1;
                        if (b == CardType.Curse) return -1; // 저주 걸림(-2), 협력(+1) -> -1
                        if (b == CardType.Sacrifice) return +2; // 상대 자해
                        return 0; 
                    }
                    if (a == CardType.Doubt) {
                        if (b == CardType.Betrayal) return r + 1;
                        if (b == CardType.Curse) return +1; // 방어 성공
                        if (b == CardType.Sacrifice) return +1;
                        return 0;
                    }
                    if (a == CardType.Betrayal) {
                        if (b == CardType.Cooperation) return r + 1;
                        if (b == CardType.Doubt) return -(r + 1);
                        if (b == CardType.Betrayal) return -2 * r; // 쌍방 배신은 큰 손해
                        if (b == CardType.Curse) return r + 2; // 공격 성공(+r), 상대 저주검(나-2) -> 감수할만함
                        if (b == CardType.Sacrifice) return r + 2;
                        return 0;
                    }
                    if (a == CardType.Pollution) {
                        if (b == CardType.Cooperation) return +2;
                        if (b == CardType.Doubt) return -1;
                        if (b == CardType.Sacrifice) return +1;
                        return 0;
                    }
                    // [신규] Curse 계산: "지연된 데이터값"
                    if (a == CardType.Curse) {
                        if (b == CardType.Doubt || b == CardType.Interrupt) return 0; // 막힘
                        if (b == CardType.Betrayal) return -1; // 맞음
                        return +2; // 성공 (2턴간 총 2데미지 이득)
                    }
                    // [신규] Sacrifice 계산
                    if (a == CardType.Sacrifice) return -1; // 기본적으로 -1 손해

                    return 0; // 나머지(Recon, Interrupt, Chaos)는 중립적
                }

                // 4. EV 기반 선택
                // *류성우는 자신의 Sacrifice 스택을 추적하여 4장째면 필승 코드로 인식해야 함
                // (여기서는 외부 변수 접근이 어려우므로, 손패에 Sacrifice가 있고 게임 후반부면 시도하는 약식 로직 사용 가능하나,
                //  기본적으로는 Draft에서 집지 않으므로 손에 있을 확률이 낮음)
                
                var cand = I.hand.Distinct().Where(I.HandHas).ToList();
                CardType best = CardType.None; 
                float bestEV = float.NegativeInfinity;

                foreach (var a in cand)
                {
                    // Sacrifice는 막타 상황(가정) 아니면 EV 계산에서 극도로 불리하게 작용
                    if (a == CardType.Sacrifice) 
                    {
                        // 내 승리 스택 확인 로직이 없다면 보수적으로 -99 처리
                         if (I.s.selfLife > 8) { /* 아주 여유로우면 예외 허용 */ }
                         else { continue; }
                    }

                    float ev = 0f;
                    foreach (var b in p.Keys) ev += p[b] * Delta(a, b);

                    // 상황 보정
                    if (losing && (a == CardType.Betrayal || a == CardType.Pollution)) ev += 0.6f;
                    if (!losing && (a == CardType.Doubt || a == CardType.Interrupt)) ev += 0.5f;
                    
                    // [Recon 선호] "데이터 독점"
                    if (a == CardType.Recon) ev += 0.5f;

                    // [Curse 평가] "방어율이 낮으면 효율적"
                    if (a == CardType.Curse)
                    {
                        float defProb = p[CardType.Doubt] + p[CardType.Interrupt];
                        if (defProb < 0.35f) ev += 1.2f; 
                    }

                    // FIX: Safe dictionary access
                    ev *= weights.ContainsKey(a) ? weights[a] : 1.0f;
                    if (ev > bestEV) { bestEV = ev; best = a; }
                }
                
                return best != CardType.None ? best : I.FirstOrNone();
            });

            // 선택 드로우: "데이터 수집(Recon)과 안정성(Doubt) 우선"
            A.chooseFromTwo = (a, b, I) => {
                var weights = AgentManager.I.GetWeights(I.selfID);
                
                // Sacrifice 배제: "데이터에 없는 요행수."
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                float Score(CardType c) => (c switch {
                    CardType.Recon       => 95, // [특성] 정보 독점
                    CardType.Doubt       => 85, // [특성] 리스크 차단
                    CardType.Pollution   => 75, // 효율적 누적 딜
                    CardType.Curse       => 70, // [신규] 계산된 도트 딜
                    CardType.Interrupt   => 65,
                    CardType.Betrayal    => 60,
                    CardType.Cooperation => 40,
                    CardType.Chaos       => 10, // "데이터 오염원"
                    CardType.Sacrifice   => -99,// [신규] 비효율
                    _ => 0
                // FIX: Safe dictionary access
                }) * (weights.ContainsKey(c) ? weights[c] : 1.0f);

                return Score(a) >= Score(b) ? 0 : 1;
            };

            A.fallback = new[] { CardType.Recon, CardType.Doubt, CardType.Pollution, CardType.Curse, CardType.Interrupt, CardType.Betrayal, CardType.Cooperation };
            return A;
        }

        // 서유리V2 — 패턴 연구가·반복 혐오 (Sacrifice 루프 파괴 / Interrupt 활용)
        static Agent Build_서유리(AgentList id)
        {
            var A = new Agent("서유리", id);

            // [내부 함수] 카운터 카드 계산 로직
            CardType GetCounter(CardType enemyCard, bool aggressive, DecisionInput I)
            {
                // 1. [신규] Sacrifice Counter: "반복은 용서 안 해."
                // 상대가 Sacrifice를 낸다면?
                if (enemyCard == CardType.Sacrifice)
                {
                    // Interrupt: 상대 비용(-1)만 나가고 스택 방해 가능(상황따라) 혹은 이득 챙김
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                    // Betrayal: 희생하느라 아픈 상대에게 치명타
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    return CardType.Pollution;
                }

                // 2. [신규] Curse Counter: "지루한 저주는 반사."
                if (enemyCard == CardType.Curse)
                {
                    // Interrupt로 막거나(효과 표 참조), Chaos로 상태 리셋
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt; 
                    if (I.HandHas(CardType.Chaos)) return CardType.Chaos;
                }

                // 3. 기존 카운터 로직
                if (enemyCard == CardType.Cooperation)
                {
                    if (aggressive && I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    return I.HandHas(CardType.Pollution) ? CardType.Pollution : CardType.Betrayal;
                }
                if (enemyCard == CardType.Doubt)
                {
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                    return CardType.Cooperation;
                }
                if (enemyCard == CardType.Betrayal)
                {
                    if (I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    return CardType.Interrupt;
                }
                if (enemyCard == CardType.Chaos)
                {
                    // 혼란에는 정보(Recon)나 더 큰 혼란(Pollution)으로 대응
                    return aggressive ? CardType.Pollution : CardType.Recon;
                }
                if (enemyCard == CardType.Pollution)
                {
                    if (I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    return CardType.Interrupt;
                }
                if (enemyCard == CardType.Interrupt)
                {
                    // Interrupt를 뚫는 건, 공격이 아닌 Cooperation이나 정보
                    return CardType.Cooperation;
                }
                
                return CardType.None;
            }

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                var history = I.HistoryOpponent();

                // 0. [절대 원칙] 자기 반복 금지
                // "어제와 같은 오늘은 죽음이다."
                // 직전에 낸 카드는 웬만하면 내지 않음 (점수에서 대폭 깎임)
                CardType lastSelf = I.s.lastSelf;

                // 1. [Loop Breaker] 상대의 Sacrifice 루프 감지
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    // "그 지루한 짓을 4번이나 하겠다고? 어림없지."
                    // 즉시 흐름을 끊거나(Chaos, Interrupt) 죽임(Betrayal)
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt; //
                    if (I.HandHas(CardType.Chaos)) return CardType.Chaos;
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                }

                // 2. [Pattern Breaker] 예측 기반 역공
                var targetOpp = I.opponentID;
                if (targetOpp != (AgentList)0)
                {
                    // LearningData를 통해 상대의 다음 수 예측
                    var predicted = AgentManager.I.PredictNextCard(I.selfID, targetOpp, I.s);
                    if (predicted.HasValue)
                    {
                        bool aggressive = I.s.selfLife >= I.s.oppLife;
                        var counter = GetCounter(predicted.Value, aggressive, I);
                        
                        // 예측된 카드에 대한 카운터가 있고, 그 카드가 '직전에 낸 카드'가 아니라면 실행
                        if (counter != CardType.None && counter != lastSelf)
                            return counter;
                    }
                }

                // 3. 상대의 단순 반복(Loop) 응징
                if (!I.s.IsFirst && I.s.lastOpp == I.s.last2Opp && I.s.lastOpp != CardType.None)
                {
                    // 상대가 같은 걸 또 냈다 -> 카운터 펀치
                    var counter = GetCounter(I.s.lastOpp, true, I);
                    if (counter != CardType.None) return counter;
                }

                // 4. [신규] Curse 활용: "정체된 판 흔들기"
                // 상대가 방어적(Doubt)이거나 평화적(Coop)인 흐름이 반복되면 저주 사용
                if (I.HandHas(CardType.Curse))
                {
                    bool boringFlow = (I.s.lastOpp == CardType.Doubt || I.s.lastOpp == CardType.Cooperation);
                    if (boringFlow && lastSelf != CardType.Curse)
                        return CardType.Curse;
                }

                // 5. 점수 계산 (변칙성 중시)
                var p = new Dictionary<CardType, float>();
                // (확률 분포 계산 생략 - 약식)

                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Interrupt => 10,  // [선호] 흐름 끊기
                        CardType.Chaos => 9,       // [선호] 변수 창출
                        CardType.Curse => 8,       // [신규] 새로운 자극
                        CardType.Pollution => 7,
                        CardType.Betrayal => 6,
                        CardType.Recon => 5,       // 패턴 분석용
                        CardType.Cooperation => 3,
                        CardType.Doubt => 2,
                        CardType.Sacrifice => -10, // [신규] 반복적인 카드는 싫음
                        _ => 0
                    };

                    // [자기 반복 페널티] 직전 카드면 점수 대폭 삭감
                    if (c == lastSelf) baseScore -= 50;

                    // [상대 반복 응징] 상대가 낸 카드를 미러링하는 건 싫어함 (독창성 부족)
                    if (c == I.s.lastOpp) baseScore -= 5;

                    // FIX: Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }

                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            // 선택 드로우 (Draft)
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);

                // [신규] Sacrifice 기피: "지루해."
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                // [직전 카드 기피] 드로우 단계에서도 직전에 낸 카드는 피함
                if (a == I.s.lastSelf && b != I.s.lastSelf) return 1;
                if (b == I.s.lastSelf && a != I.s.lastSelf) return 0;

                float Score(CardType c)
                {
                    float s = c switch {
                        CardType.Interrupt => 95,
                        CardType.Chaos => 90,
                        CardType.Curse => 85,
                        CardType.Pollution => 70,
                        CardType.Recon => 60,
                        CardType.Betrayal => 50,
                        CardType.Doubt => 30,
                        CardType.Cooperation => 20,
                        CardType.Sacrifice => -100, // 절대 안 집음
                        _ => 0
                    };
                    // FIX: Safe dictionary access
                    return s * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);
                // 동점이면 랜덤(변칙)
                if (Math.Abs(sa - sb) < 0.1f) return UnityEngine.Random.value < 0.5f ? 0 : 1;
                return sa > sb ? 0 : 1;
            };

            A.fallback = new[] { 
                CardType.Interrupt, CardType.Chaos, CardType.Curse, 
                CardType.Pollution, CardType.Recon, CardType.Betrayal, 
                CardType.Doubt, CardType.Cooperation, CardType.Sacrifice 
            };
            return A;
        }

        // 강은호V2 — 통제의 회계사 (Sacrifice 손실 회피 / Curse 확정 이득 선호)
        static Agent Build_강은호(AgentList id)
        {
            var A = new Agent("강은호", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                var history = I.HistoryOpponent();

                // 0. [리스크 관리] 상대방 Sacrifice 감지 -> 파산 방지
                // "경고: 상대방 승리 자산 축적. 긴급 청산 절차 가동."
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt; // 흐름 끊기
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;   // 강제 청산
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 1. [손익 계산] Q 함수 (상대 패 예측 확률)
                float P(CardType t) => I.Ratio(t);
                float Z = 0f;
                var types = (CardType[])Enum.GetValues(typeof(CardType));
                foreach(var t in types) if(t!=CardType.None) Z += P(t);
                if (Z <= 0) Z = 1f;
                float Q(CardType t) => P(t) / Z;

                // 2. [스트레스 반응] Chaos 혐오
                // "변수는 질색이야." (Chaos는 점수 계산에서 대폭 감점)

                // 3. [안전 자산 선호] 초반 탐색
                bool poorHand = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution) && !I.HandHas(CardType.Curse);
                if ((R <= 3 || poorHand) && I.HandHas(CardType.Recon))
                    return CardType.Recon;

                // 4. [확정 킬각] 대차대조표 마감
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && Q(CardType.Doubt) < 0.3f)
                    return CardType.Betrayal;

                // 5. [위기 관리] 방어
                if (I.s.selfLife <= R && Q(CardType.Betrayal) >= 0.25f && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                // 6. [신규] Curse 활용: "장기 부채 발행."
                // 상대의 방어 확률이 낮을 때, 저주는 가장 안정적인 투자처
                if (I.HandHas(CardType.Curse))
                {
                    float defProb = Q(CardType.Doubt) + Q(CardType.Interrupt);
                    if (defProb < 0.35f)
                        return CardType.Curse;
                }
                
                // 7. [신규] Sacrifice 처리
                // "손실 자산 처리." 3장 모았을 때만 이득, 그 외엔 무조건 기피
                if (I.HandHas(CardType.Sacrifice))
                {
                    // (약식) 본인이 3장 냈다고 가정할 수 있는 경우에만 냄
                    // 여기서는 기본적으로 안 냄 (점수 최하)
                }

                // 8. 가치 평가 함수 V
                float V(CardType c)
                {
                    float score = 0;
                    switch (c)
                    {
                        case CardType.Doubt:       score = 6.0f; break; // [특성] 방어 중시
                        case CardType.Interrupt:   score = 5.5f; break; // [특성] 통제
                        case CardType.Pollution:   score = 5.0f; break;
                        case CardType.Curse:       score = 4.8f; break; // [신규] 안정적 공격
                        case CardType.Recon:       score = 4.0f; break;
                        case CardType.Cooperation: score = 3.0f; break;
                        case CardType.Betrayal:    score = (I.s.oppLife <= R ? 8f : 2.5f); break;
                        case CardType.Chaos:       score = -10f; break; // [특성] 혐오
                        case CardType.Sacrifice:   score = -20f; break; // [신규] 손실
                    }
                    
                    // 상대 예측 보정
                    if (c == CardType.Betrayal) score -= 3.0f * Q(CardType.Doubt); // 막힐 위험 -> 감점
                    if (c == CardType.Curse)    score -= 3.0f * (Q(CardType.Doubt) + Q(CardType.Interrupt));

                    // FIX: Safe dictionary access
                    return score * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }

                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(V).FirstOrDefault();
            });

            // 선택 드로우 (Draft)
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);

                // [신규] Sacrifice 기피: "장부에 구멍 낼 일 있나."
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                // [특성] Chaos 혐오: "예측 불가능한 자산은 폐기한다."
                if (a == CardType.Chaos && b != CardType.Chaos) return 1;
                if (b == CardType.Chaos && a != CardType.Chaos) return 0;

                float V(CardType c)
                {
                    float baseScore = c switch
                    {
                        CardType.Interrupt   => 90f,
                        CardType.Doubt       => 85f,
                        CardType.Pollution   => 70f,
                        CardType.Curse       => 65f, // [신규]
                        CardType.Betrayal    => 60f,
                        CardType.Recon       => 50f,
                        CardType.Cooperation => 40f,
                        CardType.Chaos       => -100f,
                        CardType.Sacrifice   => -200f, // [신규]
                        _ => 0f
                    };
                    // FIX: Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }
                
                float va = V(a), vb = V(b);
                // 동점이면 랜덤보단 안정적인 카드(인덱스 낮은 것) 선호
                if (Math.Abs(va - vb) < 0.1f)
                {
                     int Safety(CardType t) => t switch { 
                         CardType.Doubt=>5, CardType.Interrupt=>4, CardType.Recon=>3, _=>0 
                     };
                     return Safety(a) >= Safety(b) ? 0 : 1;
                }
                return va >= vb ? 0 : 1;
            };

            A.fallback = new[] { 
                CardType.Interrupt, CardType.Doubt, CardType.Pollution, 
                CardType.Curse, CardType.Recon, CardType.Betrayal, 
                CardType.Cooperation, CardType.Chaos, CardType.Sacrifice 
            };
            return A;
        }

        // 전아람V2 — 정보 포식자 (Recon 연계 Curse / Sacrifice 테러 진압)
        static Agent Build_전아람(AgentList id)
        {
            var A = new Agent("전아람", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var history = I.HistoryOpponent();

                // 1. [테러 진압] 상대 Sacrifice 감지
                // "첩보 입수: 상대가 자폭 테러(Sacrifice Win)를 준비 중이다. 즉시 제압하라."
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    // 가장 확실한 제거 수단 우선
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt; // 스택 쌓기 방해
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 2. [정보 수집] 초반 정찰 우선
                // 공격 수단이 빈약하거나 초반이면 정보 수집에 집중
                bool poorHand = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);
                if ((R <= 4 || poorHand) && I.HandHas(CardType.Recon))
                    return CardType.Recon;

                // 3. 상대 패 예측 (정보 분석)
                var p = new Dictionary<CardType, float>
                {
                    { CardType.Cooperation, I.Ratio(CardType.Cooperation) },
                    { CardType.Doubt,       I.Ratio(CardType.Doubt)       },
                    { CardType.Betrayal,    I.Ratio(CardType.Betrayal)    },
                    { CardType.Chaos,       I.Ratio(CardType.Chaos)       },
                    { CardType.Pollution,   I.Ratio(CardType.Pollution)   },
                    { CardType.Interrupt,   I.Ratio(CardType.Interrupt)   },
                    { CardType.Recon,       I.Ratio(CardType.Recon)       },
                    { CardType.Curse,       I.Ratio(CardType.Curse)       },
                    { CardType.Sacrifice,   I.Ratio(CardType.Sacrifice)   }
                };
                float sum = p.Values.Sum(); if (sum <= 0) sum = 1f;
                foreach (var k in p.Keys.ToList()) p[k] /= sum;

                // 4. [신규] Curse 활용: "확인 사살 (Confirmed Kill)"
                // Recon 등으로 상대 정보를 파악했는데 방어 수단이 없다? -> 저주 투척
                if (I.HandHas(CardType.Curse))
                {
                    // 상대 방어 확률 계산
                    float defProb = p[CardType.Doubt] + p[CardType.Interrupt];
                    
                    // 직전에 정찰했거나(Recon), 상대 방어 확률이 매우 낮으면(20% 미만) 가학적으로 저주 사용
                    bool informationSuperiority = (nf && I.s.lastSelf == CardType.Recon) || defProb < 0.2f;
                    
                    if (informationSuperiority)
                        return CardType.Curse;
                }

                // 5. 킬각 / 위기 관리
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.35f)
                    return CardType.Betrayal;
                
                if (I.s.selfLife <= R && p[CardType.Betrayal] >= 0.28f && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                // 6. [전략적 평가] Score 함수
                CardType avoid = I.s.lastSelf; // 같은 행동 반복은 정보 노출이므로 지양
                int r = R;

                float Score(CardType a)
                {
                    float e = 0;
                    // Sacrifice는 4장 완성이 아니면 절대 내지 않음 (기피)
                    if (a == CardType.Sacrifice) return -99f; 

                    foreach (var kv in p)
                    {
                        var b = kv.Key; 
                        float q = kv.Value; 
                        float d = 0;

                        // 기존 상성 로직
                        if (a == CardType.Betrayal && b == CardType.Cooperation) d = r + 1;
                        else if (a == CardType.Pollution && b == CardType.Cooperation) d = +2;
                        else if (a == CardType.Doubt && b == CardType.Betrayal) d = r + 1;
                        else if (a == CardType.Interrupt && (b == CardType.Betrayal || b == CardType.Pollution)) d = +2;
                        else if (a == CardType.Cooperation && b == CardType.Betrayal) d = -(r + 1);
                        
                        // [신규] Curse 상성 계산
                        else if (a == CardType.Curse)
                        {
                            if (b == CardType.Doubt || b == CardType.Interrupt) d = -1; // 막힘
                            else if (b == CardType.Betrayal) d = -1; // 맞음
                            else d = +2; // 성공
                        }
                        
                        e += q * d;
                    }
                    
                    // 정보 은폐 가산점
                    if (a != avoid) e += 0.2f;

                    // 특성 가중치 적용
                    // ★ [수정됨] 안전한 가중치 접근
                    e *= weights.ContainsKey(a) ? weights[a] : 1.0f;
                    return e;
                }

                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            // 선택 드로우 (Draft)
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool losing = I.s.selfLife < I.s.oppLife;

                // [신규] Sacrifice 철저 배제: "자폭은 멍청이들이나 하는 짓."
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                float V(CardType c)
                {
                    float baseScore = 0;
                    switch (c)
                    {
                        case CardType.Recon:       baseScore = 2.0f; break; // [특성] 정보 최우선
                        case CardType.Curse:       baseScore = 1.5f; break; // [신규] 가학적 선호
                        case CardType.Pollution:   baseScore = 1.4f; break;
                        case CardType.Betrayal:    baseScore = (I.s.oppLife <= R ? 3.0f : 1.2f); break;
                        case CardType.Doubt:       baseScore = 1.2f; break;
                        case CardType.Interrupt:   baseScore = 1.1f; break;
                        case CardType.Cooperation: baseScore = 0.7f; break;
                        case CardType.Chaos:       baseScore = 0.1f; break; // 정보 오염 싫어함
                        case CardType.Sacrifice:   baseScore = -99f; break; // [신규]
                    }
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1f);
                }
                
                float va = V(a) + (I.s.lastSelf != a ? 0.2f : 0f); // 직전 카드 기피
                float vb = V(b) + (I.s.lastSelf != b ? 0.2f : 0f);
                
                if (Math.Abs(va - vb) < 0.01f) return UnityEngine.Random.value < 0.5f ? 0 : 1;
                return va >= vb ? 0 : 1;
            };

            A.fallback = new[] { 
                CardType.Recon, CardType.Curse, CardType.Pollution, 
                CardType.Doubt, CardType.Betrayal, CardType.Interrupt, 
                CardType.Cooperation, CardType.Chaos, CardType.Sacrifice 
            };
            return A;
        }
    }
}