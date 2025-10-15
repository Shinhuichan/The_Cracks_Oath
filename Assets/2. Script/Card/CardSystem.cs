// File: GameCore.cs
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

namespace GameCore
{
    public enum CardType { None = 0, Cooperation, Doubt, Betrayal, Chaos, Pollution, Interrupt, Recon }

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

    // ===== 카드 시스템 =====
    public class CardSystem : MonoBehaviour
    {
        public List<CardType> publicDeck = new();
        public List<CardType> playerIHands = new();
        public List<CardType> playerIIHands = new();
        public List<CardType> discardCards = new();

        [Header("덱 구성")]
        public int cooperationCount = 20, doubtCount = 20, betrayalCount = 3, chaosCount = 7, pollutionCount = 10, interruptCount = 4, reconCount = 6;

        [Header("게임 설정")]
        public int startingHand = 3, startLife = 10;
        public int playerILife, playerIILife;
        public bool playerILost { get; private set; }
        public bool playerIILost { get; private set; }

        public List<CardType> lastSeenByP1 = new();
        public List<CardType> lastSeenByP2 = new();

        struct Effect
        {
            public int self, opp;
            public bool repSelf, repOpp;
            public bool selfUseRound, oppUseRound;
            public bool reconSelf, reconOpp;

            public Effect(int s, int o, bool rs=false, bool ro=false, bool sr=false, bool ornd=false, bool rSelf=false, bool rOpp=false)
            { self = s; opp = o; repSelf = rs; repOpp = ro; selfUseRound = sr; oppUseRound = ornd; reconSelf = rSelf; reconOpp = rOpp; }
        }
        Dictionary<string, Effect> E;

        int roundCounter = 1;

        void Start()
        {
            Add(publicDeck, CardType.Cooperation, cooperationCount);
            Add(publicDeck, CardType.Doubt,       doubtCount);
            Add(publicDeck, CardType.Betrayal,    betrayalCount);
            Add(publicDeck, CardType.Chaos,       chaosCount);
            Add(publicDeck, CardType.Pollution,   pollutionCount);
            if (interruptCount > 0) Add(publicDeck, CardType.Interrupt, interruptCount);
            if (reconCount > 0) Add(publicDeck, CardType.Recon, reconCount);
            publicDeck.Shuffle();

            Draw(publicDeck, playerIHands,  startingHand);
            Draw(publicDeck, playerIIHands, startingHand);

            playerILife = playerIILife = startLife;
            playerILost = playerIILost = false;
            roundCounter = 1;

            BuildEffects_WithRecon();
        }

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

        public void ResolveRoundByIndex(int p1Index, int p2Index)
        {
            if (playerILost || playerIILost) return;

            var a = UseCard(playerIHands, p1Index);
            var b = UseCard(playerIIHands, p2Index);
            if (a == CardType.None || b == CardType.None) return;

            var ef = E[$"{a}-{b}"];

            int dSelf = ef.selfUseRound ? (ef.self >= 0 ? +roundCounter : -roundCounter) : ef.self;
            int dOpp  = ef.oppUseRound  ? (ef.opp  >= 0 ? +roundCounter : -roundCounter)  : ef.opp;

            playerILife += dSelf;
            playerIILife += dOpp;

            if (ef.reconSelf) { lastSeenByP1 = new List<CardType>(playerIIHands); }
            if (ef.reconOpp)  { lastSeenByP2 = new List<CardType>(playerIHands);  }

            if (ef.repSelf) ReplaceHand(playerIHands);
            if (ef.repOpp)  ReplaceHand(playerIIHands);

            playerILife--; playerIILife--;
            playerILife  = Mathf.Clamp(playerILife,  0, startLife);
            playerIILife = Mathf.Clamp(playerIILife, 0, startLife);

            if (playerILife <= 0)  playerILost  = true;
            if (playerIILife <= 0) playerIILost = true;

            DrawToThree(playerIHands);
            DrawToThree(playerIIHands);

            roundCounter++;
        }

        public Dictionary<CardType, int> BuildUnseen(bool isP1)
        {
            var unseen = new Dictionary<CardType, int>
            {
                {CardType.Cooperation,0},{CardType.Doubt,0},{CardType.Betrayal,0},
                {CardType.Chaos,0},{CardType.Pollution,0},{CardType.Interrupt,0},{CardType.Recon,0}
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
        public void ClearLoseFlags() { playerILost  = false; playerIILost = false; }

        static int IndexOfFirst(List<CardType> hand, CardType t)
        { for (int i=0;i<hand.Count;i++) if (hand[i]==t) return i; return hand.Count>0?0:-1; }

        static void Add(List<CardType> list, CardType t, int n){ for(int i=0;i<n;i++) list.Add(t); }
        CardType DrawOne(List<CardType> deck)
        {
            if (deck.Count == 0)
            {
                if (discardCards.Count == 0) return CardType.None;
                deck.AddRange(discardCards); discardCards.Clear(); deck.Shuffle();
            }
            var top = deck[0]; deck.RemoveAt(0); return top;
        }
        void Draw(List<CardType> deck, List<CardType> hand, int n)
        { for(int i=0;i<n;i++){ var c=DrawOne(deck); if(c==CardType.None) break; hand.Add(c);} }
        void DrawToThree(List<CardType> hand){ int need = 3 - hand.Count; if (need > 0) Draw(publicDeck, hand, need); }
        void ReplaceHand(List<CardType> hand){ discardCards.AddRange(hand); hand.Clear(); Draw(publicDeck, hand, 3); }
        CardType UseCard(List<CardType> hand, int index)
        { if(index<0||index>=hand.Count) return CardType.None; var c=hand[index]; hand.RemoveAt(index); discardCards.Add(c); return c; }

        void BuildEffects_WithRecon()
        {
            E = new Dictionary<string, Effect>();
            var C=CardType.Cooperation; var D=CardType.Doubt; var B=CardType.Betrayal; var X=CardType.Chaos; var P=CardType.Pollution; var I=CardType.Interrupt; var Rn=CardType.Recon;

            E[$"{C}-{C}"]=new(+1,+1);
            E[$"{C}-{D}"]=new(+1,0);
            E[$"{C}-{B}"]=new(-1,+1, false,false, true,false);
            E[$"{C}-{X}"]=new(+1,0, false,true);
            E[$"{C}-{P}"]=new(-1,+1);
            E[$"{C}-{I}"]=new(+1,-1);
            E[$"{C}-{Rn}"]=new(+1,0, false,false,false,false, false, true);

            E[$"{D}-{C}"]=new(0,+1);
            E[$"{D}-{D}"]=new(0,0);
            E[$"{D}-{B}"]=new(+1,-1, false,false, false,true);
            E[$"{D}-{X}"]=new(0,0, false,true);
            E[$"{D}-{P}"]=new(0,-1);
            E[$"{D}-{I}"]=new(-1,+1);
            E[$"{D}-{Rn}"]=new(0,0, false,false,false,false, false, true);

            E[$"{B}-{C}"]=new(+1,-1, false,false, false,true);
            E[$"{B}-{D}"]=new(-1,+1, false,false, true,false);
            E[$"{B}-{B}"]=new(-1,-1, false,false, true,true);
            E[$"{B}-{X}"]=new(+1,-1, false,false, false,true);
            E[$"{B}-{P}"]=new(+1,-1, false,false, false,true);
            E[$"{B}-{I}"]=new(-1,+1);
            E[$"{B}-{Rn}"]=new(+1,-1, false,false, false,true);

            E[$"{X}-{C}"]=new(0,+1,  true,false);
            E[$"{X}-{D}"]=new(0,0,   true,false);
            E[$"{X}-{B}"]=new(-1,+1, true,false, true,false);
            E[$"{X}-{X}"]=new(0,0,   true,true);
            E[$"{X}-{P}"]=new(-1,0,  true,false);
            E[$"{X}-{I}"]=new(0,-1,  true,false);
            E[$"{X}-{Rn}"]=new(0,0,  true,false, false,false, false, true);

            E[$"{P}-{C}"]=new(+1,-1);
            E[$"{P}-{D}"]=new(-1,0);
            E[$"{P}-{B}"]=new(-1,+1, false,false, true,false);
            E[$"{P}-{X}"]=new(0,-1,  false,true);
            E[$"{P}-{P}"]=new(-1,-1);
            E[$"{P}-{I}"]=new(-1,+1);
            E[$"{P}-{Rn}"]=new(0,-1, false,false,false,false, false, true);

            E[$"{I}-{C}"]=new(-1,+1);
            E[$"{I}-{D}"]=new(+1,-1);
            E[$"{I}-{B}"]=new(+1,-1);
            E[$"{I}-{X}"]=new(-1,0, false,true);
            E[$"{I}-{P}"]=new(+1,-1);
            E[$"{I}-{I}"]=new(0,0);
            E[$"{I}-{Rn}"]=new(-1,0);

            E[$"{Rn}-{C}"]=new(0,+1,  false,false,false,false, true,false);
            E[$"{Rn}-{D}"]=new(0,0,   false,false,false,false, true,false);
            E[$"{Rn}-{B}"]=new(-1,+1, false,false, true,false,  true,false);
            E[$"{Rn}-{X}"]=new(0,0,   false,true, false,false,  true,false);
            E[$"{Rn}-{P}"]=new(-1,0,  false,false,false,false,  true,false);
            E[$"{Rn}-{I}"]=new(0,-1,  false,false,false,false,  true,false);
            E[$"{Rn}-{Rn}"]=new(0,0,  false,false,false,false,  true,true);
        }
    }
}