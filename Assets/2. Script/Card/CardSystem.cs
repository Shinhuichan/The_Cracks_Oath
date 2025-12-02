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
    // 1. Enum에 Investment 추가
    public enum CardType { None = 0, Cooperation, Doubt, Betrayal, Chaos, Pollution, Interrupt, Recon, Curse, Sacrifice, Investment }

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
        public readonly AgentList id; // ▼ [추가됨] AI 자신의 ID
        public List<Func<DecisionInput, CardType?>> rules = new();
        public Agent(string name, AgentList id) // ▼ [수정됨] 생성자에 id 추가
        {
            this.name = name;
            this.id = id; // ▼ [추가됨]
        }

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
        // ▼ 추가: 두 장 중 1장 선택 드로우용(0 또는 1 반환, null이면 시스템 기본 휴리스틱 사용)
        public Func<CardType, CardType, DecisionInput, int?> chooseFromTwo;
        public CardType[] fallback =
            { CardType.Cooperation, CardType.Doubt, CardType.Pollution, CardType.Betrayal, CardType.Chaos, CardType.Interrupt };
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
        public readonly AgentList selfID;
        public readonly AgentList opponentID; // ▼ [추가됨]

        public readonly List<CardType> hand;
        public readonly RoundCtx s;
        public readonly IReadOnlyDictionary<CardType, int> unseen;
        public readonly int unseenTotal;

        // ▼ [수정됨] 생성자에 opponentID 추가
        // ▼ [수정됨] 생성자에 opponentID 추가
        public DecisionInput(List<CardType> hand, RoundCtx s,
                             IReadOnlyDictionary<CardType, int> unseen,
                             AgentList selfID, AgentList opponentID) 
        {
            this.hand = hand;
            this.s = s;
            this.unseen = unseen ?? EmptyCounts;
            this.unseenTotal = this.unseen.Values.Sum();
            this.selfID = selfID;
            this.opponentID = opponentID; // ▼ [추가됨]
        }
        static readonly IReadOnlyDictionary<CardType, int> EmptyCounts =
            new Dictionary<CardType, int>
            {
                {CardType.Cooperation,0},{CardType.Doubt,0},{CardType.Betrayal,0},
                {CardType.Chaos,0},{CardType.Pollution,0},{CardType.Interrupt,0},
                {CardType.Recon,0}, {CardType.Curse,0}, {CardType.Sacrifice, 0},
                {CardType.Investment, 0} // ▼ 추가
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
        public int doubtCount = 20, betrayalCount = 3, chaosCount = 7, pollutionCount = 10, interruptCount = 4, reconCount = 6, curseCount = 0, sacrificeCount = 0, investmentCount = 0; // ▼ 추가: 투자 카드 개수

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
            public bool curseSelf, curseOpp; // ▼ 추가: 저주 부여 여부
            public bool invSelf, invOpp; // ▼ 추가: Investment 회복 적용 여부

            // 생성자 업데이트
            public Effect(int s, int o, bool rs=false, bool ro=false, bool sr=false, bool ornd=false, 
                          bool rSelf=false, bool rOpp=false, bool cSelf=false, bool cOpp=false,
                          bool iSelf=false, bool iOpp=false) // 생성자 파라미터 추가
            { 
                self = s; opp = o; repSelf = rs; repOpp = ro; selfUseRound = sr; oppUseRound = ornd; 
                reconSelf = rSelf; reconOpp = rOpp;
                curseSelf = cSelf; curseOpp = cOpp;
                invSelf = iSelf; invOpp = iOpp; // 초기화
            }
        }
        Dictionary<string, Effect> E;

        public int roundCounter = 1;

        public bool enableChoiceDrawForPlayer = true;            // 인스펙터에서 켜기
        public event System.Action<GameCore.CardType, GameCore.CardType> OnOfferChoiceForPlayer;
        
        public event System.Action OnChoiceClosed;                 // 선택 UI 닫기
        public event System.Action<System.Collections.Generic.List<GameCore.CardType>> OnPlayerHandChanged; // 손패 바뀜

        bool waitingChoice = false;
        GameCore.CardType pendingA, pendingB;

        // ▼ 추가: 저주 지속 라운드 (0이면 저주 없음)
        private int curseDurationP1 = 0;
        private int curseDurationP2 = 0;

        // ▼ 추가: Sacrifice 누적 제출 횟수 추적
        private int sacrificePlayedP1 = 0;
        private int sacrificePlayedP2 = 0;

        // ▼ 추가: Investment 누적 카운트 (나 + 상대)
        private int globalInvestmentCount = 0;

        // 한파 제어 플래그
        bool coldWaveJustStartedP1, coldWaveJustStartedP2;   // 한파가 ‘시작한’ 그 라운드에서만 드로우 스킵
        bool coldWaveRecoverThisRoundP1, coldWaveRecoverThisRoundP2; // 한파가 ‘끝난’ 라운드에서 2장 복구
        GameCore.CardSystem.NaturalDisaster lastDisaster;

        // 자연재해
        // 1. Enum에 새 재해 추가
        public enum NaturalDisaster
        {
            None = 0,
            Peace,      // 평화
            Lightning,  // 낙뢰
            Sandstorm,  // 황사
            Meteorite,  // 운석 충돌
            Heatwave,   // 폭염
            Eclipse,    // 월식
            Storm,      // 폭풍
            ColdWave,   // 한파
            
            // ▼ [추가됨]
            Drought,    // 가뭄: 회복 불가
            Plague,     // 역병: 같은 카드 연속 제출 시 패널티
            MidnightSun // 백야: 라운드 기본 소모 0
        }

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

        // ▼▼▼ [이 부분을 추가해주세요] ▼▼▼
        public void ManualInitialize()
        {
            if (bootstrapped) return;
            bootstrapped = true;

            ApplyModeIfAvailable();     // 모드 적용
            ResetForNewMatch();         // 덱/변수 초기화
            BuildEffects_WithRecon();   // 상성표 빌드 (이게 없으면 오류 발생)
            
            // 시뮬레이터에서는 UI가 없으므로 RaiseDisasterUI 등은 호출되어도 무시됨
        }
        // ▲▲▲ [추가 완료] ▲▲▲

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
            
            currentP1 = p1;
            currentP2 = p2;

            // 손패가 부족하면 먼저 채움(비주얼 UI 없이)
            DrawToLimitByDisaster(true);
            DrawToLimitByDisaster(false);

            if (playerIHands.Count == 0 || playerIIHands.Count == 0) return;

            var unseenForP1 = BuildUnseen(true);
            var unseenForP2 = BuildUnseen(false);

            // ▼ [수정됨] 생성자에 p1.id와 p2.id를 전달합니다.
            var pick1 = p1.Choose(new DecisionInput(playerIHands,  ctxP1, unseenForP1, p1.id, p2.id));
            var pick2 = p2.Choose(new DecisionInput(playerIIHands, ctxP2, unseenForP2, p2.id, p1.id));

            int idx1 = IndexOfFirst(playerIHands, pick1);
            int idx2 = IndexOfFirst(playerIIHands, pick2);
            ResolveRoundByIndex(idx1, idx2);
        }

        public void ResolveRoundByIndex(int p1Index, int p2Index)
        {
            phaseBonusAppliedThisRound = false;
            if (playerILost || playerIILost) return;

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

            // ▼▼▼ [신규] Sacrifice 카운트 및 특수 승리 체크 ▼▼▼
            if (a == CardType.Sacrifice) sacrificePlayedP1++;
            if (b == CardType.Sacrifice) sacrificePlayedP2++;

            // 4장 모으면 즉시 승리 (상대를 패배 처리)
            bool p1SacComplete = sacrificePlayedP1 >= 4;
            bool p2SacComplete = sacrificePlayedP2 >= 4;

            if (p1SacComplete) 
            {
                playerIILost = true;
                IsSacrificeWinP1 = true; // ★ P1 특수 승리 확정
            }
            if (p2SacComplete) 
            {
                playerILost = true;
                IsSacrificeWinP2 = true; // ★ P2 특수 승리 확정
            }
            
            // 특수 승리가 발생했더라도, 동시 달성 시 무승부 처리를 위해 아래 로직 진행
            // (이미 Lost 상태이므로 결과에는 반영됨)
            // ▲▲▲ [신규 구현 종료] ▲▲▲

            // 4장 모으면 즉시 승리 (상대를 패배 처리)
            // 동시 달성 시 무승부(둘 다 패배) 처리가 되도록 플래그 설정
            bool p1Win = sacrificePlayedP1 >= 4;
            bool p2Win = sacrificePlayedP2 >= 4;

            if (p1Win) playerIILost = true;
            if (p2Win) playerILost = true;

            // 특수 승리가 발생했으면 여기서 라운드 종료 처리 가능
            // 단, '일반 카드 효과 처리 단계'라고 하셨으므로 아래 데미지 계산은 수행하되,
            // 이미 Lost 상태이므로 결과에 반영됩니다.
            // ▲▲▲ [신규 구현 종료] ▲▲▲

            // ▼▼▼ [신규] Investment 카운트 ▼▼▼
            // 이번 라운드에 Investment가 제출되었다면 전체 카운트 증가
            if (a == CardType.Investment) globalInvestmentCount++;
            if (b == CardType.Investment) globalInvestmentCount++;

            // 회복량 계산: (누적 사용 수 - 1), 최소 0
            int investHealAmount = Mathf.Max(0, globalInvestmentCount - 1);
            // ▲▲▲ [신규 구현] ▲▲▲

            var ef = E[$"{a}-{b}"];

            int hpP_beforeCards = playerILife;
            int hpA_beforeCards = playerIILife;

            // (B) 일반 카드 효과 → Chaos
            int dSelf = ef.selfUseRound ? (ef.self >= 0 ? +roundCounter : -roundCounter) : ef.self;
            int dOpp = ef.oppUseRound ? (ef.opp >= 0 ? +roundCounter : -roundCounter) : ef.opp;

            // ▼ [신규] Investment 회복 로직 적용
            // 상성상 회복이 가능한 상황(invSelf/Opp == true)이라면 계산된 회복량 적용
            if (ef.invSelf) dSelf = investHealAmount;
            if (ef.invOpp)  dOpp  = investHealAmount;
            
            // 월식: 일반 카드 수치 2배(Recon 제외, 수치만 배수. 리셋/정찰 같은 플래그는 그대로)
            if (currentDisaster == NaturalDisaster.Eclipse)
            {
                dSelf *= 2;
                dOpp  *= 2;
            }
            // ▼ [추가됨] 가뭄(Drought) 효과: 회복(양수) 무효화
            if (currentDisaster == NaturalDisaster.Drought)
            {
                if (dSelf > 0) dSelf = 0;
                if (dOpp > 0) dOpp = 0;
            }

            playerILife  += dSelf;
            playerIILife += dOpp;
            
            // ★ [수정] 카드 효과 직후 최대 체력 제한
            playerILife  = Mathf.Min(playerILife, startLife);
            playerIILife = Mathf.Min(playerIILife, startLife);
            
            // ★ 카드 피해 누적 기록 추가
            lastCardDeltaP1 += (playerILife  - hpP_beforeCards);
            lastCardDeltaP2 += (playerIILife - hpA_beforeCards);

            if (ef.repSelf) ReplaceHand(playerIHands);   // Chaos(손패 리셋) 처리
            if (ef.repOpp)  ReplaceHand(playerIIHands);

            // (C) 라운드 종료: 재해 → 라운드 피로 → 드로우
            // [중요] 이때 lastSubmittedP1/P2는 여전히 "이전 라운드"의 카드여야 합니다.
            ApplyDisasterEndEffects(a, b); 

            // [수정] 재해 판정이 끝난 '후'에 이번 라운드 카드로 갱신합니다.
            lastSubmittedP1 = a;
            lastSubmittedP2 = b;
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

            // ▼▼▼ [신규] Curse 효과 처리 (Recon 이후) ▼▼▼
            // 1. 저주 부여 (이번 턴 상성에 따라)
            // 상대가 방어(Doubt, Interrupt)가 아니어서 ef.curseOpp가 true라면, 상대에게 2라운드 저주 부여
            if (ef.curseOpp) curseDurationP2 = 2; 
            if (ef.curseSelf) curseDurationP1 = 2; // 상대가 나에게 저주를 걸었을 때

            // 2. 저주 데미지 처리 (라운드 종료 시점)
            // 지속 시간이 남아있으면 데미지 1 입히고 시간 감소
            if (curseDurationP1 > 0)
            {
                playerILife = Mathf.Max(0, playerILife - 1);
                lastCardDeltaP1 -= 1; // 카드에 의한 피해로 간주
                curseDurationP1--;
            }
            if (curseDurationP2 > 0)
            {
                playerIILife = Mathf.Max(0, playerIILife - 1);
                lastCardDeltaP2 -= 1;
                curseDurationP2--;
            }
            // ▲▲▲ [신규 구현 종료] ▲▲▲

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

            // ▼▼▼ [학습 트리거 추가] ▼▼▼
            // P1 학습
            if (currentP1Agent.HasValue)
            {
                AgentManager.I.LearnFromRound(
                    currentP1Agent.Value,
                    lastSubmittedP1,                  // 내가 낸 카드
                    lastCardDeltaP1 + lastDisasterDeltaP1, // 이번 라운드 나의 순수 득실
                    playerILife,
                    playerIILife
                );
            }

            // P2 학습 (P2는 항상 Agent임)
            AgentManager.I.LearnFromRound(
                currentP2Agent,
                lastSubmittedP2,
                lastCardDeltaP2 + lastDisasterDeltaP2,
                playerIILife,
                playerILife
            );
            // ▲▲▲ [학습 트리거 종료] ▲▲▲

            // ▼▼▼ [PatternBreaker 추가] 패턴 관찰 및 분석 연결 ▼▼▼
            
            // 1. 관찰 (Observe): 이번 라운드에 상대가 무엇을 냈는지 기록
            if (currentP1Agent.HasValue)
            {
                // P1(서유리 등)이 P2를 관찰
                AgentManager.I.ObserveOpponentMove(currentP1Agent.Value, currentP2Agent, lastSubmittedP2);
            }
            
            // P2가 P1을 관찰 (P1이 사람이면 null 대신 임시 ID 사용)
            AgentManager.I.ObserveOpponentMove(currentP2Agent, currentP1Agent ?? AgentList.백무적, lastSubmittedP1);

            // 2. 분석 (Analyze): 매치가 끝났다면 패턴을 추출하여 영구 저장
            if (IsMatchEnded)
            {
                if (currentP1Agent.HasValue)
                    AgentManager.I.AnalyzeMatchPatterns(currentP1Agent.Value, currentP2Agent);
                
                AgentManager.I.AnalyzeMatchPatterns(currentP2Agent, currentP1Agent ?? AgentList.백무적);
            }
            // ▲▲▲ [PatternBreaker 종료] ▲▲▲

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
                {CardType.Chaos,0},{CardType.Pollution,0},{CardType.Interrupt,0},{CardType.Recon,0},
                {CardType.Curse, 0}, {CardType.Sacrifice, 0},
                {CardType.Investment, 0} // ▼ 추가
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
            // ★ [추가] 특수 승리 플래그 초기화
            IsSacrificeWinP1 = false;
            IsSacrificeWinP2 = false;
            publicDeck.Clear();
            playerIHands.Clear();
            playerIIHands.Clear();
            discardCards.Clear();
            lastSeenByP1.Clear();
            lastSeenByP2.Clear();
            roundCounter = 1;

            // ▼ 추가: 저주 상태 초기화
            curseDurationP1 = 0;
            curseDurationP2 = 0;
            // ▼ 추가: 카운트 초기화
            sacrificePlayedP1 = 0;
            sacrificePlayedP2 = 0;
            globalInvestmentCount = 0; // ▼ 추가: 초기화

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
            Add(publicDeck, CardType.Curse, curseCount);
            Add(publicDeck, CardType.Sacrifice, sacrificeCount); // ▼ 추가
            Add(publicDeck, CardType.Investment, investmentCount); // ▼ 추가: 덱에 넣기

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
            NaturalDisaster.MidnightSun  => "<color=#00FFFF>백야</color>",
            NaturalDisaster.Drought    => "<color=#A52A2A>가뭄</color>",
            NaturalDisaster.Plague     => "<color=#006400>역병</color>",
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
            NaturalDisaster.MidnightSun => "\n<size=24>Round마다 양초 - 0</size>",
            NaturalDisaster.Drought => "\n<size=24>회복 효과 무효화</size>",
            NaturalDisaster.Plague => "\n<size=24>같은 카드 연속 제출 시\n양초 - 2</size>",
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
                case NaturalDisaster.MidnightSun: return 0; // 백야
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
                case NaturalDisaster.Plague:
                    {
                        // 같은 카드 연속 제출 시 양초 -2
                        if (a == lastSubmittedP1 && a != CardType.None)
                        {
                            playerILife = Mathf.Max(0, playerILife - 2);
                            lastDisasterDeltaP1 -= 2;
                            playerILost |= playerILife <= 0;
                        }
                        if (b == lastSubmittedP2 && b != CardType.None)
                        {
                            playerIILife = Mathf.Max(0, playerIILife - 2);
                            lastDisasterDeltaP2 -= 2;
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
        
        // ▼ [추가됨] 플레이어 UI 전용 드로우 함수
        public void StartChoiceDrawForPlayer()
        {
            if (waitingChoice) return; // 이미 선택창이 떠있으면 중복 실행 방지
            if (publicDeck.Count == 0) return;

            // 1. Chaos가 아닌 카드 1장 뽑기
            var c1 = DrawNonChaosFromPublic();
            if (c1 == CardType.None) return; // 덱 고갈

            // 2. 덱에 1장밖에 없었으면 선택 없이 그냥 가짐
            if (publicDeck.Count == 0)
            {
                playerIHands.Add(c1);
                OnPlayerHandChanged?.Invoke(new List<CardType>(playerIHands));
                return;
            }

            // 3. 두 번째 카드 뽑기
            var c2 = DrawNonChaosFromPublic();
            if (c2 == CardType.None) 
            {
                playerIHands.Add(c1);
                OnPlayerHandChanged?.Invoke(new List<CardType>(playerIHands));
                return;
            }

            // 4. 선택 대기 상태로 전환 및 UI 이벤트 발생
            pendingA = c1;
            pendingB = c2;
            waitingChoice = true;
            
            // PlayerBattle.cs가 이 이벤트를 받아 UI를 띄움
            OnOfferChoiceForPlayer?.Invoke(pendingA, pendingB);
        }
        // ───────────────────────────────────────────────
        // 누락된 프로퍼티: 매치 종료 여부
        public bool IsMatchEnded => playerILost || playerIILost || IsLastRound;
        // ───────────────────────────────────────────────

        // 1. 기존 StartChoiceDrawForAgent를 삭제하거나 이름을 변경하여 
        //    양쪽 플레이어 모두 처리 가능한 일반화된 함수로 만듭니다.
        void ExecuteAgentDraft(Agent agent, List<CardType> hand, bool isP1)
        {
            if (publicDeck.Count == 0) return;

            // Chaos 제외하고 2장 뽑기 시도 (덱 상황에 따라 1장일 수도 있음)
            var c1 = DrawNonChaosFromPublic();
            if (c1 == CardType.None) return; // 덱 고갈

            // 두 번째 장이 없으면 그냥 첫 번째 장 먹고 끝
            if (publicDeck.Count == 0)
            {
                hand.Add(c1);
                return; 
            }

            var c2 = DrawNonChaosFromPublic();
            if (c2 == CardType.None) // 혹시라도 c1 뽑고 비었을 경우
            {
                hand.Add(c1);
                return;
            }

            int pick;
            // 에이전트별 선택 로직 (Draft 전용)
            if (agent != null && agent.chooseFromTwo != null)
            {
                var ctx = new RoundCtx { /* ...생략... */ };
                var unseen = BuildUnseen(isP1);

                // ▼ [수정됨] 상대방 ID 추론하여 전달
                // (currentP2가 null이면 Human/None으로 간주하여 0 전달)
                AgentList oppID = isP1 
                    ? (currentP2 != null ? currentP2.id : (AgentList)0) 
                    : (currentP1 != null ? currentP1.id : (AgentList)0);

                var input = new DecisionInput(hand, ctx, unseen, agent.id, oppID);

                pick = agent.chooseFromTwo(c1, c2, input) ?? ChooseIndexForAgent(c1, c2, hand, isP1);
            }
            else
            {
                // 기본 휴리스틱 사용
                pick = ChooseIndexForAgent(c1, c2, hand, isP1);
            }

            var chosen = (pick == 0) ? c1 : c2;
            var other = (pick == 0) ? c2 : c1;

            hand.Add(chosen);
            
            // 선택받지 못한 카드는 덱의 랜덤한 위치로 반환
            int pos = UnityEngine.Random.Range(0, publicDeck.Count + 1);
            publicDeck.Insert(pos, other);
            
            // P1인 경우 손패 변경 이벤트 알림
            if (isP1) OnPlayerHandChanged?.Invoke(new List<CardType>(hand));
        }
        // 2. 휴리스틱 함수도 손패(hand)와 상황을 알 수 있게 수정
        int ChooseIndexForAgent(CardType a, CardType b, List<CardType> currentHand, bool isP1)
        {
            // Chaos 회피
            if (a == CardType.Chaos && b != CardType.Chaos) return 1;
            if (b == CardType.Chaos && a != CardType.Chaos) return 0;

            // 현재 내 손패 상황
            bool hasAtk = currentHand.Contains(CardType.Betrayal) || currentHand.Contains(CardType.Pollution);
            bool hasDef = currentHand.Contains(CardType.Doubt) || currentHand.Contains(CardType.Interrupt);

            int myLife = isP1 ? playerILife : playerIILife;
            // int oppLife = isP1 ? playerIILife : playerILife; // 필요시 사용

            int R = Mathf.Max(1, roundCounter);
            // 배신 카드 집착 조건: 내 체력이 낮을 때 더 공격적으로? (원래 로직 유지하되 myLife 참조 수정)
            bool killA = (a == CardType.Betrayal) && (myLife <= R); 
            bool killB = (b == CardType.Betrayal) && (myLife <= R);
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

            // Sacrifice 우선순위 로직:
            // 이미 3장을 냈다면 마지막 1장은 무조건 1순위(승리 확정)
            int mySacrificeCount = isP1 ? sacrificePlayedP1 : sacrificePlayedP2;
            if (mySacrificeCount == 3)
            {
                if (a == CardType.Sacrifice) return 0;
                if (b == CardType.Sacrifice) return 1;
            }

            // 점수제 비교
            int Score(CardType t) => t switch
            {
                CardType.Betrayal => 100,
                CardType.Curse => 95,      // ▼ 추가: 꽤 높은 우선순위 부여
                CardType.Doubt => 90,
                CardType.Interrupt => 85,
                CardType.Pollution => 80,
                CardType.Sacrifice => 65,  // ▼ Cooperation보다 약간 높게 설정 (모으는 전략)
                CardType.Cooperation => 60,
                CardType.Recon => 50,
                CardType.Chaos => 10,
                _ => 0
            };
            int sa = Score(a), sb = Score(b);
            if (sa != sb) return sa > sb ? 0 : 1;

            return UnityEngine.Random.value < 0.5f ? 0 : 1;
        }
        bool carryAfterColdWaveP1 = false, carryAfterColdWaveP2 = false;
        
        // 3. 핵심: DrawToLimitByDisaster 수정
        private void DrawToLimitByDisaster(bool isP1)
        {
            if (IsMatchEnded) return;
            var hand = isP1 ? playerIHands : playerIIHands;

            int limit = (currentDisaster == NaturalDisaster.ColdWave) ? 2 : 3;

            // (한파 시작/종료 로직은 기존 코드 유지...)
            if (currentDisaster == NaturalDisaster.ColdWave)
            {
                bool justStarted = isP1 ? coldWaveJustStartedP1 : coldWaveJustStartedP2;
                if (justStarted)
                {
                    if (isP1) coldWaveJustStartedP1 = false; else coldWaveJustStartedP2 = false;
                    if (waitingChoice) { waitingChoice = false; OnChoiceClosed?.Invoke(); }
                    return; 
                }
            }
            bool recover = isP1 ? coldWaveRecoverThisRoundP1 : coldWaveRecoverThisRoundP2;
            if (recover && currentDisaster != NaturalDisaster.ColdWave)
            {
                // ▼ [수정] 복구 시에도 덱 고갈 체크 (선택 사항이나, 안전을 위해 추가 권장)
                while (hand.Count < 3)
                {
                    // 일반 드로우 시에도 카드가 아예 없으면 멈춰야 함 (1장 드로우는 DrawOne 내부에서 처리되지만, 2장 드로우 시 체크)
                    if (publicDeck.Count + discardCards.Count == 0) break; 
                    
                    var c = DrawOne(publicDeck);
                    if (c == CardType.None) break;
                    hand.Add(c);
                }
                if (isP1) { coldWaveRecoverThisRoundP1 = false; OnPlayerHandChanged?.Invoke(new List<CardType>(playerIHands)); }
                else      { coldWaveRecoverThisRoundP2 = false; }
                return;
            }

            // ▼▼▼ [핵심 수정 구간] ▼▼▼
            while (hand.Count < limit)
            {
                // 1. 덱 고갈 체크 및 페널티/회수 처리
                // 선택 드로우(2장 필요)를 시도하기 전에 확인
                if (HandleDeckExhaustion(isP1)) 
                {
                    return; // 카드가 부족하여 페널티를 받고 카드를 회수했으므로 드로우 중단
                }

                if (isP1)
                {
                    if (enableChoiceDrawForPlayer)
                    {
                        StartChoiceDrawForPlayer(); 
                        return; 
                    }
                    else if (currentP1 != null) 
                    {
                        ExecuteAgentDraft(currentP1, playerIHands, true);
                    }
                    else
                    {
                        var c = DrawOne(publicDeck);
                        if (c == CardType.None) return;
                        hand.Add(c);
                    }
                }
                else
                {
                    if (enableChoiceDrawForAgent)
                    {
                        ExecuteAgentDraft(currentP2, playerIIHands, false);
                    }
                    else
                    {
                        var c = DrawOne(publicDeck);
                        if (c == CardType.None) return;
                        hand.Add(c);
                    }
                }
            }
        }

        // ▼ [신규] 덱 고갈 시 페널티 및 카드 회수 로직
        bool HandleDeckExhaustion(bool isP1)
        {
            // 선택 드로우를 하려면 최소 2장이 필요함 (공유 덱 + 버린 카드 더미)
            int totalAvailable = publicDeck.Count + discardCards.Count;

            if (totalAvailable < 2)
            {
                // 1. 양초 1개 끄기 (0 미만으로는 안 내려감)
                if (isP1) playerILife = Mathf.Max(0, playerILife - 1);
                else      playerIILife = Mathf.Max(0, playerIILife - 1);

                // 2. 제출했던 카드 회수 (버리지 않고 되돌리기)
                // 이번 라운드에 냈던 카드는 이미 UseCard()를 통해 discardCards에 들어가 있는 상태입니다.
                // 따라서 discardCards에서 찾아서 다시 Hand로 옮겨줍니다.
                CardType lastCard = isP1 ? lastSubmittedP1 : lastSubmittedP2;
                List<CardType> hand = isP1 ? playerIHands : playerIIHands;

                if (lastCard != CardType.None)
                {
                    // 버린 카드 더미에서 해당 카드를 찾아 제거하고
                    if (discardCards.Remove(lastCard)) 
                    {
                        // 다시 손패로 가져옴
                        hand.Add(lastCard);
                        
                        // P1이면 UI 갱신 알림
                        if (isP1) OnPlayerHandChanged?.Invoke(new List<CardType>(hand));
                    }
                }

                // 3. 패배 조건 체크 (양초가 꺼졌을 수 있으므로)
                playerILost |= playerILife <= 0;
                playerIILost |= playerIILife <= 0;

                return true; // 고갈 처리됨 (드로우 중단)
            }

            return false; // 카드 충분함 (드로우 진행)
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
        

        // 강제 패배 처리 (테스트용)
        public void ForceLoseP1() { playerILost = true; }
        public void ForceLoseP2() { playerIILost = true; }

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

        // ★ [정밀 구현] 승리 원인 판별을 위한 프로퍼티
        public bool IsSacrificeWinP1 { get; private set; }
        public bool IsSacrificeWinP2 { get; private set; }
        private CardType UseCard(List<CardType> hand, int index)
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
            var C=CardType.Cooperation; var D=CardType.Doubt; var B=CardType.Betrayal; var X=CardType.Chaos; var P=CardType.Pollution; var I=CardType.Interrupt; var Rn=CardType.Recon; var Cu=CardType.Curse; var S=CardType.Sacrifice; var Inv=CardType.Investment;// 단축어

            E[$"{C}-{C}"]=new(+1,+1);
            E[$"{C}-{D}"]=new(+1,0);
            E[$"{C}-{B}"]=new(-1,+1, false,false, true,false);
            E[$"{C}-{X}"]=new(+1,0, false,true);
            E[$"{C}-{P}"]=new(-1,+1);
            E[$"{C}-{I}"]=new(+1,-1);
            E[$"{C}-{Rn}"]=new(+1,0, false,false,false,false, false, true);
            E[$"{C}-{Cu}"] = new(0, 0, cSelf: true);
            E[$"{C}-{S}"] = new(+1, -1);
            E[$"{C}-{Inv}"] = new(+1, 0, iOpp: true);

            E[$"{D}-{C}"]=new(0,+1);
            E[$"{D}-{D}"]=new(0,0);
            E[$"{D}-{B}"]=new(+1,-1, false,false, false,true);
            E[$"{D}-{X}"]=new(0,0, false,true);
            E[$"{D}-{P}"]=new(0,-1);
            E[$"{D}-{I}"]=new(-1,+1);
            E[$"{D}-{Rn}"]=new(0,0, false,false,false,false, false, true);
            E[$"{D}-{Cu}"]=new(0, 0, cOpp: true);
            E[$"{D}-{S}"] = new(0, -1);
            E[$"{D}-{Inv}"] = new(0, 0, iOpp: true);

            E[$"{B}-{C}"]=new(+1,-1, false,false, false,true);
            E[$"{B}-{D}"]=new(-1,+1, false,false, true,false);
            E[$"{B}-{B}"]=new(-1,-1, false,false, true,true);
            E[$"{B}-{X}"]=new(+1,-1, false,false, false,true);
            E[$"{B}-{P}"]=new(+1,-1, false,false, false,true);
            E[$"{B}-{I}"]=new(-1,+1);
            E[$"{B}-{Rn}"]=new(+1,-1, false,false, false,true);
            E[$"{B}-{Cu}"]=new(+1, -1, false, false, false, true, cSelf: true);
            E[$"{B}-{S}"]=new(+1,-2, false,false, false,true);
            E[$"{B}-{Inv}"] = new(+1, -1, false, false, false, true, iOpp: false);

            E[$"{X}-{C}"]=new(0,+1,  true,false);
            E[$"{X}-{D}"]=new(0,0,   true,false);
            E[$"{X}-{B}"]=new(-1,+1, true,false, true,false);
            E[$"{X}-{X}"]=new(0,0,   true,true);
            E[$"{X}-{P}"]=new(-1,0,  true,false);
            E[$"{X}-{I}"]=new(0,-1,  true,false);
            E[$"{X}-{Rn}"]=new(0,0,  true, false, false, false, false, true);
            E[$"{X}-{Cu}"] = new(0, 0, true, false, cSelf: true);
            E[$"{X}-{S}"] = new(0, -1, true, false);
            E[$"{X}-{Inv}"] = new(0, 0, true, false, iOpp: true);

            E[$"{P}-{C}"]=new(+1, -1);
            E[$"{P}-{D}"]=new(-1, 0);
            E[$"{P}-{B}"]=new(-1, +1, false, false, true,false);
            E[$"{P}-{X}"]=new(0, -1,  false, true);
            E[$"{P}-{P}"]=new(-1, -1);
            E[$"{P}-{I}"]=new(-1, +1);
            E[$"{P}-{Rn}"]=new(0, -1, false, false, false, false, false, true);
            E[$"{P}-{Cu}"] = new(0, -1, cSelf: true);
            E[$"{P}-{S}"] = new(-1, -2);
            E[$"{P}-{Inv}"] = new(+1, -1, iOpp: false);

            E[$"{I}-{C}"]=new(-1,+1);
            E[$"{I}-{D}"]=new(+1,-1);
            E[$"{I}-{B}"]=new(+1,-1);
            E[$"{I}-{X}"]=new(-1,0, false,true);
            E[$"{I}-{P}"]=new(+1,-1);
            E[$"{I}-{I}"]=new(0,0);
            E[$"{I}-{Rn}"]=new(-1,0);
            E[$"{I}-{Cu}"] = new(+1, -1, cSelf: false);
            E[$"{I}-{S}"] = new(+1, -2);
            E[$"{I}-{Inv}"] = new(-1, +1, iOpp: true);

            E[$"{Rn}-{C}"]=new(0,+1,  false,false,false,false, true,false);
            E[$"{Rn}-{D}"]=new(0,0,   false,false,false,false, true,false);
            E[$"{Rn}-{B}"]=new(-1,+1, false,false, true,false,  true,false);
            E[$"{Rn}-{X}"]=new(0,0,   false,true, false,false,  true,false);
            E[$"{Rn}-{P}"]=new(-1,0,  false,false,false,false,  true,false);
            E[$"{Rn}-{I}"]=new(0,-1,  false,false,false,false,  true,false);
            E[$"{Rn}-{Rn}"]=new(0,0,  false,false,false,false,  true,true);
            E[$"{Rn}-{Cu}"] = new(0, 0, false,false,false,false, true,false, cSelf: true);
            E[$"{Rn}-{S}"] = new(0, -1, false,false,false,false, true,false);
            E[$"{Rn}-{Inv}"] = new(0, 0, false, false, false, false, true, false, iOpp: true);

            E[$"{Cu}-{C}"] = new(+1, +1, cOpp: true);
            E[$"{Cu}-{D}"] = new(0, 0, cSelf: true);
            E[$"{Cu}-{B}"] = new(-1, +1, false, false, true, false, cOpp: true);
            E[$"{Cu}-{X}"] = new(0, 0, false, true, cOpp: true);
            E[$"{Cu}-{P}"] = new(-1, 0, cOpp: true);
            E[$"{Cu}-{I}"] = new(-1, +1);
            E[$"{Cu}-{Rn}"] = new(0, 0, false, false, false, false, false, true, cOpp: true);
            E[$"{Cu}-{Cu}"] = new(0, 0, cSelf: true, cOpp: true);
            E[$"{Cu}-{S}"] = new(0, -1, cOpp: true);
            E[$"{Cu}-{Inv}"] = new(0, 0, cOpp: true, iOpp: false);

            E[$"{S}-{C}"] = new(-1, +1);
            E[$"{S}-{D}"] = new(-1, 0);
            E[$"{S}-{B}"] = new(-2, +1, false, false, true, false);
            E[$"{S}-{X}"] = new(-1, 0, false, true);
            E[$"{S}-{P}"] = new(-2, -1);
            E[$"{S}-{I}"] = new(-1, +1);
            E[$"{S}-{Rn}"] = new(-1, 0, false, false, false, false, false, true);
            E[$"{S}-{Cu}"] = new(0, -1, cSelf: true);
            E[$"{S}-{S}"] = new(-1, -1);
            E[$"{S}-{Inv}"] = new(-1, 0, iOpp: true);

            E[$"{Inv}-{C}"] = new(0, +1, iSelf: true);
            E[$"{Inv}-{D}"] = new(0, -1, iSelf: true); // 회복 실패
            E[$"{Inv}-{B}"] = new(-1, +1, false, false, true, false, iSelf: false); // Inv: -1, B: +Round]
            E[$"{Inv}-{X}"] = new(0, 0, false, true, iSelf: true);
            E[$"{Inv}-{P}"] = new(-1, +1, iSelf: false);
            E[$"{Inv}-{I}"] = new(+1, -1, iSelf: true);
            E[$"{Inv}-{Rn}"] = new(0, 0, false, false, false, false, false, true, iSelf: true);
            E[$"{Inv}-{Cu}"] = new(0, 0, cSelf: true, iSelf: false);
            E[$"{Inv}-{S}"] = new(0, -1, iSelf: true);
            // ▼ [FIX] Add this missing line for Investment vs Investment
            E[$"{Inv}-{Inv}"] = new(0, 0, iSelf: true, iOpp: true);
        } 
    }
}