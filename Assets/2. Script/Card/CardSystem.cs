// File: GameCore.cs
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
using CustomInspector;

public enum Mode { Quick = 0, Common, Extend }
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
    // ===== 에이전트 =====
    public sealed class Agent
    {
        public string name;
        public List<Func<DecisionInput, CardType?>> rules = new();

        // ▼ 추가: 두 장 중 1장 선택 드로우용(0 또는 1 반환, null이면 시스템 기본 휴리스틱 사용)
        public Func<CardType, CardType, DecisionInput, int?> chooseFromTwo;

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
        public bool IsLastRound => roundCounter >= maxRounds;
        public bool enableChoiceDrawForAgent = true;

        // --- Agent draw styles ---
        public enum AgentStyle { Generic, 김현수, 이수진, 최용호, 한지혜, 박민재, 정다은, 오태훈, 유민정, 김태양, 이하린, 백무적 }
        [HideInInspector] public AgentStyle opponentStyle = AgentStyle.Generic;
        public Agent currentP1, currentP2;   // ▼ 추가
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

        public bool enableChoiceDrawForPlayer = true;            // 인스펙터에서 켜기
        public event System.Action<GameCore.CardType, GameCore.CardType> OnOfferChoiceForPlayer;
        
        public event System.Action OnChoiceClosed;                 // 선택 UI 닫기
        public event System.Action<System.Collections.Generic.List<GameCore.CardType>> OnPlayerHandChanged; // 손패 바뀜

        bool waitingChoice = false;
        GameCore.CardType pendingA, pendingB;

        // 자연재해
        public enum NaturalDisaster { Peace, Meteorite, Heatwave, Lightning, Gale, ColdWave }

        [Header("Natural Disaster")]
        [SerializeField] int disasterSpan = 5; // 5라운드마다 교체
        [SerializeField] List<NaturalDisaster> disasterPool =
            new() { NaturalDisaster.Peace, NaturalDisaster.Meteorite, NaturalDisaster.Heatwave,
                    NaturalDisaster.Lightning, NaturalDisaster.Gale, NaturalDisaster.ColdWave };

        List<NaturalDisaster> disasterOrder;
        int disasterIndex = 0;
        public NaturalDisaster currentDisaster { get; private set; } = NaturalDisaster.Peace;
        public event Action<string> OnDisasterUIChanged;
        
        public CardType lastSubmittedP1 { get; private set; } = CardType.None;
        public CardType lastSubmittedP2 { get; private set; } = CardType.None;

        // Gale 전용: 제출 카드 치환 여부, ColdWave 전용: 라운드 종료 드로우 상한
        bool galeCheckedThisRound = false;
        bool extraLightningAppliedThisRound = false;

        public int lastCardDeltaP1, lastCardDeltaP2;         // 카드/혼합 효과로 변한 HP
        public int lastDisasterDeltaP1, lastDisasterDeltaP2; // 자연재해로 변한 HP

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
            // ▼ 직렬화 잔여값 정리
            playerIHands.Clear();
            playerIIHands.Clear();
            discardCards.Clear();
            bool bootstrapped;

            void Start()
            {
                if (bootstrapped) return;
                bootstrapped = true;
                ResetForNewMatch();   // ← 덱 구성과 시작 드로우는 여기서만
            }
            // Add(publicDeck, CardType.Cooperation, cooperationCount);
            // Add(publicDeck, CardType.Doubt,       doubtCount);
            // Add(publicDeck, CardType.Betrayal,    betrayalCount);
            // Add(publicDeck, CardType.Chaos,       chaosCount);
            // Add(publicDeck, CardType.Pollution,   pollutionCount);
            // if (interruptCount > 0) Add(publicDeck, CardType.Interrupt, interruptCount);
            // if (reconCount > 0)     Add(publicDeck, CardType.Recon,     reconCount);
            // publicDeck.Shuffle();

            // 정확히 3장만
            Draw(publicDeck, playerIHands, startingHand);
            Draw(publicDeck, playerIIHands, startingHand);

            OnPlayerHandChanged?.Invoke(new List<CardType>(playerIHands));

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
            currentP1 = p1;                   // ▼ 추가
            currentP2 = p2;                   // ▼ 추가
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

            // ▼ 분리된 피해 누적치 초기화
            lastCardDeltaP1 = lastCardDeltaP2 = 0;
            lastDisasterDeltaP1 = lastDisasterDeltaP2 = 0;

            // (A) 제출 카드 확정
            var a = UseCard(playerIHands, p1Index);
            var b = UseCard(playerIIHands, p2Index);
            if (a == CardType.None || b == CardType.None) return;

            // --- 강풍: 40% 확률로 제출 카드 교체(양측 동일 확률)
            if (currentDisaster == NaturalDisaster.Gale)
            {
                if (UnityEngine.Random.value < 0.25f) { playerIHands.Add(a); a = DrawOneForSubmit(); }
                if (UnityEngine.Random.value < 0.25f) { playerIIHands.Add(b); b = DrawOneForSubmit(); }
                galeCheckedThisRound = true;
            }

            // ▼ 최종 제출 카드 기록(스프라이트 표시용)
            lastSubmittedP1 = a;
            lastSubmittedP2 = b;

            var ef = E[$"{a}-{b}"];

            int hpP_beforeCards = playerILife;
            int hpA_beforeCards = playerIILife;

            // (B) 일반 카드 효과 → Chaos → Recon
            int dSelf = ef.selfUseRound ? (ef.self >= 0 ? +roundCounter : -roundCounter) : ef.self;
            int dOpp  = ef.oppUseRound  ? (ef.opp  >= 0 ? +roundCounter : -roundCounter)  : ef.opp;
            playerILife  += dSelf;
            playerIILife += dOpp;

            if (playerILife <= 0)  playerILost  = true;
            if (playerIILife <= 0) playerIILost = true;

            if (ef.repSelf) ReplaceHand(playerIHands);
            if (ef.repOpp) ReplaceHand(playerIIHands);
            
            if (ef.reconSelf) { lastSeenByP1 = new List<CardType>(playerIIHands); }
            if (ef.reconOpp)  { lastSeenByP2 = new List<CardType>(playerIHands);  }

            // ▼ 카드로 인한 순수 변화량 기록
            lastCardDeltaP1 += (playerILife  - hpP_beforeCards);
            lastCardDeltaP2 += (playerIILife - hpA_beforeCards);

            if (playerILost || playerIILost) return;

            // (C) 라운드 종료 단계: 자연재해 → 라운드 피로 → 드로우 → 라운드 증가/자연재해 교체
            ApplyDisasterEndEffects(a, b);
            if (playerILost || playerIILost) return;

            int baseLoss = GetRoundEndLossByDisaster();
            if (baseLoss > 0)
            {
                playerILife = Mathf.Clamp(playerILife - baseLoss, 0, startLife);
                playerIILife = Mathf.Clamp(playerIILife - baseLoss, 0, startLife);
                lastDisasterDeltaP1 -= baseLoss;
                lastDisasterDeltaP2 -= baseLoss;
            }
            
            playerILost  |= playerILife  <= 0;
            playerIILost |= playerIILife <= 0;
            if (playerILost || playerIILost) return;

            if (!IsLastRound)
            {
                DrawToLimitByDisaster(true);   // 플레이어
                DrawToLimitByDisaster(false);  // 상대
            }
            else
            {
                // 선택 패널이 열려 있었다면 강제 종료
                if (waitingChoice) { waitingChoice = false; OnChoiceClosed?.Invoke(); }
            }

            roundCounter++;
            // 5라운드마다 교체. 이미 쓴 재해는 재등장하지 않음. 모두 소진되면 평화 유지.
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
            OnPlayerHandChanged?.Invoke(new List<CardType>(playerIHands));

            playerILife = startLife;
            playerIILife = startLife;

            BuildDisasterOrder();
            currentDisaster = disasterOrder[0];
            OnDisasterStart(currentDisaster);
            waitingChoice = false;
            OnChoiceClosed?.Invoke();   
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
            NaturalDisaster.Meteorite => "<color=grey>운석 충돌</color>",
            NaturalDisaster.Heatwave => "<color=red>폭염</color>",
            NaturalDisaster.Lightning => "<color=yellow>낙뢰</color>",
            NaturalDisaster.Gale => "<color=black>강풍</color>",
            NaturalDisaster.ColdWave => "<color=blue>한파</color>",
            _ => d.ToString()
        };
        string DisasterRule(NaturalDisaster d) => d switch
        {
            NaturalDisaster.Peace     => "\n없음",
            NaturalDisaster.Meteorite   => "\n각 참가자의 양초 * 2/3",
            NaturalDisaster.Heatwave  => "\nRound마다 추가 양초 - 1",
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
            if (d == NaturalDisaster.Meteorite)
            {
                // 즉시 현재 생명 절반(소수 버림)
                playerILife  = Mathf.Max(0, playerILife  / 3 * 2);
                playerIILife = Mathf.Max(0, playerIILife / 3 * 2);
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
                    if (!extraLightningAppliedThisRound && UnityEngine.Random.value < 0.25f)
                    {
                        playerILife  = Mathf.Max(0, playerILife  - 3);
                        playerIILife = Mathf.Max(0, playerIILife - 3);
                        // ▼ 자연재해 피해 누적
                        lastDisasterDeltaP1 -= 3;
                        lastDisasterDeltaP2 -= 3;

                        RaiseDisasterUI();
                        playerILost  |= playerILife  <= 0;
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
                case NaturalDisaster.Meteorite:
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

        // ───────────────────────────────────────────────
        // 누락된 프로퍼티: 매치 종료 여부
        public bool IsMatchEnded => playerILost || playerIILost || IsLastRound;
        // ───────────────────────────────────────────────

        // 누락된 메서드: 플레이어 선택 드로우 시작
        void StartChoiceDrawForPlayer()
        {
            // 매치 종료/대기 중이면 무시
            if (IsMatchEnded || waitingChoice) return;

            // 플레이어의 선택 드로우가 꺼져 있으면 일반 드로우
            if (!enableChoiceDrawForPlayer)
            {
                var c = DrawOne(publicDeck);
                if (c != CardType.None) playerIHands.Add(c);
                OnPlayerHandChanged?.Invoke(new List<CardType>(playerIHands));
                return;
            }

            // Chaos 제외 2장 제시
            pendingA = DrawNonChaosFromPublic();
            pendingB = DrawNonChaosFromPublic();

            waitingChoice = true;
            // UI에 두 장 통지(구독자가 없으면 null-safe)
            OnOfferChoiceForPlayer?.Invoke(pendingA, pendingB);
        }
        // 기존 DrawToLimitByDisaster(List<CardType> hand) 수정
        private void DrawToLimitByDisaster(bool isP1)
        {
            if (IsMatchEnded) return;
            var hand = isP1 ? playerIHands : playerIIHands;

            while (hand.Count < 3)
            {
                if (isP1 && enableChoiceDrawForPlayer)
                {
                    StartChoiceDrawForPlayer(); // UI 열고 대기 → 콜백에서 이어서 채움
                    return;
                }
                else if (!isP1 && enableChoiceDrawForAgent)
                {
                    StartChoiceDrawForAgent();  // 즉시 결정
                    // 손패가 3장 될 때까지 while 계속
                }
                else
                {
                    // 일반 드로우
                    var c = DrawOne(publicDeck);
                    if (c == CardType.None) return;
                    hand.Add(c);
                }
            }
        }

        void ReplaceHand(List<CardType> hand)
        {
            // 현재 라운드 재해 기준으로 리셋 후 드로우 상한 결정
            int drawLimit = (currentDisaster == NaturalDisaster.ColdWave) ? 2 : 3;

            discardCards.AddRange(hand);
            hand.Clear();
            Draw(publicDeck, hand, drawLimit); // Chaos 리셋 시에도 한파면 최대 2장만
        }
        
        // 에이전트 선택 드로우 시작부 수정
        void StartChoiceDrawForAgent()
        {
            if (publicDeck.Count == 0) return;

            var c1 = DrawNonChaosFromPublic();
            var c2 = DrawNonChaosFromPublic();

            int pick;
            // ▼ 에이전트가 선택 규칙을 갖고 있으면 우선 사용
            if (enableChoiceDrawForAgent && currentP2 != null && currentP2.chooseFromTwo != null)
            {
                var ctx = new RoundCtx {            // 라운드 상황 구성
                    round = roundCounter,
                    selfLife = playerIILife,
                    oppLife  = playerILife,
                    lastSelf = lastSubmittedP2,
                    lastOpp  = lastSubmittedP1
                };
                var input = new DecisionInput(playerIIHands, ctx, BuildUnseen(false));
                pick = currentP2.chooseFromTwo(c1, c2, input) ?? ChooseIndexForAgent(c1, c2);
            }
            else
            {
                pick = ChooseIndexForAgent(c1, c2); // 기존 휴리스틱
            }

            var chosen = (pick == 0) ? c1 : c2;
            var other  = (pick == 0) ? c2 : c1;

            playerIIHands.Add(chosen);
            int pos = UnityEngine.Random.Range(0, publicDeck.Count + 1);
            publicDeck.Insert(pos, other);
        }
        // 두 카드 중 무엇을 고를지 간단 휴리스틱
        int ChooseIndexForAgent(CardType a, CardType b)
        {
            // Chaos 회피
            if (a == CardType.Chaos && b != CardType.Chaos) return 1;
            if (b == CardType.Chaos && a != CardType.Chaos) return 0;

            // 현재 상대 손패 상황
            bool hasAtk = playerIIHands.Contains(CardType.Betrayal) || playerIIHands.Contains(CardType.Pollution);
            bool hasDef = playerIIHands.Contains(CardType.Doubt) || playerIIHands.Contains(CardType.Interrupt);

            int R = Mathf.Max(1, roundCounter);
            bool killA = (a == CardType.Betrayal) && (playerILife <= R);
            bool killB = (b == CardType.Betrayal) && (playerILife <= R);
            if (killA != killB) return killA ? 0 : 1;

            if (!hasDef)
            {
                bool aDef = (a == CardType.Doubt || a == CardType.Interrupt);
                bool bDef = (b == CardType.Doubt || b == CardType.Interrupt);
                if (aDef != bDef) return aDef ? 0 : 1;
            }
            if (!hasAtk)
            {
                bool aAtk = (a == CardType.Betrayal || a == CardType.Pollution);
                bool bAtk = (b == CardType.Betrayal || b == CardType.Pollution);
                if (aAtk != bAtk) return aAtk ? 0 : 1;
            }

            int Score(CardType t) => t switch
            {
                CardType.Betrayal => 100,
                CardType.Doubt => 90,
                CardType.Interrupt => 85,
                CardType.Pollution => 80,
                CardType.Cooperation => 60,
                CardType.Recon => 50,
                CardType.Chaos => 10,
                _ => 0
            };
            int sa = Score(a), sb = Score(b);
            if (sa != sb) return sa > sb ? 0 : 1;

            return UnityEngine.Random.value < 0.5f ? 0 : 1;
        }
        // private CardInfo DrawNonChaosForChoice()
        // {
        //     CardInfo ci;
        //     int guard = 20;
        //     do
        //     {
        //         ci = DrawOneFromPublic(); // 기존 단일 드로우 함수
        //         guard--;
        //     } while (ci.type == CardType.Chaos && guard > 0); // Chaos 제외
        //     return ci;
        // }
        // Chaos 제외 1장 드로우(선택지용)
        CardType DrawNonChaosFromPublic()
        {
            int guard = 20;
            CardType c;
            do { c = DrawOne(publicDeck); guard--; } while (c == CardType.Chaos && guard > 0);
            return c;
        }
        public void SelectChoiceForPlayer(int idx)
        {
            if (!waitingChoice) return;
            var chosen = (idx == 0) ? pendingA : pendingB;
            var other  = (idx == 0) ? pendingB : pendingA;

            playerIHands.Add(chosen);

            int pos = UnityEngine.Random.Range(0, publicDeck.Count + 1);
            publicDeck.Insert(pos, other);

            waitingChoice = false;

            // ▼ NEW: 선택창 닫기 + 손패 갱신 통지
            OnChoiceClosed?.Invoke();
            OnPlayerHandChanged?.Invoke(new List<CardType>(playerIHands));

            // 여전히 상한 미만이면 이어서 채움(추가 선택도 자동 이어짐)
            DrawToLimitByDisaster(true);
        }

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