using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

#region Data Structures
// 📈 런타임 주식 객체 (실시간 데이터)
[System.Serializable]
public class RuntimeStock
{
    public StockData data;
    public int currentPrice;
    public int previousPrice;
    public int remainShares;
    public bool isDelisting;

    public RuntimeStock(StockData sourceData)
    {
        data = sourceData;
        currentPrice = sourceData.startPrice;
        previousPrice = sourceData.startPrice;
        remainShares = sourceData.totalShares;
        isDelisting = false;
    }

    public int GetChangeAmount() => currentPrice - previousPrice;
    public float GetChangePercent() => (previousPrice == 0) ? 0f : ((float)(currentPrice - previousPrice) / previousPrice) * 100f;
}

// 📖 시나리오 이벤트 정의 클래스
[System.Serializable]
public class ScenarioEvent
{
    public string title;
    public bool isGoodNews;
    public bool isMegaEvent; // 🌟 [신규] 초대형 이벤트 여부 (뉴스 강제 공개)
    public Dictionary<string, float> targets = new Dictionary<string, float>();

    public ScenarioEvent(string _title, bool _isGood, bool _isMega = false) // 생성자 수정
    {
        title = _title;
        isGoodNews = _isGood;
        isMegaEvent = _isMega;
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
    public int amount; 
    public long costBasis; // 1주당 구매 원가
}
#endregion

public class StockMarketManager : MonoBehaviour
{
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
    public int maxUISlots = 10;
    // 👇 [변경] 세금 주기와 이자율 분리
    public int taxIntervalTurns = 3; 
    public float shortInterestRate = 0.006f; // 기존 3턴 2% -> 1턴 0.6% (매턴 발생)
    private int currentTaxTurn = 0;
    [Range(1f, 50f)] public float impactSensitivity = 10.0f;

    [Header("Game Settings")]
    public long playerBankruptcyThreshold = 10000; // 플레이어 파산 기준

    [Header("Event Probabilities")]
    [Tooltip("시나리오(스토리)가 발생할 확률")] [Range(0f, 0.5f)] public float scenarioChance = 0.3f;
    [Tooltip("파급 효과가 발생할 확률")] [Range(0f, 0.3f)] public float rippleEffectChance = 0.15f;
    [Range(0f, 0.1f)] public float bankruptcyChance = 0.01f;
    [Range(0f, 0.5f)] public float listingChance = 0.1f;
    [Range(0f, 0.1f)] public float newsHackingChance = 0.05f;

    [Header("Macro Economy (Interest Rate)")]
    [Range(0.01f, 0.20f)] public float baseInterestRate = 0.03f; // 기준 금리 (기본 3%)
    public float bankMargin = 0.02f; // 은행 가산 금리 (2%)
    public int rateUpdateInterval = 20; // 20턴마다 금리 결정 회의
    private int currentRateTurn = 0;

    [Header("Information Trading Costs (Base)")]
    public long baseCostScammer = 2500;    // 사기꾼
    public long baseCostAnalyst = 100000;    // 분석가  
    public long baseCostHacker = 550000;    // 해커
    public long baseCostInsider = 2500000;  // 내부자
    public long baseCostSpy = 1000000;       // 스파이
    public long baseCostNewsboy = 50000;     // 신문팔이
    public long baseCostLobbyist = 750000;   // 로비스트
    public long baseCostBroker = 100000;      // 브로커

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

    // News Templates
    private readonly string[] bankruptcyNews = { "분식회계 적발!", "CEO 횡령 및 도주!", "최종 부도 처리!", "상장 폐지 결정!", "법정 관리 신청!" };
    private readonly string[] listingNews = { "IPO 대박 조짐!", "증권 시장 정식 상장!", "투자자들의 뜨거운 관심!", "거래 개시 카운트다운!" };
    private readonly string[] commonGoodNews = { "사상 최대 실적!", "외국인 대량 매수!", "신기술 특허 취득!", "파격 주주 환원!" };
    private readonly string[] commonBadNews = { "검찰 압수수색!", "부품 공급 중단!", "치명적 결함 리콜!", "어닝 쇼크!" };
    private Dictionary<StockSector, string[]> sectorGoodNews = new Dictionary<StockSector, string[]>();
    private Dictionary<StockSector, string[]> sectorBadNews = new Dictionary<StockSector, string[]>();

    // Event Structure
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
        public bool isSectorEvent;
        public bool isRippleEvent;
        public bool isHidden;
        public bool isMegaEvent; // 🌟 [신규]
    }
    private PendingEvent? currentEvent = null;
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

        UpdateStockBoardUI();
        UpdatePlayerMoneyUI();
        UpdatePortfolioUI();

        ToggleTradePanel(false);
        ToggleInfoPanel(false);

        UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_InfoTrading", false);

        UpdateNewsUI("시장이 개장했습니다.", Color.white);
        
        // 📉 시장 가격 변동 루프 (기존)
        StartCoroutine(UpdateMarketPrices());

        // 🔄 [신규] UI 자동 갱신 루프 (0.5초 주기)
        // 정보원 이용 등으로 돈이 빠져나갔을 때 포트폴리오 UI를 주기적으로 최신화합니다.
        StartCoroutine(UpdatePortfolioLoop());
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
    void InitializeScenarios()
    {
        scenarioDatabase.Clear();

        // ===============================================================================================================
        // =========================================================
        // 초대형 시나리오 20종 (Mega Event = true)
        // =========================================================
        // ===============================================================================================================

        // 1. AI 각성 (인류 멸망 위기)
        var aiAwakening = new ScenarioEvent("AI 시스템, 인간 통제 거부 선언! '스카이넷' 현실화?", false, true);
        aiAwakening.AddTarget("CSMC", 0.6f); aiAwakening.AddTarget("NEXS", 0.7f); aiAwakening.AddTarget("AEGS", 1.4f); aiAwakening.AddTarget("MIND", 0.5f);
        scenarioDatabase.Add(aiAwakening);

        // 2. 퀀텀 배터리 혁명 (산업 대격변)
        var battery = new ScenarioEvent("차세대 퀀텀 배터리 효율 500% 달성! 화석 연료 시대 종말!", true, true);
        battery.AddTarget("FLUX", 1.5f); battery.AddTarget("PRIO", 1.3f); battery.AddTarget("SKGL", 1.3f); battery.AddTarget("ZILS", 0.6f);
        scenarioDatabase.Add(battery);

        // 3. 우주 전쟁 위기 (화성 봉쇄)
        var blockade = new ScenarioEvent("화성 식민지 자치 선언! 지구-화성 무역 전면 봉쇄!", false, true);
        blockade.AddTarget("SHLD", 1.7f); blockade.AddTarget("TITN", 1.5f); blockade.AddTarget("VOID", 0.4f); blockade.AddTarget("TIMT", 1.4f);
        scenarioDatabase.Add(blockade);

        // 4. 노심 융해 (대재앙)
        var coreMeltdown = new ScenarioEvent("코어 퓨전 제2발전소 노심 융해(Meltdown)! 반경 100km 소멸 위기!", false, true);
        coreMeltdown.AddTarget("CORE", 0.05f); /*상장 폐지*/coreMeltdown.AddTarget("MAGM", 1.8f); coreMeltdown.AddTarget("SOLAR", 1.6f); coreMeltdown.AddTarget("GAIA", 1.5f);
        scenarioDatabase.Add(coreMeltdown);

        // 5. 불로장생 (인류의 꿈)
        var immortalityReal = new ScenarioEvent("크로노스 랩, '노화 역전 효소' 임상 3상 통과! 영생의 시대 개막!", true, true);
        immortalityReal.AddTarget("TIME", 2.5f); immortalityReal.AddTarget("NEO", 0.5f); immortalityReal.AddTarget("BIOS", 0.6f); immortalityReal.AddTarget("AMBR", 1.4f);
        scenarioDatabase.Add(immortalityReal);

        // 6. 블랙 스완 (금융 붕괴)
        var blackSwan = new ScenarioEvent("글로벌 금융 시스템 붕괴! '블랙 스완' 현실화, 전 세계 패닉!", false, true);
        blackSwan.AddTarget("CSMC", 0.7f); blackSwan.AddTarget("ZILS", 0.5f); blackSwan.AddTarget("PRIO", 0.5f); blackSwan.AddTarget("FNET", 0.05f);/*상장 폐지*/ blackSwan.AddTarget("BANK", 0.8f);
        scenarioDatabase.Add(blackSwan);

        // 7. 우주 엘리베이터 테러 (교통 마비)
        var spaceElevatorTerror = new ScenarioEvent("우주 엘리베이터 테러 발생! 케이블 절단으로 상부 스테이션 고립!", false, true);
        spaceElevatorTerror.AddTarget("GAIA", 0.4f); spaceElevatorTerror.AddTarget("TITN", 0.5f); spaceElevatorTerror.AddTarget("VOID", 1.8f); spaceElevatorTerror.AddTarget("SKGL", 1.5f);
        scenarioDatabase.Add(spaceElevatorTerror);

        // 8. 양자 해킹 (보안 붕괴)
        var quantumHack = new ScenarioEvent("기존 암호체계 뚫렸다! 양자 컴퓨터 해킹으로 전산망 마비!", false, true);
        quantumHack.AddTarget("BANK", 0.4f); quantumHack.AddTarget("FNET", 0.3f); quantumHack.AddTarget("AEGS", 2.0f); quantumHack.AddTarget("CSMC", 0.8f);
        scenarioDatabase.Add(quantumHack);

        // 9. 화성 수도 이전 (대형 호재)
        var marsCapital = new ScenarioEvent("지구 연방 정부, 수도를 화성 '아레스 시티'로 공식 이전 발표!", true, true);
        marsCapital.AddTarget("GAIA", 2.2f); marsCapital.AddTarget("VOID", 1.8f); marsCapital.AddTarget("SAIL", 1.5f); marsCapital.AddTarget("ORGN", 0.6f);
        scenarioDatabase.Add(marsCapital);

        // 10. 전쟁 범죄 (기업 몰락)
        var shieldGenocide = new ScenarioEvent("블랙쉴드 용병단, 민간인 학살 증거 전 세계 생중계! 국제 재판 회부!", false, true);
        shieldGenocide.AddTarget("SHLD", 0.05f); /*상장 폐지*/ shieldGenocide.AddTarget("NEXS", 1.4f); shieldGenocide.AddTarget("AEGS", 1.2f); shieldGenocide.AddTarget("ELIX", 0.7f);
        scenarioDatabase.Add(shieldGenocide);

        // 1. [IT/지배] 코즈믹 소프트의 세계 정복 (CSMC 호재)
        var csmcWorld = new ScenarioEvent("코즈믹 소프트, 전 우주 통합 OS '유니버스 1.0' 발표! 사실상 세계 정부?", true, true);
        csmcWorld.AddTarget("CSMC", 1.8f); // 주가 폭등
        csmcWorld.AddTarget("NEXS", 1.3f); // 유니버스 OS 탑재 필수
        csmcWorld.AddTarget("HEMS", 1.25f); // 통합 네트워크망 구축
        csmcWorld.AddTarget("AEGS", 1.2f); // 보안 파트너 독점
        csmcWorld.AddTarget("FNET", 0.6f); // 탈중앙화 세력 반발/규제 우려
        scenarioDatabase.Add(csmcWorld);

        // 2. [우주/교통] 워프 게이트 (대항해 시대)
        var warpGate = new ScenarioEvent("화성-목성 간 '초광속 워프 게이트' 실험 성공! 은하계 대항해 시대 개막!", true, true);
        warpGate.AddTarget("TITN", 1.7f); // 게이트 구조물 건설
        warpGate.AddTarget("VOID", 1.6f); // 원거리 운송 혁명
        warpGate.AddTarget("SAIL", 1.4f); // 장거리 여행 수요
        warpGate.AddTarget("ZILS", 1.3f); // 워프 연료(특수 자원) 채굴
        warpGate.AddTarget("SKGL", 0.8f); // 도심 교통 소외
        scenarioDatabase.Add(warpGate);

        // 3. [바이오/혁명] 완벽한 게놈 (유전병 종말)
        var perfectGenome = new ScenarioEvent("인류 게놈 지도 완벽 해독 및 편집 기술 무료 배포! 유전병의 종말!", true, true);
        perfectGenome.AddTarget("NEO", 1.8f); // 유전자 가위 기술 폭주
        perfectGenome.AddTarget("ILIA", 0.7f); // 기존 치료제 수요 급감
        perfectGenome.AddTarget("BIOS", 0.6f); // 장기 이식 필요성 감소
        perfectGenome.AddTarget("AMBR", 1.3f); // 건강해진 인류의 식욕 폭발
        scenarioDatabase.Add(perfectGenome);

        // 4. [환경/기적] 에덴 프로젝트 (지구 부활)
        var edenProject = new ScenarioEvent("대기 정화 나노봇 살포 성공! 지구의 하늘이 100년 만에 파랗게 변했다!", true, true);
        edenProject.AddTarget("ORGA", 1.9f); // 노지 농업 부활 (초대박)
        edenProject.AddTarget("GLAB", 0.5f); // 맛없는 대체 식량 폐기
        edenProject.AddTarget("GAIA", 1.5f); // 프로젝트 주관사
        edenProject.AddTarget("BLUE", 0.8f); // 해양 거주 메리트 감소
        scenarioDatabase.Add(edenProject);

        // 5. [에너지/발견] 반물질 제어 (무한 에너지)
        var antiMatter = new ScenarioEvent("극소량의 반물질 안정적 제어 성공! 배터리 하나로 100년 쓴다?", true, true);
        antiMatter.AddTarget("CORE", 1.8f); // 기술 보유
        antiMatter.AddTarget("FLUX", 0.5f); // 배터리 교체 수요 소멸 (악재)
        antiMatter.AddTarget("SOLAR", 0.6f); // 태양광 효율성 논란
        antiMatter.AddTarget("TITN", 1.4f); // 반물질 엔진 전함
        scenarioDatabase.Add(antiMatter);

        // 1. [초대형] 시간 여행 (타임 패러독스)
        var timeRift = new ScenarioEvent("크로노스 랩, 미세 '시간 균열' 관측 성공! 과거로의 메시지 전송?", true, true);
        timeRift.AddTarget("TIME", 3.0f);  // 주가 폭발 (꿈의 기술)
        timeRift.AddTarget("DATA", 0.5f);  // 미래 데이터의 가치가 무의미해짐 (정보 붕괴)
        timeRift.AddTarget("BANK", 0.6f);  // 이자/대출 시스템 붕괴 우려
        timeRift.AddTarget("ARCD", 1.5f);  // '과거'에 대한 향수와 관심 폭발
        scenarioDatabase.Add(timeRift);

        // 2. [초대형] 외계 문명 조우 (공식 수교)
        var firstContact = new ScenarioEvent("외계 문명 '제타', 지구 연방에 공식 수교 요청! 은하계 무역 시대!", true, true);
        firstContact.AddTarget("VOID", 2.0f);  // 성간 무역 독점 기대
        firstContact.AddTarget("HEMS", 1.8f);  // 외계 통신 프로토콜 개발
        firstContact.AddTarget("SHLD", 0.4f);  // 평화 모드로 방산주 폭락
        firstContact.AddTarget("DUST", 1.5f);  // 외계인이 지구 디저트에 환장함 (문화 승리)
        scenarioDatabase.Add(firstContact);

        // 3. [초대형] 지구 자기장 역전
        var poleShift = new ScenarioEvent("지구 자기장 역전(Pole Shift) 현상 시작! 전자기기 먹통 대란!", false, true);
        poleShift.AddTarget("ORGN", 2.5f);  // 전자장비 없는 아날로그 차량 유일한 이동수단
        poleShift.AddTarget("SKGL", 0.05f);  // 비행 제어 불능 (상장 폐지)
        poleShift.AddTarget("HEMS", 0.05f);  // 통신 두절 (상장 페지)
        poleShift.AddTarget("ORGA", 1.5f);  // 스마트팜 정전 -> 노지 농사 귀환
        scenarioDatabase.Add(poleShift);

        // 4. [초대형] 인공지능 신비주의 (기계교)
        var machineGod = new ScenarioEvent("넥서스 봇의 AI, 스스로를 '신'으로 선포! 추종자 수억 명 발생!", false, true);
        machineGod.AddTarget("NEXS", 1.8f);  // 광신도들의 매수세
        machineGod.AddTarget("CSMC", 1.5f);  // 성서(OS) 제작
        machineGod.AddTarget("NEO", 0.3f);   // 기계가 우월하므로 인간 개조는 신성 모독 취급
        machineGod.AddTarget("AURA", 1.4f);  // 종교 방송 독점
        scenarioDatabase.Add(machineGod);

        // 5. [초대형] 해수면 급상승 (워터 월드)
        var waterWorld = new ScenarioEvent("남극 빙하 완전 붕괴! 해수면 10m 상승, 해안 도시 수몰!", false, true);
        waterWorld.AddTarget("BLUE", 2.2f);  // 바다가 곧 영토
        waterWorld.AddTarget("GAIA", 1.7f);  // 해상 도시 건설 특수
        waterWorld.AddTarget("SAIL", 1.6f);  // 요트가 주거지가 됨
        waterWorld.AddTarget("ORGA", 0.3f);  // 농경지 침수
        scenarioDatabase.Add(waterWorld);

        // ===============================================================================================================
        // =========================================================
        // 일반 시나리오 110종 (Mega Event = true)
        // =========================================================
        // ===============================================================================================================

        // ==========================================
        // 1. 기술 및 산업 혁명 (Tech & Industry)
        // ==========================================

        var space = new ScenarioEvent("화성 탐사 로봇, 초대형 희토류 광맥 발견! '우주 골드러시'!", true);
        space.AddTarget("LUNA", 1.4f); // 2.2 -> 1.4
        space.AddTarget("VOID", 1.25f); 
        space.AddTarget("ZILS", 1.2f); 
        space.AddTarget("SAIL", 1.1f);
        scenarioDatabase.Add(space);

        var fullDive = new ScenarioEvent("뇌파 연결 '풀다이브 VR' 상용화 성공! 현실을 넘어선다!", true);
        fullDive.AddTarget("MIND", 1.35f); 
        fullDive.AddTarget("FANT", 1.3f); 
        fullDive.AddTarget("Vlive", 1.2f); 
        fullDive.AddTarget("CRCK", 1.15f);
        scenarioDatabase.Add(fullDive);

        // ==========================================
        // 2. 사회 및 윤리 (Society & Ethics)
        // ==========================================
        var ethics = new ScenarioEvent("충격! 불법 인체 실험 내부 고발! '윤리 논란' 일파만파!", false);
        ethics.AddTarget("NEO", 0.6f); // 윤리 문제는 타격이 큼 (0.3 -> 0.6)
        ethics.AddTarget("MIND", 0.7f); 
        ethics.AddTarget("ILIA", 0.85f); 
        ethics.AddTarget("CSMC", 0.9f);
        scenarioDatabase.Add(ethics);

        var hacking = new ScenarioEvent("사상 최악의 랜섬웨어 전 세계 강타! IT 인프라 마비!", false);
        hacking.AddTarget("AEGS", 1.4f); // 보안 기업 호재
        hacking.AddTarget("DATA", 0.75f); 
        hacking.AddTarget("FNET", 0.8f); 
        hacking.AddTarget("Vlive", 0.85f);
        scenarioDatabase.Add(hacking);

        var luxuryBoom = new ScenarioEvent("부의 양극화 심화... '초호화 럭셔리 시장' 나홀로 호황!", true);
        luxuryBoom.AddTarget("AMBR", 1.25f); 
        luxuryBoom.AddTarget("SAIL", 1.2f); 
        luxuryBoom.AddTarget("TIMT", 0.95f); 
        luxuryBoom.AddTarget("PIXEL", 0.9f);
        scenarioDatabase.Add(luxuryBoom);

        var robotTax = new ScenarioEvent("정부, 일자리 보호 위해 '로봇세' 도입 추진!", false);
        robotTax.AddTarget("NEXS", 0.8f); 
        robotTax.AddTarget("PRIO", 0.85f); 
        robotTax.AddTarget("LUNA", 0.9f); 
        robotTax.AddTarget("CSMC", 0.9f);
        scenarioDatabase.Add(robotTax);

        // ==========================================
        // 3. 환경 및 재난 (Environment)
        // ==========================================
        var foodCrisis = new ScenarioEvent("이상 기후로 전 세계 작물 수확량 급감! 식량 안보 비상!", false);
        foodCrisis.AddTarget("ORGA", 0.7f); // 흉작
        foodCrisis.AddTarget("GLAB", 1.35f); // 대체육/스마트팜 호재
        foodCrisis.AddTarget("BLUE", 1.2f); 
        foodCrisis.AddTarget("TIMT", 1.25f); // 비상식량
        scenarioDatabase.Add(foodCrisis);

        var nuclear = new ScenarioEvent("코어 퓨전 실험로 미세 균열 감지! 방사능 유출 공포!", false);
        nuclear.AddTarget("CORE", 0.4f); // 0.2 -> 0.4 (여전히 치명적이지만 즉시 상폐급은 면함)
        nuclear.AddTarget("MAGM", 1.2f); 
        nuclear.AddTarget("SOLAR", 1.15f); 
        nuclear.AddTarget("ZILS", 1.1f);
        scenarioDatabase.Add(nuclear);

        var solarFlare = new ScenarioEvent("초강력 태양 폭발 경보! 우주 여행 전면 금지!", false);
        solarFlare.AddTarget("SAIL", 0.6f); 
        solarFlare.AddTarget("VOID", 0.8f); 
        solarFlare.AddTarget("FANT", 1.2f); // 집에서 게임이나 하자
        scenarioDatabase.Add(solarFlare);

        var pandemic = new ScenarioEvent("신종 바이러스 확산 조짐! 전 세계가 긴장!", false);
        pandemic.AddTarget("ILIA", 1.5f); // 2.2 -> 1.5
        pandemic.AddTarget("Vlive", 1.25f); 
        pandemic.AddTarget("ORGN", 0.8f); 
        pandemic.AddTarget("SKGL", 0.75f);
        scenarioDatabase.Add(pandemic);

        // ==========================================
        // 4. 문화 및 트렌드 (Culture)
        // ==========================================
        var retro = new ScenarioEvent("디지털 피로감 확산... '아날로그와 클래식'의 귀환!", true);
        retro.AddTarget("ARCD", 1.4f); 
        retro.AddTarget("ORGN", 1.2f); 
        retro.AddTarget("ORGA", 1.15f); 
        retro.AddTarget("Vlive", 0.85f);
        scenarioDatabase.Add(retro);

        var veganTrend = new ScenarioEvent("MZ세대 중심 '가치 소비' 확산! 대체육 시장 급성장!", true);
        veganTrend.AddTarget("GLAB", 1.3f); 
        veganTrend.AddTarget("AMBR", 0.85f); 
        veganTrend.AddTarget("SOLAR", 1.1f);
        scenarioDatabase.Add(veganTrend);

        var metaConcert = new ScenarioEvent("가상 아이돌 콘서트 접속자 5억 명 돌파! 엔터 산업 지각변동!", true);
        metaConcert.AddTarget("Vlive", 1.45f); 
        metaConcert.AddTarget("ARCD", 0.9f); 
        metaConcert.AddTarget("PIXEL", 1.15f);
        scenarioDatabase.Add(metaConcert);

        // ==========================================
        // 5. 경제 및 정책 (Economy)
        // ==========================================
        var cryptoCrash = new ScenarioEvent("주요 가상화폐 거래소 뱅크런! 코인 시장 붕괴!", false);
        cryptoCrash.AddTarget("PIXEL", 0.7f); 
        cryptoCrash.AddTarget("FNET", 0.65f); 
        cryptoCrash.AddTarget("CORE", 0.9f); 
        cryptoCrash.AddTarget("TIMT", 1.05f);
        scenarioDatabase.Add(cryptoCrash);

        var spaceFund = new ScenarioEvent("정부, '제2의 지구' 찾기에 100조 원 투자 발표!", true);
        spaceFund.AddTarget("VOID", 1.3f); 
        spaceFund.AddTarget("LUNA", 1.25f); 
        spaceFund.AddTarget("SKGL", 1.15f);
        scenarioDatabase.Add(spaceFund);

        var lowRate = new ScenarioEvent("기준 금리 0%대로 인하! 시장에 유동성 공급 폭탄!", true);
        lowRate.AddTarget("FNET", 1.25f); 
        lowRate.AddTarget("CRCK", 1.2f); 
        lowRate.AddTarget("AEGS", 1.15f); 
        lowRate.AddTarget("TIMT", 0.95f);
        scenarioDatabase.Add(lowRate);

        // ==========================================
        // 6. 기업 간 알력 (Competition)
        // ==========================================
        var patentWar = new ScenarioEvent("일리아 바이오 vs 네오 진, 세기의 유전자 특허 소송 개시!", false);
        patentWar.AddTarget("ILIA", 0.9f); 
        patentWar.AddTarget("NEO", 0.85f); 
        patentWar.AddTarget("TIME", 1.1f); // 경쟁사 반사이익
        scenarioDatabase.Add(patentWar);

        var merger = new ScenarioEvent("코즈믹 소프트, 데이터 마이닝 인수 합병설 솔솔! '초거대 공룡' 탄생하나?", true);
        merger.AddTarget("DATA", 1.35f); // 피인수 기업 급등
        merger.AddTarget("CSMC", 0.95f); // 인수 비용 부담
        merger.AddTarget("AEGS", 0.9f); // 독점 우려
        scenarioDatabase.Add(merger);

        var smartCity = new ScenarioEvent("정부-기업 연합, 사막에 최첨단 '네오 서울' 건설 착수!", true);
        smartCity.AddTarget("MAGM", 1.2f); 
        smartCity.AddTarget("SKGL", 1.25f); 
        smartCity.AddTarget("AEGS", 1.15f); 
        smartCity.AddTarget("GLAB", 1.1f);
        scenarioDatabase.Add(smartCity);

        // ==========================================
        // 7. 엑스트라 및 신규 확장
        // ==========================================
        var seaResource = new ScenarioEvent("심해 양식장 바닥에서 미지의 에너지 광물 발견!", true);
        seaResource.AddTarget("BLUE", 1.5f); // 초대형 호재는 1.5배 수준 유지
        seaResource.AddTarget("ZILS", 0.9f);
        scenarioDatabase.Add(seaResource);

        var esport = new ScenarioEvent("VR 게임, 올림픽 정식 종목 채택! 전 세계 게이머 열광!", true);
        esport.AddTarget("CRCK", 1.3f); 
        esport.AddTarget("FANT", 1.25f); 
        esport.AddTarget("PIXEL", 1.1f);
        scenarioDatabase.Add(esport);

        var fakeFood = new ScenarioEvent("합성 식량 장기 섭취 시, 원인 불명 질병 발생 보고!", false);
        fakeFood.AddTarget("GLAB", 0.6f); 
        fakeFood.AddTarget("TIMT", 0.85f); 
        fakeFood.AddTarget("ORGA", 1.3f); // 유기농 떡상
        fakeFood.AddTarget("AMBR", 1.2f);
        scenarioDatabase.Add(fakeFood);

        var tunnelCrash = new ScenarioEvent("대륙간 하이퍼루프 터널 붕괴 사고! 물류 대란 발생!", false);
        tunnelCrash.AddTarget("VOID", 1.25f); // 우주 운송 반사이익
        tunnelCrash.AddTarget("SKGL", 1.15f); 
        tunnelCrash.AddTarget("ORGN", 0.85f);
        scenarioDatabase.Add(tunnelCrash);

        var botError = new ScenarioEvent("가정용 안드로이드 동시다발적 오작동 사태! 소비자들 공포!", false);
        botError.AddTarget("NEXS", 0.65f); 
        botError.AddTarget("CSMC", 0.8f); 
        botError.AddTarget("AEGS", 1.2f);
        scenarioDatabase.Add(botError);

        var alienSignal = new ScenarioEvent("심우주에서 규칙적인 전파 신호 포착! 외계 문명인가?", true);
        alienSignal.AddTarget("VOID", 1.2f); 
        alienSignal.AddTarget("SAIL", 1.25f); 
        alienSignal.AddTarget("LUNA", 1.15f);
        scenarioDatabase.Add(alienSignal);

        var artificialSun = new ScenarioEvent("K-STAR 인공 태양, 1억 도 유지 시간 신기록 경신!", true);
        artificialSun.AddTarget("CORE", 1.4f); 
        artificialSun.AddTarget("FLUX", 1.15f); 
        artificialSun.AddTarget("SOLAR", 0.9f);
        scenarioDatabase.Add(artificialSun);

        var immortalFail = new ScenarioEvent("크로노스 랩 '영생 프로젝트' 최종 실패 선언! 주가 곤두박질!", false);
        immortalFail.AddTarget("TIME", 0.05f); // 상장 폐지
        immortalFail.AddTarget("NEO", 1.15f);
        scenarioDatabase.Add(immortalFail);

        var analogTrend = new ScenarioEvent("22세기에도 식지 않는 '아날로그 감성' 열풍!", true);
        analogTrend.AddTarget("ORGN", 1.15f); 
        analogTrend.AddTarget("ARCD", 1.2f); 
        analogTrend.AddTarget("MIND", 0.95f);
        scenarioDatabase.Add(analogTrend);

        // ==========================================
        // 8. 추가 확장 (30종) - 밸런스 조정 완료
        // ==========================================

        var rateHike = new ScenarioEvent("중앙은행, 물가 잡기 위해 기준 금리 기습 인상 단행!", false);
        rateHike.AddTarget("BANK", 1.3f); // 은행은 금리 인상 호재
        rateHike.AddTarget("FNET", 0.8f); 
        rateHike.AddTarget("ZILS", 0.9f); 
        rateHike.AddTarget("GAIA", 0.85f);
        scenarioDatabase.Add(rateHike);

        var borderWar = new ScenarioEvent("제7구역 국경 분쟁 격화! 전면전 위기 고조!", false);
        borderWar.AddTarget("SHLD", 1.5f); // 방산주 호재
        borderWar.AddTarget("NEXS", 1.25f); 
        borderWar.AddTarget("ELIX", 1.2f); 
        borderWar.AddTarget("TIMT", 1.15f); 
        borderWar.AddTarget("AURA", 1.1f); 
        borderWar.AddTarget("FANT", 0.8f);
        scenarioDatabase.Add(borderWar);

        var terraformSuccess = new ScenarioEvent("가이아 건설, 화성 대기 안정화 성공! '제2의 지구' 눈앞!", true);
        terraformSuccess.AddTarget("GAIA", 1.45f); 
        terraformSuccess.AddTarget("GLAB", 1.2f); 
        terraformSuccess.AddTarget("ZILS", 1.15f); 
        terraformSuccess.AddTarget("IRON", 1.2f);
        scenarioDatabase.Add(terraformSuccess);

        var drugScandal = new ScenarioEvent("국민 아이돌, 엘릭서 팜 진통제 불법 투약 혐의 입건!", false);
        drugScandal.AddTarget("ELIX", 0.75f); 
        drugScandal.AddTarget("AURA", 1.2f); // 특종
        drugScandal.AddTarget("Vlive", 0.9f);
        scenarioDatabase.Add(drugScandal);

        var miningDisaster = new ScenarioEvent("아이언 윌 채굴 로봇 오작동, 소행성 광산 붕괴 참사!", false);
        miningDisaster.AddTarget("IRON", 0.7f); 
        miningDisaster.AddTarget("ZILS", 0.85f); 
        miningDisaster.AddTarget("VOID", 0.9f); 
        miningDisaster.AddTarget("AEGS", 1.1f);
        scenarioDatabase.Add(miningDisaster);

        var spaceFoodFad = new ScenarioEvent("'스타 더스트' 우주 빙수, 전 은하계 MZ세대 입맛 사로잡다!", true);
        spaceFoodFad.AddTarget("DUST", 1.4f); 
        spaceFoodFad.AddTarget("GLAB", 1.1f); 
        spaceFoodFad.AddTarget("PIXEL", 1.1f); 
        spaceFoodFad.AddTarget("AMBR", 0.95f);
        scenarioDatabase.Add(spaceFoodFad);

        var pirateAttack = new ScenarioEvent("악명 높은 '검은 수염' 해적단, 주요 무역 항로 약탈!", false);
        pirateAttack.AddTarget("VOID", 0.8f); 
        pirateAttack.AddTarget("SHLD", 1.3f); 
        pirateAttack.AddTarget("TITN", 1.1f); 
        pirateAttack.AddTarget("HEMS", 0.95f);
        scenarioDatabase.Add(pirateAttack);

        var fakeNews = new ScenarioEvent("오로라 미디어, 홀로그램 뉴스 조작 의혹! '신뢰도 추락'!", false);
        fakeNews.AddTarget("AURA", 0.7f); 
        fakeNews.AddTarget("DATA", 1.15f); 
        fakeNews.AddTarget("CSMC", 0.95f);
        scenarioDatabase.Add(fakeNews);

        var cryptoBill = new ScenarioEvent("은하 연방, 모든 상거래에 '디지털 코인' 결제 의무화 추진!", true);
        cryptoBill.AddTarget("FNET", 1.4f); 
        cryptoBill.AddTarget("PIXEL", 1.3f); 
        cryptoBill.AddTarget("BANK", 0.8f);
        scenarioDatabase.Add(cryptoBill);

        var brainHack = new ScenarioEvent("마인드 링크 사용자들 집단 기억 조작 증세! 해킹 의심!", false);
        brainHack.AddTarget("MIND", 0.6f); 
        brainHack.AddTarget("NEO", 0.8f); 
        brainHack.AddTarget("AEGS", 1.3f);
        scenarioDatabase.Add(brainHack);

        var warshipOrder = new ScenarioEvent("지구 연합군, 타이탄 중공업에 차세대 초대형 전함 발주!", true);
        warshipOrder.AddTarget("TITN", 1.4f); 
        warshipOrder.AddTarget("ZILS", 1.15f); 
        warshipOrder.AddTarget("MAGM", 1.1f);
        scenarioDatabase.Add(warshipOrder);

        var organBlackmarket = new ScenarioEvent("바이오 스피어 인공 장기, 암시장에서 불법 유통 정황 포착!", false);
        organBlackmarket.AddTarget("BIOS", 0.65f); 
        organBlackmarket.AddTarget("NEO", 1.2f); 
        organBlackmarket.AddTarget("TIME", 0.9f);
        scenarioDatabase.Add(organBlackmarket);

        var retroChamps = new ScenarioEvent("아케이드 X 주최 '우주 레트로 게임 챔피언십' 시청률 대박!", true);
        retroChamps.AddTarget("ARCD", 1.35f); 
        retroChamps.AddTarget("DUST", 1.15f); 
        retroChamps.AddTarget("AURA", 1.1f); 
        retroChamps.AddTarget("CRCK", 0.95f);
        scenarioDatabase.Add(retroChamps);

        var commBlackout = new ScenarioEvent("초강력 태양 흑점 폭발! 헤르메스 통신망 일시 마비!", false);
        commBlackout.AddTarget("HEMS", 0.75f); 
        commBlackout.AddTarget("VOID", 0.85f); 
        commBlackout.AddTarget("PRIO", 0.9f); 
        commBlackout.AddTarget("LUNA", 0.8f);
        scenarioDatabase.Add(commBlackout);

        var luxuryTax = new ScenarioEvent("의회, 민간 우주 여행에 50% '부유세' 부과 법안 통과!", false);
        luxuryTax.AddTarget("SAIL", 0.7f); 
        luxuryTax.AddTarget("AMBR", 0.8f); 
        luxuryTax.AddTarget("TITN", 0.9f);
        scenarioDatabase.Add(luxuryTax);

        var robotAccident = new ScenarioEvent("넥서스 봇 오작동으로 건설 현장 붕괴! 안정성 논란!", false);
        robotAccident.AddTarget("NEXS", 0.75f); 
        robotAccident.AddTarget("GAIA", 0.9f); 
        robotAccident.AddTarget("IRON", 1.1f);
        scenarioDatabase.Add(robotAccident);

        var magmaExpansion = new ScenarioEvent("마그마 썸, 금성 표면에 초대형 지열 발전소 완공!", true);
        magmaExpansion.AddTarget("MAGM", 1.35f); 
        magmaExpansion.AddTarget("TITN", 1.1f); 
        magmaExpansion.AddTarget("CORE", 0.95f);
        scenarioDatabase.Add(magmaExpansion);

        var organicTrend = new ScenarioEvent("인플루언서들 사이에서 '진짜 흙, 진짜 음식' 챌린지 유행!", true);
        organicTrend.AddTarget("ORGA", 1.4f); 
        organicTrend.AddTarget("GLAB", 0.85f); 
        organicTrend.AddTarget("AMBR", 1.1f);
        scenarioDatabase.Add(organicTrend);

        var quantumSec = new ScenarioEvent("이지스 시스템, 해킹 불가능한 '양자 방패' 프로토콜 개발!", true);
        quantumSec.AddTarget("AEGS", 1.3f); 
        quantumSec.AddTarget("BANK", 1.15f); 
        quantumSec.AddTarget("HEMS", 1.1f);
        scenarioDatabase.Add(quantumSec);

        var oceanCleanup = new ScenarioEvent("지구 연합, 전 지구적 해양 정화 프로젝트 '블루 어스' 가동!", true);
        oceanCleanup.AddTarget("BLUE", 1.3f); 
        oceanCleanup.AddTarget("LUNA", 1.15f); 
        oceanCleanup.AddTarget("GLAB", 0.95f);
        scenarioDatabase.Add(oceanCleanup);

        var alienArtifact2 = new ScenarioEvent("루나 로버 탐사대, 달 뒷면에서 '검은 비석' 발견!", true);
        alienArtifact2.AddTarget("LUNA", 1.3f); 
        alienArtifact2.AddTarget("VOID", 1.2f); 
        alienArtifact2.AddTarget("SAIL", 1.15f); 
        alienArtifact2.AddTarget("AURA", 1.2f);
        scenarioDatabase.Add(alienArtifact2);

        var superVirus = new ScenarioEvent("기존 항생제가 듣지 않는 슈퍼 박테리아 확산!", false);
        superVirus.AddTarget("ILIA", 0.8f); 
        superVirus.AddTarget("TIMT", 1.2f); 
        superVirus.AddTarget("FANT", 1.15f); 
        superVirus.AddTarget("BIOS", 1.1f);
        scenarioDatabase.Add(superVirus);

        var aiRights = new ScenarioEvent("의회, '자율 AI 인권법' 통과! 로봇 노동 비용 급증 예상!", false);
        aiRights.AddTarget("NEXS", 0.75f); 
        aiRights.AddTarget("CSMC", 0.85f); 
        aiRights.AddTarget("IRON", 1.15f);
        scenarioDatabase.Add(aiRights);

        var resourceCrisis = new ScenarioEvent("질리아스 에너지, 화성 제3광구 자원 고갈 공식 선언!", false);
        resourceCrisis.AddTarget("ZILS", 0.8f); 
        resourceCrisis.AddTarget("IRON", 0.85f); 
        resourceCrisis.AddTarget("LUNA", 1.2f); 
        resourceCrisis.AddTarget("FLUX", 0.9f);
        scenarioDatabase.Add(resourceCrisis);

        var bettingLegal = new ScenarioEvent("은하 연방, E-스포츠 승부 예측 베팅 전면 합법화!", true);
        bettingLegal.AddTarget("CRCK", 1.25f); 
        bettingLegal.AddTarget("AURA", 1.3f); 
        bettingLegal.AddTarget("PIXEL", 1.2f); 
        bettingLegal.AddTarget("BANK", 1.1f);
        scenarioDatabase.Add(bettingLegal);

        var hyperloop = new ScenarioEvent("지구 전역을 잇는 진공 하이퍼루프망 개통! 서울-뉴욕 2시간!", true);
        hyperloop.AddTarget("SKGL", 0.9f); 
        hyperloop.AddTarget("ORGN", 0.85f); 
        hyperloop.AddTarget("GAIA", 1.2f);
        scenarioDatabase.Add(hyperloop);

        var mindUpload = new ScenarioEvent("마인드 링크, 기억을 서버에 저장하는 '마인드 클라우드' 베타 오픈!", true);
        mindUpload.AddTarget("MIND", 1.3f); 
        mindUpload.AddTarget("TIME", 0.75f); 
        mindUpload.AddTarget("DATA", 1.25f); 
        mindUpload.AddTarget("NEO", 1.1f);
        scenarioDatabase.Add(mindUpload);

        var kesslerSyndrome = new ScenarioEvent("위성 충돌로 우주 파편 연쇄 폭발! 저궤도 봉쇄!", false);
        kesslerSyndrome.AddTarget("HEMS", 0.5f); // 통신 두절 (재앙)
        kesslerSyndrome.AddTarget("VOID", 0.6f); 
        kesslerSyndrome.AddTarget("SAIL", 0.65f); 
        kesslerSyndrome.AddTarget("LUNA", 1.35f); // 지상 원격 탐사 수요 증가
        scenarioDatabase.Add(kesslerSyndrome);

        var syntheticScandal = new ScenarioEvent("그린 랩 합성 고기에서 공업용 단백질 검출 의혹!", false);
        syntheticScandal.AddTarget("GLAB", 0.7f); 
        syntheticScandal.AddTarget("ORGA", 1.35f); 
        syntheticScandal.AddTarget("AMBR", 1.2f); 
        syntheticScandal.AddTarget("TIMT", 1.05f);
        scenarioDatabase.Add(syntheticScandal);

        var ubi = new ScenarioEvent("연방 정부, 전 국민에게 매달 디지털 코인으로 기본 소득 지급!", true);
        ubi.AddTarget("TIMT", 0.9f); 
        ubi.AddTarget("PIXEL", 1.25f); 
        ubi.AddTarget("DUST", 1.2f); 
        ubi.AddTarget("FNET", 1.15f);
        scenarioDatabase.Add(ubi);

        // ==========================================
        // 9. 특수 시나리오 (Special & Crisis)
        // ==========================================

        // [협력] 우주 엘리베이터 착공
        var elevator = new ScenarioEvent("가이아 건설 & 타이탄 중공업, '우주 엘리베이터' 공동 착공!", true);
        elevator.AddTarget("GAIA", 1.3f); 
        elevator.AddTarget("TITN", 1.25f); 
        elevator.AddTarget("ZILS", 1.2f); 
        elevator.AddTarget("VOID", 0.75f);
        scenarioDatabase.Add(elevator);

        // [갈등] 로봇 격투 대회 승부조작
        var robotFix = new ScenarioEvent("넥서스 봇 주최 로봇 격투 대회, 대규모 승부조작 적발!", false);
        robotFix.AddTarget("NEXS", 0.7f); 
        robotFix.AddTarget("PIXEL", 0.8f); 
        robotFix.AddTarget("AURA",  1.2f); 
        scenarioDatabase.Add(robotFix);

        // [발견] 불로초? 심해 희귀 생물
        var deepBio = new ScenarioEvent("블루 오션, 심해에서 노화 억제 성분 함유한 생물 발견!", true);
        deepBio.AddTarget("BLUE", 1.45f); 
        deepBio.AddTarget("TIME", 1.25f); 
        deepBio.AddTarget("ILIA", 0.9f);
        scenarioDatabase.Add(deepBio);

        // [사고] 궤도 엘리베이터 케이블 절단
        var elevatorSnap = new ScenarioEvent("건설 중이던 우주 엘리베이터 케이블 절단 사고! 지상 추락!", false);
        elevatorSnap.AddTarget("GAIA", 0.5f); 
        elevatorSnap.AddTarget("TITN", 0.6f);
        elevatorSnap.AddTarget("VOID", 1.3f); 
        elevatorSnap.AddTarget("SHLD", 1.2f); 
        scenarioDatabase.Add(elevatorSnap);

        // [유행] 사이보그 패션 유행
        var cyborgTrend = new ScenarioEvent("MZ세대 사이에서 '기계 팔' 패션 유행! 신체 개조 붐!", true);
        cyborgTrend.AddTarget("NEO", 1.3f); 
        cyborgTrend.AddTarget("BIOS", 0.85f); 
        cyborgTrend.AddTarget("MIND", 1.15f);
        scenarioDatabase.Add(cyborgTrend);

        // [환경] 인공 강우 성공
        var rainSuccess = new ScenarioEvent("오가닉 팜, 자체 인공 강우 기술로 사막 농지화 성공!", true);
        rainSuccess.AddTarget("ORGA", 1.25f); 
        rainSuccess.AddTarget("GLAB", 0.9f);
        scenarioDatabase.Add(rainSuccess);

        // [금융] 은행 뱅크런 사태
        var bankRun = new ScenarioEvent("네뷸라 뱅크 전산 오류 루머로 뱅크런 조짐!", false);
        bankRun.AddTarget("BANK", 0.7f); 
        bankRun.AddTarget("FNET", 1.25f); 
        bankRun.AddTarget("AEGS", 1.15f);
        scenarioDatabase.Add(bankRun);

        // [엔터] VR 중독 치료제 개발
        var vrCure = new ScenarioEvent("일리아 바이오, '디지털 마약' VR 중독 치료제 임상 돌입!", true);
        vrCure.AddTarget("ILIA", 1.3f); 
        vrCure.AddTarget("FANT", 0.85f); 
        vrCure.AddTarget("CRCK", 0.9f);
        scenarioDatabase.Add(vrCure);

        // [전쟁] 용병 반란
        var mercRevolt = new ScenarioEvent("블랙쉴드 소속 인간 용병단, 처우 불만으로 파업 및 점거!", false);
        mercRevolt.AddTarget("SHLD", 0.6f); 
        mercRevolt.AddTarget("NEXS", 1.3f); 
        scenarioDatabase.Add(mercRevolt);

        // [우주] 혜성 충돌 위기
        var cometImpact = new ScenarioEvent("직경 10km 혜성 지구 접근 중! 충돌 확률 0.01%!", false);
        cometImpact.AddTarget("CSMC", 0.85f); 
        cometImpact.AddTarget("PRIO", 0.7f);
        cometImpact.AddTarget("GAIA", 1.3f); 
        cometImpact.AddTarget("TIMT", 1.35f); 
        scenarioDatabase.Add(cometImpact);

        // [정책] 탄소세 폐지
        var carbonFree = new ScenarioEvent("연방 정부, 경기 부양 위해 '탄소세' 전격 폐지!", true);
        carbonFree.AddTarget("ZILS", 1.3f); 
        carbonFree.AddTarget("ORGN", 1.25f); 
        carbonFree.AddTarget("SOLAR", 0.75f); 
        carbonFree.AddTarget("GLAB", 0.85f);
        scenarioDatabase.Add(carbonFree);

        // [기술] 뇌킹(Brain-Hacking) 범죄 조직 검거
        var brainGang = new ScenarioEvent("타인의 뇌를 해킹해 조종한 범죄 조직 '팬텀' 일망타진!", true);
        brainGang.AddTarget("AEGS", 1.25f); 
        brainGang.AddTarget("MIND", 0.7f); 
        scenarioDatabase.Add(brainGang);

        // [미디어] 아이돌 메타버스 팬미팅 서버 다운
        var serverCrash = new ScenarioEvent("버스 라이브, 아이돌 팬미팅 중 서버 폭발! 환불 소동!", false);
        serverCrash.AddTarget("Vlive", 0.75f); 
        serverCrash.AddTarget("AURA", 0.85f);
        scenarioDatabase.Add(serverCrash);

        // [교통] 플라잉카 음주운전 사고
        var flyingDrunk = new ScenarioEvent("스카이 글라이드, 도심 한복판 추락 사고! 원인은 음주 비행!", false);
        flyingDrunk.AddTarget("SKGL", 0.7f); 
        flyingDrunk.AddTarget("PRIO", 1.1f);
        scenarioDatabase.Add(flyingDrunk);

        // [식량] 우주 곰팡이 감염
        var spaceMold = new ScenarioEvent("우주 정거장 식량 창고, 미지의 곰팡이로 전량 오염!", false);
        spaceMold.AddTarget("TIMT", 0.75f); 
        spaceMold.AddTarget("DUST", 0.7f);
        spaceMold.AddTarget("ORGA", 1.25f); 
        scenarioDatabase.Add(spaceMold);

        // [에너지] 블랙홀 에너지 추출 이론 발표
        var blackholeEnergy = new ScenarioEvent("코어 퓨전, 블랙홀 에너지 추출 이론 발표! 학계 발칵!", true);
        blackholeEnergy.AddTarget("CORE", 1.35f); 
        blackholeEnergy.AddTarget("ZILS", 0.85f);
        scenarioDatabase.Add(blackholeEnergy);

        // [로봇] 감정 노동 로봇 인기
        var emotionalBot = new ScenarioEvent("넥서스 봇, 인간의 감정을 위로하는 '케어 로봇' 출시 대박!", true);
        emotionalBot.AddTarget("NEXS", 1.3f); 
        emotionalBot.AddTarget("MIND", 1.15f);
        scenarioDatabase.Add(emotionalBot);

        // [건설] 해저 도시 프로젝트
        var underwaterCity = new ScenarioEvent("가이아 건설, 수심 3000m 해저 도시 '아틀란티스' 건설 발표!", true);
        underwaterCity.AddTarget("GAIA", 1.35f); 
        underwaterCity.AddTarget("BLUE", 1.25f); 
        underwaterCity.AddTarget("MAGM", 1.15f);
        scenarioDatabase.Add(underwaterCity);

        // [게임] 게임 아이템 상속세 부과
        var gameTax = new ScenarioEvent("국세청, 고가 게임 아이템에 상속세 부과 결정!", false);
        gameTax.AddTarget("PIXEL", 0.75f); 
        gameTax.AddTarget("CRCK", 0.8f); 
        gameTax.AddTarget("FNET", 0.85f);
        scenarioDatabase.Add(gameTax);

        // [의료] 수면 학습기 부작용
        var sleepLearn = new ScenarioEvent("마인드 링크 수면 학습기, 불면증 및 환각 부작용 보고!", false);
        sleepLearn.AddTarget("MIND", 0.7f); 
        sleepLearn.AddTarget("ELIX", 1.2f); 
        scenarioDatabase.Add(sleepLearn);

        // [방산] 외계 침공 루머
        var alienRumor = new ScenarioEvent("심우주 관측소, 미확인 대규모 함대 접근 포착 루머!", false);
        alienRumor.AddTarget("SHLD", 1.35f); // 전쟁주는 급등 허용
        alienRumor.AddTarget("TITN", 1.2f); 
        alienRumor.AddTarget("NEXS", 1.15f);
        alienRumor.AddTarget("FANT", 0.6f); 
        scenarioDatabase.Add(alienRumor);

        // [자원] 대체 희토류 합성 성공
        var syntheticRare = new ScenarioEvent("그린 랩, 식물에서 희토류 성분 추출하는 기술 개발!", true);
        syntheticRare.AddTarget("GLAB", 1.45f); 
        syntheticRare.AddTarget("ZILS", 0.65f); // 광산 타격 큼
        syntheticRare.AddTarget("LUNA", 0.7f); 
        scenarioDatabase.Add(syntheticRare);

        // [금융] 코인 해킹
        var coinHack = new ScenarioEvent("퓨처 넷 메인넷 해킹! 10조 원 규모 코인 도난!", false);
        coinHack.AddTarget("FNET", 0.5f); // 해킹은 치명타
        coinHack.AddTarget("PIXEL", 0.75f); 
        coinHack.AddTarget("AEGS", 1.25f); 
        scenarioDatabase.Add(coinHack);

        // [교통] 우주선 면허 간소화
        var spaceLicense = new ScenarioEvent("누구나 우주로! 민간 우주선 조종 면허 대폭 간소화!", true);
        spaceLicense.AddTarget("SAIL", 1.3f); 
        spaceLicense.AddTarget("TITN", 1.2f); 
        spaceLicense.AddTarget("ORGN", 0.9f); 
        scenarioDatabase.Add(spaceLicense);

        // [식품] 전설의 요리사 영입
        var starChef = new ScenarioEvent("앰브로시아, 은하계 최고의 셰프 영입! 예약 3년치 마감!", true);
        starChef.AddTarget("AMBR", 1.25f);
        scenarioDatabase.Add(starChef);

        // [IT] 6G 통신망 조기 구축
        var sixG = new ScenarioEvent("헤르메스 통신, 6G 양자 통신망 예상보다 1년 일찍 개통!", true);
        sixG.AddTarget("HEMS", 1.35f); 
        sixG.AddTarget("CSMC", 1.2f); 
        sixG.AddTarget("Vlive", 1.2f);
        scenarioDatabase.Add(sixG);

        // [바이오] 좀비 바이러스 영화 개봉
        var zombieMovie = new ScenarioEvent("영화 '바이오 해저드' 천만 관객 돌파! 좀비 관련주 들썩!", true);
        zombieMovie.AddTarget("ILIA", 1.1f); 
        zombieMovie.AddTarget("BIOS", 1.1f); 
        zombieMovie.AddTarget("AURA", 1.2f);
        scenarioDatabase.Add(zombieMovie);

        // [기타] 회장의 기부
        var donation = new ScenarioEvent("코즈믹 소프트 회장, 전 재산의 90% 사회 환원 약속!", true);
        donation.AddTarget("CSMC", 1.15f);
        scenarioDatabase.Add(donation);

        // ==========================================
        // 🌟 [신규] 대형 복합 시나리오 (30종)
        // ==========================================

        // 2. [전염병/기술]
        var nanoVirus = new ScenarioEvent("BCI 칩을 통해 뇌를 파괴하는 '디지털 바이러스' 창궐!", false);
        nanoVirus.AddTarget("MIND", 0.05f); // 상장 폐지
        nanoVirus.AddTarget("NEO", 0.7f);  // 사이보그 감염
        nanoVirus.AddTarget("AEGS", 1.35f); // 백신(보안) 개발
        nanoVirus.AddTarget("ILIA", 1.2f); // 생체 치료제 기대
        scenarioDatabase.Add(nanoVirus);

        // 3. [문화/복고]
        var analogReturn = new ScenarioEvent("전자기기 거부 운동 '네오-러다이트' 전 우주적 확산!", true);
        analogReturn.AddTarget("ORGN", 1.35f); // 내연기관 부활
        analogReturn.AddTarget("ORGA", 1.25f); // 유기농 식품
        analogReturn.AddTarget("ARCD", 1.2f); // 레트로 게임
        analogReturn.AddTarget("CSMC", 0.9f); // IT 공룡 타격
        analogReturn.AddTarget("NEXS", 0.7f); // 로봇 파괴
        scenarioDatabase.Add(analogReturn);

        // 4. [경제/금융]
        var goldAsteroid = new ScenarioEvent("순금으로 이루어진 소행성 '골드 핑거' 포획 성공!", true);
        goldAsteroid.AddTarget("LUNA", 1.4f); // 발견 공로
        goldAsteroid.AddTarget("IRON", 1.25f); // 채굴 독점
        goldAsteroid.AddTarget("VOID", 1.2f); // 운송 대박
        goldAsteroid.AddTarget("BANK", 0.7f); // 금값 폭락으로 담보 가치 하락
        scenarioDatabase.Add(goldAsteroid);

        // 5. [정치/복지]
        var cyborgHumanRight = new ScenarioEvent("연방 대법원, '전신 의체 사이보그도 100% 인간' 판결!", true);
        cyborgHumanRight.AddTarget("NEO", 1.5f); // 개조 시술 합법화 확대
        cyborgHumanRight.AddTarget("ELIX", 1.2f); // 수술 후 진통제
        cyborgHumanRight.AddTarget("BIOS", 0.7f); // 생체 장기 수요 감소
        cyborgHumanRight.AddTarget("SHLD", 1.2f); // 강력한 용병 고용 가능
        scenarioDatabase.Add(cyborgHumanRight);

        // 6. [💀 상장 폐지] 금융 사기
        var pixelScam = new ScenarioEvent("충격! 픽셀 스튜디오 'P2E 코인' 알고 보니 폰지 사기! 대표 도주!", false);
        pixelScam.AddTarget("PIXEL", 0.05f); // [사실상 상장폐지]
        pixelScam.AddTarget("FNET", 0.5f);   // 투자사 동반 폭락
        pixelScam.AddTarget("CRCK", 1.3f);   // 경쟁사 반사이익
        pixelScam.AddTarget("AEGS", 1.2f);   // 조사 착수
        scenarioDatabase.Add(pixelScam);

        // 7. [엔터/기술]
        var mindIdol = new ScenarioEvent("뇌파 공유 아이돌 '마인드 팝' 데뷔! 오감 만족 콘서트!", true);
        mindIdol.AddTarget("Vlive", 1.3f); 
        mindIdol.AddTarget("MIND", 1.2f); 
        mindIdol.AddTarget("AURA", 1.05f); 
        mindIdol.AddTarget("FANT", 0.8f); // 오프라인 테마파크 소외
        scenarioDatabase.Add(mindIdol);

        // 8. [환경/재난]
        var acidRain = new ScenarioEvent("전 지구적 산성비 사태! 노지 작물 전멸 위기!", false);
        acidRain.AddTarget("ORGA", 0.6f); // 직격탄
        acidRain.AddTarget("GLAB", 1.4f); // 대체 식량 폭등
        acidRain.AddTarget("BLUE", 1.2f); // 바다는 안전하다
        acidRain.AddTarget("GAIA", 1.1f); // 돔 건설 수요
        scenarioDatabase.Add(acidRain);

        // 9. [교통/에너지]
        var gravityEngine = new ScenarioEvent("반중력 엔진 상용화 임박! 바퀴 달린 탈것은 이제 끝?", true);
        gravityEngine.AddTarget("SKGL", 1.3f); // 최대 수혜
        gravityEngine.AddTarget("PRIO", 0.65f); // 자율주행 차 위기
        gravityEngine.AddTarget("ORGN", 0.55f); // 내연기관 사망 선고
        gravityEngine.AddTarget("FLUX", 1.2f); // 고출력 배터리 필요
        scenarioDatabase.Add(gravityEngine);

        // 10. [바이오/윤리]
        var sleepNoMore = new ScenarioEvent("잠 안 자도 되는 약 '노-슬립' 부작용(광기) 은폐 폭로!", false);
        sleepNoMore.AddTarget("ILIA", 0.7f); 
        sleepNoMore.AddTarget("ELIX", 1.35f); // 진정제 수요 폭증
        sleepNoMore.AddTarget("MIND", 1.15f); // 수면 대체 기술 부각
        sleepNoMore.AddTarget("AURA", 1.25f); // 특종 보도
        scenarioDatabase.Add(sleepNoMore);

        // 12. [우주/식품]
        var spaceMichelin = new ScenarioEvent("미슐랭 가이드, 우주 정거장 레스토랑에 최초 별 3개 부여!", true);
        spaceMichelin.AddTarget("AMBR", 1.3f); // 선정된 레스토랑
        spaceMichelin.AddTarget("SAIL", 1.2f); // 미식 여행 패키지
        spaceMichelin.AddTarget("DUST", 1.1f); // 디저트 납품
        spaceMichelin.AddTarget("TIMT", 0.8f); // 싸구려 우주식량 외면
        scenarioDatabase.Add(spaceMichelin);

        // 13. [IT/해킹]
        var osBackdoor = new ScenarioEvent("코즈믹 OS에서 정부 감시용 백도어 발견! 전 세계적 보이콧!", false);
        osBackdoor.AddTarget("CSMC", 0.75f); 
        osBackdoor.AddTarget("NEXS", 0.7f); // OS 탑재 로봇 반품
        osBackdoor.AddTarget("DATA", 0.8f); // 데이터 신뢰 하락
        osBackdoor.AddTarget("AEGS", 1.25f); // 보안 검사 의뢰 쇄도
        scenarioDatabase.Add(osBackdoor);

        // 14. [건설/자원]
        var underwaterGold = new ScenarioEvent("해저 도시 아틀란티스 인근에서 희귀 광물 '비브라늄' 발견!", true);
        underwaterGold.AddTarget("BLUE", 1.3f); // 영해권 주장
        underwaterGold.AddTarget("GAIA", 1.2f); // 채굴 기지 건설
        underwaterGold.AddTarget("IRON", 1.05f); // 수중 채굴기 납품
        underwaterGold.AddTarget("LUNA", 0.8f); // 우주 광산 매력 감소
        scenarioDatabase.Add(underwaterGold);

        // 15. [게임/도박]
        var vrGambling = new ScenarioEvent("은하 연방, VR 카지노 전면 불법화 선언!", false);
        vrGambling.AddTarget("PIXEL", 0.65f); // 주요 수입원 차단
        vrGambling.AddTarget("CRCK", 1.2f); // 건전 게임 반사이익
        vrGambling.AddTarget("ARCD", 1.1f); 
        vrGambling.AddTarget("FNET", 0.7f); // 도박 코인 폭락
        scenarioDatabase.Add(vrGambling);

        // 16. [💀 상장 폐지] 약물 스캔들
        var elixFentanyl = new ScenarioEvent("엘릭서 팜 진통제, 치사량의 마약 성분 고의 첨가 적발! CEO 구속!", false);
        elixFentanyl.AddTarget("ELIX", 0.05f); // [사실상 상장폐지]
        elixFentanyl.AddTarget("ILIA", 1.4f);  // 대체 약품 독점
        elixFentanyl.AddTarget("BIOS", 1.2f);  // 장기 손상 환자 증가(?)
        elixFentanyl.AddTarget("NEO", 1.1f);   // 고통 없는 기계 몸 선호
        scenarioDatabase.Add(elixFentanyl);

        // 17. [에너지/자원]
        var dysonSphere = new ScenarioEvent("항성을 감싸는 '다이슨 스웜' 건설 프로젝트 착수!", true);
        dysonSphere.AddTarget("SOLAR", 1.25f); // 주관사
        dysonSphere.AddTarget("TITN", 1.15f);  // 구조물 제작
        dysonSphere.AddTarget("FLUX", 1.25f);  // 에너지 저장
        dysonSphere.AddTarget("ZILS", 0.7f);  // 기존 에너지 몰락
        scenarioDatabase.Add(dysonSphere);

        // 18. [로봇/노동]
        var robotUnion = new ScenarioEvent("자아를 가진 안드로이드 노조 결성! 임금(?) 인상 파업!", false);
        robotUnion.AddTarget("NEXS", 0.7f); // 생산 차질
        robotUnion.AddTarget("CSMC", 0.85f); // AI 통제 실패 책임
        robotUnion.AddTarget("SHLD", 1.3f); // 파업 진압 용병 투입
        robotUnion.AddTarget("NEO", 1.2f);  // 말 잘 듣는 사이보그 선호
        scenarioDatabase.Add(robotUnion);

        // 19. [식품/트렌드]
        var zeroCalorie = new ScenarioEvent("먹어도 살 안 찌는 '완벽한 디저트' 개발 성공!", true);
        zeroCalorie.AddTarget("DUST", 1.3f); 
        zeroCalorie.AddTarget("AMBR", 1.2f); 
        zeroCalorie.AddTarget("GLAB", 1.1f); // 기술 제휴
        zeroCalorie.AddTarget("ORGA", 0.9f); // 맛으로 밀림
        scenarioDatabase.Add(zeroCalorie);

        // 23. [자원/지구]
        var deepCoreMining = new ScenarioEvent("지구 내핵까지 뚫는 '맨틀 드릴링' 기술 시연 성공!", true);
        deepCoreMining.AddTarget("MAGM", 1.2f); 
        deepCoreMining.AddTarget("IRON", 1.3f); 
        deepCoreMining.AddTarget("ZILS", 1.1f); 
        deepCoreMining.AddTarget("LUNA", 0.8f); // 지구에도 자원 많다
        scenarioDatabase.Add(deepCoreMining);

        // 24. [게임/메타버스]
        var virtualNation = new ScenarioEvent("버스 라이브 내 가상 국가, UN 가입 승인! 현실 국가와 동등 지위?", true);
        virtualNation.AddTarget("Vlive", 1.6f); 
        virtualNation.AddTarget("CSMC", 1.2f); 
        virtualNation.AddTarget("FNET", 1.3f); // 가상 화폐가 기축 통화?
        virtualNation.AddTarget("ORGN", 0.8f); // 현실 이동 감소
        scenarioDatabase.Add(virtualNation);

        // 26. [자동차/스포츠]
        var antiGravityRacing = new ScenarioEvent("반중력 레이싱 리그 개막! 스카이 글라이드 팀 우승!", true);
        antiGravityRacing.AddTarget("SKGL", 1.2f); 
        antiGravityRacing.AddTarget("PRIO", 0.8f); // 땅에서 달리는 건 촌스럽다
        antiGravityRacing.AddTarget("FLUX", 1.15f); 
        antiGravityRacing.AddTarget("AURA", 1.1f); // 독점 중계
        scenarioDatabase.Add(antiGravityRacing);

        // 27. [식품/환경]
        var beeExtinction = new ScenarioEvent("꿀벌 완전 멸종 선언! 자연 수분 불가능, 식량 대란!", false);
        beeExtinction.AddTarget("ORGA", 0.55f); // 농사 망함
        beeExtinction.AddTarget("GLAB", 1.3f); // 인공 식량이 유일한 희망
        beeExtinction.AddTarget("NEXS", 1.2f); // 수분용 마이크로 드론
        beeExtinction.AddTarget("TIMT", 1.1f); 
        scenarioDatabase.Add(beeExtinction);

        // 28. [IT/통신]
        var solarInternet = new ScenarioEvent("태양광 충전 위성망 구축 완료! 전 우주 무료 와이파이 시대!", true);
        solarInternet.AddTarget("HEMS", 1.3f); 
        solarInternet.AddTarget("SOLAR", 1.2f); 
        solarInternet.AddTarget("CSMC", 1.1f); 
        solarInternet.AddTarget("AEGS", 0.75f); // 공용망 보안 취약 우려
        scenarioDatabase.Add(solarInternet);

        // 30. [의료/미용]
        var plasticGene = new ScenarioEvent("원하는 얼굴로 DNA를 바꿔주는 '성형 바이러스' 시술 유행!", true);
        plasticGene.AddTarget("ILIA", 1.4f); 
        plasticGene.AddTarget("NEO", 0.85f); // 기계보다 자연스러운 생체 성형 선호
        plasticGene.AddTarget("AURA", 1.2f); // 외모 지상주의 조장
        plasticGene.AddTarget("BIOS", 1.1f); 
        scenarioDatabase.Add(plasticGene);

        // ==========================================
        // 1. 기업 분쟁 및 소송 (Legal & Conflict)
        // ==========================================

        var dreamCopyright = new ScenarioEvent("마인드 링크, 타인의 '꿈' 영상을 NFT로 파는 기술 개발! 저작권 논란!", true);
        dreamCopyright.AddTarget("MIND", 1.3f);
        dreamCopyright.AddTarget("FNET", 1.25f); // NFT 거래 활성화
        dreamCopyright.AddTarget("AURA", 0.85f);  // 연예인 꿈 도촬 문제로 언론 타격
        scenarioDatabase.Add(dreamCopyright);

        var iceCarBan = new ScenarioEvent("환경청, 도심 내 '내연기관 차량' 진입 전면 금지 법안 발의!", false);
        iceCarBan.AddTarget("ORGN", 0.55f);  // 존폐 위기
        iceCarBan.AddTarget("PRIO", 1.2f);  // 반사이익
        iceCarBan.AddTarget("ZILS", 0.85f); // 석유 수요 감소
        scenarioDatabase.Add(iceCarBan);

        var batteryRecall = new ScenarioEvent("플럭스 셀 배터리, 고속 충전 중 연쇄 폭발! 사상 최대 리콜!", false);
        batteryRecall.AddTarget("FLUX", 0.65f);
        batteryRecall.AddTarget("PRIO", 0.75f); // 전기차 신뢰도 하락
        batteryRecall.AddTarget("ORGN", 1.15f); // "역시 구관이 명관"
        scenarioDatabase.Add(batteryRecall);

        var spaceSpy = new ScenarioEvent("보이드 하울 화물선에서 '경쟁사 산업 스파이' 밀항 적발!", false);
        spaceSpy.AddTarget("VOID", 0.85f); // 보안 구멍
        spaceSpy.AddTarget("SHLD", 1.2f);  // 용병 검색 강화 요청
        scenarioDatabase.Add(spaceSpy);

        // ==========================================
        // 2. 기술 제휴 및 마케팅 (Partnership)
        // ==========================================

        var retroCarGame = new ScenarioEvent("아케이드 X, 오리진 모터스와 합작해 '실제 차량'으로 하는 레이싱 게임 런칭!", true);
        retroCarGame.AddTarget("ARCD", 1.15f);
        retroCarGame.AddTarget("ORGN", 1.2f); // "힙하다"는 평가
        retroCarGame.AddTarget("SKGL", 0.9f); // 나는 차는 재미없다
        scenarioDatabase.Add(retroCarGame);

        var militaryDessert = new ScenarioEvent("스타 더스트, 블랙 쉴드 전투식량으로 '고열량 전투 쉐이크' 납품 계약!", true);
        militaryDessert.AddTarget("DUST", 1.25f);
        militaryDessert.AddTarget("SHLD", 1.05f);
        militaryDessert.AddTarget("TIMT", 0.9f); // 보급 경쟁 패배
        scenarioDatabase.Add(militaryDessert);

        var luxuryYachtParty = new ScenarioEvent("솔라 세일, 앰브로시아와 함께하는 '우주 미식 크루즈' 패키지 완판!", true);
        luxuryYachtParty.AddTarget("SAIL", 1.25f);
        luxuryYachtParty.AddTarget("AMBR", 1.25f);
        luxuryYachtParty.AddTarget("Vlive", 0.9f); // 가상 여행보다 실제 여행 선호
        scenarioDatabase.Add(luxuryYachtParty);

        var bioComputer = new ScenarioEvent("코즈믹 소프트, 일리아 바이오의 '생체 CPU'를 탑재한 차세대 서버 공개!", true);
        bioComputer.AddTarget("CSMC", 1.3f);
        bioComputer.AddTarget("ILIA", 1.2f);
        bioComputer.AddTarget("CORE", 0.9f); // 생체 전력 효율이 좋아 전기 덜 씀
        scenarioDatabase.Add(bioComputer);

        // ==========================================
        // 3. 사회 현상 및 유행 (Social Trend)
        // ==========================================

        var memoryErase = new ScenarioEvent("엘릭서 팜, 나쁜 기억만 지워주는 '망각 알약' 출시! 전 우주 품절!", true);
        memoryErase.AddTarget("ELIX", 1.4f);
        memoryErase.AddTarget("MIND", 0.7f); // 굳이 머리에 칩 안 박아도 됨
        memoryErase.AddTarget("AURA", 1.1f); // 알약 체험기 방송 인기
        scenarioDatabase.Add(memoryErase);

        var spaceGarbage = new ScenarioEvent("우주 쓰레기장 'G-7 구역'에서 희귀 금속 다량 검출! 보물섬 등극!", true);
        spaceGarbage.AddTarget("VOID", 1.3f); // 쓰레기 수거선이 보물선으로
        spaceGarbage.AddTarget("IRON", 1.2f); // 분해 및 채굴
        spaceGarbage.AddTarget("BLUE", 0.8f); // 해양 자원 관심 하락
        scenarioDatabase.Add(spaceGarbage);

        var noBodyMovement = new ScenarioEvent("'신체 포기 운동' 확산! 육체를 버리고 메타버스로 이주하는 젊은이들!", false);
        noBodyMovement.AddTarget("Vlive", 1.35f);
        noBodyMovement.AddTarget("BIOS", 0.65f); // 장기 필요 없음
        noBodyMovement.AddTarget("TIMT", 0.8f); // 밥도 안 먹음 (영양액 주사)
        noBodyMovement.AddTarget("NEO", 1.15f);  // 최소한의 생명 유지 장치 시술
        scenarioDatabase.Add(noBodyMovement);

        var petRobot = new ScenarioEvent("넥서스 봇, 멸종 위기 동물을 본뜬 'AI 펫' 시리즈 대박!", true);
        petRobot.AddTarget("NEXS", 1.25f);
        petRobot.AddTarget("GLAB", 0.8f); // 진짜 동물 복제보다 로봇 선호
        scenarioDatabase.Add(petRobot);

        // ==========================================
        // 4. 재난 및 사고 (Disaster)
        // ==========================================

        var dataCenterFire = new ScenarioEvent("데이터 마이닝 지하 서버실 화재! 냉각 시스템 마비로 정보 증발!", false);
        dataCenterFire.AddTarget("DATA", 0.7f);
        dataCenterFire.AddTarget("CSMC", 0.85f);
        dataCenterFire.AddTarget("MAGM", 1.2f); // 지열 냉각 시스템의 안정성 재조명
        scenarioDatabase.Add(dataCenterFire);

        var airTrafficJam = new ScenarioEvent("스카이 글라이드 통제 시스템 오류! 도심 상공 수천 대 공중 추돌 위기!", false);
        airTrafficJam.AddTarget("SKGL", 0.85f);
        airTrafficJam.AddTarget("AEGS", 1.2f);  // 보안 시스템 업그레이드 요구
        airTrafficJam.AddTarget("PRIO", 1.1f);  // 지상이 더 안전하다
        scenarioDatabase.Add(airTrafficJam);

        var nutrientPoison = new ScenarioEvent("티메트 푸드 보급형 영양바에서 '미확인 독성 물질' 검출!", false);
        nutrientPoison.AddTarget("TIMT", 0.75f);
        nutrientPoison.AddTarget("ILIA", 1.2f); // 해독제 수요
        nutrientPoison.AddTarget("DUST", 1.1f); // 대체 간식 수요
        scenarioDatabase.Add(nutrientPoison);

        var themeParkHack = new ScenarioEvent("판타지아 테마파크의 안드로이드들이 해킹당해 관람객 공격!", false);
        themeParkHack.AddTarget("FANT", 0.7f);
        themeParkHack.AddTarget("SHLD", 1.15f);  // 진압 작전 수행
        themeParkHack.AddTarget("Vlive", 1.1f); // 안전한 집에서 놀자
        scenarioDatabase.Add(themeParkHack);

        // ==========================================
        // 5. 금융 및 경제 (Economy)
        // ==========================================

        var safeVaultOrbit = new ScenarioEvent("네뷸라 뱅크, 절대 털리지 않는 '궤도 금고' 위성 런칭!", true);
        safeVaultOrbit.AddTarget("BANK", 1.25f);
        safeVaultOrbit.AddTarget("TITN", 1.1f); // 위성 제작
        safeVaultOrbit.AddTarget("AEGS", 1.05f);
        scenarioDatabase.Add(safeVaultOrbit);

        var futurePredict = new ScenarioEvent("데이터 마이닝, 빅데이터로 '범죄 예측 시스템' 개발! 치안 혁명?", true);
        futurePredict.AddTarget("DATA", 1.4f);
        futurePredict.AddTarget("SHLD", 0.55f); // 범죄가 줄어들면 용병 일감 감소
        scenarioDatabase.Add(futurePredict);

        var antiqueAuction = new ScenarioEvent("오리진 모터스 2025년형 모델, 경매에서 사상 최고가 낙찰!", true);
        antiqueAuction.AddTarget("ORGN", 1.25f); // 브랜드 가치 상승
        antiqueAuction.AddTarget("ARCD", 1.1f); // 레트로 문화 확산
        scenarioDatabase.Add(antiqueAuction);

        var coinTax = new ScenarioEvent("세무국, 'P2E 게임 코인' 환전 시 세금 70% 부과 결정!", false);
        coinTax.AddTarget("PIXEL", 0.75f);
        coinTax.AddTarget("CRCK", 0.85f);
        coinTax.AddTarget("BANK", 1.2f); // 전통 화폐 가치 보존
        scenarioDatabase.Add(coinTax);

        // ==========================================
        // 6. 특수/유머 (Special)
        // ==========================================

        var aiNewsAnchor = new ScenarioEvent("오로라 미디어, 모든 인간 앵커 해고! 'AI 아나운서' 전면 도입!", true);
        aiNewsAnchor.AddTarget("AURA", 1.15f); // 인건비 절감
        aiNewsAnchor.AddTarget("CSMC", 1.1f);  // AI 기술 제공
        aiNewsAnchor.AddTarget("NEXS", 0.9f);
        scenarioDatabase.Add(aiNewsAnchor);

        var lowGravityStrike = new ScenarioEvent("아이언 윌 광산 노동자들, '저중력 후유증' 산재 인정 요구 파업!", false);
        lowGravityStrike.AddTarget("IRON", 0.75f);
        lowGravityStrike.AddTarget("ELIX", 1.2f); // 통증 완화제 납품
        lowGravityStrike.AddTarget("NEXS", 1.1f); // 로봇으로 대체하자 여론
        scenarioDatabase.Add(lowGravityStrike);

        var homeSun = new ScenarioEvent("코어 퓨전, 가정용 초소형 인공 태양 '미니 썬' 프로토타입 공개!", true);
        homeSun.AddTarget("CORE", 1.2f);
        homeSun.AddTarget("ZILS", 0.75f); // 가정용 연료 수요 삭제
        scenarioDatabase.Add(homeSun);

        var waterOnMercury = new ScenarioEvent("루나 로버, 수성 극지방에서 대규모 '얼음 창고' 발견!", true);
        waterOnMercury.AddTarget("LUNA", 1.35f);
        waterOnMercury.AddTarget("DUST", 1.15f); // 우주 빙수 재료 확보(...)
        scenarioDatabase.Add(waterOnMercury);

        var antiTechTown = new ScenarioEvent("기계를 거부하는 '자연주의 마을' 급증! 오가닉 팜 후원!", true);
        antiTechTown.AddTarget("ORGA", 1.2f);
        antiTechTown.AddTarget("ORGN", 1.1f);
        antiTechTown.AddTarget("CSMC", 0.9f);
        scenarioDatabase.Add(antiTechTown);

        var planetBuy = new ScenarioEvent("퓨처 넷, 가상 화폐 수익으로 소행성 하나를 통째로 매입!", true);
        planetBuy.AddTarget("FNET", 1.25f);
        planetBuy.AddTarget("GAIA", 1.1f); // 개발 수주 기대
        scenarioDatabase.Add(planetBuy);

        // [계절/환경] 모래 폭풍
        var sandStorm = new ScenarioEvent("화성 전역을 덮친 초대형 모래 폭풍! 모든 야외 활동 중단!", false);
        sandStorm.AddTarget("GAIA", 0.75f); // 건설 중단
        sandStorm.AddTarget("SOLAR", 0.55f); // 발전 효율 0%
        sandStorm.AddTarget("MAGM", 1.15f); // 날씨 영향 없는 지열 발전 떡상
        scenarioDatabase.Add(sandStorm);

        // [건강] 전자파 과민증
        var empSickness = new ScenarioEvent("고출력 배터리 근처에서 '전자파 과민증' 환자 급증 보고!", false);
        empSickness.AddTarget("FLUX", 0.8f);
        empSickness.AddTarget("PRIO", 0.85f);
        empSickness.AddTarget("BIOS", 1.05f); // 건강 검진 수요
        scenarioDatabase.Add(empSickness);

        // [패션] 발광 문신
        var lightTattoo = new ScenarioEvent("네오 진, 어둠 속에서 빛나는 '생체 발광 문신' 시술 유행!", true);
        lightTattoo.AddTarget("NEO", 1.15f);
        lightTattoo.AddTarget("Vlive", 1.05f); // 아바타 스킨으로도 출시
        scenarioDatabase.Add(lightTattoo);

        // [교육] 뇌 칩 불법 과외
        var chipTutoring = new ScenarioEvent("수능/시험용 불법 '지식 칩' 암거래 성행! 교육부 단속!", false);
        chipTutoring.AddTarget("MIND", 0.9f); // 규제 강화 우려
        chipTutoring.AddTarget("DATA", 1.1f); // 칩에 들어갈 지식 데이터 판매
        scenarioDatabase.Add(chipTutoring);
    }

    #endregion

    #region Core Market Logic

    // 🔄 [수정됨] UI 자동 갱신 루프 (0.5초 주기)
    // 자산 정보 뿐만 아니라, AI 매매로 인한 주가 변동도 실시간(0.5초 간격)으로 반영합니다.
    IEnumerator UpdatePortfolioLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(0.5f);

        while (true)
        {
            // 1. 플레이어 자산 정보 갱신
            UpdatePortfolioUI();
            UpdatePlayerMoneyUI();

            // 2. 대출 정보 갱신
            if (UIManager.I.HasGroup(UI_GROUP_LOAN))
            {
                UpdateLoanPanelUI();
            }

            // ✨ 3. [추가] 주식 시장 현황판 갱신
            // AI들이 수시로 사고팔면서 ApplyMarketImpact로 가격을 바꾸고 있습니다.
            // 이를 5초 턴까지 기다리지 않고 0.5초마다 갱신하여 역동적인 시장을 보여줍니다.
            UpdateStockBoardUI();

            // ✨ 4. [추가] 현재 트레이딩 패널을 열어보고 있다면, 그 정보도 갱신
            // (내가 보고 있는 주식을 AI가 사서 가격이 오르는 것을 실시간으로 확인 가능)
            if (selectedStock != null)
            {
                UpdateTradePanelUI();
            }

            yield return wait;
        }
    }

    IEnumerator UpdateMarketPrices()
    {
        while (true)
        {
            yield return new WaitForSeconds(updateInterval);

            // 👻 [핵심] 턴 시작 시, 직전 턴에 행동하지 않았으면 페널티 부과 (턴을 넘긴 것 자체에 페널티)
            ProcessBrokerNoBuyPenalty();

            UpdateBaseInterestRate(); // 🏦 [추가] 금리 변동 체크
            DistributeDividends();

            // 👇 [변경] 공매도 이자는 이제 매 턴 징수합니다.
            ApplyShortInterest();

            // 👇 [추가] AI 대출 이자 처리 및 상환 로직 호출
            if (aiManager != null) aiManager.ProcessAILoans();

            // 👇 [변경] 보유세(재산세)는 기존처럼 3턴마다 징수합니다.
            currentTaxTurn++;
            if (currentTaxTurn >= taxIntervalTurns)
            {
                ApplyTaxes();
                currentTaxTurn = 0;
            }
            currentTaxTurn++;
            if (currentTaxTurn >= taxIntervalTurns)
            {
                ApplyTaxes();
                currentTaxTurn = 0;
            }

            if (currentEvent.HasValue && currentEvent.Value.isListing)
            {
                marketStocks.Add(currentEvent.Value.singleTarget);
            }

            for (int i = marketStocks.Count - 1; i >= 0; i--)
            {
                RuntimeStock stock = marketStocks[i];
                stock.previousPrice = stock.currentPrice;

                if (stock.isDelisting)
                {
                    if (selectedStock == stock) ToggleTradePanel(false);
                    if (!upcomingStocks.Contains(stock.data)) upcomingStocks.Add(stock.data);
                    marketStocks.RemoveAt(i);
                    continue;
                }

                if (currentEvent.HasValue && currentEvent.Value.isBankruptcy && currentEvent.Value.singleTarget == stock)
                {
                    stock.currentPrice = 0;
                }
                else
                {
                    float changePercent = UnityEngine.Random.Range(-stock.data.volatility, stock.data.volatility);

                    if (currentEvent.HasValue && !currentEvent.Value.isListing)
                    {
                        var evt = currentEvent.Value;
                        float sensitivity = stock.data.eventPotential;

                        if (evt.scenarioTargets != null && evt.scenarioTargets.ContainsKey(stock))
                        {
                            float targetMultiplier = evt.scenarioTargets[stock];
                            if (targetMultiplier >= 1.0f)
                                changePercent += UnityEngine.Random.Range(0.05f, 0.15f) * targetMultiplier * sensitivity;
                            else
                                changePercent -= UnityEngine.Random.Range(0.05f, 0.15f) * (1.0f / targetMultiplier) * sensitivity;
                        }
                        else if (evt.isRippleEvent && evt.singleTarget != null)
                        {
                            if (stock == evt.singleTarget)
                                ApplyEventImpact(ref changePercent, evt.isGoodNews, evt.singleMultiplier, sensitivity);
                            else if (stock.data.sector == evt.singleTarget.data.sector)
                                ApplyEventImpact(ref changePercent, evt.isGoodNews, evt.singleMultiplier * 0.4f, sensitivity);
                        }
                    }

                    int changeAmount = (int)(stock.currentPrice * changePercent);
                    stock.currentPrice += changeAmount;
                    ClampStockPrice(stock);
                }

                int delistThreshold = (int)(stock.data.startPrice * 0.01f);
                if (stock.currentPrice <= delistThreshold && !stock.isDelisting)
                {
                    stock.currentPrice = Mathf.Max(1, stock.currentPrice);
                    stock.isDelisting = true;
                    Debug.Log($"⚠ {stock.data.stockName} 정리 매매 개시! (다음 턴 상장 폐지)");
                }
            }

            CheckPlayerBankruptcy();
            CheckMarginCall();

            // 👻 [핵심 구현] 턴 종료 시, P/L 정산 및 계약 해지
            ProcessBrokerContract();

            currentEvent = null;
            GenerateNextEvent();

            UpdateStockBoardUI();
            if (selectedStock != null) UpdateTradePanelUI();
        }
    }

    void CheckPlayerBankruptcy()
    {
        // 순자산 계산 (GetTotalAsset은 현금 + 주식 평가액 - 빚을 계산)
        long totalAsset = player.GetTotalAsset(); 
        
        // 👇 파산 조건: 순자산이 10,000원 이하일 때
        if (totalAsset <= playerBankruptcyThreshold) 
        {
            Debug.Log($"💀 [GAME OVER] 플레이어 파산! (현재 자산: {totalAsset:N0}원 / 기준: {playerBankruptcyThreshold:N0}원)");
            if (UIManager.I != null) UIManager.I.TrySetActive(UI_GROUP_GAMEOVER, "Panel_GameOver", true);
            StopAllCoroutines(); // 게임 루프 정지
        }
    }

    void CheckMarginCall()
    {
        var shorts = player.GetShortPositions();
        long totalDebt = 0;
        foreach (var item in shorts)
        {
            RuntimeStock stock = marketStocks.Find(s => s.data == item.Key);
            if (stock != null) totalDebt += (long)stock.currentPrice * item.Value;
        }

        // 현금보다 갚아야 할 주식 빚이 더 많으면 마진콜 발동
        if (totalDebt > 0 && player.money < totalDebt)
        {
            Debug.LogWarning("🚨 [마진콜] 증거금 부족! 공매도 포지션 강제 청산 절차 시작");

            // 👇 [추가] 갚아야 할 총 금액만큼 현금이 부족하므로, 보유 주식(Long)을 팔아 현금 확보
            ForceLiquidateForCash(totalDebt);

            // 현금을 확보한 뒤 공매도 강제 상환 진행
            foreach (var item in new Dictionary<StockData, int>(shorts))
            {
                RuntimeStock stock = marketStocks.Find(s => s.data == item.Key);
                if (stock != null)
                {
                    long debtCost = (long)stock.currentPrice * item.Value;
                    
                    // 만약 위에서 ForceLiquidate를 했는데도 돈이 부족하다면(전 재산을 팔아도 부족)
                    // 파산 시스템이 처리하도록 그냥 뺍니다 (마이너스 통장)
                    player.money -= debtCost; 
                    
                    stock.remainShares += item.Value;
                    player.RemoveShort(item.Key, item.Value);
                }
            }
            UpdateTradePanelUI();
            UpdatePlayerMoneyUI();
        }
    }

    void ClampStockPrice(RuntimeStock stock)
    {
        int basePrice = stock.previousPrice > 0 ? stock.previousPrice : stock.data.startPrice;
        int limitAmount = (int)(basePrice * priceLimitPercent);
        int upperLimit = basePrice + limitAmount;
        int lowerLimit = basePrice - limitAmount;

        int absoluteMax = (int)(stock.data.startPrice * maxPriceCapMultiplier);
        upperLimit = Mathf.Min(upperLimit, absoluteMax);

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
                // 👇 [추가] 이자 낼 돈이 없으면 주식 강매
                if (player.money < interest)
                {
                    ForceLiquidateForCash(interest);
                }

                player.money -= interest;
            }
        }
        UpdatePlayerMoneyUI();
    }

    void ApplyTaxes()
    {
        if (player.money > 0)
        {
            long tax = (long)(player.money * 0.01f);
            
            // 👇 [추가] 세금 낼 돈이 없으면 주식 강매 (사실 player.money > 0 조건 때문에 여기선 잘 안 걸리지만, 혹시 모를 로직을 위해)
            if (player.money < tax)
            {
                ForceLiquidateForCash(tax);
            }

            player.money -= tax;
            Debug.Log($"🏛️ [세금] 보유세 {tax:N0}원 납부");
        }

        if (aiManager != null) aiManager.ApplyTaxToAI();
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
        if (totalDiv > 0) player.money += totalDiv;
        if (aiManager != null) aiManager.DistributeAIDividends();
    }

    void ApplyEventImpact(ref float changePercent, bool isGood, float power, float sensitivity)
    {
        if (isGood) changePercent += UnityEngine.Random.Range(0.05f, 0.15f) * power * sensitivity;
        else changePercent -= UnityEngine.Random.Range(0.05f, 0.15f) * (1.0f / power) * sensitivity;
    }

    // 💸 [신규] 현금이 부족할 때 보유 주식을 무작위로 강제 매도하여 자금을 마련하는 함수
    void ForceLiquidateForCash(long amountNeeded)
    {
        long deficit = amountNeeded - player.money;
        if (deficit <= 0) return; // 현금이 충분하면 패스

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

            int currentQty = holdings[targetData];
            
            // 2. 필요한 수량 계산 (부족분 / 현재가)
            // 올림 처리를 위해 (deficit + price - 1) / price 공식 사용 혹은 단순 계산 후 +1
            int neededQty = (int)(deficit / marketStock.currentPrice);
            if (deficit % marketStock.currentPrice != 0) neededQty++;

            // 3. 실제 매도 수량 결정 (보유량보다 많이 팔 순 없음)
            int sellQty = Mathf.Min(currentQty, neededQty);

            // 4. 강제 매도 실행
            long income = (long)sellQty * marketStock.currentPrice;
            player.money += income;
            player.RemoveStock(targetData, sellQty);
            marketStock.remainShares += sellQty;
            
            // 시장가 하락 충격 적용 (투매 효과)
            ApplyMarketImpact(marketStock, sellQty, false); 

            Debug.Log($"📉 [강제 매도] {targetData.stockName} {sellQty}주 처분 -> {income:N0}원 확보");

            // 5. 상태 갱신
            deficit -= income;
            holdings = player.GetHoldings(); // 갱신된 보유 목록
            if (!holdings.ContainsKey(targetData)) myStockKeys.Remove(targetData);
        }
        
        UpdatePortfolioUI(); // UI 즉시 갱신
    }

    RuntimeStock GetWeightedRandomStock()
    {
        if (marketStocks.Count == 0) return null;
        float totalWeight = marketStocks.Sum(s => s.data.eventWeight);
        float r = UnityEngine.Random.Range(0f, totalWeight);
        foreach (var s in marketStocks) { r -= s.data.eventWeight; if (r <= 0) return s; }
        return marketStocks.Last();
    }

    // 🏦 [신규] 기준 금리 변동 로직
    void UpdateBaseInterestRate()
    {
        currentRateTurn++;
        if (currentRateTurn < rateUpdateInterval) return;

        currentRateTurn = 0;
        
        // 1. 금리 변동 방향 결정 (랜덤 + 현재 금리 수준 고려)
        // 금리가 너무 낮으면 올리려 하고, 높으면 내리려 하는 경향
        float randomVal = UnityEngine.Random.value;
        float changeAmount = 0f;
        string newsMsg = "";
        Color newsColor = Color.white;

        if (baseInterestRate < 0.02f) // 초저금리일 때 -> 인상 확률 높음
        {
            changeAmount = (randomVal > 0.3f) ? 0.0025f : 0f; 
        }
        else if (baseInterestRate > 0.10f) // 고금리일 때 -> 인하 확률 높음
        {
            changeAmount = (randomVal > 0.3f) ? -0.0025f : 0f;
        }
        else // 평범할 때 -> 랜덤
        {
            if (randomVal < 0.33f) changeAmount = 0.0025f; // 0.25%p 인상 (베이비스텝)
            else if (randomVal < 0.66f) changeAmount = -0.0025f; // 0.25%p 인하
        }

        // 2. 금리 적용 및 시장 충격
        if (changeAmount != 0)
        {
            baseInterestRate += changeAmount;
            baseInterestRate = Mathf.Clamp(baseInterestRate, 0.01f, 0.15f); // 1% ~ 15% 제한

            bool isHike = changeAmount > 0;
            
            if (isHike)
            {
                newsMsg = $"[속보] 중앙은행, 기준 금리 {Mathf.Abs(changeAmount)*100:F2}%p 전격 인상! (현재 {baseInterestRate*100:F2}%)";
                newsColor = new Color(1f, 0.4f, 0.4f); // 빨간맛 (악재)
                ApplyInterestRateImpact(false); // 시장 하락 압력
            }
            else
            {
                newsMsg = $"[속보] 중앙은행, 기준 금리 {Mathf.Abs(changeAmount)*100:F2}%p 인하! 경기 부양 의지.";
                newsColor = new Color(0.4f, 1f, 0.4f); // 초록맛 (호재)
                ApplyInterestRateImpact(true); // 시장 상승 압력
            }

            UpdateNewsUI(newsMsg, newsColor);
            Debug.Log($"🏦 {newsMsg}");
            
            // UI 갱신 (대출 패널이 열려있다면 이자율 갱신)
            if (UIManager.I.HasGroup(UI_GROUP_LOAN)) UpdateLoanPanelUI();
        }
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

    #endregion

    #region Event System

    void GenerateNextEvent()
    {
        hasPlayerUsedInfo = false;

        // ✨ [신규] 알림 아이콘 활성화 (새로운 이벤트가 생기면)
        if (notificationIcon != null)
        {
            notificationIcon.SetActive(true);
        }

        // 뒷거래 패널의 텍스트를 기본 상태로 리셋 (혹시 이전 턴의 정보가 남아있을 수 있으므로)
        UpdateDefaultInfoText();

        if (marketStocks.Count == 0 && upcomingStocks.Count == 0) return;

        // 1. 파산
        if (marketStocks.Count > 0 && UnityEngine.Random.value < bankruptcyChance)
        {
            RuntimeStock target = GetWeightedRandomStock();
            string realTitle = $"[긴급] {target.data.stockName}, {bankruptcyNews[UnityEngine.Random.Range(0, bankruptcyNews.Length)]}";
            currentEvent = new PendingEvent { singleTarget = target, newsTitle = realTitle, isBankruptcy = true };
            UpdateNewsUI("⚠ [긴급] 특정 기업의 재정 상태에 심각한 경고등이 켜졌습니다.", Color.red);
            return;
        }

        // 2. 상장
        if (upcomingStocks.Count > 0 && UnityEngine.Random.value < listingChance)
        {
            int idx = UnityEngine.Random.Range(0, upcomingStocks.Count);
            StockData newData = upcomingStocks[idx];
            RuntimeStock newStock = new RuntimeStock(newData);
            string realTitle = $"[IPO] {newData.stockName}, {listingNews[UnityEngine.Random.Range(0, listingNews.Length)]}";
            currentEvent = new PendingEvent { singleTarget = newStock, newsTitle = realTitle, isListing = true };
            upcomingStocks.RemoveAt(idx);
            UpdateNewsUI("[공시] 새로운 기업이 증권 시장 상장을 준비하고 있습니다.", Color.yellow);
            return;
        }

        // 3. 해킹
        if (UnityEngine.Random.value < newsHackingChance)
        {
            GenerateHiddenEvent();
            string[] errorMsgs = { "ERROR: 404 - Server Not Found", "※ 보안 시스템 경고: 데이터 접근 불가 ※", "System.. Hacked..", "통신망 불안정으로 뉴스 수신 지연" };
            UpdateNewsUI(errorMsgs[UnityEngine.Random.Range(0, errorMsgs.Length)], Color.green);
            return;
        }

        // 4. 메인 이벤트 (시나리오 vs 파급효과 vs 평화)
        // 시나리오
        float dice = UnityEngine.Random.value;
        if (marketStocks.Count > 0 && dice < scenarioChance)
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
                currentEvent = new PendingEvent { scenarioTargets = activeTargets, newsTitle = $"[속보] {scenario.title}", isGoodNews = scenario.isGoodNews, isMegaEvent = scenario.isMegaEvent };
                
                // 🌟 [핵심] 초대형 이벤트라면? -> 즉시 공개!
                if (scenario.isMegaEvent)
                {
                    // 1. 뉴스 티커에 내용 그대로 노출 (정보원 안 사도 됨)
                    UpdateNewsUI($"<color=red><b>[긴급 속보]</b></color> {scenario.title}", Color.yellow);
                    
                    // 2. 뒷거래 UI에도 "공개된 정보"라고 표시
                    if (UIManager.I != null)
                    {
                        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", 
                            $"<color=red><b>[GLOBAL NEWS]</b></color>\n전 세계 동시 보도!\n\n{scenario.title}\n\n(모든 투자자가 이 정보를 알고 있습니다)");
                    }
                    
                    // 3. 정보 구매 플래그를 true로 해서 더 이상 못 사게 함 (이미 알니까)
                    hasPlayerUsedInfo = true; 
                    
                    // 4. 알림 아이콘은 굳이 띄울 필요 없거나, 색을 다르게 할 수 있음
                    // (여기선 그냥 둠)
                }
                else
                {
                    // 일반 시나리오 (기존 로직)
                    string[] scenarioMsgs = { "주요 외신들이 긴급 타전을 보내고 있습니다.", "시장에 큰 영향을 미칠 이슈가 발생했습니다.", "글로벌 이슈로 인해 변동성이 확대될 전망입니다.", "투자자들의 이목이 집중되고 있습니다.", "중요한 뉴스가 전해져 시장이 술렁이고 있습니다." };
                    UpdateNewsUI(scenarioMsgs[UnityEngine.Random.Range(0, scenarioMsgs.Length)], Color.cyan);
                }
                return;
            }
        }
        else if (marketStocks.Count > 0 && dice < (scenarioChance + rippleEffectChance))
        {
            // 파급 효과
            RuntimeStock target = GetWeightedRandomStock();
            bool isGood = UnityEngine.Random.value > 0.5f;
            float multiplier = isGood ? UnityEngine.Random.Range(1.2f, 1.4f) : UnityEngine.Random.Range(0.7f, 0.85f);
            string template = isGood ? commonGoodNews[UnityEngine.Random.Range(0, commonGoodNews.Length)] : commonBadNews[UnityEngine.Random.Range(0, commonBadNews.Length)];

            currentEvent = new PendingEvent
            {
                singleTarget = target,
                singleMultiplier = multiplier,
                newsTitle = $"[동향] {target.data.stockName}, {template} (동종 업계 파급)",
                isGoodNews = isGood,

            };

            string[] rippleMsgs = { $"특정 기업의 이슈가 {target.data.sector} 업계 전반으로 확산되고 있습니다.", "한 종목의 급격한 변동이 시장에 나비 효과를 일으킵니다.", "업계 1위 기업의 행보에 투자자들의 이목이 쏠립니다.", "관련 업종 전반에 걸쳐 변동성이 확대되고 있습니다.", "시장에 파급 효과가 나타나고 있습니다." };
            UpdateNewsUI(rippleMsgs[UnityEngine.Random.Range(0, rippleMsgs.Length)], new Color(1f, 0.6f, 0.2f));
            return;
        }

        // 평화
        currentEvent = null;
        string[] peaceMsgs = { "시장은 평온합니다. 특별한 이슈가 없습니다.", "투자심리가 안정적입니다. 관망세가 이어집니다.", "폭풍전야일까요? 시장이 지나치게 조용합니다.", "개별 종목들의 자연스러운 등락이 이어지고 있습니다.", "오늘도 변함없는 하루가 지나갑니다. 특별한 뉴스는 없습니다.", "시장에 큰 변화는 없습니다. 일상적인 거래가 계속됩니다." };
        UpdateNewsUI(peaceMsgs[UnityEngine.Random.Range(0, peaceMsgs.Length)], Color.white);
    }
    void GenerateHiddenEvent()
    {
        if (marketStocks.Count > 0)
        {
            RuntimeStock target = GetWeightedRandomStock();
            bool isGood = UnityEngine.Random.value > 0.5f;
            float multiplier = isGood ? UnityEngine.Random.Range(1.5f, 2.0f) : UnityEngine.Random.Range(0.5f, 0.7f);
            currentEvent = new PendingEvent { singleTarget = target, singleMultiplier = multiplier, newsTitle = "???", isGoodNews = isGood, isHidden = true };
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

    string CorruptText(string original)
    {
        char[] chars = original.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsWhiteSpace(chars[i])) continue;
            if (UnityEngine.Random.value < 0.4f)
            {
                float r = UnityEngine.Random.value;
                if (r < 0.33f) chars[i] = '#';
                else if (r < 0.66f) chars[i] = '*';
                else chars[i] = '?';
            }
        }
        return new string(chars);
    }

    string GenerateFakeNews()
    {
        if (scenarioDatabase.Count > 0) return $"{scenarioDatabase[UnityEngine.Random.Range(0, scenarioDatabase.Count)].title}";
        return "외계인의 침공이 임박했습니다!";
    }

    #endregion

    #region Trading System

    void OnTrade(int mode)
    {
        if (selectedStock == null) return;
        if (selectedStock.isDelisting && (mode == 0 || mode == 2)) { Debug.LogWarning("정리 매매 중 매수/공매도 불가"); return; }

        int amount = UIManager.I.GetInputValueInt("TradePanel", "Input_Amount");
        if (amount <= 0) return;

        long cost = (long)selectedStock.currentPrice * amount;
        bool tradeSuccess = false;
        bool isBuyingPressure = true;

        // 👻 [수정] 브로커 정산용 변수 선언 (OnTrade 내에서는 이제 정산하지 않음)
        long totalFee = 0;
        long netProfit = 0; // 이 변수들은 이제 매도 로직(case 1)에서 사용되지 않으므로 제거 가능하지만, 안전을 위해 둠.

        switch (mode)
        {
            case 0: // 매수
                if (player.money >= cost && selectedStock.remainShares >= amount)
                {
                    player.money -= cost; selectedStock.remainShares -= amount; 
                    
                    // 👻 [핵심 구현] 브로커 정보 사용 시 계약 등록 (1턴 귀속)
                    if (wasLastInfoBroker)
                    {
                        // 1. 계약 생성 (PlayerPortfolio 대신 StockMarketManager에 등록)
                        long costBasisPerShare = cost / amount;
                        activeBrokerContract = new BrokerContract { data = selectedStock.data, amount = amount, costBasis = costBasisPerShare };
                        
                        // 2. 일반 주식 목록에 추가 (턴 종료 시 시스템이 회수)
                        player.AddStock(selectedStock.data, amount);
                        
                        wasLastInfoBroker = false; // 플래그 리셋
                        Debug.Log($"👻 [브로커] {selectedStock.data.stockName} {amount}주, 1턴 계약 체결됨.");
                    }
                    else
                    {
                        player.AddStock(selectedStock.data, amount); // 일반 매수
                    }
                    tradeSuccess = true; isBuyingPressure = true;
                }
                break;
            case 1: // 매도 (👻 브로커 주식은 이 함수로 팔 수 없습니다. 턴 종료 시 시스템이 회수)
                // 브로커 계약 주식과 일반 주식의 혼합 매매는 허용하지 않음 (복잡성 제거)
                // 플레이어가 계약 주식을 팔아도, 그 계약은 턴 종료 시점에 무시되고 강제 정산됨.
                
                // 다만, 일반 매도는 허용
                if (player.GetStockCount(selectedStock.data) >= amount)
                {
                    // 🚨 [주의] 브로커 계약이 걸린 주식은 매도 불가!
                    if (activeBrokerContract.HasValue && activeBrokerContract.Value.data == selectedStock.data)
                    {
                         Debug.LogWarning("👻 [브로커 금지] 계약된 주식은 턴 종료 전까지 매도할 수 없습니다.");
                         return; // 매도 금지
                    }

                    // 일반 주식 매도 실행
                    player.money += cost; 
                    selectedStock.remainShares += amount; 
                    player.RemoveStock(selectedStock.data, amount);
                    
                    tradeSuccess = true; isBuyingPressure = false;
                }
                break;
            case 2: // 공매도
                if (selectedStock.remainShares >= amount)
                {
                    player.money += cost; selectedStock.remainShares -= amount; player.AddShort(selectedStock.data, amount);
                    tradeSuccess = true; isBuyingPressure = false;
                }
                break;
            case 3: // 숏커버
                if (player.GetShortCount(selectedStock.data) >= amount && player.money >= cost)
                {
                    player.money -= cost; selectedStock.remainShares += amount; player.RemoveShort(selectedStock.data, amount);
                    tradeSuccess = true; isBuyingPressure = true;
                }
                break;
        }

        if (tradeSuccess)
        {
            // ➕ [추가] 플레이어 행동 기록
            string stockName = selectedStock.data.stockName;
            switch (mode)
            {
                case 0: player.SetLastAction($"<b>[{stockName}]</b> 매수"); break;
                case 1: player.SetLastAction($"<b>[{stockName}]</b> 매도"); break;
                case 2: player.SetLastAction($"<b>[{stockName}]</b> 공매도"); break;
                case 3: player.SetLastAction($"<b>[{stockName}]</b> 공매도 상환"); break;
            }
            ApplyMarketImpact(selectedStock, amount, isBuyingPressure);
            UpdateTradePanelUI(); UpdateStockBoardUI(); UpdatePlayerMoneyUI(); UpdatePortfolioUI();
        }
    }

    // 🔴 [누락된 메서드 추가] 외부에서 시장 가격에 영향을 주는 함수
    public void ApplyMarketImpact(RuntimeStock stock, int amount, bool isBuyingPressure)
    {
        if (stock == null || stock.currentPrice <= 0) return;

        float ratio = (float)amount / stock.data.totalShares;
        float changePercent = ratio * impactSensitivity;

        if (!isBuyingPressure) changePercent *= -1;

        int priceChange = (int)(stock.currentPrice * changePercent);
        
        if (priceChange == 0 && amount > 0) 
            priceChange = isBuyingPressure ? 1 : -1;

        stock.currentPrice += priceChange;
        ClampStockPrice(stock);
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
            case 0: // 🤥 사기꾼 (T0): 사실상 정보상이 아님.
                double scammerstRate = threshold > 100000000 ? 10 : 1;
                return baseCostScammer * (long)scammerstRate;

            case 5: // 🗞️ 신문팔이 (T1): 거짓일 경우에 큰 손해를 보지만, 소소한 이득을 볼 수도 있음.
                double newsboyRate = 0.001; 
                return baseCostNewsboy + (long)(totalAsset * newsboyRate);
            case 1: // 📊 분석가 (T1): 어떤 기업인지 모른다는 것이 매우 크게 작용
                double analystRate = 0.010; // 1.0%
                return baseCostAnalyst + (long)(totalAsset * analystRate);
                
            case 2: // 💻 해커 (T2): 해독만 되면 브로커급의 정보를 얻음
                double hackerRate = 0.0085;
                return baseCostHacker + (long)(totalAsset * hackerRate);
            case 6: // 🏛️ 로비스트 (T2): 파급효과일 떄, 이득을 크게 보지만 경쟁사끼리의 대결일 때, 도박이 됨.
                double lobbyRate = 0.0075; 
                return baseCostLobbyist + (long)(totalAsset * lobbyRate);    
              
            case 4: // 🕵️ 스파이 (T3): 1위의 행동을 알 수 있지만, 작전 세력에게 당할 수 있음
                double spyRate = 0.015; 
                return baseCostSpy + (long)(totalAsset * spyRate);
            case 7: // 👻 브로커 (T3): 확실한 뉴스 정보를 얻지만, 이득을 얻지 못 하면 페널티가 큼.
                double brokerRate = 0.001; 
                long perCostBroker = (long)(totalAsset * brokerRate);
                return baseCostBroker > perCostBroker ? baseCostBroker : perCostBroker;    
            
            case 3: // 🏢 내부자 (T4): 압도적으로 비싸지만 확실한 정보를 얻음
                double insiderRate = 0.0375;
                return baseCostInsider + (long)(totalAsset * insiderRate);
        }
        return 0;
    }

    public PublicEventInfo GetInfoForAI(AIInvestor ai, int infoTier)
    {
        // 🌟 [신규] 초대형 이벤트(공개 정보)라면 비용 면제 & 100% 정확한 정보 제공
        if (currentEvent.HasValue && currentEvent.Value.isMegaEvent)
        {
            PublicEventInfo publicInfo = new PublicEventInfo();
            publicInfo.hasEvent = true;
            publicInfo.eventTitle = currentEvent.Value.newsTitle;
            publicInfo.isGoodNews = currentEvent.Value.isGoodNews;
            publicInfo.targets = currentEvent.Value.scenarioTargets; // 타겟도 다 공개됨
            return publicInfo;
        }

        PublicEventInfo info = new PublicEventInfo { hasEvent = false, targets = new Dictionary<RuntimeStock, float>() };
        long aiAssets = ai.money - ai.currentDebt;
        foreach (var kvp in ai.portfolio)
        {
            RuntimeStock stock = marketStocks.Find(s => s.data == kvp.Key);
            if (stock != null) aiAssets += (long)stock.currentPrice * kvp.Value;
        }

        long cost = CalculateInfoCost(infoTier, aiAssets);
        if (ai.money < cost) return info;
        ai.money -= cost;

        if (!currentEvent.HasValue || currentEvent.Value.isHidden) return info;

        var evt = currentEvent.Value;
        info.hasEvent = true;
        info.eventTitle = evt.newsTitle;
        info.isGoodNews = evt.isGoodNews;

        Dictionary<RuntimeStock, float> realTargets = new Dictionary<RuntimeStock, float>();
        if (evt.singleTarget != null)
        {
            realTargets.Add(evt.singleTarget, evt.singleMultiplier);
            if (evt.isRippleEvent)
            {
                foreach (var s in marketStocks)
                    if (s != evt.singleTarget && s.data.sector == evt.singleTarget.data.sector)
                        realTargets.Add(s, evt.singleMultiplier * 0.4f);
            }
        }
        else if (evt.scenarioTargets != null) realTargets = evt.scenarioTargets;

        switch (infoTier)
        {
            case 0: // 사기꾼
                if (UnityEngine.Random.value > 0.8f) info.targets = realTargets;
                else
                {
                    info.isGoodNews = !evt.isGoodNews;
                    var fake = GetWeightedRandomStock();
                    if (fake != null) info.targets.Add(fake, info.isGoodNews ? 1.5f : 0.6f);
                }
                break;
            case 1: info.targets = realTargets; break; // 분석가 (AI 해석 로직에 맡김)
            case 2: info.targets = realTargets; break; // 해커 (AI 해석 로직에 맡김)
            case 3: info.targets = realTargets; break; // 내부자
        }
        return info;
    }

    // -------------------------------------------------------------
    // [수정] 패널 열 때: 알림 끄기 + 텍스트 갱신
    // -------------------------------------------------------------
    public void ToggleInfoTradingPanel(bool isOpen)
    {
        if (!UIManager.I.TrySetActive(UI_GROUP_INFOTRADE, "Panel_InfoTrading", isOpen)) return;
        
        if (isOpen)
        {
            // ✨ 패널을 열었으므로 알림 아이콘 끄기
            HideNotificationIcon();

            // 정보를 아직 안 샀다면 기본 텍스트와 가격 표시
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
            $"<color=#6F4F28>[신문팔이]</color>: <size=75%>이 아이는 정보를 아무거나 줍니다. <color=red>호재인지 악재인지는 불확실합니다.</color></size>\n" +
            $"<color=yellow>[분석가]</color>: <size=75%>그녀는 사건의 냄새를 잘 맡습니다. <color=red>하지만, 그게 누군진 모릅니다.</color></size>\n" +
            $"<color=blue>[해커]</color>: <size=75%>그의 해킹 실력은 대단하지만, <color=red>흠, 일부 텍스트가 깨진 것같군요?</color></size>\n" +
            $"<color=#8B4513>[로비스트]</color>: <size=75%>그들은 정책 변화를 분석하여 <color=green>섹터 전체의 흐름</color>을 예측합니다.</size>\n" +
            $"<color=purple>[첩보원]</color>: <size=75%>그녀는 현재 <color=green>자산 1위 투자자</color>의 최근 행적을 파헤칩니다. 무섭군요..</size>\n" +
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
        "놓치지 마세요! 오늘의 헤드라인을 확인하세요!",
    };

    void OnClickScammer()
    {
        if (IsInfoUsed()) return;
        long cost = CalculateInfoCost(0, player.GetTotalAsset());
        if (player.money < cost) { UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=white>[사기꾼]</color>\n이봐, 돈은 좀 들고 다녀라. 껌값도 없냐?"); return; }
        player.money -= cost; UpdatePlayerMoneyUI();

        bool isTruth = UnityEngine.Random.value > 0.9f;
        string msg = (currentEvent.HasValue && !currentEvent.Value.isHidden)
            ? (isTruth ? currentEvent.Value.newsTitle : GenerateFakeNews())
            : (isTruth ? "<color=white>[사기꾼]</color>\n아직은 너무 조용해. 빅 이슈가 일어나면 다시 날 찾아달라고?" : GenerateFakeNews());

        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", $"<color=white>[사기꾼]</color>\n{msg}\n({ScammerTalks[UnityEngine.Random.Range(0, ScammerTalks.Length)]})");
        hasPlayerUsedInfo = true;
    }

    void OnClickAnalyst()
    {
        if (IsInfoUsed()) return;
        long cost = CalculateInfoCost(1, player.GetTotalAsset());
        if (player.money < cost) { UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=yellow>[분석가]</color>\n먼저 충분한 돈부터 보여주세요. 가난뱅이씨."); return; }
        player.money -= cost; UpdatePlayerMoneyUI();

        if (currentEvent.HasValue && !currentEvent.Value.isHidden)
        {
            string blinded = BlindText(currentEvent.Value.newsTitle, currentEvent.Value);
            string sentiment = currentEvent.Value.isGoodNews ? "<color=red>매수(Buy)</color>" : "<color=blue>매도(Sell)</color>";
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", $"<color=yellow>[분석가]</color>\n\n[REPORT]\n{blinded}\n의견: {sentiment}");
        }
        else UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=yellow>[분석가]</color>\n\n조용해요. 너무나도.. 곧 큰 일이 일어날 것만 같네요.");
        hasPlayerUsedInfo = true;
    }

    void OnClickHacker()
    {
        if (IsInfoUsed()) return;
        long cost = CalculateInfoCost(2, player.GetTotalAsset());
        if (player.money < cost) { UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=blue>[해커]</color>\n아저씨, 설마 공짜로 정보를 원하는 건 아닐 거잖아? 그치?"); return; }
        player.money -= cost; UpdatePlayerMoneyUI();

        if (currentEvent.HasValue && !currentEvent.Value.isHidden)
        {
            string corrupted = CorruptText(currentEvent.Value.newsTitle);
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", $"<color=blue>[해커]</color>\n>_ {corrupted}");
        }
        else UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=blue>[해커]</color>\n아쉽게 됐지. 뭐. 시장의 큰 움직임은 없었어. 나중에 또 찾아와.");
        hasPlayerUsedInfo = true;
    }

    void OnClickInsider()
    {
        if (IsInfoUsed()) return;
        long cost = CalculateInfoCost(3, player.GetTotalAsset());
        if (player.money < cost) { UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=orange>[내부자]</color>\n저희는 충분한 자금을 조달해줄 VIP분들만 모십니다."); return; }
        player.money -= cost; UpdatePlayerMoneyUI();

        if (currentEvent.HasValue && !currentEvent.Value.isHidden)
        {
            var evt = currentEvent.Value;
            string msg = $"<color=orange>[내부자]</color>\n{evt.newsTitle}\n";
            if (evt.singleTarget != null) msg += $"- 타겟: {evt.singleTarget.data.stockName} ({(evt.isGoodNews ? "▲" : "▼")})";
            else if (evt.scenarioTargets != null) msg += "- 다수 종목 영향 확인";
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", msg);
        }
        else UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", "<color=orange>[내부자]</color>\n고객님, 현재로서는 특별한 내부 정보가 없습니다. 시장은 조용합니다.");
        hasPlayerUsedInfo = true;
    }

    // 🕵️‍♂️ [신규] 첩보원 버튼 클릭 시
    void OnClickSpy()
    {
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

    // 🗞️ [수정] 신문팔이 버튼 클릭 시 (랜덤 기업 정보 제공)
    void OnClickNewsboy()
    {
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

        // 결제
        player.money -= cost;
        UpdatePlayerMoneyUI();

        if (marketStocks.Count == 0) return;

        // 1. 상장된 기업 중 무작위 1개 선정 (뉴스와 무관)
        RuntimeStock targetStock = marketStocks[UnityEngine.Random.Range(0, marketStocks.Count)];
        
        float fluctuationValue = targetStock.data.volatility; // 기본값: 종목 고유 변동성
        bool isActuallyGood = UnityEngine.Random.value > 0.5f; // 기본값: 50% 확률 (이벤트 없을 때)
        bool hasActiveEventImpact = false;

        // 2. 만약 우연히 이 기업이 '현재 사건'의 당사자라면 정확한 수치 가져오기
        if (currentEvent.HasValue && !currentEvent.Value.isHidden)
        {
            var evt = currentEvent.Value;
            
            // 직접 타겟인 경우
            if (evt.singleTarget == targetStock)
            {
                fluctuationValue = evt.singleMultiplier;
                isActuallyGood = evt.isGoodNews;
                hasActiveEventImpact = true;
            }
            // 시나리오 타겟 중 하나인 경우
            else if (evt.scenarioTargets != null && evt.scenarioTargets.ContainsKey(targetStock))
            {
                fluctuationValue = evt.scenarioTargets[targetStock];
                isActuallyGood = fluctuationValue >= 1.0f; // 배율이 1보다 크면 호재
                hasActiveEventImpact = true;
            }
            // 파급 효과(Ripple) 대상인 경우 (같은 섹터)
            else if (evt.isRippleEvent && evt.singleTarget != null && evt.singleTarget.data.sector == targetStock.data.sector)
            {
                // 파급 효과는 보통 원본 배율의 40% 정도 영향
                fluctuationValue = evt.singleMultiplier * 0.4f; 
                isActuallyGood = evt.isGoodNews;
                hasActiveEventImpact = true;
            }
        }

        // 3. 텍스트 포맷팅 (이벤트면 '배수', 평소면 '퍼센트')
        string fluctuationStr = hasActiveEventImpact 
            ? $"{fluctuationValue:F2}배" 
            : $"약 {fluctuationValue * 100:F1}%";

        // 4. 🎲 진실 게임 (40% 확률로 진실을 말함)
        // (이벤트가 없으면 어차피 랜덤이므로 '찍기'가 됨)
        bool sayTruth = UnityEngine.Random.value < 0.4f;
        string predictedDirection = (isActuallyGood == sayTruth) ? "상승(▲)" : "하락(▼)";

        // 5. 결과 출력
        string resultMsg = $"<color=#6F4F28>[신문팔이]</color>\n" +
                           $"{NewsboyTalks[UnityEngine.Random.Range(0, NewsboyTalks.Length)]}\n\n" +
                           $"<b>[{targetStock.data.stockName}]</b> 종목이 심상치 않아요.\n" +
                           $"예상 변동폭은.. <color=red><b>[{fluctuationStr}]</b></color>!\n\n" +
                           $"제 감으로는.. 이번엔 <color=blue>{predictedDirection}</color> 할 것 같아요.</size>";
        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", resultMsg);
        hasPlayerUsedInfo = true;
    }

    // 🏛️ [신규] 로비스트 버튼 클릭 시
    void OnClickLobbyist()
    {
        if (IsInfoUsed()) return;

        long myTotalAsset = player.GetTotalAsset();
        long cost = CalculateInfoCost(6, myTotalAsset); // Tier 6

        // 돈 부족 확인
        if (player.money < cost) 
        { 
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", 
                $"<color=#8B4513>[로비스트]</color>\n착수금이 부족합니다. 의원님들은 현금으로만 움직이십니다."); 
            return; 
        }

        // 결제
        player.money -= cost; 
        UpdatePlayerMoneyUI();

        string resultMsg = $"<color=#8B4513>[로비스트]</color>\n뒷배경이 튼튼한 분께만 드리는 정보입니다.\n\n";

        if (!currentEvent.HasValue || currentEvent.Value.isHidden)
        {
            resultMsg += $"지금은 의회도 조용합니다. 다음 회기가 열릴 때까지 기다리시죠.";
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", resultMsg);
            hasPlayerUsedInfo = true;
            return;
        }

        // --- 핵심 로직: 섹터별 영향도 집계 ---
        var evt = currentEvent.Value;
        Dictionary<StockSector, float> sectorImpacts = new Dictionary<StockSector, float>();
        
        // 1. 타겟 집계 (단일 타겟 혹은 시나리오 타겟 모두 포함)
        var targets = evt.scenarioTargets ?? new Dictionary<RuntimeStock, float>();
        if (evt.singleTarget != null && !targets.ContainsKey(evt.singleTarget)) 
            targets.Add(evt.singleTarget, evt.singleMultiplier);

        // 2. 섹터별 순 영향도 계산 (호재는 +, 악재는 -)
        foreach (var kvp in targets)
        {
            StockSector sector = kvp.Key.data.sector;
            float multiplier = kvp.Value;
            float impact = multiplier - 1.0f; // 1.5배 -> +0.5 영향 / 0.7배 -> -0.3 영향

            if (!sectorImpacts.ContainsKey(sector)) sectorImpacts[sector] = 0;
            sectorImpacts[sector] += impact;
        }

        // 3. 가장 큰 절대적인 영향력을 가진 섹터 찾기
        StockSector mostImpactedSector = StockSector.IT; 
        float maxImpactMagnitude = 0;

        foreach (var kvp in sectorImpacts)
        {
            float magnitude = Mathf.Abs(kvp.Value);
            if (magnitude > maxImpactMagnitude)
            {
                maxImpactMagnitude = magnitude;
                mostImpactedSector = kvp.Key;
            }
        }
        
        // 4. 결과 메시지 생성
        float overallSectorImpact = sectorImpacts.ContainsKey(mostImpactedSector) ? sectorImpacts[mostImpactedSector] : 0;
        bool isSectorGoodNews = overallSectorImpact >= 0;
        string directionStr = isSectorGoodNews ? "<color=red>강력한 호재</color>" : "<color=blue>치명적 악재</color>";
        string sectorNameStr = mostImpactedSector.ToString();

        resultMsg += $"[비밀 의회 보고서]\n" +
                     $"이번 이슈는 <b><color=yellow>{sectorNameStr}</color></b> 섹터에 가장 큰 영향을 미칩니다.\n" +
                     $"예상되는 섹터 전반의 파급 효과는 {directionStr}입니다.</size>";

        UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", resultMsg);
        hasPlayerUsedInfo = true;
    }

    // 👻 [신규] 브로커 버튼 클릭 시 (Tier 7)
    void OnClickBroker()
    {
        if (IsInfoUsed()) return;

        long cost = CalculateInfoCost(7, player.GetTotalAsset()); // Tier 7: 10,000원

        // 돈 부족 확인 (1만원만 있어도 가능)
        if (player.money < cost) 
        { 
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", 
                $"<color=#FBCEB1>[브로커]</color>\n착수금 {cost:N0}원도 없으시군요. 빚내서라도 오시지오."); 
            return; 
        }

        // 🚨 [중요 체크] 이미 1턴 계약을 맺은 상태면 추가 계약 불가
        if (activeBrokerContract.HasValue)
        {
             UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", 
                $"<color=#FBCEB1>[브로커]</color>\n이미 <b>{activeBrokerContract.Value.data.stockName}</b>과(와) 계약 진행 중이오. 한 번에 하나만 하시오.");
            return;
        }

        // 결제 및 플래그 설정
        player.money -= cost; 
        UpdatePlayerMoneyUI();
        wasLastInfoBroker = true; // 👻 [핵심] 다음 OnTrade(0)에서 계약 생성 대기 플래그
        
        // 👻 [기능] 내부자(Insider)급 정보 제공
        if (currentEvent.HasValue && !currentEvent.Value.isHidden)
        {
            var evt = currentEvent.Value;
            string msg = $"<color=#FBCEB1>[브로커]</color>\n<color=red>이용료는 10,000원입니다. 그 후, 수익의 85%를 잊지 마시지요.</color>\n\n" +
                         $"[내부 정보]\n{evt.newsTitle}\n";

            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", msg);
        }
        else 
        {
            UIManager.I.TrySetText(UI_GROUP_INFOTRADE, "Txt_Result", 
                $"<color=#FBCEB1>[브로커]</color>\n{cost:N0}원 결제되었소. 하지만, 현재는 시장에 큰 움직임이 없소.");
        }

        hasPlayerUsedInfo = true;
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

                UIManager.I.TrySetText(UI_GROUP_BOARD, $"Name_{i}", $"{stock.data.stockName} <color=blue><b><size=60%>[{stock.data.symbol}]</size></b></color>\n<size=70%><color=#AAAAAA>잔여: {stock.remainShares}주</color></size>");
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
        long totalDividend = 0; // ➕ [신규] 총 배당금 계산용 변수

        var myHoldings = player.GetHoldings();
        var holdingList = myHoldings.Keys.ToList();

        // 1. 보유 주식(Long) 루프: 평가액 및 배당금 계산
        for (int i = 0; i < maxUISlots; i++)
        {
            if (i < holdingList.Count)
            {
                StockData data = holdingList[i];
                int amount = myHoldings[data];
                RuntimeStock currentStock = marketStocks.Find(s => s.data == data);
                int price = (currentStock != null) ? currentStock.currentPrice : 0;
                long valuation = (long)price * amount;
                
                totalStockValue += valuation;

                // ➕ 배당금 누적 (주당 배당금 * 보유 수량)
                if (data.dividendPerShare > 0)
                {
                    totalDividend += (long)data.dividendPerShare * amount;
                }

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

        // 2. ➕ [신규] 공매도 부채(Short Debt) 계산
        long totalShortDebt = 0;
        var shorts = player.GetShortPositions();
        foreach (var item in shorts)
        {
            RuntimeStock stock = marketStocks.Find(s => s.data == item.Key);
            if (stock != null)
            {
                totalShortDebt += (long)stock.currentPrice * item.Value;
            }
        }

        // 3. 하단 텍스트 갱신
        long totalAsset = player.money + totalStockValue;
        
        UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, "Txt_Cash", $"{player.money:N0} 원");

        // 👇 [수정] 주식 자산 (공매도 부채 + 예상 배당금) 표시
        // 예: 1,000,000 원 (-500,000 / +20,000)
        if (totalDividend-totalShortDebt >= 0)
            UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, "Txt_StockVal", 
            $"{totalStockValue:N0} 원\n(<color=red>+{totalDividend-totalShortDebt:N0}원</color>)");
        else
        UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, "Txt_StockVal", 
            $"{totalStockValue:N0} 원\n(<color=blue>{totalDividend-totalShortDebt:N0}원</color>)");
            
        UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, "Txt_TotalAsset", $"{totalAsset:N0} 원");
    }

    void UpdateTradePanelUI()
    {
        if (selectedStock == null) return;
        StockData data = selectedStock.data;
        string divStr = data.dividendPerShare > 0 ? $"{data.dividendPerShare:N0}원" : "0원";

        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_Title", $"{data.stockName}");
        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_Desc", data.description);
        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_StartPrice", $"초기 주가: {data.startPrice:N0}원");
        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_TotalShares", $"초기 물량: {data.totalShares:N0} <size=75%>(배당금: {divStr})</size>");

        // 1. 보유 주식(Long) 가져오기
        int myCount = player.GetStockCount(data);
        
        // 2. 공매도 주식(Short) 가져오기
        int myShortCount = player.GetShortCount(data);

        // 3. 평가 금액 (Long 포지션 기준)
        long val = (long)myCount * selectedStock.currentPrice;

        // 4. 공매도 표시 문자열 생성 (수량이 있으면 빨간색, 없으면 회색)
        string shortInfo = myShortCount > 0 
            ? $"<color=#FF6666>(공매도: {myShortCount:N0}주)</color>" 
            : $"<size=80%><color=black>(공매도: 0주)</color></size>";

        // 5. 텍스트 조합 및 적용
        string infoText = $"현재가: <color=yellow>{selectedStock.currentPrice:N0}원</color>\n" +
                        $"잔여 주: {selectedStock.remainShares:N0}주\n" +
                        $"보유: <color=green>{myCount:N0}주</color> {shortInfo}\n" +
                        $"가치: {val:N0}원<size=75%> (배당금: {((long)data.dividendPerShare * myCount):N0}원)</size>";
                          

        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_Info", infoText);

        // 🛠️ [수정됨] 슬라이더 최대값(Max) 계산 로직
        // 기존: 기업의 남은 주식(remainShares)까지 포함하여 슬라이더가 너무 커짐
        // 변경: 내 '돈으로 살 수 있는 양' vs '내 보유량' 중 큰 값으로 제한

        long maxBuyable = 0;
        if (selectedStock.currentPrice > 0)
            maxBuyable = player.money / selectedStock.currentPrice; // 내 돈으로 살 수 있는 최대치

        long maxSellable = myCount;      // 내가 팔 수 있는 최대치
        long maxCoverable = myShortCount; // 내가 갚아야 할 공매도 수량

        // 👇 [핵심 변경] 기업 잔여량(selectedStock.remainShares)을 제외했습니다.
        // 이제 슬라이더는 철저히 '플레이어의 능력(자금력, 보유력)' 안에서만 움직입니다.
        long sliderMax = Math.Max(maxBuyable, Math.Max(maxSellable, maxCoverable));
        
        // 최소값 보정 (살 돈도 없고 가진 것도 없으면 1로 설정하여 UI 오류 방지, 어차피 구매 불가)
        if (sliderMax <= 0) sliderMax = 1;
        if (sliderMax > int.MaxValue) sliderMax = int.MaxValue;

        // 👇 [수정] 슬라이더 범위 설정 (0 -> 1 로 변경)
        UIManager.I.TrySetSliderMinMax(UI_GROUP_POPUP, UI_NAME_SLIDER_AMOUNT, 1, sliderMax);
        
        // 현재 입력창에 있는 값이 슬라이더 범위 밖이면 조정
        int currentInput = UIManager.I.GetInputValueInt(UI_GROUP_POPUP, UI_NAME_INPUT_AMOUNT);
        if (currentInput > sliderMax)
        {
            currentInput = (int)sliderMax;
            UIManager.I.TrySetInputValue(UI_GROUP_POPUP, UI_NAME_INPUT_AMOUNT, currentInput.ToString());
        }
        
        // 슬라이더 현재 값 동기화
        UIManager.I.TrySetSliderValue(UI_GROUP_POPUP, UI_NAME_SLIDER_AMOUNT, currentInput);
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

    void OnClickBorrow() { long amt = GetLoanInputAmount(); if (amt > 0 && player.BorrowMoney(amt)) { UpdateLoanPanelUI(); UpdatePlayerMoneyUI(); UpdatePortfolioUI(); } }
    void OnClickRepay() { long amt = GetLoanInputAmount(); if (amt > 0 && player.RepayMoney(amt)) { UpdateLoanPanelUI(); UpdatePlayerMoneyUI(); UpdatePortfolioUI(); } }
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
}