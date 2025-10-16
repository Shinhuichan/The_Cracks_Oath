// File: GameCore.cs
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
using CustomInspector;

public enum Mode { Light = 0, Common, Heavy }
[System.Serializable]
public struct PlayMode
{
    public Mode mode;
    public int StartHP;
    public int maxRounds;
}

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

        public Agent(string name) { this.name = name; }

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
        public PlayMode[] modes;
        public Mode currentMode = Mode.Common;
    
        public List<CardType> publicDeck = new();
        public List<CardType> playerIHands = new();
        public List<CardType> playerIIHands = new();
        public List<CardType> discardCards = new();

        [Header("덱 구성")]
        public int cooperationCount = 20; 
        public int doubtCount = 20, betrayalCount = 3, chaosCount = 7, pollutionCount = 10, interruptCount = 4, reconCount = 6;

        [Header("게임 설정")]
        [ReadOnly] public int startingHand = 3;
        [ReadOnly] public int startLife = 10;
        [ReadOnly] public int maxRounds = 10;
        [ReadOnly] public int playerILife, playerIILife;
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

        // 자연재해
        public enum NaturalDisaster { Peace, Typhoon, Heatwave, Lightning, Gale, ColdWave }

        [Header("Natural Disaster")]
        [SerializeField] int disasterSpan = 5; // 5라운드마다 교체
        [SerializeField] List<NaturalDisaster> disasterPool =
            new() { NaturalDisaster.Peace, NaturalDisaster.Typhoon, NaturalDisaster.Heatwave,
                    NaturalDisaster.Lightning, NaturalDisaster.Gale, NaturalDisaster.ColdWave };

        List<NaturalDisaster> disasterOrder;
        int disasterIndex = 0;
        public NaturalDisaster currentDisaster { get; private set; } = NaturalDisaster.Peace;
        public event Action<string> OnDisasterUIChanged;

        // Gale 전용: 제출 카드 치환 여부, ColdWave 전용: 라운드 종료 드로우 상한
        bool galeCheckedThisRound = false;
        bool extraLightningAppliedThisRound = false;

        private void Awake()
        {
            ApplyModeIfAvailable(); // 인스펙터 설정 반영
        }

#if UNITY_EDITOR
    void OnValidate()
    {
        // 에디터에서 값 바꿀 때도 미리 반영되게
        ApplyModeIfAvailable();
    }
#endif
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

            Draw(publicDeck, playerIHands, startingHand);  // fix
            Draw(publicDeck, playerIIHands, startingHand); // fix

            playerILife = playerIILife = startLife;
            playerILost = playerIILost = false;
            roundCounter = 1;

            BuildEffects_WithRecon();

            // 자연재해 초기화
            BuildDisasterOrder();
            currentDisaster = disasterOrder[0];
            OnDisasterStart(currentDisaster); // 태풍 즉시 반영 등
            RaiseDisasterUI();
        }

        public string CurrentDisasterLabel
        {
            get
            {
                return $"{ToKorean(currentDisaster)} {DisasterRule(currentDisaster)}";
            }
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

            galeCheckedThisRound = false;
            extraLightningAppliedThisRound = false;

            // (A) 제출 카드 확정
            var a = UseCard(playerIHands, p1Index);
            var b = UseCard(playerIIHands, p2Index);
            if (a == CardType.None || b == CardType.None) return;

            // 강풍: 25% 확률로 제출 카드가 덱에서 뽑은 새 카드로 치환(원카드 반환)
            if (currentDisaster == NaturalDisaster.Gale)
            {
                // P1
                if (UnityEngine.Random.value < 0.25f)
                {
                    // 원래 낸 카드 되돌리고
                    playerIHands.Add(a);
                    // 새 카드 1장 뽑아 그 카드로 제출(손패에는 넣지 않음)
                    a = DrawOneForSubmit();
                }
                // P2
                if (UnityEngine.Random.value < 0.25f)
                {
                    playerIIHands.Add(b);
                    b = DrawOneForSubmit();
                }
                galeCheckedThisRound = true;
            }

            var ef = E[$"{a}-{b}"];
            // (B) 일반 카드 효과 → Chaos → Recon (기존 그대로)
            int dSelf = ef.selfUseRound ? (ef.self >= 0 ? +roundCounter : -roundCounter) : ef.self;
            int dOpp  = ef.oppUseRound  ? (ef.opp  >= 0 ? +roundCounter : -roundCounter)  : ef.opp;
            playerILife  += dSelf;
            playerIILife += dOpp;
            if (playerILife <= 0)  playerILost  = true;
            if (playerIILife <= 0) playerIILost = true;

            if (ef.repSelf) ReplaceHand(playerIHands);
            if (ef.repOpp)  ReplaceHand(playerIIHands);

            if (ef.reconSelf) { lastSeenByP1 = new List<CardType>(playerIIHands); }
            if (ef.reconOpp)  { lastSeenByP2 = new List<CardType>(playerIHands);  }

            if (playerILost || playerIILost) return;

            // (C) 라운드 종료 단계: 자연재해 → 라운드 피로 → 드로우 → 라운드 증가/자연재해 교체
            ApplyDisasterEndEffects(a, b);
            if (playerILost || playerIILost) return;

            int baseLoss = GetRoundEndLossByDisaster(); // 평화:-1, 폭염:-2, 기타 규칙 반영
            playerILife  = Mathf.Clamp(playerILife  - baseLoss, 0, startLife);
            playerIILife = Mathf.Clamp(playerIILife - baseLoss, 0, startLife);
            playerILost  |= playerILife  <= 0;
            playerIILost |= playerIILife <= 0;
            if (playerILost || playerIILost) return;

            DrawToLimitByDisaster(playerIHands);
            DrawToLimitByDisaster(playerIIHands);

            roundCounter++;
            // 5라운드마다 교체. 이미 쓴 재해는 재등장하지 않음. 모두 소진되면 평화 유지.
            if ((roundCounter - 1) % disasterSpan == 0)
            {
                disasterIndex++;
                if (disasterIndex < disasterOrder.Count)
                {
                    currentDisaster = disasterOrder[disasterIndex];
                }
                else
                {
                    currentDisaster = NaturalDisaster.Peace;
                }
                OnDisasterStart(currentDisaster); // 태풍 즉시 반영
            }
            if ((roundCounter - 1) % disasterSpan == 0)
            {
                disasterIndex++;
                if (disasterIndex < disasterOrder.Count)
                    currentDisaster = disasterOrder[disasterIndex];
                else
                    currentDisaster = NaturalDisaster.Peace;

                OnDisasterStart(currentDisaster);
                RaiseDisasterUI();   // ← 추가
            }
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
        public void ResetForNewMatch()
        {
            ClearLoseFlags();
            publicDeck.Clear();
            playerIHands.Clear();
            playerIIHands.Clear();
            discardCards.Clear();
            lastSeenByP1.Clear();
            lastSeenByP2.Clear();
            roundCounter = 1;

            // 선택 모드 재적용 후 초기화
            ApplyModeIfAvailable();

            // 덱 재구성
            Add(publicDeck, CardType.Cooperation, cooperationCount);
            Add(publicDeck, CardType.Doubt, doubtCount);
            Add(publicDeck, CardType.Betrayal, betrayalCount);
            Add(publicDeck, CardType.Chaos, chaosCount);
            Add(publicDeck, CardType.Pollution, pollutionCount);
            Add(publicDeck, CardType.Interrupt, interruptCount);
            Add(publicDeck, CardType.Recon, reconCount);

            publicDeck.Shuffle();
            Draw(publicDeck, playerIHands, startingHand);  // fix
            Draw(publicDeck, playerIIHands, startingHand); // fix

            playerILife = startLife;
            playerIILife = startLife;

            BuildDisasterOrder();
            currentDisaster = disasterOrder[0];
            OnDisasterStart(currentDisaster);
        }
        void RaiseDisasterUI() => OnDisasterUIChanged?.Invoke(CurrentDisasterLabel);
        void BuildDisasterOrder()
        {
            disasterOrder = new List<NaturalDisaster>(disasterPool);
            // 셔플
            for (int i = 0; i < disasterOrder.Count; i++)
            {
                int j = UnityEngine.Random.Range(i, disasterOrder.Count);
                (disasterOrder[i], disasterOrder[j]) = (disasterOrder[j], disasterOrder[i]);
            }
            disasterIndex = 0;
        }
        string ToKorean(NaturalDisaster d) => d switch
        {
            NaturalDisaster.Peace => "맑음",
            NaturalDisaster.Typhoon => "<color=grey>태풍</color>",
            NaturalDisaster.Heatwave => "<color=red>폭염</color>",
            NaturalDisaster.Lightning => "<color=yellow>낙뢰</color>",
            NaturalDisaster.Gale => "<color=black>강풍</color>",
            NaturalDisaster.ColdWave => "<color=blue>한파</color>",
            _ => d.ToString()
        };
        string DisasterRule(NaturalDisaster d) => d switch
        {
            NaturalDisaster.Peace     => "\nRound마다 양초 - 1",
            NaturalDisaster.Typhoon   => "\n각 참가자의 양초 1/2",
            NaturalDisaster.Heatwave  => "\nRound마다 양초 - 2",
            NaturalDisaster.Lightning => "\nRound마다 25% 확률로 \n양초 - 3",
            NaturalDisaster.Gale      => "\nRound마다 25% 확률로 \n제출된 카드 교체",
            NaturalDisaster.ColdWave  => "\n최대 패 수급 2장으로 제한",
            _ => ""
        };
        CardType DrawOneForSubmit()
        {
            // publicDeck에서 1장 뽑아 제출용으로만 사용
            if (publicDeck.Count == 0) return CardType.None;
            int idx = UnityEngine.Random.Range(0, publicDeck.Count);
            var card = publicDeck[idx];
            publicDeck.RemoveAt(idx);
            return card;
        }

        #region Disaster Helpers
        void OnDisasterStart(NaturalDisaster d)
        {
            if (d == NaturalDisaster.Typhoon)
            {
                // 즉시 현재 생명 절반(소수 버림)
                playerILife  = Mathf.Max(0, playerILife  / 2);
                playerIILife = Mathf.Max(0, playerIILife / 2);
                playerILost  |= playerILife  <= 0;
                playerIILost |= playerIILife <= 0;
            }
            // 다른 재해는 시작 시점 추가효과 없음
        }

        int GetRoundEndLossByDisaster()
        {
            switch (currentDisaster)
            {
                case NaturalDisaster.Peace:    return 1; // 기본 규칙
                case NaturalDisaster.Heatwave: return 2; // 폭염
                default:                       return 1; // 나머지는 기본 1
            }
        }

        void ApplyDisasterEndEffects(CardType a, CardType b)
        {
            switch (currentDisaster)
            {
                case NaturalDisaster.Lightning:
                    // 라운드마다 25% 확률로 동시 -3
                    if (!extraLightningAppliedThisRound && UnityEngine.Random.value < 0.25f)
                    {
                        playerILife = Mathf.Max(0, playerILife - 3);
                        playerIILife = Mathf.Max(0, playerIILife - 3);
                        RaiseDisasterUI();
                        playerILost |= playerILife <= 0;
                        playerIILost |= playerIILife <= 0;
                        extraLightningAppliedThisRound = true;
                    }
                    break;

                case NaturalDisaster.Gale:
                    // 제출 단계에서 이미 처리
                    break;

                case NaturalDisaster.ColdWave:
                    // 드로우 단계에서 처리(2장 상한)
                    break;

                case NaturalDisaster.Peace:
                case NaturalDisaster.Heatwave:
                case NaturalDisaster.Typhoon:
                default:
                    break;
            }
        }
        #endregion Disaster Helpers

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
        void DrawToThree(List<CardType> hand) { int need = 3 - hand.Count; if (need > 0) Draw(publicDeck, hand, need); }
        void DrawToLimitByDisaster(List<CardType> hand)
        {
            int limit = (currentDisaster == NaturalDisaster.ColdWave) ? 2 : 3;
            while (hand.Count < limit) Draw(publicDeck, hand, 1);
        }
        void ReplaceHand(List<CardType> hand){ discardCards.AddRange(hand); hand.Clear(); Draw(publicDeck, hand, 3); }
        CardType UseCard(List<CardType> hand, int index)
        { if (index < 0 || index >= hand.Count) return CardType.None; var c = hand[index]; hand.RemoveAt(index); discardCards.Add(c); return c; }
        
        // === 모드 적용 유틸 ===
        public void ApplyModeIfAvailable()
        {
            if (modes != null && modes.Length > 0)
            {
                // currentMode와 일치하는 항목 찾기
                for (int i = 0; i < modes.Length; i++)
                {
                    if (modes[i].mode == currentMode)
                    {
                        startLife = modes[i].StartHP;
                        maxRounds = modes[i].maxRounds;
                        return;
                    }
                }
            }
            // 없으면 기본값 유지
        }
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