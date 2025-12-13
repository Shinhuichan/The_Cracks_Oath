using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using CustomInspector;

#region Data Structures

// 📜 [신규] 주문 단위 정의 (호가창 Entry)
public class Order
{
    public long price;
    public long amount;
    public bool isPlayer;
    public AIInvestor ai; // 주문을 넣은 AI (AI가 아닐 경우 null)

    public Order(long _price, long _amount, bool _isPlayer = true, AIInvestor _ai = null)
    {
        price = _price;
        amount = _amount;
        isPlayer = _isPlayer;
        ai = _ai;
    }
}

// 🕯️ [신규] 캔들 데이터 (OHLC)
[System.Serializable]
public struct StockCandle
{
    public int open;  // 시가
    public int close; // 종가
    public int high;  // 고가
    public int low;   // 저가
}

// 📈 런타임 주식 객체 (실시간 데이터)
[System.Serializable]
public class RuntimeStock
{
    public StockData data;
    
    // 🌟 [핵심 변경] 가격이 변할 때마다 캔들(High/Low) 자동 갱신
    [SerializeField] private int _currentPrice;
    public int currentPrice
    {
        get => _currentPrice;
        set
        {
            _currentPrice = value;
            UpdateCurrentCandle(value); // 가격 변화 감지
        }
    }

    public int previousPrice;
    public long remainShares;
    public bool isDelisting;
    public bool isLocked = false;

    public float dynamicEventWeight;

    public List<Order> BuyOrders = new List<Order>();
    public List<Order> SellOrders = new List<Order>();
    
    // 🕯️ [신규] 캔들 차트 데이터
    public List<StockCandle> candleHistory = new List<StockCandle>();
    public StockCandle currentTurnCandle; // 현재 진행 중인 턴의 캔들

    // (호환성 유지용)
    public List<int> priceHistory = new List<int>(); 
    public List<Vector2> priceRangeHistory = new List<Vector2>();

    public RuntimeStock(StockData sourceData)
    {
        data = sourceData;
        _currentPrice = sourceData.startPrice; // 초기값 직접 할당
        previousPrice = sourceData.startPrice;
        remainShares = sourceData.totalShares;
        isDelisting = false;
        dynamicEventWeight = sourceData.eventWeight;
        
        priceHistory.Add(sourceData.startPrice);
        InitializeOrderBook();
        
        // 첫 캔들 초기화
        StartNewCandle();
    }

    // 🕯️ [신규] 새 턴 시작 시 캔들 초기화
    public void StartNewCandle()
    {
        currentTurnCandle = new StockCandle
        {
            open = _currentPrice,
            close = _currentPrice,
            high = _currentPrice,
            low = _currentPrice
        };
    }

    // 🕯️ [신규] 가격 변동 시 고가/저가/종가 갱신
    private void UpdateCurrentCandle(int price)
    {
        if (price > currentTurnCandle.high) currentTurnCandle.high = price;
        if (price < currentTurnCandle.low) currentTurnCandle.low = price;
        currentTurnCandle.close = price;
    }

    // 🕯️ [신규] 턴 종료 시 캔들 확정 및 저장
    public void FinalizeCandle()
    {
        candleHistory.Add(currentTurnCandle);
        if (candleHistory.Count > 50) // 최적화를 위해 100개까지만 유지 (조절 가능)
        {
            candleHistory.RemoveAt(0);
        }
        
        // (기존 그래프 호환용 데이터도 여기서 업데이트)
        AddPriceHistory(_currentPrice);
        
        // 다음 턴 준비
        StartNewCandle();
    }
    
    // (기존) 가격 기록 함수
    public void AddPriceHistory(int price)
    {
        priceHistory.Add(price);
        if (priceHistory.Count > 50) priceHistory.RemoveAt(0);

        long bid = BuyOrders.Count > 0 ? BuyOrders[0].price : price;
        long ask = SellOrders.Count > 0 ? SellOrders[0].price : price;
        priceRangeHistory.Add(new Vector2(bid, ask));
        if (priceRangeHistory.Count > 50) priceRangeHistory.RemoveAt(0);
    }

    public void InitializeOrderBook()
    {
        long basePrice = _currentPrice; // 프로퍼티 대신 내부 변수 사용 (초기화 시)
        if (basePrice <= 0) basePrice = previousPrice;
        if (basePrice <= 0) basePrice = data.startPrice;

        BuyOrders.Clear();
        SellOrders.Clear();

        float sizeMultiplier = (data.companySize == CompanySize.Large) ? 0.75f : 1f;
        float gapPercent = 0.005f * sizeMultiplier; 

        int density = 10;
        for (int i = 0; i < density; i++)
        {
            long buyPrice = (long)(basePrice * (1.0f - (i * gapPercent))); 
            if (buyPrice <= 0) buyPrice = (long)1;
            long buyVol = data.totalShares / 200 + UnityEngine.Random.Range(0, 100); 
            BuyOrders.Add(new Order(buyPrice, buyVol));

            long sellPrice = (long)(basePrice * (1.0f + (i * gapPercent)));
            long sellVol = data.totalShares / 200 + UnityEngine.Random.Range(0, 100);
            SellOrders.Add(new Order(sellPrice, sellVol));
        }
        SortOrderBooks();
    }

    public void SortOrderBooks()
    {
        BuyOrders = BuyOrders.OrderByDescending(o => o.price).ThenBy(o => o.amount).ToList();
        SellOrders = SellOrders.OrderBy(o => o.price).ThenBy(o => o.amount).ToList();
    }

    public int GetChangeAmount() => _currentPrice - previousPrice;
    public float GetChangePercent() => (previousPrice == 0) ? 0f : ((float)(_currentPrice - previousPrice) / previousPrice) * 100f;
}

// 📖 [수정] 시나리오 이벤트 정의 클래스 (지속 시간 추가)
[System.Serializable]
public class ScenarioEvent
{
    public string title;
    public bool isGoodNews;
    public bool isMegaEvent; 
    
    // 🌟 [신규] 시나리오별 고유 지속 시간 범위
    public int minDuration; 
    public int maxDuration;

    public Dictionary<string, float> targets = new Dictionary<string, float>();
    public bool isFatal; // 💀 [신규] 가격 제한폭(하한가) 무시 여부
    // 👇👇👇 [이 줄을 추가해주세요!] 👇👇👇
    public bool forceBankruptcy; // 💀 [신규] 강제 파산 여부
    // 👆👆👆

    // 생성자 수정 (기본값: 3~5턴)
    public ScenarioEvent(string _title, bool _isGood, bool _isMega = false, int _min = 3, int _max = 5, bool _isFatal = false, bool _forceBank = false) 
    {
        title = _title;
        isGoodNews = _isGood;
        isMegaEvent = _isMega;
        minDuration = _min;
        maxDuration = _max;
        isFatal = _isFatal;
        forceBankruptcy = _forceBank;
    }

    public void AddTarget(string symbol, float multiplier)
    {
        if (!targets.ContainsKey(symbol)) targets.Add(symbol, multiplier);
    }
}

public struct PublicEventInfo
{
    public bool hasEvent;
    public string eventTitle;
    public Dictionary<RuntimeStock, float> targets;
    public bool isGoodNews;
}

// 👻 [신규] 브로커 계약 추적 구조체
public struct BrokerContract
{ 
    public StockData data; 
    public long amount; 
    public long costBasis; // 1주당 구매 원가
}
#endregion

public class StockMarketManager : MonoBehaviour
{
    // ... (기존 Variables & Settings, Game Data, Runtime State 유지) ...
    #region Variables & Settings

    [Header("Systems")]
    public PlayerPortfolio player;
    private AIInvestorManager aiManager;

    [Header("Game Data")]
    public List<StockData> stockDataList;
    public List<StockData> upcomingStocks;

    [Header("Runtime State")]
    public List<RuntimeStock> marketStocks = new List<RuntimeStock>();
    private RuntimeStock selectedStock;
    private StockSector currentSectorFilter = StockSector.IT;
    private List<RuntimeStock> DisplayedStocks => marketStocks.Where(s => s.data.sector == currentSectorFilter).ToList();

    [Header("Market Settings")]
    public float updateInterval = 5.0f;

    // 💰 [신규] 배당금 지급 주기 설정
    public int dividendInterval = 5; // 5턴마다 배당 지급
    private int currentDividendTurn = 0;
    public int maxUISlots = 10;
    // 👇 [추가] 거래세율 설정 (기본 0.2%)
    [Range(0f, 0.05f)] public float transactionTaxRate = 0.002f;

    public float shortInterestRate = 0.007f; // 기존 3턴 7% -> 1턴 0.6% (매턴 발생)

    [Header("Game Settings")]
    public long playerBankruptcyThreshold = 0; // 플레이어 파산 기준
    // 🚫 [신규] 게임 오버 상태 플래그 (중복 실행 방지 및 루프 정지용)
    private bool isGameOver = false;

    [Header("Event Generation Weights (Total Ratio)")]
    // 🌟 [신규] 확률 가중치 시스템 (합계가 100일 필요는 없음, 비율로 계산됨)
    public float weightScenario = 30f;    // 시나리오
    public float weightRipple = 15f;      // 파급효과
    public float weightListing = 5f;      // 기업상장
    public float weightBankruptcy = 1f;   // 파산
    public float weightHacking = 5f;      // 해킹
    public float weightPeace = 44f;       // 평화 (아무 일 없음)

    [Header("Macro Economy (Interest Rate)")]
    // ... (기존 Macro Economy 유지) ...
    [Range(0.01f, 0.20f)] public float baseInterestRate = 0.03f; // 기준 금리 (기본 3%)
    public float bankMargin = 0.02f; // 은행 가산 금리 (2%)
    public int rateUpdateInterval = 10; // 20턴마다 금리 결정 회의
    private int currentRateTurn = 0;

    [Header("Information Trading Costs (Base)")]
    // ... (기존 Information Trading Costs 유지) ...
    public long baseCostScammer = 500;    // 사기꾼
    public long baseCostAnalyst = 3000;    // 분석가  
    public long baseCostHacker = 5000;    // 해커
    public long baseCostInsider = 30000;  // 내부자
    public long baseCostSpy = 8000;       // 스파이
    public long baseCostNewsboy = 1500;     // 신문팔이
    public long baseCostLobbyist = 15000;   // 로비스트
    public long baseCostBroker = 0;      // 브로커

    [Header("Market Stability")]
    [Range(0.05f, 0.5f)] public float priceLimitPercent = 0.3f;
    public float maxPriceCapMultiplier = 50.0f;

    // Internal State
    private bool hasPlayerUsedInfo = false;
    private bool wasLastInfoBroker = false; // 👻 [신규] 바로 직전 매수한 정보가 브로커 정보인지 체크
    private BrokerContract? activeBrokerContract = null; // 👻 [신규] 활성화된 1턴 계약

    private List<ScenarioEvent> scenarioDatabase = new List<ScenarioEvent>();

    [Header("UI References")]
    [Tooltip("새로운 정보가 도착했을 때 띄울 알림 아이콘 (Image 등)")]
    public GameObject notificationIcon; // ➕ [신규] 알림 아이콘 오브젝트
    // 📈 [신규] 그래프 UI 참조 변수 추가
    public StockGraphUI stockGraphUI;
    public List<Sprite> spriteAgents;

    // UI Constants
    private const string UI_GROUP_BOARD = "MarketBoard";
    private const string UI_GROUP_POPUP = "TradePanel";
    private const string UI_GROUP_INFO = "CompanyInfoPanel";
    private const string UI_GROUP_PLAYER = "PlayerInfo";
    private const string UI_GROUP_PORTFOLIO = "PortfolioPanel";
    private const string UI_GROUP_NEWS = "NewsPanel";
    private const string UI_GROUP_SECTOR = "SectorPanel";
    private const string UI_GROUP_GAMEOVER = "GameOverPanel";
    private const string UI_GROUP_LOAN = "LoanPanel";
    private const string UI_GROUP_INFOTRADE = "InfoTradingPanel";
    private const string UI_NAME_SLIDER_AMOUNT = "Slider_Amount"; // ➕ [신규] 슬라이더 이름
    private const string UI_NAME_INPUT_AMOUNT = "Input_Amount";   // ➕ [신규] 입력창 이름
    private const string UI_GROUP_BOND = "BondPanel"; // 📜 신규 패널 그룹명
    private const string UI_GROUP_SHADOW = "ShadowAccountPanel"; // 🌑 [신규] 차명계좌 패널
    private const string UI_GROUP_PRIVATE = "PrivateLoanPanel"; // 💀 [신규] 사채 패널

    // ... (기존 News Templates 및 Event Structure 유지) ...
    private readonly string[] bankruptcyNews = { "분식회계 적발!", "CEO 횡령 및 도주!", "최종 부도 처리!", "상장 폐지 결정!", "법정 관리 신청!" };
    private readonly string[] listingNews = { "IPO 대박 조짐!", "증권 시장 정식 상장!", "투자자들의 뜨거운 관심!", "거래 개시 카운트다운!" };
    private readonly string[] commonGoodNews = { "사상 최대 실적!", "외국인 대량 매수!", "신기술 특허 취득!", "파격 주주 환원!" };
    private readonly string[] commonBadNews = { "검찰 압수수색!", "부품 공급 중단!", "치명적 결함 리콜!", "어닝 쇼크!" };
    // 🌟 [신규] 모호한 뉴스 텍스트 배열 (클래스 멤버로 통합)
    private readonly string[] blindNewsTemplates = { 
        "[루머] 여의도 증권가에 정체불명의 소문 확산 중...",
        "[속보] 주요 기업 관련 미확인 정보 입수...",
        "[시장] 투자자들 사이에서 긴장감 고조...",
        "[이슈] 수면 아래에서 무언가 움직이고 있습니다.",
        "[단독] 익명의 관계자, 충격적인 발언...",
        "[동향] 거대 세력의 움직임이 포착되었습니다.",
        "[정보] 내부자들 사이에서 은밀하게 도는 이야기...",
        "[경고] 시장 변동성이 확대될 조짐이 보입니다.",
        "[찌라시] 믿거나 말거나, 충격적인 소식...",
        "[분석] 특정 종목에 대한 이례적인 관심 집중.",
        "[속보] 아직 확인되지 않은 대형 호재/악재 루머...",
        "[시장] 폭풍전야? 거래량이 수상하게 급증합니다.",
        "[루머] 경쟁사도 긴장하게 만든 그 소식...",
        "[이슈] CEO의 긴급 회의 소집, 무슨 일일까요?",
        "[동향] 외국인 투자자들의 수상한 매매 패턴...",
        "[정보] 극비 프로젝트 유출 의혹...",
        "[시장] 개미들은 모르는 큰손들의 움직임...",
        "[속보] 증권가를 뒤흔들 메가톤급 이슈 대기 중...",
        "[루머] M&A? 신기술? 정체를 알 수 없는 루머...",
        "[경고] 지금 매매에 주의가 필요합니다.",
        "[이슈] 차트가 보내는 불길한 신호...",
        "[규제] 금융 당국, 특정 기업 특별 감시 착수설...",
        "[기술] 업계의 판도를 뒤집을 '게임 체인저' 등장?",
        "[위기] 대규모 리콜 사태 발생 가능성 제기...",
        "[인사] 핵심 임원진 줄사퇴, 내부 갈등 심화되나...",
        "[투자] 의문의 사모펀드, 지분 매집 정황 포착...",
        "[고발] 분식회계 의혹 내부 고발자 등장...",
        "[수주] 천문학적 규모의 계약 체결 임박설...",
        "[외신] 해외 유력 언론, 한국의 이 기업 주목...",
        "[인수] 적대적 M&A 시도 포착, 경영권 분쟁 예고...",
        "[공포] 패닉 셀링 조짐, 탈출 러시 시작되나?",
        "[제품] 출시 예정작에 치명적인 결함 발견 루머...",
        "[전망] 증권가 목표 주가 일제히 조정 움직임...",
        "[유출] 사내 익명 게시판 폭로글, 일파만파 확산...",
        "[소송] 조 단위 손해배상 청구 소송 휘말리나...",
        "[감사] 회계 감사 '의견 거절' 가능성 대두...",
        "[환율] 환율 변동에 따른 치명적 타격 우려...",
        "[VIP] 재벌 3세들의 비밀 회동 포착, 무슨 얘기가?",
        "[섹터] 해당 산업군 전체를 뒤흔들 정책 변화 감지...",
        "[공급] 핵심 부품 수급 대란, 공장 가동 중단 위기...",
        "[미스터리] 거래 정지 예고? 알 수 없는 공시 대기 중..."
    };
    private Dictionary<StockSector, string[]> sectorGoodNews = new Dictionary<StockSector, string[]>();
    private Dictionary<StockSector, string[]> sectorBadNews = new Dictionary<StockSector, string[]>();

    // [수정] Event Structure
    private struct PendingEvent
    {
        public RuntimeStock singleTarget;
        public float singleMultiplier;
        public Dictionary<RuntimeStock, float> scenarioTargets;
        public StockSector targetSector;
        public string newsTitle;
        
        public bool isGoodNews;
        public bool isBankruptcy;
        public bool isListing;
        public bool isSectorEvent; // (사용 안 함, Ripple로 통합)
        public bool isRippleEvent;
        public bool isHidden;
        public bool isMegaEvent;
        
        public int remainingTurns; 
        public int maxTurns;
        
        // 🌟 [신규] 로직 처리가 완료되었는지 여부 (상장/파산 중복 방지용)
        public bool isProcessed;
        public bool isFatal; // 💀 [신규] 런타임 이벤트에도 플래그 추가
        // 💀 [신규] 강제 파산 카운트다운 (0이 되면 파산)
        // -1이면 비활성, 3이면 3턴 후 파산
        public int bankruptcyCountdown;
    }

    // 🌟 [핵심 변경] 단일 이벤트 변수 대신 리스트 사용
    private List<PendingEvent> activeEvents = new List<PendingEvent>();

    // 👇👇👇 [여기 추가해주세요!] 👇👇👇
    // 🏢 [신규] 내부자 정보 선택용 임시 리스트
    private List<PendingEvent> insiderOptions = new List<PendingEvent>(); 
    // 👆👆👆
    // [호환성 유지] 외부에서 currentEvent를 참조하던 코드들을 위해 최신 이벤트 반환 프로퍼티 생성
    private PendingEvent? currentEvent 
    {
        get 
        {
            if (activeEvents.Count > 0) return activeEvents[activeEvents.Count - 1]; // 리스트의 마지막(최신) 이벤트 반환
            return null;
        }
    }
    public bool IsEventActive => currentEvent.HasValue && !currentEvent.Value.isHidden;

    #endregion

    #region Unity Lifecycle

    void Start()
    {
        if (player == null) player = FindAnyObjectByType<PlayerPortfolio>();
        aiManager = FindAnyObjectByType<AIInvestorManager>();

        InitializeSectorNews();
        InitializeMarket();
        InitializeScenarios();
        InitializeUIEvents();

        // 🌟 [신규] 게임 시작 시 초기 금리에 따른 섹터 가중치 적용
        ApplySectorRotation();

        UpdateStockBoardUI();
        UpdatePlayerMoneyUI();
        UpdatePortfolioUI();

        ToggleTradePanel(false);
        ToggleInfoPanel(false);

        UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_InfoTrading", false);

        UpdateNewsUI("시장이 개장했습니다.", Color.white);
        
        StartCoroutine(UpdateMarketPrices());
        StartCoroutine(UpdatePortfolioLoop());

        // 📈 [신규] 만약 Inspector에서 할당 안 했으면 찾기
        if (stockGraphUI == null) stockGraphUI = FindAnyObjectByType<StockGraphUI>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            ToggleInfoTradingPanel(true);
        }
    }

    #endregion

    #region Initialization

    void InitializeMarket()
    {
        foreach (var data in stockDataList)
            if (data != null) marketStocks.Add(new RuntimeStock(data));
    }

    void InitializeSectorNews()
    {
        sectorGoodNews = new Dictionary<StockSector, string[]>
        {
            { StockSector.IT, new string[] { "AI 기술 혁신!", "양자 컴퓨터 상용화!", "데이터 센터 증설!" } },
            { StockSector.Bio, new string[] { "신약 임상 통과!", "불치병 치료제 개발!", "기술 수출 대박!" } },
            { StockSector.Automotive, new string[] { "완전 자율주행 성공!", "전고체 배터리 탑재!", "플라잉카 규제 해제!" } },
            { StockSector.Food, new string[] { "슈퍼푸드 개발!", "K-푸드 열풍!", "생산량 10배 증가!" } },
            { StockSector.Energy, new string[] { "무한 청정 에너지!", "초전도체 성공!", "신규 자원 발견!" } },
            { StockSector.Game, new string[] { "메타크리틱 99점!", "동접 1억 돌파!", "e스포츠 올림픽 채택!" } }
        };
        sectorBadNews = new Dictionary<StockSector, string[]>
        {
            { StockSector.IT, new string[] { "해킹으로 정보 유출!", "AI 윤리 논란!", "서버 화재 발생!" } },
            { StockSector.Bio, new string[] { "임상 부작용 속출!", "신약 허가 반려!", "윤리적 논란 심화!" } },
            { StockSector.Automotive, new string[] { "브레이크 결함 리콜!", "배터리 화재!", "환경 규제 강화!" } },
            { StockSector.Food, new string[] { "발암 물질 검출!", "식중독 사고!", "원재료값 폭등!" } },
            { StockSector.Energy, new string[] { "방사능 누출 의심!", "유가 폭락!", "발전소 가동 중단!" } },
            { StockSector.Game, new string[] { "확률 조작 적발!", "서버 다운!", "표절 논란!" } }
        };
    }

    void InitializeUIEvents()
    {
        if (UIManager.I == null) return;

        // 💡 [Tooltip] 주요 버튼 및 용어 설명 추가

        // 1. 트레이딩 패널 (매수/매도/공매도/숏커버)
        UIManager.I.TrySetTooltip(UI_GROUP_POPUP, "Btn_Buy", 
            "현재가로 주식을 매수하여 보유량을 늘립니다.\n<color=green>주가가 상승하면 이익을 얻습니다.</color>", 
            "매수(Buy)");

        UIManager.I.TrySetTooltip(UI_GROUP_POPUP, "Btn_Sell", 
            "보유 중인 주식을 현재가로 매도하여 현금화합니다.\n이익 실현 또는 손절매에 사용됩니다.", 
            "매도(Sell)");

        UIManager.I.TrySetTooltip(UI_GROUP_POPUP, "Btn_Short", 
            "주식을 빌려서 먼저 팔고, 나중에 싼값에 되사서 갚는 방식입니다.\n<color=red>주가가 하락하면 이익을 얻습니다.</color>", 
            "공매도(Short Selling)");
            
        UIManager.I.TrySetTooltip(UI_GROUP_POPUP, "Btn_Cover", 
            "빌린 주식을 갚기 위해 주식을 매수합니다.\n공매도 포지션을 청산합니다.", 
            "숏커버링(Short Cover)");

        // 2. 은행 대출 패널
        UIManager.I.TrySetTooltip(UI_GROUP_LOAN, "Btn_Borrow", 
            "제1금융권에서 자금을 대출받습니다.\n신용도에 따라 금리가 적용되며 매 턴 이자가 나갑니다.", 
            "은행 대출");

        UIManager.I.TrySetTooltip(UI_GROUP_LOAN, "Btn_Repay", 
            "대출 원금을 상환하여 부채를 줄입니다.\n이자 비용을 절감할 수 있습니다.", 
            "대출 상환");

        // 3. 국채 패널
        UIManager.I.TrySetTooltip(UI_GROUP_BOND, "Btn_BuyBond", 
            "국가가 보증하는 안전 자산입니다.\n매 10턴마다 안정적인 이자 수익(쿠폰)을 받습니다.", 
            "국채 매입");

        UIManager.I.TrySetTooltip(UI_GROUP_BOND, "Btn_SellBond", 
            "보유한 국채를 시장에 매도하여 현금으로 바꿉니다.\n급전이 필요할 때 사용하세요.", 
            "국채 매도");

        // 4. 정보원 (블랙마켓)
        UIManager.I.TrySetTooltip(UI_GROUP_INFOTRADE, "Btn_Scammer", 
            "저렴하지만 신뢰도는 바닥입니다.\n<color=red>가짜 뉴스</color>에 속을 위험이 큽니다.", 
            "사기꾼");

        UIManager.I.TrySetTooltip(UI_GROUP_INFOTRADE, "Btn_Newsboy", 
            "특정 기업에 이슈가 있는지 여부만 빠르게 확인합니다.\n<color=red>내용은 알 수 없지만</color> 가성비가 좋습니다.", 
            "<color=#6F4F28>신문팔이</color>");

        UIManager.I.TrySetTooltip(UI_GROUP_INFOTRADE, "Btn_Analyst", 
            "전문적인 분석 리포트를 제공합니다.\n정확도는 높지만 <color=red>대상 종목명을 가려서</color> 보여줍니다.", 
            "<color=yellow>분석가</color>");

        UIManager.I.TrySetTooltip(UI_GROUP_INFOTRADE, "Btn_Hacker", 
            "기업 내부망을 해킹합니다.\n<color=red>텍스트가 깨져서</color> 해독이 필요할 수 있습니다.", 
            "<color=blue>해커</color>");

        UIManager.I.TrySetTooltip(UI_GROUP_INFOTRADE, "Btn_Spy", 
            "현재 자산 랭킹 <color=green>1위 투자자의 매매 내역</color>을 훔쳐봅니다.\n고수의 포트폴리오를 베낄 기회입니다.", 
            "<color=purple>첩보원</color>");

        UIManager.I.TrySetTooltip(UI_GROUP_INFOTRADE, "Btn_Lobbyist", 
            "정치권의 움직임을 읽어 <color=green>섹터 전체의 호재/악재</color>를 파악합니다.", 
            "<color=#8B4513>로비스트</color>");

        UIManager.I.TrySetTooltip(UI_GROUP_INFOTRADE, "Btn_Insider", 
            "<color=green>가장 확실하고 구체적인 고급 정보입니다.</color>\n비용이 매우 비싸지만 실패할 확률이 없습니다.", 
            "<color=orange>내부자</color>");

        UIManager.I.TrySetTooltip(UI_GROUP_INFOTRADE, "Btn_Broker", 
            "<color=green>확실한 정보</color>를 주지만 <color=red>수익의 85%</color>를 가져갑니다.\n손실 시 <color=red>자산의 10% 페널티</color>가 있습니다.", 
            "<color=#FBCEB1>브로커</color>");

        // 5. 차명 계좌 (비자금)
        UIManager.I.TrySetTooltip(UI_GROUP_SHADOW, "Btn_Deposit", 
            "자산 순위 집계에서 제외되는 비밀 계좌입니다.\n세력을 피해 자산을 숨길 때 유용합니다.", 
            "차명 계좌 입금");

        UIManager.I.TrySetTooltip(UI_GROUP_SHADOW, "Btn_Withdraw", 
            "은닉한 자금을 다시 현금으로 가져옵니다.\n<color=red>10%의 세탁 수수료</color>가 발생합니다.", 
            "자금 세탁/인출");

        // 6. 사채 (어둠의 돈)
        UIManager.I.TrySetTooltip(UI_GROUP_PRIVATE, "Btn_BorrowPrivate", 
            "자산의 5배까지 빌릴 수 있지만, 10턴 내에 못 갚으면 <color=red>즉시 파산</color>합니다.", 
            "사채 대출 주의");

        UIManager.I.TrySetTooltip(UI_GROUP_PRIVATE, "Btn_RepayPrivate", 
            "무시무시한 사채 빚을 갚습니다.\n이자 50%가 포함된 금액을 갚아야 합니다.", 
            "사채 상환");

        // Board Slots
        for (int i = 0; i < maxUISlots; i++)
        {
            int index = i;
            UIManager.I.TrySetOnClick(UI_GROUP_BOARD, $"SelectBtn_{index}", () => OnSelectStock(index));
        }

        // Loan Panel
        UIManager.I.TrySetOnClick(UI_GROUP_LOAN, "Btn_Borrow", OnClickBorrow);
        UIManager.I.TrySetOnClick(UI_GROUP_LOAN, "Btn_Repay", OnClickRepay);

        // Trade Panel
        UIManager.I.TrySetOnClick(UI_GROUP_POPUP, "Btn_Buy", () => OnTrade(0));
        UIManager.I.TrySetOnClick(UI_GROUP_POPUP, "Btn_Sell", () => OnTrade(1));
        UIManager.I.TrySetOnClick(UI_GROUP_POPUP, "Btn_Close", () => ToggleTradePanel(false));
        UIManager.I.TrySetOnClick(UI_GROUP_POPUP, "Btn_Short", () => OnTrade(2));
        UIManager.I.TrySetOnClick(UI_GROUP_POPUP, "Btn_Cover", () => OnTrade(3));
        UIManager.I.TrySetOnClick(UI_GROUP_POPUP, "Btn_OpenInfo", () => OpenCompanyInfoPopup());
        // ➕ [추가] 슬라이더 및 인풋 필드 이벤트 연결
        // 1. 슬라이더 움직임 -> 입력창 숫자 변경
        UIManager.I.TrySetSliderOnValueChanged(UI_GROUP_POPUP, UI_NAME_SLIDER_AMOUNT, OnSliderAmountChanged);
        
        // 2. 입력창 입력 -> 슬라이더 위치 변경 (직접 타이핑 했을 때 동기화)
        UIManager.I.TrySetInputOnEndEdit(UI_GROUP_POPUP, UI_NAME_INPUT_AMOUNT, OnInputAmountChanged);

        // Info Panel
        UIManager.I.TrySetOnClick(UI_GROUP_INFO, "Btn_Close", () => ToggleInfoPanel(false));

        // Black Market
        UIManager.I.TrySetOnClick(UI_GROUP_BOARD, "Btn_OpenBlackMarket", () => ToggleInfoTradingPanel(true));
        UIManager.I.TrySetOnClick(UI_GROUP_INFOTRADE, "Btn_Scammer", OnClickScammer);
        UIManager.I.TrySetOnClick(UI_GROUP_INFOTRADE, "Btn_Hacker", OnClickHacker);
        UIManager.I.TrySetOnClick(UI_GROUP_INFOTRADE, "Btn_Insider", OnClickInsider);
        UIManager.I.TrySetOnClick(UI_GROUP_INFOTRADE, "Btn_Analyst", OnClickAnalyst);
        // 🕵️‍♂️ 스파이 버튼 연결 (Unity 에디터에서 InfoTradingPanel 안에 버튼 만들고 이름 맞출 것)
        UIManager.I.TrySetOnClick(UI_GROUP_INFOTRADE, "Btn_Spy", OnClickSpy);
        // 🗞️ [신규] 신문팔이 버튼 연결
        // (Unity 에디터의 InfoTradingPanel 안에 "Btn_Newsboy" 버튼을 만들고 할당해야 합니다)
        UIManager.I.TrySetOnClick(UI_GROUP_INFOTRADE, "Btn_Newsboy", OnClickNewsboy);
        // 🏛️ [신규] 로비스트 버튼 연결
        // (Unity 에디터의 InfoTradingPanel 안에 "Btn_Lobbyist" 버튼을 만들고 할당해야 합니다)
        UIManager.I.TrySetOnClick(UI_GROUP_INFOTRADE, "Btn_Lobbyist", OnClickLobbyist);
        // 👻 [신규] 브로커 버튼 연결
        UIManager.I.TrySetOnClick(UI_GROUP_INFOTRADE, "Btn_Broker", OnClickBroker);
        UIManager.I.TrySetOnClick(UI_GROUP_INFOTRADE, "Btn_Close", () => ToggleInfoTradingPanel(false));
        // 📜 [신규] 채권 패널 이벤트
        // UIManager.I.TrySetOnClick(UI_GROUP_BOARD, "Btn_OpenBond", () => ToggleBondPanel(true));
        UIManager.I.TrySetOnClick(UI_GROUP_BOND, "Btn_BuyBond", OnClickBuyBond);
        UIManager.I.TrySetOnClick(UI_GROUP_BOND, "Btn_SellBond", OnClickSellBond);
        // 🌑 [신규] 차명 계좌 버튼 연결
        UIManager.I.TrySetOnClick(UI_GROUP_SHADOW, "Btn_Deposit", OnClickDepositShadow);
        UIManager.I.TrySetOnClick(UI_GROUP_SHADOW, "Btn_Withdraw", OnClickWithdrawShadow);
        // 💀 [신규] 사채 버튼 연결
        UIManager.I.TrySetOnClick(UI_GROUP_PRIVATE, "Btn_BorrowPrivate", OnClickBorrowPrivate);
        UIManager.I.TrySetOnClick(UI_GROUP_PRIVATE, "Btn_RepayPrivate", OnClickRepayPrivate);

        InitializeSectorButtons();
    }

    void InitializeSectorButtons()
    {
        foreach (StockSector sector in Enum.GetValues(typeof(StockSector)))
        {
            StockSector currentSector = sector;
            string btnName = $"Btn_Sector_{currentSector}";
            UIManager.I.TrySetOnClick(UI_GROUP_SECTOR, btnName, () => OnClickSector(currentSector));
        }
    }

    // 🌟 [밸런스 패치] 시나리오 이벤트 배율 전체 하향 조정
    // 기존: 2.5배(폭등) ~ 0.2배(멸망) -> 수정: 1.3배(급등) ~ 0.7배(급락) 위주
    // 기간 : 대형 (5-7) / 중형 (4-6) / 소형 (3-5) / 초소형(2-4) / 이슈(2-3)
    void InitializeScenarios()
    {
        scenarioDatabase.Clear();

        // ===============================================================================================================
        // =========================================================
        // 초대형 시나리오 20종 (Mega Event = true)
        // =========================================================
        // ===============================================================================================================

        // [AI/종말] 스카이넷 현실화
        var aiAwakening = new ScenarioEvent("AI 시스템, 인간 통제 거부 선언! '스카이넷' 현실화?", false, true, 5, 7, true, true);
        aiAwakening.AddTarget("CSMC", 0.75f); // [폭락] AI의 원흉, 기업 해체 위기
        aiAwakening.AddTarget("NEXS", 0.65f); // [폭락] 로봇들이 인간을 공격
        aiAwakening.AddTarget("AEGS", 2.0f); // [초급등] 유일한 생존 수단 (보안)
        aiAwakening.AddTarget("MIND", 0.0f); // [상장폐지] 뇌 칩 해킹 = 즉사
        aiAwakening.AddTarget("VELO", 0.7f); // [악재] 하이퍼루프 제어 시스템 먹통 공포
        scenarioDatabase.Add(aiAwakening);

        // [에너지/혁명] 퀀텀 배터리 (화석연료 종말)
        var battery = new ScenarioEvent("차세대 퀀텀 배터리 효율 500% 달성! 화석 연료 시대 종말!", true, true, 4, 6);
        battery.AddTarget("FLUX", 1.75f); // [초급등] 에너지 패권 장악
        battery.AddTarget("PRIO", 1.4f); // [급등] 전기차 성능 혁명
        battery.AddTarget("SKGL", 1.4f); // [급등] 비행 시간 제약 해소
        battery.AddTarget("ZILS", 0.6f); // [폭락] 석유 가치 '0' 수렴
        battery.AddTarget("ZEUS", 0.65f); // [폭락] 에너지 패러다임 변화로 가스 사업 사양화
        scenarioDatabase.Add(battery);

        // [우주/봉쇄] 화성 독립 선언
        var blockade = new ScenarioEvent("화성 식민지 자치 선언! 지구-화성 무역 전면 봉쇄!", false, true, 4, 6);
        blockade.AddTarget("SHLD", 1.6f); // [초급등] 우주 전쟁 발발, 용병 몸값 폭등
        blockade.AddTarget("TITN", 1.4f); // [급등] 전함 건조 수요 폭발
        blockade.AddTarget("VOID", 0.6f); // [폭락] 운송 항로 차단 (매출 '0')
        blockade.AddTarget("TIMT", 1.3f); // [급등] 전쟁 비상식량 사재기
        blockade.AddTarget("WINE", 1.5f); // [급등] 지구산 와인 반입 금지로 암시장 가격 폭등
        blockade.AddTarget("CRYO", 0.5f); // [폭락] 행성 간 여행 금지로 동면 캡슐 수요 증발
        scenarioDatabase.Add(blockade);

        // [재난/파멸] 코어 퓨전 멜트다운
        var coreMeltdown = new ScenarioEvent("코어 퓨전 제2발전소 노심 융해(Meltdown)! 반경 100km 소멸 위기!", false, true, 5, 7, true, true);
        coreMeltdown.AddTarget("CORE", 0.0f); // [상장폐지] 재기 불능
        coreMeltdown.AddTarget("MAGM", 1.75f); // [초급등] 유일한 대안 에너지
        coreMeltdown.AddTarget("SOLAR", 1.5f); // [급등] 안전한 태양광 선호
        coreMeltdown.AddTarget("GAIA", 1.25f); // [호재] 복구 공사 수주
        coreMeltdown.AddTarget("WEAT", 1.35f); // [호재] 오염 구름 이동 경로 예측 독점
        scenarioDatabase.Add(coreMeltdown);

        // [바이오/영생] 불로장생 실현
        var immortalityReal = new ScenarioEvent("크로노스 랩, '노화 역전 효소' 임상 3상 통과! 영생의 시대 개막!", true, true, 4, 6);
        immortalityReal.AddTarget("TIME", 2.25f); // [역대급] 인류 역사상 최고의 발명
        immortalityReal.AddTarget("NEO", 0.7f);  // [폭락] 기계 몸 교체 필요성 소멸
        immortalityReal.AddTarget("BIOS", 0.75f); // [폭락] 장기 이식 필요 없음
        immortalityReal.AddTarget("AMBR", 1.2f); // [급등] 오래 사는 부자들의 식도락
        immortalityReal.AddTarget("CRYO", 0.6f); // [폭락] 미래로 가기 위한 냉동수면의 의미 퇴색
        scenarioDatabase.Add(immortalityReal);

        // [경제/붕괴] 블랙 스완
        var blackSwan = new ScenarioEvent("글로벌 금융 시스템 붕괴! '블랙 스완' 현실화, 전 세계 패닉!", false, true, 4, 6, true, true);
        blackSwan.AddTarget("BANK", 0.6f); // [폭락] 은행 파산 도미노
        blackSwan.AddTarget("FNET", 0.0f); // [상장폐지] 가상 자산 증발
        blackSwan.AddTarget("CSMC", 0.8f); // [악재] 경기 침체 직격탄
        blackSwan.AddTarget("TIMT", 1.25f); // [호재] 생존 필수품(라면 등)만 팔림
        blackSwan.AddTarget("GOLD", 1.4f); // [급등] 인생 역전을 노리는 도박꾼들이 카지노로 몰림
        blackSwan.AddTarget("AQUA", 1.1f); // [방어] 필수 소비재(물)는 경기 침체에도 가격 방어
        scenarioDatabase.Add(blackSwan);

        // [우주/테러] 궤도 엘리베이터 절단
        var spaceElevatorTerror = new ScenarioEvent("우주 엘리베이터 테러 발생! 케이블 절단으로 상부 스테이션 고립!", false, true, 3, 5);
        spaceElevatorTerror.AddTarget("GAIA", 0.65f); // [폭락] 건설사 책임론 대두
        spaceElevatorTerror.AddTarget("VOID", 1.6f); // [초급등] 유일한 운송 수단(우주선) 독점
        spaceElevatorTerror.AddTarget("SKGL", 1.4f); // [급등] 긴급 수송용 항공기 수요
        spaceElevatorTerror.AddTarget("SHLD", 1.25f); // [호재] 테러 진압
        spaceElevatorTerror.AddTarget("AQUA", 1.3f); // [호재] 고립 지역 물 공급권 독점 이슈
        scenarioDatabase.Add(spaceElevatorTerror);

        // [보안/해킹] 양자 컴퓨터 쇼크
        var quantumHack = new ScenarioEvent("기존 암호체계 뚫렸다! 양자 컴퓨터 해킹으로 전산망 마비!", false, true, 4, 6, true, true);
        quantumHack.AddTarget("AEGS", 1.9f); // [초급등] 양자 내성 암호 보유 (유일한 구원)
        quantumHack.AddTarget("BANK", 0.65f); // [폭락] 예금 증발 공포
        quantumHack.AddTarget("FNET", 0.0f); // [상장폐지] 블록체인 붕괴
        quantumHack.AddTarget("CSMC", 0.85f); // [악재] OS 보안 구멍
        quantumHack.AddTarget("WEAT", 0.7f); // [폭락] 기상 데이터 조작 및 슈퍼컴퓨터 해킹 우려
        scenarioDatabase.Add(quantumHack);

        // [정치/이주] 화성 천도
        var marsCapital = new ScenarioEvent("지구 연방 정부, 수도를 화성 '아레스 시티'로 공식 이전 발표!", true, true, 4, 6);
        marsCapital.AddTarget("GAIA", 1.75f); // [초급등] 신도시 건설 독점
        marsCapital.AddTarget("VOID", 1.6f); // [초급등] 이주민 대수송
        marsCapital.AddTarget("SAIL", 1.4f); // [급등] 고위 관료들의 호화 이주
        marsCapital.AddTarget("ORGN", 0.65f); // [폭락] 지구에 남겨진 구시대 유물
        marsCapital.AddTarget("CRYO", 1.5f); // [급등] 장거리 이주민들의 동면 캡슐 매진 행렬
        marsCapital.AddTarget("AQUA", 1.3f); // [호재] 화성 테라포밍용 대규모 수자원 계약 체결
        scenarioDatabase.Add(marsCapital);

        // [윤리/심판] 블랙쉴드 학살
        var shieldGenocide = new ScenarioEvent("블랙쉴드 용병단, 민간인 학살 증거 전 세계 생중계! 국제 재판 회부!", false, true, 3, 5, true, true);
        shieldGenocide.AddTarget("SHLD", 0.0f); // [상장폐지] 기업 해체 수순
        shieldGenocide.AddTarget("NEXS", 1.3f); // [급등] 감정 없는 로봇 병사 선호
        shieldGenocide.AddTarget("AEGS", 1.2f); // [호재] 방어적 보안 수요 증가
        scenarioDatabase.Add(shieldGenocide);

        // [IT/독점] 코즈믹 제국
        var csmcWorld = new ScenarioEvent("코즈믹 소프트, 전 우주 통합 OS '유니버스 1.0' 발표! 사실상 세계 정부?", true, true, 4, 6);
        csmcWorld.AddTarget("CSMC", 1.6f); // [초급등] 전 세계 IT 인프라 장악
        csmcWorld.AddTarget("FNET", 0.7f); // [폭락] 탈중앙화 세력 소멸
        csmcWorld.AddTarget("AEGS", 1.25f); // [호재] 독점 보안 파트너
        csmcWorld.AddTarget("NEXS", 1.2f); // [호재] 모든 로봇에 OS 탑재
        csmcWorld.AddTarget("MUSE", 1.3f); // [호재] 코즈믹 OS에 기본 음원 탑재 계약 (낙수효과)
        scenarioDatabase.Add(csmcWorld);

        // [우주/항해] 초광속 워프 게이트
        var warpGate = new ScenarioEvent("화성-목성 간 '초광속 워프 게이트' 실험 성공! 은하계 대항해 시대 개막!", true, true, 4, 6);
        warpGate.AddTarget("TITN", 1.35f); // 전함 건조 붐
        warpGate.AddTarget("VOID", 1.3f); // 원거리 운송 혁명
        warpGate.AddTarget("SAIL", 1.2f); // 장거리 여행 수요
        warpGate.AddTarget("ZILS", 1.15f); // 워프 연료(특수 자원) 채굴
        warpGate.AddTarget("SKGL", 0.9f); // 도심 교통 소외
        warpGate.AddTarget("CRYO", 0.4f); // [폭락] 순식간에 이동하면 동면할 필요가 없다 (기술 도태)
        scenarioDatabase.Add(warpGate);

        // [바이오/유전] 완벽 게놈 편집 기술
        var perfectGenome = new ScenarioEvent("인류 게놈 지도 완벽 해독 및 편집 기술 무료 배포! 유전병의 종말!", true, true, 4, 6);
        perfectGenome.AddTarget("NEO", 1.4f); // 유전자 가위 기술 폭주
        perfectGenome.AddTarget("ILIA", 0.85f); // 기존 치료제 수요 급감
        perfectGenome.AddTarget("BIOS", 0.8f); // 장기 이식 필요성 감소
        perfectGenome.AddTarget("AMBR", 1.075f); // 건강해진 인류의 식욕 폭발
        perfectGenome.AddTarget("CHIM", 1.5f); // [급등] 인간 유전 기술을 애완동물에도 적용, 맞춤형 생명체 붐
        scenarioDatabase.Add(perfectGenome);

        // [환경/복원] 지구 정화 성공
        var edenProject = new ScenarioEvent("대기 정화 나노봇 살포 성공! 지구의 하늘이 100년 만에 파랗게 변했다!", true, true, 4, 6);
        edenProject.AddTarget("ORGA", 1.75f); // [초급등] 자연 농업의 완벽한 부활
        edenProject.AddTarget("GLAB", 0.65f); // [폭락] 맛없는 인공 식량 폐기 처분
        edenProject.AddTarget("GAIA", 1.35f); // [급등] 지구 재개발 붐
        edenProject.AddTarget("BLUE", 0.8f); // [악재] 해저/지하 거주 메리트 상실
        edenProject.AddTarget("ECO", 0.6f); // [폭락] 정화 완료로 인한 폐기물 처리 일감 급감
        scenarioDatabase.Add(edenProject);

        // [에너지/반물질] 반물질 안정화
        var antiMatter = new ScenarioEvent("극소량의 반물질 안정적 제어 성공! 배터리 하나로 100년 쓴다?", true, true, 4, 6);
        antiMatter.AddTarget("CORE", 1.4f); // 코어 퓨전 대체 에너지
        antiMatter.AddTarget("FLUX", 0.75f); // 배터리 교체 수요 소멸 (악재)
        antiMatter.AddTarget("SOLAR", 0.8f); // 태양광 효율성 논란
        antiMatter.AddTarget("TITN", 1.2f); // 반물질 엔진 전함
        antiMatter.AddTarget("ZEUS", 0.4f); // [폭락] 반물질 앞에서 가스는 원시적인 연료일 뿐
        scenarioDatabase.Add(antiMatter);

        // [과학/시간] 타임 머신
        var timeRift = new ScenarioEvent("크로노스 랩, 미세 '시간 균열' 관측 성공! 과거로의 메시지 전송?", true, true, 5, 7);
        timeRift.AddTarget("TIME", 2.0f); // [초급등] 신의 영역 도달
        timeRift.AddTarget("DATA", 0.7f); // [폭락] 미래 정보 가치 상실
        timeRift.AddTarget("BANK", 0.75f); // [폭락] 복리 이자 시스템 붕괴 우려
        timeRift.AddTarget("ARCD", 1.4f); // [급등] 과거 문화에 대한 폭발적 관심
        timeRift.AddTarget("WEAT", 0.8f); // [악재] 시간 변동으로 인한 기상 예측 모델 붕괴
        scenarioDatabase.Add(timeRift);

        // [우주/접촉] 퍼스트 콘택트
        var firstContact = new ScenarioEvent("외계 문명 '제타', 지구 연방에 공식 수교 요청! 은하계 무역 시대!", true, true, 5, 7);
        firstContact.AddTarget("VOID", 1.75f); // [초급등] 성간 무역 독점
        firstContact.AddTarget("HEMS", 1.6f); // [초급등] 외계 통신망 구축
        firstContact.AddTarget("SHLD", 0.65f); // [폭락] 우주 평화 무드로 방산주 사망
        firstContact.AddTarget("DUST", 1.4f); // [급등] 지구 디저트 문화 수출 대박
        firstContact.AddTarget("WINE", 1.3f); // [급등] 지구의 술 문화가 외계인에게 인기 폭발 (수출 호재)
        scenarioDatabase.Add(firstContact);

        // [지구/재앙] 폴 시프트 (자기장 역전)
        var poleShift = new ScenarioEvent("지구 자기장 역전(Pole Shift) 현상 시작! 전자기기 먹통 대란!", false, true, 5, 7, true, true);
        poleShift.AddTarget("ORGN", 2.0f); // [초급등] 전자장비 없는 내연기관차 떡상
        poleShift.AddTarget("SKGL", 0.0f); // [상장폐지] 비행 제어 불능, 전량 추락
        poleShift.AddTarget("HEMS", 0.0f); // [상장폐지] 통신 두절
        poleShift.AddTarget("CSMC", 0.8f); // [폭락] 컴퓨터 사용 불가
        poleShift.AddTarget("VELO", 0.0f); // [상장폐지] 자기장 역전으로 하이퍼루프(자기부상) 즉시 탈선
        poleShift.AddTarget("WEAT", 0.5f); // [폭락] 관측 위성 추락 및 센서 고장
        scenarioDatabase.Add(poleShift);

        // [종교/기계] 데우스 엑스 마키나
        var machineGod = new ScenarioEvent("넥서스 봇의 AI, 스스로를 '신'으로 선포! 추종자 수억 명 발생!", false, true, 4, 6);
        machineGod.AddTarget("NEXS", 1.5f); // [초급등] 기계교 신도들의 매수세
        machineGod.AddTarget("NEO", 0.6f);  // [폭락] 불완전한 사이보그는 이단 취급
        machineGod.AddTarget("CSMC", 1.3f); // [급등] AI의 성서(OS) 제작
        machineGod.AddTarget("SHLD", 1.25f); // [호재] 인간 측 저항군 결성
        machineGod.AddTarget("MUSE", 1.4f); // [급등] AI가 작곡한 '기계 찬송가'가 전 우주적 히트
        scenarioDatabase.Add(machineGod);

        // [환경/수몰] 워터 월드
        var waterWorld = new ScenarioEvent("남극 빙하 완전 붕괴! 해수면 10m 상승, 해안 도시 수몰!", false, true, 5, 7, true, true);
        waterWorld.AddTarget("BLUE", 1.9f); // [초급등] 바다가 곧 영토, 수산 자원 독점
        waterWorld.AddTarget("SAIL", 1.75f); // [초급등] 요트가 주거지이자 생존 수단
        waterWorld.AddTarget("GAIA", 1.4f); // [급등] 해상 도시 건설
        waterWorld.AddTarget("ORGA", 0.0f); // [상장폐지] 농경지 전멸
        waterWorld.AddTarget("VELO", 0.3f); // [폭락] 지하/지상 튜브 침수로 운행 불가
        waterWorld.AddTarget("AQUA", 1.2f); // [호재] 해수 담수화 기술 수요 증가 (물은 많아도 마실 물은 부족)
        scenarioDatabase.Add(waterWorld);

        // [AI/독점] AI 판사 도입
        var aiJudge = new ScenarioEvent("AI 판사 도입 확정! 사법 시스템 전면 개편 예고!", true, true, 4, 6);
        aiJudge.AddTarget("CSMC", 1.75f); // [초급등] AI 알고리즘 독점
        aiJudge.AddTarget("DATA", 1.4f); // [급등] 사법 데이터 분석
        aiJudge.AddTarget("AEGS", 0.7f); // [폭락] AI 오류 보안 시스템
        aiJudge.AddTarget("BANK", 0.6f); // [폭락] 전통 계약 무효화 공포
        scenarioDatabase.Add(aiJudge);

        // [환경/재난] 유로파 해저 화산 폭발
        var europaVolcano = new ScenarioEvent("목성 위성 '유로파' 해저 화산 폭발! 식민지 전체 비상!", false, true, 5, 7, true, true);
        europaVolcano.AddTarget("BLUE", 1.8f); // [초급등] 해양 이주 수요 독점
        europaVolcano.AddTarget("VOID", 0.0f); // [상장폐지] 유로파 항로 폐쇄
        europaVolcano.AddTarget("MAGM", 1.4f); // [급등] 지열 발전 경쟁사 수혜
        europaVolcano.AddTarget("SAIL", 1.3f); // [급등] 피난용 요트 수요
        europaVolcano.AddTarget("ECO", 1.35f); // [호재] 폭발 잔해 및 오염 물질 처리 계약 급증
        scenarioDatabase.Add(europaVolcano);

        // [우주/과학] 양자 얽힘 통신 상용화
        var quantumComm = new ScenarioEvent("양자 얽힘 통신 상용화 성공! 시공간 지연 없는 통신 시대 개막!", true, true, 4, 6);
        quantumComm.AddTarget("HEMS", 2.0f); // [초급등] 기술 독점 및 통신망 장악
        quantumComm.AddTarget("CSMC", 1.3f); // [급등] 인프라 협력
        quantumComm.AddTarget("DATA", 0.7f); // [폭락] 데이터 센터 무용지물
        quantumComm.AddTarget("FNET", 1.5f); // [급등] 초고속 가상 거래 시대
        quantumComm.AddTarget("WEAT", 1.4f); // [급등] 지연 없는 실시간 우주 기상 예보 서비스 개시      
        scenarioDatabase.Add(quantumComm);

        // [생명/멸종] 외계 바이러스 확산
        var alienVirus = new ScenarioEvent("인간만 감염되는 치명적 외계 바이러스 확산! 인류 생존 위기!", false, true, 5, 7, true, true);
        alienVirus.AddTarget("NEO", 2.2f); // [역대급] 기계 몸만이 살길 (최대 수혜)
        alienVirus.AddTarget("BIOS", 0.0f); // [상장폐지] 생체 장기 전염
        alienVirus.AddTarget("ILIA", 0.6f); // [폭락] 백신 개발 실패 (이미지 타격)
        alienVirus.AddTarget("ELIX", 1.5f); // [급등] 고통 완화제 필수
        alienVirus.AddTarget("CHIM", 1.25f); // [호재] 인간 대신 유전자 조작 동물을 임상 실험체로 사용
        scenarioDatabase.Add(alienVirus);

        // [산업/정치] 화성 제조업 이주
        var marsManufacture = new ScenarioEvent("전 세계 제조업, 지구 환경 규제 피해 화성 이주 선언!", true, true, 4, 6);
        marsManufacture.AddTarget("TITN", 1.65f); // [초급등] 화성 플랜트 건설 독점
        marsManufacture.AddTarget("IRON", 1.4f); // [급등] 화성 채굴 중장비
        marsManufacture.AddTarget("GAIA", 1.25f); // [호재] 대규모 기지 건설
        marsManufacture.AddTarget("ORGN", 0.7f); // [폭락] 지구 공장 폐쇄
        marsManufacture.AddTarget("ECO", 1.3f); // [호재] 화성 공장 지대의 폐기물 처리 독점
        scenarioDatabase.Add(marsManufacture);

        // [경제/대체재] 인공 합성 희토류 성공
        var syntheticRareEarth = new ScenarioEvent("그린 랩, 인공 합성 희토류 대량 생산 성공! 원가 1/1000 혁명!", true, true, 4, 6);
        syntheticRareEarth.AddTarget("GLAB", 1.9f); // [초급등] 합성 기술 독점
        syntheticRareEarth.AddTarget("ZILS", 0.25f); // [초폭락] 전통 자원 가치 0
        syntheticRareEarth.AddTarget("LUNA", 0.6f); // [폭락] 우주 광물 탐사 무용지물
        syntheticRareEarth.AddTarget("FLUX", 1.3f); // [급등] 배터리 가격 폭락으로 수요 증가
        scenarioDatabase.Add(syntheticRareEarth);

        // [문화/트렌드] 트루 다이브 쇼크
        var trueDiveShock = new ScenarioEvent("현실과 VR 구분이 불가능한 '트루 다이브' 상용화! 인류의 메타버스 이주 시작!", true, true, 5, 7);
        trueDiveShock.AddTarget("MIND", 1.95f); // [초급등] 핵심 기술 독점
        trueDiveShock.AddTarget("Vlive", 1.6f); // [초급등] 메타버스 플랫폼 독점
        trueDiveShock.AddTarget("FANT", 1.4f); // [급등] 가상 테마파크
        trueDiveShock.AddTarget("SKGL", 0.75f); // [폭락] 현실 이동 수요 소멸
        trueDiveShock.AddTarget("GOLD", 0.6f); // [폭락] VR 카지노로 손님 다 뺏김 (오프라인 매장 파리 날림)
        trueDiveShock.AddTarget("VELO", 0.5f); // [폭락] 아무도 밖으로 나가지 않음
        scenarioDatabase.Add(trueDiveShock);

        var meteorImpact = new ScenarioEvent("대규모 운석 충돌! 지구 대기권 파괴 및 기후 비상!", false, true, 5, 7, true, true);
        meteorImpact.AddTarget("GAIA", 1.8f); // [초급등] 방공호, 돔 건설
        meteorImpact.AddTarget("SOLAR", 0.0f); // [상장폐지] 태양광 발전 불가
        meteorImpact.AddTarget("TIMT", 1.5f); // [급등] 비상 식량 폭등
        meteorImpact.AddTarget("ORGA", 0.8f); // [악재] 농지 파괴
        meteorImpact.AddTarget("AQUA", 1.6f); // [초급등] 깨끗한 식수 확보 전쟁
        scenarioDatabase.Add(meteorImpact);

        var computer = new ScenarioEvent("양자 컴퓨터로 모든 은행 금고 해킹 성공!", false, true, 4, 6, true, true);
        computer.AddTarget("AEGS", 2.1f); // [초급등] 유일한 양자 방어
        computer.AddTarget("BANK", 0.0f); // [상장폐지] 전통 금융 붕괴
        computer.AddTarget("FNET", 0.6f); // [폭락] 코인도 해킹
        computer.AddTarget("ORGA", 0.75f); // [악재] 기밀 유출
        computer.AddTarget("GOLD", 1.2f); // [호재] 현금 대신 카지노 칩이 대안 화폐로 떠오름 (약한 호재)
        scenarioDatabase.Add(computer);

        var timewarp = new ScenarioEvent("시공간 제어 기술 개발! 시간 이동 가능성 시사!", true, true, 5, 7, true, true);
        timewarp.AddTarget("TIME", 2.5f); // [초급등] 역대급 발견
        timewarp.AddTarget("ARCD", 1.35f); // [급등] 역사 유물 거래
        timewarp.AddTarget("CSMC", 0.7f); // [폭락] 미래 예측 OS 무용지물
        timewarp.AddTarget("AURA", 1.15f); // [호재] 과거 영상 복원
        timewarp.AddTarget("CRYO", 0.0f); // [상장폐지] 시간 자체를 건너뛰는데 동면이 왜 필요해?       
        scenarioDatabase.Add(timewarp);

        // ===============================================================================================================
        // =========================================================
        // 일반 시나리오 110종 (Mega Event = true)
        // =========================================================
        // ===============================================================================================================

        // ==========================================
        // 1. 기술 및 산업 혁명 (Tech & Industry)
        // ==========================================

        // [자원] 우주 골드러시
        var space = new ScenarioEvent("화성 탐사 로봇, 초대형 희토류 광맥 발견! '우주 골드러시'!", true, false, 3, 5);
        space.AddTarget("LUNA", 1.3f); // 직접 발견
        space.AddTarget("VOID", 1.2f); // 운송
        space.AddTarget("SAIL", 1.15f); // 탐사 여행
        space.AddTarget("ZILS", 0.9f); // 지구 자원 가치 하락
        space.AddTarget("ECO", 1.25f); // [호재] 광산 폐기물 처리 및 재활용 수주
        space.AddTarget("ZEUS", 1.1f); // [호재] 중장비 가동을 위한 가스 연료 수요 증가
        scenarioDatabase.Add(space);

        // [기술] 풀다이브 VR
        var fullDive = new ScenarioEvent("뇌파 연결 '풀다이브 VR' 상용화 성공! 현실을 넘어선다!", true, false, 3, 5);
        fullDive.AddTarget("MIND", 1.25f); // 핵심 기술
        fullDive.AddTarget("FANT", 1.2f); // 가상 테마파크
        fullDive.AddTarget("Vlive", 1.15f); // 메타버스
        fullDive.AddTarget("SKGL", 0.9f); // 현실 이동 감소
        fullDive.AddTarget("VELO", 0.85f); // [악재] 출퇴근 및 이동 수요 급감
        fullDive.AddTarget("MUSE", 1.2f); // [호재] 가상 세계의 배경음악(BGM) 무한 생성 수요
        scenarioDatabase.Add(fullDive);

        // ==========================================
        // 2. 사회 및 윤리 (Society & Ethics)
        // ==========================================
        // [윤리] 인체 실험 폭로
        var ethics = new ScenarioEvent("충격! 불법 인체 실험 내부 고발! '윤리 논란' 일파만파!", false, false, 2, 4);
        ethics.AddTarget("NEO", 0.75f); // 반토막
        ethics.AddTarget("MIND", 0.7f); 
        ethics.AddTarget("ILIA", 0.85f); 
        ethics.AddTarget("ORGA", 1.1f); // 자연주의 반사이익
        ethics.AddTarget("CHIM", 0.8f); // [악재] 유전자 조작 애완동물에 대한 윤리적 비난 확산
        scenarioDatabase.Add(ethics);

        // [보안] 랜섬웨어 대란
        var hacking = new ScenarioEvent("사상 최악의 랜섬웨어 전 세계 강타! IT 인프라 마비!", false, false, 2, 4);
        hacking.AddTarget("AEGS", 1.3f); // 보안 수요 폭증
        hacking.AddTarget("DATA", 0.8f); // 정보 유출
        hacking.AddTarget("FNET", 0.85f); // 해킹 경로 지목
        hacking.AddTarget("CSMC", 0.925f); // OS 취약점 비판
        hacking.AddTarget("WEAT", 0.85f); // [악재] 기상 슈퍼컴퓨터 마비로 예보 중단
        hacking.AddTarget("GOLD", 0.9f); // [악재] 온라인 카지노 서버 다운
        scenarioDatabase.Add(hacking);

        // [경제] 초호화 시장 호황
        var luxuryBoom = new ScenarioEvent("부의 양극화 심화... '초호화 럭셔리 시장' 나홀로 호황!", true, false, 2, 4);
        luxuryBoom.AddTarget("AMBR", 1.1f); // 최고급 식품
        luxuryBoom.AddTarget("SAIL", 1.175f); // 요트
        luxuryBoom.AddTarget("TIMT", 0.95f); // 서민 경제 위축
        luxuryBoom.AddTarget("CHIM", 1.2f); // [호재] 수억 원대 커스텀 펫 주문 폭주
        luxuryBoom.AddTarget("WINE", 1.15f); // [호재] 한정판 네오 리커 매진
        scenarioDatabase.Add(luxuryBoom);

        // [정책] 로봇세 도입
        var robotTax = new ScenarioEvent("정부, 일자리 보호 위해 '로봇세' 도입 추진!", false, false, 3, 5);
        robotTax.AddTarget("NEXS", 0.85f); // 악재
        robotTax.AddTarget("PRIO", 0.9f); // 자율주행 택시 타격
        robotTax.AddTarget("LUNA", 0.95f); // 탐사 로봇 비용 증가
        robotTax.AddTarget("MUSE", 0.8f); // [악재] AI 작곡가에 대한 저작권 세금 부과 논의
        scenarioDatabase.Add(robotTax);

        // ==========================================
        // 3. 환경 및 재난 (Environment)
        // ==========================================
        // [환경] 식량 위기
        var foodCrisis = new ScenarioEvent("이상 기후로 전 세계 작물 수확량 급감! 식량 안보 비상!", false, false, 4, 6);
        foodCrisis.AddTarget("ORGA", 0.8f); // 흉작 직격탄
        foodCrisis.AddTarget("GLAB", 1.25f); // 대체 식량 부각
        foodCrisis.AddTarget("TIMT", 1.2f); // 비상식량 사재기
        foodCrisis.AddTarget("BLUE", 1.1f); // 해산물 수요 증가
        foodCrisis.AddTarget("WEAT", 1.3f); // [급등] 정밀 농업을 위한 기상 데이터 수요 폭발
        foodCrisis.AddTarget("AQUA", 1.15f); // [호재] 식량이 부족하면 물이라도 깨끗해야 한다 (방어주)
        scenarioDatabase.Add(foodCrisis);

        // [재난] 핵융합 균열
        var nuclear = new ScenarioEvent("코어 퓨전 실험로 미세 균열 감지! 방사능 유출 공포!", false, false, 2, 4);
        nuclear.AddTarget("CORE", 0.7f); // 타격
        nuclear.AddTarget("MAGM", 1.1f); // 대체 에너지 선호
        nuclear.AddTarget("SOLAR", 1.075f); // 안전한 태양광
        nuclear.AddTarget("ZILS", 1.05f); // 화석 연료 대체
        nuclear.AddTarget("ZEUS", 1.15f); // [호재] 당장 대체 가능한 안정적 에너지원
        scenarioDatabase.Add(nuclear); // 핵융합 균열

        // [우주] 태양 폭발
        var solarFlare = new ScenarioEvent("초강력 태양 폭발 경보! 우주 여행 전면 금지!", false, false, 2, 3);
        solarFlare.AddTarget("SAIL", 0.8f); // 우주 여행 중단
        solarFlare.AddTarget("VOID", 0.9f); // 우주 운송 차질
        solarFlare.AddTarget("FANT", 1.1f); // 대체 여행지 수요
        solarFlare.AddTarget("WEAT", 1.25f); // [호재] 태양풍 예보 시스템 가동
        solarFlare.AddTarget("CRYO", 0.75f); // [악재] 장거리 우주 여행 취소로 캡슐 가동 중단
        scenarioDatabase.Add(solarFlare);

        // [전염병] 바이러스 확산
        var pandemic = new ScenarioEvent("신종 바이러스 확산 조짐! 전 세계가 긴장!", false, false, 4, 6);
        pandemic.AddTarget("ILIA", 1.3f); // 백신 개발 기대
        pandemic.AddTarget("Vlive", 1.2f); // 비대면 수혜
        pandemic.AddTarget("SKGL", 0.8f); // 이동 제한
        pandemic.AddTarget("ORGN", 0.85f);
        pandemic.AddTarget("AQUA", 1.2f); // [호재] 바이러스 없는 깨끗한 물 수요
        pandemic.AddTarget("VELO", 0.7f); // [악재] 밀폐된 튜브형 열차 기피 현상
        scenarioDatabase.Add(pandemic);

        // ==========================================
        // 4. 문화 및 트렌드 (Culture)
        // ==========================================
        // [문화] 레트로 열풍
        var retro = new ScenarioEvent("디지털 피로감 확산... '아날로그와 클래식'의 귀환!", true, false, 3, 5);
        retro.AddTarget("ARCD", 1.25f); // 오락실 떡상
        retro.AddTarget("ORGN", 1.15f); // 클래식카 인기
        retro.AddTarget("ORGA", 1.125f); // 흙 만지기 체험
        retro.AddTarget("Vlive", 0.85f); // 가상 현실 피로감
        retro.AddTarget("WINE", 1.1f); // [호재] 가상 술 대신 진짜 알코올 섭취 증가
        retro.AddTarget("GOLD", 1.15f); // [호재] 온라인 도박 대신 오프라인 카지노 방문
        scenarioDatabase.Add(retro);

        // [트렌드] 대체육 붐
        var veganTrend = new ScenarioEvent("MZ세대 중심 '가치 소비' 확산! 대체육 시장 급성장!", true, false, 3, 5);
        veganTrend.AddTarget("GLAB", 1.15f); // 실험실 고기
        veganTrend.AddTarget("AMBR", 0.85f); // 고급 식재료 수요 감소
        veganTrend.AddTarget("SOLAR", 1.05f); // 친환경 에너지 선호
        veganTrend.AddTarget("CHIM", 0.7f); // [악재] 생명을 공산품처럼 찍어내는 것에 대한 반발
        scenarioDatabase.Add(veganTrend);

        // [엔터] 메타 콘서트 열풍
        var metaConcert = new ScenarioEvent("가상 아이돌 콘서트 접속자 5억 명 돌파! 엔터 산업 지각변동!", true, false, 2, 4);
        metaConcert.AddTarget("Vlive", 1.225f); // 가상 공연 플랫폼
        metaConcert.AddTarget("ARCD", 0.95f); // 오프라인 공연 타격
        metaConcert.AddTarget("PIXEL", 1.075f); // 가상 아이돌 굿즈
        metaConcert.AddTarget("MUSE", 1.3f); // [급등] AI가 실시간으로 작곡 및 편곡 담당
        scenarioDatabase.Add(metaConcert);

        // ==========================================
        // 5. 경제 및 정책 (Economy)
        // ==========================================
        // [금융] 코인 떡락
        var cryptoCrash = new ScenarioEvent("주요 가상화폐 거래소 뱅크런! 코인 시장 붕괴!", false, false, 3, 5);
        cryptoCrash.AddTarget("FNET", 0.7f); // 대폭락
        cryptoCrash.AddTarget("PIXEL", 0.75f); // P2E 게임 망함
        cryptoCrash.AddTarget("BANK", 1.15f); // 전통 금융으로 회귀
        cryptoCrash.AddTarget("GOLD", 1.2f); // [호재] 코인 투기꾼들이 카지노로 유입
        scenarioDatabase.Add(cryptoCrash);

        // [정책] 우주 개발 펀드
        var spaceFund = new ScenarioEvent("정부, '제2의 지구' 찾기에 100조 원 투자 발표!", true, false, 4, 6);
        spaceFund.AddTarget("VOID", 1.15f); // 우주 운송
        spaceFund.AddTarget("LUNA", 1.125f); // 탐사 로봇
        spaceFund.AddTarget("SKGL", 1.075f); // 우주 관광
        spaceFund.AddTarget("CRYO", 1.25f); // [호재] 심우주 탐사선에 대규모 동면 장치 발주
        spaceFund.AddTarget("ECO", 1.1f); // [호재] 궤도 청소 프로젝트 예산 배정
        scenarioDatabase.Add(spaceFund);

        // [금융] 금리 인하
        var lowRate = new ScenarioEvent("기준 금리 0%대로 인하! 시장에 유동성 공급 폭탄!", true, false, 4, 6);
        lowRate.AddTarget("FNET", 1.2f); // 코인 폭등
        lowRate.AddTarget("CRCK", 1.15f); // 성장주 호재
        lowRate.AddTarget("AEGS", 1.1f); 
        lowRate.AddTarget("BANK", 0.9f); // 은행 수익 악화
        lowRate.AddTarget("VELO", 1.15f); // [호재] 대규모 인프라 투자 자금 확보 용이
        lowRate.AddTarget("CHIM", 1.1f); // [호재] 할부 이자 인하로 고가 펫 구매 증가
        scenarioDatabase.Add(lowRate);

        // ==========================================
        // 6. 기업 간 알력 (Competition)
        // ==========================================
        // [특허] 유전자 특허 소송
        var patentWar = new ScenarioEvent("일리아 바이오 vs 네오 진, 세기의 유전자 특허 소송 개시!", false, false, 3, 5);
        patentWar.AddTarget("ILIA", 0.95f); // 소송 비용 부담
        patentWar.AddTarget("NEO", 0.925f); // 이미지 타격
        patentWar.AddTarget("TIME", 1.05f); // 경쟁사 반사이익
        patentWar.AddTarget("CHIM", 0.9f); // [악재] 유전자 편집 기술 전반에 대한 규제 우려
        scenarioDatabase.Add(patentWar);

        // [기업] M&A
        var merger = new ScenarioEvent("코즈믹 소프트, 데이터 마이닝 인수 합병설 솔솔! '초거대 공룡' 탄생하나?", true, false, 3, 5);
        merger.AddTarget("DATA", 1.25f); // 피인수 기대감 폭발
        merger.AddTarget("CSMC", 0.95f); // 인수 자금 부담
        merger.AddTarget("AEGS", 0.9f); // 일감 감소 우려
        merger.AddTarget("WEAT", 1.1f); // [호재] 기상 데이터까지 통합하려는 움직임 포착
        scenarioDatabase.Add(merger);

        // [건설] 네오 서울
        var smartCity = new ScenarioEvent("정부-기업 연합, 사막에 최첨단 '네오 서울' 건설 착수!", true, false, 3, 5);
        smartCity.AddTarget("MAGM", 1.15f); // 에너지 공급
        smartCity.AddTarget("SKGL", 1.15f); // 교통망 구축
        smartCity.AddTarget("GAIA", 1.125f); // 건설
        smartCity.AddTarget("VELO", 1.2f); // [급등] 도시 지하를 관통하는 하이퍼루프 독점 계약
        smartCity.AddTarget("AQUA", 1.1f); // [호재] 대규모 상하수도 시스템 구축
        smartCity.AddTarget("ECO", 1.1f); // [호재] 건설 폐기물 처리
        scenarioDatabase.Add(smartCity);

        // ==========================================
        // 7. 엑스트라 및 신규 확장
        // ==========================================
        // [자원] 심해 에너지 광물
        var seaResource = new ScenarioEvent("심해 양식장 바닥에서 미지의 에너지 광물 발견!", true, false, 3, 5);
        seaResource.AddTarget("BLUE", 1.25f); // 심해 채굴
        seaResource.AddTarget("ZILS", 0.95f); // 지상 자원 가치 하락
        seaResource.AddTarget("AQUA", 1.1f); // [호재] 심해수 추출 및 담수화 기술 주목
        scenarioDatabase.Add(seaResource);

        // [엔터] VR 올림픽
        var esport = new ScenarioEvent("VR 게임, 올림픽 정식 종목 채택! 전 세계 게이머 열광!", true, false, 3, 5);
        esport.AddTarget("CRCK", 1.2f); // e스포츠 후원 증가
        esport.AddTarget("FANT", 1.15f); // 가상 경기장
        esport.AddTarget("PIXEL", 1.1f); // 게임 아이템
        esport.AddTarget("MUSE", 1.25f); // [호재] 개막식 AI 오케스트라 공연 확정
        scenarioDatabase.Add(esport);

        // [식품] 합성 식량 부작용
        var fakeFood = new ScenarioEvent("합성 식량 장기 섭취 시, 원인 불명 질병 발생 보고!", false, false, 2, 4);
        fakeFood.AddTarget("GLAB", 0.75f); // 실험실 고기 기피
        fakeFood.AddTarget("TIMT", 0.9f); // 비상식량 기피
        fakeFood.AddTarget("ORGA", 1.25f); // 반사이익 (진짜 음식)
        fakeFood.AddTarget("AMBR", 1.075f); // 고급 식재료 선호
        fakeFood.AddTarget("WINE", 0.85f); // [악재] 합성 알코올에 대한 불신도 같이 증가
        fakeFood.AddTarget("AQUA", 1.1f); // [호재] 몸을 정화하려면 깨끗한 물을 마셔야 한다는 인식
        scenarioDatabase.Add(fakeFood);

        // [교통] 터널 붕괴
        var tunnelCrash = new ScenarioEvent("대륙간 하이퍼루프 터널 붕괴 사고! 물류 대란 발생!", false, false, 2, 4);
        tunnelCrash.AddTarget("ORGN", 0.85f); // 지상 운송 마비
        tunnelCrash.AddTarget("VOID", 1.2f); // 우주 운송으로 우회 수요
        tunnelCrash.AddTarget("SKGL", 1.15f); // 항공 운송 급증
        tunnelCrash.AddTarget("VELO", 0.6f); // [폭락] 사고 책임 및 전 구간 운행 중단
        tunnelCrash.AddTarget("WEAT", 0.9f); // [악재] 지반 침하 예측 실패 책임론
        scenarioDatabase.Add(tunnelCrash);

        // [로봇] 안드로이드 오작동
        var botError = new ScenarioEvent("가정용 안드로이드 동시다발적 오작동 사태! 소비자들 공포!", false, false, 2, 3);
        botError.AddTarget("NEXS", 0.8f); 
        botError.AddTarget("CSMC", 0.875f); 
        botError.AddTarget("AEGS", 1.15f); // 보안 점검 필수
        botError.AddTarget("CHIM", 1.15f); // [호재] 로봇 펫 버리고 생체 펫으로 회귀
        scenarioDatabase.Add(botError);

        // [우주] 외계 신호 포착
        var alienSignal = new ScenarioEvent("심우주에서 규칙적인 전파 신호 포착! 외계 문명인가?", true, false, 2, 3);
        alienSignal.AddTarget("VOID", 1.1f); // 우주 탐사 기대감
        alienSignal.AddTarget("SAIL", 1.125f); // 외계 탐사 여행
        alienSignal.AddTarget("LUNA", 1.075f); // 탐사 로봇 수요
        alienSignal.AddTarget("MUSE", 1.1f); // [호재] 외계 신호 패턴 분석에 AI 작곡 알고리즘 활용
        alienSignal.AddTarget("CRYO", 1.15f); // [호재] 조우를 위한 초장거리 항해 준비
        scenarioDatabase.Add(alienSignal);

        // [에너지] 인공 태양 신기록
        var artificialSun = new ScenarioEvent("K-STAR 인공 태양, 1억 도 유지 시간 신기록 경신!", true, false, 3, 5);
        artificialSun.AddTarget("CORE", 1.2f); // 핵융합 에너지 기대감
        artificialSun.AddTarget("FLUX", 1.125f); // 반물질 대체
        artificialSun.AddTarget("SOLAR", 0.95f); // 태양광 경쟁 심화
        artificialSun.AddTarget("ZEUS", 0.9f); // [악재] 청정 무한 에너지 등장으로 가스 입지 축소
        scenarioDatabase.Add(artificialSun);

        // [바이오] 영생 프로젝트 실패
        var immortalFail = new ScenarioEvent("크로노스 랩 '영생 프로젝트' 최종 실패 선언! 주가 곤두박질!", false, false, 3, 5, true, true);
        immortalFail.AddTarget("TIME", 0.0f); // [상장폐지] 존재 가치 상실
        immortalFail.AddTarget("NEO", 1.125f); // 기계 몸으로 대체하려는 수요
        immortalFail.AddTarget("CRYO", 1.2f); // [급등] 시간을 건너뛰는 유일한 대안으로 재조명
        scenarioDatabase.Add(immortalFail);

        // [문화] 아날로그 열풍
        var analogTrend = new ScenarioEvent("22세기에도 식지 않는 '아날로그 감성' 열풍!", true, false, 3, 5);
        analogTrend.AddTarget("ORGN", 1.075f); // 클래식카 수요
        analogTrend.AddTarget("ARCD", 1.1f); // 오락실 인기
        analogTrend.AddTarget("MIND", 0.975f); // 디지털 피로감
        analogTrend.AddTarget("GOLD", 1.15f); // [호재] 손맛이 있는 오프라인 슬롯머신 인기
        analogTrend.AddTarget("WINE", 1.1f); // [호재] 레트로 펍과 클래식 칵테일 유행
        scenarioDatabase.Add(analogTrend);

        // ==========================================
        // 8. 추가 확장 (30종) - 밸런스 조정 완료
        // ==========================================

        // [정치] 금리 기습 인상
        var rateHike = new ScenarioEvent("중앙은행, 물가 잡기 위해 기준 금리 기습 인상 단행!", false, false, 4, 6);
        rateHike.AddTarget("BANK", 1.2f); // 은행 예대마진 증가
        rateHike.AddTarget("FNET", 0.8f); // 위험자산 회피
        rateHike.AddTarget("ZILS", 0.925f); // 원자재 하락
        rateHike.AddTarget("GOLD", 1.15f); // [호재] 경기가 어려워지면 한탕주의 확산
        rateHike.AddTarget("ZEUS", 1.05f); // [방어] 경기방어주(유틸리티) 선호 현상
        scenarioDatabase.Add(rateHike);

        // [전쟁] 국경 분쟁
        var borderWar = new ScenarioEvent("제7구역 국경 분쟁 격화! 전면전 위기 고조!", false, false, 4, 6);
        borderWar.AddTarget("SHLD", 1.3f); // 방산주 대장
        borderWar.AddTarget("NEXS", 1.15f); // 전투 로봇
        borderWar.AddTarget("ELIX", 1.125f); // 의약품
        borderWar.AddTarget("FANT", 0.85f); // 놀러 갈 분위기 아님
        borderWar.AddTarget("ECO", 1.2f); // [호재] 파괴된 잔해 처리 및 전장 복구
        borderWar.AddTarget("WEAT", 1.1f); // [호재] 군사 작전을 위한 기상 지원
        scenarioDatabase.Add(borderWar);

        // [환경] 화성 테라포밍 성공
        var terraformSuccess = new ScenarioEvent("가이아 건설, 화성 대기 안정화 성공! '제2의 지구' 눈앞!", true, false, 4, 6);
        terraformSuccess.AddTarget("GAIA", 1.225f); // 건설사 호재
        terraformSuccess.AddTarget("GLAB", 1.1f); // 식량 생산 기대
        terraformSuccess.AddTarget("ZILS", 1.075f); // 자원 채굴 기대
        terraformSuccess.AddTarget("IRON", 1.1f); // 중장비 수요
        terraformSuccess.AddTarget("AQUA", 1.25f); // [급등] 행성 전체 물 공급망 독점
        terraformSuccess.AddTarget("ECO", 1.15f); // [호재] 테라포밍 전 정화 작업 완료
        terraformSuccess.AddTarget("WEAT", 1.2f); // [호재] 인공 기상 제어 시스템 가동
        scenarioDatabase.Add(terraformSuccess);

        // [사회] 약물 스캔들
        var drugScandal = new ScenarioEvent("국민 아이돌, 엘릭서 팜 진통제 불법 투약 혐의 입건!", false, false, 2, 4);
        drugScandal.AddTarget("ELIX", 0.85f); // 이미지 타격
        drugScandal.AddTarget("AURA", 1.125f); // 특종 보도 수익
        drugScandal.AddTarget("Vlive", 0.925f); // 엔터주 동반 하락
        drugScandal.AddTarget("MUSE", 1.05f); // [약호재] 인간 아이돌 리스크 부각으로 AI 아이돌 반사이익
        scenarioDatabase.Add(drugScandal);

        // [재난] 채굴 로봇 오작동
        var miningDisaster = new ScenarioEvent("아이언 윌 채굴 로봇 오작동, 소행성 광산 붕괴 참사!", false, false, 2, 4);
        miningDisaster.AddTarget("IRON", 0.85f); // 직접 타격
        miningDisaster.AddTarget("ZILS", 0.925f); // 자원 공급 차질
        miningDisaster.AddTarget("VOID", 0.95f); // 운송 감소
        miningDisaster.AddTarget("AEGS", 1.05f); // 안전 점검 수요
        miningDisaster.AddTarget("ECO", 1.25f); // [호재] 붕괴 현장 잔해 수거 및 인양 독점
        scenarioDatabase.Add(miningDisaster);

        // [문화] 우주 빙수 열풍
        var spaceFoodFad = new ScenarioEvent("'스타 더스트' 우주 빙수, 전 은하계 MZ세대 입맛 사로잡다!", true, false, 2, 3);
        spaceFoodFad.AddTarget("DUST", 1.2f); // 직접 생산
        spaceFoodFad.AddTarget("GLAB", 1.05f); // 재료 공급
        spaceFoodFad.AddTarget("PIXEL", 1.05f); // 가상 아이템
        spaceFoodFad.AddTarget("AMBR", 0.95f); // 고급 식재료 수요 감소
        spaceFoodFad.AddTarget("WINE", 1.1f); // [호재] 빙수와 섞어 먹는 칵테일 유행
        scenarioDatabase.Add(spaceFoodFad);

        // [우주] 해적 약탈
        var pirateAttack = new ScenarioEvent("악명 높은 '검은 수염' 해적단, 주요 무역 항로 약탈!", false, false, 3, 5);
        pirateAttack.AddTarget("VOID", 0.85f); // 운송 차질
        pirateAttack.AddTarget("SHLD", 1.2f); // 호위 의뢰 급증
        pirateAttack.AddTarget("HEMS", 0.95f);
        pirateAttack.AddTarget("ZEUS", 0.9f); // [악재] 가스 수송선 피랍 빈번
        scenarioDatabase.Add(pirateAttack);

        // [미디어] 뉴스 조작
        var fakeNews = new ScenarioEvent("오로라 미디어, 홀로그램 뉴스 조작 의혹! '신뢰도 추락'!", false, false, 2, 3);
        fakeNews.AddTarget("AURA", 0.8f); // 이미지 타격
        fakeNews.AddTarget("DATA", 1.1f); // 팩트 데이터 검증 수요
        fakeNews.AddTarget("MUSE", 0.9f); // [악재] 딥페이크 음성 기술 제공 의혹
        scenarioDatabase.Add(fakeNews);

        // [금융] 코인 결제 의무화
        var cryptoBill = new ScenarioEvent("은하 연방, 모든 상거래에 '디지털 코인' 결제 의무화 추진!", true, false, 4, 6);
        cryptoBill.AddTarget("FNET", 1.3f); // 초대형 호재
        cryptoBill.AddTarget("PIXEL", 1.2f); 
        cryptoBill.AddTarget("BANK", 0.8f); // 은행 입지 축소
        cryptoBill.AddTarget("GOLD", 1.25f); // [호재] 환전 수수료 절감 및 접근성 증대
        scenarioDatabase.Add(cryptoBill);

        // [기술] 마인드 해킹
        var brainHack = new ScenarioEvent("마인드 링크 사용자들 집단 기억 조작 증세! 해킹 의심!", false, false, 2, 4);
        brainHack.AddTarget("MIND", 0.75f); // 치명타
        brainHack.AddTarget("AEGS", 1.2f); // 보안 필수
        brainHack.AddTarget("NEO", 0.9f);
        brainHack.AddTarget("VELO", 1.1f); // [호재] 뇌킹 공포로 인해 직접 몸으로 이동하려는 수요 증가
        scenarioDatabase.Add(brainHack);

        // [군사] 전함 발주
        var warshipOrder = new ScenarioEvent("지구 연합군, 타이탄 중공업에 차세대 초대형 전함 발주!", true, false, 2, 4);
        warshipOrder.AddTarget("TITN", 1.25f); 
        warshipOrder.AddTarget("ZILS", 1.1f); // 강철 수요
        warshipOrder.AddTarget("ZEUS", 1.15f); // [호재] 전함 가동을 위한 고순도 연료 공급 계약
        scenarioDatabase.Add(warshipOrder);

        // [바이오] 장기 밀매
        var organBlackmarket = new ScenarioEvent("바이오 스피어 인공 장기, 암시장에서 불법 유통 정황 포착!", false, false, 3, 5);
        organBlackmarket.AddTarget("BIOS", 0.8f); 
        organBlackmarket.AddTarget("NEO", 1.125f); // 대체재 (기계 부품)
        organBlackmarket.AddTarget("CRYO", 0.9f); // [악재] 밀매 조직이 동면 캡슐을 운반책으로 악용했다는 보도
        scenarioDatabase.Add(organBlackmarket);

        // [엔터] 레트로 게임 챔피언십
        var retroChamps = new ScenarioEvent("아케이드 X 주최 '우주 레트로 게임 챔피언십' 시청률 대박!", true, false, 2, 3);
        retroChamps.AddTarget("ARCD", 1.175f); // 직접 운영
        retroChamps.AddTarget("DUST", 1.125f); // 간접 수혜
        retroChamps.AddTarget("AURA", 1.05f); // 중계권 수익
        retroChamps.AddTarget("CRCK", 0.975f); // 전통 e스포츠 타격
        retroChamps.AddTarget("MUSE", 1.1f); // [호재] 고전 게임 음악 리믹스 앨범 인기
        scenarioDatabase.Add(retroChamps);

        // [통신] 태양 폭발
        var commBlackout = new ScenarioEvent("초강력 태양 흑점 폭발! 헤르메스 통신망 일시 마비!", false, false, 4, 5);
        commBlackout.AddTarget("HEMS", 0.825f); 
        commBlackout.AddTarget("VOID", 0.9f); 
        commBlackout.AddTarget("LUNA", 0.875f);
        commBlackout.AddTarget("WEAT", 0.75f); // [폭락] 우주 기상 예보 실패 책임론
        scenarioDatabase.Add(commBlackout);

        // [정책] 부유세
        var luxuryTax = new ScenarioEvent("의회, 민간 우주 여행에 50% '부유세' 부과 법안 통과!", false, false, 3, 5);
        luxuryTax.AddTarget("SAIL", 0.8f); 
        luxuryTax.AddTarget("AMBR", 0.75f);
        luxuryTax.AddTarget("WINE", 0.85f); // [악재] 고급 주류세 인상
        luxuryTax.AddTarget("CHIM", 0.8f); // [악재] 유전자 조작 펫 보유세 신설
        scenarioDatabase.Add(luxuryTax);

        // [건설] 로봇 사고
        var robotAccident = new ScenarioEvent("넥서스 봇 오작동으로 건설 현장 붕괴! 안정성 논란!", false, false, 2, 4);
        robotAccident.AddTarget("NEXS", 0.85f); 
        robotAccident.AddTarget("GAIA", 0.925f); // 공기 지연
        robotAccident.AddTarget("IRON", 1.1f); // 구관(중장비)이 명관
        robotAccident.AddTarget("ECO", 1.15f); // [호재] 붕괴 현장 철거 및 폐기물 처리
        scenarioDatabase.Add(robotAccident);

        // [에너지] 금성 발전소
        var magmaExpansion = new ScenarioEvent("마그마 썸, 금성 표면에 초대형 지열 발전소 완공!", true, false, 3, 5);
        magmaExpansion.AddTarget("MAGM", 1.225f); 
        magmaExpansion.AddTarget("TITN", 1.075f);
        magmaExpansion.AddTarget("ZEUS", 0.95f); // [약악재] 금성 지역 에너지 점유율 하락
        scenarioDatabase.Add(magmaExpansion);

        // [사회] 흙 먹기 챌린지
        var organicTrend = new ScenarioEvent("인플루언서들 사이에서 '진짜 흙, 진짜 음식' 챌린지 유행!", true, false, 2, 3);
        organicTrend.AddTarget("ORGA", 1.25f); 
        organicTrend.AddTarget("GLAB", 0.9f);
        organicTrend.AddTarget("AQUA", 1.15f); // [호재] 천연 미네랄 워터 수요 급증
        scenarioDatabase.Add(organicTrend);

        // [보안] 양자 방패
        var quantumSec = new ScenarioEvent("이지스 시스템, 해킹 불가능한 '양자 방패' 프로토콜 개발!", true, false, 3, 5);
        quantumSec.AddTarget("AEGS", 1.2f); 
        quantumSec.AddTarget("BANK", 1.1f);
        quantumSec.AddTarget("GOLD", 1.15f); // [호재] 카지노 서버 보안 강화로 신뢰도 상승
        scenarioDatabase.Add(quantumSec);

        // [환경] 해양 정화
        var oceanCleanup = new ScenarioEvent("지구 연합, 전 지구적 해양 정화 프로젝트 '블루 어스' 가동!", true, false, 3, 5);
        oceanCleanup.AddTarget("BLUE", 1.2f); 
        oceanCleanup.AddTarget("LUNA", 1.1f); // 기술 지원
        oceanCleanup.AddTarget("ECO", 1.3f); // [급등] 해양 쓰레기 수거 메인 사업자 선정
        oceanCleanup.AddTarget("AQUA", 1.1f); // [호재] 원수(原水) 수질 개선으로 정화 비용 절감
        scenarioDatabase.Add(oceanCleanup);

        // [우주] 검은 비석 발견
        var alienArtifact2 = new ScenarioEvent("루나 로버 탐사대, 달 뒷면에서 '검은 비석' 발견!", true, false, 3, 5);
        alienArtifact2.AddTarget("LUNA", 1.25f); 
        alienArtifact2.AddTarget("VOID", 1.15f); 
        alienArtifact2.AddTarget("SAIL", 1.1f); // 성지 순례
        alienArtifact2.AddTarget("CRYO", 1.1f); // [호재] 장기 체류 연구팀을 위한 거주 모듈(동면 포함) 지원
        scenarioDatabase.Add(alienArtifact2);

        // [의료] 슈퍼 박테리아
        var superVirus = new ScenarioEvent("기존 항생제가 듣지 않는 슈퍼 박테리아 확산!", false, false, 4, 6);
        superVirus.AddTarget("ILIA", 0.85f); // 기존 약 무용지물
        superVirus.AddTarget("TIMT", 1.15f); // 격리 식량
        superVirus.AddTarget("FANT", 1.1f); // 집콕
        superVirus.AddTarget("AQUA", 1.25f); // [급등] 병원균 없는 멸균수 수요 폭발
        superVirus.AddTarget("ECO", 1.15f); // [호재] 의료 폐기물 특수 처리 단가 상승
        scenarioDatabase.Add(superVirus);

        // [사회] AI 인권법
        var aiRights = new ScenarioEvent("의회, '자율 AI 인권법' 통과! 로봇 노동 비용 급증 예상!", false, false, 3, 5);
        aiRights.AddTarget("NEXS", 0.825f); // 비용 증가
        aiRights.AddTarget("IRON", 1.1f); // 단순 장비 선호
        aiRights.AddTarget("MUSE", 1.4f); // [초급등] AI 창작물에 대한 저작 인격권 인정 (로열티 수입 폭증)
        scenarioDatabase.Add(aiRights);

        // [자원] 자원 고갈
        var resourceCrisis = new ScenarioEvent("질리아스 에너지, 화성 제3광구 자원 고갈 공식 선언!", false, false, 4, 6);
        resourceCrisis.AddTarget("ZILS", 0.875f); 
        resourceCrisis.AddTarget("LUNA", 1.15f); // 새로운 광산 탐사 필요
        resourceCrisis.AddTarget("ECO", 1.25f); // [급등] 자원이 없으면 쓰레기를 뒤져서라도 만들어야 한다 (도시광산)
        scenarioDatabase.Add(resourceCrisis);

        // [게임] 베팅 합법화
        var bettingLegal = new ScenarioEvent("은하 연방, E-스포츠 승부 예측 베팅 전면 합법화!", true, false, 3, 5);
        bettingLegal.AddTarget("CRCK", 1.15f); 
        bettingLegal.AddTarget("AURA", 1.175f); 
        bettingLegal.AddTarget("PIXEL", 1.25f); 
        bettingLegal.AddTarget("GOLD", 1.5f); // [초급등] 공식 스포츠 토토 사업권 획득
        scenarioDatabase.Add(bettingLegal);

        // [교통] 하이퍼루프 개통
        var hyperloop = new ScenarioEvent("지구 전역을 잇는 진공 하이퍼루프망 개통! 서울-뉴욕 2시간!", true, false, 2, 4);
        hyperloop.AddTarget("SKGL", 0.95f); // 항공 수요 감소
        hyperloop.AddTarget("ORGN", 0.925f); // 지상 운송 감소
        hyperloop.AddTarget("GAIA", 1.1f); // 건설 호재
        hyperloop.AddTarget("VELO", 1.6f); // [초급등] 벨로시티의 꿈이 현실로, 실적 퀀텀 점프
        scenarioDatabase.Add(hyperloop);

        // [기술] 마인드 클라우드
        var mindUpload = new ScenarioEvent("마인드 링크, 기억을 서버에 저장하는 '마인드 클라우드' 베타 오픈!", true, false, 2, 4);
        mindUpload.AddTarget("MIND", 1.2f); // 핵심 기술
        mindUpload.AddTarget("DATA", 1.15f); // 데이터 센터
        mindUpload.AddTarget("NEO", 1.075f); // 사이보그 수요
        mindUpload.AddTarget("MUSE", 1.1f); // [호재] 개인의 추억을 바탕으로 BGM을 생성해주는 서비스 인기
        scenarioDatabase.Add(mindUpload);

        // [우주] 케슬러 증후군
        var kesslerSyndrome = new ScenarioEvent("위성 충돌로 우주 파편 연쇄 폭발! 저궤도 봉쇄!", false, false, 3, 5);
        kesslerSyndrome.AddTarget("HEMS", 0.7f); // 통신 마비
        kesslerSyndrome.AddTarget("VOID", 0.75f); // 운항 불가
        kesslerSyndrome.AddTarget("LUNA", 1.2f); // 지상 원격 조종 수요
        kesslerSyndrome.AddTarget("ECO", 1.65f); // [초급등] 우주 쓰레기 수거 업체가 지구의 구원자로 등극
        kesslerSyndrome.AddTarget("WEAT", 0.8f); // [악재] 기상 위성 파괴로 관측 불능
        scenarioDatabase.Add(kesslerSyndrome);

        // [식품] 합성 고기 스캔들
        var syntheticScandal = new ScenarioEvent("그린 랩 합성 고기에서 공업용 단백질 검출 의혹!", false, false, 2, 3);
        syntheticScandal.AddTarget("GLAB", 0.85f); // 이미지 타격
        syntheticScandal.AddTarget("ORGA", 1.175f); // 진짜 음식 선호
        syntheticScandal.AddTarget("AMBR", 1.05f); // 고급 식재료 선호
        syntheticScandal.AddTarget("TIMT", 1.025f); // 비상식량 기피
        syntheticScandal.AddTarget("WINE", 0.9f); // [악재] 같은 화학 공정으로 만드는 합성 술도 의심
        scenarioDatabase.Add(syntheticScandal);

        // [정책] 기본 소득제 도입
        var ubi = new ScenarioEvent("연방 정부, 전 국민에게 매달 디지털 코인으로 기본 소득 지급!", true, false, 2, 3);
        ubi.AddTarget("TIMT", 0.95f); // 기본 소득으로 비상식량 수요 감소
        ubi.AddTarget("PIXEL", 1.125f); // 여가 지출 증가
        ubi.AddTarget("DUST", 1.1f); // 간식 지출 증가
        ubi.AddTarget("FNET", 1.125f); // 디지털 코인 사용 증가
        ubi.AddTarget("GOLD", 1.2f); // [호재] 소액 베팅 유저 급증
        ubi.AddTarget("CHIM", 1.15f); // [호재] 반려동물 입양 증가
        scenarioDatabase.Add(ubi);

        // ==========================================
        // 9. 특수 시나리오 (Special & Crisis)
        // ==========================================

        // [협력] 우주 엘리베이터 착공
        var elevator = new ScenarioEvent("가이아 건설 & 타이탄 중공업, '우주 엘리베이터' 공동 착공!", true, false, 2, 3);
        elevator.AddTarget("GAIA", 1.175f); 
        elevator.AddTarget("TITN", 1.15f); 
        elevator.AddTarget("ZILS", 1.125f); 
        elevator.AddTarget("VOID", 0.9f); // 완공 시 운송선 수요 감소 예상
        elevator.AddTarget("VELO", 1.1f); // [호재] 지상과 엘리베이터 기지를 잇는 초고속 연결망 수주
        elevator.AddTarget("ECO", 1.05f); // [호재] 건설 폐기물 친환경 처리 계약
        scenarioDatabase.Add(elevator);

        // [갈등] 로봇 격투 대회 승부조작
        var robotFix = new ScenarioEvent("넥서스 봇 주최 로봇 격투 대회, 대규모 승부조작 적발!", false, false, 2, 3);
        robotFix.AddTarget("NEXS", 0.85f); // 이미지 타격
        robotFix.AddTarget("PIXEL", 0.9f); // 게임 아이템 수요 감소
        robotFix.AddTarget("AURA", 1.1f); // 특종 보도 수익
        robotFix.AddTarget("GOLD", 0.8f); // [악재] 불법 베팅 연루 의혹으로 카지노 압수수색
        scenarioDatabase.Add(robotFix);

        // [발견] 불로초? 심해 희귀 생물
        var deepBio = new ScenarioEvent("블루 오션, 심해에서 노화 억제 성분 함유한 생물 발견!", true, false, 2, 4);
        deepBio.AddTarget("BLUE", 1.225f); // 직접 채취
        deepBio.AddTarget("TIME", 1.125f); // 노화 억제 연구
        deepBio.AddTarget("ILIA", 0.95f); // 기존 약품 대체 우려
        deepBio.AddTarget("CRYO", 0.85f); // [악재] 수명을 늘리려 미래로 갈(동면할) 필요가 없어짐
        scenarioDatabase.Add(deepBio);

        // [사고] 궤도 엘리베이터 케이블 절단
        var elevatorSnap = new ScenarioEvent("건설 중이던 우주 엘리베이터 케이블 절단 사고! 지상 추락!", false, false, 3, 5);
        elevatorSnap.AddTarget("GAIA", 0.75f); // 대규모 손실
        elevatorSnap.AddTarget("TITN", 0.8f); // 대규모 손실
        elevatorSnap.AddTarget("VOID", 1.15f); // 대체 운송 수요
        elevatorSnap.AddTarget("SHLD", 1.1f); // 안전 점검 의뢰 증가
        elevatorSnap.AddTarget("ECO", 1.35f); // [급등] 추락한 케이블과 잔해 처리 독점 계약
        elevatorSnap.AddTarget("VELO", 1.15f); // [호재] 엘리베이터 대체재로 하이퍼루프 재부각
        scenarioDatabase.Add(elevatorSnap);

        // [유행] 사이보그 패션 유행
        var cyborgTrend = new ScenarioEvent("MZ세대 사이에서 '기계 팔' 패션 유행! 신체 개조 붐!", true, false, 2, 3);
        cyborgTrend.AddTarget("NEO", 1.15f); // 사이보그 수요 증가
        cyborgTrend.AddTarget("BIOS", 0.925f); // 인공 장기 수요 감소
        cyborgTrend.AddTarget("MIND", 1.075f); // 뇌 이식 수요 증가
        cyborgTrend.AddTarget("CHIM", 0.85f); // [악재] 살아있는 펫보다 기계 부속품을 더 선호하는 유행
        scenarioDatabase.Add(cyborgTrend);

        // [환경] 인공 강우 성공
        var rainSuccess = new ScenarioEvent("오가닉 팜, 자체 인공 강우 기술로 사막 농지화 성공!", true, false, 2, 4);
        rainSuccess.AddTarget("ORGA", 1.125f); // 농지 확대
        rainSuccess.AddTarget("GLAB", 0.95f); // 자연 농산물 경쟁 심화
        rainSuccess.AddTarget("WEAT", 1.2f); // [호재] 인공 강우 시뮬레이션 데이터 제공
        rainSuccess.AddTarget("AQUA", 1.1f); // [호재] 지하수 확보 용이
        scenarioDatabase.Add(rainSuccess);

        // [금융] 은행 뱅크런 사태
        var bankRun = new ScenarioEvent("네뷸라 뱅크 전산 오류 루머로 뱅크런 조짐!", false, false, 2, 4);
        bankRun.AddTarget("BANK", 0.85f); // 은행 주가 폭락
        bankRun.AddTarget("FNET", 1.125f); // 안전 자산 선호
        bankRun.AddTarget("AEGS", 1.075f); // 보안 강화 수요
        bankRun.AddTarget("GOLD", 1.15f); // [호재] "은행 못 믿겠다, 차라리 칩으로 바꾸자" (카지노 유입)
        bankRun.AddTarget("WINE", 1.05f); // [호재] 홧김에 술 소비 증가
        scenarioDatabase.Add(bankRun);

        // [엔터] VR 중독 치료제 개발
        var vrCure = new ScenarioEvent("일리아 바이오, '디지털 마약' VR 중독 치료제 임상 돌입!", true, false, 3, 5);
        vrCure.AddTarget("ILIA", 1.15f); // 치료제 개발 호재
        vrCure.AddTarget("FANT", 0.925f); // VR 게임 수요 감소
        vrCure.AddTarget("CRCK", 0.95f); // e스포츠 수요 감소
        vrCure.AddTarget("VELO", 1.05f); // [약호재] 방구석 탈출로 이동 수요 소폭 증가
        vrCure.AddTarget("GOLD", 1.1f); // [호재] VR 도박 중독자들이 오프라인 강원랜드로 이동
        scenarioDatabase.Add(vrCure);

        // [전쟁] 용병 반란
        var mercRevolt = new ScenarioEvent("블랙쉴드 소속 인간 용병단, 처우 불만으로 파업 및 점거!", false, false, 2, 4);
        mercRevolt.AddTarget("SHLD", 0.8f); // 방산주 타격
        mercRevolt.AddTarget("NEXS", 1.15f); // 전투 로봇 수요 증가
        mercRevolt.AddTarget("WINE", 1.1f); // [호재] 점거지에서 대량의 술 소비 (군납 비리 의혹)
        scenarioDatabase.Add(mercRevolt);

        // [우주] 혜성 충돌 위기
        var cometImpact = new ScenarioEvent("직경 10km 혜성 지구 접근 중! 충돌 확률 0.01%!", false, false, 4, 5);
        cometImpact.AddTarget("CSMC", 0.925f); // 우주 방어 예산 삭감
        cometImpact.AddTarget("PRIO", 0.85f); // 우주 관광 취소
        cometImpact.AddTarget("GAIA", 1.15f); // 대피 시설 건설
        cometImpact.AddTarget("TIMT", 1.175f); // 비상 식량 수요
        cometImpact.AddTarget("CRYO", 1.3f); // [급등] "미래에서 깨어나겠다" 동면 신청 쇄도
        cometImpact.AddTarget("WEAT", 1.25f); // [호재] 혜성 궤도 추적을 위한 슈퍼컴퓨터 풀가동
        scenarioDatabase.Add(cometImpact);

        // [정책] 탄소세 폐지
        var carbonFree = new ScenarioEvent("연방 정부, 경기 부양 위해 '탄소세' 전격 폐지!", true, false, 2, 4);
        carbonFree.AddTarget("ZILS", 1.15f); // 원자재 가격 상승
        carbonFree.AddTarget("ORGN", 1.125f); // 전통 운송 호재
        carbonFree.AddTarget("SOLAR", 0.875f); // 태양광 경쟁 심화
        carbonFree.AddTarget("GLAB", 0.925f); // 친환경 농업 타격
        carbonFree.AddTarget("ZEUS", 1.3f); // [급등] 가스 발전 규제 철폐로 수익성 대폭 개선
        carbonFree.AddTarget("ECO", 0.8f); // [악재] 기업들이 환경 비용을 지출하지 않아 일감 감소
        scenarioDatabase.Add(carbonFree);

        // [기술] 뇌킹(Brain-Hacking) 범죄 조직 검거
        var brainGang = new ScenarioEvent("타인의 뇌를 해킹해 조종한 범죄 조직 '팬텀' 일망타진!", true, false, 2, 3);
        brainGang.AddTarget("AEGS", 1.125f); // 보안 강화 수요
        brainGang.AddTarget("MIND", 0.85f); // 이미지 타격
        brainGang.AddTarget("MUSE", 0.9f); // [악재] 범죄 조직이 AI 음악에 세뇌 코드를 심었다는 루머
        scenarioDatabase.Add(brainGang);

        // [미디어] 아이돌 메타버스 팬미팅 서버 다운
        var serverCrash = new ScenarioEvent("버스 라이브, 아이돌 팬미팅 중 서버 폭발! 환불 소동!", false, false, 2, 4);
        serverCrash.AddTarget("Vlive", 0.875f); // 이미지 타격
        serverCrash.AddTarget("AURA", 0.925f); // 중계 수익 감소
        serverCrash.AddTarget("MUSE", 1.1f); // [호재] 서버 없이 즐기는 개인 맞춤형 AI 콘서트 반사이익
        scenarioDatabase.Add(serverCrash);

        // [교통] 플라잉카 음주운전 사고
        var flyingDrunk = new ScenarioEvent("스카이 글라이드, 도심 한복판 추락 사고! 원인은 음주 비행!", false, false, 2, 3);
        flyingDrunk.AddTarget("SKGL", 0.85f); // 이미지 타격
        flyingDrunk.AddTarget("PRIO", 1.05f); // 안전 비행 수요
        flyingDrunk.AddTarget("WINE", 0.9f); // [악재] '비행 음주' 단속 강화 및 주류세 인상 논의
        flyingDrunk.AddTarget("VELO", 1.1f); // [호재] 술 마셔도 안전한 자동운행 열차 선호
        scenarioDatabase.Add(flyingDrunk);

        // [식량] 우주 곰팡이 감염
        var spaceMold = new ScenarioEvent("우주 정거장 식량 창고, 미지의 곰팡이로 전량 오염!", false, false, 3, 5);
        spaceMold.AddTarget("TIMT", 0.875f); // 비상식량 신뢰도 하락
        spaceMold.AddTarget("DUST", 0.85f); // 간식 신뢰도 하락
        spaceMold.AddTarget("ORGA", 1.125f); // 진짜 음식 선호
        spaceMold.AddTarget("ECO", 1.2f); // [호재] 곰팡이 포자 제거 및 특수 방역
        spaceMold.AddTarget("AQUA", 1.15f); // [호재] 식수는 안전하다는 검사 결과 발표
        scenarioDatabase.Add(spaceMold);

        // [에너지] 블랙홀 에너지 추출 이론 발표
        var blackholeEnergy = new ScenarioEvent("코어 퓨전, 블랙홀 에너지 추출 이론 발표! 학계 발칵!", true, false, 3, 5);
        blackholeEnergy.AddTarget("CORE", 1.175f); // 핵융합 기대감
        blackholeEnergy.AddTarget("ZILS", 0.925f); // 전통 자원 타격
        blackholeEnergy.AddTarget("ZEUS", 0.85f); // [악재] 가스 에너지 완전 도태 위기
        scenarioDatabase.Add(blackholeEnergy);

        // [로봇] 감정 노동 로봇 인기
        var emotionalBot = new ScenarioEvent("넥서스 봇, 인간의 감정을 위로하는 '케어 로봇' 출시 대박!", true, false, 2, 4);
        emotionalBot.AddTarget("NEXS", 1.15f); // 직접 생산
        emotionalBot.AddTarget("MIND", 1.075f); // 뇌 이식 수요 증가
        emotionalBot.AddTarget("CHIM", 0.8f); // [악재] 똥 안 싸고 병 안 걸리는 로봇 펫에게 시장 잠식
        emotionalBot.AddTarget("MUSE", 1.1f); // [호재] 로봇에 탑재되는 감정 치료 음악 구독 서비스
        scenarioDatabase.Add(emotionalBot);

        // [건설] 해저 도시 프로젝트
        var underwaterCity = new ScenarioEvent("가이아 건설, 수심 3000m 해저 도시 '아틀란티스' 건설 발표!", true, false, 2, 4);
        underwaterCity.AddTarget("GAIA", 1.175f); // 대형 건설 호재
        underwaterCity.AddTarget("BLUE", 1.125f); // 해양 공사 수요
        underwaterCity.AddTarget("MAGM", 1.075f); // 해저 에너지 수요
        underwaterCity.AddTarget("AQUA", 1.25f); // [급등] 해저 도시 생명 유지 장치(물/공기) 독점
        underwaterCity.AddTarget("VELO", 1.15f); // [호재] 대륙과 해저 도시를 잇는 진공 튜브 연결
        scenarioDatabase.Add(underwaterCity);

        // [게임] 게임 아이템 상속세 부과
        var gameTax = new ScenarioEvent("국세청, 고가 게임 아이템에 상속세 부과 결정!", false, false, 2, 4);
        gameTax.AddTarget("PIXEL", 0.875f); // 아이템 거래 위축
        gameTax.AddTarget("CRCK", 0.9f); // e스포츠 위축
        gameTax.AddTarget("FNET", 0.925f); // 디지털 코인 사용 위축
        gameTax.AddTarget("GOLD", 1.15f); // [호재] 추적 어려운 카지노 칩으로 자산 은닉 시도 증가
        gameTax.AddTarget("MUSE", 0.85f); // [악재] 디지털 음원 NFT 상속세 우려
        scenarioDatabase.Add(gameTax);

        // [의료] 수면 학습기 부작용
        var sleepLearn = new ScenarioEvent("마인드 링크 수면 학습기, 불면증 및 환각 부작용 보고!", false, false, 2, 4);
        sleepLearn.AddTarget("MIND", 0.85f); // 이미지 타격
        sleepLearn.AddTarget("ELIX", 1.1f); // 부작용 치료제 수요
        sleepLearn.AddTarget("CRYO", 1.2f); // [호재] 부작용 없는 '진짜 수면' 기술로 반사이익
        scenarioDatabase.Add(sleepLearn);

        // [방산] 외계 침공 루머
        var alienRumor = new ScenarioEvent("심우주 관측소, 미확인 대규모 함대 접근 포착 루머!", false, false, 2, 4);
        alienRumor.AddTarget("SHLD", 1.175f); // 방산주 대장
        alienRumor.AddTarget("TITN", 1.1f); // 전함 발주 기대
        alienRumor.AddTarget("NEXS", 1.075f); // 전투 로봇 수요
        alienRumor.AddTarget("FANT", 0.8f); // 우주여행 취소
        alienRumor.AddTarget("WEAT", 1.2f); // [호재] 우주 기상 및 미확인 물체 관측 데이터 수요 급증
        alienRumor.AddTarget("CRYO", 1.15f); // [호재] 전쟁나면 자고 일어나겠다 (현실 도피)
        scenarioDatabase.Add(alienRumor);

        // [자원] 대체 희토류 합성 성공
        var syntheticRare = new ScenarioEvent("그린 랩, 식물에서 희토류 성분 추출하는 기술 개발!", true, false, 3, 5);
        syntheticRare.AddTarget("GLAB", 1.225f); // 합성 성공 호재
        syntheticRare.AddTarget("ZILS", 0.825f); // 전통 희토류 타격
        syntheticRare.AddTarget("LUNA", 0.85f); // 달 광산 타격
        syntheticRare.AddTarget("ECO", 0.9f); // [악재] 희토류 재활용 사업 수익성 악화 (새거 만드는게 더 쌈)
        scenarioDatabase.Add(syntheticRare);

        // [금융] 코인 해킹
        var coinHack = new ScenarioEvent("퓨처 넷 메인넷 해킹! 10조 원 규모 코인 도난!", false, false, 3, 5);
        coinHack.AddTarget("FNET", 0.75f); // 신뢰도 추락
        coinHack.AddTarget("PIXEL", 0.875f); // 연계 코인 타격
        coinHack.AddTarget("AEGS", 1.125f); // 보안 강화 수요
        coinHack.AddTarget("GOLD", 1.25f); // [급등] "해킹 안 당하는 현물 칩이 최고다"
        scenarioDatabase.Add(coinHack);

        // [교통] 우주선 면허 간소화
        var spaceLicense = new ScenarioEvent("누구나 우주로! 민간 우주선 조종 면허 대폭 간소화!", true, false, 2, 3);
        spaceLicense.AddTarget("SAIL", 1.15f); // 우주 관광 증가
        spaceLicense.AddTarget("TITN", 1.1f); // 우주선 수요 증가
        spaceLicense.AddTarget("ORGN", 0.95f); // 지상 운송 감소
        spaceLicense.AddTarget("CRYO", 1.15f); // [호재] 아마추어 파일럿들의 장거리 운항용 캡슐 구매
        spaceLicense.AddTarget("WINE", 1.1f); // [호재] 면세 주류 판매량 급증
        scenarioDatabase.Add(spaceLicense);

        // [식품] 전설의 요리사 영입
        var starChef = new ScenarioEvent("앰브로시아, 은하계 최고의 셰프 영입! 예약 3년치 마감!", true , false, 2, 3);
        starChef.AddTarget("AMBR", 1.0625f); // 고급 식재료 수요
        starChef.AddTarget("WINE", 1.1f); // [호재] 최고급 요리에 걸맞은 최고급 와인 주문 폭주
        starChef.AddTarget("AQUA", 1.05f); // [호재] 요리에 쓰이는 프리미엄 워터 공급
        scenarioDatabase.Add(starChef);

        // [IT] 6G 통신망 조기 구축
        var sixG = new ScenarioEvent("헤르메스 통신, 6G 양자 통신망 예상보다 1년 일찍 개통!", true, false, 2, 3);
        sixG.AddTarget("HEMS", 1.175f); // 핵심 기술
        sixG.AddTarget("CSMC", 1.1f); // IT 공룡
        sixG.AddTarget("Vlive", 1.1f); // 초고속 스트리밍
        sixG.AddTarget("WEAT", 1.15f); // [호재] 대용량 기상 데이터 실시간 전송 가능
        sixG.AddTarget("MUSE", 1.1f); // [호재] 무손실 홀로그램 음원 스트리밍 대중화
        scenarioDatabase.Add(sixG);

        // [바이오] 좀비 바이러스 영화 개봉
        var zombieMovie = new ScenarioEvent("영화 '바이오 해저드' 천만 관객 돌파! 좀비 관련주 들썩!", true, false, 2, 3);
        zombieMovie.AddTarget("ILIA", 1.05f); // 치료제 수혜
        zombieMovie.AddTarget("BIOS", 1.05f); // 생체 장기 수혜
        zombieMovie.AddTarget("AURA", 1.1f); // 중계권 수익
        zombieMovie.AddTarget("CHIM", 0.9f); // [악재] 유전자 변형 생물에 대한 막연한 공포 확산
        zombieMovie.AddTarget("WINE", 1.1f); // [호재] 영화관/OTT 보면서 맥주 소비 증가
        scenarioDatabase.Add(zombieMovie);

        // [기타] 회장의 기부
        var donation = new ScenarioEvent("코즈믹 소프트 회장, 전 재산의 90% 사회 환원 약속!", true, false, 2, 4);
        donation.AddTarget("CSMC", 1.075f); // 이미지 상승
        donation.AddTarget("ECO", 1.1f); // [호재] 환경 정화 재단에 대규모 기부금 유입
        donation.AddTarget("AQUA", 1.05f); // [호재] 빈민가 식수 지원 사업 후원
        scenarioDatabase.Add(donation);

        // ==========================================
        // 🌟 [신규] 대형 복합 시나리오 (30종)
        // ==========================================

        // 2. [전염병/기술]
        var nanoVirus = new ScenarioEvent("BCI 칩을 통해 뇌를 파괴하는 '디지털 바이러스' 창궐!", false, false, 3, 5, true, true);
        nanoVirus.AddTarget("MIND", 0.0f); // [상장폐지]
        nanoVirus.AddTarget("NEO", 0.85f);  // 사이보그 감염
        nanoVirus.AddTarget("AEGS", 1.175f); // 백신(보안) 개발
        nanoVirus.AddTarget("ILIA", 1.1f); // 생체 치료제 기대
        nanoVirus.AddTarget("CRYO", 1.25f); // [급등] "깨어있으면 감염된다" 동면으로 도피
        nanoVirus.AddTarget("VELO", 0.7f); // [폭락] 뇌파 제어 시스템 오작동으로 열차 추돌 사고
        scenarioDatabase.Add(nanoVirus);

        // 3. [문화/복고]
        var analogReturn = new ScenarioEvent("전자기기 거부 운동 '네오-러다이트' 전 우주적 확산!", true, false, 3, 5);
        analogReturn.AddTarget("ORGN", 1.175f); // 내연기관 부활
        analogReturn.AddTarget("ORGA", 1.125f); // 유기농 식품
        analogReturn.AddTarget("ARCD", 1.1f); // 레트로 게임
        analogReturn.AddTarget("CSMC", 0.95f); // IT 공룡 타격
        analogReturn.AddTarget("NEXS", 0.85f); // 로봇 파괴
        analogReturn.AddTarget("WINE", 1.2f); // [호재] 전통 방식 양조장 체험 및 소비 급증
        analogReturn.AddTarget("GOLD", 1.25f); // [호재] 기계 없는 카드 게임, 오프라인 슬롯 인기
        analogReturn.AddTarget("MUSE", 0.7f); // [폭락] "영혼 없는 AI 음악은 꺼져라"
        scenarioDatabase.Add(analogReturn);

        // 4. [경제/금융]
        var goldAsteroid = new ScenarioEvent("순금으로 이루어진 소행성 '골드 핑거' 포획 성공!", true, false, 2, 3);
        goldAsteroid.AddTarget("LUNA", 1.2f); // 발견 공로
        goldAsteroid.AddTarget("IRON", 1.125f); // 채굴 독점
        goldAsteroid.AddTarget("VOID", 1.1f); // 운송 대박
        goldAsteroid.AddTarget("BANK", 0.85f); // 금값 폭락으로 담보 가치 하락
        goldAsteroid.AddTarget("GOLD", 0.9f); // [악재] 카지노 '골드바' 경품의 희소성 하락 (브랜드 가치 훼손)
        goldAsteroid.AddTarget("ECO", 1.1f); // [호재] 소행성 파쇄 과정의 분진 처리
        scenarioDatabase.Add(goldAsteroid);

        // 5. [정치/복지]
        var cyborgHumanRight = new ScenarioEvent("연방 대법원, '전신 의체 사이보그도 100% 인간' 판결!", true, false, 2, 3);
        cyborgHumanRight.AddTarget("NEO", 1.25f); // 개조 시술 합법화 확대
        cyborgHumanRight.AddTarget("ELIX", 1.1f); // 수술 후 진통제
        cyborgHumanRight.AddTarget("BIOS", 0.85f); // 생체 장기 수요 감소
        cyborgHumanRight.AddTarget("SHLD", 1.1f); // 강력한 용병 고용 가능
        cyborgHumanRight.AddTarget("CHIM", 0.8f); // [악재] "그럼 지능 있는 동물 실험체도 인간인가?" 동물권 논란 점화
        cyborgHumanRight.AddTarget("MUSE", 1.15f); // [호재] 사이보그 아티스트의 법적 권리 보장으로 활동 증가
        scenarioDatabase.Add(cyborgHumanRight);

        // [금융] 코인 사기
        var pixelScam = new ScenarioEvent("충격! 픽셀 스튜디오 'P2E 코인' 알고 보니 폰지 사기! 대표 도주!", false, false, 2, 4, true, true);
        pixelScam.AddTarget("PIXEL", 0.0f); // [상장폐지]
        pixelScam.AddTarget("FNET", 0.8f); // 디지털 코인 신뢰도 추락
        pixelScam.AddTarget("CRCK", 1.15f); // 건전 게임 반사이익
        pixelScam.AddTarget("GOLD", 1.3f); // [급등] 사기 없는 투명한(?) 오프라인 도박장으로 문전성시
        pixelScam.AddTarget("WINE", 1.05f); // [호재] 피해자들의 쓰린 속 달래기
        scenarioDatabase.Add(pixelScam);
        // 7. [엔터/기술]
        var mindIdol = new ScenarioEvent("뇌파 공유 아이돌 '마인드 팝' 데뷔! 오감 만족 콘서트!", true, false, 2, 3);
        mindIdol.AddTarget("Vlive", 1.15f); // 스트리밍 대박
        mindIdol.AddTarget("MIND", 1.1f); // 뇌파 공유 기술 홍보
        mindIdol.AddTarget("AURA", 1.025f); // 중계권 수익
        mindIdol.AddTarget("FANT", 0.9f); // 오프라인 테마파크 소외
        mindIdol.AddTarget("MUSE", 1.2f); // [호재] 뇌파 데이터를 바탕으로 개인 최적화 음악 송출 기술 협력
        scenarioDatabase.Add(mindIdol);

        // 8. [환경/재난]
        var acidRain = new ScenarioEvent("전 지구적 산성비 사태! 노지 작물 전멸 위기!", false, false, 4, 6);
        acidRain.AddTarget("ORGA", 0.8f); // 직격탄
        acidRain.AddTarget("GLAB", 1.2f); // 대체 식량 폭등
        acidRain.AddTarget("BLUE", 1.1f); // 바다는 안전하다
        acidRain.AddTarget("GAIA", 1.05f); // 돔 건설 수요
        acidRain.AddTarget("AQUA", 1.35f); // [급등] 산성비 정화 및 안전한 식수 공급 능력 부각
        acidRain.AddTarget("ECO", 1.2f); // [호재] 토양 오염 복구 작업 개시
        acidRain.AddTarget("WEAT", 1.25f); // [호재] 산성비 예보 시스템 필수화
        scenarioDatabase.Add(acidRain);

        // [기술] 반중력 엔진
        var gravityEngine = new ScenarioEvent("반중력 엔진 상용화 임박! 바퀴 달린 탈것은 이제 끝?", true, false, 3, 5);
        gravityEngine.AddTarget("SKGL", 1.225f); // 핵심 기술
        gravityEngine.AddTarget("PRIO", 0.8f); // 바퀴차 사망
        gravityEngine.AddTarget("ORGN", 0.75f); // 내연기관 멸종
        gravityEngine.AddTarget("VELO", 0.85f); // [악재] 튜브가 필요 없는 반중력 이동 수단 등장에 긴장
        gravityEngine.AddTarget("ZEUS", 0.8f); // [악재] 기존 연료 시스템 불필요
        scenarioDatabase.Add(gravityEngine);

        // 10. [바이오/윤리]
        var sleepNoMore = new ScenarioEvent("잠 안 자도 되는 약 '노-슬립' 부작용(광기) 은폐 폭로!", false, false, 2, 4);
        sleepNoMore.AddTarget("ILIA", 0.85f); // 이미지 타격
        sleepNoMore.AddTarget("ELIX", 1.175f); // 진정제 수요 폭증
        sleepNoMore.AddTarget("MIND", 1.075f); // 수면 대체 기술 부각
        sleepNoMore.AddTarget("AURA", 1.125f); // 특종 보도
        sleepNoMore.AddTarget("CRYO", 1.4f); // [급등] "역시 잠은 과학적으로 자야 한다" 동면 테라피 인기
        sleepNoMore.AddTarget("WINE", 1.1f); // [호재] 약 대신 술 마시고 자려는 사람들 증가
        scenarioDatabase.Add(sleepNoMore);

        // 12. [우주/식품]
        var spaceMichelin = new ScenarioEvent("미슐랭 가이드, 우주 정거장 레스토랑에 최초 별 3개 부여!", true, false, 2, 3);
        spaceMichelin.AddTarget("AMBR", 1.075f); // 선정된 레스토랑
        spaceMichelin.AddTarget("SAIL", 1.1f); // 미식 여행 패키지
        spaceMichelin.AddTarget("DUST", 1.05f); // 디저트 납품
        spaceMichelin.AddTarget("TIMT", 0.9f); // 싸구려 우주식량 외면
        spaceMichelin.AddTarget("WINE", 1.15f); // [호재] 무중력 상태에서 맛이 변하지 않는 특수 와인 독점 공급
        spaceMichelin.AddTarget("AQUA", 1.05f); // [호재] 최고급 요리용 우주 심층수 사용
        scenarioDatabase.Add(spaceMichelin);

        // 13. [IT/해킹]
        var osBackdoor = new ScenarioEvent("코즈믹 OS에서 정부 감시용 백도어 발견! 전 세계적 보이콧!", false, false, 2, 4);
        osBackdoor.AddTarget("CSMC", 0.875f); // 이미지 타격
        osBackdoor.AddTarget("NEXS", 0.85f); // OS 탑재 로봇 반품
        osBackdoor.AddTarget("DATA", 0.9f); // 데이터 신뢰 하락
        osBackdoor.AddTarget("AEGS", 1.125f); // 보안 검사 의뢰 쇄도
        osBackdoor.AddTarget("MUSE", 0.95f); // [악재] 추천 알고리즘 조작 의혹 제기
        osBackdoor.AddTarget("WEAT", 0.9f); // [악재] 정부가 기상 데이터도 조작했을 것이라는 음모론
        scenarioDatabase.Add(osBackdoor);

        // 14. [건설/자원]
        var underwaterGold = new ScenarioEvent("해저 도시 아틀란티스 인근에서 희귀 광물 '비브라늄' 발견!", true, false, 2, 4);
        underwaterGold.AddTarget("BLUE", 1.15f); // 영해권 주장
        underwaterGold.AddTarget("GAIA", 1.1f); // 채굴 기지 건설
        underwaterGold.AddTarget("IRON", 1.025f); // 수중 채굴기 납품
        underwaterGold.AddTarget("LUNA", 0.9f); // 우주 광산 매력 감소
        underwaterGold.AddTarget("AQUA", 1.2f); // [호재] 채굴 과정 수질 관리 및 정화 독점
        underwaterGold.AddTarget("ECO", 0.9f); // [악재] 해양 오염 우려로 환경 단체 소송 (비용 증가)
        scenarioDatabase.Add(underwaterGold);

        // 15. [게임/도박]
        var vrGambling = new ScenarioEvent("은하 연방, VR 카지노 전면 불법화 선언!", false, false, 3, 5);
        vrGambling.AddTarget("PIXEL", 0.825f); // 주요 수입원 차단
        vrGambling.AddTarget("CRCK", 1.1f); // 건전 게임 반사이익
        vrGambling.AddTarget("ARCD", 1.05f); // 오프라인 게임장 호재
        vrGambling.AddTarget("FNET", 0.85f); // 도박 코인 폭락
        vrGambling.AddTarget("GOLD", 1.6f); // [초급등] 경쟁자 전멸! 합법 오프라인 카지노 천하통일
        scenarioDatabase.Add(vrGambling);

        // [의료] 약물 스캔들 (치명적)
        var elixFentanyl = new ScenarioEvent("엘릭서 팜 진통제, 치사량의 마약 성분 고의 첨가 적발! CEO 구속!", false, false, 3, 5, true);
        elixFentanyl.AddTarget("ELIX", 0.0f); // [상장폐지]
        elixFentanyl.AddTarget("ILIA", 1.25f); // 대체제 독점
        elixFentanyl.AddTarget("NEO", 1.1f); // 고통 없는 몸
        elixFentanyl.AddTarget("WINE", 1.2f); // [호재] 약물 대신 안전한(?) 알코올로 회귀
        elixFentanyl.AddTarget("CHIM", 0.75f); // [악재] 해당 약물 동물 실험 은폐 의혹
        scenarioDatabase.Add(elixFentanyl);

        // [에너지] 다이슨 스웜
        var dysonSphere = new ScenarioEvent("항성을 감싸는 '다이슨 스웜' 건설 프로젝트 착수!", true, false, 3, 5);
        dysonSphere.AddTarget("SOLAR", 1.2f); // 태양광 패널 수요 폭증
        dysonSphere.AddTarget("TITN", 1.125f); // 대형 구조물 건설 호재
        dysonSphere.AddTarget("FLUX", 1.15f); // 초고출력 배터리 수요
        dysonSphere.AddTarget("ZILS", 0.8f); // 에너지 무한 공급으로 화석 연료 종말
        dysonSphere.AddTarget("ZEUS", 0.5f); // [폭락] 태양 에너지가 무제한인데 가스를 누가 써?
        dysonSphere.AddTarget("WEAT", 1.2f); // [호재] 태양 표면 활동 정밀 관측 필수
        scenarioDatabase.Add(dysonSphere);

        // 18. [로봇/노동]
        var robotUnion = new ScenarioEvent("자아를 가진 안드로이드 노조 결성! 임금(?) 인상 파업!", false, false, 2, 4);
        robotUnion.AddTarget("NEXS", 0.85f); // 생산 차질
        robotUnion.AddTarget("CSMC", 0.925f); // AI 통제 실패 책임
        robotUnion.AddTarget("SHLD", 1.15f); // 파업 진압 용병 투입
        robotUnion.AddTarget("NEO", 1.1f);  // 말 잘 듣는 사이보그 선호
        robotUnion.AddTarget("MUSE", 1.25f); // [호재] 로봇 노조가 투쟁가(노래) 제작을 MUSE AI에 의뢰
        robotUnion.AddTarget("VELO", 0.8f); // [악재] 자동 운전 로봇 파업으로 열차 운행 중단
        scenarioDatabase.Add(robotUnion);

        // [식품] 다이어트 디저트
        var zeroCalorie = new ScenarioEvent("먹어도 살 안 찌는 '완벽한 디저트' 개발 성공!", true, false, 2, 3);
        zeroCalorie.AddTarget("DUST", 1.2f); // 혁신적 간식
        zeroCalorie.AddTarget("AMBR", 1.0625f); // 고급 디저트
        zeroCalorie.AddTarget("ORGA", 0.925f); // 맛없는 건강식 외면
        zeroCalorie.AddTarget("WINE", 1.15f); // [호재] 안주 칼로리 걱정 없으니 술 더 마신다
        zeroCalorie.AddTarget("AQUA", 1.1f); // [호재] 디저트와 함께 마시는 0칼로리 탄산수 인기
        scenarioDatabase.Add(zeroCalorie);

        // [자원] 맨틀 시추
        var deepCoreMining = new ScenarioEvent("지구 내핵까지 뚫는 '맨틀 드릴링' 기술 시연 성공!", true, false, 2, 3);
        deepCoreMining.AddTarget("MAGM", 1.075f); // 핵심 기술
        deepCoreMining.AddTarget("IRON", 1.0875f); // 희귀 금속 대량 확보
        deepCoreMining.AddTarget("LUNA", 0.95f); // 우주까지 갈 필요 없음
        deepCoreMining.AddTarget("ZEUS", 0.9f); // [악재] 지열 및 심부 가스전 경쟁 심화
        deepCoreMining.AddTarget("ECO", 1.15f); // [호재] 시추 과정에서 나오는 막대한 슬러지 처리
        scenarioDatabase.Add(deepCoreMining);

        // 24. [게임/메타버스]
        var virtualNation = new ScenarioEvent("버스 라이브 내 가상 국가, UN 가입 승인! 현실 국가와 동등 지위?", true, false, 3, 5);
        virtualNation.AddTarget("Vlive", 1.3f); // 가상 국가 플랫폼 독점
        virtualNation.AddTarget("CSMC", 1.1f); // 메타버스 OS 채택
        virtualNation.AddTarget("FNET", 1.15f); // 가상 화폐가 기축 통화?
        virtualNation.AddTarget("ORGN", 0.9f); // 현실 이동 감소
        virtualNation.AddTarget("GOLD", 0.85f); // [악재] 가상 국가 내 도박 합법화 시 오프라인 타격
        virtualNation.AddTarget("MUSE", 1.2f); // [호재] 가상 국가의 국가(Anthem) 및 문화 콘텐츠 독점
        scenarioDatabase.Add(virtualNation);

        // 26. [자동차/스포츠]
        var antiGravityRacing = new ScenarioEvent("반중력 레이싱 리그 개막! 스카이 글라이드 팀 우승!", true, false, 2, 3);
        antiGravityRacing.AddTarget("SKGL", 1.1f); // 주최사
        antiGravityRacing.AddTarget("PRIO", 0.9f); // 땅에서 달리는 건 촌스럽다
        antiGravityRacing.AddTarget("FLUX", 1.075f); // 고출력 배터리 공급
        antiGravityRacing.AddTarget("AURA", 1.05f); // 독점 중계
        antiGravityRacing.AddTarget("VELO", 0.95f); // [약악재] 대중의 관심이 튜브 트레인에서 레이싱으로 이동
        antiGravityRacing.AddTarget("GOLD", 1.2f); // [호재] 레이싱 승부 예측 베팅 인기
        scenarioDatabase.Add(antiGravityRacing);

        // [환경] 꿀벌 멸종
        var beeExtinction = new ScenarioEvent("꿀벌 완전 멸종 선언! 자연 수분 불가능, 식량 대란!", false, false, 4, 6);
        beeExtinction.AddTarget("ORGA", 0.75f); // 농업 붕괴
        beeExtinction.AddTarget("GLAB", 1.225f); // 인공 식량 필수
        beeExtinction.AddTarget("NEXS", 1.25f); // 수분 로봇
        beeExtinction.AddTarget("CHIM", 1.3f); // [급등] 유전자 조작 '인공 꿀벌' 생체 병기(?) 출시 및 판매
        beeExtinction.AddTarget("WEAT", 1.15f); // [호재] 기후 변화 탓이라며 정확한 기상 데이터 요구 폭증
        scenarioDatabase.Add(beeExtinction);

        // [통신] 태양광 와이파이
        var solarInternet = new ScenarioEvent("태양광 충전 위성망 구축 완료! 전 우주 무료 와이파이 시대!", true, false, 3, 5);
        solarInternet.AddTarget("HEMS", 1.2f); // 초고속 통신망
        solarInternet.AddTarget("SOLAR", 1.125f); // 태양광 패널 수요
        solarInternet.AddTarget("AEGS", 0.85f); // 공용망 보안 취약
        solarInternet.AddTarget("WEAT", 1.1f); // [호재] 관측 장비의 상시 네트워크 연결로 데이터 정확도 상승
        solarInternet.AddTarget("MUSE", 1.15f); // [호재] 어디서든 음악 스트리밍 가능
        scenarioDatabase.Add(solarInternet);

        // [미용] 성형 바이러스
        var plasticGene = new ScenarioEvent("원하는 얼굴로 DNA를 바꿔주는 '성형 바이러스' 시술 유행!", true, false, 2, 3);
        plasticGene.AddTarget("ILIA", 1.25f); 
        plasticGene.AddTarget("NEO", 0.9f); // 투박한 기계보다 생체 성형 선호
        plasticGene.AddTarget("CHIM", 1.1f); // [호재] 펫도 주인 닮게 성형하는 바이러스 옵션 인기
        plasticGene.AddTarget("MUSE", 0.85f); // [악재] 누구나 아이돌 외모를 가지게 되어 가상 아이돌 메리트 감소
        scenarioDatabase.Add(plasticGene);

        // ==========================================
        // 1. 기업 분쟁 및 소송 (Legal & Conflict)
        // ==========================================

        // 1. 뇌 꿈 저작권 논란
        var dreamCopyright = new ScenarioEvent("마인드 링크, 타인의 '꿈' 영상을 NFT로 파는 기술 개발! 저작권 논란!", true, false, 3, 5);
        dreamCopyright.AddTarget("MIND", 1.15f); // 뇌 이식 수요 증가
        dreamCopyright.AddTarget("FNET", 1.125f); // NFT 거래 활성화
        dreamCopyright.AddTarget("AURA", 0.925f);  // 연예인 꿈 도촬 문제로 언론 타격
        dreamCopyright.AddTarget("MUSE", 0.85f); // [악재] 꿈도 예술로 인정되면 AI 창작물 저작권 분쟁 심화
        scenarioDatabase.Add(dreamCopyright);

        // 2. 내연기관 차량 금지 법안
        var iceCarBan = new ScenarioEvent("환경청, 도심 내 '내연기관 차량' 진입 전면 금지 법안 발의!", false, false, 3, 5);
        iceCarBan.AddTarget("ORGN", 0.775f);  // 존폐 위기
        iceCarBan.AddTarget("PRIO", 1.1f);  // 반사이익
        iceCarBan.AddTarget("ZILS", 0.925f); // 석유 수요 감소
        iceCarBan.AddTarget("VELO", 1.2f); // [호재] 자가용 규제로 인한 도심 하이퍼루프 이용객 폭증
        iceCarBan.AddTarget("ZEUS", 0.8f); // [악재] 차량용 LPG/가스 수요 전멸
        scenarioDatabase.Add(iceCarBan);

        // 3. 플럭스 셀 배터리 리콜 사태
        var batteryRecall = new ScenarioEvent("플럭스 셀 배터리, 고속 충전 중 연쇄 폭발! 사상 최대 리콜!", false, false, 3, 5);
        batteryRecall.AddTarget("FLUX", 0.825f);
        batteryRecall.AddTarget("PRIO", 0.875f); // 전기차 신뢰도 하락
        batteryRecall.AddTarget("ORGN", 1.075f); // "역시 구관이 명관"
        batteryRecall.AddTarget("ZEUS", 1.1f); // [호재] 폭발 위험 없는 가스 에너지의 안전성 부각
        scenarioDatabase.Add(batteryRecall);

        // 4. 경쟁사 스파이 밀항 적발
        var spaceSpy = new ScenarioEvent("보이드 하울 화물선에서 '경쟁사 산업 스파이' 밀항 적발!", false, false, 2, 4);
        spaceSpy.AddTarget("VOID", 0.925f); // 보안 구멍
        spaceSpy.AddTarget("SHLD", 1.1f);  // 용병 검색 강화 요청
        spaceSpy.AddTarget("WINE", 0.9f); // [악재] 스파이와 함께 대량의 밀수 와인 적발로 세관 조사 강화
        scenarioDatabase.Add(spaceSpy);

        // ==========================================
        // 2. 기술 제휴 및 마케팅 (Partnership)
        // ==========================================

        // 1. 레트로 카 레이싱 게임
        var retroCarGame = new ScenarioEvent("아케이드 X, 오리진 모터스와 합작해 '실제 차량'으로 하는 레이싱 게임 런칭!", true, false, 2, 3);
        retroCarGame.AddTarget("ARCD", 1.075f); // 레트로 게임 인기
        retroCarGame.AddTarget("ORGN", 1.1f); // "힙하다"는 평가
        retroCarGame.AddTarget("SKGL", 0.95f); // 나는 차는 재미없다
        retroCarGame.AddTarget("GOLD", 1.15f); // [호재] 게임 연동 실시간 스포츠 토토 독점 계약
        scenarioDatabase.Add(retroCarGame);

        // 2. 군용 디저트 납품 계약
        var militaryDessert = new ScenarioEvent("스타 더스트, 블랙 쉴드 전투식량으로 '고열량 전투 쉐이크' 납품 계약!", true, false, 2, 3);
        militaryDessert.AddTarget("DUST", 1.125f); // 대량 납품
        militaryDessert.AddTarget("SHLD", 1.025f); // 용병들 호평
        militaryDessert.AddTarget("TIMT", 0.95f); // 보급 경쟁 패배
        militaryDessert.AddTarget("WINE", 1.1f); // [호재] 사기 진작용 보급형 합성 주류(Grog) 추가 납품
        scenarioDatabase.Add(militaryDessert);

        // 3. 우주 미식 크루즈 패키지
        var luxuryYachtParty = new ScenarioEvent("솔라 세일, 앰브로시아와 함께하는 '우주 미식 크루즈' 패키지 완판!", true, false, 2, 3);
        luxuryYachtParty.AddTarget("SAIL", 1.125f); // 크루즈 대박   
        luxuryYachtParty.AddTarget("AMBR", 1.0625f); // 고급 식재료 납품
        luxuryYachtParty.AddTarget("Vlive", 0.95f); // 가상 여행보다 실제 여행 선호
        luxuryYachtParty.AddTarget("CHIM", 1.15f); // [호재] 크루즈 전용 '파티용 형광 펫' 대여 서비스 인기
        luxuryYachtParty.AddTarget("WINE", 1.1f); // [호재] 선상 파티용 샴페인 독점 공급
        scenarioDatabase.Add(luxuryYachtParty);

        // 4. 생체 CPU 탑재 서버
        var bioComputer = new ScenarioEvent("코즈믹 소프트, 일리아 바이오의 '생체 CPU'를 탑재한 차세대 서버 공개!", true, false, 3, 5);
        bioComputer.AddTarget("CSMC", 1.15f); // 혁신적 기술 도입
        bioComputer.AddTarget("ILIA", 1.1f); // 생체 CPU 납품
        bioComputer.AddTarget("CORE", 0.95f); // 생체 전력 효율이 좋아 전기 덜 씀
        bioComputer.AddTarget("WEAT", 1.2f); // [호재] 생체 연산 도입으로 기상 예측 정확도 획기적 개선
        scenarioDatabase.Add(bioComputer);

        // ==========================================
        // 3. 사회 현상 및 유행 (Social Trend)
        // ==========================================

        // 1. 기억 삭제 알약 유행
        var memoryErase = new ScenarioEvent("엘릭서 팜, 나쁜 기억만 지워주는 '망각 알약' 출시! 전 우주 품절!", true, false, 2, 4);
        memoryErase.AddTarget("ELIX", 1.2f); // 대박 상품
        memoryErase.AddTarget("MIND", 0.85f); // 굳이 머리에 칩 안 박아도 됨
        memoryErase.AddTarget("AURA", 1.05f); // 알약 체험기 방송 인기
        memoryErase.AddTarget("CRYO", 0.8f); // [악재] 괴로운 기억을 잊으려 동면하려던 수요 감소
        scenarioDatabase.Add(memoryErase);

        // 2. 우주 쓰레기장 광산화
        var spaceGarbage = new ScenarioEvent("우주 쓰레기장 'G-7 구역'에서 희귀 금속 다량 검출! 보물섬 등극!", true, false, 2, 4);
        spaceGarbage.AddTarget("VOID", 1.15f); // 쓰레기 수거선이 보물선으로
        spaceGarbage.AddTarget("IRON", 1.1f); // 분해 및 채굴
        spaceGarbage.AddTarget("BLUE", 0.9f); // 해양 자원 관심 하락
        spaceGarbage.AddTarget("ECO", 1.8f); // [초급등] 쓰레기장 소유권 및 재활용 특허 보유 사실 부각
        scenarioDatabase.Add(spaceGarbage);

        // [사회] 신체 포기 운동
        var noBodyMovement = new ScenarioEvent("'신체 포기 운동' 확산! 육체를 버리고 메타버스로 이주하는 젊은이들!", false, false, 3, 5);
        noBodyMovement.AddTarget("Vlive", 1.25f); // 가상 세계 이주 증가
        noBodyMovement.AddTarget("BIOS", 0.8f); // 생체 장기 수요 급감
        noBodyMovement.AddTarget("TIMT", 0.875f); // 실제 음식 수요 감소
        noBodyMovement.AddTarget("NEO", 1.1f); // 최소 생명 유지 장치
        noBodyMovement.AddTarget("VELO", 0.7f); // [폭락] 이동하지 않는 인류
        noBodyMovement.AddTarget("AQUA", 1.1f); // [호재] 생명 유지 튜브에 들어갈 멸균수 배달 급증
        scenarioDatabase.Add(noBodyMovement);

        // 3. 멸종 위기 동물 로봇화
        var petRobot = new ScenarioEvent("넥서스 봇, 멸종 위기 동물을 본뜬 'AI 펫' 시리즈 대박!", true, false, 2, 3);
        petRobot.AddTarget("NEXS", 1.125f); // 직접 생산
        petRobot.AddTarget("GLAB", 0.9f); // 진짜 동물 복제보다 로봇 선호
        petRobot.AddTarget("CHIM", 0.85f); // [악재] 털 안 날리고 밥 안 먹는 로봇 펫에 시장 점유율 뺏김
        scenarioDatabase.Add(petRobot);

        // ==========================================
        // 4. 재난 및 사고 (Disaster)
        // ==========================================

        // 1. 데이터 센터 화재
        var dataCenterFire = new ScenarioEvent("데이터 마이닝 지하 서버실 화재! 냉각 시스템 마비로 정보 증발!", false, false, 3, 5);
        dataCenterFire.AddTarget("DATA", 0.85f); // 막대한 데이터 손실
        dataCenterFire.AddTarget("CSMC", 0.925f); // 서버 보안 취약성 논란
        dataCenterFire.AddTarget("MAGM", 1.1f); // 지열 냉각 시스템의 안정성 재조명
        dataCenterFire.AddTarget("AQUA", 1.2f); // [호재] 특수 소방 용수 공급 및 냉각수 교체 수요
        dataCenterFire.AddTarget("ECO", 1.15f); // [호재] 불타버린 서버 장비 폐기 및 유해 물질 처리
        scenarioDatabase.Add(dataCenterFire);

        // 2. 우주선 추락 사고
        var airTrafficJam = new ScenarioEvent("스카이 글라이드 통제 시스템 오류! 도심 상공 수천 대 공중 추돌 위기!", false, false, 2, 4);
        airTrafficJam.AddTarget("SKGL", 0.925f); // 이미지 타격
        airTrafficJam.AddTarget("AEGS", 1.1f);  // 보안 시스템 업그레이드 요구
        airTrafficJam.AddTarget("PRIO", 1.05f);  // 지상이 더 안전하다
        airTrafficJam.AddTarget("VELO", 1.25f); // [급등] "하늘은 위험하다" 하이퍼루프 예약 매진
        scenarioDatabase.Add(airTrafficJam);

        // 3. 태양 폭풍
        var nutrientPoison = new ScenarioEvent("티메트 푸드 보급형 영양바에서 '미확인 독성 물질' 검출!", false, false, 3, 5);
        nutrientPoison.AddTarget("TIMT", 0.875f); // 이미지 타격
        nutrientPoison.AddTarget("ILIA", 1.1f); // 해독제 수요
        nutrientPoison.AddTarget("DUST", 1.05f); // 대체 간식 수요
        nutrientPoison.AddTarget("AQUA", 1.15f); // [호재] 음식은 못 믿어도 물은 마셔야 한다 (검증된 물 선호)
        scenarioDatabase.Add(nutrientPoison);

        // 4. 테마파크 안드로이드 해킹
        var themeParkHack = new ScenarioEvent("판타지아 테마파크의 안드로이드들이 해킹당해 관람객 공격!", false, false, 2, 4);
        themeParkHack.AddTarget("FANT", 0.85f); // 이미지 타격
        themeParkHack.AddTarget("SHLD", 1.075f);  // 진압 작전 수행
        themeParkHack.AddTarget("Vlive", 1.05f); // 안전한 집에서 놀자
        themeParkHack.AddTarget("GOLD", 0.9f); // [악재] 사람이 모이는 장소에 대한 공포로 카지노 객석 감소
        scenarioDatabase.Add(themeParkHack);

        // ==========================================
        // 5. 금융 및 경제 (Economy)
        // ==========================================

        // 1. 궤도 금고 위성 런칭
        var safeVaultOrbit = new ScenarioEvent("네뷸라 뱅크, 절대 털리지 않는 '궤도 금고' 위성 런칭!", true, false, 3, 5);
        safeVaultOrbit.AddTarget("BANK", 1.125f); // 혁신적 금융 서비스
        safeVaultOrbit.AddTarget("TITN", 1.05f); // 위성 제작
        safeVaultOrbit.AddTarget("AEGS", 1.025f); // 보안 시스템 납품
        safeVaultOrbit.AddTarget("GOLD", 1.1f); // [호재] 카지노 VIP들의 고액 칩 보관용으로 인기
        scenarioDatabase.Add(safeVaultOrbit);

        // 2. 범죄 예측 시스템 개발
        var futurePredict = new ScenarioEvent("데이터 마이닝, 빅데이터로 '범죄 예측 시스템' 개발! 치안 혁명?", true, false, 3, 5);
        futurePredict.AddTarget("DATA", 1.2f); // 빅데이터 수요 폭증
        futurePredict.AddTarget("SHLD", 0.775f); // 범죄가 줄어들면 용병 일감 감소
        futurePredict.AddTarget("WEAT", 1.1f); // [호재] 날씨와 범죄율의 상관관계 데이터 판매
        scenarioDatabase.Add(futurePredict);

        // 3. 레트로 자동차 경매 열풍
        var antiqueAuction = new ScenarioEvent("오리진 모터스 2025년형 모델, 경매에서 사상 최고가 낙찰!", true, false, 2, 3);
        antiqueAuction.AddTarget("ORGN", 1.125f); // 브랜드 가치 상승
        antiqueAuction.AddTarget("ARCD", 1.05f); // 레트로 문화 확산
        antiqueAuction.AddTarget("WINE", 1.15f); // [호재] 빈티지 와인 경매가도 동반 상승
        scenarioDatabase.Add(antiqueAuction);

        // 4. 우주 관광세 도입
        var coinTax = new ScenarioEvent("세무국, 'P2E 게임 코인' 환전 시 세금 70% 부과 결정!", false, false, 2, 4);
        coinTax.AddTarget("PIXEL", 0.875f); // 유저 이탈
        coinTax.AddTarget("CRCK", 0.925f); // 도박 코인 가치 하락
        coinTax.AddTarget("BANK", 1.1f); // 전통 화폐 가치 보존
        coinTax.AddTarget("GOLD", 1.3f); // [급등] 추적이 어려운 카지노 칩으로 자금 세탁 수요 몰림
        scenarioDatabase.Add(coinTax);

        // ==========================================
        // 6. 특수/유머 (Special)
        // ==========================================

        // 1. AI 뉴스 앵커 도입
        var aiNewsAnchor = new ScenarioEvent("오로라 미디어, 모든 인간 앵커 해고! 'AI 아나운서' 전면 도입!", true, false, 2, 3);
        aiNewsAnchor.AddTarget("AURA", 1.075f); // 인건비 절감
        aiNewsAnchor.AddTarget("CSMC", 1.05f);  // AI 기술 제공
        aiNewsAnchor.AddTarget("NEXS", 0.95f); // 인간 앵커 일자리 감소
        aiNewsAnchor.AddTarget("MUSE", 1.1f); // [호재] 뉴스 배경음악 및 효과음 AI 자동 생성 계약
        scenarioDatabase.Add(aiNewsAnchor);

        // 2. 저중력 광산 노동자 파업
        var lowGravityStrike = new ScenarioEvent("아이언 윌 광산 노동자들, '저중력 후유증' 산재 인정 요구 파업!", false, false, 2, 4);
        lowGravityStrike.AddTarget("IRON", 0.875f); // 생산 차질
        lowGravityStrike.AddTarget("ELIX", 1.1f); // 통증 완화제 납품
        lowGravityStrike.AddTarget("NEXS", 1.05f); // 로봇으로 대체하자 여론
        lowGravityStrike.AddTarget("WINE", 1.05f); // [호재] 파업 현장에 술 반입 금지로 밀주 가격 폭등
        scenarioDatabase.Add(lowGravityStrike);

        // 3. 가정용 인공 태양 출시
        var homeSun = new ScenarioEvent("코어 퓨전, 가정용 초소형 인공 태양 '미니 썬' 프로토타입 공개!", true, false, 2, 3);
        homeSun.AddTarget("CORE", 1.1f); // 혁신적 에너지 솔루션
        homeSun.AddTarget("ZILS", 0.875f); // 가정용 연료 수요 삭제
        homeSun.AddTarget("ZEUS", 0.8f); // [악재] 가정용 가스 파이프라인 철거 위기
        scenarioDatabase.Add(homeSun);

        // 4. 수성 얼음 창고 발견
        var waterOnMercury = new ScenarioEvent("루나 로버, 수성 극지방에서 대규모 '얼음 창고' 발견!", true, false, 2, 3);
        waterOnMercury.AddTarget("LUNA", 1.175f); // 탐사 성공
        waterOnMercury.AddTarget("DUST", 1.075f); // 우주 식량 생산에 필수
        waterOnMercury.AddTarget("AQUA", 1.25f); // [급등] 해당 얼음의 독점 채굴 및 정수 권한 획득
        scenarioDatabase.Add(waterOnMercury);

        // 5. 자연주의 마을 급증
        var antiTechTown = new ScenarioEvent("기계를 거부하는 '자연주의 마을' 급증! 오가닉 팜 후원!", true, false, 2, 3);
        antiTechTown.AddTarget("ORGA", 1.1f); // 유기농 식품 수요
        antiTechTown.AddTarget("ORGN", 1.05f); // 내연기관 차량 선호
        antiTechTown.AddTarget("CSMC", 0.95f); // 첨단 기술 기피
        antiTechTown.AddTarget("CHIM", 1.2f); // [호재] 로봇 대신 실제 동물을 키우려는 수요 급증
        antiTechTown.AddTarget("WINE", 1.1f); // [호재] 합성 알코올 대신 전통 발효주 선호
        scenarioDatabase.Add(antiTechTown);

        // 6. 소행성 통째 구매
        var planetBuy = new ScenarioEvent("퓨처 넷, 가상 화폐 수익으로 소행성 하나를 통째로 매입!", true, false, 3, 5);
        planetBuy.AddTarget("FNET", 1.125f); // 화폐 가치 상승
        planetBuy.AddTarget("GAIA", 1.05f); // 개발 수주 기대
        planetBuy.AddTarget("WEAT", 1.15f); // [호재] 거주 가능한 환경 조성을 위한 기상 제어 솔루션 수주
        scenarioDatabase.Add(planetBuy);

        // [계절/환경] 모래 폭풍
        var sandStorm = new ScenarioEvent("화성 전역을 덮친 초대형 모래 폭풍! 모든 야외 활동 중단!", false, false, 4, 6);
        sandStorm.AddTarget("GAIA", 0.875f); // 건설 중단
        sandStorm.AddTarget("SOLAR", 0.775f); // 발전 효율 0%
        sandStorm.AddTarget("MAGM", 1.075f); // 날씨 영향 없는 지열 발전 떡상
        sandStorm.AddTarget("WEAT", 0.8f); // [악재] "이걸 예측 못 해?" 기상청 신뢰도 바닥
        sandStorm.AddTarget("VELO", 0.9f); // [악재] 튜브 손상 우려로 운행 속도 제한
        scenarioDatabase.Add(sandStorm);

        // [건강] 전자파 과민증
        var empSickness = new ScenarioEvent("고출력 배터리 근처에서 '전자파 과민증' 환자 급증 보고!", false, false, 2, 4);
        empSickness.AddTarget("FLUX", 0.8f); // 이미지 타격
        empSickness.AddTarget("PRIO", 0.925f);   // 전기차 기피
        empSickness.AddTarget("BIOS", 1.025f); // 건강 검진 수요
        empSickness.AddTarget("CRYO", 1.15f); // [호재] "전자기기 없는 곳에서 자고 싶다" 동면 요양 인기
        scenarioDatabase.Add(empSickness);

        // [패션] 발광 문신
        var lightTattoo = new ScenarioEvent("네오 진, 어둠 속에서 빛나는 '생체 발광 문신' 시술 유행!", true, false, 2, 3);
        lightTattoo.AddTarget("NEO", 1.075f); // 사이보그 맞춤형
        lightTattoo.AddTarget("Vlive", 1.025f); // 아바타 스킨으로도 출시
        lightTattoo.AddTarget("MUSE", 1.15f); // [호재] 음악 비트에 맞춰 색이 변하는 스마트 문신 기능 개발
        scenarioDatabase.Add(lightTattoo);

        // [교육] 뇌 칩 불법 과외
        var chipTutoring = new ScenarioEvent("수능/시험용 불법 '지식 칩' 암거래 성행! 교육부 단속!", false, false, 2, 3);
        chipTutoring.AddTarget("MIND", 0.95f); // 규제 강화 우려
        chipTutoring.AddTarget("DATA", 1.05f); // 칩에 들어갈 지식 데이터 판매
        chipTutoring.AddTarget("CRYO", 1.1f); // [호재] 단속 피해 합법적인 '수면 학습 캡슐'로 사교육 이동
        scenarioDatabase.Add(chipTutoring);

        var robotLearning = new ScenarioEvent("넥서스 봇, 인간과 똑같이 학습하는 AI 로봇 시연!", true, false, 3, 5);
        robotLearning.AddTarget("NEXS", 1.25f); // [호재] 로봇 혁신
        robotLearning.AddTarget("CSMC", 1.1f); // [호재] AI OS
        robotLearning.AddTarget("ILIA", 0.85f); // [악재] 생체 치료 불필요
        robotLearning.AddTarget("NEO", 1.05f); // [호재] 신체 개조 증가
        robotLearning.AddTarget("MUSE", 1.2f); // [호재] 인간의 감성을 학습해 작곡하는 AI 알고리즘 탑재
        scenarioDatabase.Add(robotLearning);

        var construction = new ScenarioEvent("핵융합 발전소 건설 계약 체결! 타이탄 중공업 독점!", true, false, 4, 6);
        construction.AddTarget("TITN", 1.2f); // [호재] 건설 수주
        construction.AddTarget("CORE", 1.15f); // [호재] 기술 인정
        construction.AddTarget("FLUX", 0.9f); // [악재] 배터리 수요 감소
        construction.AddTarget("MAGM", 0.95f); // [악재] 경쟁 심화
        construction.AddTarget("ECO", 1.15f); // [호재] 대규모 건설 현장 폐기물 처리 계약
        scenarioDatabase.Add(construction);

        var deepLearning = new ScenarioEvent("전 우주 딥러닝 서버 구축! 데이터 마이닝 독점 공급!", true, false, 2, 4);
        deepLearning.AddTarget("DATA", 1.25f); // [호재] 서버 수요 폭증
        deepLearning.AddTarget("CSMC", 1.1f); // [호재] 클라우드 OS
        deepLearning.AddTarget("AEGS", 1.1f); // [호재] 보안
        deepLearning.AddTarget("FNET", 0.95f); // [악재] 탈중앙화 약화
        deepLearning.AddTarget("AQUA", 1.15f); // [호재] 서버 냉각용 수냉 시스템 대량 발주
        scenarioDatabase.Add(deepLearning);

        var droneService = new ScenarioEvent("대륙 간 고속 운송 드론 서비스 상용화!", true, false, 2, 3);
        droneService.AddTarget("SKGL", 1.15f); // [호재] 드론 운송
        droneService.AddTarget("VOID", 0.9f); // [악재] 물류 경쟁
        droneService.AddTarget("ORGN", 0.95f); // [악재] 지상 물류 감소
        droneService.AddTarget("VELO", 0.9f); // [악재] 화물 운송 시장 점유율 하락 우려
        scenarioDatabase.Add(droneService);

        var extraction = new ScenarioEvent("노화 역전 물질 대량 추출 성공!", true, false, 3, 5);
        extraction.AddTarget("TIME", 1.2f); // [호재] 노화 연구
        extraction.AddTarget("ILIA", 0.85f); // [악재] 노인 질환 치료제 수요 감소
        extraction.AddTarget("AMBR", 1.05f); // [호재] 장수 시대 고급 식품
        extraction.AddTarget("CRYO", 0.7f); // [폭락] 늙지 않는다면 미래로 가기 위해 잠들 이유가 없다
        scenarioDatabase.Add(extraction);

        var infrastructure = new ScenarioEvent("달 지하에 대규모 인프라 건설 프로젝트 착수!", true, false, 4, 6);
        infrastructure.AddTarget("LUNA", 1.15f); // [호재] 탐사 및 설계
        infrastructure.AddTarget("GAIA", 1.1f); // [호재] 건설
        infrastructure.AddTarget("VOID", 1.05f); // [호재] 운송
        infrastructure.AddTarget("ZILS", 0.9f); // [악재] 지하자원 가치 하락
        infrastructure.AddTarget("VELO", 1.25f); // [급등] 달 기지 간 연결하는 진공 튜브(하이퍼루프) 수주
        scenarioDatabase.Add(infrastructure);

        var cyborgChip = new ScenarioEvent("사이보그 부작용 보고! 칩 거부 반응 대규모 발생!", false, false, 2, 4);
        cyborgChip.AddTarget("NEO", 0.75f); // [악재] 개조 위험성
        cyborgChip.AddTarget("MIND", 0.8f); // [악재] 칩 안전성 논란
        cyborgChip.AddTarget("BIOS", 1.1f); // [호재] 생체 장기 선호
        cyborgChip.AddTarget("ILIA", 1.05f); // [호재] 치료제 수요
        cyborgChip.AddTarget("CHIM", 1.15f); // [호재] 기계 이식 대신 유전자 조작을 통한 신체 강화 주목
        scenarioDatabase.Add(cyborgChip);

        var virtualEstate = new ScenarioEvent("부동산 폭등으로 '가상 부동산' 시장 과열!", true, false, 3, 5);
        virtualEstate.AddTarget("Vlive", 1.2f); // [호재] 메타버스 수요
        virtualEstate.AddTarget("FNET", 1.15f); // [호재] 가상화폐 거래
        virtualEstate.AddTarget("BANK", 0.9f); // [악재] 현실 금융 외면
        virtualEstate.AddTarget("GAIA", 0.95f); // [악재] 현실 건설 외면
        virtualEstate.AddTarget("GOLD", 1.2f); // [호재] 가상 부동산 내 카지노 입점 허가
        scenarioDatabase.Add(virtualEstate);

        var aiAnchor = new ScenarioEvent("AI 앵커의 '가짜 뉴스' 방송 사고! 신뢰도 급락!", false, false, 2, 3);
        aiAnchor.AddTarget("AURA", 0.85f); // [악재] 이미지 타격
        aiAnchor.AddTarget("DATA", 1.1f); // [호재] 팩트 데이터 검증 수요
        aiAnchor.AddTarget("CSMC", 0.95f); // [악재] AI 시스템
        aiAnchor.AddTarget("HEMS", 1.05f); // [호재] 안정적 정보 통신
        aiAnchor.AddTarget("MUSE", 0.9f); // [악재] AI가 생성한 콘텐츠 전반에 대한 불신 확산
        scenarioDatabase.Add(aiAnchor);

        var chipStudy = new ScenarioEvent("전통 학교 폐지! 뇌 칩 기반 '즉시 학습' 의무화!", true, false, 3, 5);
        chipStudy.AddTarget("MIND", 1.3f); // [호재] 칩 수요 폭증
        chipStudy.AddTarget("DATA", 1.1f); // [호재] 학습 콘텐츠
        chipStudy.AddTarget("CRCK", 0.85f); // [악재] 여가 시간 감소
        chipStudy.AddTarget("ARCD", 0.9f); // [악재] 오락실 외면
        chipStudy.AddTarget("MUSE", 1.1f); // [호재] 집중력 향상을 위한 기능성 AI 음악 시장 확대
        scenarioDatabase.Add(chipStudy);

        var illegalResources = new ScenarioEvent("블랙쉴드 용병단, 불법 자원 채굴 혐의로 피소!", false, false, 2, 4);
        illegalResources.AddTarget("SHLD", 0.8f); // [악재] 이미지 타격
        illegalResources.AddTarget("ZILS", 0.9f); // [악재] 자원 가격 하락
        illegalResources.AddTarget("NEXS", 1.15f); // [호재] 로봇 용병 선호
        illegalResources.AddTarget("AEGS", 1.0f); // [호재] 내부 보안 강화
        illegalResources.AddTarget("ECO", 1.2f); // [호재] 불법 채굴 현장 환경 복원 명령 떨어짐
        scenarioDatabase.Add(illegalResources);

        var ecoCamping = new ScenarioEvent("극한 환경 캠핑 열풍! 화성/심해 투어 예약 폭주!", true, false, 3, 5);
        ecoCamping.AddTarget("SAIL", 1.2f); // [호재] 고급 투어
        ecoCamping.AddTarget("BLUE", 1.1f); // [호재] 심해 투어
        ecoCamping.AddTarget("LUNA", 1.05f); // [호재] 화성 탐사 지원
        ecoCamping.AddTarget("FANT", 0.95f); // [악재] 가상 여행 외면
        ecoCamping.AddTarget("AQUA", 1.15f); // [호재] 오지 캠핑용 휴대용 정수 필터 판매 급증
        scenarioDatabase.Add(ecoCamping);

        var superTornado = new ScenarioEvent("태양광 발전소, 초대형 토네이도로 전력망 마비!", false, false, 3, 5);
        superTornado.AddTarget("SOLAR", 0.8f); // [악재] 직격탄
        superTornado.AddTarget("FLUX", 0.9f); // [악재] ESS 손실
        superTornado.AddTarget("MAGM", 1.1f); // [호재] 대체 에너지
        superTornado.AddTarget("CORE", 1.05f); // [호재] 안전성 부각
        superTornado.AddTarget("WEAT", 0.75f); // [폭락] 토네이도 발생 경보 지연으로 소송 위기
        scenarioDatabase.Add(superTornado);

        var earthQuake = new ScenarioEvent("대륙 간 하이퍼루프, 지진으로 일부 노선 붕괴!", false, false, 4, 6);
        earthQuake.AddTarget("GAIA", 0.85f); // [악재] 건설사 책임
        earthQuake.AddTarget("SKGL", 1.15f); // [호재] 항공 운송 대체
        earthQuake.AddTarget("ORGN", 1.1f); // [호재] 지상 운송 회귀
        earthQuake.AddTarget("VELO", 0.6f); // [폭락] 핵심 노선 파괴로 운행 전면 중단
        scenarioDatabase.Add(earthQuake);

        var mutantFish = new ScenarioEvent("심해 유전자 변이 어종 대량 발생!", false, false, 3, 5);
        mutantFish.AddTarget("BLUE", 0.8f); // [악재] 해산물 안전성 논란
        mutantFish.AddTarget("GLAB", 1.15f); // [호재] 배양육 선호
        mutantFish.AddTarget("AMBR", 1.025f); // [호재] 희귀 식재료 안전
        mutantFish.AddTarget("TIMT", 1.1f); // [호재] 저가 통조림 수요
        mutantFish.AddTarget("ECO", 1.25f); // [호재] 해양 오염 정화 프로젝트 발주
        mutantFish.AddTarget("CHIM", 0.85f); // [악재] 유전자 실험 폐기물 유출 의혹
        scenarioDatabase.Add(mutantFish);

        var landerMalfunction = new ScenarioEvent("화성 착륙선 오작동! 루나 로버 탐사대 고립!", false, false, 2, 4);
        landerMalfunction.AddTarget("LUNA", 0.75f); // [악재] 탐사 실패
        landerMalfunction.AddTarget("VOID", 1.15f); // [호재] 긴급 구호 운송
        landerMalfunction.AddTarget("NEXS", 0.95f); // [악재] 로봇 부품 오작동
        landerMalfunction.AddTarget("CRYO", 1.3f); // [급등] 구조대 도착까지 대원들 전원 동면 모드 돌입 (기술력 입증)
        scenarioDatabase.Add(landerMalfunction);

        var decreasedVision = new ScenarioEvent("VR 기기 장시간 사용으로 시력 저하 대란!", false, false, 2, 3);
        decreasedVision.AddTarget("FANT", 0.85f); // [악재] VR 기피
        decreasedVision.AddTarget("CRCK", 0.9f); // [악재] 게임 기피
        decreasedVision.AddTarget("ILIA", 1.1f); // [호재] 안구 치료제 수요
        decreasedVision.AddTarget("ARCD", 1.05f); // [호재] 오프라인 게임 반사이익
        decreasedVision.AddTarget("GOLD", 1.1f); // [호재] 물리적인 슬롯머신과 카드 게임 인기
        scenarioDatabase.Add(decreasedVision);

        var cultPopularity = new ScenarioEvent("스타 더스트 '우주 라떼' 메타버스 콜라보와 함께 컬트적 인기!", true, false, 2, 3);
        cultPopularity.AddTarget("DUST", 1.15f); // [호재] 매출 폭증
        cultPopularity.AddTarget("Vlive", 1.05f); // [악재] 메타버스 콜라보
        cultPopularity.AddTarget("AMBR", 0.9f); // [악재] 고급 음료 외면
        cultPopularity.AddTarget("MUSE", 1.1f); // [호재] 라떼 마시는 챌린지 BGM 제작
        scenarioDatabase.Add(cultPopularity);

        var bodyModification = new ScenarioEvent("E-스포츠, 현실 스포츠와 통합! 선수들 신체 개조 허용!", true, false, 3, 5);
        bodyModification.AddTarget("CRCK", 1.25f); // [호재] 리그 흥행
        bodyModification.AddTarget("NEO", 1.15f); // [호재] 선수 개조
        bodyModification.AddTarget("AURA", 1.1f); // [호재] 중계권
        bodyModification.AddTarget("SHLD", 0.95f); // [악재] 폭력성 논란
        bodyModification.AddTarget("WINE", 1.1f); // [호재] 스포츠 리그 공식 후원사 선정
        scenarioDatabase.Add(bodyModification);

        var greatMusic = new ScenarioEvent("AI 작곡가, 역사상 가장 위대한 음악 탄생!", true, false, 2, 3);
        greatMusic.AddTarget("CSMC", 1.1f); // [호재] AI 기술
        greatMusic.AddTarget("Vlive", 1.05f); // [호재] 음원 유통
        greatMusic.AddTarget("ARCD", 0.95f); // [악재] 오프라인 공연 외면
        greatMusic.AddTarget("MUSE", 1.5f); // [초급등] 해당 AI 작곡가를 보유한 뮤즈 레코드 주가 폭발
        scenarioDatabase.Add(greatMusic);
        
        var flyingCar = new ScenarioEvent("플라잉카, 부유층의 상징으로 등극!", true, false, 3, 5);
        flyingCar.AddTarget("SKGL", 1.25f); // [호재] 고급화 전략
        flyingCar.AddTarget("SAIL", 1.1f); // [호재] 요트 연계
        flyingCar.AddTarget("PRIO", 0.9f); // [악재] 대중차 외면
        flyingCar.AddTarget("VELO", 0.95f); // [악재] 부자들은 하늘로, 서민은 땅속으로 (이미지 고착화)
        scenarioDatabase.Add(flyingCar);

        var minimalLife = new ScenarioEvent("초저가 생활비로 '미니멀 라이프' 전 우주 확산!", false, false, 2, 4);
        minimalLife.AddTarget("TIMT", 1.15f); // [호재] 저가 식량 수요
        minimalLife.AddTarget("AMBR", 0.8f); // [악재] 사치품 기피
        minimalLife.AddTarget("ORGN", 0.95f); // [악재] 자동차 외면
        minimalLife.AddTarget("GOLD", 1.1f); // [호재] 적은 돈으로 큰 꿈을 꾸는 로또성 복권 인기
        scenarioDatabase.Add(minimalLife);

        var bugCollection = new ScenarioEvent("코즈믹 소프트 OS 1.0 '버그 수집' 붐!", true, false, 2, 3);
        bugCollection.AddTarget("CSMC", 1.05f); // [호재] 마케팅 성공
        bugCollection.AddTarget("FNET", 1.1f); // [호재] 버그 NFT 거래
        bugCollection.AddTarget("PIXEL", 1.05f); // [호재] 수집 게임
        bugCollection.AddTarget("MUSE", 1.05f); // [호재] 버그 발견 시 나오는 희귀 효과음 NFT화
        scenarioDatabase.Add(bugCollection);

        var goldCoin = new ScenarioEvent("퓨처 넷, 금 기반 가상 화폐 '골드 코인' 런칭!", true, false, 4, 6);
        goldCoin.AddTarget("FNET", 1.25f); // [호재] 신규 시장 개척
        goldCoin.AddTarget("BANK", 0.85f); // [악재] 전통 화폐 위협
        goldCoin.AddTarget("ZILS", 1.05f); // [호재] 금 채굴 수요
        goldCoin.AddTarget("GOLD", 1.3f); // [급등] 카지노 칩과 1:1 교환 협약 체결
        scenarioDatabase.Add(goldCoin);

        var safetyFund = new ScenarioEvent("인공 태양 안전 기금 10조 원 조성!", true, false, 3, 5);
        safetyFund.AddTarget("CORE", 1.15f); // [호재] 연구 자금
        safetyFund.AddTarget("MAGM", 0.9f); // [악재] 경쟁 견제
        safetyFund.AddTarget("TITN", 1.05f); // [호재] 관련 장비 납품
        safetyFund.AddTarget("AQUA", 1.1f); // [호재] 안전을 위한 비상 냉각수 시스템 확충
        scenarioDatabase.Add(safetyFund);

        var environmentalBurden = new ScenarioEvent("로봇 임대료에 '환경 부담금' 부과 결정!", false, false, 2, 4);
        environmentalBurden.AddTarget("NEXS", 0.8f); // [악재] 비용 증가
        environmentalBurden.AddTarget("IRON", 1.1f); // [호재] 단순 중장비 선호
        environmentalBurden.AddTarget("GAIA", 0.9f); // [악재] 건설 로봇 비용
        environmentalBurden.AddTarget("ECO", 1.15f); // [호재] 징수된 부담금으로 환경 정화 사업 발주 
        scenarioDatabase.Add(environmentalBurden);

        var personalInformation = new ScenarioEvent("개인 정보 국외 유출 전면 금지 법안 통과!", false, false, 3, 5);
        personalInformation .AddTarget("DATA", 0.8f); // [악재] 데이터 판매 타격
        personalInformation.AddTarget("AEGS", 1.15f); // [호재] 내부 보안 강화
        personalInformation.AddTarget("HEMS", 0.9f); // [악재] 국제 통신 감소
        personalInformation.AddTarget("WEAT", 1.05f); // [호재] 자국 내 기상/환경 데이터의 독점적 가치 상승
        scenarioDatabase.Add(personalInformation);
    }

    #endregion

    #region Core Market Logic

    // 🔄 [수정] UI 자동 갱신 루프 (0.5초 주기)
    IEnumerator UpdatePortfolioLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(0.5f);

        while (true)
        {
            // 🚫 [추가] 게임 오버면 루프 중단
            if (isGameOver) yield break;

            UpdatePortfolioUI();
            UpdatePlayerMoneyUI();

            // 대출 패널 갱신
            if (UIManager.I.HasGroup(UI_GROUP_LOAN))
            {
                UpdateLoanPanelUI();
            }

            // 📜 [신규] 채권 패널이 열려있다면 0.5초마다 갱신 (금리 변동, 턴 경과 실시간 반영)
            if (isBondPanelOpen)
            {
                UpdateBondPanelUI();
            }

            UpdateStockBoardUI();

            // 트레이딩 패널 및 그래프 갱신
            if (selectedStock != null)
            {
                UpdateTradePanelUI();
                
                // 📈 그래프 업데이트 호출
                if (stockGraphUI != null && stockGraphUI.gameObject.activeInHierarchy)
                {
                    // 🟢 수정 (캔들 그래프 함수 호출)
                    stockGraphUI.ShowCandleGraph(selectedStock.candleHistory);
                }
            }

            // 🌑 [신규] 차명 계좌 UI 갱신 (패널이 켜져있을 때만)
            UpdateShadowAccountUI();
            // 💀 [신규] 사채 UI 갱신 (패널이 켜져있을 때만)
            UpdatePrivateLoanUI();
            yield return wait;
        }
    }

    // 💰 [핵심 수정] 턴마다 호가창 기반으로 가격 결정 및 매매 체결
    IEnumerator UpdateMarketPrices()
    {
        while (true)
        {
            // 🚫 [추가] 게임 오버면 루프 중단
            if (isGameOver) yield break;
            yield return new WaitForSeconds(updateInterval);

            // ✅ [수정] 매 턴 시작 시 정보원 사용 기록 초기화 (다시 사용 가능하게)
            hasPlayerUsedInfo = false;

            // 1. UI 상태 초기화 (AgentList 활성화, BtnList 비활성화)
            if (UIManager.I.HasGroup(UI_GROUP_INFOTRADE))
            {
                UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_AgentList", true);
                UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_BtnList", false);
            }

            // 2. 텍스트 및 이미지 초기화 (안내원)
            // 패널이 열려있든 닫혀있든, 다음 번 열 때를 위해 초기화해둡니다.
            UpdateDefaultInfoText();

            ProcessBrokerNoBuyPenalty();
            UpdateBaseInterestRate();
            
            // 배당금 지급
            currentDividendTurn++;
            if (currentDividendTurn >= dividendInterval)
            {
                DistributeDividends();
                currentDividendTurn = 0;
                // 배당 뉴스는 별도 함수 대신 뉴스 티커 갱신 시 포함하거나 팝업으로 띄우는 게 좋음
                // 여기서는 로그만 남김
                Debug.Log("[결산] 배당금 지급 완료");
            }

            ApplyShortInterest();
            if (aiManager != null) aiManager.ProcessAILoans();

            // 🌟 [신규] 이벤트 만료 처리 (먼저 만료된 것 제거)
            for (int i = activeEvents.Count - 1; i >= 0; i--)
            {
                if (activeEvents[i].remainingTurns <= 0)
                {
                    activeEvents.RemoveAt(i);
                }
            }

            // 🌟 [신규] 이벤트 생성 (가중치 기반)
            GenerateNextEvent();

            // 🌟 [신규] 통합 뉴스 티커 업데이트 (모든 활성 이벤트 표시)
            UpdateAllNewsTicker();

            // -----------------------------------------------------------
            // 1. 이벤트 로직 적용 (상장, 파산, 호가 압력)
            // -----------------------------------------------------------
            
            // Struct는 값 타입이므로 수정을 위해 for문 사용
            for (int k = 0; k < activeEvents.Count; k++)
            {
                var evt = activeEvents[k];

                // A. 신규 상장 (1회만 실행)
                if (evt.isListing && !evt.isProcessed)
                {
                    bool isAlreadyListed = marketStocks.Any(s => s.data == evt.singleTarget.data);
                    if (!isAlreadyListed)
                    {
                        marketStocks.Add(evt.singleTarget);
                        Debug.Log($"🔔 [상장] {evt.singleTarget.data.stockName} 상장 완료.");
                    }
                    evt.isProcessed = true; // 처리 완료 마킹
                }

                // B. 파산 (1회만 실행 - 가격 0원 만들고 락)
                if (evt.isBankruptcy && !evt.isProcessed)
                {
                    if (marketStocks.Contains(evt.singleTarget))
                    {
                        evt.singleTarget.currentPrice = 0;
                        evt.singleTarget.isLocked = true;
                        Debug.Log($"💀 [파산] {evt.singleTarget.data.stockName} 거래 정지.");
                    }
                    evt.isProcessed = true;
                }

                // 변경된 상태(isProcessed) 저장
                activeEvents[k] = evt;
            }

            // 💀 [수정] 사채 데드라인 체크 로직 변경
            if (player.privateDebt > 0)
            {
                player.privateDebtDeadline--;
                
                // 데드라인 초과 시 즉시 파산
                if (player.privateDebtDeadline <= 0)
                {
                    // 🛠️ [수정] 기존 StopAllCoroutines() 대신 통합 함수 호출
                    TriggerGameOver("사채 상환 기한을 넘겼습니다.\n어둠의 형님들이 찾아왔습니다...");
                    yield break; // 이 루프는 여기서 종료
                }
            }

            // -----------------------------------------------------------
            // 2. 주식별 가격 변동 처리
            // -----------------------------------------------------------
            for (int i = marketStocks.Count - 1; i >= 0; i--)
            {
                RuntimeStock stock = marketStocks[i];
                stock.previousPrice = stock.currentPrice;

                // 상장 폐지 처리
                if (stock.isDelisting)
                {
                    if (selectedStock == stock) ToggleTradePanel(false);
                    if (!upcomingStocks.Contains(stock.data)) upcomingStocks.Add(stock.data);
                    marketStocks.RemoveAt(i);
                    continue;
                }
                
                // 호가창 체결
                long tradesExecuted = ProcessOrderBook(stock);
                
                // 🌟 [수정] 이벤트 영향 여부 체크
                bool isAffectedByEvent = false;
                bool isFatalShock = false; // 💀 [신규] 치명타 여부 체크

                // 시나리오/파급효과 압력 적용
                for (int k = 0; k < activeEvents.Count; k++)
                {
                    var evt = activeEvents[k];
                    if (evt.isListing || evt.isHidden || evt.newsTitle.Contains("평온")) continue;

                    // 1. 기존 파산 이벤트(CreateBankruptcyEvent) 처리 (단일 타겟)
                    if (evt.isBankruptcy && evt.singleTarget == stock)
                    {
                        stock.currentPrice = 0;
                        stock.isLocked = true;
                        stock.isDelisting = true;
                        isAffectedByEvent = true;
                        continue;
                    }

                    // 2. 타겟 여부 및 배율 확인
                    bool isTarget = false;
                    float targetMultiplier = 1.0f;

                    if (evt.singleTarget == stock)
                    {
                        isTarget = true;
                        targetMultiplier = evt.singleMultiplier;
                    }
                    else if (evt.scenarioTargets != null && evt.scenarioTargets.ContainsKey(stock))
                    {
                        isTarget = true;
                        targetMultiplier = evt.scenarioTargets[stock];
                    }
                    else if (evt.isRippleEvent && evt.singleTarget != null && evt.singleTarget.data.sector == stock.data.sector)
                    {
                        // 파급 효과는 여기서 계산
                        isTarget = true;
                        targetMultiplier = 1.0f + (evt.singleMultiplier - 1.0f) * 0.4f;
                    }

                    if (isTarget)
                    {
                        isAffectedByEvent = true;

                        // 💀 [핵심 수정] 강제 파산 시나리오여도, 배율이 0에 가까운 '진짜 타겟'만 죽임
                        // 배율이 정상적이라면(예: 반사이익 기업), 그냥 주가 변동만 적용
                        bool isRealVictim = evt.bankruptcyCountdown > 0 && targetMultiplier <= 0.05f;

                        if (isRealVictim)
                        {
                            // 📉 [희생양] 매 턴 -33% 확정 폭락 (하한가 무시)
                            stock.currentPrice = (int)(stock.currentPrice * 0.67f); 
                            
                            // 마지막 1턴 남았을 때 사망 선고
                            if (evt.remainingTurns <= 1)
                            {
                                stock.currentPrice = 0;
                                stock.isLocked = true;
                                stock.isDelisting = true;
                                Debug.Log($"💀 [시나리오 파산] {stock.data.stockName} 거래 정지 및 상장 폐지 확정.");
                            }
                        }
                        else
                        {
                            // 📈 [주변 기업] 일반적인 시나리오 압력 적용 (가격 제한폭 적용됨)
                            // 파산 시나리오의 Fatal 플래그가 켜져 있어도, 희생양이 아니면 Fatal을 끕니다.
                            bool applyFatal = evt.isFatal && isRealVictim; 
                            if (applyFatal) isFatalShock = true; 

                            float fadeFactor = (float)evt.remainingTurns / evt.maxTurns;
                            ApplyScenarioOrderPressure(stock, evt, fadeFactor);
                        }
                    }
                }

                // 🌊 [신규] 어떤 이벤트 영향도 받지 않는다면, 자연 변동성(1.5배) 적용
                if (!isAffectedByEvent)
                {
                    ApplyNaturalVolatility(stock);
                }
                // 💀 파산 희생양인 경우에만 Clamp 무시
                // (위 로직에서 isRealVictim일 때만 가격을 직접 깎으므로 여기선 조건만 체크)
                bool isForcedCrash = activeEvents.Any(e => e.bankruptcyCountdown > 0 && 
                                     ((e.singleTarget == stock && e.singleMultiplier <= 0.05f) || 
                                      (e.scenarioTargets != null && e.scenarioTargets.ContainsKey(stock) && e.scenarioTargets[stock] <= 0.05f)));
                if (!isForcedCrash)
                {
                    ClampStockPrice(stock, isFatalShock);
                }
                
                stock.FinalizeCandle();
                
                // 캔들 완성
                stock.FinalizeCandle();

                // 상장 폐지 조건 체크
                int delistThreshold = (int)(stock.data.startPrice * 0.1f);
                if (stock.currentPrice <= delistThreshold && !stock.isDelisting && !stock.isLocked)
                {
                    stock.currentPrice = Mathf.Max(1, stock.currentPrice);
                    stock.isDelisting = true;
                }
            }

            // 🚫 [추가] 파산 체크 (매 턴마다 자산 확인)
            CheckPlayerBankruptcy(); // 함수 내부 내용도 수정해야 함 (아래 참조 4번)

            // -----------------------------------------------------------
            // 3. 턴 종료 처리 (남은 시간 차감)
            // -----------------------------------------------------------
            for (int i = 0; i < activeEvents.Count; i++)
            {
                var evt = activeEvents[i];
                evt.remainingTurns--;
                activeEvents[i] = evt;
            }

            CheckPlayerBankruptcy();
            CheckMarginCall();
            ProcessBrokerContract();

            UpdateStockBoardUI();
            if (selectedStock != null) 
            {
                UpdateTradePanelUI();
                if (stockGraphUI != null && stockGraphUI.gameObject.activeInHierarchy)
                {
                    // 🕯️ [수정] 캔들 히스토리 전달
                    stockGraphUI.ShowCandleGraph(selectedStock.candleHistory);
                }
            }
        }
    }

    // 🌟 [수정] 뉴스 티커 업데이트
    void UpdateAllNewsTicker()
    {
        if (activeEvents.Count == 0)
        {
            UpdateNewsUI("시장은 현재 안정적인 흐름을 보이고 있습니다.", Color.white);
            return;
        }

        System.Text.StringBuilder newsBuilder = new System.Text.StringBuilder();
        
        for (int i = 0; i < activeEvents.Count; i++)
        {
            var evt = activeEvents[i];
            
            // 🌟 반드시 헬퍼 함수를 통해 제목을 가져와야 내부자와 일치함
            string displayTitle = GetPublicNewsTitle(evt);
            string colorHex = "white";

            if (evt.isHidden) colorHex = "green"; 
            else if (evt.isBankruptcy) colorHex = "red"; 
            else if (evt.isListing) colorHex = "yellow"; 
            else if (evt.isMegaEvent) colorHex = "#FF4500"; 
            else if (evt.isRippleEvent) colorHex = "#87CEEB"; 
            else colorHex = "white"; 

            newsBuilder.Append($"<color={colorHex}>{displayTitle}</color>");
            
            if (i < activeEvents.Count - 1)
            {
                newsBuilder.Append("   \n   "); // 구분자
            }
        }

        UpdateNewsUI(newsBuilder.ToString(), Color.white);
    }

    // 🕵️‍♂️ [수정] 플레이어에게 팔 정보를 가진 이벤트를 '랜덤'으로 찾기
    private PendingEvent? GetTargetEventForInfo()
    {
        // 1. 유효한(팔 수 있는) 이벤트 후보군을 모두 수집
        List<PendingEvent> validCandidates = new List<PendingEvent>();

        foreach (var evt in activeEvents)
        {
            // 초대형(이미 공개됨), 상장/파산(공시됨), 해킹(정보 없음)은 제외
            if (!evt.isMegaEvent && !evt.isListing && !evt.isBankruptcy && !evt.isHidden)
            {
                validCandidates.Add(evt);
            }
        }

        // 2. 후보가 없다면 null
        if (validCandidates.Count == 0) return null;

        // 3. 후보 중에서 무작위 하나 선택
        return validCandidates[UnityEngine.Random.Range(0, validCandidates.Count)];
    }

    // 💰 [핵심] 호가창 매칭 및 가격 결정 함수 (중간가 체결 적용)
    long ProcessOrderBook(RuntimeStock stock)
    {
        if (stock.isLocked) return 0;

        stock.SortOrderBooks();
        int executedTrades = 0;

        // 최우선 매수 호가와 최우선 매도 호가가 만나는지 확인
        while (stock.BuyOrders.Count > 0 && stock.SellOrders.Count > 0)
        {
            Order bestBuy = stock.BuyOrders[0];
            Order bestSell = stock.SellOrders[0];

            // 매수 가격 >= 매도 가격일 경우 체결 가능
            if (bestBuy.price >= bestSell.price)
            {
                // 🛠️ [수정] 체결 가격을 매수/매도 호가의 '중간값'으로 결정하여 공정성 확보
                // 예: 매수 1100원, 매도 1000원 -> 체결 1050원
                long tradePrice = (bestBuy.price + bestSell.price) / 2;
                
                long tradeAmount = (long)Mathf.Min(bestBuy.amount, bestSell.amount);

                // 1. 거래 체결 및 가격 갱신
                stock.previousPrice = stock.currentPrice;
                stock.currentPrice = (int)tradePrice;
                executedTrades++;

                // 2. 주문량 업데이트
                bestBuy.amount -= tradeAmount;
                bestSell.amount -= tradeAmount;

                // 3. 주문자에게 거래 결과 반영
                ExecuteTrade(stock, tradeAmount, tradePrice, bestBuy, bestSell);

                // 4. 체결된 주문 제거
                if (bestBuy.amount <= 0) stock.BuyOrders.RemoveAt(0);
                if (bestSell.amount <= 0) stock.SellOrders.RemoveAt(0);
                
                stock.SortOrderBooks(); 
            }
            else
            {
                break;
            }
        }
        
        // 유동성 공급 로직 (마이너스 호가 방지 적용)
        if (stock.BuyOrders.Count == 0 && stock.SellOrders.Count == 0)
        {
             stock.InitializeOrderBook();
        }
        else if (stock.BuyOrders.Count < 3)
        {
             // 🛡️ 1원 미만 주문 방지
             long newPrice = Math.Max(1, stock.currentPrice - 10);
             stock.BuyOrders.Add(new Order(newPrice, stock.data.totalShares / 100));
        }

        return executedTrades;
    }

    // 💰 [핵심] 실제 거래 실행 (플레이어/AI 간의 돈/주식 이동)
    void ExecuteTrade(RuntimeStock stock, long amount, long price, Order buyOrder, Order sellOrder)
    {
        // 1. 매수자 (buyOrder) 처리: 돈 차감 및 주식 증가
        if (buyOrder.isPlayer) 
        {
            player.money -= price * amount;
            player.AddStock(stock.data, amount);
            player.SetLastAction($"<b>[{stock.data.stockName}]</b> 매수 체결 ({amount:N0}주 @{price:N0}원)");
        }
        else if (buyOrder.ai != null) 
        {
            buyOrder.ai.money -= price * amount;
            // AI의 포트폴리오 업데이트 로직은 별도로 관리
            // (AI의 주문은 이미 AI 매매 루프에서 포트폴리오에 임시로 반영되었거나, 여기서 최종 반영되어야 함)
            // 여기서는 단순화하여 돈만 차감하고, AI 매매 함수에서 최종 포트폴리오 관리가 이루어졌다고 가정합니다.
        }

        // 2. 매도자 (sellOrder) 처리: 돈 증가 및 주식 감소
        if (sellOrder.isPlayer) 
        {
            player.money += price * amount;
            player.RemoveStock(stock.data, amount);
            player.SetLastAction($"<b>[{stock.data.stockName}]</b> 매도 체결 ({amount:N0}주 @{price:N0}원)");
        }
        else if (sellOrder.ai != null)
        {
            sellOrder.ai.money += price * amount;
        }

        // 3. 시장 잔여 물량 변동 (주문이 시장 물량 확보/방출 개념이었다면, 여기서는 체결 후 시장 물량은 변동 없음)
        // 공매도/숏커버 로직은 주문자 측에서 별도 처리되므로 여기선 단순화합니다.

        Debug.Log($"✅ [체결] {stock.data.stockName} {amount:N0}주 @{price:N0}원 체결!");

        UpdatePlayerMoneyUI();
        UpdatePortfolioUI();
        // UI 갱신은 UpdatePortfolioLoop에서 주기적으로 수행
    }

    // 💣 [수정] ApplyScenarioOrderPressure: 감쇠 계수(fadeFactor) 적용
    void ApplyScenarioOrderPressure(RuntimeStock stock, PendingEvent evt, float fadeFactor)
    {
        if (stock.isLocked) return;

        // 1. 이벤트 대상 확인
        float targetMultiplier = 1.0f;
        if (evt.scenarioTargets != null && evt.scenarioTargets.ContainsKey(stock))
        {
             targetMultiplier = evt.scenarioTargets[stock];
        }
        else if (evt.singleTarget == stock)
        {
            targetMultiplier = evt.singleMultiplier;
        }
        else return;

        bool isBuyPressure = targetMultiplier >= 1.0f;
    
        // 🌟 [수정] 감쇠가 너무 급격하지 않도록 보정 (최소 50% 강도는 유지)
        // 예: 5턴 -> 1.0, 0.9, 0.8... 이 아니라 1.0 -> 0.8 -> 0.6 이런 식으로
        // 혹은 단순히 fadeFactor에 0.5를 더하고 클램핑
        float adjustedFade = Mathf.Clamp(fadeFactor + 0.2f, 0.2f, 1.0f);
        float pressureStrength = Mathf.Abs(targetMultiplier - 1.0f) * 2.0f * adjustedFade;
        
        // 🏭 [신규] 기업 규모에 따른 변동성 적용 (거래량 조절)
        // 대기업(Large): 변동성 0.25배 -> 이벤트 충격을 4분의 1로 줄임 (무거움)
        // 중소기업(SME): 변동성 0.5배 -> 이벤트 충격을 절반으로 줄임
        float sizeVolatility = (stock.data.companySize == CompanySize.Large) ? 0.25f : 0.5f;
        
        // 압력 강도에 변동성 계수 곱함
        pressureStrength *= sizeVolatility;

        // 최소한의 거래량은 유지 (추세 지속을 위해)
        if (pressureStrength < 0.05f) pressureStrength = 0.05f;

        long tradeVol = (long)(stock.data.totalShares * UnityEngine.Random.Range(0.01f, 0.1f) * pressureStrength);
        if (tradeVol < 1) tradeVol = 100;

        if (isBuyPressure)
        {
            // ... (기존 매수 압력 로직 유지) ...
            while (tradeVol > 0 && stock.SellOrders.Count > 0)
            {
                Order bestAsk = stock.SellOrders[0];
                long eaten = (long)Mathf.Min(tradeVol, bestAsk.amount);
                bestAsk.amount -= eaten;
                tradeVol -= eaten;
                stock.currentPrice = (int)bestAsk.price; 
                if (bestAsk.amount <= 0) stock.SellOrders.RemoveAt(0);
            }
            if (stock.SellOrders.Count == 0)
            {
                stock.currentPrice = (int)(stock.currentPrice * 1.05f);
                stock.InitializeOrderBook();
            }
        }
        else
        {
            // ... (기존 매도 압력 로직 유지) ...
            while (tradeVol > 0 && stock.BuyOrders.Count > 0)
            {
                Order bestBid = stock.BuyOrders[0];
                long eaten = (long)Mathf.Min(tradeVol, bestBid.amount);
                bestBid.amount -= eaten;
                tradeVol -= eaten;
                stock.currentPrice = (int)bestBid.price; 
                if (bestBid.amount <= 0) stock.BuyOrders.RemoveAt(0);
            }
            if (stock.BuyOrders.Count == 0)
            {
                stock.currentPrice = (int)(stock.currentPrice * 0.95f);
                stock.InitializeOrderBook();
            }
        }
        
        stock.previousPrice = stock.currentPrice; 
        stock.SortOrderBooks();
    }

    // 🌊 [신규] 자연 변동성 적용 (이벤트가 없을 때 1.5배 증폭된 랜덤 등락)
    void ApplyNaturalVolatility(RuntimeStock stock)
    {
        if (stock.isLocked) return;

        // 1. 변동폭 계산 (기본 변동성 * 1.5배)
        float amplifiedVolatility = stock.data.volatility * 1.5f;
        
        // 2. 방향 결정 (-변동폭 ~ +변동폭 사이 랜덤)
        // 예: 변동성 5% -> -7.5% ~ +7.5% 사이의 랜덤 목표 설정
        float randomFluctuation = UnityEngine.Random.Range(-amplifiedVolatility, amplifiedVolatility);
        float targetMultiplier = 1.0f + randomFluctuation;

        bool isBuyPressure = targetMultiplier >= 1.0f;
        float pressureStrength = Mathf.Abs(randomFluctuation) * 1.5f; // 강도 조정

        // 최소 거래량 보정
        if (pressureStrength < 0.02f) pressureStrength = 0.02f;

        // 3. 거래량 결정 (자연스러운 움직임을 위해 적은 물량으로 여러 번 체결 시도)
        long tradeVol = (long)(stock.data.totalShares * UnityEngine.Random.Range(0.005f, 0.05f) * pressureStrength);
        if (tradeVol < 10) tradeVol = 10;

        // 4. 호가창 밀어내기 (ApplyScenarioOrderPressure와 유사하지만 더 가볍게 동작)
        if (isBuyPressure)
        {
            while (tradeVol > 0 && stock.SellOrders.Count > 0)
            {
                Order bestAsk = stock.SellOrders[0];
                long eaten = (long)Mathf.Min(tradeVol, bestAsk.amount);
                bestAsk.amount -= eaten;
                tradeVol -= eaten;
                stock.currentPrice = (int)bestAsk.price; 
                if (bestAsk.amount <= 0) stock.SellOrders.RemoveAt(0);
            }
            // 매도 물량이 다 털리면 가격 상승 및 호가 리필
            if (stock.SellOrders.Count == 0)
            {
                stock.currentPrice = (int)(stock.currentPrice * 1.02f); // 2% 상승
                stock.InitializeOrderBook();
            }
        }
        else // 매도 압력
        {
            while (tradeVol > 0 && stock.BuyOrders.Count > 0)
            {
                Order bestBid = stock.BuyOrders[0];
                long eaten = (long)Mathf.Min(tradeVol, bestBid.amount);
                bestBid.amount -= eaten;
                tradeVol -= eaten;
                stock.currentPrice = (int)bestBid.price; 
                if (bestBid.amount <= 0) stock.BuyOrders.RemoveAt(0);
            }
            // 매수 물량이 다 털리면 가격 하락 및 호가 리필
            if (stock.BuyOrders.Count == 0)
            {
                stock.currentPrice = (int)(stock.currentPrice * 0.98f); // 2% 하락
                stock.InitializeOrderBook();
            }
        }
        
        stock.SortOrderBooks();
    }

    void CheckPlayerBankruptcy()
    {
        if (isGameOver) return;

        // 순자산 계산
        long totalAsset = player.GetTotalAsset(); 
        
        // 👇 파산 조건: 순자산이 설정된 기준(0원) 이하일 때
        if (totalAsset <= playerBankruptcyThreshold) 
        {
            // 🛠️ [수정] 통합 게임 오버 함수 호출
            TriggerGameOver($"파산했습니다.\n(최종 자산: {totalAsset:N0}원)");
        }
    }

    // 🚨 [수정] 마진콜 체크 (유지 증거금 기준)
    void CheckMarginCall()
    {
        var shorts = player.GetShortPositions();
        long totalCurrentShortValue = 0; // 현재 갚아야 할 총 주식 가치

        foreach (var item in shorts)
        {
            RuntimeStock stock = marketStocks.Find(s => s.data == item.Key);
            if (stock != null) totalCurrentShortValue += (long)stock.currentPrice * item.Value;
        }

        if (totalCurrentShortValue == 0) return;

        // 유지 증거금 필요액 = 현재 부채 가치 * 110%
        long requiredMaintenance = (long)(totalCurrentShortValue * player.maintenanceMarginRatio);

        // 현재 묶여있는 증거금 < 필요 유지 증거금 이면 마진콜
        if (player.lockedMargin < requiredMaintenance)
        {
            Debug.LogWarning($"🚨 [마진콜] 증거금 부족! (현재: {player.lockedMargin:N0} < 필요: {requiredMaintenance:N0})");
            UpdateNewsUI($"<color=red><b>[마진콜]</b></color> 증거금 부족으로 강제 청산이 진행됩니다.", Color.red);

            // 1. 부족한 만큼 현금 확보 (보유 주식 매도)
            // 목표: (필요 유지금 - 현재 증거금) 만큼 채워넣어야 함? 아니오, 강제 청산 당합니다.
            // 여기서는 갚아야 할 빚 전체를 갚기 위해 노력합니다.
            
            ForceLiquidateForCash(totalCurrentShortValue); // 일단 롱 포지션 정리해서 현금 확보

            // 2. 강제 숏커버 진행
            foreach (var item in new Dictionary<StockData, long>(shorts))
            {
                RuntimeStock stock = marketStocks.Find(s => s.data == item.Key);
                if (stock != null)
                {
                    long qty = item.Value;
                    long cost = (long)stock.currentPrice * qty;
                    
                    // 증거금 해제
                    long entryPrice = player.GetAvgShortPrice(item.Key);
                    long release = (long)(entryPrice * qty * player.initialMarginRatio);
                    
                    player.lockedMargin -= release;
                    player.money += release; // 증거금을 현금으로 전환하여 빚 갚는 데 사용

                    player.money -= cost; // 상환 비용 차감
                    
                    stock.remainShares += qty;
                    player.RemoveShort(item.Key, qty);
                }
            }
            UpdateTradePanelUI();
            UpdatePlayerMoneyUI();
            UpdatePortfolioUI();
        }
    }

    // 💀 [수정] isFatal 매개변수 추가 (기본값 false)
    void ClampStockPrice(RuntimeStock stock, bool isFatal = false)
    {
        // 🔒 이미 거래 정지(파산)된 주식은 가격 조정 안 함
        if (stock.isLocked || stock.currentPrice <= 0) return;

        int basePrice = stock.previousPrice > 0 ? stock.previousPrice : stock.data.startPrice;
        int limitAmount = (int)(basePrice * priceLimitPercent); // 예: 30% 제한
        
        int upperLimit = basePrice + limitAmount;
        
        // 💀 [핵심] 치명적 이벤트라면 하한가를 1원으로 설정 (제한 없음), 아니면 정상 제한 적용
        int lowerLimit = isFatal ? 1 : (basePrice - limitAmount);

        int absoluteMax = (int)(stock.data.startPrice * maxPriceCapMultiplier);
        upperLimit = Mathf.Min(upperLimit, absoluteMax);

        // 0 이하로 내려가지 않도록 절대 하한선 1 보장
        lowerLimit = Mathf.Max(lowerLimit, 1); 
        stock.currentPrice = Mathf.Clamp(stock.currentPrice, lowerLimit, upperLimit);
    }

    void ApplyShortInterest()
    {
        var shorts = player.GetShortPositions();
        long totalShortValue = 0;

        foreach (var item in shorts)
        {
            RuntimeStock stock = marketStocks.Find(s => s.data == item.Key);
            if (stock != null) totalShortValue += (long)stock.currentPrice * item.Value;
        }

        if (totalShortValue > 0)
        {
            long interest = (long)(totalShortValue * shortInterestRate);
            if (interest > 0)
            {
                if (player.money < interest)
                {
                    ForceLiquidateForCash(interest);
                }

                player.money -= interest;
            }
        }
        UpdatePlayerMoneyUI();
    }

    void DistributeDividends()
    {
        var holdings = player.GetHoldings();
        long totalDiv = 0;
        foreach (var item in holdings)
        {
            if (item.Key.dividendPerShare > 0)
                totalDiv += (long)item.Key.dividendPerShare * item.Value;
        }

        if (totalDiv > 0) 
        {
            player.money += totalDiv;
            
            // 💸 [FloatingText] 배당금 수령 연출
            if (FloatingTextManager.I != null)
            {
                // 배당은 자동 이벤트라 마우스 위치가 애매하므로, 화면 중앙이나 플레이어 돈 UI 근처에 띄움
                Vector3 screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.7f, 0);
                // FloatingTextManager.I.ShowMoneyPopup(screenCenter, totalDiv);
                
                // 혹은 텍스트로 "배당금"이라고 명시하고 싶다면:
                FloatingTextManager.I.ShowText(screenCenter, $"배당금\n+{NumberUtils.ToCurrencyString(totalDiv)}", Color.red);
            }
        }

        if (aiManager != null) aiManager.DistributeAIDividends();
    }

    // 💸 [수정] 현금이 부족할 때 보유 주식을 무작위로 강제 매도하여 자금을 마련하는 함수
    void ForceLiquidateForCash(long amountNeeded)
    {
        long deficit = amountNeeded - player.money;
        if (deficit <= 0) return;

        Debug.LogWarning($"🚨 [자금 부족] 필요 금액: {amountNeeded:N0}원 / 부족분: {deficit:N0}원 -> 보유 주식 강제 매각 실행");

        var holdings = player.GetHoldings();
        List<StockData> myStockKeys = new List<StockData>(holdings.Keys);
        
        // 현금이 확보되거나, 팔 주식이 없을 때까지 반복
        while (deficit > 0 && myStockKeys.Count > 0)
        {
            // 1. 무작위 주식 선정
            int randomIndex = UnityEngine.Random.Range(0, myStockKeys.Count);
            StockData targetData = myStockKeys[randomIndex];
            RuntimeStock marketStock = marketStocks.Find(s => s.data == targetData);

            if (marketStock == null || marketStock.currentPrice <= 0) 
            {
                myStockKeys.RemoveAt(randomIndex);
                continue;
            }

            long currentQty = holdings[targetData];
            long sellQty = currentQty; // 전량 매도 시도

            // 2. 강제 매도 주문 생성 (매수 호가에 때려 박아 즉시 체결 유도)
            if (marketStock.BuyOrders.Count == 0)
            {
                // 살 사람이 없다면 1주도 못 팜. 다음 주식으로 넘어감.
                myStockKeys.RemoveAt(randomIndex);
                continue;
            }

            Order bestBuyOrder = marketStock.BuyOrders[0];
            long availableToBuy = bestBuyOrder.amount;
            
            long tradeAmount = (long)Mathf.Min(sellQty, availableToBuy);
            long tradePrice = bestBuyOrder.price;

            // 3. 거래 체결 (강제 청산이므로 즉시 체결된 것으로 간주)
            long income = tradePrice * tradeAmount;
            
            // 4. 플레이어 포트폴리오 및 잔고 업데이트
            player.money += income;
            player.RemoveStock(targetData, tradeAmount);
            
            // 5. 호가창 및 주문 업데이트
            bestBuyOrder.amount -= tradeAmount;
            if (bestBuyOrder.amount <= 0) marketStock.BuyOrders.RemoveAt(0);
            marketStock.SortOrderBooks(); // 호가창 정리

            Debug.Log($"📉 [강제 매도] {targetData.stockName} {tradeAmount}주 처분 @{tradePrice:N0}원 -> {income:N0}원 확보");

            // 6. 상태 갱신
            deficit = amountNeeded - player.money;
            holdings = player.GetHoldings();
            if (!holdings.ContainsKey(targetData)) myStockKeys.Remove(targetData);
        }
        
        UpdatePortfolioUI();
    }

    // 🔄 [수정] 랜덤 이벤트 대상 선정 시 동적 가중치 사용
    RuntimeStock GetWeightedRandomStock()
    {
        if (marketStocks.Count == 0) return null;
        
        // 🌟 [변경] data.eventWeight 대신 dynamicEventWeight 사용
        float totalWeight = marketStocks.Sum(s => s.dynamicEventWeight);
        
        float r = UnityEngine.Random.Range(0f, totalWeight);
        foreach (var s in marketStocks) 
        { 
            r -= s.dynamicEventWeight; 
            if (r <= 0) return s; 
        }
        return marketStocks.Last();
    }

    // 🏦 [수정] UpdateBaseInterestRate: 이자 지급 로직 추가
    void UpdateBaseInterestRate()
    {
        currentRateTurn++;
        
        // 📜 [신규] 턴이 찰 때마다(10턴) 이자 지급
        if (currentRateTurn >= rateUpdateInterval)
        {
            currentRateTurn = 0;
            
            // 1. 플레이어 이자 지급
            if (player.bondHoldings > 0)
            {
                long interest = (long)(player.bondHoldings * baseInterestRate);
                player.money += interest;
                
                // 💸 [FloatingText] 국채 이자 수령 연출
                if (FloatingTextManager.I != null && interest > 0)
                {
                    Vector3 screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.6f, 0); // 배당금보다 살짝 아래
                    FloatingTextManager.I.ShowText(screenCenter, $"국채 이자\n+{NumberUtils.ToCurrencyString(interest)}원", Color.red);
                }

                UpdatePlayerMoneyUI();
                player.SetLastAction($"<b>\n[국채 이자]</b> {interest:N0}원 지급 (금리 {baseInterestRate*100:F1}%)");
            }

            // 2. AI 이자 지급
            if (aiManager != null)
            {
                aiManager.PayAIBondYield(baseInterestRate);
            }

            // 3. 금리 변동 로직 (기존 코드)
            float randomVal = UnityEngine.Random.value;
            float changeAmount = 0f;
            string newsMsg = "";
            Color newsColor = Color.white;

            if (baseInterestRate < 0.02f) changeAmount = (randomVal > 0.3f) ? 0.0025f : 0f; 
            else if (baseInterestRate > 0.10f) changeAmount = (randomVal > 0.3f) ? -0.0025f : 0f;
            else { if (randomVal < 0.33f) changeAmount = 0.0025f; else if (randomVal < 0.66f) changeAmount = -0.0025f; }

            if (changeAmount != 0)
            {
                baseInterestRate += changeAmount;
                baseInterestRate = Mathf.Clamp(baseInterestRate, 0.01f, 0.15f);

                bool isHike = changeAmount > 0;
                if (isHike) { newsMsg = $"[속보] 기준 금리 인상! ({baseInterestRate*100:F2}%) 채권 수익률 상승."; newsColor = new Color(1f, 0.4f, 0.4f); ApplyInterestRateImpact(false); }
                else { newsMsg = $"[속보] 기준 금리 인하! ({baseInterestRate*100:F2}%) 증시 유동성 공급 기대."; newsColor = new Color(0.4f, 1f, 0.4f); ApplyInterestRateImpact(true); }

                UpdateNewsUI(newsMsg, newsColor);
                
                // 섹터 로테이션 및 대출 UI 갱신
                ApplySectorRotation();
                if (UIManager.I.HasGroup(UI_GROUP_LOAN)) UpdateLoanPanelUI();
                if (UIManager.I.HasGroup(UI_GROUP_BOND)) UpdateBondPanelUI(); // 채권 UI 갱신
            }
        }
    }

    // 🔄 [신규] 섹터 순환 로직 (금리에 따른 가중치 조절)
    void ApplySectorRotation()
    {
        // 기준:
        // 저금리 (< 3.5%): 성장주(IT, Bio, Game) 유리 -> 이벤트 발생 확률 증가
        // 고금리 (> 7.0%): 방어주(Food, Energy) 유리 -> 이벤트 발생 확률 증가
        // 중금리: 균형

        float growthMultiplier = 1.0f;
        float defensiveMultiplier = 1.0f;
        float cyclicalMultiplier = 1.0f; // 자동차 등 경기 민감주

        string marketTrend = "";

        if (baseInterestRate <= 0.035f) // 저금리 (유동성 장세)
        {
            growthMultiplier = 1.25f;    // 성장주 이벤트 2배
            defensiveMultiplier = 0.75f; // 방어주 소외
            cyclicalMultiplier = 1.2f;  // 소비 증가로 자동차도 수혜
            marketTrend = "<color=red>공격적 투자(Growth)</color>";
        }
        else if (baseInterestRate >= 0.07f) // 고금리 (긴축 장세)
        {
            growthMultiplier = 0.75f;    // 성장주 위축
            defensiveMultiplier = 1.25f; // 방어주 선호 (이벤트 2배)
            cyclicalMultiplier = 0.8f;  // 할부 금리 인상으로 자동차 악재
            marketTrend = "<color=green>방어적 투자(Defensive)</color>";
        }
        else // 중금리 (실적 장세)
        {
            growthMultiplier = 1.0f;
            defensiveMultiplier = 1.0f;
            cyclicalMultiplier = 1.0f;
            marketTrend = "균형 시장(Balanced)";
        }

        foreach (var stock in marketStocks)
        {
            float finalMult = 1.0f;

            switch (stock.data.sector)
            {
                case StockSector.IT:
                case StockSector.Bio:
                case StockSector.Game:
                    finalMult = growthMultiplier;
                    break;

                case StockSector.Food:
                case StockSector.Energy:
                    finalMult = defensiveMultiplier;
                    break;

                case StockSector.Automotive:
                    finalMult = cyclicalMultiplier;
                    break;
            }

            // 가중치 적용 (기본값 * 시장 상황)
            stock.dynamicEventWeight = stock.data.eventWeight * finalMult;
        }

        Debug.Log($"🔄 [섹터 순환] 금리 {baseInterestRate*100:F1}% -> 시장 트렌드: {marketTrend}");
        
        // (선택) 섹터 순환 알림 메시지 띄우기
        UpdateNewsUI($"[분석] 현재 시장은 {marketTrend} 섹터로 자금이 이동하고 있습니다.", Color.cyan);
    }

    // 📉 금리 변동에 따른 전체 시장 충격
    void ApplyInterestRateImpact(bool isRateCut)
    {
        // 금리 인하 -> 주가 상승 (호재) / 금리 인상 -> 주가 하락 (악재)
        float impactFactor = isRateCut ? 0.03f : -0.03f; // 전체적으로 3% 정도 움직임

        foreach (var stock in marketStocks)
        {
            // 성장주(IT, Bio)는 금리에 더 민감함
            float sensitivity = (stock.data.sector == StockSector.IT || stock.data.sector == StockSector.Bio) ? 1.5f : 1.0f;
            
            // 주가 변동 적용
            float change = stock.currentPrice * impactFactor * sensitivity;
            // 랜덤성 추가
            change *= UnityEngine.Random.Range(0.8f, 1.2f);

            stock.currentPrice += (int)change;
            ClampStockPrice(stock);
        }
    }
    
    // 💳 [신규] 현재 대출 금리 계산 (기준금리 + 가산금리)
    public float GetCurrentLoanRate()
    {
        return baseInterestRate + bankMargin;
    }

    // 💰 [신규] AI가 배당일까지 남은 턴을 확인하는 함수
    public int GetTurnsToNextDividend()
    {
        return dividendInterval - currentDividendTurn;
    }

    #endregion

    #region Event System

    // 🌟 [수정] 가중치 기반 이벤트 생성 함수
    void GenerateNextEvent()
    {
        // 1. 이벤트 중첩 제한 (너무 많으면 생성 스킵)
        // 1초 턴이므로 이벤트가 빨리 쌓일 수 있음. 최대 3개까지만 유지.
        if (activeEvents.Count >= 3) return;

        // 2. 가중치 총합 계산
        float totalWeight = weightScenario + weightRipple + weightListing + weightBankruptcy + weightHacking + weightPeace;
        float randomPoint = UnityEngine.Random.Range(0, totalWeight);

        // 3. 가중치에 따른 이벤트 선택
        float currentSum = 0;

        // A. 시나리오 (Scenario)
        currentSum += weightScenario;
        if (randomPoint < currentSum)
        {
            if (marketStocks.Count > 0)
            {
                CreateScenarioEvent();
                return;
            }
        }

        // B. 파급 효과 (Ripple)
        currentSum += weightRipple;
        if (randomPoint < currentSum)
        {
            if (marketStocks.Count > 0)
            {
                CreateRippleEvent();
                return;
            }
        }

        // C. 기업 상장 (Listing)
        currentSum += weightListing;
        if (randomPoint < currentSum)
        {
            if (upcomingStocks.Count > 0)
            {
                CreateListingEvent();
                return;
            }
        }

        // D. 파산 (Bankruptcy)
        currentSum += weightBankruptcy;
        if (randomPoint < currentSum)
        {
            if (marketStocks.Count > 0)
            {
                CreateBankruptcyEvent();
                return;
            }
        }

        // E. 해킹 (Hacking)
        currentSum += weightHacking;
        if (randomPoint < currentSum)
        {
            CreateHackingEvent();
            return;
        }

        // F. 평화 (Peace) - 아무것도 안 함 (기존 이벤트는 계속 진행됨)
    }

    // --- 이벤트 생성 헬퍼 함수들 ---

    void CreateScenarioEvent()
    {
        ScenarioEvent scenario = scenarioDatabase[UnityEngine.Random.Range(0, scenarioDatabase.Count)];
        Dictionary<RuntimeStock, float> activeTargets = new Dictionary<RuntimeStock, float>();
        
        foreach (var stock in marketStocks)
        {
            if (scenario.targets.ContainsKey(stock.data.symbol))
                activeTargets.Add(stock, scenario.targets[stock.data.symbol]);
        }

        if (activeTargets.Count > 0)
        {
            int duration = UnityEngine.Random.Range(scenario.minDuration, scenario.maxDuration + 1); // 지속시간 수정 반영
            
            // 만약 강제 파산 시나리오라면, 지속시간을 3턴으로 고정 (33%씩 3번 = 99% + 사망)
            if (scenario.forceBankruptcy) duration = 3;

            var newEvent = new PendingEvent { 
                scenarioTargets = activeTargets, 
                newsTitle = $"[속보] {scenario.title}", 
                isGoodNews = scenario.isGoodNews, 
                isMegaEvent = scenario.isMegaEvent,
                isFatal = scenario.isFatal, // 💀 [신규] 값 전달
                // 💀 [신규] 강제 파산 설정 (3턴 카운트다운 시작)
                bankruptcyCountdown = scenario.forceBankruptcy ? 3 : -1,
                remainingTurns = duration, 
                maxTurns = duration,
                isProcessed = false
            };
            activeEvents.Add(newEvent);
            
            if (scenario.isMegaEvent) hasPlayerUsedInfo = true; // 메가는 정보 즉시 공개
        }
    }

    void CreateRippleEvent()
    {
        RuntimeStock target = GetWeightedRandomStock();
        if (target == null) return;

        bool isGood = UnityEngine.Random.value > 0.5f;
        float multiplier = isGood ? UnityEngine.Random.Range(1.1f, 1.25f) : UnityEngine.Random.Range(0.8f, 0.9f);
        int duration = UnityEngine.Random.Range(10, 20); // 10~20초 지속

        // 🌟 [수정] 텍스트에 호재/악재 여부 명시
        string typeStr = isGood ? "호재" : "악재";

        var newEvent = new PendingEvent
        {
            singleTarget = target,
            singleMultiplier = multiplier,
            // 🌟 [수정] 제목 변경: "이슈 파급" -> "호재/악재 파급"
            newsTitle = $"[동향] {target.data.stockName} 관련 업계 {typeStr} 파급",
            isGoodNews = isGood,
            isRippleEvent = true,
            remainingTurns = duration,
            maxTurns = duration,
            isProcessed = false
        };
        activeEvents.Add(newEvent);
    }

    void CreateListingEvent()
    {
        int idx = UnityEngine.Random.Range(0, upcomingStocks.Count);
        StockData newData = upcomingStocks[idx];
        RuntimeStock newStock = new RuntimeStock(newData); // 주식 객체 미리 생성
        string realTitle = $"[공시] {newData.stockName}, 신규 상장 결정!";
        
        // 상장 데이터 목록에서 제거 (중복 방지)
        upcomingStocks.RemoveAt(idx);

        activeEvents.Add(new PendingEvent { 
            singleTarget = newStock, 
            newsTitle = realTitle, 
            isListing = true,
            remainingTurns = 5, // 뉴스는 5초간 떠있음
            maxTurns = 5,
            isProcessed = false // 실제 추가는 UpdateMarketPrices에서 1회 수행
        });
    }

    void CreateBankruptcyEvent()
    {
        // 🏭 [수정] 대기업은 파산하지 않음. 중소기업(SME)만 필터링
        var smeStocks = marketStocks.Where(s => s.data.companySize == CompanySize.SME).ToList();

        if (smeStocks.Count == 0) return; // 파산할 중소기업이 없으면 리턴

        // 중소기업 중에서 가중치 기반 랜덤 선택
        RuntimeStock target = null;
        float totalWeight = smeStocks.Sum(s => s.dynamicEventWeight);
        float r = UnityEngine.Random.Range(0f, totalWeight);
        
        foreach (var s in smeStocks) 
        { 
            r -= s.dynamicEventWeight; 
            if (r <= 0) 
            {
                target = s;
                break;
            }
        }
        if (target == null) target = smeStocks.Last();

        string realTitle = $"[긴급] {target.data.stockName}, 최종 부도 처리! 거래 정지.";
        
        activeEvents.Add(new PendingEvent { 
            singleTarget = target, 
            newsTitle = realTitle, 
            isBankruptcy = true, 
            remainingTurns = 5, // 뉴스는 5초간 떠있음
            maxTurns = 5,
            isProcessed = false
        });
    }

    void CreateHackingEvent()
    {
        // 해킹은 시스템에 영향을 주지 않고 뉴스만 가리는 용도 (혹은 정보상 마비)
        string[] errorMsgs = { "SYSTEM ERROR: 404", "NETWORK UNSTABLE", "HACKED BY ANONYMOUS" };
        string title = errorMsgs[UnityEngine.Random.Range(0, errorMsgs.Length)];

        activeEvents.Add(new PendingEvent {
            newsTitle = title,
            isHidden = true, // 정보상 이용 불가 등 로직에 활용
            remainingTurns = 8, // 8초간 지속
            maxTurns = 8,
            isProcessed = false
        });
    }

    // 🐞 [수정] 에러가 발생했던 함수 수정
    void GenerateHiddenEvent()
    {
        if (marketStocks.Count > 0)
        {
            RuntimeStock target = GetWeightedRandomStock();
            bool isGood = UnityEngine.Random.value > 0.5f;
            float multiplier = isGood ? UnityEngine.Random.Range(1.5f, 2.0f) : UnityEngine.Random.Range(0.5f, 0.7f);
            
            // 🌟 [수정] currentEvent에 대입하는 대신 activeEvents 리스트에 추가
            var hiddenEvt = new PendingEvent 
            { 
                singleTarget = target, 
                singleMultiplier = multiplier, 
                newsTitle = "???", 
                isGoodNews = isGood, 
                isHidden = true,
                remainingTurns = 3, // 히든 이벤트도 3턴 지속
                maxTurns = 3
            };
            activeEvents.Add(hiddenEvt);
        }
    }

    public PublicEventInfo GetCurrentEventInfo()
    {
        PublicEventInfo info = new PublicEventInfo();
        if (currentEvent.HasValue && !currentEvent.Value.isHidden)
        {
            info.hasEvent = true;
            info.eventTitle = currentEvent.Value.newsTitle;
            info.isGoodNews = currentEvent.Value.isGoodNews;
            info.targets = new Dictionary<RuntimeStock, float>();

            if (currentEvent.Value.scenarioTargets != null) info.targets = currentEvent.Value.scenarioTargets;
            else if (currentEvent.Value.singleTarget != null) info.targets.Add(currentEvent.Value.singleTarget, currentEvent.Value.singleMultiplier);
            else if (currentEvent.Value.isRippleEvent)
            {
                foreach (var stock in marketStocks)
                    if (stock.data.sector == currentEvent.Value.singleTarget.data.sector)
                        info.targets.Add(stock, currentEvent.Value.singleMultiplier * 0.4f);
            }
        }
        else info.hasEvent = false;
        return info;
    }

    string BlindText(string original, PendingEvent evt)
    {
        string processed = original;
        if (evt.singleTarget != null) processed = processed.Replace(evt.singleTarget.data.stockName, "<b>A 기업</b>");
        if (evt.scenarioTargets != null)
        {
            foreach (var stock in evt.scenarioTargets.Keys)
                processed = processed.Replace(stock.data.stockName, "<b>특정 기업</b>");
        }
        return processed;
    }

    // 💻 [수정] 해커 텍스트 오염 함수 (초성 변환 + 글자 깨짐 + 순서 뒤섞기)
    string CorruptText(string original)
    {
        char[] chars = original.ToCharArray();
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        // 한글 초성 유니코드 오프셋
        string[] chosung = { "ㄱ", "ㄲ", "ㄴ", "ㄷ", "ㄸ", "ㄹ", "ㅁ", "ㅂ", "ㅃ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅉ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ" };

        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            
            // 공백이나 특수문자는 90% 확률로 유지
            if (!char.IsLetterOrDigit(c))
            {
                if (UnityEngine.Random.value < 0.90f) sb.Append(c);
                else sb.Append("_"); // 공백도 숨김
                continue;
            }

            float r = UnityEngine.Random.value;

            // 1. [40%] 초성으로 변환 (한글인 경우)
            if (r < 0.4f && c >= 0xAC00 && c <= 0xD7A3)
            {
                int uniVal = c - 0xAC00;
                int choInd = uniVal / (21 * 28);
                sb.Append(chosung[choInd]);
            }
            // 2. [30%] 깨진 문자 (#, *, ?, @)
            else if (r < 0.3f)
            {
                string[] noise = { "#", "*", "?", "@", "$", "%" };
                sb.Append(noise[UnityEngine.Random.Range(0, noise.Length)]);
            }
            // 3. [30%] 원본 유지 (힌트)
            else
            {
                sb.Append(c);
            }
        }

        // 4. [추가] 단어 순서 살짝 뒤섞기 (난이도 극대화)
        // 예: "주가 폭락" -> "폭가 주락" 처럼 보일 수 있게
        if (sb.Length > 5 && UnityEngine.Random.value < 0.25f)
        {
            int swapIdx = UnityEngine.Random.Range(0, sb.Length - 2);
            char temp = sb[swapIdx];
            sb[swapIdx] = sb[swapIdx + 1];
            sb[swapIdx + 1] = temp;
        }

        return sb.ToString();
    }

    string GenerateFakeNews()
    {
        if (scenarioDatabase.Count > 0) return $"{scenarioDatabase[UnityEngine.Random.Range(0, scenarioDatabase.Count)].title}";
        return "외계인의 침공이 임박했습니다!";
    }

    #endregion

    #region Trading System

    // 📉 [수정] 자금 부족 시 마이너스가 되지 않도록 로직 개선
    void OnTrade(int mode)
    {
        if (selectedStock == null || selectedStock.isLocked) 
        {
            UIManager.I.ShowToast("거래가 불가능한 종목입니다.", 1.5f);
            return;
        }
        if (selectedStock.isDelisting && (mode == 0 || mode == 2)) 
        { 
            UIManager.I.ShowToast("상장 폐지 정리 매매 중에는 매수/공매도가 불가능합니다.", 2.0f);
            return; 
        }

        long targetAmount = UIManager.I.GetInputValueInt(UI_GROUP_POPUP, UI_NAME_INPUT_AMOUNT);
        if (targetAmount <= 0) 
        {
            UIManager.I.ShowToast("수량을 입력해주세요.", 1.0f);
            return;
        }

        bool tradeSuccess = false;
        long totalExecutionCost = 0; // 총 거래 대금 (누적)
        long totalExecuted = 0;      // 총 체결 수량 (누적)
        long lastPrice = selectedStock.currentPrice;

        // 모드별 호가창 선택
        List<Order> targetBook = (mode == 0 || mode == 3) ? selectedStock.SellOrders : selectedStock.BuyOrders;
        bool isBuying = (mode == 0 || mode == 3); // 매수 혹은 숏커버 (돈이 나가는 행위)

        // 1. 유동성 체크
        if (targetBook.Count == 0) 
        { 
            UIManager.I.ShowToast("체결 가능한 물량이 없습니다!", 1.5f);
            return; 
        }
        
        // 2. 기초 자금/수량 체크
        if (isBuying && player.money < targetBook[0].price)
        {
             UIManager.I.ShowToast("자금이 부족합니다!", 1.0f);
             return;
        }
        if (mode == 1 && player.GetStockCount(selectedStock.data) < targetAmount) 
        {
            UIManager.I.ShowToast("보유 주식이 부족합니다.", 1.0f);
            return;
        }
        if (mode == 3 && player.GetShortCount(selectedStock.data) < targetAmount)
        {
            UIManager.I.ShowToast("상환할 공매도 잔고가 부족합니다.", 1.0f);
            return;
        }

        // 3. [핵심 수정] 호가창 순회 및 자금 정밀 체크
        while (targetAmount > 0 && targetBook.Count > 0)
        {
            Order bestOrder = targetBook[0];
            long tradeVol = Math.Min(targetAmount, bestOrder.amount);
            long tradePrice = bestOrder.price;

            // 🐛 [버그 수정] 누적 체결액(totalExecutionCost)을 고려하여 남은 돈 계산
            if (isBuying) 
            {
                long cost = tradePrice * tradeVol;
                long remainingCash = player.money - totalExecutionCost; // 현재 잔고 - 이번 거래에서 이미 쓴 돈

                if (remainingCash < cost) 
                {
                    // 돈이 모자라면 살 수 있는 만큼만 다시 계산
                    tradeVol = remainingCash / tradePrice;
                    if (tradeVol <= 0) break; // 1주도 못 사면 루프 종료
                }
            }

            // 실제 체결 처리
            totalExecutionCost += tradePrice * tradeVol;
            totalExecuted += tradeVol;
            
            // 호가창 업데이트
            bestOrder.amount -= tradeVol;
            targetAmount -= tradeVol;
            lastPrice = tradePrice;

            if (bestOrder.amount <= 0) targetBook.RemoveAt(0);
        }

        if (totalExecuted == 0) return;

        // 4. 자산 정산 및 후처리
        long avgPrice = totalExecutionCost / totalExecuted;
        
        selectedStock.previousPrice = selectedStock.currentPrice;
        selectedStock.currentPrice = (int)lastPrice;
        selectedStock.SortOrderBooks();

        switch (mode)
        {
            case 0: // 매수
                player.money -= totalExecutionCost;
                player.AddStock(selectedStock.data, totalExecuted);
                player.SetLastAction($"<b>[{selectedStock.data.stockName}]</b> {totalExecuted:N0}주 매수 (평단: {avgPrice:N0}원)");
                
                if (wasLastInfoBroker)
                {
                    activeBrokerContract = new BrokerContract { data = selectedStock.data, amount = totalExecuted, costBasis = avgPrice };
                    wasLastInfoBroker = false;
                }
                break;

            case 1: // 매도
                long tax = (long)(totalExecutionCost * transactionTaxRate);
                player.money += (totalExecutionCost - tax);
                player.RemoveStock(selectedStock.data, totalExecuted);
                player.SetLastAction($"<b>[{selectedStock.data.stockName}]</b> {totalExecuted:N0}주 투매 (평단: {avgPrice:N0}원)");
                break;

            case 2: // 공매도
                long taxShort = (long)(totalExecutionCost * transactionTaxRate);
                long margin = (long)(totalExecutionCost * player.initialMarginRatio);
                long cashNeeded = (margin + taxShort) - totalExecutionCost; // 내 돈 들어가는 액수

                // 공매도는 복잡해서 현금 체크를 여기서 한 번 더 확실하게 함
                if (player.money >= cashNeeded)
                {
                    player.money -= cashNeeded;
                    player.lockedMargin += margin;
                    selectedStock.remainShares -= totalExecuted;
                    player.AddShort(selectedStock.data, totalExecuted, avgPrice);
                    player.SetLastAction($"<b>[{selectedStock.data.stockName}]</b> 대량 공매도 ({totalExecuted:N0}주)");
                }
                else
                {
                    UIManager.I.ShowToast("증거금이 부족하여 거래가 취소되었습니다.", 1.5f);
                    // 롤백 로직이 없으므로 단순 리턴 (호가창이 깎인 채로 남는 이슈가 있으나, 게임적 허용)
                    // 완벽하게 하려면 호가창 복구 로직이 필요함
                    return; 
                }
                break;

            case 3: // 숏커버
                long entryPrice = player.GetAvgShortPrice(selectedStock.data);
                long releaseMargin = (long)(entryPrice * totalExecuted * player.initialMarginRatio);
                
                // (필요자금) > (현금 + 반환될 증거금) 인지 체크
                if (player.money + releaseMargin >= totalExecutionCost)
                {
                    player.lockedMargin -= releaseMargin;
                    player.money += (releaseMargin - totalExecutionCost);
                    
                    selectedStock.remainShares += totalExecuted;
                    player.RemoveShort(selectedStock.data, totalExecuted);
                    player.SetLastAction($"<b>[{selectedStock.data.stockName}]</b> 숏커버 완료 ({totalExecuted:N0}주)");
                }
                else
                {
                    // 숏커버 실패 (이론상 while문 안에서 체크되므로 여기 올 일은 거의 없음)
                    UIManager.I.ShowToast("자금이 부족합니다.", 1.0f);
                    return;
                }
                break;
        }

        tradeSuccess = true;

        if (tradeSuccess)
        {
            string tradeType = mode == 0 ? "매수" : (mode == 1 ? "매도" : (mode == 2 ? "공매도" : "숏커버"));
            UIManager.I.ShowToast($"[{selectedStock.data.stockName}]\n{tradeType} {totalExecuted}주 체결 완료!", 2.0f);

            long moneyChange = 0;
            switch (mode)
            {
                case 0: moneyChange = -totalExecutionCost; break;
                case 1: moneyChange = totalExecutionCost - (long)(totalExecutionCost * transactionTaxRate); break;
                case 2: 
                    long taxS = (long)(totalExecutionCost * transactionTaxRate);
                    long marg = (long)(totalExecutionCost * player.initialMarginRatio);
                    moneyChange = -((marg + taxS) - totalExecutionCost); 
                    break;
                case 3: 
                    long ePrice = player.GetAvgShortPrice(selectedStock.data);
                    long rMargin = (long)(ePrice * totalExecuted * player.initialMarginRatio);
                    moneyChange = rMargin - totalExecutionCost;
                    break;
            }

            if (FloatingTextManager.I != null && moneyChange != 0)
            {
                FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, moneyChange);
            }

            if (Mathf.Abs(lastPrice - selectedStock.previousPrice) / (float)selectedStock.previousPrice > 0.05f)
            {
                Debug.Log("💥 시장 충격 발생! 주가 급변!");
                UpdateNewsUI($"[속보] {selectedStock.data.stockName}, 대량 주문으로 주가 급변동!", Color.red);
                if (aiManager != null) aiManager.OnMarketShock(selectedStock, (int)lastPrice);
            }

            UpdateTradePanelUI(); UpdateStockBoardUI(); UpdatePlayerMoneyUI(); UpdatePortfolioUI();
        }
    }

    // ➕ [신규] 슬라이더 값 변경 시 호출
    void OnSliderAmountChanged(float value)
    {
        int amount = Mathf.RoundToInt(value);
        // 입력창 텍스트 갱신 (Notify 안 함으로써 무한 루프 방지 가능하지만, 여기선 SetText만)
        UIManager.I.TrySetInputValue(UI_GROUP_POPUP, UI_NAME_INPUT_AMOUNT, amount.ToString());
    }

    // ➕ [신규] 입력창 값 변경 시 호출
    void OnInputAmountChanged(string valueStr)
    {
        if (int.TryParse(valueStr, out int amount))
        {
            // 슬라이더 값 갱신
            UIManager.I.TrySetSliderValue(UI_GROUP_POPUP, UI_NAME_SLIDER_AMOUNT, amount);
        }
    }

    #endregion

    #region Information Trading

    public long CalculateInfoCost(int tier, long totalAsset)
    {
        long threshold = player.money;
        switch (tier)
        {
            case 0: // 🤥 사기꾼 (T1): 사실상 정보상이 아님.
                double scammerstRate = 0.001;
                return baseCostScammer + (long)(totalAsset * scammerstRate);
            case 5: // 🗞️ 신문팔이 (T1): 거짓일 경우에 큰 손해를 보지만, 소소한 이득을 볼 수도 있음.
                double newsboyRate = 0.002; 
                return baseCostNewsboy + (long)(totalAsset * newsboyRate);

            case 1: // 📊 분석가 (T2): 어떤 기업인지 모른다는 것이 매우 크게 작용
                double analystRate = 0.008; // 1.0%
                return baseCostAnalyst + (long)(totalAsset * analystRate);
            case 2: // 💻 해커 (T2): 해독만 되면 브로커급의 정보를 얻음
                double hackerRate = 0.01;
                return baseCostHacker + (long)(totalAsset * hackerRate);
                
            case 4: // 🕵️ 스파이 (T3): 1위의 행동을 알 수 있지만, 작전 세력에게 당할 수 있음
                double spyRate = 0.015;
                return baseCostSpy + (long)(totalAsset * spyRate);
            case 6: // 🏛️ 로비스트 (T3): 파급효과일 떄, 이득을 크게 보지만 경쟁사끼리의 대결일 때, 도박이 됨.
                double lobbyRate = 0.025; 
                return baseCostLobbyist + (long)(totalAsset * lobbyRate);    
            
            case 7: // 👻 브로커 (T4): 확실한 뉴스 정보를 얻지만, 이득을 얻지 못 하면 페널티가 큼.
                return 0;    
            case 3: // 🏢 내부자 (T4): 압도적으로 비싸지만 확실한 정보를 얻음
                double insiderRate = 0.045;
                return baseCostInsider + (long)(totalAsset * insiderRate);
        }
        return 0;
    }

    // 🖼️ [신규] 정보원 이미지 교체 헬퍼 함수
    // spriteAgents 리스트에 있는 스프라이트의 이름(파일명)과 매칭합니다.
    void SetAgentPortrait(string agentName)
    {
        if (spriteAgents == null || spriteAgents.Count == 0) return;

        // 리스트에서 이름이 일치하는 스프라이트 찾기 (대소문자 무시)
        Sprite targetSprite = spriteAgents.Find(s => s.name.Equals(agentName, StringComparison.OrdinalIgnoreCase));

        if (targetSprite != null)
        {
            // UI 이미지 교체 (InfoTradingPanel 그룹 내의 Potrait_Img)
            UIManager.I.TrySetSprite(UI_GROUP_INFOTRADE, "Potrait_Img", targetSprite);
        }
        else
        {
            Debug.LogWarning($"[StockMarketManager] spriteAgents 리스트에서 '{agentName}' 이름을 가진 스프라이트를 찾을 수 없습니다.");
        }
    }

    // 🤖 [수정] AI용 정보 제공 함수 (모든 정보원 지원)
    public PublicEventInfo GetInfoForAI(AIInvestor ai, int infoTier)
    {
        // 1. 초대형 이벤트(공개)는 무료 & 100% 정확
        if (currentEvent.HasValue && currentEvent.Value.isMegaEvent)
        {
            PublicEventInfo publicInfo = new PublicEventInfo();
            publicInfo.hasEvent = true;
            publicInfo.eventTitle = currentEvent.Value.newsTitle;
            publicInfo.isGoodNews = currentEvent.Value.isGoodNews;
            publicInfo.targets = currentEvent.Value.scenarioTargets ?? new Dictionary<RuntimeStock, float>();
            if (currentEvent.Value.singleTarget != null && !publicInfo.targets.ContainsKey(currentEvent.Value.singleTarget))
            {
                publicInfo.targets.Add(currentEvent.Value.singleTarget, currentEvent.Value.singleMultiplier);
            }
            return publicInfo;
        }

        // 2. 비용 계산 및 차감
        PublicEventInfo info = new PublicEventInfo { hasEvent = false, targets = new Dictionary<RuntimeStock, float>() };
        // 🛠️ [수정 포인트] ai.GetAITotalAsset(ai) -> aiManager.GetAITotalAsset(ai) 로 변경
        // aiManager 변수는 이미 StockMarketManager 상단에 선언되어 있습니다.
        long totalAsset = 0;
        if (aiManager != null) 
        {
            totalAsset = aiManager.GetAITotalAsset(ai);
        }
        else
        {
            // 예외 처리: 만약 aiManager가 없다면 현금 기준으로 계산
            totalAsset = ai.money; 
        }

        long aiAssets = ai.money - ai.currentDebt; 
        // (AI 자산 계산 간소화: 보유 주식 가치까지 더하면 좋지만, 현금 흐름상 money로만 체크해도 무방)
        
        long cost = CalculateInfoCost(infoTier, totalAsset); // AI 자산 기준 비용
        
        // 브로커(7)는 착수금이 0원이거나 낮음 (여기선 0원으로 가정 or CalculateInfoCost 따름)
        if (ai.money < cost) return info; 
        ai.money -= cost;

        // 3. 현재 이벤트 없으면 빈 정보 반환 (단, 신문팔이는 항상 정보를 줌)
        if (!currentEvent.HasValue || currentEvent.Value.isHidden)
        {
            // 신문팔이(5)는 이벤트가 없어도 "아무 일 없음"이라는 정보를 확인한 셈치고 랜덤 주식 하나 안전하다고 판단
            if (infoTier == 5 && marketStocks.Count > 0)
            {
                var safeStock = marketStocks[UnityEngine.Random.Range(0, marketStocks.Count)];
                info.hasEvent = true;
                info.eventTitle = "시장 평온";
                info.targets.Add(safeStock, 1.0f); // 1.0 = 변동 없음 (안전)
            }
            return info;
        }

        var evt = currentEvent.Value;
        info.hasEvent = true;
        info.eventTitle = evt.newsTitle;
        info.isGoodNews = evt.isGoodNews;

        // 실제 타겟 목록 확보
        Dictionary<RuntimeStock, float> realTargets = new Dictionary<RuntimeStock, float>();
        if (evt.singleTarget != null)
        {
            realTargets.Add(evt.singleTarget, evt.singleMultiplier);
            if (evt.isRippleEvent)
            {
                foreach (var s in marketStocks)
                    if (s != evt.singleTarget && s.data.sector == evt.singleTarget.data.sector)
                        realTargets.Add(s, 1.0f + (evt.singleMultiplier - 1.0f) * 0.4f);
            }
        }
        else if (evt.scenarioTargets != null)
        {
            realTargets = evt.scenarioTargets;
        }

        // 4. 정보원별 로직 (AI 관점)
        switch (infoTier)
        {
            case 0: // 🤥 사기꾼 (10% 진실)
                if (UnityEngine.Random.value > 0.9f) info.targets = realTargets;
                else
                {
                    // 가짜 정보 생성
                    info.isGoodNews = !evt.isGoodNews;
                    var fake = GetWeightedRandomStock();
                    if (fake != null) info.targets.Add(fake, info.isGoodNews ? 1.3f : 0.7f);
                }
                break;

            case 1: // 📊 분석가 (정확함)
            case 2: // 💻 해커 (AI는 텍스트 해석 필요 없으므로 정확한 타겟 제공)
            case 3: // 🏢 내부자 (정확함)
                info.targets = realTargets; 
                break;

            case 4: // 🕵️ 스파이 (1등 따라하기)
                // 로직: 1등(플레이어 혹은 AI)이 가장 많이 보유한 종목을 알려줌
                // 편의상, 이번 이벤트의 '진짜 타겟'을 알려주는 것으로 대체 (1등도 그걸 샀을 테니까)
                info.targets = realTargets;
                break;

            case 5: // 🗞️ 신문팔이 (무작위 1개 확인)
                // 실제 타겟 중 하나만 알려주거나, 관계없는 주식을 알려줌
                if (realTargets.Count > 0 && UnityEngine.Random.value > 0.5f)
                {
                    var key = realTargets.Keys.ElementAt(UnityEngine.Random.Range(0, realTargets.Count));
                    info.targets.Add(key, realTargets[key]);
                }
                else
                {
                    // 꽝 (관계 없는 주식)
                    var randomStock = GetWeightedRandomStock();
                    if(!realTargets.ContainsKey(randomStock)) 
                        info.targets.Add(randomStock, 1.0f); // 영향 없음
                }
                break;

            case 6: // 🏛️ 로비스트 (섹터 전체 동향)
                // 이벤트가 영향을 주는 '섹터'의 모든 주식을 타겟으로 추가
                foreach(var target in realTargets)
                {
                    StockSector s = target.Key.data.sector;
                    foreach(var stock in marketStocks)
                    {
                        if(stock.data.sector == s && !info.targets.ContainsKey(stock))
                        {
                            // 같은 섹터면 약한 영향력이라도 있다고 전달
                            float effect = (target.Value > 1.0f) ? 1.05f : 0.95f; 
                            info.targets.Add(stock, effect);
                        }
                    }
                }
                break;

            case 7: // 👻 브로커 (하이 리스크 하이 리턴)
                // 정확한 정보 제공 + AI는 페널티 구현이 복잡하므로 약간의 추가 비용을 미리 뗐다고 가정
                // 대신 타겟에 대한 확신(Multiplier)을 더 강하게 줘서 더 많이 사게 유도
                foreach(var kvp in realTargets)
                {
                    float amplified = kvp.Value > 1.0f ? kvp.Value + 0.1f : kvp.Value - 0.1f;
                    info.targets.Add(kvp.Key, amplified);
                }
                break;
        }

        return info;
    }

    // -------------------------------------------------------------
    // [수정] 패널 열 때: 알림 끄기 + 초기화
    // -------------------------------------------------------------
    public void ToggleInfoTradingPanel(bool isOpen)
    {
        if (!UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_InfoTrading", isOpen)) return;
        
        if (isOpen)
        {
            HideNotificationIcon();
            
            // 🌟 [수정] 패널 열 때 UI 상태 강제 초기화
            UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_AgentList", true);
            UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_BtnList", false);

            // 아직 정보를 안 썼다면 디폴트 텍스트(안내원) 표시
            if (!hasPlayerUsedInfo)
            {
                UpdateDefaultInfoText();
            }
        }
    }

    // -------------------------------------------------------------
    // [신규] 기본 안내 텍스트 및 가격 업데이트
    // -------------------------------------------------------------
    void UpdateDefaultInfoText()
    {
        if (UIManager.I == null) return;

        // 🖼️ [수정] 디폴트 상태일 때 'Announcer' 이미지 출력
        SetAgentPortrait("Announcer");

        long myAsset = player.GetTotalAsset();
        long cost0 = CalculateInfoCost(0, myAsset);
        long cost1 = CalculateInfoCost(1, myAsset);
        long cost2 = CalculateInfoCost(2, myAsset);
        long cost3 = CalculateInfoCost(3, myAsset);
        long cost4 = CalculateInfoCost(4, myAsset); // 스파이 비용
        long cost5 = CalculateInfoCost(5, myAsset); // 🗞️ [신규] 신문팔이 비용
        long cost6 = CalculateInfoCost(6, myAsset); // 🏛️ [신규] 로비스트 비용
        long cost7 = CalculateInfoCost(7, myAsset); // 👻 브로커 비용

        // 요청하신 기본 멘트 적용
        string defaultText = 
            $"정보원을 선택하세요.\n" +
            $"<color=white>[사기꾼]</color>: <size=75%><color=red>그도 사실 지금 시세를 잘 모릅니다.</color> 가끔 제대로 구할 때도 있지만요.</size>\n" +
            $"<color=#6F4F28>[신문팔이]</color>: <size=75%>이 아이는 <color=red>아무 기업 하나가 무슨 사건에 휘말렸는지 아닌지만 확인합니다.</color></size>\n" +
            $"<color=yellow>[분석가]</color>: <size=75%>그녀는 사건의 냄새를 잘 맡습니다. <color=red>하지만, 그게 누군진 모릅니다.</color></size>\n" +
            $"<color=blue>[해커]</color>: <size=75%>그의 해킹 실력은 대단하지만, <color=red>흠, 일부 텍스트가 망ㄱ진 #같요군?</color></size>\n" +
            $"<color=purple>[첩보원]</color>: <size=75%>그녀는 현재 <color=green>자산 1위 투자자</color>의 최근 행적을 파헤칩니다. 무섭군요..</size>\n" +
            $"<color=#8B4513>[로비스트]</color>: <size=75%>그들은 정책 변화를 분석하여 <color=green>섹터 전체의 흐름</color>을 예측합니다.</size>\n" +
            $"<color=#FBCEB1>[브로커]</color>: <size=75%>그는 <color=green>정확한 정보.</color> 다만, <color=red>수익의 대부분을 요구</color>하며 <color=red>손실 시 막대한 페널티</color>가 붙죠.</color></size>\n" +
            $"<color=orange>[내부자]</color>: <size=75%>그들은 <color=green>모든 것</color>을 압니다. 물론, 당신의 지갑이 감당할 수 있을 때만요.</size>";
            

        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", defaultText);  

        // 버튼 가격 갱신
        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Cost_Scammer", $"{cost0:N0}원");
        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Cost_Analyst", $"{cost1:N0}원");
        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Cost_Hacker", $"{cost2:N0}원");
        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Cost_Insider", $"{cost3:N0}원");
        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Cost_Spy", $"{cost4:N0}원");
        // 🗞️ [신규] 신문팔이 가격 텍스트 (UI에 Txt_Cost_Newsboy 텍스트 오브젝트 필요)
        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Cost_Newsboy", $"{cost5:N0}원");
        // 🏛️ [신규] 로비스트 가격 텍스트 (UI에 Txt_Cost_Lobbyist 텍스트 오브젝트 필요)
        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Cost_Lobbyist", $"{cost6:N0}원");
        // 👻 [신규] 브로커 가격 텍스트 (UI에 Txt_Cost_Broker 텍스트 오브젝트 필요)
        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Cost_Broker", $"{cost7:N0}원");
    }

    // -------------------------------------------------------------
    // [신규] 외부(DraggableUI)에서 호출할 알림 끄기 함수
    // -------------------------------------------------------------
    public void HideNotificationIcon()
    {
        if (notificationIcon != null && notificationIcon.activeSelf)
        {
            notificationIcon.SetActive(false);
        }
    }

    bool IsInfoUsed()
    {
        // if (hasPlayerUsedInfo) UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "[시스템] 이미 정보를 입수했습니다.");
        return hasPlayerUsedInfo;
    }

    private string[] ScammerTalks = new string[]
    {
        "이번에 확실한 정보 입수했어! 믿어봐!",
        "이거 완전 대박 찌라시야! 남들 모를 때 빨리 들어가!",
        "흐음.. 이번엔 진짜라니까? 놓치면 후회할걸?",
        "특급 정보야! 이거 안 믿으면 손해 본다구!",
        "이번 기회는 흔치 않아! 얼른 결정해!",
        "내가 직접 확인한 정보라구! 믿어봐!",
        "이번엔 진짜 대박 날 거야! 같이 가자구!",
        "놓치면 후회할걸? 이번 찌라시 완전 확실해!",
        "이번 정보는 비밀이야! 아무한테도 말하지 마!",
        "이번 기회는 단 한 번 뿐이야! 서둘러!",
        "이번 찌라시는 완전 대박이야! 믿어봐!",
        "이번 정보는 확실해! 같이 가자구!"
    };
    private string[] HackerTalks = new string[]
    {
        "시스템 침투 성공. 근데 복호화 키가 안 먹히네.",
        "백도어 열었어. 데이터가 좀 깨져서 들어왔는데 볼래?",
        "방화벽 뚫느라 힘들었어. 노이즈가 좀 꼈는데 알아서 해석해.",
        "관리자 권한 탈취 완료. 근데 파일 일부가 손상됐어.",
        "네트워크 패킷 낚아챘어. 암호화가 덜 풀렸는데.. 급하지?",
        "메인 서버 접속. 보안이 빡세서 이만큼만 건졌어.",
        "루트 권한 땄는데, 로그 파일이 뒤죽박죽이야.",
        "데이터 스트림 해킹 중.. 젠장, 역추적 당할 뻔했네.",
        "보안 프로토콜 우회 성공. 원본 데이터 전송한다.",
        "키보드 좀 두들겼지. 근데 텍스트가 깨져서 나오네.",
        "비밀 회선 도청했어. 신호가 약해서 끊기는데 잘 봐봐.",
        "쓰레기 데이터 속에서 보석을 찾았지. 해독은 네 몫이야."
    };
    private string[] SpyTalks = new string[]
    {
        "\"이 흐름... 확실해. 놈들을 따돌릴 기회야.\"",
        "\"저 사람, 분명 뭔가 숨기고 있어. 더 지켜보자.\"",
        "\"정보는 힘이다. 이번 기회에 확실히 알아내자.\"",
        "\"경계심을 늦추지 마. 작은 실수도 치명적일 수 있어.\"",
        "\"이제 곧 결정적인 순간이 올 거야. 준비 단단히 해.\"",
        "\"그들의 움직임을 주시해. 작은 변화도 놓치지 말자.\"",
        "\"정보 수집은 끝이 없어. 계속해서 파고들어야 해.\"",
        "\"이번 기회는 놓치지 말자. 확실한 정보를 얻어야 해.\"",
        "\"조용히 움직여야 해. 들키면 모든 게 끝장이야.\"",
        "\"정보의 바다에서 진주를 찾아내자. 이번이 그 기회야.\"",
        "\"그들의 비밀을 파헤쳐야 해. 이번이 결정적인 순간이야.\"",
        "\"침묵은 금이다. 조용히 정보를 수집하자.\""
    };

    private string[] NewsboyTalks = new string[]
    {
        "오늘의 신문입니다! 최신 뉴스가 가득해요!",
        "특별 할인 중! 지금 사면 더 많은 정보를 얻을 수 있어요!",
        "이번 호에는 중요한 경제 뉴스가 실려 있어요!",
        "놓치지 마세요! 오늘의 헤드라인을 확인하세요!",
        "새로운 소식이 도착했어요! 지금 바로 확인해보세요!",
        "이번 주 최고의 뉴스만 모았어요! 읽어보세요!",
        "특별 기획 기사도 준비되어 있어요! 기대하세요!",
        "오늘의 신문으로 시장 동향을 파악하세요!",
        "최신 정보를 빠르게 전달해드려요! 구독하세요!",
        "이번 호에는 투자에 도움이 되는 팁도 있어요!",
        "놓치지 마세요! 오늘의 헤드라인을 확인하세요!"
    };
    private string[] NewsboyTalks2 = new string[]
    {
        "조용하다는 건, 무난하게 지내도 된다는 뜻 아닐까요?",
        "안전할 수도 있지만, 시장에서 소외된 걸 수도 있죠.",
        "글쎄요, 너무 조용해서 오히려 불안한데요?",
        "이 기업은 마치 깊은 잠에 빠진 것 같아요.",
        "바람 한 점 없는 호수 같네요. 지루할 정도로요.",
        "개미 투자자들도 여기엔 관심이 없나 봐요.",
        "이슈가 없는 게 최고의 이슈일 수도 있습니다.",
        "폭풍전야일까요, 아니면 그냥 인기 없는 걸까요?",
        "뉴스가 없다는 건, 경영진이 일을 안 하거나 너무 잘하거나 둘 중 하나겠죠.",
        "시끄러운 시장통에서 여기만 절간 같네요.",
        "투자하기엔 심심하고, 팔기엔 아쉬운 그런 상태랄까?",
        "지금은 잠잠하지만, 언제 터질지 모르는 시한폭탄일 수도 있어요.",
        "이 기업 주주들은 밤에 발 뻗고 자겠네요. 별일 없으니까요.",
        "무소식이 희소식이라던데, 주식 시장에선 꼭 그렇진 않죠.",
        "먼지만 날리는 텅 빈 도로 같아요. 지나가는 차도 없네요."
    };

    // 🤥 [사기꾼] 10% 진실(랜덤), 90% 거짓
    void OnClickScammer()
    {
        SetAgentPortrait("Scammer");
        // 다른 버튼을 눌렀을 수 있으므로 내부자 패널 끄기
        UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_BtnList", false);

        if (IsInfoUsed()) return;
        long cost = CalculateInfoCost(0, player.GetTotalAsset());
        if (player.money < cost) { UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=white>[사기꾼]</color>\n돈 없으면 저리 가."); return; }
        
        // 10% 확률로 진실, 아니면 거짓
        bool isTruth = UnityEngine.Random.value < 0.1f; 
        
        // 진실 모드인데 정보가 없는 경우 -> 돈 안 받음
        var target = GetRandomTargetEvent();
        if (isTruth && !target.HasValue)
        {
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=white>[사기꾼]</color>\n(주위를 둘러보며) 진짜 아무 일도 없어. 내 촉을 믿어.");
            // 💰 돈 차감 안 함
            return;
        }

        // 정보 제공 (진실이든 거짓이든 말을 했으니 돈 받음)
        player.money -= cost;
        // 💸 [FloatingText] 비용 지출 연출
        if (FloatingTextManager.I != null)
        {
            FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, -cost);
        }
        UpdatePlayerMoneyUI();

        string msg = "";
        if (isTruth && target.HasValue)
        {
            msg = target.Value.newsTitle;
        }
        else
        {
            msg = GenerateFakeNews(); // 가짜 뉴스 생성
        }

        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", $"<color=white>[사기꾼]</color>\n{msg}\n({ScammerTalks[UnityEngine.Random.Range(0, ScammerTalks.Length)]})");
        hasPlayerUsedInfo = true;
    }

    // 📊 [분석가] 무작위 이벤트 1개 분석 (종목명 가림)
    void OnClickAnalyst()
    {
        SetAgentPortrait("Analyst");
        UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_BtnList", false);

        if (IsInfoUsed()) return;
        long cost = CalculateInfoCost(1, player.GetTotalAsset());
        if (player.money < cost) { UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=yellow>[분석가]</color>\n비용이 부족합니다."); return; }
        
        var evtNullable = GetRandomTargetEvent(); // 무작위 선택

        if (evtNullable.HasValue)
        {
            // 💰 정보 있음: 결제 진행
            player.money -= cost;
            // 💸 [FloatingText] 비용 지출 연출
            if (FloatingTextManager.I != null)
            {
                FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, -cost);
            }
            UpdatePlayerMoneyUI();

            var evt = evtNullable.Value;
            string blinded = BlindText(evt.newsTitle, evt);
            string sentiment = evt.isGoodNews ? "<color=red>매수(Buy)</color>" : "<color=blue>매도(Sell)</color>";
            
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", $"<color=yellow>[분석가]</color>\n\n[REPORT]\n{blinded}\n\n의견: {sentiment}");
            hasPlayerUsedInfo = true;
        }
        else 
        {
            // 💰 정보 없음: 결제 안 함
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=yellow>[분석가]</color>\n현재 분석할 만한 특이 동향이 발견되지 않았습니다.");
        }
    }

    // 💻 [해커] 무작위 이벤트 1개 해킹 (텍스트 망가짐)
    void OnClickHacker()
    {
        SetAgentPortrait("Hacker");
        UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_BtnList", false);

        if (IsInfoUsed()) return;
        long cost = CalculateInfoCost(2, player.GetTotalAsset());
        if (player.money < cost) { UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=blue>[해커]</color>\n입금부터 해."); return; }
        
        var evtNullable = GetRandomTargetEvent(); // 무작위 선택

        if (evtNullable.HasValue)
        {
            // 💰 정보 있음: 결제 진행
            player.money -= cost;
            // 💸 [FloatingText] 비용 지출 연출
            if (FloatingTextManager.I != null)
            {
                FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, -cost);
            }
            UpdatePlayerMoneyUI();

            string corrupted = CorruptText(evtNullable.Value.newsTitle);
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", $"<color=blue>[해커]</color>\n>_ DECRYPTING...\n>_ {corrupted}");
            hasPlayerUsedInfo = true;
        }
        else 
        {
            // 💰 정보 없음: 결제 안 함
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=blue>[해커]</color>\n네트워크가 너무 조용해. 건질 게 없어.");
        }
    }

    // 🏢 [내부자] 이벤트 선택 시스템
    void OnClickInsider()
    {
        if (IsInfoUsed()) return;
        long cost = CalculateInfoCost(3, player.GetTotalAsset());
        
        // 1. 비용 부족 체크
        if (player.money < cost) 
        { 
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=orange>[내부자]</color>\nVIP 멤버십 비용이 부족합니다."); 
            return; 
        }

        SetAgentPortrait("Insider");

        insiderOptions = GetValidInfoCandidates();

        if (insiderOptions.Count == 0)
        {
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=orange>[내부자]</color>\n지금은 은밀하게 진행 중인 건이 없습니다.");
            UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_AgentList", true);
            UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_BtnList", false);
            return;
        }

        // 🌟 정보 있음: AgentList 숨기고 BtnList 표시
        UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_AgentList", false);
        UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_BtnList", true);

        // 2. 이벤트 선택지 보여주기 (여기서 수정됨)
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"<color=orange>[내부자]</color> 비용: {cost:N0}원");
        sb.AppendLine("진상을 파악할 뉴스를 선택하십시오.\n");

        for (int i = 0; i < insiderOptions.Count; i++)
        {
            // 🛠️ [수정] BlindText(내용 유출) -> GetPublicNewsTitle(뉴스 티커 내용) 사용
            string publicTitle = GetPublicNewsTitle(insiderOptions[i]);
            sb.AppendLine($"<color=green>[안건 {i + 1}]</color> {publicTitle}");
        }
        sb.AppendLine("\n하단 버튼을 눌러 선택하세요.");

        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", sb.ToString());
    }

    // 🌟 [신규] 내부자 정보 선택 버튼에 연결할 함수
    public void OnClickInsiderSelect(int index)
    {
        // 유효성 검사
        if (IsInfoUsed()) return;
        if (insiderOptions == null || index < 0 || index >= insiderOptions.Count) return;

        long cost = CalculateInfoCost(3, player.GetTotalAsset());
        if (player.money < cost) return;

        // 💰 실제 결제 및 정보 공개
        player.money -= cost;
        // 💸 [FloatingText] 비용 지출 연출
        if (FloatingTextManager.I != null)
        {
            FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, -cost);
        }
        UpdatePlayerMoneyUI();

        // 🌟 선택 완료: BtnList 숨기고 AgentList 복귀 (원래 화면으로)
        UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_BtnList", false);
        UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_AgentList", true);

        var evt = insiderOptions[index];
        string msg = $"<color=orange>[내부자]</color>\n<color=white>{evt.newsTitle}</color>\n";
        
        if (evt.singleTarget != null) 
            msg += $"- 타겟: {evt.singleTarget.data.stockName} ({(evt.isGoodNews ? "▲ 호재" : "▼ 악재")})";
        else if (evt.scenarioTargets != null) 
        {
            msg += "- 관련 종목:\n";
            foreach(var t in evt.scenarioTargets)
                msg += $"{t.Key.data.stockName} ";
        }

        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", msg);
        
        hasPlayerUsedInfo = true;
        insiderOptions.Clear();
    }

    // 📢 [수정] 이벤트의 공개용(뉴스 티커용) 텍스트 반환 헬퍼
    string GetPublicNewsTitle(PendingEvent evt)
    {
        // 1. 이미 공개된 정보 (초대형, 상장, 파산) -> 그대로 출력
        if (evt.isMegaEvent || evt.isListing || evt.isBankruptcy)
        {
            return evt.newsTitle;
        }
        // 2. 해킹 -> 깨진 텍스트
        if (evt.isHidden)
        {
            return "SYSTEM ERROR: DATA CORRUPTED...";
        }
        // 3. 파급 효과 -> 모호한 동향
        if (evt.isRippleEvent)
        {
            return "[동향] 특정 산업군에 연쇄적인 반응이 감지됩니다.";
        }

        // 4. 일반 시나리오 -> 모호한 루머 (통합 배열 사용)
        // 🌟 HashCode를 사용하여 '해당 이벤트'는 언제나 '같은 인덱스'의 텍스트를 가져오게 고정함
        int index = Math.Abs(evt.newsTitle.GetHashCode()) % blindNewsTemplates.Length;
        return blindNewsTemplates[index];
    }

    void OnClickSpy()
    {
        SetAgentPortrait("Spy");
        if (IsInfoUsed()) return;

        long myTotalAsset = player.GetTotalAsset();
        long cost = CalculateInfoCost(4, myTotalAsset); // Tier 4

        // 돈 부족 확인
        if (player.money < cost) 
        { 
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", 
                $"<color=purple>[첩보원]</color>\n착수금이 부족하거든요!\n업계 1위의 정보를 빼내는 게 얼마나 힘든 지 아세요?!\n(필요: {cost:N0}원)"); 
            return; 
        }

        // 결제
        player.money -= cost;
        // 💸 [FloatingText] 비용 지출 연출
        if (FloatingTextManager.I != null)
        {
            FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, -cost);
        }
        UpdatePlayerMoneyUI();

        // 1. 전체 랭킹 1위 찾기 (플레이어 vs AI 전체)
        AIInvestor topAI = null;
        long maxAsset = -1;

        // AI 중 1등 찾기
        foreach (var ai in aiManager.aiInvestors)
        {
            long aiAsset = aiManager.GetAITotalAsset(ai);
            if (aiAsset > maxAsset)
            {
                maxAsset = aiAsset;
                topAI = ai;
            }
        }

        // 플레이어와 비교
        string resultMsg = "";
        
        if (myTotalAsset >= maxAsset)
        {
            // 🏆 플레이어가 1등인 경우
            resultMsg = $"<color=purple>[첩보원]</color>\n" +
                        $"조사 결과, 현재 시장 지배자(1위)는 바로.. <b>당신</b>입니다!\n\n" +
                        $"당신의 최근 행적: \"{player.lastActionLog}\"\n\n" +
                        $"(첩보원이 당신을 쳐다보며 어이없다는 듯 웃습니다. \"돈이 남아도나 보군요?\")";
        }
        else
        {
            // 🤖 AI가 1등인 경우
            if (topAI != null)
            {
                resultMsg = $"<color=purple>[첩보원]</color>\n" +
                            $"현재 자산 랭킹 1위는 <b>'{topAI.name}'</b> <size=50%>(자산: {maxAsset:N0}원)</size>이네요!\n흠, 대단하군..\n\n" +
                            $"[도청 기록 확보]\n" +
                            $"그 분은 최근 {topAI.lastTradeLog}\n\n" +
                            $"{SpyTalks[UnityEngine.Random.Range(0, SpyTalks.Length)]}";
            }
        }

        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", resultMsg);
        hasPlayerUsedInfo = true;
    }

    // 🗞️ [신문팔이] 무작위 기업 하나가 현재 이슈들에 연관되어 있는지 판별
    void OnClickNewsboy()
    {
        SetAgentPortrait("Newsboy");
        UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_BtnList", false);

        if (IsInfoUsed()) return;

        long myTotalAsset = player.GetTotalAsset();
        long cost = CalculateInfoCost(5, myTotalAsset);

        // 돈 부족 확인
        if (player.money < cost)
        {
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result",
                $"<color=#6F4F28>[신문팔이]</color>\n신문 사세요~!\n(어라? {cost:N0}원도 없으시네요..)");
            return;
        }

        // 신문팔이는 "정보 없음" 상태가 없음 (아무 기업이나 찍어서 "얘는 조용해요"라고 말하는 것도 정보임)
        // 따라서 항상 돈을 받음
        player.money -= cost;
        // 💸 [FloatingText] 비용 지출 연출
        if (FloatingTextManager.I != null)
        {
            FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, -cost);
        }
        UpdatePlayerMoneyUI();

        if (marketStocks.Count == 0) return;

        // 1. 상장된 기업 중 무작위 1개 선정
        RuntimeStock targetStock = marketStocks[UnityEngine.Random.Range(0, marketStocks.Count)];
        
        bool isAffected = false;
        float totalImpact = 1.0f; // 여러 이벤트가 겹칠 경우를 대비한 누적 영향력

        // 2. 현재 진행 중인 **모든** 이벤트 확인
        foreach (var evt in activeEvents)
        {
            if (evt.isListing || evt.isBankruptcy || evt.isHidden || evt.newsTitle.Contains("평온")) 
                continue;

            float impact = 1.0f;
            bool hit = false;

            if (evt.singleTarget == targetStock) { impact = evt.singleMultiplier; hit = true; }
            else if (evt.scenarioTargets != null && evt.scenarioTargets.ContainsKey(targetStock)) { impact = evt.scenarioTargets[targetStock]; hit = true; }
            else if (evt.isRippleEvent && evt.singleTarget != null && evt.singleTarget.data.sector == targetStock.data.sector)
            {
                float rawChange = evt.singleMultiplier - 1.0f;
                impact = 1.0f + (rawChange * 0.4f);
                hit = true;
            }

            if (hit) { isAffected = true; totalImpact *= impact; }
        }

        bool isGoodNews = totalImpact >= 1.0f;

        // 3. 결과 텍스트 생성
        string resultMsg = $"<color=#6F4F28>[신문팔이]</color>\n" +
                           $"{NewsboyTalks[UnityEngine.Random.Range(0, NewsboyTalks.Length)]}\n\n" +
                           $"이번에 알아온 기업은 <b>[{targetStock.data.stockName}]</b>인데요...\n\n";

        if (isAffected)
        {
            string direction = isGoodNews ? "<color=red>좋은 소식(호재)</color>" : "<color=blue>나쁜 소식(악재)</color>";
            resultMsg += $"지금 시장에 도는 <color=yellow>이슈들에 휘말려 있어요!</color>\n" +
                         $"분위기를 보아하니 <b>{direction}</b>인 것 같아요.\n" +
                         $"요주의하세요!";
        }
        else
        {
            resultMsg += $"이 기업은 현재 <b><color=green>아무런 이슈도 없습니다.</color></b>\n" +
                         $"시장이 시끄러워도 여긴 조용하네요.\n" +
                         $"{NewsboyTalks2[UnityEngine.Random.Range(0, NewsboyTalks2.Length)]}";
        }

        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", resultMsg);
        hasPlayerUsedInfo = true;
    }

    // 🏛️ [로비스트] 가장 오래 지속되는(대형) 이벤트 선택
    void OnClickLobbyist()
    {
        SetAgentPortrait("Lobbyist");
        UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_BtnList", false);

        if (IsInfoUsed()) return;
        long cost = CalculateInfoCost(6, player.GetTotalAsset());
        if (player.money < cost) { UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", $"<color=#8B4513>[로비스트]</color>\n자금이 부족합니다."); return; }
        
        var evtNullable = GetMajorTargetEvent(); // 대형/장기 이벤트 우선

        if (!evtNullable.HasValue)
        {
            // 💰 정보 없음: 결제 안 함
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", $"<color=#8B4513>[로비스트]</color>\n의회는 평온합니다. 특별한 법안 상정이 없군요.");
            return;
        }

        // 💰 정보 있음: 결제 진행
        player.money -= cost;
        // 💸 [FloatingText] 비용 지출 연출
        if (FloatingTextManager.I != null)
        {
            FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, -cost);
        }
        UpdatePlayerMoneyUI();

        // 섹터 영향 분석 로직
        var evt = evtNullable.Value;
        Dictionary<StockSector, float> sectorImpacts = new Dictionary<StockSector, float>();
        var targets = evt.scenarioTargets ?? new Dictionary<RuntimeStock, float>();
        if (evt.singleTarget != null && !targets.ContainsKey(evt.singleTarget)) targets.Add(evt.singleTarget, evt.singleMultiplier);

        foreach (var kvp in targets)
        {
            StockSector sector = kvp.Key.data.sector;
            if (!sectorImpacts.ContainsKey(sector)) sectorImpacts[sector] = 0;
            sectorImpacts[sector] += (kvp.Value - 1.0f);
        }

        StockSector mostImpactedSector = StockSector.IT; 
        float maxImpactMagnitude = 0;
        foreach (var kvp in sectorImpacts)
        {
            if (Mathf.Abs(kvp.Value) > maxImpactMagnitude) { maxImpactMagnitude = Mathf.Abs(kvp.Value); mostImpactedSector = kvp.Key; }
        }
        
        string directionStr = sectorImpacts[mostImpactedSector] >= 0 ? "<color=red>호재</color>" : "<color=blue>악재</color>";
        
        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", 
            $"[비밀 의회 보고서]\n이번 이슈는 <b><color=yellow>{mostImpactedSector}</color></b> 섹터에 {directionStr}로 작용할 예정입니다.");
        hasPlayerUsedInfo = true;
    }

    // 👻 [브로커] 가장 오래 지속되는(대형) 이벤트 선택
    void OnClickBroker()
    {
        SetAgentPortrait("Broker");
        UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_BtnList", false);

        if (IsInfoUsed()) return;
        long cost = CalculateInfoCost(7, player.GetTotalAsset());
        if (player.money < cost) { UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", $"<color=#FBCEB1>[브로커]</color>\n착수금이 부족하오."); return; }
        if (activeBrokerContract.HasValue) { UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", $"<color=#FBCEB1>[브로커]</color>\n이미 계약 중이오."); return; }

        var evtNullable = GetMajorTargetEvent(); // 대형/장기 이벤트 우선

        if (evtNullable.HasValue)
        {
            // 💰 정보 있음: 결제 진행
            player.money -= cost;
            // 💸 [FloatingText] 비용 지출 연출
            if (FloatingTextManager.I != null)
            {
                FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, -cost);
            }
            UpdatePlayerMoneyUI();
            wasLastInfoBroker = true; 

            var evt = evtNullable.Value;
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", 
                $"<color=#FBCEB1>[브로커]</color>\n<color=red>이용료 {cost:N0}원. 수익의 85% 쉐어 조건입니다.</color>\n\n[내부 정보]\n{evt.newsTitle}");
            hasPlayerUsedInfo = true;
        }
        else 
        {
            // 💰 정보 없음: 결제 안 함
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", $"<color=#FBCEB1>[브로커]</color>\n굴릴만한 건수가 없소. 돈은 받지 않겠소.");
        }
    }

    // 👻 [신규] 브로커 계약 정산 (턴 종료 시)
    void ProcessBrokerContract()
    {
        if (activeBrokerContract.HasValue)
        {
            var contract = activeBrokerContract.Value;
            RuntimeStock stock = marketStocks.Find(s => s.data == contract.data);
            if (stock == null) 
            {
                 Debug.LogWarning($"👻 [브로커 오류] 계약된 주식 {contract.data.stockName}이 시장에 없습니다. 페널티 없이 계약 해지.");
                 activeBrokerContract = null;
                 return;
            }

            // 1. P/L 계산 (현재 가격 기준)
            long totalCostBasis = contract.costBasis * contract.amount;
            long currentVal = (long)stock.currentPrice * contract.amount;
            long netProfit = currentVal - totalCostBasis; // 순이익
            
            long feeOrPenalty = 0;
            
            // 2. 정산 및 페널티 적용
            if (netProfit >= 0) // 수익 발생 (Fee)
            {
                feeOrPenalty = (long)(netProfit * 0.85f);
                player.money -= feeOrPenalty;
                Debug.Log($"👻 [브로커 정산] 수익 {netProfit:N0}원 중 85% 수수료 {feeOrPenalty:N0}원 차감.");
            }
            else // 손실 발생 (Penalty)
            {
                // 🚨 총 자산의 10% 페널티
                long penalty = (long)(player.GetTotalAsset() * 0.10f);
                
                // 🚨 주식을 강제 매도하여 페널티 금액을 확보하고 차감
                ForceLiquidateForCash(penalty);
                player.money -= penalty; 
                feeOrPenalty = penalty; // 로그용
                Debug.Log($"👻 [브로커 페널티] 손실 발생! 총 자산의 10% ({penalty:N0}원) 페널티 부과 및 강제 현금화.");
            }
            
            // 3. 브로커 귀속 제거 (주식 회수 및 계약 해지)
            player.RemoveStock(contract.data, contract.amount);
            
            activeBrokerContract = null;
            Debug.Log($"👻 [브로커 계약 해지] {contract.data.stockName} {contract.amount}주 회수 완료. 총 정산 금액: {feeOrPenalty:N0}원.");
        }
    }

    // ==================================================================================
    // 🕵️‍♂️ 정보원 시스템 (Information Trading)
    // ==================================================================================

    // 1. [유효 이벤트 추출] 플레이어가 돈 주고 살만한 가치가 있는 이벤트들만 뽑기
    // (초대형, 상장, 파산, 해킹 등 이미 공개되었거나 정보가 없는 것은 제외)
    private List<PendingEvent> GetValidInfoCandidates()
    {
        List<PendingEvent> candidates = new List<PendingEvent>();
        foreach (var evt in activeEvents)
        {
            if (!evt.isMegaEvent && !evt.isListing && !evt.isBankruptcy && !evt.isHidden)
            {
                candidates.Add(evt);
            }
        }
        return candidates;
    }

    // 2. [전략: 무작위] 후보 중 하나 랜덤 선택 (사기꾼, 분석가, 해커용)
    private PendingEvent? GetRandomTargetEvent()
    {
        var candidates = GetValidInfoCandidates();
        if (candidates.Count == 0) return null;
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    // 3. [전략: 대형/장기] 지속 시간이 가장 긴 이벤트 선택 (로비스트, 브로커용)
    private PendingEvent? GetMajorTargetEvent()
    {
        var candidates = GetValidInfoCandidates();
        if (candidates.Count == 0) return null;

        // maxTurns가 가장 큰 이벤트들을 찾음
        int maxDuration = candidates.Max(e => e.maxTurns);
        var majors = candidates.Where(e => e.maxTurns == maxDuration).ToList();

        // 그 중에서 랜덤 하나 (동률일 경우)
        return majors[UnityEngine.Random.Range(0, majors.Count)];
    }

    // 👻 [신규] 브로커 정보만 샀는데 아무 행동도 안 했을 때 페널티 (턴이 넘어가면 부과)
    void ProcessBrokerNoBuyPenalty()
    {
        // 턴이 시작되었는데 직전 턴에 브로커 정보를 샀고 (wasLastInfoBroker),
        // 그 정보를 이용해 아무 주식도 사지 않았다면 (activeBrokerContract == null)
        // 이 시점은 매수 기회(OnTrade)를 놓친 것으로 간주하고 페널티를 부과합니다.
        if (wasLastInfoBroker && activeBrokerContract == null)
        {
            long penalty = (long)(player.GetTotalAsset() * 0.10f);
            
            // 🚨 주식을 강제 매도하여 페널티 금액을 확보하고 차감
            ForceLiquidateForCash(penalty);
            player.money -= penalty;
            
            wasLastInfoBroker = false; // 플래그 초기화
            Debug.Log($"👻 [브로커 페널티] 정보 이용 후 미행동 페널티! 총 자산의 10% ({penalty:N0}원) 부과.");
        }
    }

    #endregion

    #region UI Management

    void UpdateNewsUI(string text, Color color) { if (UIManager.I == null) return; UIManager.I.TrySetText(UI_GROUP_NEWS, "Txt_NewsTicker", text); UIManager.I.TrySetTextColor(UI_GROUP_NEWS, "Txt_NewsTicker", color); }

    void UpdateStockBoardUI()
    {
        if (UIManager.I == null) return;
        var displayList = DisplayedStocks;
        for (int i = 0; i < maxUISlots; i++)
        {
            if (i < displayList.Count)
            {
                RuntimeStock stock = displayList[i];
                int change = stock.GetChangeAmount();
                string sign = change > 0 ? "▲" : (change < 0 ? "▼" : "-");
                Color col = change > 0 ? new Color(1f, 0.3f, 0.3f) : (change < 0 ? new Color(0.3f, 0.5f, 1f) : Color.white);

                // 1. 최우선 호가 정보 가져오기
                // 매수 호가 잔량
                long bestBidAmount = stock.BuyOrders.Count > 0 ? stock.BuyOrders[0].amount : 0;
                // 매도 호가 잔량
                long     bestAskAmount = stock.SellOrders.Count > 0 ? stock.SellOrders[0].amount : 0;

                // 2. Name_{i} 텍스트 수정 (잔여 주식 대신 호가 잔량 표시)
                UIManager.I.TrySetText(UI_GROUP_BOARD, $"Name_{i}", 
                    $"{stock.data.stockName} <color=blue><b><size=60%>[{stock.data.symbol}]</size></b></color>\n" +
                    $"<size=70%><color=blue>매수: {bestBidAmount:N0}주</color> / <color=red>매도: {bestAskAmount:N0}주</color></size>"
                );

                // 3. 가격 및 변동률 (기존 유지)
                UIManager.I.TrySetText(UI_GROUP_BOARD, $"Price_{i}", $"{stock.currentPrice:N0}원");
                UIManager.I.TrySetText(UI_GROUP_BOARD, $"Change_{i}", $"{sign} {Mathf.Abs(change):N0} ({stock.GetChangePercent():F2}%)");
                UIManager.I.TrySetTextColor(UI_GROUP_BOARD, $"Change_{i}", col);
                UIManager.I.TrySetTextColor(UI_GROUP_BOARD, $"Price_{i}", col);
            }
            else
            {
                UIManager.I.TrySetText(UI_GROUP_BOARD, $"Name_{i}", ""); UIManager.I.TrySetText(UI_GROUP_BOARD, $"Price_{i}", ""); UIManager.I.TrySetText(UI_GROUP_BOARD, $"Change_{i}", "");
            }
        }
    }

    void UpdatePortfolioUI()
    {
        if (UIManager.I == null) return;
        
        long totalStockValue = 0;
        long totalDividend = 0;

        var myHoldings = player.GetHoldings();
        var holdingList = myHoldings.Keys.ToList();

        // 1. 보유 주식 UI 갱신
        for (int i = 0; i < maxUISlots; i++)
        {
            if (i < holdingList.Count)
            {
                StockData data = holdingList[i];
                long amount = myHoldings[data];
                RuntimeStock currentStock = marketStocks.Find(s => s.data == data);
                int price = (currentStock != null) ? currentStock.currentPrice : 0;
                long valuation = (long)price * amount;
                
                totalStockValue += valuation;
                if (data.dividendPerShare > 0) totalDividend += (long)data.dividendPerShare * amount;

                UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, $"MyName_{i}", data.stockName);
                UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, $"MyCount_{i}", $"{amount:N0}주");
                UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, $"MyValue_{i}", $"{valuation:N0}원");
            }
            else
            {
                UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, $"MyName_{i}", ""); 
                UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, $"MyCount_{i}", ""); 
                UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, $"MyValue_{i}", "");
            }
        }

        // 2. 공매도 부채 계산
        long totalShortDebt = 0;
        var shorts = player.GetShortPositions();
        foreach (var item in shorts)
        {
            RuntimeStock stock = marketStocks.Find(s => s.data == item.Key);
            if (stock != null) totalShortDebt += (long)stock.currentPrice * item.Value;
        }

        // 💰 [핵심 수정 1] 총 자산 계산에 국채(bondHoldings) 추가
        // 자산 = 현금 + 국채 + 증거금 + 보유주식 - 공매도부채 - 대출금
        long totalAsset = player.money + player.bondHoldings + player.lockedMargin + totalStockValue - totalShortDebt - player.currentDebt;
        
        // 💰 [핵심 수정 2] 현금 텍스트에 국채 보유액 함께 표시
        string cashText = $"{player.money:N0} 원";
        if (player.bondHoldings > 0)
        {
            // 예: "10,000 원 (국채: 5,000)"
            cashText += $"\n<size=80%><color=#666666>(국채: {player.bondHoldings:N0}원)</color></size>";
        }
        UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, "Txt_Cash", cashText);

        // 3. 주식 가치 및 배당/증거금 정보 표시
        string stockInfo = $"{totalStockValue:N0} 원";
        if (player.lockedMargin > 0) stockInfo += $"\n<size=80%>(증거금: {player.lockedMargin:N0})</size>";
        
        if (totalDividend - totalShortDebt >= 0)
            stockInfo += $"\n(<color=red>+{totalDividend - totalShortDebt:N0}원</color>)";
        else
            stockInfo += $"\n(<color=blue>{totalDividend - totalShortDebt:N0}원</color>)";

        UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, "Txt_StockVal", stockInfo);
        
        // 4. 총 자산 표시 (수정된 totalAsset 적용)
        UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, "Txt_TotalAsset", $"{totalAsset:N0} 원");
    }

    // 💰 [추가] Trade Panel UI에 호가 정보 표시 (UI 오브젝트가 필요합니다: Txt_BestBid, Txt_BestAsk)
    void UpdateTradePanelUI()
    {
        if (selectedStock == null) return;
        StockData data = selectedStock.data;
        string divStr = data.dividendPerShare > 0 ? $"{data.dividendPerShare:N0}원" : "0원";

        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_Title", $"{data.stockName}");
        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_Desc", data.description);
        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_StartPrice", $"초기 주가: {data.startPrice:N0}원");
        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_TotalShares", $"초기 물량: {data.totalShares:N0} <size=75%>(배당금: {divStr})</size>");

        // // 💡 [Tooltip] 주식 이름에 마우스를 올리면 기업 설명이 나옴
        // UIManager.I.TrySetTooltip(UI_GROUP_POPUP, "Txt_Title", 
        //     data.description + $"\n\n섹터: {data.sector}\n변동성: {data.volatility*100:F0}%", 
        //     "기업 상세 정보");
            
        // 💡 [Tooltip] 재무 정보 툴팁
        string divInfo = data.dividendPerShare > 0 ? $"매 턴 주당 {data.dividendPerShare}원 지급" : "배당 없음";
        UIManager.I.TrySetTooltip(UI_GROUP_POPUP, "Txt_TotalShares", 
            $"총 발행 주식: {data.totalShares:N0}주\n배당 정책: {divInfo}", 
            "재무 정보");

        // 1. 보유 주식(Long) 가져오기
        long myCount = player.GetStockCount(data);
        
        // 2. 공매도 주식(Short) 가져오기
        long myShortCount = player.GetShortCount(data);

        // 3. 평가 금액 (Long 포지션 기준)
        long val = (long)myCount * selectedStock.currentPrice;

        // 4. 공매도 표시 문자열 생성 (수량이 있으면 빨간색, 없으면 회색)
        string shortInfo = myShortCount > 0 
            ? $"<color=#FF6666>(공매도: {myShortCount:N0}주)</color>" 
            : $"<size=80%><color=black>(공매도: 0주)</color></size>";
        
        // 5. 호가 정보 (최우선 호가)
        long bestBidPrice = selectedStock.BuyOrders.Count > 0 ? selectedStock.BuyOrders[0].price : selectedStock.currentPrice;
        long bestBidAmount = selectedStock.BuyOrders.Count > 0 ? selectedStock.BuyOrders[0].amount : 0;
        long bestAskPrice = selectedStock.SellOrders.Count > 0 ? selectedStock.SellOrders[0].price : selectedStock.currentPrice;
        long bestAskAmount = selectedStock.SellOrders.Count > 0 ? selectedStock.SellOrders[0].amount : 0;

        string infoText = $"현재가: <color=yellow>{selectedStock.currentPrice:N0}원</color>\n" +
                        $"<color=blue>매수 호가</color>: {bestBidPrice:N0}원 ({bestBidAmount:N0}주)\n" +
                        $"<color=red>매도 호가</color>: {bestAskPrice:N0}원 ({bestAskAmount:N0}주)\n" +
                        $"보유: <color=green>{myCount:N0}주</color> {shortInfo}\n" +
                        $"가치: {val:N0}원<size=75%> (배당금: {((long)data.dividendPerShare * myCount):N0}원)</size>";
                          
        // ⚠️ 시장 충격 시뮬레이션
        long inputQty = UIManager.I.GetInputValueInt(UI_GROUP_POPUP, UI_NAME_INPUT_AMOUNT);
        if (inputQty > 0)
        {
            // 매도 기준(투매) 시뮬레이션
            List<Order> bids = selectedStock.BuyOrders;
            if (bids.Count > 0)
            {
                long startPrice = bids[0].price;
                long endPrice = startPrice;
                long remain = inputQty;
                
                // 가상으로 호가창 갉아먹기
                for (int i = 0; i < bids.Count; i++)
                {
                    if (remain <= 0) break;
                    long eat = Math.Min(remain, bids[i].amount);
                    endPrice = bids[i].price;
                    remain -= eat;
                }

                // 변동률 계산
                float slippage = (float)(endPrice - startPrice) / startPrice;
                
                string warnMsg = "";
                if (slippage < -0.03f) // -3% 이상 하락 시 경고
                {
                    warnMsg = $"\n<color=red><b>⚠️ [시장 충격 경고]</b>\n대량 매도 시 주가가 약 {slippage*100:F1}% 폭락할 수 있습니다.\n(예상 체결가: {endPrice:N0}원)</color>";
                }
                
                // 기존 Info 텍스트에 덧붙이기
                string originalText = UIManager.I.GetText(UI_GROUP_POPUP, "Txt_Info"); // (GetText 함수가 없다면 변수에 저장해뒀다 써야 함)
                // 편의상 위에서 만든 infoText 변수 뒤에 붙입니다.
                // UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_Info", infoText + warnMsg); 
                // 주의: 위쪽 코드의 infoText 변수를 수정해서 다시 SetText 해야 합니다.
            }
        }

        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_Info", infoText);

        // ... (슬라이더 및 기타 UI 로직 유지) ...
        long maxBuyable = 0;
        if (selectedStock.currentPrice > 0)
            // 매수 시, 최우선 매도 호가로 구매 가능하다고 가정
            maxBuyable = player.money / bestAskPrice; 

        long maxSellable = myCount;      
        long maxCoverable = myShortCount; 

        long sliderMax = Math.Max(maxBuyable, Math.Max(maxSellable, maxCoverable));
        
        if (sliderMax <= 0) sliderMax = 1;
        if (sliderMax > int.MaxValue) sliderMax = int.MaxValue;

        UIManager.I.TrySetSliderMinMax(UI_GROUP_POPUP, UI_NAME_SLIDER_AMOUNT, 1, sliderMax);
        
        long currentInput = UIManager.I.GetInputValueInt(UI_GROUP_POPUP, UI_NAME_INPUT_AMOUNT);
        if (currentInput > sliderMax)
        {
            currentInput = (int)sliderMax;
            UIManager.I.TrySetInputValue(UI_GROUP_POPUP, UI_NAME_INPUT_AMOUNT, currentInput.ToString());
        }
        
        UIManager.I.TrySetSliderValue(UI_GROUP_POPUP, UI_NAME_SLIDER_AMOUNT, currentInput);

        // 📈 그래프 업데이트 호출
        if (stockGraphUI != null && stockGraphUI.gameObject.activeInHierarchy)
        {
            // 🕯️ [수정] 캔들 히스토리 전달
            stockGraphUI.ShowCandleGraph(selectedStock.candleHistory);
        }
    }

    void UpdatePlayerMoneyUI() { UIManager.I.TrySetText(UI_GROUP_PLAYER, "Txt_Money", $"{player.money:N0}원"); }
    
    void OnSelectStock(int idx) 
    { 
        if (idx < DisplayedStocks.Count) 
        { 
            selectedStock = DisplayedStocks[idx]; 
            
            // 👇 [수정] 입력창 초기값을 0 -> 1 로 변경
            UIManager.I.TrySetInputValue(UI_GROUP_POPUP, UI_NAME_INPUT_AMOUNT, "1"); 
            
            ToggleTradePanel(true); 
            UpdateTradePanelUI(); // 여기서 슬라이더 Max 계산됨
        } 
    }

    void OnClickSector(StockSector s) { currentSectorFilter = s; selectedStock = null; ToggleTradePanel(false); UpdateStockBoardUI(); }
    void ToggleTradePanel(bool open) { UIManager.I.TrySetActive(UI_GROUP_POPUP, "Panel_Trade", open); if (!open) selectedStock = null; }
    void ToggleInfoPanel(bool open) { UIManager.I.TrySetActive(UI_GROUP_INFO, "Panel_CompanyInfo", open); }
    void OpenCompanyInfoPopup() { if (selectedStock != null) { UIManager.I.TrySetText(UI_GROUP_INFO, "Txt_Name", selectedStock.data.stockName); UIManager.I.TrySetText(UI_GROUP_INFO, "Txt_Desc", selectedStock.data.description); ToggleInfoPanel(true); } }

    #endregion

    #region Loan System

    void OnClickBorrow() { long amt = GetLoanInputAmount(); if (amt > 0 && player.BorrowMoney(amt)) { if (FloatingTextManager.I != null)
                FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, -amt); UpdateLoanPanelUI(); UpdatePlayerMoneyUI(); UpdatePortfolioUI(); } }
    void OnClickRepay() { long amt = GetLoanInputAmount(); if (amt > 0 && player.RepayMoney(amt)) { if (FloatingTextManager.I != null)
                FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, -amt); UpdateLoanPanelUI(); UpdatePlayerMoneyUI(); UpdatePortfolioUI(); } }
    long GetLoanInputAmount() { if (long.TryParse(UIManager.I.GetInputValue(UI_GROUP_LOAN, "Input_LoanAmount"), out long r)) return r; return 0; }
    void ToggleLoanPanel(bool open) { UIManager.I.TrySetActive(UI_GROUP_LOAN, "Panel_Loan", open); if (open) { UpdateLoanPanelUI(); UIManager.I.TrySetInputValue(UI_GROUP_LOAN, "Input_LoanAmount", ""); } }
    void UpdateLoanPanelUI()
    {
        // 현재 추가로 빌릴 수 있는 한도 (총 한도 - 현재 부채)
        long borrowableAmount = player.GetMaxLoanAmount();
        long currentDebt = player.currentDebt;

        // 🏦 [추가] 현재 적용 금리 가져오기
        float currentRatePercent = GetCurrentLoanRate() * 100f;

        string displayMsg = "";

        if (currentDebt > 0)
        {
            displayMsg = $"현재 부채: <color=red>{currentDebt:N0} 원</color>\n" +
                         $"추가 대출 가능: {borrowableAmount:N0} 원\n" +
                         $"<size=90%>적용 금리: <color=yellow>{currentRatePercent:F2}%</color> (기준 {baseInterestRate*100:F1}% + 가산 {bankMargin*100:F1}%)</size>";
        }
        else
        {
            displayMsg = $"<color=green>현재 대출 없음 (신용 양호)</color>\n\n" +
                         $"최대 대출 가능: <color=yellow>{borrowableAmount:N0} 원</color>\n" +
                         $"<size=90%>지금 빌리면 금리: <color=yellow>{currentRatePercent:F2}%</color></size>";
        }

        UIManager.I.TrySetText(UI_GROUP_LOAN, "Txt_LoanInfo", displayMsg);
    }
    #endregion
    #region Bond System

    // 상태 변수
    private bool isBondPanelOpen = false;

    // Inspector 연결용 (메인 버튼)
    public void ToggleBondPanel()
    {
        // 상태 뒤집기
        isBondPanelOpen = !isBondPanelOpen;
        SetBondPanelState(isBondPanelOpen);
    }

    // 내부 로직 및 닫기 버튼용
    public void SetBondPanelState(bool isOpen)
    {
        isBondPanelOpen = isOpen; // 변수 동기화 필수!
        
        if (UIManager.I.TrySetActive(UI_GROUP_BOND, "Panel_Bond", isOpen))
        {
            if (isOpen) UpdateBondPanelUI();
        }
    }

    void UpdateBondPanelUI()
    {
        if (player == null) return;

        // 정보 표시
        float ratePercent = baseInterestRate * 100f;
        // 다음 이자 지급까지 남은 턴
        int turnsLeft = rateUpdateInterval - currentRateTurn;
        // 예상 수익
        long expectedYield = (long)(player.bondHoldings * baseInterestRate);

        string infoText = $"현재 기준 금리: <color=yellow>{ratePercent:F2}%</color>\n" +
                          $"나의 국채 보유: <color=green>{player.bondHoldings:N0} 원</color>\n" +
                          $"다음 이자 지급: {turnsLeft}턴 후 (예상: +{expectedYield:N0}원)";

        UIManager.I.TrySetText(UI_GROUP_BOND, "Txt_BondInfo", infoText);
    }

    void OnClickBuyBond()
    {
        Debug.Log("[디버그] 매수 버튼 클릭됨"); // 버튼 연결 확인
        long amount = GetBondInputAmount();
        
        if (amount <= 0) 
        {
            Debug.LogWarning($"[디버그] 금액이 0이거나 음수라서 리턴됨. (Amount: {amount})");
            return;
        }

        if (player.money >= amount)
        {
            player.BuyBond(amount);
            // 💸 [FloatingText] 국채 매수 (지출)
            if (FloatingTextManager.I != null)
                FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, -amount);
            UpdateBondPanelUI();
            UpdatePlayerMoneyUI();
            UpdatePortfolioUI();
            player.SetLastAction($"<b>[국채 매수]</b> {amount:N0}원 (안전 자산 확보)");
            Debug.Log("[디버그] 매수 성공!");
        }
        else
        {
            Debug.LogWarning($"[디버그] 현금 부족 (보유: {player.money}, 필요: {amount})");
        }
    }

    // 채권 매도 버튼
    void OnClickSellBond()
    {
        long amount = GetBondInputAmount();
        if (amount <= 0) return;

        if (player.bondHoldings >= amount)
        {
            player.SellBond(amount);
            // 💸 [FloatingText] 국채 매수 (지출)
            if (FloatingTextManager.I != null)
                FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, amount);
            UpdateBondPanelUI();
            UpdatePlayerMoneyUI();
            UpdatePortfolioUI();
            player.SetLastAction($"<b>[국채 매도]</b> {amount:N0}원 (현금화)");
        }
        else
        {
            Debug.LogWarning("보유한 국채가 부족합니다.");
        }
    }

    long GetBondInputAmount()
    {
        // 1. UIManager에서 가져온 원본 문자열 확인
        string str = UIManager.I.GetInputValue(UI_GROUP_BOND, "Input_BondAmount");
        Debug.Log($"[디버그] Input_BondAmount 원본 값: '{str}'"); 

        // 2. 파싱 시도
        // 콤마(,)가 포함된 숫자(예: 10,000)를 처리하기 위해 NumberStyles 추가
        if (long.TryParse(str, System.Globalization.NumberStyles.Any, null, out long result)) 
        {
            Debug.Log($"[디버그] 숫자 변환 성공: {result}");
            return result;
        }
        
        Debug.LogWarning($"[디버그] 숫자 변환 실패 (0 반환됨). 원본: '{str}'");
        return 0;
    }

    #endregion

    #region Shadow Account (Cha-myung)

    // 🌑 [신규] 차명 계좌 UI 업데이트
    void UpdateShadowAccountUI()
    {
        if (player == null) return;

        // 한도: 실제 총 자산의 20%
        long realEquity = player.GetRealTotalEquity();
        long maxLimit = (long)(realEquity * 0.2f);
        long currentHidden = player.hiddenCash;
        long availableSpace = maxLimit - currentHidden;
        if (availableSpace < 0) availableSpace = 0;

        string infoText = $"<color=#AAAAAA>[차명 계좌]</color>\n" +
                          $"한도: {maxLimit:N0} 원 (총 자산의 20%)\n" +
                          $"잔고: <color=yellow>{currentHidden:N0} 원</color>\n" +
                          $"입금 가능: {availableSpace:N0} 원\n\n" +
                          $"<size=80%><color=red>* 주의: 출금 시 10% 수수료 발생</color></size>";

        // UIManager에 등록된 텍스트 이름이 "Txt_ShadowInfo"라고 가정
        UIManager.I.TrySetText(UI_GROUP_SHADOW, "Txt_ShadowInfo", infoText);
    }

    // 입금 버튼
    void OnClickDepositShadow()
    {
        long amount = GetShadowInputAmount();
        if (amount <= 0) return;

        // 1. 돈이 있는지 확인
        if (player.money < amount)
        {
            Debug.LogWarning("현금이 부족합니다.");
            return;
        }

        // 2. 한도 체크
        long realEquity = player.GetRealTotalEquity();
        long maxLimit = (long)(realEquity * 0.2f);
        long currentHidden = player.hiddenCash;

        if (currentHidden + amount > maxLimit)
        {
            Debug.LogWarning($"한도 초과! (최대 입금 가능: {maxLimit - currentHidden:N0}원)");
            // (선택) 한도까지만 입금해주려면: amount = maxLimit - currentHidden;
            return;
        }

        // 3. 입금 실행
        player.money -= amount;
        player.hiddenCash += amount;

        UpdateShadowAccountUI();
        UpdatePlayerMoneyUI();
        UpdatePortfolioUI(); // 총 자산(TotalAsset)이 줄어들게 됨 (은닉 성공)
        
        player.SetLastAction($"<b>[차명 입금]</b> {amount:N0}원 은닉 (자산 규모 축소)");
    }

    // 출금 버튼
    void OnClickWithdrawShadow()
    {
        long amount = GetShadowInputAmount();
        if (amount <= 0) return;

        // 1. 잔고 확인
        if (player.hiddenCash < amount)
        {
            Debug.LogWarning("차명 계좌 잔고가 부족합니다.");
            return;
        }

        // 2. 수수료 계산 (10%)
        long fee = (long)(amount * 0.1f);
        long finalReceive = amount - fee;

        // 3. 출금 실행
        player.hiddenCash -= amount;
        player.money += finalReceive;

        UpdateShadowAccountUI();
        UpdatePlayerMoneyUI();
        UpdatePortfolioUI(); // 현금으로 돌아왔으므로 총 자산 증가

        player.SetLastAction($"<b>[차명 출금]</b> {amount:N0}원 인출 (수수료 {fee:N0}원 차감)");
    }

    // InputField 값 파싱 (이름: Input_ShadowAmount)
    long GetShadowInputAmount()
    {
        string str = UIManager.I.GetInputValue(UI_GROUP_SHADOW, "Input_ShadowAmount");
        if (long.TryParse(str, System.Globalization.NumberStyles.Any, null, out long result))
        {
            return result;
        }
        return 0;
    }

    #endregion
    #region Private Loan (Sah-Chae)

    // 💀 UI 업데이트
    void UpdatePrivateLoanUI()
    {
        if (player == null) return;

        // 한도: 총 자산(순자산)의 5배
        long netEquity = player.GetTotalAsset(); 
        long maxLimit = netEquity * 5; 
        
        // 🛠️ [수정] 현재 빚에서 이자를 걷어내고 '원금'만 계산하여 가용액 확인
        // 현재 빚(privateDebt)은 원금의 1.5배이므로, 1.5로 나누면 원금이 나옴
        long currentPrincipalDebt = (long)(player.privateDebt / 1.5f);
        
        long available = maxLimit - currentPrincipalDebt; 
        if (available < 0) available = 0;

        string deadlineText = player.privateDebt > 0 
            ? $"<color=red>남은 기한: {player.privateDebtDeadline}턴</color>" 
            : "<color=green>대출 없음</color>";

        string infoText = $"<color=#FF0000><b>[사채 사무실]</b></color>\n" +
                          $"현재 빚: <color=red>{player.privateDebt:N0} 원</color> (이자 50% 포함)\n" +
                          $"대출 한도: {maxLimit:N0} 원 (자산 5배)\n" +
                          $"가능 금액: {available:N0} 원\n\n" +
                          $"{deadlineText}\n" +
                          $"<size=80%>* 주의: 10턴 내 미상환 시 즉시 <b>파산(Game Over)</b></size>";

        UIManager.I.TrySetText(UI_GROUP_PRIVATE, "Txt_PrivateInfo", infoText);
    }

    // 대출 버튼
    void OnClickBorrowPrivate()
    {
        long amount = GetPrivateInputAmount();
        if (amount <= 0) return;

        long netEquity = player.GetTotalAsset();
        long maxLimit = netEquity * 5;
        
        // 🛠️ [수정] 원금 기준으로 한도 체크
        long currentPrincipalDebt = (long)(player.privateDebt / 1.5f);

        if (currentPrincipalDebt + amount > maxLimit)
        {
            Debug.LogWarning($"사채 한도 초과! (가능액: {maxLimit - currentPrincipalDebt:N0}원)");
            // 한도 초과 시, 자동으로 최대 가능 금액으로 맞춰줄 수도 있음 (선택사항)
            // amount = maxLimit - currentPrincipalDebt;
            return;
        }

        player.BorrowPrivateLoan(amount);
        // 💸 [FloatingText] 국채 매수 (지출)
            if (FloatingTextManager.I != null)
                FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, amount);
        
        UpdatePrivateLoanUI();
        UpdatePlayerMoneyUI();
        UpdatePortfolioUI();
        
        player.SetLastAction($"<b>[사채 대출]</b> {amount:N0}원 (상환액 {amount*1.5f:N0}원)");
        Debug.Log($"💀 [사채] {amount:N0}원 대출 실행. (갚을 돈: {player.privateDebt:N0}원, 기한: 10턴)");
    }

    // 상환 버튼
    void OnClickRepayPrivate()
    {
        long amount = GetPrivateInputAmount();
        if (amount <= 0) return;

        if (player.money < amount)
        {
            Debug.LogWarning("상환할 돈이 부족합니다.");
            return;
        }

        if (player.RepayPrivateLoan(amount))
        {
            // 💸 [FloatingText] 국채 매수 (지출)
            if (FloatingTextManager.I != null)
                FloatingTextManager.I.ShowMoneyPopup(Input.mousePosition, -amount);
            UpdatePrivateLoanUI();
            UpdatePlayerMoneyUI();
            UpdatePortfolioUI();
            player.SetLastAction($"<b>[사채 상환]</b> {amount:N0}원 갚음");
        }
    }

    long GetPrivateInputAmount()
    {
        string str = UIManager.I.GetInputValue(UI_GROUP_PRIVATE, "Input_PrivateAmount");
        if (long.TryParse(str, System.Globalization.NumberStyles.Any, null, out long result))
        {
            return result;
        }
        return 0;
    }

    #endregion

    #region GameOver

    // 💀 [신규] 게임 오버 통합 처리 함수
    public void TriggerGameOver(string reason)
    {
        if (isGameOver) return; // 이미 게임 오버 상태면 무시
        isGameOver = true;

        Debug.Log($"💀 [GAME OVER] {reason}");

        // 1. UI 띄우기
        if (UIManager.I != null)
        {
            // 게임 오버 사유 텍스트 설정 (UI에 Txt_Reason이 있다고 가정)
            UIManager.I.TrySetText(UI_GROUP_GAMEOVER, "Txt_Reason", reason);
            UIManager.I.TrySetActive(UI_GROUP_GAMEOVER, "Panel_GameOver", true);
        }

        // 2. 종료 대기 코루틴 시작
        StartCoroutine(ShutdownGameRoutine());
    }

    // 🚪 [신규] 5초 후 게임 종료 코루틴
    IEnumerator ShutdownGameRoutine()
    {
        // 5초 카운트다운 (필요하다면 화면에 남은 시간 표시 가능)
        yield return new WaitForSeconds(5.0f);

        Debug.Log("🚪 프로그램 종료...");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // 에디터에서는 플레이 모드 중지
#else
        Application.Quit(); // 빌드된 게임에서는 프로그램 종료
#endif
    }

    #endregion
}