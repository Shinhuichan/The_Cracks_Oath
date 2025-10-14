// File: GameCore.cs
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

namespace GameCore
{
    public enum CardType { None = 0, Cooperation, Doubt, Betrayal, Chaos, Pollution, Interrupt }

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
                {CardType.Chaos,0},{CardType.Pollution,0},{CardType.Interrupt,0}
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
            { CardType.Cooperation, CardType.Doubt, CardType.Pollution, CardType.Betrayal, CardType.Chaos, CardType.Interrupt };

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
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Cooperation
                            && I.s.selfLife >= I.s.oppLife
                            && I.HandHas(CardType.Interrupt) ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution
                            && !I.HandHas(CardType.Doubt)
                            && I.HandHas(CardType.Interrupt) ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => I.Ratio(CardType.Pollution) >= 0.25f && I.HandHas(CardType.Doubt) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Chaos && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Cooperation
                            && I.s.selfLife >= I.s.oppLife
                            && I.Ratio(CardType.Doubt) < 0.20f
                            && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => I.s.selfLife <= 4 && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.fallback = new[] { CardType.Cooperation, CardType.Doubt, CardType.Interrupt, CardType.Pollution, CardType.Betrayal, CardType.Chaos };
            return A;
        }

        // 이수진
        static Agent Build_이수진()
        {
            var A = new Agent("이수진");
            A.rules.Add(I => I.s.round <= 3 && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.s.round >= 3
                            && I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation
                            && I.s.selfLife >= I.s.oppLife - 1
                            && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst
                            && I.s.lastOpp == CardType.Cooperation
                            && I.HandHas(CardType.Interrupt) ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && !I.HandHas(CardType.Doubt) && I.HandHas(CardType.Interrupt) ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Chaos && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.hand.Count(t => t == CardType.Cooperation) >= 2
                            && (I.s.round % 3 == 0 || I.s.round >= 7)
                            && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => I.s.selfLife >= I.s.oppLife && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.s.selfLife < I.s.oppLife && I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => I.s.round % 3 == 0 && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.fallback = new[] { CardType.Pollution, CardType.Interrupt, CardType.Betrayal, CardType.Doubt, CardType.Cooperation, CardType.Chaos };
            return A;
        }

        // 최용호
        static Agent Build_최용호()
        {
            var A = new Agent("최용호");
            A.rules.Add(I => I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Cooperation && I.HandHas(CardType.Betrayal)
                            ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Interrupt)
                            ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => (I.s.round >= 6 || I.s.oppLife <= 4) && I.HandHas(CardType.Betrayal)
                            ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt)
                            ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => I.s.selfLife <= 4 && I.HandHas(CardType.Chaos)
                            ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => !I.HandHas(CardType.Pollution) && !I.HandHas(CardType.Betrayal) && I.HandHas(CardType.Chaos)
                            ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Interrupt) ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.fallback = new[] { CardType.Pollution, CardType.Interrupt, CardType.Betrayal, CardType.Chaos, CardType.Doubt, CardType.Cooperation };
            return A;
        }

        // 한지혜
        static Agent Build_한지혜()
        {
            var A = new Agent("한지혜");
            A.rules.Add(I => I.s.round <= 2 && I.HandHas(CardType.Cooperation)
                            ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Interrupt)
                            ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt)
                            ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => I.s.selfLife <= I.s.oppLife + 1 && I.HandHas(CardType.Cooperation)
                            ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.selfLife >= I.s.oppLife
                            && I.s.lastOpp == CardType.Cooperation
                            && I.Ratio(CardType.Doubt) >= 0.25f
                            && I.HandHas(CardType.Pollution)
                            ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.selfLife >= I.s.oppLife
                            && I.s.lastOpp == CardType.Cooperation
                            && !I.HandHas(CardType.Pollution)
                            && I.HandHas(CardType.Interrupt)
                            ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => (I.s.selfLife <= 4 || (I.s.round % 3 == 0)) && I.HandHas(CardType.Chaos)
                            ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => I.s.round >= 8 && !I.s.IsFirst
                            && I.s.lastOpp == CardType.Cooperation
                            && I.s.selfLife >= I.s.oppLife
                            && I.HandHas(CardType.Betrayal)
                            ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => (I.Ratio(CardType.Cooperation) >= 0.33f || I.Ratio(CardType.Pollution) >= 0.33f)
                            && I.Ratio(CardType.Doubt) < 0.20f
                            && I.HandHas(CardType.Interrupt)
                            ? CardType.Interrupt : (CardType?)null);
            A.fallback = new[] { CardType.Cooperation, CardType.Doubt, CardType.Pollution, CardType.Interrupt, CardType.Betrayal, CardType.Chaos };
            return A;
        }

        // 박민재
        static Agent Build_박민재()
        {
            var A = new Agent("박민재");
            A.rules.Add(I =>
                I.Ratio(CardType.Cooperation) >= 0.38f &&
                I.Ratio(CardType.Doubt) < 0.22f &&
                I.HandHas(CardType.Doubt) &&                 // 커버 카드 확보
                I.HandHas(CardType.Betrayal)
                    ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I =>
                !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Interrupt)
                    ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I =>
                !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt)
                    ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I =>
                I.Ratio(CardType.Pollution) >= 0.16f && I.HandHas(CardType.Interrupt)
                    ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I =>
                I.Ratio(CardType.Pollution) >= 0.16f && I.HandHas(CardType.Doubt)
                    ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I =>
                (!I.s.IsFirst && I.s.lastOpp == CardType.Chaos) ||
                I.Ratio(CardType.Cooperation) >= 0.33f
                    ? (I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null)
                    : (CardType?)null);
            A.rules.Add(I =>
                !I.s.IsFirst && I.s.lastOpp == CardType.Cooperation &&
                I.Ratio(CardType.Doubt) >= 0.33f && I.HandHas(CardType.Cooperation)
                    ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I =>
                !I.s.IsFirst && I.s.lastOpp == CardType.Cooperation &&
                I.s.last2Opp == CardType.Cooperation &&
                I.HandHas(CardType.Betrayal)
                    ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I =>
                !I.s.IsFirst && I.s.lastOpp == CardType.Cooperation &&
                I.s.last2Opp == CardType.Cooperation &&
                I.HandHas(CardType.Pollution)
                    ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I =>
                I.Ratio(CardType.Doubt) >= 0.25f && I.s.selfLife <= 4 &&
                I.HandHas(CardType.Doubt)
                    ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I =>
                (!I.HandHas(CardType.Betrayal) && I.s.selfLife >= 2 || I.s.round % 3 == 0) &&
                I.HandHas(CardType.Chaos)
                    ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I =>
                (I.Ratio(CardType.Cooperation) >= 0.33f ||
                (!I.s.IsFirst && I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation)) &&
                I.Ratio(CardType.Doubt) < 0.20f &&
                I.HandHas(CardType.Interrupt)
                    ? CardType.Interrupt : (CardType?)null);
            A.fallback = new[]
            {
                CardType.Betrayal, CardType.Interrupt, CardType.Cooperation,
                CardType.Pollution, CardType.Doubt, CardType.Chaos
            };
            return A;
        }

        // 정다은
        static Agent Build_정다은()
        {
            var A = new Agent("정다은");
            CardType Counter(CardType o, int round, DecisionInput I)
            {
                if (o == CardType.Cooperation)
                    return I.HandHas(CardType.Betrayal) ? CardType.Betrayal
                        : I.HandHas(CardType.Interrupt) ? CardType.Interrupt
                        : I.HandHas(CardType.Pollution) ? CardType.Pollution
                        : I.HandHas(CardType.Doubt) ? CardType.Doubt
                        : (I.HandHas(CardType.Cooperation) ? CardType.Cooperation : CardType.None);
                if (o == CardType.Doubt)
                    return I.HandHas(CardType.Cooperation) ? CardType.Cooperation : CardType.None;
                if (o == CardType.Betrayal)
                    return I.HandHas(CardType.Doubt) ? CardType.Doubt
                        : I.HandHas(CardType.Interrupt) ? CardType.Interrupt
                        : (I.HandHas(CardType.Cooperation) ? CardType.Cooperation : CardType.None);
                if (o == CardType.Chaos)
                    return I.HandHas(CardType.Betrayal) ? CardType.Betrayal
                        : I.HandHas(CardType.Pollution) ? CardType.Pollution
                        : (I.HandHas(CardType.Cooperation) ? CardType.Cooperation : CardType.None);
                if (o == CardType.Pollution)
                    return I.HandHas(CardType.Interrupt) ? CardType.Interrupt
                        : (I.HandHas(CardType.Doubt) ? CardType.Doubt : CardType.None);
                if (o == CardType.Interrupt)
                    return I.HandHas(CardType.Cooperation) ? CardType.Cooperation
                        : I.HandHas(CardType.Betrayal) ? CardType.Betrayal
                        : (I.HandHas(CardType.Pollution) ? CardType.Pollution : CardType.None);
                return CardType.None;
            }
            A.rules.Add(I => I.s.round == 1 && I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => I.s.round >= 4 && I.s.OppNoDoubtInLast3() && I.s.selfLife >= I.s.oppLife && I.HandHas(CardType.Betrayal)
                            ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Interrupt)
                            ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && (I.s.lastOpp == CardType.Pollution || I.s.lastOpp != CardType.Cooperation) && I.HandHas(CardType.Doubt)
                            ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I =>
            {
                if (!I.s.IsFirst && I.s.lastOpp == I.s.last2Opp)
                {
                    var c = Counter(I.s.lastOpp, I.s.round, I);
                    if (c != CardType.None && I.HandHas(c)) return c;
                }
                return (CardType?)null;
            });
            A.rules.Add(I => ((I.s.round % 3 == 0) || I.s.selfLife >= 2) && !I.HandHas(CardType.Betrayal) && I.HandHas(CardType.Chaos)
                            ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => I.s.round >= 3 &&
                            (!I.s.IsFirst && (I.s.lastOpp == CardType.Cooperation || I.s.last2Opp == CardType.Cooperation)) &&
                            I.HandHas(CardType.Pollution)
                            ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.s.oppLife <= 3 && I.Ratio(CardType.Doubt) < 0.25f && I.HandHas(CardType.Betrayal)
                            ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => I.Ratio(CardType.Pollution) >= 0.35f && I.HandHas(CardType.Doubt)
                            ? CardType.Doubt : (CardType?)null);
            A.fallback = new[] { CardType.Pollution, CardType.Doubt, CardType.Cooperation, CardType.Betrayal, CardType.Chaos };
            return A;
        }

        // 오태훈
        static Agent Build_오태훈()
        {
            var A = new Agent("오태훈");
            A.rules.Add(I => I.s.round <= 2 && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.s.round >= 3
                            && I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation
                            && I.s.selfLife >= I.s.oppLife - 1
                            && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => (I.Ratio(CardType.Cooperation) >= 0.50f || I.s.lastOpp == CardType.Cooperation)
                            && I.Ratio(CardType.Doubt) < 0.20f
                            && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst
                            && (I.s.lastOpp == CardType.Pollution || I.Ratio(CardType.Pollution) >= 0.35f)
                            && I.HandHas(CardType.Doubt) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => I.hand.Count(t => t == CardType.Cooperation) >= 2
                            && (I.s.selfLife >= I.s.oppLife || I.s.round % 3 == 0)
                            && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst
                            && I.s.selfLife >= I.s.oppLife
                            && I.s.lastOpp != CardType.Doubt
                            && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => (I.s.lastOpp == CardType.Cooperation || I.s.last2Opp == CardType.Cooperation
                            || I.Ratio(CardType.Cooperation) >= 0.40f)
                            && I.HandHas(CardType.Interrupt) ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => (I.s.lastOpp == CardType.Betrayal || (I.s.oppLife <= 4 && I.Ratio(CardType.Cooperation) >= 0.33f))
                            && I.HandHas(CardType.Interrupt) ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => (I.s.lastOpp == CardType.Pollution)
                            && I.HandHas(CardType.Interrupt)
                            && !I.HandHas(CardType.Doubt) ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => I.s.oppLife <= 3 && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => I.s.oppLife <= 3
                            && (I.s.lastOpp == CardType.Cooperation || I.s.lastOpp == CardType.Pollution || I.s.lastOpp == CardType.Betrayal)
                            && I.HandHas(CardType.Interrupt) ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => (I.s.round % 2 == 1) && !I.s.IsFirst && I.HandHas(I.s.lastOpp) ? I.s.lastOpp : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.fallback = new[] { CardType.Pollution, CardType.Betrayal, CardType.Interrupt, CardType.Doubt, CardType.Cooperation, CardType.Chaos };
            return A;
        }

        // 유민정
        static Agent Build_유민정()
        {
            var A = new Agent("유민정");
            A.rules.Add(I => !I.s.IsFirst
                            && I.s.lastOpp == CardType.Pollution
                            && I.HandHas(CardType.Doubt) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => I.s.selfLife < I.s.oppLife
                            && I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst
                            && I.s.lastOpp == CardType.Cooperation
                            && I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst
                            && I.s.selfLife > I.s.oppLife
                            && I.s.lastOpp == CardType.Cooperation
                            && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst
                            && I.s.lastOpp == I.s.last2Opp
                            && I.HandHas(I.s.lastOpp) ? I.s.lastOpp : (CardType?)null);
            A.rules.Add(I => I.s.round >= 9
                            && !I.s.IsFirst
                            && I.s.lastOpp == CardType.Cooperation
                            && I.s.selfLife >= I.s.oppLife
                            && I.Ratio(CardType.Doubt) < 0.20f
                            && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => I.s.round >= 6
                            && I.s.lastOpp != I.s.last2Opp
                            && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => ( (I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation)
                                || I.Ratio(CardType.Cooperation) >= 0.40f )
                            && I.HandHas(CardType.Interrupt) ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst
                            && I.s.lastOpp == CardType.Pollution
                            && !I.HandHas(CardType.Doubt)
                            && I.HandHas(CardType.Interrupt) ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.fallback = new[] { CardType.Doubt, CardType.Cooperation, CardType.Pollution, CardType.Interrupt, CardType.Betrayal, CardType.Chaos };
            return A;
        }

        // 김태양
        static Agent Build_김태양()
        {
            var A = new Agent("김태양");
            A.rules.Add(I =>
            {
                if (I.s.round == 1 && I.hand.Count > 0)
                    return I.hand[UnityEngine.Random.Range(0, I.hand.Count)];
                return (CardType?)null;
            });
            A.rules.Add(I => !I.s.IsFirst
                            && (I.s.lastOpp == CardType.Pollution || I.s.lastOpp == CardType.Betrayal)
                            && I.HandHas(CardType.Interrupt) ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => I.s.round >= 3
                            && I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation
                            && I.s.selfLife >= I.s.oppLife
                            && I.HandHas(CardType.Betrayal) ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => I.s.round != 1
                            && (I.s.lastOpp == CardType.Cooperation || (I.s.round % 2 == 0))
                            && I.HandHas(CardType.Pollution) ? CardType.Pollution : (CardType?)null);
            A.rules.Add(I => I.s.round % 3 == 0 && I.HandHas(CardType.Chaos) ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I =>
            {
                if (I.HandHas(CardType.Betrayal) && I.HandHas(CardType.Pollution))
                    return UnityEngine.Random.Range(0, 2) == 0 ? CardType.Betrayal : CardType.Pollution;
                return (CardType?)null;
            });
            A.rules.Add(I =>
            {
                if (!I.s.IsFirst && I.HandHas(I.s.lastOpp))
                    return I.s.lastOpp;
                return (CardType?)null;
            });
            A.rules.Add(I => !I.s.IsFirst
                            && I.s.lastOpp == CardType.Pollution
                            && I.HandHas(CardType.Doubt) ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I =>
            {
                bool oppSafe = (!I.s.IsFirst && I.s.lastOpp == CardType.Doubt)
                            || (I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation);
                if (oppSafe && I.HandHas(CardType.Cooperation)) return CardType.Cooperation;
                return (CardType?)null;
            });
            A.rules.Add(I =>
            {
                if (UnityEngine.Random.value < 0.10f && I.hand.Count > 0)
                    return I.hand[UnityEngine.Random.Range(0, I.hand.Count)];
                return (CardType?)null;
            });
            A.rules.Add(I => I.HandHas(CardType.Cooperation) ? CardType.Cooperation : (CardType?)null);
            A.fallback = new[] { CardType.Betrayal, CardType.Pollution, CardType.Interrupt, CardType.Doubt, CardType.Cooperation, CardType.Chaos };
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
                    // 연속 협력 읽힘 → 협력 확률 가중
                    if (I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation) Boost(CardType.Cooperation, 1.7f);
                    // 직전 오염 반복 경향
                    if (I.s.lastOpp == CardType.Pollution) Boost(CardType.Pollution, 1.5f);
                    // 직전 의심은 연속 약화
                    if (I.s.lastOpp == CardType.Doubt) Boost(CardType.Doubt, 1.2f);
                    // 3의 배수 라운드 혼돈 미세 가중
                    if (I.s.round % 3 == 0) Boost(CardType.Chaos, 1.12f);
                }
                // 체력 상황 보정
                if (I.s.oppLife <= 3) { Boost(CardType.Cooperation, 1.15f); Boost(CardType.Pollution, 1.08f); }
                if (I.s.selfLife <= 3) { Boost(CardType.Doubt, 1.10f); Boost(CardType.Chaos, 1.08f); }

                // 오염을 자주 쓰는 메타에는 상대 Interrupt 가능성 소폭 가중
                Boost(CardType.Interrupt, 1.0f + 0.4f * I.Ratio(CardType.Pollution));

                float sum = p.Values.Sum(); if (sum <= 0f) sum = 1f;
                foreach (var k in p.Keys.ToList()) p[k] /= sum;

                // ===== 1) 즉시 전술 =====
                int R = Math.Max(1, I.s.round);

                // 1-1) 직전 Pollution/Betrayal에는 Interrupt 최우선(확정 우위)
                if (!I.s.IsFirst && (I.s.lastOpp == CardType.Pollution || I.s.lastOpp == CardType.Betrayal) && I.HandHas(CardType.Interrupt))
                    return CardType.Interrupt;

                // 1-2) 직전 Pollution에는 Doubt도 우위. Interrupt 없으면 Doubt
                if (!I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt))
                    return CardType.Doubt;

                // 1-3) 킬각: Betrayal로 즉사 가능(상대 체력 ≤ R)
                bool lethal = I.HandHas(CardType.Betrayal) && I.s.oppLife <= R && p[CardType.Doubt] < 0.30f;
                if (lethal) return CardType.Betrayal;

                // 1-4) 생존: 상대 배신 확률 높고 내 체력 ≤ R → Doubt로 막기
                if (I.HandHas(CardType.Doubt) && I.s.selfLife <= R && p[CardType.Betrayal] >= 0.28f)
                    return CardType.Doubt;

                // 1-5) 협력 연속 읽힘 + 의심 낮음 → Betrayal
                if (I.HandHas(CardType.Betrayal)
                    && (!I.s.IsFirst && I.s.lastOpp == CardType.Cooperation && I.s.last2Opp == CardType.Cooperation
                        || I.s.oppLife <= R + 1)
                    && p[CardType.Doubt] < 0.33f)
                    return CardType.Betrayal;

                // 1-6) 협력 성향↑ 또는 직전 혼돈 → Pollution
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
                        else if (c == CardType.Interrupt) s += 1;   // 안정적 카운터
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
                    // Cooperation
                    if (a == CardType.Cooperation && b == CardType.Cooperation) return 0;
                    if (a == CardType.Cooperation && b == CardType.Doubt) return +1;
                    if (a == CardType.Cooperation && b == CardType.Betrayal) return -(R + 1);
                    if (a == CardType.Cooperation && b == CardType.Chaos) return +1;
                    if (a == CardType.Cooperation && b == CardType.Pollution) return -2;
                    if (a == CardType.Cooperation && b == CardType.Interrupt) return -2; // 상대 I에 취약(+1/-1 → Δ-2)

                    // Doubt
                    if (a == CardType.Doubt && b == CardType.Cooperation) return -1;
                    if (a == CardType.Doubt && b == CardType.Doubt) return 0;
                    if (a == CardType.Doubt && b == CardType.Betrayal) return R + 1;
                    if (a == CardType.Doubt && b == CardType.Chaos) return 0;
                    if (a == CardType.Doubt && b == CardType.Pollution) return +1;
                    if (a == CardType.Doubt && b == CardType.Interrupt) return +1; // 0,-1

                    // Betrayal
                    if (a == CardType.Betrayal && b == CardType.Cooperation) return R + 1;
                    if (a == CardType.Betrayal && b == CardType.Doubt) return -(R + 1);
                    if (a == CardType.Betrayal && b == CardType.Betrayal) return -2 * R;
                    if (a == CardType.Betrayal && b == CardType.Chaos) return R + 1;
                    if (a == CardType.Betrayal && b == CardType.Pollution) return R + 1;
                    if (a == CardType.Betrayal && b == CardType.Interrupt) return -(2); // -1,+1 → Δ-2

                    // Chaos
                    if (a == CardType.Chaos && b == CardType.Cooperation) return -1;
                    if (a == CardType.Chaos && b == CardType.Doubt) return 0;
                    if (a == CardType.Chaos && b == CardType.Betrayal) return -(R + 1);
                    if (a == CardType.Chaos && b == CardType.Chaos) return 0;
                    if (a == CardType.Chaos && b == CardType.Pollution) return 0;
                    if (a == CardType.Chaos && b == CardType.Interrupt) return +1; // 0,-1

                    // Pollution
                    if (a == CardType.Pollution && b == CardType.Cooperation) return +2;
                    if (a == CardType.Pollution && b == CardType.Doubt) return -1;
                    if (a == CardType.Pollution && b == CardType.Betrayal) return -(R + 1);
                    if (a == CardType.Pollution && b == CardType.Chaos) return 0;
                    if (a == CardType.Pollution && b == CardType.Pollution) return 0;
                    if (a == CardType.Pollution && b == CardType.Interrupt) return -2; // -1,+1

                    // Interrupt
                    if (a == CardType.Interrupt && b == CardType.Cooperation) return +2; // +1,-1
                    if (a == CardType.Interrupt && b == CardType.Doubt) return -1;      // -1,0
                    if (a == CardType.Interrupt && b == CardType.Betrayal) return +2;   // +1,-1
                    if (a == CardType.Interrupt && b == CardType.Chaos) return -1;      // -1,0(reset)
                    if (a == CardType.Interrupt && b == CardType.Pollution) return +2;  // +1,-1
                    if (a == CardType.Interrupt && b == CardType.Interrupt) return 0;   // 0,0

                    return 0;
                }

                var choices = I.hand.Distinct().Where(I.HandHas).ToList();
                CardType best = CardType.None; float bestEV = float.NegativeInfinity;

                foreach (var a in choices)
                {
                    float ev = 0f; foreach (var b in p.Keys) ev += p[b] * Delta(a, b);

                    // 생존 리스크 페널티(배신 노출)
                    if (I.s.selfLife <= R)
                    {
                        ev -= p[CardType.Betrayal] * 3.5f;
                        if (a == CardType.Pollution) ev -= p[CardType.Betrayal] * 3.5f;
                    }

                    // 손패가 나쁠수록 혼돈 가치 가산
                    if (a == CardType.Chaos && hs <= 2) ev += 2.0f;

                    // 막판 킬각 가중
                    if (a == CardType.Betrayal && I.s.oppLife <= R + 1) ev += p[CardType.Cooperation] * 3.0f;

                    if (ev > bestEV) { bestEV = ev; best = a; }
                }

                // 안전장치: 의심 확률↑ + 체력 낮음 → 배신 회피
                if (best == CardType.Betrayal && p[CardType.Doubt] >= 0.30f && I.s.selfLife <= R + 1)
                {
                    var alt = choices.Where(t => t != CardType.Betrayal)
                                    .OrderByDescending(t =>
                                    {
                                        float ev = 0f; foreach (var b in p.Keys) ev += p[b] * Delta(t, b); return ev;
                                    }).FirstOrDefault();
                    if (alt != CardType.None) best = alt;
                }

                // 읽힘 방지 소량 랜덤화
                if (UnityEngine.Random.value < 0.07f)
                {
                    var mix = choices.Where(t => t != best).ToList();
                    if (mix.Count > 0) best = mix[UnityEngine.Random.Range(0, mix.Count)];
                }

                return best;
            });

            // Interrupt를 기본 옵션에도 포함
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
            A.rules.Add(I => I.s.round == 1 && I.HandHas(CardType.Cooperation)
                ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.HandHas(I.s.lastOpp)
                ? I.s.lastOpp : (CardType?)null);
            A.rules.Add(I => I.s.round % 2 == 0 && I.HandHas(CardType.Chaos)
                ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Interrupt)
                ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Pollution && I.HandHas(CardType.Doubt)
                ? CardType.Doubt : (CardType?)null);
            A.rules.Add(I => !I.s.IsFirst && I.s.lastOpp == CardType.Betrayal && I.HandHas(CardType.Interrupt)
                ? CardType.Interrupt : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation, 2) && I.HandHas(CardType.Chaos)
                ? CardType.Chaos : (CardType?)null);
            A.rules.Add(I => I.s.selfLife <= I.s.oppLife - 2 && I.HandHas(CardType.Cooperation)
                ? CardType.Cooperation : (CardType?)null);
            A.rules.Add(I => I.s.round >= 8 && !I.HandHas(CardType.Pollution) && !I.HandHas(CardType.Doubt)
                            && I.HandHas(CardType.Betrayal)
                ? CardType.Betrayal : (CardType?)null);
            A.rules.Add(I => I.HandHas(CardType.Cooperation)
                ? CardType.Cooperation : (CardType?)null);
            A.fallback = new[] { CardType.Cooperation, CardType.Doubt, CardType.Interrupt, CardType.Pollution, CardType.Betrayal, CardType.Chaos };
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
        public int cooperationCount = 20, doubtCount = 20, betrayalCount = 3, chaosCount = 7, pollutionCount = 10, interruptCount = 6;

        [Header("게임 설정")]
        public int startingHand = 3, startLife = 10;
        public int playerILife, playerIILife;
        public bool playerILost { get; private set; }
        public bool playerIILost { get; private set; }

        // --- round-scaled effect support ---
        struct Effect
        {
            public int self, opp;                 // constant delta
            public bool repSelf, repOpp;          // hand reset flags
            public bool selfUseRound, oppUseRound;// if true, use ±(current round) with sign of self/opp

            public Effect(int s, int o, bool rs=false, bool ro=false, bool sr=false, bool ornd=false)
            { self = s; opp = o; repSelf = rs; repOpp = ro; selfUseRound = sr; oppUseRound = ornd; }
        }
        Dictionary<string, Effect> E;

        // 내부 라운드 카운터(1부터 시작). 라운드 처리마다 +1
        int roundCounter = 1;

        void Start()
        {
            Add(publicDeck, CardType.Cooperation, cooperationCount);
            Add(publicDeck, CardType.Doubt,       doubtCount);
            Add(publicDeck, CardType.Betrayal,    betrayalCount);
            Add(publicDeck, CardType.Chaos,       chaosCount);
            Add(publicDeck, CardType.Pollution,   pollutionCount);
            if (interruptCount > 0) Add(publicDeck, CardType.Interrupt, interruptCount);
            Shuffle(publicDeck);

            Draw(publicDeck, playerIHands,  startingHand);
            Draw(publicDeck, playerIIHands, startingHand);

            playerILife = playerIILife = startLife;
            playerILost = playerIILost = false;
            roundCounter = 1;

            BuildEffects_NewRules();
        }

        // 외부에서 세트 리셋 시 호출(선택)
        public void ClearLoseFlags()
        {
            playerILost = false;
            playerIILost = false;
        }

        // 라운드 자동 진행(에이전트 선택 사용)
        public void ResolveRoundAuto(Agent p1, Agent p2, RoundCtx ctxP1, RoundCtx ctxP2)
        {
            if (playerILost || playerIILost) return;

            var unseenForP1 = BuildUnseen(true);
            var unseenForP2 = BuildUnseen(false);

            var pick1 = p1.Choose(new DecisionInput(playerIHands,  ctxP1, unseenForP1));
            var pick2 = p2.Choose(new DecisionInput(playerIIHands, ctxP2, unseenForP2));

            int idx1 = IndexOfFirst(playerIHands, pick1);
            int idx2 = IndexOfFirst(playerIIHands, pick2);
            ResolveRoundByIndex(idx1, idx2);
        }

        // 라운드 처리(인덱스 지정)
        public void ResolveRoundByIndex(int p1Index, int p2Index)
        {
            if (playerILost || playerIILost) return;

            var a = UseCard(playerIHands, p1Index);
            var b = UseCard(playerIIHands, p2Index);
            if (a == CardType.None || b == CardType.None) return;

            var ef = E[$"{a}-{b}"];

            // round-scaled 적용
            int dSelf = ef.selfUseRound ? (ef.self >= 0 ? +roundCounter : -roundCounter) : ef.self;
            int dOpp  = ef.oppUseRound  ? (ef.opp  >= 0 ? +roundCounter : -roundCounter)  : ef.opp;

            playerILife += dSelf;
            playerIILife += dOpp;

            if (ef.repSelf) ReplaceHand(playerIHands);
            if (ef.repOpp)  ReplaceHand(playerIIHands);

            // 라운드 종료 -1, 상한 = startLife
            playerILife--; playerIILife--;
            playerILife  = Mathf.Clamp(playerILife,  0, startLife);
            playerIILife = Mathf.Clamp(playerIILife, 0, startLife);

            if (playerILife <= 0)  playerILost  = true;
            if (playerIILife <= 0) playerIILost = true;

            DrawToThree(playerIHands);
            DrawToThree(playerIIHands);

            roundCounter++; // 다음 라운드
        }

        // “덱 + 상대 손패” 집계
        public Dictionary<CardType,int> BuildUnseen(bool isP1)
        {
            var unseen = new Dictionary<CardType,int>
            {
                {CardType.Cooperation,0},{CardType.Doubt,0},{CardType.Betrayal,0},
                {CardType.Chaos,0},{CardType.Pollution,0},{CardType.Interrupt,0}
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

        // ==== 유틸 ====
        static int IndexOfFirst(List<CardType> hand, CardType t)
        { for (int i=0;i<hand.Count;i++) if (hand[i]==t) return i; return hand.Count>0?0:-1; }

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
        void Draw(List<CardType> deck, List<CardType> hand, int n)
        { for(int i=0;i<n;i++){ var c=DrawOne(deck); if(c==CardType.None) break; hand.Add(c);} }
        void DrawToThree(List<CardType> hand){ int need = 3 - hand.Count; if (need > 0) Draw(publicDeck, hand, need); }
        void ReplaceHand(List<CardType> hand){ discardCards.AddRange(hand); hand.Clear(); Draw(publicDeck, hand, 3); }
        CardType UseCard(List<CardType> hand, int index)
        { if(index<0||index>=hand.Count) return CardType.None; var c=hand[index]; hand.RemoveAt(index); discardCards.Add(c); return c; }
        static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            { int j = UnityEngine.Random.Range(0, i + 1); (list[i], list[j]) = (list[j], list[i]); }
        }

        // === 신규 규칙 매트릭스(Interrupt 포함) ===
        void BuildEffects_NewRules()
        {
            E = new Dictionary<string, Effect>();
            var C=CardType.Cooperation; var D=CardType.Doubt; var B=CardType.Betrayal; var X=CardType.Chaos; var P=CardType.Pollution; var I=CardType.Interrupt;

            // Cooperation row
            E[$"{C}-{C}"]=new(+1,+1);
            E[$"{C}-{D}"]=new(+1,0);
            E[$"{C}-{B}"]=new(-1,+1, false,false, true,false); // self = -(round)
            E[$"{C}-{X}"]=new(+1,0, false,true);               // opp hand reset
            E[$"{C}-{P}"]=new(-1,+1);
            E[$"{C}-{I}"]=new(+1,-1);

            // Doubt row
            E[$"{D}-{C}"]=new(0,+1);
            E[$"{D}-{D}"]=new(0,0);
            E[$"{D}-{B}"]=new(+1,-1, false,false, false,true); // opp = -(round)
            E[$"{D}-{X}"]=new(0,0, false,true);                // opp hand reset
            E[$"{D}-{P}"]=new(0,-1);
            E[$"{D}-{I}"]=new(-1,+1);

            // Betrayal row  (-(round) 사용)
            E[$"{B}-{C}"]=new(+1,-1, false,false, false,true);
            E[$"{B}-{D}"]=new(-1,+1, false,false, true,false);
            E[$"{B}-{B}"]=new(-1,-1, false,false, true,true);
            E[$"{B}-{X}"]=new(+1,-1, false,false, false,true);
            E[$"{B}-{P}"]=new(+1,-1, false,false, false,true);
            E[$"{B}-{I}"]=new(-1,+1); // Interrupt에게 상수 패배

            // Chaos row  (손패 리셋)
            E[$"{X}-{C}"]=new(0,+1,  true,false);                    // self reset
            E[$"{X}-{D}"]=new(0,0,   true,false);
            E[$"{X}-{B}"]=new(-1,+1, true,false, true,false);       // self = -(round)
            E[$"{X}-{X}"]=new(0,0,   true,true);
            E[$"{X}-{P}"]=new(-1,0,  true,false);
            E[$"{X}-{I}"]=new(0,-1,  true,false);                   // self reset, opp -1

            // Pollution row
            E[$"{P}-{C}"]=new(+1,-1);
            E[$"{P}-{D}"]=new(-1,0);
            E[$"{P}-{B}"]=new(-1,+1, false,false, true,false);      // self = -(round)
            E[$"{P}-{X}"]=new(0,-1,  false,true);                   // opp hand reset, opp -1
            E[$"{P}-{P}"]=new(-1,-1);
            E[$"{P}-{I}"]=new(-1,+1);

            // Interrupt row
            E[$"{I}-{C}"]=new(-1,+1);
            E[$"{I}-{D}"]=new(+1,-1);
            E[$"{I}-{B}"]=new(+1,-1);
            E[$"{I}-{X}"]=new(-1,0, false,true); // opp hand reset
            E[$"{I}-{P}"]=new(+1,-1);
            E[$"{I}-{I}"]=new(0,0);
        }
    }
}