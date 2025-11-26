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
        
        // Kim Hyun-su v4 — The Shield: Prudent Analyst (Data-driven Defense & Late-game Investment Monopoly)
        static Agent Build_김현수(AgentList id)
        {
            var A = new Agent("김현수", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var history = I.HistoryOpponent(); 

                // [Data Analysis]
                float oppBetrayalProb = I.Ratio(CardType.Betrayal);
                float oppCurseProb = I.Ratio(CardType.Curse);
                bool isOpponentAggressive = (oppBetrayalProb + oppCurseProb) > 0.35f;
                
                // [The Shield] Crisis Detection System
                // Activate defense if health is critical (<= 3) or attack probability is high
                bool isEmergency = I.s.selfLife <= 3;
                bool expectAttack = isOpponentAggressive || (nf && (I.s.lastOpp == CardType.Betrayal || I.s.lastOpp == CardType.Curse));

                // 1. [Survival First] Ironclad Defense
                // "Defeat comes from a single mistake." -> Eliminate risk completely
                if (isEmergency || expectAttack)
                {
                    if (I.HandHas(CardType.Doubt)) return CardType.Doubt;       // Guaranteed defense
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt; // Break the flow
                }

                // 2. [Data Collection] Utilize Recon
                // "Information superiority is survival." -> Scout early or when situation is unclear
                if (I.HandHas(CardType.Recon))
                {
                    // Scout if opponent's hand is largely unknown or next move is uncertain
                    if (R <= 4 || I.unseenTotal > 15) 
                        return CardType.Recon;
                }

                // 3. [NEW] Utilize Investment: "Yield Calculation Complete."
                // Kim Hyun-su views early uncertain investments as 'gambling' and avoids them.
                // However, as the game progresses (R >= 5) and stacks build, he classifies it as a 'Guaranteed Asset' and retrieves it.
                if (I.HandHas(CardType.Investment))
                {
                    // Invest if it's late game (Round 5+) and not an immediate emergency
                    if (R >= 5 && !isEmergency)
                    {
                        // Realize investment profit safely if opponent is likely defensive (not attacking)
                        if (I.Ratio(CardType.Doubt) > 0.3f || I.Ratio(CardType.Cooperation) > 0.3f)
                            return CardType.Investment;
                    }
                }

                // 4. [Cynical Check] Curse / Pollution
                // "Emotionless attrition." Dry them out when opponent is in turtle mode
                if (!isEmergency && !expectAttack)
                {
                    // Use Curse for DoT if opponent is defensive
                    if (I.HandHas(CardType.Curse) && I.Ratio(CardType.Doubt) > 0.25f)
                        return CardType.Curse;
                    
                    // Use Pollution to induce resource consumption
                    if (I.HandHas(CardType.Pollution))
                        return CardType.Pollution;
                }

                // 5. [Lethal Calculation] Perfect Calculation
                // Be bold only when opponent is gathering Sacrifice or can be finished with an attack
                int oppSacCount = history.Count(x => x == CardType.Sacrifice);
                if (oppSacCount >= 3 || (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R))
                {
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                }

                // 6. Calculate Weighted Score (Safe Access Applied)
                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Doubt => 12,       // [Core] The Shield: Defense First
                        CardType.Recon => 10,       // [Core] Value Information
                        CardType.Interrupt => 9,    // Block Variables
                        CardType.Investment => (R >= 5) ? 11 : 2, // [NEW] Tier 1 in late game, trash in early game
                        CardType.Curse => 8,        // Cynical Attack
                        CardType.Cooperation => 7,  // Calculated Cooperation
                        CardType.Pollution => 6,
                        CardType.Betrayal => 5,     // Risky attacks are not preferred
                        CardType.Chaos => -10,      // [Trait] Hates unpredictable Chaos
                        CardType.Sacrifice => -20,  // [Trait] Self-harm is considered a 'mistake'
                        _ => 0
                    };
                    // ★ [FIX] Safe dictionary access (Prevent KeyNotFound)
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }
                
                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            A.fallback = new[] {
                CardType.Doubt, CardType.Recon, CardType.Interrupt, // Defense/Info Line
                CardType.Investment, // Use if situation fits
                CardType.Curse, CardType.Cooperation, 
                CardType.Pollution, CardType.Betrayal, 
                CardType.Chaos, CardType.Sacrifice
            };

            // --- Selective Draw (Draft) ---
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);

                // [NEW] Investment: Never pick early (hand waste), value skyrockets after Round 6
                bool highYield = R >= 6;

                // [Existing] Sacrifice: Never pick
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1; 
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0; 

                float Score(CardType t)
                {
                    float baseScore = t switch
                    {
                        CardType.Doubt       => 120, // Always welcome defense cards
                        CardType.Recon       => 110, // The more info, the better
                        CardType.Investment  => highYield ? 115 : 10, // [NEW] Thorough valuation by timing
                        CardType.Interrupt   => 100,
                        CardType.Curse       => 90,
                        CardType.Cooperation => 80,
                        CardType.Pollution   => 70,
                        CardType.Betrayal    => 60,
                        CardType.Chaos       => -50, // Hate unpredictability
                        CardType.Sacrifice   => -100,
                        _ => 0
                    };
                    // ★ [FIX] Safe dictionary access
                    return baseScore * (weights.ContainsKey(t) ? weights[t] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);
                
                // If scores are similar, pick the 'safer' card (lower index = defensive card)
                if (Math.Abs(sa - sb) < 0.1f)
                {
                    int SafetyRank(CardType t) => t switch { 
                        CardType.Doubt => 10, CardType.Recon => 9, CardType.Interrupt => 8, _ => 0 
                    };
                    return SafetyRank(a) >= SafetyRank(b) ? 0 : 1;
                }

                return sa > sb ? 0 : 1;
            };

            return A;
        }

        // Lee Su-jin v3 — High Roller: Adventurous Improviser (Jackpot Seeking & Variable Creation)
        static Agent Build_이수진(AgentList id)
        {
            var A = new Agent("이수진", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                // [High Roller] Situation Analysis
                // She values 'gut feeling' and 'flow' over calculation.
                bool feelingLucky = UnityEngine.Random.value < 0.4f; // 40% chance to just feel lucky
                bool losing = I.s.selfLife < I.s.oppLife; // Becomes bolder when losing
                
                // [Existing] Sacrifice "Fatalism"
                // "I'll end this now." If she has 3 Sacrifices (estimated), she throws it without hesitation.
                int unseenSac = I.unseen.TryGetValue(CardType.Sacrifice, out int v) ? v : 0;
                int handSac = I.hand.Count(c => c == CardType.Sacrifice);
                int myPlayedSacrifice = Math.Max(0, 4 - unseenSac - handSac); // (Simplified estimation)

                if (myPlayedSacrifice >= 3 && I.HandHas(CardType.Sacrifice))
                    return CardType.Sacrifice;

                // 3. [NEW] Utilize Investment: "Let's raise the stakes?"
                // To her, investment isn't preparation for the future, it's a 'bet' on the present.
                // Uses it when she has plenty of health (>= 6) OR when she's desperate (<= 2) and needs a "big hit".
                if (I.HandHas(CardType.Investment))
                {
                    bool rich = I.s.selfLife >= 6;
                    bool desperate = I.s.selfLife <= 2;
                    
                    // Throw it for fun when rich, or aiming for a jackpot when desperate
                    if (rich || desperate) return CardType.Investment;
                }

                // [Aggressive Instinct]
                // "Defense? What's that?" If she has an attack card, she likely uses it.
                if (I.HandHas(CardType.Betrayal))
                {
                    // Uses it if lethal or just feeling lucky
                    if (I.s.oppLife <= R || feelingLucky) return CardType.Betrayal;
                }

                // [Variable Creation] Chaos
                // "Flip the table!" Reset if losing or hand is bad.
                if (I.HandHas(CardType.Chaos))
                {
                    bool badHand = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Sacrifice);
                    if (losing || badHand) return CardType.Chaos;
                }

                // Calculate Weighted Score (Safe Access Applied)
                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Sacrifice => 15,   // [Core] Loves High Risk High Return
                        CardType.Betrayal => 12,    // [Trait] Aggressive
                        CardType.Chaos => 10,       // [Trait] Creates variables
                        CardType.Curse => 8,        // Fun to torment
                        CardType.Pollution => 7,
                        CardType.Interrupt => 6,    // Uses if she feels like it
                        CardType.Investment => 5,   // [NEW] Boring too
                        CardType.Cooperation => 3,  // Boring
                        CardType.Recon => 1,        // Hates calculation
                        CardType.Doubt => -5,       // [Trait] No shields in a man's fight (Avoids)
                        _ => 0
                    };

                    // [High Roller] Score boost for Sacrifice/Chaos when losing
                    if (losing)
                    {
                        if (c == CardType.Sacrifice) baseScore += 10;
                        if (c == CardType.Chaos) baseScore += 5;
                    }

                    // ★ [FIX] Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }
                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            A.fallback = new[] {
                CardType.Sacrifice, CardType.Betrayal, CardType.Chaos, // Adventure Line
                CardType.Curse, 
                CardType.Pollution, CardType.Interrupt, CardType.Investment, 
                CardType.Cooperation, CardType.Recon, CardType.Doubt
            };
            
            // --- Selective Draw (Draft) ---
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool losing = I.s.selfLife < I.s.oppLife;

                // [NEW] Investment: "Bury it for later."
                // Shows interest if seen in the deck.
                
                // [Existing] Sacrifice: "This is MY card!"
                // Tries to pick it up if she already has one (Desire to complete set)
                bool hasSac = I.HandHas(CardType.Sacrifice);
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return hasSac ? 0 : (UnityEngine.Random.value < 0.6f ? 0 : 1); 
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return hasSac ? 1 : (UnityEngine.Random.value < 0.6f ? 1 : 0);

                float Score(CardType t)
                {
                    float baseScore = t switch
                    {
                        CardType.Sacrifice   => 120, // Obsession level
                        CardType.Betrayal    => 100, // Attack
                        CardType.Chaos       => 95,  // Chaos
                        CardType.Curse       => 80,
                        CardType.Pollution   => 70,
                        CardType.Interrupt   => 60,
                        CardType.Investment  => 50,  // [NEW] Moderate interest
                        CardType.Cooperation => 40,
                        CardType.Recon       => 20,
                        CardType.Doubt       => 10,  // Rarely picks defense
                        _ => 0
                    };
                    
                    // Prefer reversal cards if losing
                    if (losing && (t == CardType.Sacrifice || t == CardType.Chaos))
                        baseScore += 50;

                    // ★ [FIX] Safe dictionary access
                    return baseScore * (weights.ContainsKey(t) ? weights[t] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);

                // If scores are similar, choose randomly (High Roller's whim)
                if (Math.Abs(sa - sb) < 15f)
                    return UnityEngine.Random.value < 0.5f ? 0 : 1;

                return sa > sb ? 0 : 1;
            };
            return A;
        }

        // Choi Yong-ho v3 — The Rusher: Short-term Decisive Battle (Investment Aversion & All-in Attack)
        static Agent Build_최용호(AgentList id)
        {
            var A = new Agent("최용호", id);

            A.rules.Clear();
            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;

                // 0) [Trait] Sacrifice: "Full production line!"
                // Throw it without hesitation.
                if (I.HandHas(CardType.Sacrifice))
                    return CardType.Sacrifice;

                // 1) Guaranteed Lethal (Ignore weights)
                if (I.HandHas(CardType.Betrayal)  && I.s.oppLife <= R) return CardType.Betrayal;

                // 2) Early Rush (Round 1~3) – "First strike wins!"
                if (R <= 3)
                {
                    if (I.HandHas(CardType.Betrayal))  return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    // If no attack means, use Chaos to shuffle
                    int atk = (I.HandHas(CardType.Betrayal)?1:0) + (I.HandHas(CardType.Pollution)?1:0);
                    if (atk==0 && I.HandHas(CardType.Chaos)) return CardType.Chaos;
                }
                
                // 3) [The Rusher] Attack Instinct
                // High probability to attack regardless of health
                if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                if (I.HandHas(CardType.Pollution)) return CardType.Pollution;

                // 4) [NEW] Investment Handling: "No time to grow this trash."
                // If he has Investment, he treats it as a waste of a turn.
                // He will prioritize ANY other card over Investment, unless it's the only option.
                // No specific logic needed to 'use' it preferentially, it naturally falls to the bottom.

                // 5) Simple Counters
                if (nf && I.s.lastOpp == CardType.Betrayal && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                if (nf && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt))    return CardType.Doubt;

                // 6) Curse Handling: "Too slow."
                // Not preferred as it's not immediate damage.

                // 7) Fixed Priority (Sacrifice handled at top)
                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Sacrifice => 15,  // [Core] Always throw if available
                        CardType.Betrayal => 10,   // Attack Priority 1
                        CardType.Pollution => 9,   // Attack Priority 2
                        CardType.Interrupt => 7,   // Counter
                        CardType.Chaos => 6,       // Create variables
                        CardType.Curse => 4,       // Slow attack (Meh)
                        CardType.Cooperation => 3, // No cooperation
                        CardType.Doubt => 2,       // No defense
                        CardType.Recon => 1,       // No thinking
                        CardType.Investment => -5, // [NEW] Hate: Useless right now
                        _ => 0
                    };
                    // ★ [FIX] Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }
                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            A.fallback = new[] {
                CardType.Sacrifice, CardType.Betrayal, CardType.Pollution, CardType.Chaos,
                CardType.Interrupt, CardType.Curse, CardType.Cooperation, CardType.Doubt, 
                CardType.Recon, CardType.Investment // Lowest priority
            };
            
            // --- Selective Draw (Draft) ---
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);

                // [NEW] Investment Avoidance: "Throw it away."
                // Always skip it if possible.
                if (a == CardType.Investment && b != CardType.Investment) return 1;
                if (b == CardType.Investment && a != CardType.Investment) return 0;

                // [Existing] Sacrifice Priority
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 0;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 1;

                // Chaos Avoidance
                if (a == CardType.Chaos && b != CardType.Chaos) return 1;
                if (b == CardType.Chaos && a != CardType.Chaos) return 0;

                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var last = I.s.lastOpp;

                float Score(CardType x)
                {
                    float s = x switch
                    {
                        CardType.Sacrifice   => 200, // Overwhelming preference
                        CardType.Betrayal    => 100, // Attack
                        CardType.Pollution   => 80,  // Attack
                        CardType.Interrupt   => 60,
                        CardType.Curse       => 50,
                        CardType.Doubt       => 45,
                        CardType.Cooperation => 30,
                        CardType.Recon       => 10,
                        CardType.Investment  => -100, // [NEW] Absolute refusal
                        _ => 0
                    };
                    
                    // Situational Bonus (Obsession with Attack)
                    if (x==CardType.Betrayal && I.s.oppLife <= R+1) s += 25;
                    
                    // Secure Attack Means
                    int atkInHand = (I.HandHas(CardType.Betrayal)?1:0)+(I.HandHas(CardType.Pollution)?1:0);
                    if (atkInHand==0 && (x==CardType.Betrayal||x==CardType.Pollution)) s += 12;
                    
                    // ★ [FIX] Safe dictionary access
                    return s * (weights.ContainsKey(x) ? weights[x] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);
                if (Math.Abs(sa - sb) < 0.1f)
                {
                    // If scores are similar, pick the more aggressive one (lower index usually in enum)
                    return sa > sb ? 0 : 1;
                }
                return sa>sb?0:1;
            };
            return A;
        }

        // Han Ji-hye v3 — The Balancer: Balance and Harmony (Investment for Co-existence & Strict Balance)
        static Agent Build_한지혜(AgentList id)
        {
            var A = new Agent("한지혜", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var history = I.HistoryOpponent();

                // Opponent Distribution (Based on recent history)
                var p = new Dictionary<CardType, float> {
                    {CardType.Cooperation, 0.05f + I.Ratio(CardType.Cooperation)},
                    {CardType.Doubt,       0.05f + I.Ratio(CardType.Doubt)},
                    {CardType.Betrayal,    0.05f + I.Ratio(CardType.Betrayal)},
                    {CardType.Chaos,       0.05f + I.Ratio(CardType.Chaos)},
                    {CardType.Pollution,   0.05f + I.Ratio(CardType.Pollution)},
                    {CardType.Interrupt,   0.05f + I.Ratio(CardType.Interrupt)},
                    {CardType.Recon,       0.05f + I.Ratio(CardType.Recon)},
                    {CardType.Curse,       0.05f + I.Ratio(CardType.Curse)},    
                    {CardType.Sacrifice,   0.05f + I.Ratio(CardType.Sacrifice)},
                    {CardType.Investment,  0.05f + I.Ratio(CardType.Investment)} // [NEW]
                };
                float S = p.Values.Sum(); foreach (var k in p.Keys.ToList()) p[k] /= S;

                // [The Balancer] Detect Balance Disruption
                bool isPeaceful = p[CardType.Cooperation] + p[CardType.Investment] > 0.4f;
                bool isHostile = p[CardType.Betrayal] + p[CardType.Pollution] > 0.3f;
                
                // 1. [NEW] Utilize Investment: "A path to survive together."
                // Use when opponent is peaceful (Coop/Inv) OR when situation is balanced (neither too advantageous nor disadvantageous)
                if (I.HandHas(CardType.Investment))
                {
                    // If opponent shows trust, actively invest for mutual benefit
                    if (isPeaceful) return CardType.Investment;
                    
                    // If mid-game (R >= 4) and opponent is not hostile, attempt investment
                    if (R >= 4 && !isHostile) return CardType.Investment;
                }

                // 2. [Tuning] Counter Sacrifice
                // If opponent tries to 'monopolize' victory with Sacrifice (3 cards) -> Stop to restore balance
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal; 
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 3. [Defense Mechanism] Fear of Betrayal
                // Over-defend if opponent attack probability is high (> 30%)
                bool lethalRisk = I.s.selfLife <= R;
                if ((lethalRisk || isHostile) && I.HandHas(CardType.Doubt)) 
                    return CardType.Doubt;

                // 4. [Pattern Counter]
                if (nf && I.s.lastOpp == I.s.last2Opp && I.s.lastOpp != CardType.None)
                {
                    var x = I.s.lastOpp;
                    // If opponent keeps investing? -> Invest together (Coop) or Defend (Doubt)
                    if (x == CardType.Investment)
                    {
                        if (I.HandHas(CardType.Investment)) return CardType.Investment;
                        if (I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                    }
                    
                    if (x == CardType.Cooperation && I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    if (x == CardType.Pollution && I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    if (x == CardType.Betrayal && I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                }

                // 5. [Co-existence] Cooperation
                // Attempt to build trust if no specific threat
                if (I.HandHas(CardType.Cooperation) && !isHostile)
                    return CardType.Cooperation;

                // 6. [Balance] Avoid cruelty even if lethal (Trait: Emotional)
                // But retaliate if opponent betrayed first
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R)
                {
                    // Only kill if opponent betrayal rate is high
                    if (p[CardType.Betrayal] > 0.2f) return CardType.Betrayal;
                }

                // 7. Base Priority Score
                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Investment => 8,  // [NEW] Tool for co-existence (Preferred)
                        CardType.Cooperation => 7, // Peace preference
                        CardType.Doubt => 6,       // Defense (Safe)
                        CardType.Pollution => 5,   // Moderate check
                        CardType.Recon => 4,       // Check info
                        CardType.Betrayal => 3,    // Reluctant to betray first
                        CardType.Curse => 2,       // Hates harming others
                        CardType.Interrupt => 2,
                        CardType.Chaos => 1,       // Hates chaos
                        CardType.Sacrifice => -10, // [Trait] Never self-harm
                        _ => 0
                    };
                    // ★ [FIX] Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }
                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            A.fallback = new[] {
                CardType.Investment, CardType.Cooperation, CardType.Doubt,
                CardType.Pollution, CardType.Recon,        CardType.Interrupt,
                CardType.Betrayal,  CardType.Curse,        CardType.Chaos,
                CardType.Sacrifice
            };

            // [Selective Draw]
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);

                // [NEW] Investment: "Let's live together."
                // Actively pick if opponent is friendly
                bool isFriendly = I.Ratio(CardType.Cooperation) + I.Ratio(CardType.Investment) > 0.4f;

                // [Existing] Hate Sacrifice: "I don't want to hurt myself."
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1; 
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                float Score(CardType c)
                {
                    float baseScore = 0;
                    if (c == CardType.Investment)  baseScore = isFriendly ? 4.5f : 2.5f; // [NEW]
                    if (c == CardType.Cooperation) baseScore = 4.0f;
                    if (c == CardType.Doubt)       baseScore = 3.5f; // Secure defense
                    if (c == CardType.Pollution)   baseScore = 2.0f;
                    if (c == CardType.Interrupt)   baseScore = 1.5f;
                    if (c == CardType.Recon)       baseScore = 1.0f;
                    if (c == CardType.Betrayal)    baseScore = (I.s.oppLife <= R ? 3 : 0.5f); // Only pick if lethal
                    if (c == CardType.Curse)       baseScore = 0.2f; // Hate curse
                    if (c == CardType.Chaos)       baseScore = -5f;  // Hate chaos
                    if (c == CardType.Sacrifice)   baseScore = -100f;
                    
                    // ★ [FIX] Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }

                float sa = Score(a);
                float sb = Score(b);

                // Balance hand (Attack/Defense Ratio)
                bool needAtk = !(I.HandHas(CardType.Betrayal) || I.HandHas(CardType.Pollution));
                bool needDef = !(I.HandHas(CardType.Doubt) || I.HandHas(CardType.Interrupt));

                if (Math.Abs(sa - sb) < 0.1f)
                {
                    if (needDef && ((a == CardType.Doubt || a == CardType.Interrupt) || (b == CardType.Doubt || b == CardType.Interrupt)))
                        return (a == CardType.Doubt || a == CardType.Interrupt) ? 0 : 1;
                        
                    if (needAtk && ((a == CardType.Betrayal || a == CardType.Pollution) || (b == CardType.Betrayal || b == CardType.Pollution)))
                        return (a == CardType.Betrayal || a == CardType.Pollution) ? 0 : 1;
                }

                return sa >= sb ? 0 : 1;
            };
            return A;
        }

        // 박민재v4 — 계산된 냉정함 (Sacrifice 배제 / Curse 확률 계산)
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

                // 3. [NEW] Investment EV Calculation
                // "Has the current stack crossed the break-even point?"
                // Min-jae acknowledges value when Investment stack is likely >= 2 (Heal +1).
                // *Note: Since globalCount is unknown to Agent, estimate using Round(R).
                bool investmentProfitable = R >= 4; 

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

                        case CardType.Investment:
                            // [NEW] High-yield asset if profitable, otherwise trash
                            score = investmentProfitable ? 4.5f : -5f;
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
                    
                    // ★ [FIX] Safe dictionary access
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
                CardType.Investment, CardType.Cooperation, CardType.Doubt, CardType.Interrupt, CardType.Recon
            };

            // --- 선택 드로우 (Draft) ---
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool losing = I.s.selfLife < I.s.oppLife;

                // [NEW] Investment: "Calculating late-game potential."
                // Actively pick if Round >= 5 (Break-even likely passed).
                bool lateGame = R >= 5;

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
                        case CardType.Investment:  score = lateGame ? 3.0f : 2.5f; break; // [NEW] Value shifts by time
                        case CardType.Cooperation: score = 2.0f; break;
                        case CardType.Recon:       score = 1.0f; break;
                        case CardType.Chaos:       score = -3.0f; break; // "변수 혐오"
                        case CardType.Sacrifice:   score = -10.0f; break; // [신규]
                    }
                    // ★ [FIX] Safe dictionary access
                    return score * (weights.ContainsKey(c) ? weights[c] : 1.0f);

                }

                float va = V(a), vb = V(b);

                // 정밀 비교 (동점 시 티어 구분)
                if (Math.Abs(va - vb) < 0.1f)
                {
                    int Rank(CardType t) => t switch
                    {
                        CardType.Pollution => 9, // 가장 선호 (안정적 공격)
                        CardType.Betrayal => 8,
                        CardType.Curse => 7,
                        CardType.Doubt => 6,
                        CardType.Interrupt => 5,
                        CardType.Investment => 4,
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

        // Jeon Da-eun v3 — The Analyst: Psychological Manipulation & Sadistic Analysis (Investment as Bait)
        static Agent Build_정다은(AgentList id)
        {
            var A = new Agent("정다은", id);

            // ① Round Card Selection
            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                var history = I.HistoryOpponent();

                // 1. Estimate Opponent Behavior Distribution (Psychological Analysis)
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
                    { CardType.Sacrifice,   I.Ratio(CardType.Sacrifice)   },
                    { CardType.Investment,  I.Ratio(CardType.Investment)  } // [NEW]
                };

                // [Pattern Analysis] Short-term memory weight
                var recent = new[] { I.s.lastOpp, I.s.last2Opp }.Where(t => t != CardType.None).ToArray();
                if (recent.Length > 0)
                {
                    var mode = recent.GroupBy(t => t).OrderByDescending(g => g.Count()).First().Key;
                    if (p.ContainsKey(mode)) p[mode] *= 1.5f; // "They'll use that move again."
                }
                
                float sum = p.Values.Sum(); if (sum <= 0) sum = 1f;
                foreach (var k in p.Keys.ToList()) p[k] /= sum;

                // 2. [Psychological Manipulation] Utilize Investment
                // "This is bait." If opponent is not defensive (Doubt), throw Investment to profit 
                // or build trust for a future betrayal.
                if (I.HandHas(CardType.Investment))
                {
                    // If opponent seems cooperative (Coop/Inv) or scouting (Recon) -> Invest safely
                    bool isSafe = p[CardType.Cooperation] + p[CardType.Investment] + p[CardType.Recon] > 0.4f;
                    if (isSafe) return CardType.Investment;
                }

                // 3. [Sadistic Control] Curse / Interrupt
                // If opponent shows a gap (not defending), torment with Curse. If attacking, cut off with Interrupt.
                if (I.HandHas(CardType.Curse))
                {
                    float defensiveProb = p[CardType.Doubt] + p[CardType.Interrupt];
                    // Opponent let guard down (low def prob) -> Curse
                    if (defensiveProb < 0.35f) 
                        return CardType.Curse;
                }

                // If opponent attack prob is high, counter with Interrupt
                float attackProb = p[CardType.Betrayal] + p[CardType.Pollution];
                if (attackProb > 0.4f && I.HandHas(CardType.Interrupt))
                    return CardType.Interrupt;

                // 4. [Execution] Lethal & Sacrifice Suppression
                int oppSacCount = history.Count(x => x == CardType.Sacrifice);
                if (oppSacCount >= 3) // Opponent has false hope (Sacrifice)
                {
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal; // Execute
                    if (I.HandHas(CardType.Curse)) return CardType.Curse; // Dry out
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // Normal Lethal: Betray when opponent guard is down
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.30f)
                    return CardType.Betrayal;
                
                // 5. Crisis Management (Defense)
                bool danger = I.s.selfLife <= R && (attackProb > 0.4f);
                if (danger && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                // 6. EV Simulation (Matrix Delta)
                int Delta(CardType a, CardType b) 
                { 
                    int r = R;
                    
                    if (a == CardType.Investment)
                    {
                        // Investment is profit (+2~+R) if uninterrupted (considered bait value too)
                        if (b == CardType.Doubt || b == CardType.Interrupt) return 0;
                        if (b == CardType.Betrayal || b == CardType.Pollution) return -1;
                        return 2; 
                    }
                    if (a == CardType.Curse)
                    {
                        if (b == CardType.Doubt || b == CardType.Interrupt) return 0; 
                        if (b == CardType.Betrayal) return -1; 
                        return +2; // Psychological advantage (+2) on success
                    }
                    if (a == CardType.Sacrifice) return -10; // She never sacrifices
                    
                    // When opponent uses Curse
                    if (b == CardType.Curse)
                    {
                        if (a == CardType.Doubt || a == CardType.Interrupt) return +1; // Defense success
                        return -1;
                    }

                    // Existing Delta Logic (Simplified)
                    if (a == CardType.Betrayal && b == CardType.Cooperation) return r + 1;
                    if (a == CardType.Cooperation && b == CardType.Betrayal) return -(r + 1);
                    if (a == CardType.Interrupt && (b == CardType.Betrayal || b == CardType.Pollution)) return +2;
                    
                    return 0; 
                }

                var cand = I.hand.Distinct().Where(I.HandHas).ToList();
                CardType best = CardType.None; 
                float bestEV = float.NegativeInfinity;

                foreach (var a in cand)
                {
                    // Never play Sacrifice
                    if (a == CardType.Sacrifice) continue;

                    float ev = 0f; 
                    foreach (var b in p.Keys) ev += p[b] * Delta(a, b);

                    // [Pattern Hiding] Avoid playing the same card twice (anti-read)
                    if (I.s.lastSelf == a) ev -= 0.8f;

                    // ★ [FIX] Safe dictionary access
                    ev *= (weights.ContainsKey(a) ? weights[a] : 1.0f);

                    if (ev > bestEV) { bestEV = ev; best = a; }
                }
                return best;
            });

            // ② Selective Draw (Draft)
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);

                // [Existing] Disdain Sacrifice: "For the weak."
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                // [NEW] Investment: "Nice bait."
                // Grab it if seen (After Betrayal, Curse)

                // Score Function V(x, y) - Jeon Da-eun's valuation
                float Score(CardType card)
                {
                    float s = card switch
                    {
                        CardType.Curse       => 90, // [Core] Sadistic preference
                        CardType.Interrupt   => 85, // [Core] Control
                        CardType.Betrayal    => 80,
                        CardType.Recon       => 75, // Information
                        CardType.Pollution   => 60,
                        CardType.Doubt       => 40,
                        CardType.Investment  => 35, // [NEW] Bait
                        CardType.Cooperation => 30,
                        CardType.Chaos       => 10,
                        CardType.Sacrifice   => -99, // Hate
                        _ => 0
                    };

                    // Context Adjustment: Increase aggressive card value if opponent defends poorly
                    float defensiveRatio = I.Ratio(CardType.Doubt) + I.Ratio(CardType.Interrupt);
                    if (defensiveRatio < 0.3f)
                    {
                        if (card == CardType.Curse) s += 15;
                        if (card == CardType.Betrayal) s += 10;
                    }

                    // ★ [FIX] Safe dictionary access
                    return s * (weights.ContainsKey(card) ? weights[card] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);
                
                // If tied, prefer the more 'devious' card (Curse, Interrupt)
                if (Math.Abs(sa - sb) < 0.1f)
                {
                    int Rank(CardType t) => t switch
                    {
                        CardType.Curse => 10, CardType.Interrupt => 9, CardType.Recon => 8, 
                        CardType.Betrayal => 7, CardType.Investment => 6, _ => 0
                    };
                    return Rank(a) >= Rank(b) ? 0 : 1;
                }
                return sa >= sb ? 0 : 1;
            };

            // ③ Fallback Priority
            A.fallback = new[]
            {
                CardType.Curse,     // 1st: Torment
                CardType.Interrupt, // 2nd: Disrupt
                CardType.Recon,     // 3rd: Peek
                CardType.Betrayal,
                CardType.Pollution,
                CardType.Doubt,
                CardType.Investment,// [NEW] Bait
                CardType.Cooperation,
                CardType.Chaos,
                CardType.Sacrifice  // Lowest
            };
            return A;
        }
        
        // Oh Tae-hoon v3 — Berserker: Immature Genius (Pattern Analysis + Hubris/Rage + Investment Snowballing)
        static Agent Build_오태훈(AgentList id)
        {
            var A = new Agent("오태훈", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var history = I.HistoryOpponent();

                // [Emotional State Analysis]
                bool winning = I.s.selfLife > I.s.oppLife; // Leading (Arrogance)
                bool losing = I.s.selfLife < I.s.oppLife;  // Trailing (Rage)
                bool angry = losing && (I.s.selfLife <= 5); // Berserk Mode

                // 1. [Genius Intuition] Counter Opponent Sacrifice -> Rage Response
                // "You think you can end me? No chance!"
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    // No defense. Kill before killed.
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                    // If no attack card, flip the table with Chaos
                    if (I.HandHas(CardType.Chaos)) return CardType.Chaos;
                }

                // 2. [Hubris] Attempt Sacrifice / Investment when winning (Widen the gap)
                if (winning)
                {
                    // [NEW] Investment: "I already own this game."
                    // When winning, use Investment to widen the HP gap and cause despair.
                    if (I.HandHas(CardType.Investment))
                    {
                        // If opponent is defensive (scared), invest even more boldly
                        if (I.Ratio(CardType.Doubt) > 0.3f || I.s.lastOpp == CardType.Doubt)
                            return CardType.Investment;
                    }

                    // Sacrifice: Used for a stylish victory
                    if (I.HandHas(CardType.Sacrifice) && I.s.selfLife >= 6)
                        return CardType.Sacrifice;
                }

                // 3. [Pattern Learning] Read Opponent Routine
                // "That again? Obvious." Perfect counter if opponent plays same card twice
                if (nf && I.s.lastOpp == I.s.last2Opp && I.s.lastOpp != CardType.None)
                {
                    var pattern = I.s.lastOpp;
                    
                    // If opponent only Defends/Cooperates -> Punish with Curse or Betrayal
                    if (pattern == CardType.Cooperation || pattern == CardType.Doubt || pattern == CardType.Investment)
                    {
                        if (I.HandHas(CardType.Curse)) return CardType.Curse; 
                        if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    }
                    // If opponent keeps Attacking -> Reflect with Interrupt
                    if ((pattern == CardType.Betrayal || pattern == CardType.Pollution) && I.HandHas(CardType.Interrupt))
                        return CardType.Interrupt;
                }

                // 4. [Emotional Explosion] Spam Chaos when losing
                // "Ah, annoying! Flip it all!"
                if (losing && I.HandHas(CardType.Chaos))
                {
                    // Ignore risk, just do it
                    return CardType.Chaos;
                }

                // 5. Lethal Instinct (Genius Calculation)
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R) return CardType.Betrayal;
                if (I.HandHas(CardType.Pollution) && I.s.oppLife <= R - 1) return CardType.Pollution;

                // 6. [Utilize Curse] Pressure in ambiguous situations
                if (I.HandHas(CardType.Curse))
                {
                    // If opponent was aggressive or used Chaos, assume low defense chance -> Curse
                    if (nf && (I.s.lastOpp == CardType.Betrayal || I.s.lastOpp == CardType.Chaos))
                        return CardType.Curse;
                }

                // 7. Calculate Weighted Score
                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Betrayal => 10,   // [Trait] High Aggression
                        CardType.Chaos => 9,       // [Favorite] Loves Chaos
                        CardType.Curse => 8,       // Fun toy
                        CardType.Pollution => 7,
                        // Sacrifice: 6 pts when winning (Hubris), -10 pts when losing (Dislike)
                        CardType.Sacrifice => winning ? 6 : -10, 
                        CardType.Interrupt => 5,
                        CardType.Recon => 4,       // "I already know everything, why bother?"
                        CardType.Investment => winning ? 3 : -5, // [NEW] Snowballing tool
                        CardType.Cooperation => 2, // Boring
                        CardType.Doubt => 1,       // For cowards
                        _ => 0
                    };

                    // [Berserker] Attack card scores explode when angry
                    if (angry)
                    {
                        if (c == CardType.Betrayal || c == CardType.Pollution) baseScore += 5;
                        if (c == CardType.Chaos) baseScore += 10; // Desperation
                    }

                    // ★ [FIX] Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }

                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            A.fallback = new[] {
                CardType.Betrayal, CardType.Chaos, CardType.Curse,
                CardType.Pollution, CardType.Interrupt, CardType.Investment,
                CardType.Cooperation, CardType.Doubt, CardType.Recon, CardType.Sacrifice
            };
            
            // Oh Tae-hoon — Selective Draw (Aggressive, Erratic, Hubris)
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool winning = I.s.selfLife > I.s.oppLife;

                // [NEW] Investment / Sacrifice: Pick only when winning (Snowballing)
                if (winning)
                {
                    // Pick "Show off" cards when winning
                    if (a == CardType.Investment || a == CardType.Sacrifice) 
                        if (b != CardType.Investment && b != CardType.Sacrifice) return 0;
                    
                    if (b == CardType.Investment || b == CardType.Sacrifice)
                        if (a != CardType.Investment && a != CardType.Sacrifice) return 1;
                }
                else
                {
                    // Discard cards that don't help immediately when losing
                    if (a == CardType.Investment || a == CardType.Sacrifice) return 1;
                    if (b == CardType.Investment || b == CardType.Sacrifice) return 0;
                }

                // [Chaos Love]: Pick Chaos if seen
                if (a == CardType.Chaos && b != CardType.Chaos) return 0;
                if (b == CardType.Chaos && a != CardType.Chaos) return 1;

                float Score(CardType x)
                {
                    float s = x switch
                    {
                        CardType.Betrayal    => 100,
                        CardType.Chaos       => 95,  // [Favorite]
                        CardType.Curse       => 85,  
                        CardType.Pollution   => 80,
                        CardType.Sacrifice   => winning ? 88 : -50,
                        CardType.Recon       => 35,
                        CardType.Interrupt   => 30,
                        // [NEW] Value fluctuation based on state
                        CardType.Investment  => winning ? 25 : -50, 
                        CardType.Cooperation => 20,
                        CardType.Doubt       => 10,
                        _ => 0
                    };

                    if (x == CardType.Betrayal && I.s.oppLife <= R + 2) s += 30; 
                    
                    // ★ [FIX] Safe dictionary access
                    return s * (weights.ContainsKey(x) ? weights[x] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);
                
                // Whimsical: 50% chance to pick randomly if scores are close
                if (Math.Abs(sa - sb) < 10f) return UnityEngine.Random.value < 0.5f ? 0 : 1;
                return sa > sb ? 0 : 1;
            };
            return A;
        }

        // Yoo Min-jung v3 — Iron Wall: Aesthetics of Compliance (Defensive Mirroring & Investment Bandwagoning)
        static Agent Build_유민정(AgentList id)
        {
            var A = new Agent("유민정", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var history = I.HistoryOpponent();

                // [Compliance] Read opponent's flow
                bool opponentIsAggressive = I.Ratio(CardType.Betrayal) + I.Ratio(CardType.Pollution) > 0.3f;
                bool opponentIsInvesting = I.Ratio(CardType.Investment) > 0.2f;

                // 1. [Defensive] Iron Wall Defense
                // Priority defense if opponent is aggressive or health is low
                if (opponentIsAggressive || I.s.selfLife <= R)
                {
                    if (I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    // Counter with Interrupt when opponent attacks (Using others' force)
                    if (nf && (I.s.lastOpp == CardType.Betrayal || I.s.lastOpp == CardType.Pollution))
                        if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                }

                // 2. [Existing] Counter Sacrifice: "Don't make me a scapegoat."
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 3. [NEW] Investment Bandwagon: "You invest? Then I will too."
                // She doesn't lead investment. Only follows.
                if (I.HandHas(CardType.Investment))
                {
                    // If opponent invested last round, or general investment ratio is high
                    if (nf && I.s.lastOpp == CardType.Investment)
                        return CardType.Investment;
                    
                    if (opponentIsInvesting) 
                        return CardType.Investment;
                }

                // 4. [Existing] Utilize Curse: "Silent Encroachment"
                // Use when opponent is peaceful/distracted
                if (nf && I.HandHas(CardType.Curse))
                {
                    if (I.s.lastOpp == CardType.Cooperation || I.s.lastOpp == CardType.Recon || I.s.lastOpp == CardType.Investment)
                    {
                        // Only use if low chance of defense (dislikes attention)
                        if (I.Ratio(CardType.Doubt) < 0.4f)
                            return CardType.Curse;
                    }
                }

                // 5. Immediate Response (Mirroring & Counter)
                if (nf)
                {
                    if (I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt))
                        return CardType.Doubt;
                    
                    if (I.s.lastOpp == CardType.Curse)
                    {
                        if (I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                        if (I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    }

                    // Mirroring (Copy opponent unless it's aggressive)
                    // [NEW] Include Investment in mirroring logic if not caught above
                    if (I.HandHas(I.s.lastOpp) && 
                        I.s.lastOpp != CardType.Betrayal && 
                        I.s.lastOpp != CardType.Pollution && 
                        I.s.lastOpp != CardType.Curse &&
                        I.s.lastOpp != CardType.Sacrifice) 
                        return I.s.lastOpp;
                }

                // 6. Base Priority Score
                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Doubt => 12,       // [Core] Iron Wall: Defense First
                        CardType.Investment => 10,   // [NEW] Use if context fits (Bandwagon)
                        CardType.Cooperation => 9,  // [Trait] Compliance
                        CardType.Interrupt => 8,    // Use opponent's force
                        CardType.Pollution => 6,
                        CardType.Curse => 5,        // Silent attack
                        CardType.Recon => 4,
                        CardType.Chaos => 2,
                        CardType.Betrayal => 1,     // Dislikes attention-grabbing attacks
                        CardType.Sacrifice => -20,  // [Trait] Avoids completely
                        _ => 0
                    };
                    // ★ [FIX] Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }
                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            A.fallback = new[]
            {
                CardType.Doubt, CardType.Cooperation, CardType.Interrupt,
                CardType.Investment, CardType.Pollution, CardType.Curse, 
                CardType.Recon, CardType.Chaos, CardType.Betrayal, CardType.Sacrifice
            };
            
            // Selective Draw (Draft)
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);

                // [Existing] Hate Sacrifice: "It's dangerous."
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                // [NEW] Investment: "Should I follow the flow?"
                // Pick if opponent invests often
                bool opponentInvests = I.Ratio(CardType.Investment) > 0.2f;

                float Score(CardType t)
                {
                    float s = t switch
                    {
                        CardType.Doubt        => 100, // Defense essential
                        CardType.Investment   => opponentInvests ? 85 : 40, // [NEW] Follow opponent
                        CardType.Cooperation  => 80,  // Compliance
                        CardType.Interrupt    => 70,
                        CardType.Recon        => 50,
                        CardType.Curse        => 45,
                        CardType.Pollution    => 30,
                        CardType.Betrayal     => 20,
                        CardType.Chaos        => 10,
                        CardType.Sacrifice    => -100,
                        _ => 0
                    };

                    // Context Adjustment
                    if (I.s.selfLife < I.s.oppLife && t == CardType.Doubt) s += 30; // Withdraw further
                    
                    if (I.Ratio(CardType.Curse) > 0.2f && t == CardType.Cooperation) s += 20;

                    // ★ [FIX] Safe dictionary access
                    return s * (weights.ContainsKey(t) ? weights[t] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);
                if (Math.Abs(sa - sb) < 0.1f)
                {
                    // Pick safer card on tie
                    int safe(CardType t) => t switch
                    {
                        CardType.Doubt => 5, CardType.Cooperation => 4, 
                        CardType.Interrupt => 3, CardType.Investment => 2, _ => 0
                    };
                    return safe(a) >= safe(b) ? 0 : 1;
                }
                return sa > sb ? 0 : 1;
            };
            return A;
        }

        // Kim Tae-yang v3 — The Joker: Erratic & Random (Investment Prank & Chaos Lover)
        static Agent Build_김태양(AgentList id)
        {
            var A = new Agent("김태양", id);
            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var history = I.HistoryOpponent();

                // [The Joker] Mood Mode
                // Mood changes every turn (0: Aggro, 1: Chaos, 2: Random)
                int mood = UnityEngine.Random.Range(0, 3); 

                // 0. [Genius Sense] Sacrifice Lethal (Can't resist)
                if (I.HandHas(CardType.Sacrifice))
                {
                    // Randomly throw it, or show off if health is high
                    if (UnityEngine.Random.value < 0.4f || I.s.selfLife > 6) 
                        return CardType.Sacrifice;
                }

                // 1. [Disruption] Opponent trying 'Boring Win' with Sacrifice?
                // "Don't plan! Flip the table!" -> Chaos first, then Betrayal
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    if (I.HandHas(CardType.Chaos)) return CardType.Chaos; // Reset is most fun
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                }

                // 2. [NEW] Utilize Investment: "Investing now? Lol"
                // He uses Investment as an unpredictable move.
                // Plays it when opponent expects an attack (Doubt timing) to confuse them,
                // or throws it on Round 1 for no reason.
                if (I.HandHas(CardType.Investment))
                {
                    // 30% chance to just throw it (No reason)
                    if (UnityEngine.Random.value < 0.3f) return CardType.Investment;
                    
                    // Reverse psychology: Invest when opponent is likely to Doubt (Mockery)
                    if (I.Ratio(CardType.Doubt) > 0.4f) return CardType.Investment;
                }

                // 3. [Curse Prank]
                // If opponent is quiet, throw a curse out of boredom
                if (nf && (I.s.lastOpp == CardType.Cooperation || I.s.lastOpp == CardType.Doubt))
                {
                    if (I.HandHas(CardType.Curse) && UnityEngine.Random.value < 0.5f)
                        return CardType.Curse;
                }

                // 4. [Chaos Pursuit] Chaos (Active when Mood 1)
                if (mood == 1 && I.HandHas(CardType.Chaos))
                    return CardType.Chaos;

                // 5. [Early Rush] Random Attack (Active when Mood 0)
                if (mood == 0 && (R <= 4 || UnityEngine.Random.value < 0.6f))
                {
                    var pool = new List<CardType>();
                    if (I.HandHas(CardType.Chaos)) pool.Add(CardType.Chaos);
                    if (I.HandHas(CardType.Pollution)) pool.Add(CardType.Pollution);
                    if (I.HandHas(CardType.Betrayal)) pool.Add(CardType.Betrayal);
                    if (I.HandHas(CardType.Curse)) pool.Add(CardType.Curse); 
                    if (I.HandHas(CardType.Sacrifice)) pool.Add(CardType.Sacrifice); 
                    
                    if (pool.Count > 0) return pool[UnityEngine.Random.Range(0, pool.Count)];
                }

                // 6. [Random Pick] Weighted Random
                // He hates calculation, picks randomly based on weights
                {
                    var bag = new List<CardType>();
                    void Push(CardType t, int w)
                    {
                        if (!I.HandHas(t)) return;
                        // ★ [FIX] Safe dictionary access
                        float weightVal = weights.ContainsKey(t) ? weights[t] : 1.0f;
                        int finalWeight = Mathf.RoundToInt(w * weightVal);
                        for (int k = 0; k < finalWeight; ++k) bag.Add(t);
                    }
                    // More fun = Higher weight
                    Push(CardType.Betrayal, 5);
                    Push(CardType.Chaos, 5);
                    Push(CardType.Sacrifice, 4);  
                    Push(CardType.Curse, 4);      
                    Push(CardType.Pollution, 3);
                    Push(CardType.Interrupt, 3);
                    Push(CardType.Investment, 2); // [NEW] Moderate fun
                    Push(CardType.Cooperation, 1); // Boring
                    Push(CardType.Doubt, 1);       // Boring
                    Push(CardType.Recon, 0);       // Hate

                    if (bag.Count > 0)
                        return bag[UnityEngine.Random.Range(0, bag.Count)];
                }

                // Fallback
                return I.FirstOrNone();
            });

            A.fallback = new[]
            {
                CardType.Chaos, CardType.Betrayal, CardType.Sacrifice, 
                CardType.Curse, CardType.Pollution, 
                CardType.Interrupt, CardType.Doubt, CardType.Investment, CardType.Cooperation, CardType.Recon
            };
            
            // Selective Draw (2 cards): "Which one is more fun?"
            A.chooseFromTwo = (CardType a, CardType b, DecisionInput I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                bool losing = I.s.selfLife < I.s.oppLife;

                // [Chaos Love]
                if (a == CardType.Chaos && b != CardType.Chaos) return UnityEngine.Random.value < 0.7f ? 0 : 1;
                if (b == CardType.Chaos && a != CardType.Chaos) return UnityEngine.Random.value < 0.7f ? 1 : 0;

                // [Sacrifice Gamble]
                if (losing)
                {
                    // 50% chance to pick 'Red Pill' (Sacrifice) when losing
                    if (a == CardType.Sacrifice) if (UnityEngine.Random.value < 0.5f) return 0;
                    if (b == CardType.Sacrifice) if (UnityEngine.Random.value < 0.5f) return 1;
                }

                float Score(CardType x)
                {
                    int s = x switch
                    {
                        CardType.Chaos       => 100, // [Favorite]
                        CardType.Betrayal    => 90,
                        CardType.Sacrifice   => 85, 
                        CardType.Curse       => 80, 
                        CardType.Pollution   => 60,
                        CardType.Interrupt   => 40,
                        CardType.Investment  => 30, // [NEW] Moderate fun
                        CardType.Cooperation => 20, // Boring
                        CardType.Doubt       => 10, // Boring
                        CardType.Recon       => 0,  // Hate
                        _ => 0
                    };
                    
                    // Whimsical bonus (Changes every match) -> Erratic trait
                    s += UnityEngine.Random.Range(-20, 20);

                    // ★ [FIX] Safe dictionary access
                    return s * (weights.ContainsKey(x) ? weights[x] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);

                // Total Randomness (20% chance to pick anything regardless of score)
                if (UnityEngine.Random.value < 0.2f)
                    return UnityEngine.Random.Range(0, 2);

                return sa > sb ? 0 : 1;
            };
            return A;
        }

        // Lee Ha-rin v3 — The Innocent: Pure Mimic (Visual Preference & Emotional Sync)
        static Agent Build_이하린(AgentList id)
        {
            var A = new Agent("이하린", id);

            // Kindergarten preference order (Visual/Emotional)
            // Sparkly(Coop) > Treasure(Inv) > Toy(Chaos) > Scope(Recon) > Shield(Doubt) > ... > Scary(Curse/Sacrifice/Betrayal)
            CardType[] cuteOrder = {
                CardType.Cooperation, // Sparkly & Pretty (Favorite)
                CardType.Investment,  // [NEW] Treasure Chest / Piggy Bank (Loves it)
                CardType.Chaos,       // Colorful & Fun
                CardType.Recon,       // Telescope Toy
                CardType.Doubt,       // Blue Shield (Looks safe)
                CardType.Interrupt,   // High-five
                CardType.Pollution,   // Green Slime (Eww)
                CardType.Curse,       // Ghost (Scary)
                CardType.Sacrifice,   // Hurt (Dislike)
                CardType.Betrayal     // Knife (Scariest)
            };

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);

                // 0. [Emotional Sync] Chaos is fun!
                if (I.HandHas(CardType.Chaos) && UnityEngine.Random.value < 0.5f)
                    return CardType.Chaos;

                // 1. [Pure Mimicry] "I want to do what unnie/oppa did!"
                // Mimic previous opponent card with 40% chance
                if (!I.s.IsFirst && I.HandHas(I.s.lastOpp))
                {
                    // She hates mimicking scary cards
                    if (I.s.lastOpp == CardType.Sacrifice || I.s.lastOpp == CardType.Betrayal || I.s.lastOpp == CardType.Curse)
                    {
                        // Very low chance to mimic scary things
                        if (UnityEngine.Random.value < 0.05f) return I.s.lastOpp;
                    }
                    else
                    {
                        // Mimic others (including Investment) with 40% chance
                        if (UnityEngine.Random.value < 0.40f) return I.s.lastOpp;
                    }
                }

                // 2. [NEW] Utilize Investment: "I'm saving up!"
                // She plays it because she likes the picture, no calculation involved.
                // But if opponent is "scary" (attacking), she might cry and defend/run away.
                if (I.HandHas(CardType.Investment))
                {
                    // If opponent didn't attack last turn (peaceful), she happily invests.
                    bool safe = I.s.lastOpp != CardType.Betrayal && I.s.lastOpp != CardType.Pollution;
                    if (safe && UnityEngine.Random.value < 0.3f)
                        return CardType.Investment;
                }

                // 3. [Visual Preference] Play cards in order of "cuteness"
                foreach (var c in cuteOrder)
                {
                    if (I.HandHas(c))
                    {
                        // ★ [FIX] Safe dictionary access
                        float w = weights.ContainsKey(c) ? weights[c] : 1.0f;
                        if (w < 0.5f) continue; 
                        
                        return c;
                    }
                }

                // 4. Fallback
                return I.FirstOrNone();
            });

            A.fallback = cuteOrder;

            // --- Selective Draw (Draft): "This one is prettier!" ---
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);

                // [Existing] Absolute avoidance of Scary cards
                bool aIsScary = a == CardType.Sacrifice || a == CardType.Betrayal;
                bool bIsScary = b == CardType.Sacrifice || b == CardType.Betrayal;

                if (aIsScary && !bIsScary) return 1;
                if (bIsScary && !aIsScary) return 0;

                // [NEW] Investment Preference ("Look, a treasure chest!")
                if (a == CardType.Investment && b != CardType.Investment) return 0;
                if (b == CardType.Investment && a != CardType.Investment) return 1;

                // Kindergartner's Scoreboard
                float Score(CardType c)
                {
                    float baseScore = c switch
                    {
                        CardType.Cooperation => 100f, // Best
                        CardType.Investment  => 95f,  // [NEW] Treasure is good
                        CardType.Chaos       => 90f,  // Fun
                        CardType.Recon       => 70f,  // Toy
                        CardType.Doubt       => 60f,  // Safe
                        CardType.Interrupt   => 50f,
                        CardType.Pollution   => 40f,  // Yucky
                        CardType.Curse       => 10f,  // Scary
                        CardType.Sacrifice   => -50f, // Ouch
                        CardType.Betrayal    => -100f,// Very Scary
                        _ => 0f
                    };
                    // ★ [FIX] Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }

                float sa = Score(a) + UnityEngine.Random.Range(-10f, 10f); // Child's whim
                float sb = Score(b) + UnityEngine.Random.Range(-10f, 10f);

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
                        if (oppCard == CardType.Curse) return -2;      // 나 저주걸림(-2), 협력보상(+1) -> 손해
                        if (oppCard == CardType.Sacrifice) return +2;  // 상대 자해(-1), 나 협력(+1) -> 이득
                        if (oppCard == CardType.Pollution) return -1;  // 상대 자해(-1), 나 협력(+1) -> 이득
                        if (oppCard == CardType.Investment) return 0;
                        if (oppCard == CardType.Interrupt) return +2; // 방어 성공
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
                        if (oppCard == CardType.Investment) return +2;
                        if (oppCard == CardType.Doubt) return -1;
                        if (oppCard == CardType.Sacrifice) return +1;
                        if (oppCard == CardType.Betrayal) return -r + 2;
                        if (oppCard == CardType.Curse) return -1; // 저주걸림
                        if (oppCard == CardType.Interrupt) return -2; // 방어당함
                        return 0;

                    case CardType.Interrupt:
                        if (oppCard == CardType.Betrayal || oppCard == CardType.Pollution) return +2;
                        if (oppCard == CardType.Cooperation) return -2;
                        if (oppCard == CardType.Curse) return +2; // 방어 성공
                        return 0;

                    case CardType.Recon:
                        if (oppCard == CardType.Betrayal) return -(r + 1);
                        if (oppCard == CardType.Pollution) return - 1;
                        if (oppCard == CardType.Curse) return -2;
                        return 0;

                    case CardType.Chaos:
                        if (oppCard == CardType.Betrayal) return -(r + 1);
                        if (oppCard == CardType.Pollution) return - 1;
                        if (oppCard == CardType.Curse) return -2;
                        return 0;
            
                    case CardType.Investment:
                        if (oppCard == CardType.Cooperation) return 0; // 서로 +1
                        if (oppCard == CardType.Doubt) return +1;      // 나+1, 상0 (상대는 비용지불) -> 이득
                        if (oppCard == CardType.Betrayal) return -(r + 2); // 나 배신당함(-1), 상대 성공(+r) -> 큰 손해
                        if (oppCard == CardType.Curse) return -2;      // 나 저주걸림(-2), 협력보상(+1) -> 손해
                        if (oppCard == CardType.Sacrifice) return +2;  // 상대 자해(-1), 나 협력(+1) -> 이득
                        if (oppCard == CardType.Pollution) return -1;  // 상대 자해(-1), 나 협력(+1) -> 이득
                        if (oppCard == CardType.Investment) return 0;
                        if (oppCard == CardType.Interrupt) return +2; // 방어 성공
                        return +1;
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

        // Ryu Sung-woo v3 — Risk Manager: Data Analyst (Risk Hedging & Info Monopoly)
        static Agent Build_류성우(AgentList id)
        {
            var A = new Agent("류성우", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool losing = I.s.selfLife < I.s.oppLife;
                var history = I.HistoryOpponent();

                // 1. [Anomaly Control] Detect Opponent Sacrifice
                // "System Alert: Win probability threshold breached. Initiate forced termination."
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 2. Collect & Normalize Probability Data
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
                    { CardType.Sacrifice,   I.Ratio(CardType.Sacrifice)   },
                    { CardType.Investment,  I.Ratio(CardType.Investment)  } // [NEW]
                };
                float sum = p.Values.Sum(); 
                if (sum <= 0) sum = 1f; 
                foreach (var k in p.Keys.ToList()) p[k] /= sum;

                // 3. Combat Simulation (Delta: My Gain - Opp Gain)
                int Delta(CardType a, CardType b)
                {
                    int r = R;
                    
                    // [NEW] Investment Calculation: "Risk Hedging"
                    // Investment is successful if not attacked.
                    // *Note: Actual stack is unknown, assume value rises with round (r/2)
                    int investVal = Math.Max(1, r / 2); 

                    if (a == CardType.Investment)
                    {
                        if (b == CardType.Betrayal) return -r - 2; // Critical Loss
                        if (b == CardType.Pollution) return -2;
                        if (b == CardType.Doubt || b == CardType.Interrupt) return 0;
                        return investVal; // Success
                    }

                    // [Existing Logic]
                    if (a == CardType.Cooperation) {
                        if (b == CardType.Betrayal) return -(r + 1);
                        if (b == CardType.Doubt) return +1;
                        if (b == CardType.Curse) return -1; 
                        if (b == CardType.Sacrifice) return +2; 
                        if (b == CardType.Investment) return -1; // Opponent gains only
                        return 0; 
                    }
                    // ... (Omitted for brevity, assume standard logic for others) ...
                    if (a == CardType.Doubt) {
                        if (b == CardType.Betrayal) return r + 1;
                        if (b == CardType.Curse) return +1; 
                        if (b == CardType.Sacrifice) return +1;
                        return 0;
                    }
                    if (a == CardType.Betrayal) {
                        if (b == CardType.Cooperation) return r + 1;
                        if (b == CardType.Doubt) return -(r + 1);
                        if (b == CardType.Betrayal) return -2 * r; 
                        if (b == CardType.Curse) return r + 2; 
                        if (b == CardType.Sacrifice) return r + 2;
                        return 0;
                    }
                    if (a == CardType.Pollution) {
                        if (b == CardType.Cooperation) return +2;
                        if (b == CardType.Doubt) return -1;
                        if (b == CardType.Sacrifice) return +1;
                        return 0;
                    }
                    if (a == CardType.Curse) {
                        if (b == CardType.Doubt || b == CardType.Interrupt) return 0; 
                        if (b == CardType.Betrayal) return -1; 
                        return +2; 
                    }
                    if (a == CardType.Sacrifice) return -1; 

                    if (a == CardType.Recon) return 0.5f > 0 ? 1 : 0; // Info value

                    return 0; 
                }

                // 4. EV-based Selection
                var cand = I.hand.Distinct().Where(I.HandHas).ToList();
                CardType best = CardType.None; 
                float bestEV = float.NegativeInfinity;

                foreach (var a in cand)
                {
                    // Sacrifice is an outlier variable -> Exclude
                    if (a == CardType.Sacrifice) 
                    {
                         if (I.s.selfLife > 8) { }
                         else { continue; }
                    }

                    float ev = 0f;
                    foreach (var b in p.Keys) ev += p[b] * Delta(a, b);

                    // [Risk Manager] Context Adjustment
                    if (losing && (a == CardType.Betrayal || a == CardType.Pollution)) ev += 0.8f; // Aggression
                    if (!losing && (a == CardType.Doubt || a == CardType.Interrupt)) ev += 0.5f;   // Stability
                    
                    // [Recon Preference] "Data Monopoly"
                    if (a == CardType.Recon && R <= 5) ev += 1.5f;

                    // [Investment Correction]
                    // Invest boldly if attack probability is low (< 0.3)
                    if (a == CardType.Investment)
                    {
                        float attackProb = p[CardType.Betrayal] + p[CardType.Pollution];
                        if (attackProb < 0.3f) ev += 2.0f; 
                        else ev -= 10.0f; // High risk -> Discard
                    }

                    // ★ [FIX] Safe dictionary access
                    ev *= (weights.ContainsKey(a) ? weights[a] : 1.0f);
                    
                    if (ev > bestEV) { bestEV = ev; best = a; }
                }
                
                return best != CardType.None ? best : I.FirstOrNone();
            });

            // Selective Draw: "Data Collection (Recon) and Stability (Doubt) First"
            A.chooseFromTwo = (a, b, I) => {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                
                // [NEW] Investment: Pick for late game potential (only if safe)
                bool lateGame = R >= 6;

                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                float Score(CardType c) => (c switch {
                    CardType.Recon       => 95, // [Trait] Info Monopoly (Top)
                    CardType.Doubt       => 85, // [Trait] Risk Block
                    CardType.Pollution   => 80, // Efficient Dmg
                    CardType.Curse       => 75,
                    CardType.Interrupt   => 70,
                    CardType.Investment  => lateGame ? 65 : 20, // [NEW] Value shifts by time
                    CardType.Betrayal    => 60,
                    CardType.Cooperation => 40,
                    CardType.Chaos       => 10, // "Data Contamination" (Hate)
                    CardType.Sacrifice   => -99,
                    _ => 0
                // ★ [FIX] Safe dictionary access
                }) * (weights.ContainsKey(c) ? weights[c] : 1.0f);

                return Score(a) >= Score(b) ? 0 : 1;
            };

            A.fallback = new[] { 
                CardType.Recon, CardType.Doubt, 
                CardType.Pollution, CardType.Curse, CardType.Interrupt, 
                CardType.Betrayal, CardType.Investment, CardType.Cooperation 
            };
            return A;
        }

        // Seo Yu-ri v3 — Pattern Breaker: Anti-Repetition & Predictive Counter (Anomaly Creation with Investment)
        static Agent Build_서유리(AgentList id)
        {
            var A = new Agent("서유리", id);

            // [Internal Function] Counter Logic (Pattern Breaking)
            CardType GetCounter(CardType enemyCard, bool aggressive, DecisionInput I)
            {
                // 1. Counter Sacrifice: "Repetitive self-harm is boring."
                if (enemyCard == CardType.Sacrifice)
                {
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt; // Break flow
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;   // Punish
                    return CardType.Pollution;
                }

                // 2. [NEW] Counter Investment: "Money games are a pattern too."
                if (enemyCard == CardType.Investment)
                {
                    // Interrupt to ruin stack efficiency
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt;
                    // Betrayal to make investment fail
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal; 
                    return CardType.Pollution; // Pollute
                }

                // 3. Counter Curse
                if (enemyCard == CardType.Curse)
                {
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt; 
                    if (I.HandHas(CardType.Chaos)) return CardType.Chaos; // Flip table
                }

                // 4. Existing Counters
                if (enemyCard == CardType.Cooperation)
                {
                    if (aggressive && I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    return I.HandHas(CardType.Pollution) ? CardType.Pollution : CardType.Betrayal;
                }
                if (enemyCard == CardType.Doubt)
                {
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt; // Break shield
                    return CardType.Cooperation; // Induce waste
                }
                if (enemyCard == CardType.Betrayal)
                {
                    if (I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    return CardType.Interrupt; // Nullify attack
                }
                if (enemyCard == CardType.Chaos)
                {
                    return aggressive ? CardType.Pollution : CardType.Recon;
                }
                if (enemyCard == CardType.Pollution)
                {
                    if (I.HandHas(CardType.Doubt)) return CardType.Doubt;
                    return CardType.Interrupt;
                }
                if (enemyCard == CardType.Interrupt)
                {
                    return CardType.Cooperation; // Flexibility
                }
                
                return CardType.None;
            }

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                var history = I.HistoryOpponent();

                // 0. [Absolute Rule] No Self-Repetition (Pattern Breaker)
                CardType lastSelf = I.s.lastSelf;

                // 1. [Loop Breaker] Detect Sacrifice Loop
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt; 
                    if (I.HandHas(CardType.Chaos)) return CardType.Chaos;
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                }

                // 2. [Pattern Breaker] Predictive Counter (Core Ability)
                var targetOpp = I.opponentID;
                if (targetOpp != (AgentList)0)
                {
                    // Predict next move using Learning Data
                    var predicted = AgentManager.I.PredictNextCard(I.selfID, targetOpp, I.s);
                    
                    if (predicted.HasValue)
                    {
                        bool aggressive = I.s.selfLife >= I.s.oppLife;
                        var counter = GetCounter(predicted.Value, aggressive, I);
                        
                        if (counter != CardType.None && counter != lastSelf)
                            return counter;
                    }
                }

                // 3. [Loop Punishment] Punish Simple Repetition
                if (!I.s.IsFirst && I.s.lastOpp == I.s.last2Opp && I.s.lastOpp != CardType.None)
                {
                    var counter = GetCounter(I.s.lastOpp, true, I);
                    if (counter != CardType.None) return counter;
                }

                // 4. [NEW] Utilize Investment: "Anomaly Creation"
                // Use Investment not for healing, but to disrupt tempo when flow is boring (Defensive/Peaceful).
                if (I.HandHas(CardType.Investment))
                {
                    bool boringFlow = (I.s.lastOpp == CardType.Doubt || I.s.lastOpp == CardType.Cooperation);
                    // Avoid self-repetition, use if flow is stagnant
                    if (boringFlow && lastSelf != CardType.Investment)
                        return CardType.Investment;
                }

                // 5. Calculate Weighted Score
                float Score(CardType c)
                {
                    float baseScore = c switch {
                        CardType.Interrupt => 12,  // [Favorite] Break flow
                        CardType.Chaos => 10,      // [Favorite] Flip table
                        CardType.Curse => 9,       // New stimulus
                        CardType.Pollution => 7,
                        CardType.Betrayal => 6,
                        CardType.Recon => 5,       // For analysis
                        CardType.Investment => 4,  // [NEW] Anomaly tool
                        CardType.Cooperation => 3,
                        CardType.Doubt => 2,       // Hates passive defense
                        CardType.Sacrifice => -10, // Hates repetition
                        _ => 0
                    };

                    // [Self-Repetition Penalty]
                    if (c == lastSelf) baseScore -= 50;

                    // [Mirroring Penalty] Hates unoriginal moves
                    if (c == I.s.lastOpp) baseScore -= 5;

                    // ★ [FIX] Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }

                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            // --- Selective Draw (Draft) ---
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);

                // [Existing] Avoid Sacrifice
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                // [Self-Repetition Avoidance] Even in draft, avoid last played card
                if (a == I.s.lastSelf && b != I.s.lastSelf) return 1;
                if (b == I.s.lastSelf && a != I.s.lastSelf) return 0;

                float Score(CardType c)
                {
                    float s = c switch {
                        CardType.Interrupt => 100, // [Core]
                        CardType.Chaos => 95,      // [Core]
                        CardType.Curse => 90,
                        CardType.Pollution => 70,
                        CardType.Recon => 60,
                        CardType.Betrayal => 50,
                        CardType.Doubt => 30,
                        CardType.Investment => 25, // [NEW] Variable tool
                        CardType.Cooperation => 20,
                        CardType.Sacrifice => -100, 
                        _ => 0
                    };
                    // ★ [FIX] Safe dictionary access
                    return s * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }

                float sa = Score(a), sb = Score(b);
                // Random on tie (Erratic)
                if (Math.Abs(sa - sb) < 0.1f) return UnityEngine.Random.value < 0.5f ? 0 : 1;
                return sa > sb ? 0 : 1;
            };

            A.fallback = new[] { 
                CardType.Interrupt, CardType.Chaos, CardType.Curse, 
                CardType.Pollution, CardType.Recon, 
                CardType.Betrayal, CardType.Doubt, CardType.Investment, CardType.Cooperation, CardType.Sacrifice 
            };
            return A;
        }

        // Kang Eun-ho v3 — The Actuary: Controller Accountant (Variable Blocking & Safe Asset Management)
        static Agent Build_강은호(AgentList id)
        {
            var A = new Agent("강은호", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                var history = I.HistoryOpponent();

                // 0. [Risk Management] Detect Sacrifice -> Prevent Bankruptcy
                // "Warning: Asset loss threshold reached. Emergency liquidation."
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt; // Cut flow
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;   // Forced liquidation
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 1. [P&L Calculation] Q Function
                float P(CardType t) => I.Ratio(t);
                float Z = 0f;
                var types = (CardType[])Enum.GetValues(typeof(CardType));
                foreach(var t in types) if(t!=CardType.None) Z += P(t);
                if (Z <= 0) Z = 1f;
                float Q(CardType t) => P(t) / Z;

                // 2. [Stress Response] Hate Chaos (Handled in Score)

                // 3. [Safe Asset Preference] Early Game Scouting
                bool poorHand = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution) && !I.HandHas(CardType.Curse);
                if ((R <= 3 || poorHand) && I.HandHas(CardType.Recon))
                    return CardType.Recon;

                // 4. [Guaranteed Lethal] Closing the Balance Sheet
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && Q(CardType.Doubt) < 0.3f)
                    return CardType.Betrayal;

                // 5. [Crisis Management] Defense
                if (I.s.selfLife <= R && Q(CardType.Betrayal) >= 0.25f && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                // 6. [NEW] Utilize Investment: "Secure Safe Margin."
                // Only invest if expected return > risk.
                if (I.HandHas(CardType.Investment))
                {
                    // Calculate Risk: Opponent Attack + Interrupt probability
                    float riskProb = Q(CardType.Betrayal) + Q(CardType.Pollution) + Q(CardType.Interrupt);
                    
                    // Invest only if risk is very low (< 20%)
                    if (riskProb < 0.2f)
                        return CardType.Investment;
                }
                
                // 7. Utilize Curse: "Issue Long-term Debt."
                if (I.HandHas(CardType.Curse))
                {
                    float defProb = Q(CardType.Doubt) + Q(CardType.Interrupt);
                    if (defProb < 0.35f)
                        return CardType.Curse;
                }

                // 8. Valuation Function V
                float V(CardType c)
                {
                    float score = 0;
                    switch (c)
                    {
                        case CardType.Doubt:       score = 12.0f; break; // [Core] Defense First
                        case CardType.Interrupt:   score = 10.0f; break; // [Core] Control
                        case CardType.Pollution:   score = 7.0f; break;  // Stable Damage
                        case CardType.Recon:       score = 6.0f; break;  // Audit
                        case CardType.Curse:       score = 5.0f; break;  // Debt
                        case CardType.Investment:  score = 4.5f; break;  // [NEW] Safe Asset (Conditional)
                        case CardType.Cooperation: score = 4.0f; break;  // Low Variable
                        case CardType.Betrayal:    score = (I.s.oppLife <= R ? 15f : 3.0f); break; // Risky
                        case CardType.Chaos:       score = -20f; break; // [Hate] Unpredictable
                        case CardType.Sacrifice:   score = -30f; break; // [Hate] Loss
                    }
                    
                    // Risk Adjustment
                    if (c == CardType.Betrayal) score -= 5.0f * Q(CardType.Doubt); 
                    if (c == CardType.Curse)    score -= 4.0f * (Q(CardType.Doubt) + Q(CardType.Interrupt));
                    
                    // [NEW] Investment Risk Adjustment
                    if (c == CardType.Investment) score -= 10.0f * (Q(CardType.Betrayal) + Q(CardType.Pollution)); 

                    // ★ [FIX] Safe dictionary access
                    return score * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }

                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(V).FirstOrDefault();
            });

            // Selective Draw (Draft)
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);

                // [Existing] Avoid Sacrifice
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                // [Existing] Avoid Chaos
                if (a == CardType.Chaos && b != CardType.Chaos) return 1;
                if (b == CardType.Chaos && a != CardType.Chaos) return 0;

                float V(CardType c)
                {
                    float baseScore = c switch
                    {
                        CardType.Doubt       => 100f, // Safety First
                        CardType.Interrupt   => 90f,  // Control
                        CardType.Pollution   => 70f,
                        CardType.Recon       => 60f,
                        CardType.Curse       => 50f,
                        CardType.Investment  => 45f,  // [NEW] Safe Asset
                        CardType.Cooperation => 40f,
                        CardType.Betrayal    => 30f,  // Risky
                        CardType.Chaos       => -100f,
                        CardType.Sacrifice   => -200f,
                        _ => 0f
                    };
                    // ★ [FIX] Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1.0f);
                }
                
                float va = V(a), vb = V(b);
                
                // Tie-breaker: Pick Safer Card
                if (Math.Abs(va - vb) < 0.1f)
                {
                     int Safety(CardType t) => t switch { 
                         CardType.Doubt=>5, CardType.Interrupt=>4, CardType.Investment=>3, 
                         CardType.Recon=>2, _=>0 
                     };
                     return Safety(a) >= Safety(b) ? 0 : 1;
                }
                return va >= vb ? 0 : 1;
            };

            A.fallback = new[] { 
                CardType.Doubt, CardType.Interrupt, 
                CardType.Pollution, CardType.Recon, CardType.Curse, 
                CardType.Investment,
                CardType.Cooperation, CardType.Betrayal, 
                CardType.Chaos, CardType.Sacrifice 
            };
            return A;
        }

        // Jeon A-ram v3 — Info Hunter: Information Predator (Recon Combo & Confirmed Kill with Investment)
        static Agent Build_전아람(AgentList id)
        {
            var A = new Agent("전아람", id);

            A.rules.Add(I =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool nf = !I.s.IsFirst;
                var history = I.HistoryOpponent();

                // 1. [Terror Suppression] Detect Opponent Sacrifice
                // "Intel received: Opponent preparing suicide attack (Sacrifice). Neutralize immediately."
                int oppSacCount = history.Count(c => c == CardType.Sacrifice);
                if (oppSacCount >= 3)
                {
                    if (I.HandHas(CardType.Betrayal)) return CardType.Betrayal;
                    if (I.HandHas(CardType.Interrupt)) return CardType.Interrupt; // Disrupt stack
                    if (I.HandHas(CardType.Pollution)) return CardType.Pollution;
                }

                // 2. [Intel Gathering] Priority on Early Recon
                // If weak attack or early game, focus on intel
                bool poorHand = !I.HandHas(CardType.Betrayal) && !I.HandHas(CardType.Pollution);
                if ((R <= 4 || poorHand) && I.HandHas(CardType.Recon))
                    return CardType.Recon;

                // 3. Predict Opponent Hand (Intel Analysis)
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
                    { CardType.Sacrifice,   I.Ratio(CardType.Sacrifice)   },
                    { CardType.Investment,  I.Ratio(CardType.Investment)  } // [NEW]
                };
                float sum = p.Values.Sum(); if (sum <= 0) sum = 1f;
                foreach (var k in p.Keys.ToList()) p[k] /= sum;

                // 4. [NEW] Utilize Investment: "Building Intelligence Network"
                // Jeon A-ram does not make uncertain investments.
                // Invest only if she knows the opponent's hand (via Recon) or attack probability is very low.
                if (I.HandHas(CardType.Investment))
                {
                    // If I used Recon last turn (I know their hand) OR attack risk is low
                    // (Simplified check using lastSelf == Recon)
                    if (nf && I.s.lastSelf == CardType.Recon)
                    {
                        // If opponent's estimated hand has low attack probability
                        float risk = p[CardType.Betrayal] + p[CardType.Pollution];
                        if (risk < 0.2f) return CardType.Investment;
                    }
                }

                // 5. [Sadistic Control] Utilize Curse: "Confirmed Kill"
                if (I.HandHas(CardType.Curse))
                {
                    float defProb = p[CardType.Doubt] + p[CardType.Interrupt];
                    
                    // If I used Recon or opponent defense chance is very low (< 20%) -> Curse
                    bool informationSuperiority = (nf && I.s.lastSelf == CardType.Recon) || defProb < 0.2f;
                    
                    if (informationSuperiority)
                        return CardType.Curse;
                }

                // 6. Lethal / Crisis Management
                if (I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.35f)
                    return CardType.Betrayal;
                
                if (I.s.selfLife <= R && p[CardType.Betrayal] >= 0.28f && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                // 7. [Strategic Assessment] Score Function
                CardType avoid = I.s.lastSelf; // Avoid repeating patterns to hide info
                int r = R;

                float Score(CardType a)
                {
                    float e = 0;
                    // Sacrifice is taboo (Trait: Avoid self-harm)
                    if (a == CardType.Sacrifice) return -99f; 

                    foreach (var kv in p)
                    {
                        var b = kv.Key; 
                        float q = kv.Value; 
                        float d = 0;

                        // Existing Interactions
                        if (a == CardType.Betrayal && b == CardType.Cooperation) d = r + 1;
                        else if (a == CardType.Pollution && b == CardType.Cooperation) d = +2;
                        else if (a == CardType.Doubt && b == CardType.Betrayal) d = r + 1;
                        else if (a == CardType.Interrupt && (b == CardType.Betrayal || b == CardType.Pollution)) d = +2;
                        else if (a == CardType.Cooperation && b == CardType.Betrayal) d = -(r + 1);
                        
                        // Curse Interaction
                        else if (a == CardType.Curse)
                        {
                            if (b == CardType.Doubt || b == CardType.Interrupt) d = -1; 
                            else if (b == CardType.Betrayal) d = -1; 
                            else d = +2; 
                        }
                        // [NEW] Investment Interaction (Simplified)
                        else if (a == CardType.Investment)
                        {
                            if (b == CardType.Betrayal || b == CardType.Pollution) d = -r; // Fail if attacked
                            else if (b == CardType.Doubt) d = 0; // Blocked
                            else d = 2; // Assume gain on success
                        }
                        
                        e += q * d;
                    }
                    
                    // Bonus for hiding info (changing cards)
                    if (a != avoid) e += 0.2f;

                    // ★ [FIX] Safe dictionary access
                    e *= (weights.ContainsKey(a) ? weights[a] : 1.0f);
                    return e;
                }

                return I.hand.Distinct().Where(I.HandHas).OrderByDescending(Score).FirstOrDefault();
            });

            // Selective Draw (Draft)
            A.chooseFromTwo = (a, b, I) =>
            {
                var weights = AgentManager.I.GetWeights(I.selfID);
                int R = Math.Max(1, I.s.round);
                bool losing = I.s.selfLife < I.s.oppLife;

                // [Existing] Absolute Avoidance of Sacrifice
                if (a == CardType.Sacrifice && b != CardType.Sacrifice) return 1;
                if (b == CardType.Sacrifice && a != CardType.Sacrifice) return 0;

                // [NEW] Investment: "Take it only when certain."
                // Low priority, but taken if situation allows.

                float V(CardType c)
                {
                    float baseScore = 0;
                    switch (c)
                    {
                        case CardType.Recon:       baseScore = 2.0f; break; // [Core] Info Priority
                        case CardType.Curse:       baseScore = 1.5f; break; // Sadistic Preference
                        case CardType.Pollution:   baseScore = 1.4f; break;
                        case CardType.Betrayal:    baseScore = (I.s.oppLife <= R ? 3.0f : 1.2f); break;
                        case CardType.Doubt:       baseScore = 1.2f; break;
                        case CardType.Interrupt:   baseScore = 1.1f; break;
                        case CardType.Investment:  baseScore = 0.9f; break; // [NEW] Cautious approach
                        case CardType.Cooperation: baseScore = 0.5f; break; // Distrust
                        case CardType.Chaos:       baseScore = 0.1f; break; // Hates info contamination
                        case CardType.Sacrifice:   baseScore = -99f; break; // Never
                    }
                    // ★ [FIX] Safe dictionary access
                    return baseScore * (weights.ContainsKey(c) ? weights[c] : 1f);
                }
                
                float va = V(a) + (I.s.lastSelf != a ? 0.2f : 0f); // Avoid last card
                float vb = V(b) + (I.s.lastSelf != b ? 0.2f : 0f);
                
                if (Math.Abs(va - vb) < 0.01f) return UnityEngine.Random.value < 0.5f ? 0 : 1;
                return va >= vb ? 0 : 1;
            };

            A.fallback = new[] { 
                CardType.Recon, CardType.Curse, CardType.Pollution, 
                CardType.Doubt, CardType.Betrayal, CardType.Interrupt, 
                CardType.Investment, CardType.Cooperation, CardType.Chaos, CardType.Sacrifice 
            };
            return A;
        }
    }
}