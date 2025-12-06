using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

// 📈 런타임 주식 객체 (실시간 데이터)
[System.Serializable]
public class RuntimeStock
{
    public StockData data;
    public int currentPrice;
    public int previousPrice;
    public int remainShares; 
    public bool isDelisting; // ➕ [신규] 정리 매매(상폐 예고) 상태

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
    public string title;       // 뉴스 제목
    public bool isGoodNews;    // 호재/악재 (색상용)
    // 타겟 심볼과 변동 배율 (예: "FLUX", 2.0f)
    public Dictionary<string, float> targets = new Dictionary<string, float>(); 

    public ScenarioEvent(string _title, bool _isGood)
    {
        title = _title;
        isGoodNews = _isGood;
    }

    public void AddTarget(string symbol, float multiplier)
    {
        if (!targets.ContainsKey(symbol)) targets.Add(symbol, multiplier);
    }
}

public class StockMarketManager : MonoBehaviour
{
    [Header("Systems")]
    public PlayerPortfolio player;

    [Header("Game Data")]
    public List<StockData> stockDataList; 
    [Header("Upcoming Listings (IPO)")]
    public List<StockData> upcomingStocks;

    [Header("Runtime State")]
    public List<RuntimeStock> marketStocks = new List<RuntimeStock>();
    private RuntimeStock selectedStock;

    // 섹터 필터링
    private StockSector currentSectorFilter = StockSector.IT;
    private List<RuntimeStock> DisplayedStocks
    {
        get { return marketStocks.Where(s => s.data.sector == currentSectorFilter).ToList(); }
    }

    [Header("Settings")]
    public float updateInterval = 5.0f;
    public int maxUISlots = 10;

    // ➕ [신규] 세금 및 경제 설정
    [Header("Economy Settings")]
    public int taxIntervalTurns = 3; // 3턴마다 세금
    private int currentTaxTurn = 0;

    [Header("Event Settings")]
    [Range(0f, 1f)] public float eventChance = 0.35f; // 35% 확률로 이벤트 발생
    [Range(0f, 0.1f)] public float bankruptcyChance = 0.003f; // 0.3% 확률로 파산
    [Range(0f, 0.5f)] public float listingChance = 0.1f; // 10% 확률로 신규 상장
    [Range(0f, 0.75f)] public float scenarioChance = 0.2f; // 20% 확률로 시나리오 이벤트
    [Range(0f, 0.5f)] public float rippleEffectChance = 0.2f; // 20% 확률로 파급 효과

    [Header("Special Events")]
    [Range(0f, 0.1f)] public float newsHackingChance = 0.05f; // 5% 확률로 뉴스 해킹

    // UI 그룹 상수
    private const string UI_GROUP_BOARD = "MarketBoard"; 
    private const string UI_GROUP_POPUP = "TradePanel";
    private const string UI_GROUP_INFO = "CompanyInfoPanel"; // ℹ️ 상세 정보 패널
    private const string UI_GROUP_PLAYER = "PlayerInfo"; 
    private const string UI_GROUP_PORTFOLIO = "PortfolioPanel";
    private const string UI_GROUP_NEWS = "NewsPanel";
    private const string UI_GROUP_SECTOR = "SectorPanel";
    private const string UI_GROUP_GAMEOVER = "GameOverPanel";
    private const string UI_GROUP_LOAN = "LoanPanel"; // ➕ [신규] 대출 패널 그룹

    // 내부 이벤트 처리용 구조체
    private struct PendingEvent
    {
        public RuntimeStock singleTarget; 
        public float singleMultiplier;
        public Dictionary<RuntimeStock, float> scenarioTargets; // 시나리오용 다중 타겟

        public StockSector targetSector; // 섹터 이벤트용
        public string newsTitle;         
        public bool isGoodNews;          
        public bool isBankruptcy; 
        public bool isListing;
        public bool isSectorEvent;
        public bool isRippleEvent;
    }
    private PendingEvent? currentEvent = null;

    // 📜 시나리오 데이터베이스
    private List<ScenarioEvent> scenarioDatabase = new List<ScenarioEvent>();
    private AIInvestorManager aiManager; // 👈 AI 매니저 참조 추가

    // 일반 뉴스 템플릿
    private readonly string[] bankruptcyNews = { "분식회계 적발!", "CEO 횡령 및 도주!", "최종 부도 처리!", "상장 폐지 결정!", "법정 관리 신청!" };
    private readonly string[] listingNews = { "IPO 대박 조짐!", "증권 시장 정식 상장!", "투자자들의 뜨거운 관심!", "거래 개시 카운트다운!" };
    private readonly string[] commonGoodNews = { "사상 최대 실적!", "외국인 대량 매수!", "신기술 특허 취득!", "파격 주주 환원!" };
    private readonly string[] commonBadNews = { "검찰 압수수색!", "부품 공급 중단!", "치명적 결함 리콜!", "어닝 쇼크!" };

    // 섹터 뉴스 템플릿
    private Dictionary<StockSector, string[]> sectorGoodNews = new Dictionary<StockSector, string[]>
    {
        { StockSector.IT, new string[] { "AI 기술 혁신!", "양자 컴퓨터 상용화!", "데이터 센터 증설!" } },
        { StockSector.Bio, new string[] { "신약 임상 통과!", "불치병 치료제 개발!", "기술 수출 대박!" } },
        { StockSector.Automotive, new string[] { "완전 자율주행 성공!", "전고체 배터리 탑재!", "플라잉카 규제 해제!" } },
        { StockSector.Food, new string[] { "슈퍼푸드 개발!", "K-푸드 열풍!", "생산량 10배 증가!" } },
        { StockSector.Energy, new string[] { "무한 청정 에너지!", "초전도체 성공!", "신규 자원 발견!" } },
        { StockSector.Game, new string[] { "메타크리틱 99점!", "동접 1억 돌파!", "e스포츠 올림픽 채택!" } }
    };
    private Dictionary<StockSector, string[]> sectorBadNews = new Dictionary<StockSector, string[]>
    {
        { StockSector.IT, new string[] { "해킹으로 정보 유출!", "AI 윤리 논란!", "서버 화재 발생!" } },
        { StockSector.Bio, new string[] { "임상 부작용 속출!", "신약 허가 반려!", "윤리적 논란 심화!" } },
        { StockSector.Automotive, new string[] { "브레이크 결함 리콜!", "배터리 화재!", "환경 규제 강화!" } },
        { StockSector.Food, new string[] { "발암 물질 검출!", "식중독 사고!", "원재료값 폭등!" } },
        { StockSector.Energy, new string[] { "방사능 누출 의심!", "유가 폭락!", "발전소 가동 중단!" } },
        { StockSector.Game, new string[] { "확률 조작 적발!", "서버 다운!", "표절 논란!" } }
    };

    void Start()
    {
        if (player == null) player = FindAnyObjectByType<PlayerPortfolio>();
        aiManager = FindAnyObjectByType<AIInvestorManager>(); // 👈 AI 매니저 찾기
        
        InitializeMarket();
        InitializeScenarios(); // 시나리오 60개 로드
        InitializeUIEvents();
        
        UpdateStockBoardUI();
        UpdatePlayerMoneyUI();
        UpdatePortfolioUI();
        
        ToggleTradePanel(false);
        ToggleInfoPanel(false);
        
        UpdateNewsUI("시장이 개장했습니다.", Color.white);
        
        StartCoroutine(UpdateMarketPrices());
    }

    // 🌟 [핵심] 60가지 기업 서사 시나리오 정의
    void InitializeScenarios()
    {
        scenarioDatabase.Clear();

        // 1. 기술 및 산업 혁명
        var battery = new ScenarioEvent("차세대 퀀텀 배터리 효율 500% 달성! 에너지 혁명 시작!", true);
        battery.AddTarget("FLUX", 2.5f); battery.AddTarget("PRIO", 1.4f); battery.AddTarget("SKGL", 1.4f); battery.AddTarget("NEXS", 1.3f); battery.AddTarget("ORGN", 0.7f);
        scenarioDatabase.Add(battery);

        var aiAwakening = new ScenarioEvent("AI 시스템, 인간 통제 거부 징후 포착! '스카이넷' 현실화?", false);
        aiAwakening.AddTarget("CSMC", 0.5f); aiAwakening.AddTarget("NEXS", 0.5f); aiAwakening.AddTarget("AEGS", 1.8f); aiAwakening.AddTarget("MIND", 0.6f);
        scenarioDatabase.Add(aiAwakening);

        var space = new ScenarioEvent("화성 탐사 로봇, 초대형 희토류 광맥 발견! '우주 골드러시'!", true);
        space.AddTarget("LUNA", 2.2f); space.AddTarget("VOID", 1.5f); space.AddTarget("ZILS", 1.3f); space.AddTarget("SAIL", 1.2f);
        scenarioDatabase.Add(space);

        var fullDive = new ScenarioEvent("뇌파 연결 '풀다이브 VR' 상용화 성공! 현실을 넘어선다!", true);
        fullDive.AddTarget("MIND", 2.0f); fullDive.AddTarget("FANT", 1.8f); fullDive.AddTarget("Vlive", 1.5f); fullDive.AddTarget("CRCK", 1.4f);
        scenarioDatabase.Add(fullDive);

        // 2. 사회 및 윤리
        var ethics = new ScenarioEvent("충격! 불법 인체 실험 내부 고발! '윤리 논란' 일파만파!", false);
        ethics.AddTarget("NEO", 0.3f); ethics.AddTarget("MIND", 0.4f); ethics.AddTarget("ILIA", 0.7f); ethics.AddTarget("CSMC", 0.9f);
        scenarioDatabase.Add(ethics);

        var hacking = new ScenarioEvent("사상 최악의 랜섬웨어 전 세계 강타! IT 인프라 마비!", false);
        hacking.AddTarget("AEGS", 2.0f); hacking.AddTarget("DATA", 0.5f); hacking.AddTarget("FNET", 0.6f); hacking.AddTarget("Vlive", 0.7f);
        scenarioDatabase.Add(hacking);

        var luxuryBoom = new ScenarioEvent("부의 양극화 심화... '초호화 럭셔리 시장' 나홀로 호황!", true);
        luxuryBoom.AddTarget("AMBR", 1.8f); luxuryBoom.AddTarget("SAIL", 1.6f); luxuryBoom.AddTarget("TIMT", 0.8f); luxuryBoom.AddTarget("PIXEL", 0.7f);
        scenarioDatabase.Add(luxuryBoom);

        var robotTax = new ScenarioEvent("정부, 일자리 보호 위해 '로봇세' 도입 추진!", false);
        robotTax.AddTarget("NEXS", 0.5f); robotTax.AddTarget("PRIO", 0.7f); robotTax.AddTarget("LUNA", 0.8f); robotTax.AddTarget("CSMC", 0.8f);
        scenarioDatabase.Add(robotTax);

        // 3. 환경 및 재난
        var foodCrisis = new ScenarioEvent("이상 기후로 전 세계 작물 수확량 급감! 식량 안보 비상!", false);
        foodCrisis.AddTarget("ORGA", 0.4f); foodCrisis.AddTarget("GLAB", 1.8f); foodCrisis.AddTarget("BLUE", 1.5f); foodCrisis.AddTarget("TIMT", 1.4f);
        scenarioDatabase.Add(foodCrisis);

        var nuclear = new ScenarioEvent("코어 퓨전 실험로 미세 균열 감지! 방사능 유출 공포!", false);
        nuclear.AddTarget("CORE", 0.2f); nuclear.AddTarget("MAGM", 1.4f); nuclear.AddTarget("SOLAR", 1.3f); nuclear.AddTarget("ZILS", 1.2f);
        scenarioDatabase.Add(nuclear);

        var solarFlare = new ScenarioEvent("초강력 태양 폭발 경보! 우주 여행 전면 금지!", false);
        solarFlare.AddTarget("SAIL", 0.3f); solarFlare.AddTarget("VOID", 0.7f); solarFlare.AddTarget("FANT", 1.6f);
        scenarioDatabase.Add(solarFlare);

        var pandemic = new ScenarioEvent("신종 바이러스 확산 조짐! 전 세계가 긴장!", false);
        pandemic.AddTarget("ILIA", 2.2f); pandemic.AddTarget("Vlive", 1.5f); pandemic.AddTarget("ORGN", 0.6f); pandemic.AddTarget("SKGL", 0.5f);
        scenarioDatabase.Add(pandemic);

        // 4. 문화 및 트렌드
        var retro = new ScenarioEvent("디지털 피로감 확산... '아날로그와 클래식'의 귀환!", true);
        retro.AddTarget("ARCD", 2.2f); retro.AddTarget("ORGN", 1.6f); retro.AddTarget("ORGA", 1.4f); retro.AddTarget("Vlive", 0.7f);
        scenarioDatabase.Add(retro);

        var veganTrend = new ScenarioEvent("MZ세대 중심 '가치 소비' 확산! 대체육 시장 급성장!", true);
        veganTrend.AddTarget("GLAB", 2.0f); veganTrend.AddTarget("AMBR", 0.6f); veganTrend.AddTarget("SOLAR", 1.2f);
        scenarioDatabase.Add(veganTrend);

        var metaConcert = new ScenarioEvent("가상 아이돌 콘서트 접속자 5억 명 돌파! 엔터 산업 지각변동!", true);
        metaConcert.AddTarget("Vlive", 2.5f); metaConcert.AddTarget("ARCD", 0.7f); metaConcert.AddTarget("PIXEL", 1.2f);
        scenarioDatabase.Add(metaConcert);

        // 5. 경제 및 정책
        var cryptoCrash = new ScenarioEvent("주요 가상화폐 거래소 뱅크런! 코인 시장 붕괴!", false);
        cryptoCrash.AddTarget("PIXEL", 0.4f); cryptoCrash.AddTarget("FNET", 0.5f); cryptoCrash.AddTarget("CORE", 0.8f); cryptoCrash.AddTarget("TIMT", 1.1f);
        scenarioDatabase.Add(cryptoCrash);

        var spaceFund = new ScenarioEvent("정부, '제2의 지구' 찾기에 100조 원 투자 발표!", true);
        spaceFund.AddTarget("VOID", 1.8f); spaceFund.AddTarget("LUNA", 1.7f); spaceFund.AddTarget("SKGL", 1.4f);
        scenarioDatabase.Add(spaceFund);

        var lowRate = new ScenarioEvent("기준 금리 0%대로 인하! 시장에 유동성 공급 폭탄!", true);
        lowRate.AddTarget("FNET", 1.6f); lowRate.AddTarget("CRCK", 1.5f); lowRate.AddTarget("AEGS", 1.4f); lowRate.AddTarget("TIMT", 0.9f);
        scenarioDatabase.Add(lowRate);

        // 6. 기업 간 알력
        var patentWar = new ScenarioEvent("일리아 바이오 vs 네오 진, 세기의 유전자 특허 소송 개시!", false);
        patentWar.AddTarget("ILIA", 0.8f); patentWar.AddTarget("NEO", 0.7f); patentWar.AddTarget("TIME", 1.2f);
        scenarioDatabase.Add(patentWar);

        var merger = new ScenarioEvent("코즈믹 소프트, 데이터 마이닝 인수 합병설 솔솔! '초거대 공룡' 탄생하나?", true);
        merger.AddTarget("DATA", 1.5f); merger.AddTarget("CSMC", 0.9f); merger.AddTarget("AEGS", 0.8f);
        scenarioDatabase.Add(merger);

        var smartCity = new ScenarioEvent("정부-기업 연합, 사막에 최첨단 '네오 서울' 건설 착수!", true);
        smartCity.AddTarget("MAGM", 1.4f); smartCity.AddTarget("SKGL", 1.5f); smartCity.AddTarget("AEGS", 1.3f); smartCity.AddTarget("GLAB", 1.2f);
        scenarioDatabase.Add(smartCity);

        // 7. 엑스트라 및 신규 확장 (총 60개 채우기용)
        var seaResource = new ScenarioEvent("심해 양식장 바닥에서 미지의 에너지 광물 발견!", true);
        seaResource.AddTarget("BLUE", 2.5f); seaResource.AddTarget("ZILS", 0.8f);
        scenarioDatabase.Add(seaResource);

        var esport = new ScenarioEvent("VR 게임, 올림픽 정식 종목 채택! 전 세계 게이머 열광!", true);
        esport.AddTarget("CRCK", 1.8f); esport.AddTarget("FANT", 1.5f); esport.AddTarget("PIXEL", 1.2f);
        scenarioDatabase.Add(esport);

        var fakeFood = new ScenarioEvent("합성 식량 장기 섭취 시, 원인 불명 질병 발생 보고!", false);
        fakeFood.AddTarget("GLAB", 0.4f); fakeFood.AddTarget("TIMT", 0.7f); fakeFood.AddTarget("ORGA", 1.6f); fakeFood.AddTarget("AMBR", 1.3f);
        scenarioDatabase.Add(fakeFood);

        var tunnelCrash = new ScenarioEvent("대륙간 하이퍼루프 터널 붕괴 사고! 물류 대란 발생!", false);
        tunnelCrash.AddTarget("VOID", 1.5f); tunnelCrash.AddTarget("SKGL", 1.3f); tunnelCrash.AddTarget("ORGN", 0.8f);
        scenarioDatabase.Add(tunnelCrash);

        var botError = new ScenarioEvent("가정용 안드로이드 동시다발적 오작동 사태! 소비자들 공포!", false);
        botError.AddTarget("NEXS", 0.3f); botError.AddTarget("CSMC", 0.6f); botError.AddTarget("AEGS", 1.4f);
        scenarioDatabase.Add(botError);

        var alienSignal = new ScenarioEvent("심우주에서 규칙적인 전파 신호 포착! 외계 문명인가?", true);
        alienSignal.AddTarget("VOID", 1.4f); alienSignal.AddTarget("SAIL", 1.5f); alienSignal.AddTarget("LUNA", 1.3f);
        scenarioDatabase.Add(alienSignal);

        var artificialSun = new ScenarioEvent("K-STAR 인공 태양, 1억 도 유지 시간 신기록 경신!", true);
        artificialSun.AddTarget("CORE", 1.8f); artificialSun.AddTarget("FLUX", 1.3f); artificialSun.AddTarget("SOLAR", 0.8f);
        scenarioDatabase.Add(artificialSun);

        var immortalFail = new ScenarioEvent("크로노스 랩 '영생 프로젝트' 최종 실패 선언! 주가 곤두박질!", false);
        immortalFail.AddTarget("TIME", 0.2f); immortalFail.AddTarget("NEO", 1.3f);
        scenarioDatabase.Add(immortalFail);

        var analogTrend = new ScenarioEvent("22세기에도 식지 않는 '아날로그 감성' 열풍!", true);
        analogTrend.AddTarget("ORGN", 1.2f); analogTrend.AddTarget("ARCD", 1.3f); analogTrend.AddTarget("MIND", 0.9f);
        scenarioDatabase.Add(analogTrend);

        // --- 추가 30종 (Expansion) ---

        var rateHike = new ScenarioEvent("중앙은행, 물가 잡기 위해 기준 금리 기습 인상 단행!", false);
        rateHike.AddTarget("BANK", 1.8f); rateHike.AddTarget("FNET", 0.6f); rateHike.AddTarget("ZILS", 0.8f); rateHike.AddTarget("GAIA", 0.7f);
        scenarioDatabase.Add(rateHike);

        var borderWar = new ScenarioEvent("제7구역 국경 분쟁 격화! 전면전 위기 고조!", false);
        borderWar.AddTarget("SHLD", 2.5f); borderWar.AddTarget("NEXS", 1.6f); borderWar.AddTarget("ELIX", 1.5f); borderWar.AddTarget("TIMT", 1.4f); borderWar.AddTarget("AURA", 1.3f); borderWar.AddTarget("FANT", 0.6f);
        scenarioDatabase.Add(borderWar);

        var terraformSuccess = new ScenarioEvent("가이아 건설, 화성 대기 안정화 성공! '제2의 지구' 눈앞!", true);
        terraformSuccess.AddTarget("GAIA", 2.2f); terraformSuccess.AddTarget("GLAB", 1.5f); terraformSuccess.AddTarget("ZILS", 1.3f); terraformSuccess.AddTarget("IRON", 1.4f);
        scenarioDatabase.Add(terraformSuccess);

        var drugScandal = new ScenarioEvent("국민 아이돌, 엘릭서 팜 진통제 불법 투약 혐의 입건!", false);
        drugScandal.AddTarget("ELIX", 0.4f); drugScandal.AddTarget("AURA", 1.5f); drugScandal.AddTarget("Vlive", 0.8f);
        scenarioDatabase.Add(drugScandal);

        var miningDisaster = new ScenarioEvent("아이언 윌 채굴 로봇 오작동, 소행성 광산 붕괴 참사!", false);
        miningDisaster.AddTarget("IRON", 0.5f); miningDisaster.AddTarget("ZILS", 0.7f); miningDisaster.AddTarget("VOID", 0.8f); miningDisaster.AddTarget("AEGS", 1.2f);
        scenarioDatabase.Add(miningDisaster);

        var spaceFoodFad = new ScenarioEvent("'스타 더스트' 우주 빙수, 전 은하계 MZ세대 입맛 사로잡다!", true);
        spaceFoodFad.AddTarget("DUST", 2.0f); spaceFoodFad.AddTarget("GLAB", 1.3f); spaceFoodFad.AddTarget("PIXEL", 1.2f); spaceFoodFad.AddTarget("AMBR", 0.8f);
        scenarioDatabase.Add(spaceFoodFad);

        var pirateAttack = new ScenarioEvent("악명 높은 '검은 수염' 해적단, 주요 무역 항로 약탈!", false);
        pirateAttack.AddTarget("VOID", 0.6f); pirateAttack.AddTarget("SHLD", 1.8f); pirateAttack.AddTarget("TITN", 1.3f); pirateAttack.AddTarget("HEMS", 0.9f);
        scenarioDatabase.Add(pirateAttack);

        var fakeNews = new ScenarioEvent("오로라 미디어, 홀로그램 뉴스 조작 의혹! '신뢰도 추락'!", false);
        fakeNews.AddTarget("AURA", 0.5f); fakeNews.AddTarget("DATA", 1.4f); fakeNews.AddTarget("CSMC", 0.9f);
        scenarioDatabase.Add(fakeNews);

        var cryptoBill = new ScenarioEvent("은하 연방, 모든 상거래에 '디지털 코인' 결제 의무화 추진!", true);
        cryptoBill.AddTarget("FNET", 2.0f); cryptoBill.AddTarget("PIXEL", 1.8f); cryptoBill.AddTarget("BANK", 0.6f);
        scenarioDatabase.Add(cryptoBill);

        var brainHack = new ScenarioEvent("마인드 링크 사용자들 집단 기억 조작 증세! 해킹 의심!", false);
        brainHack.AddTarget("MIND", 0.3f); brainHack.AddTarget("NEO", 0.5f); brainHack.AddTarget("AEGS", 1.7f);
        scenarioDatabase.Add(brainHack);

        var warshipOrder = new ScenarioEvent("지구 연합군, 타이탄 중공업에 차세대 초대형 전함 발주!", true);
        warshipOrder.AddTarget("TITN", 2.0f); warshipOrder.AddTarget("ZILS", 1.3f); warshipOrder.AddTarget("MAGM", 1.2f);
        scenarioDatabase.Add(warshipOrder);

        var organBlackmarket = new ScenarioEvent("바이오 스피어 인공 장기, 암시장에서 불법 유통 정황 포착!", false);
        organBlackmarket.AddTarget("BIOS", 0.4f); organBlackmarket.AddTarget("NEO", 1.5f); organBlackmarket.AddTarget("TIME", 0.8f);
        scenarioDatabase.Add(organBlackmarket);

        var retroChamps = new ScenarioEvent("아케이드 X 주최 '우주 레트로 게임 챔피언십' 시청률 대박!", true);
        retroChamps.AddTarget("ARCD", 1.8f); retroChamps.AddTarget("DUST", 1.3f); retroChamps.AddTarget("AURA", 1.2f); retroChamps.AddTarget("CRCK", 0.9f);
        scenarioDatabase.Add(retroChamps);

        var commBlackout = new ScenarioEvent("초강력 태양 흑점 폭발! 헤르메스 통신망 일시 마비!", false);
        commBlackout.AddTarget("HEMS", 0.5f); commBlackout.AddTarget("VOID", 0.7f); commBlackout.AddTarget("PRIO", 0.8f); commBlackout.AddTarget("LUNA", 0.6f);
        scenarioDatabase.Add(commBlackout);

        var luxuryTax = new ScenarioEvent("의회, 민간 우주 여행에 50% '부유세' 부과 법안 통과!", false);
        luxuryTax.AddTarget("SAIL", 0.5f); luxuryTax.AddTarget("AMBR", 0.7f); luxuryTax.AddTarget("TITN", 0.8f);
        scenarioDatabase.Add(luxuryTax);

        var robotAccident = new ScenarioEvent("넥서스 봇 오작동으로 건설 현장 붕괴! 안정성 논란!", false);
        robotAccident.AddTarget("NEXS", 0.6f); robotAccident.AddTarget("GAIA", 0.8f); robotAccident.AddTarget("IRON", 1.2f);
        scenarioDatabase.Add(robotAccident);

        var magmaExpansion = new ScenarioEvent("마그마 썸, 금성 표면에 초대형 지열 발전소 완공!", true);
        magmaExpansion.AddTarget("MAGM", 1.9f); magmaExpansion.AddTarget("TITN", 1.2f); magmaExpansion.AddTarget("CORE", 0.9f);
        scenarioDatabase.Add(magmaExpansion);

        var organicTrend = new ScenarioEvent("인플루언서들 사이에서 '진짜 흙, 진짜 음식' 챌린지 유행!", true);
        organicTrend.AddTarget("ORGA", 2.0f); organicTrend.AddTarget("GLAB", 0.7f); organicTrend.AddTarget("AMBR", 1.2f);
        scenarioDatabase.Add(organicTrend);

        var quantumSec = new ScenarioEvent("이지스 시스템, 해킹 불가능한 '양자 방패' 프로토콜 개발!", true);
        quantumSec.AddTarget("AEGS", 1.8f); quantumSec.AddTarget("BANK", 1.3f); quantumSec.AddTarget("HEMS", 1.2f);
        scenarioDatabase.Add(quantumSec);

        var oceanCleanup = new ScenarioEvent("지구 연합, 전 지구적 해양 정화 프로젝트 '블루 어스' 가동!", true);
        oceanCleanup.AddTarget("BLUE", 1.6f); oceanCleanup.AddTarget("LUNA", 1.4f); oceanCleanup.AddTarget("GLAB", 0.9f);
        scenarioDatabase.Add(oceanCleanup);

        var alienArtifact2 = new ScenarioEvent("루나 로버 탐사대, 달 뒷면에서 '검은 비석' 발견!", true);
        alienArtifact2.AddTarget("LUNA", 2.5f); alienArtifact2.AddTarget("VOID", 1.4f); alienArtifact2.AddTarget("SAIL", 1.3f); alienArtifact2.AddTarget("AURA", 1.5f);
        scenarioDatabase.Add(alienArtifact2);

        var superVirus = new ScenarioEvent("기존 항생제가 듣지 않는 슈퍼 박테리아 확산!", false);
        superVirus.AddTarget("ILIA", 0.6f); superVirus.AddTarget("TIMT", 1.5f); superVirus.AddTarget("FANT", 1.4f); superVirus.AddTarget("BIOS", 1.3f);
        scenarioDatabase.Add(superVirus);

        var aiRights = new ScenarioEvent("의회, '자율 AI 인권법' 통과! 로봇 노동 비용 급증 예상!", false);
        aiRights.AddTarget("NEXS", 0.5f); aiRights.AddTarget("CSMC", 0.7f); aiRights.AddTarget("IRON", 1.4f);
        scenarioDatabase.Add(aiRights);

        var resourceCrisis = new ScenarioEvent("질리아스 에너지, 화성 제3광구 자원 고갈 공식 선언!", false);
        resourceCrisis.AddTarget("ZILS", 0.6f); resourceCrisis.AddTarget("IRON", 0.7f); resourceCrisis.AddTarget("LUNA", 1.5f); resourceCrisis.AddTarget("FLUX", 0.8f);
        scenarioDatabase.Add(resourceCrisis);

        var bettingLegal = new ScenarioEvent("은하 연방, E-스포츠 승부 예측 베팅 전면 합법화!", true);
        bettingLegal.AddTarget("CRCK", 1.6f); bettingLegal.AddTarget("AURA", 1.7f); bettingLegal.AddTarget("PIXEL", 1.5f); bettingLegal.AddTarget("BANK", 1.2f);
        scenarioDatabase.Add(bettingLegal);

        var hyperloop = new ScenarioEvent("지구 전역을 잇는 진공 하이퍼루프망 개통! 서울-뉴욕 2시간!", true);
        hyperloop.AddTarget("SKGL", 0.8f); hyperloop.AddTarget("ORGN", 0.7f); hyperloop.AddTarget("GAIA", 1.4f);
        scenarioDatabase.Add(hyperloop);

        var mindUpload = new ScenarioEvent("마인드 링크, 기억을 서버에 저장하는 '마인드 클라우드' 베타 오픈!", true);
        mindUpload.AddTarget("MIND", 2.5f); mindUpload.AddTarget("TIME", 0.5f); mindUpload.AddTarget("DATA", 1.6f); mindUpload.AddTarget("NEO", 1.2f);
        scenarioDatabase.Add(mindUpload);

        var kesslerSyndrome = new ScenarioEvent("위성 충돌로 우주 파편 연쇄 폭발! 저궤도 봉쇄!", false);
        kesslerSyndrome.AddTarget("HEMS", 0.2f); kesslerSyndrome.AddTarget("VOID", 0.3f); kesslerSyndrome.AddTarget("SAIL", 0.3f); kesslerSyndrome.AddTarget("LUNA", 1.8f);
        scenarioDatabase.Add(kesslerSyndrome);

        var syntheticScandal = new ScenarioEvent("그린 랩 합성 고기에서 공업용 단백질 검출 의혹!", false);
        syntheticScandal.AddTarget("GLAB", 0.3f); syntheticScandal.AddTarget("ORGA", 1.8f); syntheticScandal.AddTarget("AMBR", 1.5f); syntheticScandal.AddTarget("TIMT", 1.1f);
        scenarioDatabase.Add(syntheticScandal);

        var ubi = new ScenarioEvent("연방 정부, 전 국민에게 매달 디지털 코인으로 기본 소득 지급!", true);
        ubi.AddTarget("TIMT", 0.8f); ubi.AddTarget("PIXEL", 1.5f); ubi.AddTarget("DUST", 1.4f); ubi.AddTarget("FNET", 1.3f);
        scenarioDatabase.Add(ubi);

        // ==========================================
        // 9. 특수 시나리오 (Special & Crisis - 30 New)
        // ==========================================

        // [대공황] 블랙 스완
        // 전 종목 폭락 (안전자산 제외)
        var blackSwan = new ScenarioEvent("글로벌 금융 시스템 붕괴! '블랙 스완' 현실화!", false);
        // 모든 주식에 악재를 걸기 위해 루프를 돌릴 수도 있지만, 주요 기업들을 직접 타겟팅
        blackSwan.AddTarget("CSMC", 0.5f); blackSwan.AddTarget("ZILS", 0.5f); blackSwan.AddTarget("PRIO", 0.5f);
        blackSwan.AddTarget("ILIA", 0.5f); blackSwan.AddTarget("CRCK", 0.5f); blackSwan.AddTarget("FNET", 0.3f);
        blackSwan.AddTarget("BANK", 0.8f); // 은행은 그나마 덜 떨어짐
        blackSwan.AddTarget("TIMT", 1.2f); // 티메트 푸드 (유일한 생존자 - 비상식량)
        scenarioDatabase.Add(blackSwan);

        // [해킹] 뉴스 네트워크 마비 (정보 차단)
        // 실제로는 아무 일도 없거나 랜덤하게 움직이는데, 뉴스가 안 보여서 공포감 조성
        // * 이 이벤트는 GenerateNextEvent에서 별도로 처리합니다 (News Title: "ERROR: 404")
        
        // [협력] 우주 엘리베이터 착공
        // 건설/철강/운송 호재
        var elevator = new ScenarioEvent("가이아 건설 & 타이탄 중공업, '우주 엘리베이터' 공동 착공!", true);
        elevator.AddTarget("GAIA", 2.0f); elevator.AddTarget("TITN", 1.8f); elevator.AddTarget("ZILS", 1.5f); elevator.AddTarget("VOID", 0.6f); // 보이드 하울 악재 (운송 대체)
        scenarioDatabase.Add(elevator);

        // [갈등] 로봇 격투 대회 승부조작
        // 로봇/도박 악재 vs 미디어 호재
        var robotFix = new ScenarioEvent("넥서스 봇 주최 로봇 격투 대회, 대규모 승부조작 적발!", false);
        robotFix.AddTarget("NEXS", 0.6f); robotFix.AddTarget("PIXEL", 0.7f); // 베팅 업체 타격
        robotFix.AddTarget("AURA",  1.4f); // 오로라 미디어 (특종)
        scenarioDatabase.Add(robotFix);

        // [발견] 불로초? 심해 희귀 생물
        // 바이오/식품 호재
        var deepBio = new ScenarioEvent("블루 오션, 심해에서 노화 억제 성분 함유한 생물 발견!", true);
        deepBio.AddTarget("BLUE", 2.2f); deepBio.AddTarget("TIME", 1.5f); // 크로노스 랩 (성분 독점 계약설)
        deepBio.AddTarget("ILIA", 0.8f); // 일리아 바이오 (경쟁 약물)
        scenarioDatabase.Add(deepBio);

        // [사고] 궤도 엘리베이터 케이블 절단
        // 건설/철강 악재 vs 운송 호재
        var elevatorSnap = new ScenarioEvent("건설 중이던 우주 엘리베이터 케이블 절단 사고! 지상 추락!", false);
        elevatorSnap.AddTarget("GAIA", 0.3f); elevatorSnap.AddTarget("TITN", 0.5f);
        elevatorSnap.AddTarget("VOID", 1.6f); // 보이드 하울 (역시 우주선이 최고다)
        elevatorSnap.AddTarget("SHLD", 1.4f); // 블랙쉴드 (현장 통제 및 구조)
        scenarioDatabase.Add(elevatorSnap);

        // [유행] 사이보그 패션 유행
        // 신체개조 호재
        var cyborgTrend = new ScenarioEvent("MZ세대 사이에서 '기계 팔' 패션 유행! 신체 개조 붐!", true);
        cyborgTrend.AddTarget("NEO", 2.0f); cyborgTrend.AddTarget("BIOS", 0.7f); // 생체 장기는 촌스럽다
        cyborgTrend.AddTarget("MIND", 1.3f); // 패션 제어용 칩
        scenarioDatabase.Add(cyborgTrend);

        // [환경] 인공 강우 성공
        // 농업 호재
        var rainSuccess = new ScenarioEvent("오가닉 팜, 자체 인공 강우 기술로 사막 농지화 성공!", true);
        rainSuccess.AddTarget("ORGA", 2.5f); rainSuccess.AddTarget("GLAB", 0.8f);
        scenarioDatabase.Add(rainSuccess);

        // [금융] 은행 뱅크런 사태
        // 은행 악재 vs 코인/금고 호재
        var bankRun = new ScenarioEvent("네뷸라 뱅크 전산 오류 루머로 뱅크런 조짐!", false);
        bankRun.AddTarget("BANK", 0.4f); bankRun.AddTarget("FNET", 1.5f); // 대안 투자처
        bankRun.AddTarget("AEGS", 1.3f); // 보안 점검
        scenarioDatabase.Add(bankRun);

        // [엔터] VR 중독 치료제 개발
        // 제약 호재 vs 게임 악재
        var vrCure = new ScenarioEvent("일리아 바이오, '디지털 마약' VR 중독 치료제 임상 돌입!", true);
        vrCure.AddTarget("ILIA", 1.6f); vrCure.AddTarget("FANT", 0.7f); vrCure.AddTarget("CRCK", 0.8f);
        scenarioDatabase.Add(vrCure);

        // [전쟁] 용병 반란
        // 방산 악재 vs 로봇 호재
        var mercRevolt = new ScenarioEvent("블랙쉴드 소속 인간 용병단, 처우 불만으로 파업 및 점거!", false);
        mercRevolt.AddTarget("SHLD", 0.5f); 
        mercRevolt.AddTarget("NEXS", 1.8f); // 인간 말고 말 잘 듣는 로봇 쓰자
        scenarioDatabase.Add(mercRevolt);

        // [우주] 혜성 충돌 위기
        // 전 종목 폭락 (쉘터/식량 제외)
        var cometImpact = new ScenarioEvent("직경 10km 혜성 지구 접근 중! 충돌 확률 0.01%!", false);
        cometImpact.AddTarget("CSMC", 0.6f); cometImpact.AddTarget("PRIO", 0.6f);
        cometImpact.AddTarget("GAIA", 1.5f); // 지하 쉘터 건설
        cometImpact.AddTarget("TIMT", 2.0f); // 비상 식량
        scenarioDatabase.Add(cometImpact);

        // [정책] 탄소세 폐지
        // 에너지/자동차 호재 vs 환경 악재
        var carbonFree = new ScenarioEvent("연방 정부, 경기 부양 위해 '탄소세' 전격 폐지!", true);
        carbonFree.AddTarget("ZILS", 1.8f); carbonFree.AddTarget("ORGN", 1.7f); // 내연기관 부활
        carbonFree.AddTarget("SOLAR", 0.6f); // 친환경 메리트 하락
        carbonFree.AddTarget("GLAB", 0.7f);
        scenarioDatabase.Add(carbonFree);

        // [기술] 뇌킹(Brain-Hacking) 범죄 조직 검거
        // 보안 호재 vs BCI 악재
        var brainGang = new ScenarioEvent("타인의 뇌를 해킹해 조종한 범죄 조직 '팬텀' 일망타진!", true);
        brainGang.AddTarget("AEGS", 1.5f); // 검거 공로
        brainGang.AddTarget("MIND", 0.5f); // 해킹 가능성이 사실로 증명됨 (악재)
        scenarioDatabase.Add(brainGang);

        // [미디어] 아이돌 메타버스 팬미팅 서버 다운
        // 미디어/플랫폼 악재
        var serverCrash = new ScenarioEvent("버스 라이브, 아이돌 팬미팅 중 서버 폭발! 환불 소동!", false);
        serverCrash.AddTarget("Vlive", 0.6f); serverCrash.AddTarget("AURA", 0.7f);
        scenarioDatabase.Add(serverCrash);

        // [교통] 플라잉카 음주운전 사고
        // 자동차 악재
        var flyingDrunk = new ScenarioEvent("스카이 글라이드, 도심 한복판 추락 사고! 원인은 음주 비행!", false);
        flyingDrunk.AddTarget("SKGL", 0.5f); flyingDrunk.AddTarget("PRIO", 1.2f); // 자율주행(안전) 반사이익
        scenarioDatabase.Add(flyingDrunk);

        // [식량] 우주 곰팡이 감염
        // 우주 식량 악재 vs 지구 식량 호재
        var spaceMold = new ScenarioEvent("우주 정거장 식량 창고, 미지의 곰팡이로 전량 오염!", false);
        spaceMold.AddTarget("TIMT", 0.6f); spaceMold.AddTarget("DUST", 0.5f);
        spaceMold.AddTarget("ORGA", 1.5f); // 지구산이 안전하다
        scenarioDatabase.Add(spaceMold);

        // [에너지] 블랙홀 에너지 추출 이론 발표
        // 기초과학 호재
        var blackholeEnergy = new ScenarioEvent("코어 퓨전, 블랙홀 에너지 추출 이론 발표! 학계 발칵!", true);
        blackholeEnergy.AddTarget("CORE", 2.5f); blackholeEnergy.AddTarget("ZILS", 0.7f);
        scenarioDatabase.Add(blackholeEnergy);

        // [로봇] 감정 노동 로봇 인기
        // 로봇 호재
        var emotionalBot = new ScenarioEvent("넥서스 봇, 인간의 감정을 위로하는 '케어 로봇' 출시 대박!", true);
        emotionalBot.AddTarget("NEXS", 1.8f); emotionalBot.AddTarget("MIND", 1.3f); // 감정 알고리즘 제휴
        scenarioDatabase.Add(emotionalBot);

        // [건설] 해저 도시 프로젝트
        // 건설/해양 호재
        var underwaterCity = new ScenarioEvent("가이아 건설, 수심 3000m 해저 도시 '아틀란티스' 건설 발표!", true);
        underwaterCity.AddTarget("GAIA", 1.7f); underwaterCity.AddTarget("BLUE", 1.6f); // 양식장 연계
        underwaterCity.AddTarget("MAGM", 1.4f); // 해저 지열 발전
        scenarioDatabase.Add(underwaterCity);

        // [게임] 게임 아이템 상속세 부과
        // 게임 악재
        var gameTax = new ScenarioEvent("국세청, 고가 게임 아이템에 상속세 부과 결정!", false);
        gameTax.AddTarget("PIXEL", 0.6f); gameTax.AddTarget("CRCK", 0.7f); gameTax.AddTarget("FNET", 0.8f);
        scenarioDatabase.Add(gameTax);

        // [의료] 수면 학습기 부작용
        // IT 악재 vs 의료 호재
        var sleepLearn = new ScenarioEvent("마인드 링크 수면 학습기, 불면증 및 환각 부작용 보고!", false);
        sleepLearn.AddTarget("MIND", 0.5f); 
        sleepLearn.AddTarget("ELIX", 1.4f); // 수면제/진정제 수요
        scenarioDatabase.Add(sleepLearn);

        // [방산] 외계 침공 루머
        // 방산 폭등
        var alienRumor = new ScenarioEvent("심우주 관측소, 미확인 대규모 함대 접근 포착 루머!", false);
        alienRumor.AddTarget("SHLD", 2.5f); alienRumor.AddTarget("TITN", 2.0f); alienRumor.AddTarget("NEXS", 1.8f);
        alienRumor.AddTarget("FANT", 0.5f); // 평화 산업 폭락
        scenarioDatabase.Add(alienRumor);

        // [자원] 대체 희토류 합성 성공
        // 화학/제조 호재 vs 광산 악재
        var syntheticRare = new ScenarioEvent("그린 랩, 식물에서 희토류 성분 추출하는 기술 개발!", true);
        syntheticRare.AddTarget("GLAB", 2.2f); // 바이오 기업의 쾌거
        syntheticRare.AddTarget("ZILS", 0.5f); // 광산 가치 하락
        syntheticRare.AddTarget("LUNA", 0.6f); // 탐사 로봇 수요 감소
        scenarioDatabase.Add(syntheticRare);

        // [금융] 코인 해킹
        // 코인 악재 vs 보안 호재
        var coinHack = new ScenarioEvent("퓨처 넷 메인넷 해킹! 10조 원 규모 코인 도난!", false);
        coinHack.AddTarget("FNET", 0.2f); // 사망
        coinHack.AddTarget("PIXEL", 0.4f); 
        coinHack.AddTarget("AEGS", 1.5f); // 보안 컨설팅 폭주
        scenarioDatabase.Add(coinHack);

        // [교통] 우주선 면허 간소화
        // 우주 운송/제조 호재
        var spaceLicense = new ScenarioEvent("누구나 우주로! 민간 우주선 조종 면허 대폭 간소화!", true);
        spaceLicense.AddTarget("SAIL", 1.6f); spaceLicense.AddTarget("TITN", 1.4f); spaceLicense.AddTarget("ORGN", 0.8f); // 지상차 매력 감소
        scenarioDatabase.Add(spaceLicense);

        // [식품] 전설의 요리사 영입
        // 고급식품 호재
        var starChef = new ScenarioEvent("앰브로시아, 은하계 최고의 셰프 영입! 예약 3년치 마감!", true);
        starChef.AddTarget("AMBR", 1.7f);
        scenarioDatabase.Add(starChef);

        // [IT] 6G 통신망 조기 구축
        // 통신/IT 호재
        var sixG = new ScenarioEvent("헤르메스 통신, 6G 양자 통신망 예상보다 1년 일찍 개통!", true);
        sixG.AddTarget("HEMS", 1.8f); sixG.AddTarget("CSMC", 1.3f); sixG.AddTarget("Vlive", 1.4f);
        scenarioDatabase.Add(sixG);

        // [바이오] 좀비 바이러스 영화 개봉
        // 바이오 테마성 호재 (실제론 관련 없음)
        var zombieMovie = new ScenarioEvent("영화 '바이오 해저드' 천만 관객 돌파! 좀비 관련주 들썩!", true);
        zombieMovie.AddTarget("ILIA", 1.2f); zombieMovie.AddTarget("BIOS", 1.2f); zombieMovie.AddTarget("AURA", 1.5f);
        scenarioDatabase.Add(zombieMovie);

        // [기타] 회장의 기부
        // 기업 이미지 개선
        var donation = new ScenarioEvent("코즈믹 소프트 회장, 전 재산의 90% 사회 환원 약속!", true);
        donation.AddTarget("CSMC", 1.2f);
        scenarioDatabase.Add(donation);
    }

    void InitializeMarket()
    {
        foreach (var data in stockDataList) if (data != null) marketStocks.Add(new RuntimeStock(data));
    }

    void InitializeUIEvents()
    {
        if (UIManager.I == null) return;

        for (int i = 0; i < maxUISlots; i++)
        {
            int index = i; 
            UIManager.I.TrySetOnClick(UI_GROUP_BOARD, $"SelectBtn_{index}", () => OnSelectStock(index));
        }

        UIManager.I.TrySetOnClick(UI_GROUP_LOAN, "Btn_Borrow", OnClickBorrow);
        UIManager.I.TrySetOnClick(UI_GROUP_LOAN, "Btn_Repay", OnClickRepay);

        // 👇 여기를 수정하세요! (true -> 0, false -> 1)
        UIManager.I.TrySetOnClick(UI_GROUP_POPUP, "Btn_Buy", () => OnTrade(0)); // 매수
        UIManager.I.TrySetOnClick(UI_GROUP_POPUP, "Btn_Sell", () => OnTrade(1)); // 매도
        UIManager.I.TrySetOnClick(UI_GROUP_POPUP, "Btn_Close", () => ToggleTradePanel(false));
        UIManager.I.TrySetOnClick(UI_GROUP_POPUP, "Btn_Short", () => OnTrade(2)); // 공매도
        UIManager.I.TrySetOnClick(UI_GROUP_POPUP, "Btn_Cover", () => OnTrade(3)); // 갚기
        
        // ➕ 정보 패널 열기/닫기
        UIManager.I.TrySetOnClick(UI_GROUP_POPUP, "Btn_OpenInfo", () => OpenCompanyInfoPopup());
        UIManager.I.TrySetOnClick(UI_GROUP_INFO, "Btn_Close", () => ToggleInfoPanel(false));

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

    void OnClickSector(StockSector sector)
    {
        currentSectorFilter = sector; 
        selectedStock = null; 
        ToggleTradePanel(false); 
        UpdateStockBoardUI();
    }

    void OnSelectStock(int uiIndex)
    {
        var currentDisplayList = DisplayedStocks;
        if (uiIndex >= currentDisplayList.Count) return;

        selectedStock = currentDisplayList[uiIndex];
        
        UIManager.I.TrySetInputValue(UI_GROUP_POPUP, "Input_Amount", "1");
        ToggleTradePanel(true);
        UpdateTradePanelUI();
    }

    void ToggleTradePanel(bool isOpen)
    {
        UIManager.I.TrySetActive(UI_GROUP_POPUP, "Panel_Trade", isOpen);
        if (!isOpen) selectedStock = null;
    }

    void ToggleInfoPanel(bool isOpen)
    {
        UIManager.I.TrySetActive(UI_GROUP_INFO, "Panel_CompanyInfo", isOpen);
    }

    // ➕ 간소화된 상세 정보 팝업
    void OpenCompanyInfoPopup()
    {
        if (selectedStock == null) return;

        StockData data = selectedStock.data;

        // 1. 이름 및 심볼
        UIManager.I.TrySetText(UI_GROUP_INFO, "Txt_Name", data.stockName);
        UIManager.I.TrySetText(UI_GROUP_INFO, "Txt_Symbol", $"{data.symbol} | {data.sector}");
        
        // 2. 시작 단가
        UIManager.I.TrySetText(UI_GROUP_INFO, "Txt_StartPrice", $"{data.startPrice:N0} 원");
        
        // 3. 초기 재고
        UIManager.I.TrySetText(UI_GROUP_INFO, "Txt_TotalShares", $"{data.totalShares:N0} 주");

        // 4. 소개글 (스크롤 뷰 추천)
        UIManager.I.TrySetText(UI_GROUP_INFO, "Txt_Desc", data.description);

        ToggleInfoPanel(true);
    }
    

    // 💰 거래 로직 (공매도 포함)
    // mode: 0=매수(Long), 1=매도(Sell), 2=공매도(Short), 3=숏커버링(Cover)
    void OnTrade(int mode)
    {
        if (selectedStock == null) return;
        if (selectedStock.isDelisting && (mode == 0 || mode == 2)) 
        {
            Debug.LogWarning("정리 매매 중인 종목은 신규 매수/공매도가 불가능합니다.");
            return;
        }

        int amount = UIManager.I.GetInputValueInt("TradePanel", "Input_Amount"); // UI 그룹명 하드코딩 주의
        if (amount <= 0) return;

        long cost = (long)selectedStock.currentPrice * amount;

        switch (mode)
        {
            case 0: // 매수 (Buy)
                if (player.money >= cost && selectedStock.remainShares >= amount)
                {
                    player.money -= cost;
                    selectedStock.remainShares -= amount;
                    player.AddStock(selectedStock.data, amount);
                }
                break;
            case 1: // 매도 (Sell)
                if (player.GetStockCount(selectedStock.data) >= amount)
                {
                    player.money += cost;
                    selectedStock.remainShares += amount;
                    player.RemoveStock(selectedStock.data, amount);
                }
                break;
            case 2: // 공매도 (Short Sell) - 주식을 빌려서 팖 (돈이 들어옴)
                // 신용도나 증거금 로직은 복잡하니 일단 제약 없이 가능하게 구현
                // 실제로는 '빌릴 수 있는 잔여 주식'이 있어야 함
                if (selectedStock.remainShares >= amount)
                {
                    player.money += cost; // 판 돈을 먼저 받음
                    selectedStock.remainShares -= amount;
                    player.AddShort(selectedStock.data, amount);
                }
                break;
            case 3: // 숏커버링 (Short Cover) - 주식을 사서 갚음 (돈이 나감)
                if (player.GetShortCount(selectedStock.data) >= amount && player.money >= cost)
                {
                    player.money -= cost;
                    selectedStock.remainShares += amount;
                    player.RemoveShort(selectedStock.data, amount);
                }
                break;
        }

        UpdateTradePanelUI();
        UpdateStockBoardUI();
        UpdatePlayerMoneyUI();
        UpdatePortfolioUI();
    }

    IEnumerator UpdateMarketPrices()
    {
        while (true)
        {
            yield return new WaitForSeconds(updateInterval);

            // ==========================================
            // 1. 배당금 지급 (매 턴)
            // ==========================================
            DistributeDividends();

            // ==========================================
            // 2. 세금 및 공매도 이자 징수 (3턴마다)
            // ==========================================
            currentTaxTurn++;
            if (currentTaxTurn >= taxIntervalTurns)
            {
                ApplyTaxes();
                currentTaxTurn = 0;
            }

            // ==========================================
            // 3. 상장 처리 (IPO)
            // ==========================================
            if (currentEvent.HasValue && currentEvent.Value.isListing)
            {
                marketStocks.Add(currentEvent.Value.singleTarget);
            }

            // ==========================================
            // 4. 시세 변동 및 정리 매매 로직
            // ==========================================
            for (int i = marketStocks.Count - 1; i >= 0; i--)
            {
                RuntimeStock stock = marketStocks[i];
                stock.previousPrice = stock.currentPrice;

                // A. 정리 매매 중인 종목 처리 (지난 턴 상폐 확정됨 -> 삭제)
                if (stock.isDelisting)
                {
                    Debug.Log($"💀 {stock.data.stockName} 최종 상장 폐지 처리 완료.");
                    
                    if (selectedStock == stock) ToggleTradePanel(false);
                    if (!upcomingStocks.Contains(stock.data)) upcomingStocks.Add(stock.data); // 재상장 대기열로
                    marketStocks.RemoveAt(i);
                    continue; 
                }

                // 파산 시나리오 처리
                if (currentEvent.HasValue && currentEvent.Value.isBankruptcy && currentEvent.Value.singleTarget == stock)
                {
                    stock.currentPrice = 0; // 즉시 0원 (다음 로직에서 상폐 트리거됨)
                }
                else
                {
                    // B. 가격 변동 계산
                    float changePercent = UnityEngine.Random.Range(-stock.data.volatility, stock.data.volatility);
                    
                    // 이벤트 효과 적용
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
                        else if (evt.isSectorEvent && stock.data.sector == evt.targetSector)
                        {
                            ApplyEventImpact(ref changePercent, evt.isGoodNews, evt.singleMultiplier * 0.7f, sensitivity);
                        }
                        else if (evt.singleTarget == stock)
                        {
                            ApplyEventImpact(ref changePercent, evt.isGoodNews, evt.singleMultiplier, sensitivity);
                        }
                        else if (evt.isRippleEvent && evt.singleTarget != null && stock.data.sector == evt.singleTarget.data.sector)
                        {
                            ApplyEventImpact(ref changePercent, evt.isGoodNews, evt.singleMultiplier * 0.4f, sensitivity);
                        }
                    }

                    int changeAmount = (int)(stock.currentPrice * changePercent);
                    stock.currentPrice += changeAmount;
                }

                // C. 상장 폐지 조건 체크 (1% 이하) -> 정리 매매 모드 진입
                int delistThreshold = (int)(stock.data.startPrice * 0.01f);
                if (stock.currentPrice <= delistThreshold && !stock.isDelisting)
                {
                    stock.currentPrice = Mathf.Max(1, stock.currentPrice); // 최소 1원 유지
                    stock.isDelisting = true; // ⚠️ 정리 매매 시작
                    Debug.Log($"⚠ {stock.data.stockName} 정리 매매 개시! (다음 턴 상장 폐지)");
                }
            }

            // ⚠️ [신규] 플레이어 파산 체크
            CheckPlayerBankruptcy();

            // ==========================================
            // 5. ⚠️ 공매도 마진콜 체크 (시세 변동 후 즉시 확인)
            // ==========================================
            CheckMarginCall(); // 👈 여기에 넣으시면 됩니다!

            // ==========================================
            // 6. 다음 턴 준비 및 UI 갱신
            // ==========================================
            currentEvent = null; 
            GenerateNextEvent(); 

            UpdateStockBoardUI();
            if(selectedStock != null) UpdateTradePanelUI();
            UpdatePortfolioUI();
            UpdatePlayerMoneyUI();
        }
    }

    // 💀 플레이어 파산 처리 함수
    void CheckPlayerBankruptcy()
    {
        // 총 자산 = 현금 + 주식 평가금 - 공매도 부채
        // (단, 공매도 부채는 마진콜에서 이미 현금으로 차감되므로, 여기선 현금만 봐도 됨)
        // 하지만 더 정확히 '회생 불가능' 상태를 체크하려면 총 자산을 봐야 함.
        
        long totalAsset = player.money;
        var holdings = player.GetHoldings();
        foreach (var item in holdings)
        {
            RuntimeStock stock = marketStocks.Find(s => s.data == item.Key);
            if (stock != null) totalAsset += (long)stock.currentPrice * item.Value;
        }

        // 현금이 마이너스고, 주식을 다 팔아도 빚을 못 갚는 상태면 파산
        // 혹은 단순히 현금 + 주식가치 합산이 0 이하일 때
        if (totalAsset <= 0)
        {
            Debug.Log("💀 [GAME OVER] 플레이어 파산!");
            
            // 게임 오버 UI 띄우기
            if (UIManager.I != null)
            {
                UIManager.I.TrySetActive(UI_GROUP_GAMEOVER, "Panel_GameOver", true);
            }

            // 게임 루프 정지 (코루틴 중단)
            StopAllCoroutines(); 
        }
    }

    void CheckMarginCall()
    {
        var shorts = player.GetShortPositions();
        long totalDebt = 0;

        // 갚아야 할 주식들의 총 가치 계산
        foreach (var item in shorts)
        {
            RuntimeStock stock = marketStocks.Find(s => s.data == item.Key);
            if (stock != null) totalDebt += (long)stock.currentPrice * item.Value;
        }

        // 만약 (내 현금 + 보유주식 가치) < (갚아야 할 돈 * 1.1) 이라면 위험!
        // 여기서는 간단하게 "현금이 빚보다 적어지면" 강제로 갚게 함
        if (totalDebt > 0 && player.money < totalDebt)
        {
            Debug.LogWarning("🚨 [마진콜] 증거금 부족! 공매도 포지션이 강제 청산됩니다.");
            
            // 모든 공매도 강제 상환 (현금이 마이너스가 되더라도 강제 집행)
            foreach (var item in new Dictionary<StockData, int>(shorts)) // 복사본으로 순회
            {
                RuntimeStock stock = marketStocks.Find(s => s.data == item.Key);
                if (stock != null)
                {
                    long debtCost = (long)stock.currentPrice * item.Value;
                    player.money -= debtCost; // 돈이 모자라면 마이너스 통장 됨 (게임오버 트리거)
                    stock.remainShares += item.Value;
                    player.RemoveShort(item.Key, item.Value);
                }
            }
            
            // UI 갱신
            UpdateTradePanelUI();
            UpdatePlayerMoneyUI();
        }
    }

    // 💸 배당금 지급 함수
    void DistributeDividends()
    {
        // 1. 플레이어
        var holdings = player.GetHoldings();
        long totalDiv = 0;
        foreach(var item in holdings)
        {
            if (item.Key.dividendPerShare > 0)
                totalDiv += (long)item.Key.dividendPerShare * item.Value;
        }
        if (totalDiv > 0) 
        {
            player.money += totalDiv;
            // Debug.Log($"💰 배당금 입금: {totalDiv:N0}원");
        }

        // 2. AI
        if (aiManager != null) aiManager.DistributeAIDividends();
    }

    void ApplyEventImpact(ref float changePercent, bool isGood, float power, float sensitivity)
    {
        if (isGood) changePercent += UnityEngine.Random.Range(0.05f, 0.15f) * power * sensitivity;
        else changePercent -= UnityEngine.Random.Range(0.05f, 0.15f) * (1.0f / power) * sensitivity;
    }

    RuntimeStock GetWeightedRandomStock()
    {
        if (marketStocks.Count == 0) return null;
        float totalWeight = marketStocks.Sum(s => s.data.eventWeight);
        float r = UnityEngine.Random.Range(0f, totalWeight);
        foreach (var s in marketStocks) { r -= s.data.eventWeight; if (r <= 0) return s; }
        return marketStocks.Last();
    }

    void GenerateNextEvent()
    {
        if (marketStocks.Count == 0 && upcomingStocks.Count == 0) return;

        // 1. 파산
        if (marketStocks.Count > 0 && UnityEngine.Random.value < bankruptcyChance)
        {
            RuntimeStock target = GetWeightedRandomStock();
            string title = $"[긴급] {target.data.stockName}, {bankruptcyNews[UnityEngine.Random.Range(0, bankruptcyNews.Length)]}";
            currentEvent = new PendingEvent { singleTarget = target, newsTitle = title, isBankruptcy = true };
            UpdateNewsUI(title, new Color(1f, 0f, 1f));
            return;
        }

        // 2. 상장
        if (upcomingStocks.Count > 0 && UnityEngine.Random.value < listingChance)
        {
            int idx = UnityEngine.Random.Range(0, upcomingStocks.Count);
            StockData newData = upcomingStocks[idx];
            upcomingStocks.RemoveAt(idx);
            RuntimeStock newStock = new RuntimeStock(newData);
            string title = $"[IPO 예고] {newData.stockName}, {listingNews[UnityEngine.Random.Range(0, listingNews.Length)]}";
            currentEvent = new PendingEvent { singleTarget = newStock, newsTitle = title, isListing = true };
            UpdateNewsUI(title, Color.green);
            return;
        }

        // 🌟 [신규] 뉴스 해킹 (정보 차단) 체크
        if (UnityEngine.Random.value < newsHackingChance)
        {
            // 실제로는 백그라운드에서 일반 이벤트가 발생하지만, 플레이어에게는 안 보여줌
            GenerateHiddenEvent(); 
            
            // 뉴스 UI는 해킹된 상태로 표시
            string[] hackedMsgs = { 
                "ERROR: 404 Not Found", 
                "※ 보안 통신망 접속 불가 ※", 
                "⚠ 시스템 해킹 감지! 정보 차단됨 ⚠", 
                "Unknown Signal Received...", 
                "01001000 01000101 01001100 01010000" 
            };
            string hackedText = hackedMsgs[UnityEngine.Random.Range(0, hackedMsgs.Length)];
            UpdateNewsUI(hackedText, Color.green); // 매트릭스 느낌의 초록색
            return;
        }

        // 3. 시나리오 (복합)
        if (marketStocks.Count > 0 && UnityEngine.Random.value < scenarioChance)
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
                currentEvent = new PendingEvent
                {
                    scenarioTargets = activeTargets,
                    newsTitle = $"[속보] {scenario.title}",
                    isGoodNews = scenario.isGoodNews
                };
                Color color = scenario.isGoodNews ? new Color(1f, 0.5f, 0f) : new Color(0.5f, 0f, 0.5f);
                UpdateNewsUI(scenario.title, color);
                return;
            }
        }

        // 4. 섹터 이벤트
        if (marketStocks.Count > 0 && UnityEngine.Random.value < 0.1f) 
        {
            Array sectors = Enum.GetValues(typeof(StockSector));
            StockSector targetSector = (StockSector)sectors.GetValue(UnityEngine.Random.Range(0, sectors.Length));
            bool isBoom = UnityEngine.Random.value > 0.5f;
            
            string baseMsg = isBoom ? "호황" : "불황";
            string[] templates = isBoom ? sectorGoodNews[targetSector] : sectorBadNews[targetSector];
            string msg = templates[UnityEngine.Random.Range(0, templates.Length)];

            currentEvent = new PendingEvent 
            { 
                isSectorEvent = true, targetSector = targetSector, 
                singleMultiplier = isBoom ? 1.5f : 0.6f, newsTitle = $"[시장 동향] {targetSector}: {msg}", isGoodNews = isBoom 
            };
            UpdateNewsUI(currentEvent.Value.newsTitle, isBoom ? new Color(1f, 0.5f, 0f) : new Color(0.3f, 0.3f, 1f));
            return;
        }

        // 5. 일반/파급효과 뉴스
        if (marketStocks.Count > 0 && UnityEngine.Random.value < eventChance)
        {
            RuntimeStock target = GetWeightedRandomStock();
            bool isGood = UnityEngine.Random.value > 0.5f;
            float multiplier = isGood ? UnityEngine.Random.Range(1.5f, 2.0f) : UnityEngine.Random.Range(0.5f, 0.7f);
            
            string template = isGood 
                ? commonGoodNews[UnityEngine.Random.Range(0, commonGoodNews.Length)] 
                : commonBadNews[UnityEngine.Random.Range(0, commonBadNews.Length)];
            
            bool isRipple = UnityEngine.Random.value < rippleEffectChance;
            string newsText = $"[정보] {target.data.stockName}, {template}";
            if (isRipple) newsText += " <color=yellow>(파급 효과 예상)</color>";

            currentEvent = new PendingEvent 
            { 
                singleTarget = target, 
                singleMultiplier = multiplier,
                newsTitle = newsText, 
                isGoodNews = isGood,
                isRippleEvent = isRipple
            };
            UpdateNewsUI(newsText, isGood ? Color.red : Color.blue);
            return;
        }

        UpdateNewsUI("시장은 평온합니다.", Color.white);
    }

    // 🕵️ 숨겨진 이벤트 생성 (해킹 시 백그라운드 작동)
    void GenerateHiddenEvent()
    {
        // 일반 이벤트 로직을 그대로 수행하되, UI 업데이트만 안 함 (내부적으로 currentEvent 설정)
        // 코드를 재사용하기 위해 기존 로직을 함수로 분리하거나 여기서 복사해서 씁니다.
        // 편의상 가장 흔한 '일반 뉴스' 로직을 하나 돌립니다.
        
        if (marketStocks.Count > 0)
        {
            RuntimeStock target = GetWeightedRandomStock();
            bool isGood = UnityEngine.Random.value > 0.5f;
            float multiplier = isGood ? UnityEngine.Random.Range(1.5f, 2.0f) : UnityEngine.Random.Range(0.5f, 0.7f);
            
            // 뉴스는 생성하지만 UI에는 표시 안 함 (currentEvent만 세팅)
            currentEvent = new PendingEvent 
            { 
                singleTarget = target, 
                singleMultiplier = multiplier,
                newsTitle = "???", // 로그에서도 안 보이게
                isGoodNews = isGood 
            };
        }
    }

    void UpdateNewsUI(string text, Color color) { if (UIManager.I == null) return; UIManager.I.TrySetText(UI_GROUP_NEWS, "Txt_NewsTicker", text); UIManager.I.TrySetTextColor(UI_GROUP_NEWS, "Txt_NewsTicker", color); }
    
    void UpdateStockBoardUI()
    {
        if (UIManager.I == null) return;
        var displayList = DisplayedStocks;

        for (int i = 0; i < maxUISlots; i++)
        {
            string nameKey = $"Name_{i}"; string priceKey = $"Price_{i}"; string changeKey = $"Change_{i}";

            if (i < displayList.Count)
            {
                RuntimeStock stock = displayList[i];
                int change = stock.GetChangeAmount();
                float percent = stock.GetChangePercent();

                string nameText = $"{stock.data.stockName}\n<size=70%><color=#AAAAAA>{stock.data.symbol} | 잔여: {stock.remainShares:N0}</color></size>";
                UIManager.I.TrySetText(UI_GROUP_BOARD, nameKey, nameText);
                UIManager.I.TrySetText(UI_GROUP_BOARD, priceKey, $"{stock.currentPrice:N0}원");

                string sign = change > 0 ? "▲" : (change < 0 ? "▼" : "-");
                Color displayColor = change > 0 ? new Color(1f, 0.3f, 0.3f) : (change < 0 ? new Color(0.3f, 0.5f, 1f) : Color.white);
                string changeText = $"{sign} {Mathf.Abs(change):N0} ({percent:F2}%)";

                UIManager.I.TrySetText(UI_GROUP_BOARD, changeKey, changeText);
                UIManager.I.TrySetTextColor(UI_GROUP_BOARD, changeKey, displayColor);
                UIManager.I.TrySetTextColor(UI_GROUP_BOARD, priceKey, displayColor);
            }
            else
            {
                UIManager.I.TrySetText(UI_GROUP_BOARD, nameKey, "");
                UIManager.I.TrySetText(UI_GROUP_BOARD, priceKey, "");
                UIManager.I.TrySetText(UI_GROUP_BOARD, changeKey, "");
            }
        }
    }

    void UpdatePortfolioUI()
    {
        if (UIManager.I == null) return;
        long totalStockValue = 0;
        var myHoldings = player.GetHoldings();
        var holdingList = myHoldings.Keys.ToList();

        for (int i = 0; i < maxUISlots; i++)
        {
            string nameKey = $"MyName_{i}"; string countKey = $"MyCount_{i}"; string valKey = $"MyValue_{i}";

            if (i < holdingList.Count)
            {
                StockData data = holdingList[i];
                int amount = myHoldings[data];
                RuntimeStock currentStock = marketStocks.Find(s => s.data == data);
                
                int price = (currentStock != null) ? currentStock.currentPrice : 0;
                long valuation = (long)price * amount;
                totalStockValue += valuation;

                UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, nameKey, data.stockName);
                UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, countKey, $"{amount:N0}주");
                UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, valKey, $"{valuation:N0}원");
            }
            else
            {
                UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, nameKey, "");
                UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, countKey, "");
                UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, valKey, "");
            }
        }

        long totalAsset = player.money + totalStockValue;
        UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, "Txt_Cash", $"{player.money:N0} 원");
        UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, "Txt_StockVal", $"{totalStockValue:N0} 원");
        UIManager.I.TrySetText(UI_GROUP_PORTFOLIO, "Txt_TotalAsset", $"{totalAsset:N0} 원");
    }

    // 🪟 [수정됨] 통합 거래 패널 UI 갱신 (모든 정보 포함)
    void UpdateTradePanelUI()
    {
        if (selectedStock == null) return;

        StockData data = selectedStock.data;

        // 1. 기본 정보 (제목, 심볼, 설명)
        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_Title", $"{data.stockName} <size=70%>({data.symbol} | {data.sector})</size>");
        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_Desc", data.description);

        // 2. ➕ [추가] 상세 스탯 (시작가, 총 발행량)
        // UIManager에 "Txt_StartPrice", "Txt_TotalShares"가 등록되어 있어야 합니다.
        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_StartPrice", $"초기 단가: {data.startPrice:N0} 원");
        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_TotalShares", $"초기 재고: {data.totalShares:N0} 주");

        // 3. 변동성 및 위험도 (DetailStats에 통합하거나 별도 표시 가능)
        string riskLabel = data.volatility >= 0.1f ? "<color=red>(고위험)</color>" : "<color=green>(안정)</color>";
        string volText = $"변동성: {data.volatility * 100:F0}% {riskLabel}";
        
        // 만약 Txt_DetailStats를 쓰고 있다면 여기에 합쳐서 보여줄 수도 있습니다.
        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_DetailStats", volText);

        // 4. 거래 정보 (현재가, 잔여량, 내 보유량, 평가금)
        int myCount = player.GetStockCount(data);
        long valuation = (long)myCount * selectedStock.currentPrice;
        
        string tradeInfo = $"현재가: <color=yellow>{selectedStock.currentPrice:N0}원</color>\n" +
                           $"시장 잔여: {selectedStock.remainShares:N0}주\n" +
                           $"내 보유: <color=green>{myCount:N0}주</color>\n" +
                           $"평가금액: {valuation:N0}원";

        UIManager.I.TrySetText(UI_GROUP_POPUP, "Txt_Info", tradeInfo);
    }

    void UpdatePlayerMoneyUI()
    {
        UIManager.I.TrySetText(UI_GROUP_PLAYER, "Txt_Money", $"{player.money:N0}원");
    }
    
    // ----------------------------------------
    // 📢 [신규] 외부(AI)에서 현재 이벤트 정보를 열람하기 위한 공개 구조체
    public struct PublicEventInfo
    {
        public bool hasEvent;
        public Dictionary<RuntimeStock, float> targets; // 영향받는 주식과 배율
        public bool isGoodNews;
    }

    // 📢 [신규] AI가 호출할 함수: 현재 진행 중인 이벤트 정보를 반환
    public PublicEventInfo GetCurrentEventInfo()
    {
        PublicEventInfo info = new PublicEventInfo();
        
        if (currentEvent.HasValue)
        {
            info.hasEvent = true;
            info.isGoodNews = currentEvent.Value.isGoodNews;
            info.targets = new Dictionary<RuntimeStock, float>();

            // 1. 다중 타겟 (시나리오)
            if (currentEvent.Value.scenarioTargets != null)
            {
                info.targets = currentEvent.Value.scenarioTargets;
            }
            // 2. 섹터 이벤트
            else if (currentEvent.Value.isSectorEvent)
            {
                foreach(var stock in marketStocks)
                {
                    if(stock.data.sector == currentEvent.Value.targetSector)
                        info.targets.Add(stock, currentEvent.Value.singleMultiplier * 0.7f);
                }
            }
            // 3. 단일 타겟
            else if (currentEvent.Value.singleTarget != null)
            {
                info.targets.Add(currentEvent.Value.singleTarget, currentEvent.Value.singleMultiplier);
            }
            // 4. 파급 효과 (Ripple)
            else if (currentEvent.Value.isRippleEvent)
            {
                 foreach(var stock in marketStocks)
                {
                    if(stock.data.sector == currentEvent.Value.singleTarget.data.sector)
                        info.targets.Add(stock, currentEvent.Value.singleMultiplier * 0.4f);
                }
            }
        }
        else
        {
            info.hasEvent = false;
        }

        return info;
    }

    // 💸 세금 및 공매도 이자 징수 함수
    void ApplyTaxes()
    {
        // 1. 보유세 (기존 로직)
        if (player.money > 0)
        {
            long tax = (long)(player.money * 0.01f);
            player.money -= tax;
            // Debug.Log($"⚖️ [세금] 보유세 납부: {tax:N0}원");
        }

        // 2. ➕ [신규] 공매도 이자 (Short Interest)
        // 빌린 주식의 현재 가치 * 이자율(예: 2%)만큼 돈이 빠져나감
        var shorts = player.GetShortPositions();
        long totalShortValue = 0;
        
        foreach(var item in shorts)
        {
            RuntimeStock stock = marketStocks.Find(s => s.data == item.Key);
            if (stock != null)
                totalShortValue += (long)stock.currentPrice * item.Value;
        }

        if (totalShortValue > 0)
        {
            long interest = (long)(totalShortValue * 0.02f); // 2% 이자
            player.money -= interest;
            Debug.Log($"📉 [공매도] 대차 이자 납부: {interest:N0}원 (잔고에서 차감)");
        }

        // AI에게도 동일하게 적용
        if (aiManager != null) aiManager.ApplyTaxToAI();
    }
    
    // 🏦 대출 패널 열기/닫기 & UI 갱신
    void ToggleLoanPanel(bool isOpen)
    {
        UIManager.I.TrySetActive(UI_GROUP_LOAN, "Panel_Loan", isOpen);
        if (isOpen)
        {
            UpdateLoanPanelUI();
            // 입력창 초기화
            UIManager.I.TrySetInputValue(UI_GROUP_LOAN, "Input_LoanAmount", "");
        }
    }

    // 🏦 대출 패널 정보 갱신
    void UpdateLoanPanelUI()
    {
        long maxLoan = player.GetMaxLoanAmount();
        long currentDebt = player.currentDebt;
        float interestRate = player.loanInterestRate * 100f; // 퍼센트 변환

        string info = $"현재 부채: <color=red>{currentDebt:N0} 원</color>\n" +
                      $"대출 한도: <color=green>{maxLoan:N0} 원</color>\n" +
                      $"이자율: {interestRate:F1}% / 턴";

        UIManager.I.TrySetText(UI_GROUP_LOAN, "Txt_LoanInfo", info);
    }

    // 💰 대출 버튼 클릭 시
    void OnClickBorrow()
    {
        long amount = GetLoanInputAmount();
        if (amount <= 0) return;

        if (player.BorrowMoney(amount))
        {
            Debug.Log("대출 성공!");
            UpdateLoanPanelUI();
            UpdatePlayerMoneyUI();
            UpdatePortfolioUI(); // 자산 변동 반영
        }
        else
        {
            Debug.LogWarning("대출 실패: 한도 초과");
        }
    }

    // 💸 상환 버튼 클릭 시
    void OnClickRepay()
    {
        long amount = GetLoanInputAmount();
        if (amount <= 0) return;

        if (player.RepayMoney(amount))
        {
            Debug.Log("상환 성공!");
            UpdateLoanPanelUI();
            UpdatePlayerMoneyUI();
            UpdatePortfolioUI();
        }
        else
        {
            Debug.LogWarning("상환 실패: 잔액 부족 또는 부채 없음");
        }
    }

    // 입력창에서 금액 가져오기 (long 타입 파싱)
    long GetLoanInputAmount()
    {
        string inputStr = UIManager.I.GetInputValue(UI_GROUP_LOAN, "Input_LoanAmount");
        if (long.TryParse(inputStr, out long result))
        {
            return result;
        }
        return 0;
    }
}