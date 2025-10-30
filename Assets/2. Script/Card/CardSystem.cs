// File: GameCore.cs
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
using CustomInspector;
using GameCore;  // CardType

public enum Mode { Quick = 0, Standard, Extend }
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
        public List<CardType> oppHistory;
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
    public static class DecisionInputExtensions
{
    public static List<CardType> HistoryOpponent(this DecisionInput I)
    {
        return I.s.oppHistory ?? new List<CardType>();
    }
}
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
        public Mode currentMode = Mode.Standard;

        [Header("현재 선택된 에이전트")]
        private AgentList? currentP1Agent;   // 플레이어가 사람이면 null
        private AgentList  currentP2Agent;  // 상대 에이전트

        [Header("카드 상태")]
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

        // 한파 제어 플래그
        bool coldWaveJustStartedP1, coldWaveJustStartedP2;   // 한파가 ‘시작한’ 그 라운드에서만 드로우 스킵
        bool coldWaveRecoverThisRoundP1, coldWaveRecoverThisRoundP2; // 한파가 ‘끝난’ 라운드에서 2장 복구
        GameCore.CardSystem.NaturalDisaster lastDisaster;

        // 자연재해
        public enum NaturalDisaster { Peace, Meteorite, Heatwave, Lightning, Storm, ColdWave, Eclipse, Sandstorm }

        [Header("Natural Disaster")]
        [SerializeField] int disasterSpan = 5; // 5라운드마다 교체
        [SerializeField] List<NaturalDisaster> disasterPool =
            new() {
                NaturalDisaster.Peace, NaturalDisaster.Meteorite, NaturalDisaster.Heatwave,
                NaturalDisaster.Lightning, NaturalDisaster.Storm, NaturalDisaster.ColdWave,
                NaturalDisaster.Eclipse, NaturalDisaster.Sandstorm
            };
        List<NaturalDisaster> disasterOrder;
        int disasterIndex = 0;
        public NaturalDisaster currentDisaster { get; private set; } = NaturalDisaster.Peace;
        public event Action<string> OnDisasterUIChanged;
        
        public CardType lastSubmittedP1 { get; private set; } = CardType.None;
        public CardType lastSubmittedP2 { get; private set; } = CardType.None;

        // Storm 전용: 제출 카드 치환 여부, ColdWave 전용: 라운드 종료 드로우 상한
        bool StormCheckedThisRound = false;
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
        bool bootstrapped = false;
        void Start()
        {
            if (bootstrapped) return;
            bootstrapped = true;

            ApplyModeIfAvailable();     // 모드 반영
            ResetForNewMatch();         // 덱 구성 + 시작 드로우 + 재해 초기화
            BuildEffects_WithRecon();   // 효과 테이블
            RaiseDisasterUI();

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
            // 한파 종료 시, 대기 중이던 복구 2장을 즉시 지급
            GrantColdWaveCarryIfNeeded();
            ApplyPhaseStartBonusIfNeeded();
        }

        public void SetCurrentAgents(AgentList? p1, AgentList p2)
        {
            currentP1Agent = p1;
            currentP2Agent = p2;
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

            // 자동 매치일 땐 선택드로우 전부 비활성 보장
            enableChoiceDrawForPlayer = false;
            enableChoiceDrawForAgent = true;
            
            ApplyConditionIfAny(p1, true);
            ApplyConditionIfAny(p2, false);
            currentP1 = p1;
            currentP2 = p2;

            // 손패가 부족하면 먼저 채움(비주얼 UI 없이)
            DrawToLimitByDisaster(true);
            DrawToLimitByDisaster(false);

            if (playerIHands.Count == 0 || playerIIHands.Count == 0) return;

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
            phaseBonusAppliedThisRound = false;
            if (playerILost || playerIILost) return;

            ApplyConditionIfAny(currentP2, false);
            StormCheckedThisRound = false;
            extraLightningAppliedThisRound = false;

            int hpP_StartOfRound = playerILife;
            int hpA_StartOfRound = playerIILife;

            // ▼ 분리된 피해 누적치 초기화
            lastCardDeltaP1 = lastCardDeltaP2 = 0;
            lastDisasterDeltaP1 = lastDisasterDeltaP2 = 0;

            // (A) 제출 카드 확정
            var a = UseCard(playerIHands, p1Index);
            var b = UseCard(playerIIHands, p2Index);
            if (a == CardType.None || b == CardType.None) return;

            // --- 강풍: 40% 확률로 제출 카드 교체(양측 동일 확률)
            if (currentDisaster == NaturalDisaster.Storm)
            {
                if (UnityEngine.Random.value < 0.1f) { playerIHands.Add(a); a = DrawOneForSubmit(); }
                if (UnityEngine.Random.value < 0.1f) { playerIIHands.Add(b); b = DrawOneForSubmit(); }
                StormCheckedThisRound = true;
            }

            // ▼ 최종 제출 카드 기록(스프라이트 표시용)
            lastSubmittedP1 = a;
            lastSubmittedP2 = b;

            var ef = E[$"{a}-{b}"];

            int hpP_beforeCards = playerILife;
            int hpA_beforeCards = playerIILife;

            // (B) 일반 카드 효과 → Chaos
            int dSelf = ef.selfUseRound ? (ef.self >= 0 ? +roundCounter : -roundCounter) : ef.self;
            int dOpp = ef.oppUseRound ? (ef.opp >= 0 ? +roundCounter : -roundCounter) : ef.opp;
            
            // 월식: 일반 카드 수치 2배(Recon 제외, 수치만 배수. 리셋/정찰 같은 플래그는 그대로)
            if (currentDisaster == NaturalDisaster.Eclipse)
            {
                dSelf *= 2;
                dOpp  *= 2;
            }

            playerILife  += dSelf;
            playerIILife += dOpp;
            
            // ★ 카드 피해 누적 기록 추가
            lastCardDeltaP1 += (playerILife  - hpP_beforeCards);
            lastCardDeltaP2 += (playerIILife - hpA_beforeCards);

            if (ef.repSelf) ReplaceHand(playerIHands);   // Chaos(손패 리셋) 처리
            if (ef.repOpp)  ReplaceHand(playerIIHands);

            // (C) 라운드 종료: 재해 → 라운드 피로 → 드로우
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

            // ▶ 그 다음 패 채움(한파면 DrawToLimitByDisaster 내부가 2장 상한 유지)
            if (!IsLastRound)
            {
                DrawToLimitByDisaster(true);   // 플레이어
                DrawToLimitByDisaster(false);  // 상대
            }
            else
            {
                if (waitingChoice) { waitingChoice = false; OnChoiceClosed?.Invoke(); }
            }

            // ▶ Recon 공개를 먼저
            if (ef.reconSelf)
                lastSeenByP1 = new List<CardType>(playerIIHands.Take(3));
            if (ef.reconOpp)
                lastSeenByP2 = new List<CardType>(playerIHands.Take(3));

            // Peace: 라운드 총 변화량을 ±5로 캡(캡에 의한 보정은 재해 피해로 기록)
            if (currentDisaster == NaturalDisaster.Peace)
            {
                const int CAP = 5;
                void Cap(ref int hp, int start, ref int disasterDelta)
                {
                    int delta = hp - start;
                    if (delta > CAP) { int adj = delta - CAP; hp -= adj; disasterDelta -= adj; }
                    else if (delta < -CAP) { int adj = -CAP - delta; hp += adj; disasterDelta += adj; }
                }
                Cap(ref playerILife, hpP_StartOfRound, ref lastDisasterDeltaP1);
                Cap(ref playerIILife, hpA_StartOfRound, ref lastDisasterDeltaP2);
            }

            // ▶ 재해 종료 후 condition 적용
            var mgr = AgentManager.I;

            // P1이 에이전트일 때만 P1 적용
            if (currentP1Agent.HasValue)
            {
                mgr.ApplyConditionAfterRound(
                    currentP1Agent.Value,
                    lastCardDeltaP1 + lastDisasterDeltaP1,     // 내 변화량
                    lastCardDeltaP2 + lastDisasterDeltaP2,     // 상대 변화량(가학적 성격용)
                    playerILife,                                // 내 현재 HP
                    playerIILife                                // 상대 현재 HP
                );
            }

            // P2는 항상 에이전트이므로 항상 적용
            mgr.ApplyConditionAfterRound(
                currentP2Agent,
                lastCardDeltaP2 + lastDisasterDeltaP2,
                lastCardDeltaP1 + lastDisasterDeltaP1,
                playerIILife,
                playerILife
            );

            roundCounter++;

            var prev = currentDisaster;
            if ((roundCounter - 1) % disasterSpan == 0)
            {
                disasterIndex++;
                if (disasterIndex < disasterOrder.Count)
                    currentDisaster = disasterOrder[disasterIndex];
                else
                    currentDisaster = NaturalDisaster.Peace;

                // ▼ 추가: 전환 플래그
                if (currentDisaster == NaturalDisaster.ColdWave)
                {
                    coldWaveJustStartedP1 = coldWaveJustStartedP2 = true;
                }
                if (prev == NaturalDisaster.ColdWave && currentDisaster != NaturalDisaster.ColdWave)
                {
                    coldWaveRecoverThisRoundP1 = coldWaveRecoverThisRoundP2 = true;
                }

                OnDisasterStart(currentDisaster);
                RaiseDisasterUI();
            }

            ApplyPhaseStartBonusIfNeeded();
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
            carryAfterColdWaveP1 = carryAfterColdWaveP2 = false;

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
            NaturalDisaster.Peace      => "평화",
            NaturalDisaster.Meteorite  => "<color=grey>운석 충돌</color>",
            NaturalDisaster.Heatwave   => "<color=red>폭염</color>",
            NaturalDisaster.Lightning  => "<color=yellow>낙뢰</color>",
            NaturalDisaster.Storm      => "<color=black>폭풍</color>",
            NaturalDisaster.ColdWave   => "<color=blue>한파</color>",
            NaturalDisaster.Eclipse    => "<color=#888>월식</color>",
            NaturalDisaster.Sandstorm  => "<color=#caa566>황사</color>",
            _ => d.ToString()
        };

        string DisasterRule(NaturalDisaster d) => d switch
        {
            // Peace 변경: “라운드 총 변화량 |ΔHP| ≤ 5”
            NaturalDisaster.Peace => "\n<size=24>한 라운드 내 양초 변화량을\n5로 제한</size>",
            NaturalDisaster.Meteorite => "\n<size=24>각 참가자의 양초 * 2/3</size>",
            NaturalDisaster.Heatwave => "\n<size=24>Round마다 추가 양초 - 1</size>",
            NaturalDisaster.Lightning => "\n<size=24>Round마다 25% 확률로\n각 참가자의 양초 - 3</size>",
            NaturalDisaster.Storm => "\n<size=24>Round마다 10% 확률로\n제출 카드 교체</size>",
            NaturalDisaster.ColdWave => "\n<size=24>최대 패 수급 2장으로 제한</size>",
            NaturalDisaster.Eclipse => "\n<size=24>일반 카드의 효과를 2배로 증폭</size>",
            NaturalDisaster.Sandstorm => "\n<size=24>Pollution이 공개되면,\n각 참가자의 양초 - 1</size>",
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

                case NaturalDisaster.Storm:
                    // 제출 단계에서 이미 처리
                    break;

                case NaturalDisaster.ColdWave:
                    // 드로우 단계에서 처리(2장 상한)
                    break;
                case NaturalDisaster.Sandstorm:
                    {
                        bool polluted = (a == CardType.Pollution) || (b == CardType.Pollution);
                        if (polluted)
                        {
                            playerILife  = Mathf.Max(0, playerILife  - 1);
                            playerIILife = Mathf.Max(0, playerIILife - 1);
                            lastDisasterDeltaP1 -= 1;
                            lastDisasterDeltaP2 -= 1;
                            playerILost  |= playerILife  <= 0;
                            playerIILost |= playerIILife <= 0;
                        }
                        break;
                    }
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

            // H2H처럼 UI 구독자가 없거나 기능을 끈 경우 → 자동 드로우(Chaos 제외 1장)
            bool noUI = OnOfferChoiceForPlayer == null;
            if (!enableChoiceDrawForPlayer || noUI)
            {
                var c = DrawNonChaosFromPublic();              // 기존 DrawOne 대신 Chaos 제외
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
        bool carryAfterColdWaveP1 = false, carryAfterColdWaveP2 = false;
        private void DrawToLimitByDisaster(bool isP1)
        {
            if (IsMatchEnded) return;
            var hand = isP1 ? playerIHands : playerIIHands;

            int limit = (currentDisaster == NaturalDisaster.ColdWave) ? 2 : 3;

            // 1) 한파 '시작' 라운드: 이번 라운드만 드로우 스킵
            if (currentDisaster == NaturalDisaster.ColdWave)
            {
                bool justStarted = isP1 ? coldWaveJustStartedP1 : coldWaveJustStartedP2;
                if (justStarted)
                {
                    if (isP1) coldWaveJustStartedP1 = false; else coldWaveJustStartedP2 = false;
                    if (waitingChoice) { waitingChoice = false; OnChoiceClosed?.Invoke(); }
                    return; // 손패 2장 유지
                }
            }

            // 2) 한파 '종료' 라운드 복구: 선택 없이 즉시 3장까지
            bool recover = isP1 ? coldWaveRecoverThisRoundP1 : coldWaveRecoverThisRoundP2;
            if (recover && currentDisaster != NaturalDisaster.ColdWave)
            {
                while (hand.Count < 3)
                {
                    var c = DrawOne(publicDeck);
                    if (c == CardType.None) break;
                    hand.Add(c);
                }
                if (isP1) { coldWaveRecoverThisRoundP1 = false; OnPlayerHandChanged?.Invoke(new List<CardType>(playerIHands)); }
                else      { coldWaveRecoverThisRoundP2 = false; }
                return;
            }

            // 3) 평시/한파 진행 라운드: 상한까지 정상 드로우
            while (hand.Count < limit)
            {
                if (isP1 && enableChoiceDrawForPlayer)
                {
                    StartChoiceDrawForPlayer(); // 2장 중 1장 선택 UI
                    return; // 선택 콜백에서 이어서 채움
                }
                else if (!isP1 && enableChoiceDrawForAgent)
                {
                    StartChoiceDrawForAgent();  // 에이전트 선택 로직
                }
                else
                {
                    var c = DrawOne(publicDeck);
                    if (c == CardType.None) return;
                    hand.Add(c);
                }
            }
        }
        void GrantColdWaveCarryIfNeeded()
        {
            if (currentDisaster == NaturalDisaster.ColdWave) return;

            if (carryAfterColdWaveP1)
            {
                playerIHands.Add(DrawOne(publicDeck));
                playerIHands.Add(DrawOne(publicDeck));
                carryAfterColdWaveP1 = false;
                OnPlayerHandChanged?.Invoke(new List<CardType>(playerIHands));
            }
            if (carryAfterColdWaveP2)
            {
                playerIIHands.Add(DrawOne(publicDeck));
                playerIIHands.Add(DrawOne(publicDeck));
                carryAfterColdWaveP2 = false;
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
                var ctx = new RoundCtx
                {            // 라운드 상황 구성
                    round = roundCounter,
                    selfLife = playerIILife,
                    oppLife = playerILife,
                    lastSelf = lastSubmittedP2,
                    lastOpp = lastSubmittedP1
                };
                var input = new DecisionInput(playerIIHands, ctx, BuildUnseen(false));
                pick = currentP2.chooseFromTwo(c1, c2, input) ?? ChooseIndexForAgent(c1, c2);
            }
            else
            {
                pick = ChooseIndexForAgent(c1, c2); // 기존 휴리스틱
            }

            var chosen = (pick == 0) ? c1 : c2;
            var other = (pick == 0) ? c2 : c1;

            playerIIHands.Add(chosen);
            int pos = UnityEngine.Random.Range(0, publicDeck.Count + 1);
            publicDeck.Insert(pos, other);
        }
        // 내부 유틸: 라운드마다 선택 직전에 Condition 반영
        private void ApplyConditionIfAny(GameCore.Agent agent, bool isP1)
        {
            // 라운드 종료 시 ResolveRoundByIndex에서만 컨디션을 적용한다.
            // 여기서는 아무 것도 하지 않는다. (컴파일 에러를 유발하던 지역변수 의존 제거)
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
        // 페이즈 시작 보너스(동점이면 없음)
        bool phaseBonusAppliedThisRound = false;
        void ApplyPhaseStartBonusIfNeeded()
        {
            // 라운드 1,6,11,16… = (roundCounter-1) % disasterSpan == 0
            if ((roundCounter - 1) % disasterSpan != 0) return;
            if (phaseBonusAppliedThisRound) return;

            if (playerILife > playerIILife) playerILife += 1;
            else if (playerIILife > playerILife) playerIILife += 1;
            // 동점이면 아무도 보너스 없음

            phaseBonusAppliedThisRound = true;
        }
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