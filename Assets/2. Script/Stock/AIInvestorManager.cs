using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using CustomInspector; // StockData.cs에 CustomInspector가 사용되므로 추가 (필요할 경우)

#region Data Structures

public enum InvestmentStyle
{
    Aggressive,    // 공격형
    Defensive,     // 방어형
    Balanced,      // 균형형
    TrendFollower, // 추세추종형
    Contrarian,    // 역발상
    ShortSeller,   // 공매도형
    Copycat,       // 따라쟁이
    Rival,          // 👑 [신규] 라이벌 (압도적 성능)
    MarketManipulator,  // 😈 [신규] 작전 세력 (시세 조종)
    HFT,                // ⚡ [신규] 초단타 매매 (기계적)
    SectorSpecialist,    // 🔬 [신규] 섹터 전문가 (외골수)
    DividendHunter    // 💰 [신규] 배당 사냥꾼 (배당금 킬러)
}

[System.Serializable]
public struct AIPreference
{
    public StockSector? favoriteSector;
    public bool preferPennyStock;
    public bool preferBlueChip;
    public float riskTolerance;
}

[System.Serializable]
public class AIInvestor
{
    [Header("Profile")]
    public string name;
    public InvestmentStyle style;
    public AIPreference preference;
    public long money;

    [Header("Attributes")]
    public float reactionDelay;
    [HideInInspector] public float nextActTime;
    [HideInInspector] public string lastReactedEventTitle = "";

    [Header("Status")]
    public Dictionary<StockData, int> portfolio = new Dictionary<StockData, int>();
    public Dictionary<StockData, int> avgCost = new Dictionary<StockData, int>();
    public Dictionary<StockData, int> shortPositions = new Dictionary<StockData, int>();
    public long currentDebt = 0;
    // ➕ [신규] 마지막 행동 기록용 변수
    [HideInInspector] public string lastTradeLog = "아직 관망 중";

    public AIInvestor(string _name, InvestmentStyle _style, long _money, float _delay, AIPreference _pref)
    {
        name = _name; style = _style; money = _money; reactionDelay = _delay; preference = _pref; nextActTime = 0;
    }
}

#endregion

public class AIInvestorManager : MonoBehaviour
{
    #region Settings & References

    [Header("Simulation Settings")]
    public float tradeInterval = 1.0f;
    [Range(0f, 1f)] public float newsReactionProbability = 0.8f;

    [Header("Bankruptcy Settings")]
    [Tooltip("이 금액 이하로 자산이 떨어지면 파산 처리합니다.")]
    public long bankruptcyThreshold = 10000;

    [Header("Loan Settings")]
    public float loanInterestRate = 0.005f;

    [Header("AI Roster")]
    public List<AIInvestor> aiInvestors = new List<AIInvestor>();

    private StockMarketManager market;
    private RankingManager rankingManager;

    #endregion

    #region Unity Lifecycle & Init

    void Start()
    {
        market = FindAnyObjectByType<StockMarketManager>();
        rankingManager = FindAnyObjectByType<RankingManager>();

        if (aiInvestors.Count == 0)
        {
            InitializeExpandedAI();
        }

        StartCoroutine(AITradingLoop());
    }

    void InitializeExpandedAI()
    {
        // 👑 [0순위] 플레이어의 라이벌 (초기 자금 10만, 반응속도 0.1초)
        aiInvestors.Add(new AIInvestor("Phantom (Rival)", InvestmentStyle.Rival, 1000000, 0.5f, new AIPreference { riskTolerance = 1.0f }));

        // 1. [거대 세력]
        aiInvestors.Add(new AIInvestor("갤럭시 자산운용", InvestmentStyle.Balanced, 10000000000, 3f, new AIPreference { preferBlueChip = true, riskTolerance = 0.3f }));
        aiInvestors.Add(new AIInvestor("세력 형님", InvestmentStyle.TrendFollower, 5000000000, 2f, new AIPreference { riskTolerance = 0.6f }));
        aiInvestors.Add(new AIInvestor("공매도 폭격기", InvestmentStyle.ShortSeller, 3000000000, 1.5f, new AIPreference { riskTolerance = 0.7f }));
        aiInvestors.Add(new AIInvestor("주식의 왕", InvestmentStyle.Defensive, 4000000000, 4f, new AIPreference { preferBlueChip = true, riskTolerance = 0.1f }));
        aiInvestors.Add(new AIInvestor("테크 억만장자", InvestmentStyle.Aggressive, 2500000000, 2.5f, new AIPreference { favoriteSector = StockSector.IT, riskTolerance = 0.8f }));
        aiInvestors.Add(new AIInvestor("검은 손", InvestmentStyle.MarketManipulator, 8000000000, 1.5f, new AIPreference { riskTolerance = 0.9f }));
        aiInvestors.Add(new AIInvestor("다크나이트", InvestmentStyle.MarketManipulator, 1000000000, 2.0f, new AIPreference { riskTolerance = 0.8f }));

        // 2. [전문가 & 중형]
        aiInvestors.Add(new AIInvestor("영포티", InvestmentStyle.TrendFollower, 500000000, 6.25f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("스피드러너", InvestmentStyle.ShortSeller, 300000000, 6f, new AIPreference())); // 🐞 117번 줄 에러 수정: Investor -> AIInvestor
        aiInvestors.Add(new AIInvestor("여의도 저승사자", InvestmentStyle.Contrarian, 400000000, 7.75f, new AIPreference { riskTolerance = 0.9f }));
        aiInvestors.Add(new AIInvestor("강남 건물주", InvestmentStyle.Defensive, 500000000, 9f, new AIPreference { preferBlueChip = true }));
        aiInvestors.Add(new AIInvestor("가치투자자", InvestmentStyle.Defensive, 100000000, 8f, new AIPreference { preferPennyStock = false }));
        aiInvestors.Add(new AIInvestor("전업 10년차", InvestmentStyle.Aggressive, 80000000, 7.5f, new AIPreference { riskTolerance = 0.6f }));
        aiInvestors.Add(new AIInvestor("단타 스캘퍼", InvestmentStyle.Aggressive, 50000000, 6.75f, new AIPreference { riskTolerance = 0.8f }));
        aiInvestors.Add(new AIInvestor("차트의 신", InvestmentStyle.TrendFollower, 150000000, 7f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("바이오 학회장", InvestmentStyle.Aggressive, 200000000, 7.75f, new AIPreference { favoriteSector = StockSector.Bio }));
        aiInvestors.Add(new AIInvestor("우주 개척자", InvestmentStyle.TrendFollower, 120000000, 7.5f, new AIPreference { favoriteSector = StockSector.Automotive }));
        aiInvestors.Add(new AIInvestor("게임 폐인", InvestmentStyle.Aggressive, 60000000, 7.5f, new AIPreference { favoriteSector = StockSector.Game }));
        aiInvestors.Add(new AIInvestor("손절의 달인", InvestmentStyle.Defensive, 70000000, 7f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("지옥의 줍줍러", InvestmentStyle.Contrarian, 250000000, 9f, new AIPreference { riskTolerance = 1.0f }));
        aiInvestors.Add(new AIInvestor("주식 동호회장", InvestmentStyle.Balanced, 90000000, 8f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("캐시카우", InvestmentStyle.DividendHunter, 800000000, 6.5f, new AIPreference()));
        
        aiInvestors.Add(new AIInvestor("바이오 광신도", InvestmentStyle.SectorSpecialist, 400000000, 7.5f, new AIPreference { favoriteSector = StockSector.Bio }));
        aiInvestors.Add(new AIInvestor("일론 머스크 팬", InvestmentStyle.SectorSpecialist, 700000000, 6.75f, new AIPreference { favoriteSector = StockSector.Automotive }));
        aiInvestors.Add(new AIInvestor("게임조아", InvestmentStyle.SectorSpecialist, 250000000, 6.5f, new AIPreference { favoriteSector = StockSector.Game }));
        aiInvestors.Add(new AIInvestor("친환경 지킴이", InvestmentStyle.SectorSpecialist, 300000000, 6.875f, new AIPreference { favoriteSector = StockSector.Energy }));
        aiInvestors.Add(new AIInvestor("IT 개발자", InvestmentStyle.SectorSpecialist, 500000000, 6.125f, new AIPreference { favoriteSector = StockSector.IT }));
        aiInvestors.Add(new AIInvestor("미식가", InvestmentStyle.SectorSpecialist, 150000000, 8.25f, new AIPreference { favoriteSector = StockSector.Food }));
        aiInvestors.Add(new AIInvestor("제약회사 연구원", InvestmentStyle.SectorSpecialist, 600000000, 6.625f, new AIPreference { favoriteSector = StockSector.Bio }));
        aiInvestors.Add(new AIInvestor("우주 덕후", InvestmentStyle.SectorSpecialist, 300000000, 7.25f, new AIPreference { favoriteSector = StockSector.Automotive })); // 우주선 관련

        aiInvestors.Add(new AIInvestor("StockFish v15.7", InvestmentStyle.HFT, 350000000, 0.1f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("Macro3.141592", InvestmentStyle.HFT, 200000000, 1f, new AIPreference()));

        aiInvestors.Add(new AIInvestor("여의도 불도저", InvestmentStyle.Aggressive, 120000000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("강남 큰손 할머니", InvestmentStyle.Defensive, 1000000000, 10.0f, new AIPreference { preferBlueChip = true }));
        aiInvestors.Add(new AIInvestor("판교 IT 부자", InvestmentStyle.Balanced, 1500000000, 6.0f, new AIPreference { favoriteSector = StockSector.IT }));
        aiInvestors.Add(new AIInvestor("은둔 고수", InvestmentStyle.Balanced, 800000000, 8.0f, new AIPreference())); // Value 스타일이 없다면 Balanced로 대체
        aiInvestors.Add(new AIInvestor("헤지펀드 매니저", InvestmentStyle.ShortSeller, 200000000, 4.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("시스템 트레이더", InvestmentStyle.HFT, 100000000, 1.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("가치투자 전도사", InvestmentStyle.Defensive, 900000000, 14.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("뉴스 헌터", InvestmentStyle.TrendFollower, 600000000, 3.5f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("피리 부는 사나이", InvestmentStyle.MarketManipulator, 150000000, 5.0f, new AIPreference())); // Insider 스타일 없다면 MarketManipulator나 Balanced로
        aiInvestors.Add(new AIInvestor("럭키가이", InvestmentStyle.Aggressive, 10000000, 7.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("불패신화", InvestmentStyle.Balanced, 250000000, 5.5f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("졸부", InvestmentStyle.Copycat, 300000000, 9.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("전세금 뺌", InvestmentStyle.Aggressive, 300000000, 12f, new AIPreference { riskTolerance = 0.9f }));
        aiInvestors.Add(new AIInvestor("안전 제일", InvestmentStyle.Defensive, 150000000, 18f, new AIPreference { preferBlueChip = true }));
        aiInvestors.Add(new AIInvestor("복리의 마법사", InvestmentStyle.DividendHunter, 500000000, 8.0f, new AIPreference { preferBlueChip = true }));
        aiInvestors.Add(new AIInvestor("은퇴 준비 김과장", InvestmentStyle.DividendHunter, 150000000, 15.0f, new AIPreference()));


        // 3. [개미 & 소형]
        aiInvestors.Add(new AIInvestor("배당이 연금이다", InvestmentStyle.DividendHunter, 9000000, 10.0f, new AIPreference { riskTolerance = 0.2f })); // 안전지향
        aiInvestors.Add(new AIInvestor("공포의 주둥아리", InvestmentStyle.MarketManipulator, 15000000, 2.5f, new AIPreference { riskTolerance = 1.0f }));
        aiInvestors.Add(new AIInvestor("존버는 승리한다", InvestmentStyle.Defensive, 4000000, 30f, new AIPreference { preferBlueChip = true }));
        aiInvestors.Add(new AIInvestor("무지성 탑승러", InvestmentStyle.Copycat, 5000000, 13.5f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("옆집 아저씨", InvestmentStyle.Copycat, 3000000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("직장인 김씨", InvestmentStyle.Copycat, 4000000, 18.0f, new AIPreference())); 
        aiInvestors.Add(new AIInvestor("늦깎이 투자자", InvestmentStyle.Copycat, 8000000, 22.0f, new AIPreference())); 
        aiInvestors.Add(new AIInvestor("팔랑귀", InvestmentStyle.Copycat, 1500000, 14.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("인생 한방", InvestmentStyle.Aggressive, 500000, 12.5f, new AIPreference { preferPennyStock = true }));
        aiInvestors.Add(new AIInvestor("불나방", InvestmentStyle.Aggressive, 200000, 12.0f, new AIPreference { riskTolerance = 1.0f })); 
        aiInvestors.Add(new AIInvestor("한강 뷰", InvestmentStyle.Aggressive, 1000000, 12.8f, new AIPreference { preferPennyStock = true }));
        aiInvestors.Add(new AIInvestor("가즈아", InvestmentStyle.Aggressive, 300000, 13.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("영끌족", InvestmentStyle.Aggressive, 10000000, 12.5f, new AIPreference { riskTolerance = 0.9f }));
        aiInvestors.Add(new AIInvestor("동전 수집가", InvestmentStyle.Aggressive, 500000, 12.2f, new AIPreference { preferPennyStock = true }));
        aiInvestors.Add(new AIInvestor("상따 초보", InvestmentStyle.Aggressive, 800000, 13.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("야수의 심장(짭)", InvestmentStyle.Aggressive, 150000, 13.5f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("불타기 장인", InvestmentStyle.TrendFollower, 4500000, 14.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("추격 매수자", InvestmentStyle.TrendFollower, 2500000, 15.5f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("박민재(대학생)", InvestmentStyle.TrendFollower, 200000, 16.0f, new AIPreference())); 
        aiInvestors.Add(new AIInvestor("군대간 친구", InvestmentStyle.TrendFollower, 500000, 25.0f, new AIPreference())); 
        aiInvestors.Add(new AIInvestor("뉴스만 믿음", InvestmentStyle.TrendFollower, 3500000, 19.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("청개구리", InvestmentStyle.Contrarian, 1200000, 16.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("떨어지는 칼날", InvestmentStyle.Contrarian, 800000, 14.5f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("마이너스의 손", InvestmentStyle.Contrarian, 400000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("이혜원(주부)", InvestmentStyle.Defensive, 3000000, 17.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("은퇴한 김부장", InvestmentStyle.Defensive, 8000000, 20.0f, new AIPreference { preferBlueChip = true }));
        aiInvestors.Add(new AIInvestor("적금 만기", InvestmentStyle.Defensive, 2000000, 23.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("쫄보", InvestmentStyle.Defensive, 500000, 18.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("김철수", InvestmentStyle.Balanced, 5000000, 18.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("사회 초년생", InvestmentStyle.Balanced, 1000000, 19.5f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("편의점 알바", InvestmentStyle.Balanced, 200000, 21.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("행복한 투자", InvestmentStyle.Balanced, 500000, 22.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("장기 투자자", InvestmentStyle.Balanced, 6000000, 24.0f, new AIPreference()));
        
        aiInvestors.Add(new AIInvestor("월급쟁이", InvestmentStyle.Balanced, 35000000, 15f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("마통 뚫음", InvestmentStyle.Aggressive, 5000000, 13.5f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("기도매매", InvestmentStyle.Defensive, 20000000, 22.5f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("영차영차", InvestmentStyle.Copycat, 15000000, 18f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("인간지표", InvestmentStyle.Contrarian, 10000000, 16.5f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("상따 매니아", InvestmentStyle.TrendFollower, 6000000, 13f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("하따 매니아", InvestmentStyle.Contrarian, 5500000, 14f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("주린이 1일차", InvestmentStyle.Copycat, 5000000, 27f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("소문난 똥손", InvestmentStyle.Contrarian, 8000000, 19.5f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("가상화폐 난민", InvestmentStyle.Aggressive, 2500000, 12.5f, new AIPreference { favoriteSector = StockSector.IT }));
        aiInvestors.Add(new AIInvestor("몰빵맨", InvestmentStyle.Aggressive, 4000000, 14.25f, new AIPreference { riskTolerance = 1.0f }));
        aiInvestors.Add(new AIInvestor("차트 분석가(초보)", InvestmentStyle.TrendFollower, 3000000, 15f, new AIPreference()));

    }

    #endregion

    #region Core AI Loop

    IEnumerator AITradingLoop()
    {
        foreach (var ai in aiInvestors)
        {
            ai.nextActTime = Time.time + Random.Range(1.0f, ai.reactionDelay);
        }

        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            float currentTime = Time.time;
            var publicInfo = market.GetCurrentEventInfo();

            CheckBankruptcy();

            for (int i = 0; i < aiInvestors.Count; i++)
            {
                AIInvestor ai = aiInvestors[i];
                if (currentTime < ai.nextActTime) continue;

                bool isNewEvent = publicInfo.hasEvent && (ai.lastReactedEventTitle != publicInfo.eventTitle);

                if (isNewEvent)
                {
                    if (Random.value < newsReactionProbability)
                    {
                        DecideAndTradeOnNews(ai, publicInfo.eventTitle);
                        ai.lastReactedEventTitle = publicInfo.eventTitle;
                    }
                    else ProcessNormalDecision(ai);
                }
                else ProcessNormalDecision(ai);

                ai.nextActTime = currentTime + ai.reactionDelay;
            }
        }
    }

    #endregion

    #region Decision Making (News & Info)

    void DecideAndTradeOnNews(AIInvestor ai, string currentEventTitle)
    {
        // 🐞 에러 수정: aiAssets 변수 선언 위치 이동 (함수 시작 부분으로)
        long aiAssets = ai.money - ai.currentDebt;
        foreach (var kvp in ai.portfolio)
        {
            var stock = market.marketStocks.Find(s => s.data == kvp.Key);
            if (stock != null) aiAssets += (long)stock.currentPrice * kvp.Value;
        }

        int selectedTier = DecideInfoSource(ai);
        long cost = market.CalculateInfoCost(selectedTier, aiAssets);

        while (ai.money < cost && selectedTier > 0)
        {
            selectedTier--;
            cost = market.CalculateInfoCost(selectedTier, aiAssets);
        }

        if (ai.money < cost) return;

        var boughtInfo = market.GetInfoForAI(ai, selectedTier);
        if (!boughtInfo.hasEvent || boughtInfo.targets == null || boughtInfo.targets.Count == 0) return;

        // 👑 [수정 반영] Rival의 초대형 시나리오 타겟 분배 로직을 위해 GetInfoForAI가 리턴한 rawTargets를 그대로 사용
        var finalTargets = InterpretInfo(ai, selectedTier, boughtInfo.targets);

        string sourceName = "";
        string logColor = "white";
        switch (selectedTier)
        {
            case 0: sourceName = "사기꾼"; logColor = "black"; break;
            case 1: sourceName = "분석가"; logColor = "yellow"; break;
            case 2: sourceName = "해커"; logColor = "blue"; break;
            case 3: sourceName = "내부자"; logColor = "orange"; break;
            case 4: sourceName = "첩보원"; logColor = "purple"; break; 
            case 5: sourceName = "신문팔이"; logColor = "#6F4F28"; break; 
            case 6: sourceName = "로비스트"; logColor = "#8B4513"; break; 
            case 7: sourceName = "브로커"; logColor = "#FBCEB1"; break; 
        }

        foreach (var targetStock in finalTargets.Keys)
        {
            float multiplier = finalTargets[targetStock];
            bool isGoodNews = multiplier >= 1.0f;
            Debug.Log($"<color={logColor}><b>[{sourceName}]</b></color> 🤖 {ai.name} ({ai.style}) 정보 획득 -> {targetStock.data.stockName} 대응");
            
            // 👑 [핵심] Rival은 초대형 시나리오(복수 타겟)일 때만 Mega 로직을 사용
            // boughtInfo.targets는 market.GetInfoForAI에서 얻은 '원본' 정보이며,
            // 초대형 시나리오의 경우 모든 타겟이 들어있습니다.
            if (ai.style == InvestmentStyle.Rival && boughtInfo.targets.Count > 1)
            {
                // 초대형 이벤트 여부는 StarMarketManager에서 MegaEvent일 때 boughtInfo.targets가 여러개로 리턴되므로
                // 이를 통해 판단 가능합니다.
                ReactToStockRivalMega(ai, targetStock, boughtInfo.targets);
            }
            else
            {
                // 일반 이벤트(단일 타겟) 또는 일반 AI는 기본 로직 사용
                ReactToStock(ai, targetStock, isGoodNews);
            }
        }
    }

    int DecideInfoSource(AIInvestor ai)
    {
        // 👑 [라이벌 로직] - 가장 확실한 T3/T4/T2만 사용 (T0, T1 같은 불확실성 제거)
        if (ai.style == InvestmentStyle.Rival)
        {
            // 20억 이상: T3 내부자 (가장 완벽한 정보)
            if (ai.money > 2000000000) return 3; 
            
            // 첩보원(T4) 비용 계산 및 선호 (최고 경쟁자 파악)
            long aiAssets = ai.money - ai.currentDebt + CalculateStockValue(ai.portfolio);
            long spyCost = market.CalculateInfoCost(4, aiAssets);

            // 첩보원 살 돈이 있으면 T4, 없으면 T2 (해커)
            if (ai.money >= spyCost) return 4;
            else return 2; 
        }

        // 돈이 10만원 미만이면 60% 확률로 사기꾼 선택
        if (ai.money < 100000 && Random.value < 0.6f) return 0; 

        float dice = Random.value;

        // 부자 AI (10억 이상)는 T3/T4/T6 선호
        if (ai.money >= 1000000000)
        {
            if (dice < 0.4f) return 3; // 40% 내부자
            else if (dice < 0.6f) return 4; // 20% 첩보원
            else if (dice < 0.8f) return 6; // 20% 로비스트
            else return 2; // 20% 해커
        }

        switch (ai.style)
        {
            // 😈 작전 세력: 내부자(3)로 완벽한 정보 독점 후 펌핑/덤핑, 또는 첩보원(4)으로 경쟁 피하기
            case InvestmentStyle.MarketManipulator:
                if (dice < 0.5f) return 3; 
                else if (dice < 0.8f) return 4; 
                else return 0; // T0 사기꾼으로 싼값에 역정보를 퍼뜨리려 시도

            // ⚡ 초단타: 변동폭/로우 데이터 선호
            case InvestmentStyle.HFT:
                if (dice < 0.45f) return 5; // 45% 신문팔이 (변동폭 Magnitude)
                else if (dice < 0.85f) return 2; // 40% 해커 (로우 데이터)
                else return 1; // 15% 분석가

            // 🔬 섹터 전문가: 매크로/정책 중시 (정책적 확신을 위해 T6/T3/T1 사용)
            case InvestmentStyle.SectorSpecialist:
                if (dice < 0.55f) return 6; // 55% 로비스트 (섹터 정보)
                else if (dice < 0.85f) return 1; // 30% 분석가 (정기 리포트)
                else return 3; // 15% 내부자 (가끔 확실하게)

            // 💰 배당 사냥꾼: 정책/안정성 중시 (T6으로 정책 리스크 회피)
            case InvestmentStyle.DividendHunter:
                if (dice < 0.7f) return 6; // 70% 로비스트 (정책 리스크 관리)
                else return 1; // 30% 분석가

            // 🔥 공격형: 도박 성향이 강함. 브로커(7)를 가장 선호함.
            case InvestmentStyle.Aggressive:
                long aiAssets = ai.money - ai.currentDebt + CalculateStockValue(ai.portfolio);
                // 브로커는 비용이 100000원으로 고정되므로, 현금만 있다면 선택 가능
                long brokerCost = market.CalculateInfoCost(7, aiAssets);
                if (ai.money >= brokerCost)
                {
                    if (dice < 0.35f) return 7; // 35% 브로커 (극단적 도박)
                    else if (dice < 0.7f) return 5; // 35% 신문팔이 (변동성 매매)
                    else return 2; // 30% 해커 (로우 데이터)
                }
                else
                {
                    return 0; // 돈이 없으면 사기꾼
                }

            // 💎 역발상: 사기꾼 정보를 이용해 남들과 반대로 가거나, 변동폭 큰 종목 노림
            case InvestmentStyle.Contrarian:
                if (dice < 0.5f) return 0; // 50% 사기꾼 (남들이 믿는 정보 반대로)
                else if (dice < 0.8f) return 5; // 30% 신문팔이 (변동성 노리고 진입)
                else return 2; // 20% 해커
                
            // 🦈 공매도형: 정확한 타이밍과 정보 요구
            case InvestmentStyle.ShortSeller: return (dice < 0.6f) ? 3 : 2; // 내부자(60%) 또는 해커(40%)
            
            // 쫄보/무난/따라쟁이는 가성비나 안정성 위주
            case InvestmentStyle.Defensive: return (dice < 0.7f) ? 1 : 6; // 분석가(저렴) 또는 로비스트(거시 정책)
            case InvestmentStyle.Balanced: return (dice < 0.4f) ? 1 : (dice < 0.7f ? 2 : 4); // 분석가, 해커, 첩보원
            case InvestmentStyle.Copycat: 
                // 첩보원(4)으로 1등 따라가거나, 분석가(1)로 가성비 추구
                if (dice < 0.6f) return 1; // 분석가 정보로 따라하기 (60%)
                else return 4; // 첩보원 (40%)
                
            default: return Random.Range(0, 3);
        }
    }

    // (헬퍼 함수 추가)
    long CalculateStockValue(Dictionary<StockData, int> portfolio)
    {
        long val = 0;
        foreach(var kvp in portfolio) 
        {
            // market.marketStocks는 StockMarketManager에 정의되어 있습니다.
            var s = market.marketStocks.Find(st => st.data == kvp.Key); 
            if(s != null) val += (long)s.currentPrice * kvp.Value;
        }
        return val;
    }

    Dictionary<RuntimeStock, float> InterpretInfo(AIInvestor ai, int tier, Dictionary<RuntimeStock, float> rawTargets)
    {
        // Tier 3(내부자), T4(첩보원), T6(로비스트)는 완벽한 정보
        if ((tier >= 3 && tier != 7) || tier == 4 || tier == 6 || tier == 5) return rawTargets; 
        
        // Tier 7 (브로커)도 완벽한 정보
        if (tier == 7) return rawTargets;


        // 👑 [라이벌 로직] "압도적인 통찰력"
        if (ai.style == InvestmentStyle.Rival)
        {
            // Tier 1(분석가) 이상이면 100% 정답
            if (tier >= 1) return rawTargets;

            // Tier 0(사기꾼)일 경우: 사기꾼은 80% 확률로 거짓말을 함.
            if (tier == 0)
            {
                // 사실상 치트: 90% 확률로 원본 데이터(rawTargets가 아닌 실제 이벤트 타겟)를 유추해냄
                float rivalClairvoyanceChance = 0.9f;
                if (Random.value < rivalClairvoyanceChance)
                {
                    var realEventInfo = market.GetCurrentEventInfo(); 
                    if (realEventInfo.hasEvent && realEventInfo.targets != null)
                    {
                        // 초대형 이벤트는 GetCurrentEventInfo()에서 targets가 정확하게 나옴
                        if (realEventInfo.targets.Count > 0) return realEventInfo.targets; 
                    }
                }
            }
            return rawTargets; 
        }

        // 일반 AI 지능 계산 (반응 지연 시간이 짧을수록 지능 높음)
        float intelligence = Mathf.Clamp01(1.0f - (ai.reactionDelay / 20.0f));
        
        if (tier == 0) return rawTargets; // 사기꾼 정보는 그대로(변조된 상태로) 사용

        if (tier == 1) // 분석가 (종목은 헷갈리고, 호재/악재는 비교적 잘 맞춤)
        {
            float successChance = 0.3f + (intelligence * 0.6f);
            if (Random.value < successChance) return rawTargets;
            else
            {
                Dictionary<RuntimeStock, float> confused = new Dictionary<RuntimeStock, float>();
                if (rawTargets.Count > 0)
                {
                    var originalStock = rawTargets.Keys.First();
                    var sameSectorStocks = market.marketStocks.Where(s => s.data.sector == originalStock.data.sector && s != originalStock).ToList();
                    RuntimeStock wrongPick = (sameSectorStocks.Count > 0) ? sameSectorStocks[Random.Range(0, sameSectorStocks.Count)] : market.marketStocks[Random.Range(0, market.marketStocks.Count)];
                    confused.Add(wrongPick, rawTargets.Values.First()); // 배율은 유지하고 타겟만 바꿈
                    Debug.Log($"😵 <b>{ai.name}</b>(분석가): 보고서 해석 오류! {originalStock.data.stockName} 대신 {wrongPick.data.stockName} 지목.");
                }
                return confused;
            }
        }

        if (tier == 2) // 해커 (데이터가 깨짐, 랜덤 종목을 고름)
        {
            float successChance = 0.5f + (intelligence * 0.4f);
            if (Random.value < successChance) return rawTargets;
            else
            {
                Dictionary<RuntimeStock, float> confused = new Dictionary<RuntimeStock, float>();
                if (rawTargets.Count > 0)
                {
                    float multiplier = rawTargets.Values.First();
                    var wrongStock = market.marketStocks[Random.Range(0, market.marketStocks.Count)];
                    confused.Add(wrongStock, multiplier);
                }
                return confused;
            }
        }
        
        return rawTargets;
    }

    #endregion

    #region Trading Execution

    // 👑 [신규/핵심] Rival의 초대형 시나리오 대응 로직 (배율 기반 동적 베팅)
    void ReactToStockRivalMega(AIInvestor ai, RuntimeStock target, Dictionary<RuntimeStock, float> allTargets)
    {
        // 1. 타겟 종목의 실제 배율(Multiplier)을 가져옴
        float multiplier = allTargets.ContainsKey(target) ? allTargets[target] : 1.0f;
        bool isGoodNews = multiplier >= 1.0f;
        const long AGGRESSIVE_THRESHOLD = 5000000;
        long currentAsset = GetAITotalAsset(ai); // 🐞 에러 수정: aiAssets 대신 currentAsset 사용

        // 2. 가장 높은 배율의 종목을 찾기 (집중 투자 대상)
        // 1.05배 이상 중 가장 높은 종목
        var primaryTarget = allTargets.Where(kvp => kvp.Value >= 1.05f).OrderByDescending(kvp => kvp.Value).FirstOrDefault();
        // 0.95배 이하 중 가장 낮은 종목
        var antiTarget = allTargets.Where(kvp => kvp.Value <= 0.95f).OrderBy(kvp => kvp.Value).FirstOrDefault();


        // 3. 베팅 비율 계산 (배율에 따른 동적 결정)
        float baseRatio = (currentAsset < AGGRESSIVE_THRESHOLD) ? 0.3f : 0.6f; // 공격 모드 60%, 생존 모드 30%
        float finalRatio = baseRatio;

        if (multiplier >= 1.0f) // 호재
        {
            float betWeight = Mathf.Clamp01((multiplier - 1.0f) / 1.5f); // 1.0 -> 0, 2.5 -> 1.0
            finalRatio = baseRatio + (betWeight * (0.95f - baseRatio)); // baseRatio에서 95%까지 확장
            finalRatio = Mathf.Clamp(finalRatio, 0.2f, 0.95f);
        }
        else // 악재
        {
            float betWeight = Mathf.Clamp01(1.0f - multiplier); // 1.0 -> 0, 0.0 -> 1.0
            finalRatio = baseRatio + (betWeight * (0.95f - baseRatio));
            finalRatio = Mathf.Clamp(finalRatio, 0.2f, 0.95f);
        }

        // 4. 매매 실행
        if (isGoodNews)
        {
            // 빚 갚기 우선
            if (ai.currentDebt > 0) TryShortCover(ai, target, true);

            // 공격 모드 & 최고 호재 종목이면 풀 대출 후 집중 투자
            if (currentAsset >= AGGRESSIVE_THRESHOLD && target == primaryTarget.Key)
            {
                PerformAILoan(ai, 1.0f); // 풀 대출
                TryBuyStock(ai, target, 0.95f); // 거의 풀 베팅
            }
            // 그 외 호재 종목
            else if (target == primaryTarget.Key)
            {
                TryBuyStock(ai, target, finalRatio);
            }
            else // 서브 호재 종목은 소액만
            {
                // 타겟 배율이 1.1배 이하는 무시하거나 소액만
                if (multiplier > 1.1f)
                {
                    TryBuyStock(ai, target, finalRatio * 0.2f);
                }
            }
        }
        else // 악재
        {
            // 보유 주식 전량 매도
            TrySellStock(ai, target, true);

            // 공격 모드 & 최고 악재 종목이면 집중 공매도
            if (currentAsset >= AGGRESSIVE_THRESHOLD && target == antiTarget.Key)
            {
                TryShortSell(ai, target, 0.9f); 
            }
            // 그 외 악재 종목
            else if (target == antiTarget.Key)
            {
                TryShortSell(ai, target, finalRatio);
            }
            // 생존 모드에서는 공매도 자제 (악재 강도 무시)
        }
    }


    void ReactToStock(AIInvestor ai, RuntimeStock target, bool isGoodNews)
    {
        // 👑 [라이벌 로직] (일반 이벤트/단일 종목일 때의 기본 Rival 로직)
        if (ai.style == InvestmentStyle.Rival)
        {
            const long AGGRESSIVE_THRESHOLD = 5000000; // 500만원 이상이면 공격 모드
            long currentAsset = GetAITotalAsset(ai);

            if (isGoodNews)
            {
                // 1. 빚 갚기 우선 (이자 지출 최소화)
                if (ai.currentDebt > 0) TryShortCover(ai, target, true);

                // 2. 모드에 따른 베팅
                if (currentAsset < AGGRESSIVE_THRESHOLD) 
                {
                    TryBuyStock(ai, target, 0.5f); // 생존 모드: 현금 50%
                }
                else 
                {
                    PerformAILoan(ai, 1.0f); // 공격 모드: 풀 대출
                    TryBuyStock(ai, target, 0.95f); // 자금의 95% 투입
                }
            }
            else // 악재 발생
            {
                TrySellStock(ai, target, true); // 보유 주식 전량 매도

                if (currentAsset >= AGGRESSIVE_THRESHOLD) 
                {
                    TryShortSell(ai, target, 0.9f); // 공격 모드: 풀 공매도
                }
            }
            return; // 라이벌 로직 종료
        }

        switch (ai.style)
        {
            // 😈 [신규] 작전 세력: 시장을 흔들기 위해 과감하게 지름
            case InvestmentStyle.MarketManipulator:
                if (isGoodNews) TryBuyStock(ai, target, 0.7f); // 자산 70% 투입 (Pump)
                else TryShortSell(ai, target, 0.8f); // 자산 80% 공매도 (Panic inducing)
                break;

            // ⚡ [신규] 초단타: 짧게 먹고 빠짐
            case InvestmentStyle.HFT:
                if (isGoodNews) TryBuyStock(ai, target, 0.3f); // 30% 진입
                else TrySellStock(ai, target, true); // 악재면 즉시 전량 매도
                break;

            // 🔬 [신규] 섹터 전문가: 내 분야 아니면 관심 없음
            case InvestmentStyle.SectorSpecialist:
                // 선호 섹터가 있고, 타겟이 그 섹터가 아니면 무시
                if (ai.preference.favoriteSector.HasValue && target.data.sector != ai.preference.favoriteSector.Value)
                {
                    return; 
                }
                
                // 내 분야면 확실하게 배팅
                if (isGoodNews) TryBuyStock(ai, target, 0.6f);
                else TrySellStock(ai, target, true);
                break;
            // 💰 [신규] 배당 사냥꾼: 배당주는 안 팜. 오히려 악재(주가 하락)를 기회로 봄.
            case InvestmentStyle.DividendHunter:
                // 배당금을 주는 주식인가?
                if (target.data.dividendPerShare > 0)
                {
                    if (isGoodNews) 
                    {
                        TryBuyStock(ai, target, 0.2f);
                    }
                    else 
                    {
                        if (target.currentPrice > 1000)
                            TryBuyStock(ai, target, 0.5f); 
                        else
                            TrySellStock(ai, target, true); // 망할 것 같으면 튐
                    }
                }
                else
                {
                    TrySellStock(ai, target, true);
                }
                break;
            case InvestmentStyle.TrendFollower: 
                if (isGoodNews) TryBuyStock(ai, target, 0.8f); else TrySellStock(ai, target, true); break;
            
            // 🔥 공격형: 배팅 비율 80%로 상향
            case InvestmentStyle.Aggressive: 
                if (isGoodNews) TryBuyStock(ai, target, 0.8f); 
                else if (Random.value < 0.5f) TryShortSell(ai, target, 0.4f); // 악재에 50% 확률로 공매도
                else TrySellStock(ai, target, true);
                break;
                
            // 🛡️ 방어형: 배팅 비율 10%로 축소
            case InvestmentStyle.Defensive: 
                if (isGoodNews) TryBuyStock(ai, target, 0.1f); 
                else TrySellStock(ai, target, true); 
                break;
                
            case InvestmentStyle.Contrarian: 
                if (isGoodNews) TrySellStock(ai, target, true); 
                else TryBuyStock(ai, target, 0.5f); 
                break;
            
            // 🦈 공매도형: 악재 시 무조건 공매도
            case InvestmentStyle.ShortSeller: 
                if (isGoodNews) TryShortCover(ai, target, true); 
                else TryShortSell(ai, target, 0.7f); // 공매도 비중 70%로 상향
                break;
                
            case InvestmentStyle.Balanced: case InvestmentStyle.Copycat: 
                if (isGoodNews) TryBuyStock(ai, target, 0.3f); 
                else TrySellStock(ai, target, false); 
                break;
        }
    }

    // 👑 [신규] AI용 대출 실행 함수 (비율로 대출)
    void PerformAILoan(AIInvestor ai, float ratio)
    {
        long totalAsset = ai.money - ai.currentDebt;
        foreach(var kvp in ai.portfolio)
        {
            var stock = market.marketStocks.Find(s => s.data == kvp.Key);
            if(stock != null) totalAsset += (long)stock.currentPrice * kvp.Value;
        }

        // 대출 한도 (플레이어와 동일하게 50% 설정)
        long maxLoan = (long)(totalAsset * 0.5f); 
        long borrowable = maxLoan - ai.currentDebt;

        if (borrowable > 0)
        {
            long amountToBorrow = (long)(borrowable * ratio);
            
            if(amountToBorrow > 0) {
                ai.currentDebt += amountToBorrow;
                ai.money += amountToBorrow;
                Debug.Log($"💰 <b>{ai.name}</b>: 전략적 {ratio:P0} 대출 ({amountToBorrow:N0}원) 실행.");
            }
        }
    }

    void ProcessNormalDecision(AIInvestor ai)
    {
        if (ai.style == InvestmentStyle.Copycat) { TryCopyTopRanker(ai); return; }

        // 👑 [라이벌 로직] 모멘텀 투자 (추세 추종)
        if (ai.style == InvestmentStyle.Rival)
        {
            const long AGGRESSIVE_THRESHOLD = 5000000; // 500만원
            
            // 1. 빚 갚기 우선 (리스크 관리)
            if (ai.currentDebt > 0 && ai.money > ai.currentDebt * 1.5f)
            {
                long repay = ai.currentDebt;
                ai.money -= repay;
                ai.currentDebt = 0;
                Debug.Log($"👑 <b>{ai.name}</b>: 리스크 관리. 대출금 {repay:N0}원 전액 상환.");
            }
            
            // 2. 자금 운용: 빚이 없고 공격 모드가 아닐 때, 대출 한도의 절반을 미리 확보 (유동성 확보)
            if (ai.currentDebt == 0 && GetAITotalAsset(ai) < AGGRESSIVE_THRESHOLD)
            {
                PerformAILoan(ai, 0.5f); // 총 한도의 50%만 대출 (리스크 최소화)
            }
            
            // 3. 시장 상황에 따른 매매 결정 (급등/급락 추종)
            var soaringStock = market.marketStocks.OrderByDescending(s => s.GetChangePercent()).FirstOrDefault();
            var crashingStock = market.marketStocks.OrderBy(s => s.GetChangePercent()).FirstOrDefault();

            // 상승장이면 매수 (3% 이상 급등 시)
            if (soaringStock != null && soaringStock.GetChangePercent() > 3.0f)
            {
                if (ai.money > soaringStock.currentPrice)
                {
                    TryBuyStock(ai, soaringStock, 0.5f); 
                }
            }
            // 하락장이면 공매도 (3% 이상 급락 시)
            else if (crashingStock != null && crashingStock.GetChangePercent() < -3.0f)
            {
                // 보유 주식이 있다면 매도하여 현금 확보를 시도
                if (ai.portfolio.ContainsKey(crashingStock.data)) TrySellStock(ai, crashingStock, true); 
                // 공격 모드라면 공매도 시도
                else if (GetAITotalAsset(ai) >= AGGRESSIVE_THRESHOLD) TryShortSell(ai, crashingStock, 0.3f);
            }
            // 횡보장이면: 이익 실현 후 Event Potential 주식에 소액 투자
            else
            {
                // 10% 이상 수익이면 익절
                foreach (var key in new List<StockData>(ai.portfolio.Keys))
                {
                    var stock = market.marketStocks.Find(s => s.data == key);
                    if (stock != null && ai.avgCost.ContainsKey(key))
                        // 평단가 대비 10% 이상 수익 시
                        if (stock.currentPrice >= ai.avgCost[key] * 1.1f) TrySellStock(ai, stock, true);
                }
            }
            return;
        }

        // 😈 [신규] 작전 세력: Pump & Dump (동전주 매집 후 털기)
        if (ai.style == InvestmentStyle.MarketManipulator)
        {
            // 1. 수익 실현 (보유 주식 중 20% 이상 오른 것 투매)
            foreach (var key in new List<StockData>(ai.portfolio.Keys))
            {
                var stock = market.marketStocks.Find(s => s.data == key);
                if (stock != null && ai.avgCost.ContainsKey(key))
                {
                    if (stock.currentPrice >= ai.avgCost[key] * 1.2f) // 20% 수익
                    {
                        TrySellStock(ai, stock, true); // 전량 매도 (Dump)
                        Debug.Log($"😈 <b>{ai.name}</b>: {stock.data.stockName} 설거지 완료 (익절).");
                        return;
                    }
                }
            }

            // 2. 매집 (가격이 싼 동전주 타겟)
            var pennyStock = market.marketStocks
                .Where(s => s.currentPrice < 5000)
                .OrderBy(x => Random.value) // 랜덤 선택
                .FirstOrDefault();

            if (pennyStock != null)
            {
                TryBuyStock(ai, pennyStock, 0.4f); // 40% 매수 (Pump 시도)
            }
            return;
        }

        // ⚡ [신규] 초단타 (HFT): 스캘핑 목표치 5%로 상향
        if (ai.style == InvestmentStyle.HFT)
        {
            // 1. 짧은 익절 (5%만 먹어도 팜)
            foreach (var key in new List<StockData>(ai.portfolio.Keys))
            {
                var stock = market.marketStocks.Find(s => s.data == key);
                if (stock != null && ai.avgCost.ContainsKey(key))
                {
                    if (stock.currentPrice >= ai.avgCost[key] * 1.05f) // 5% 수익
                    {
                        TrySellStock(ai, stock, true);
                        return;
                    }
                }
            }

            // 2. 변동성 있는 주식 매수 (변동성이 0.1 이상인 주식 선호)
            var candidates = market.marketStocks
                .Where(s => s.data.volatility >= 0.1f)
                .ToList();
                
            if (candidates.Count > 0)
            {
                var target = candidates[Random.Range(0, candidates.Count)];
                TryBuyStock(ai, target, 0.15f); // 조금씩 자주 삼 (15% 투입)
            }
            else
            {
                TryBuyGeneral(ai);
            }
            return;
        }

        // 🔬 [신규] 섹터 전문가: 내 구역만 판다
        if (ai.style == InvestmentStyle.SectorSpecialist && ai.preference.favoriteSector.HasValue)
        {
            var mySectorStocks = market.marketStocks
                .Where(s => s.data.sector == ai.preference.favoriteSector.Value)
                .ToList();

            if (mySectorStocks.Count > 0)
            {
                var target = mySectorStocks[Random.Range(0, mySectorStocks.Count)];
                // 랜덤하게 사고 팔기
                if (Random.value < 0.6f) TryBuyStock(ai, target, 0.3f);
                else TrySellStock(ai, target, false);
            }
            return;
        }

        // 💰 [신규] 배당 사냥꾼: 배당 수익률이 높은 주식 매집
        if (ai.style == InvestmentStyle.DividendHunter)
        {
            // 1. 배당금이 있는 주식 중, (배당금 / 현재가) 비율이 가장 높은 종목 찾기
            var bestDividendStock = market.marketStocks
                .Where(s => s.data.dividendPerShare > 0)
                .OrderByDescending(s => (float)s.data.dividendPerShare / s.currentPrice) // 배당 수익률 내림차순
                .FirstOrDefault();

            if (bestDividendStock != null)
            {
                TryBuyStock(ai, bestDividendStock, 0.25f); // 25% 비율로 꾸준히 매수
            }
            return;
        }

        float buyChance = (ai.portfolio.Count == 0) ? 0.7f : 0.4f;
        if (Random.value < buyChance) TryBuyGeneral(ai); else TrySellGeneral(ai);
    }

    void TryBuyStock(AIInvestor ai, RuntimeStock target, float investRatio)
    {
        if (target == null || target.currentPrice <= 0 || target.remainShares <= 0) return;
        if (ai.money < target.currentPrice) return;

        long investAmount = (long)(ai.money * investRatio);
        int countToBuy = (int)(investAmount / target.currentPrice);
        countToBuy = Mathf.Clamp(countToBuy, 1, target.remainShares);
        if (countToBuy <= 0) return;

        long cost = (long)countToBuy * target.currentPrice;
        ai.money -= cost; target.remainShares -= countToBuy;

        if (ai.portfolio.ContainsKey(target.data))
        {
            int oldQty = ai.portfolio[target.data];
            int oldCost = ai.avgCost.ContainsKey(target.data) ? ai.avgCost[target.data] : target.previousPrice;
            long totalVal = ((long)oldCost * oldQty) + cost;
            ai.avgCost[target.data] = (int)(totalVal / (oldQty + countToBuy));
            ai.portfolio[target.data] += countToBuy;
        }
        else
        {
            ai.portfolio.Add(target.data, countToBuy);
            ai.avgCost.Add(target.data, target.currentPrice);
        }
        // StockMarketManager의 ApplyMarketImpact를 호출합니다.
        market.ApplyMarketImpact(target, countToBuy, true);
        // ➕ [추가] 행동 기록
        ai.lastTradeLog = $"<b>[{target.data.stockName}]</b>를(을) <color=red>대량 매수</color>했습니다.";
        Debug.Log($"🤖 {GetStyleIcon(ai.style)} <b>{ai.name}</b>: {target.data.stockName} <color=red>{countToBuy:N0}주 매수</color>");
    }

    void TrySellStock(AIInvestor ai, RuntimeStock target, bool sellAll)
    {
        if (!ai.portfolio.ContainsKey(target.data)) return;
        int myCount = ai.portfolio[target.data];
        int countToSell = sellAll ? myCount : myCount / 2;
        if (countToSell == 0) countToSell = 1;

        long income = (long)countToSell * target.currentPrice;
        ai.money += income; ai.portfolio[target.data] -= countToSell;
        if (ai.portfolio[target.data] <= 0) { ai.portfolio.Remove(target.data); ai.avgCost.Remove(target.data); }
        target.remainShares += countToSell;
        // StockMarketManager의 ApplyMarketImpact를 호출합니다.
        market.ApplyMarketImpact(target, countToSell, false);
        // ➕ [추가] 행동 기록
        string type = sellAll ? "전량 매도" : "일부 매도";
        ai.lastTradeLog = $"<b>[{target.data.stockName}]</b>를(을) <color=blue>{type}</color>하고 이익을 실현했습니다.";
        Debug.Log($"🤖 {GetStyleIcon(ai.style)} {ai.name}: {target.data.stockName} <color=blue>{countToSell:N0}주 매도</color>");
    }

    void TryShortSell(AIInvestor ai, RuntimeStock target, float investRatio)
    {
        if (target.remainShares <= 0) return;
        long maxShortAmount = (long)(ai.money * investRatio);
        int countToShort = (int)(maxShortAmount / target.currentPrice);
        if (countToShort <= 0) return;

        long income = (long)countToShort * target.currentPrice;
        ai.money += income; target.remainShares -= countToShort;

        if (ai.shortPositions.ContainsKey(target.data)) ai.shortPositions[target.data] += countToShort;
        else ai.shortPositions.Add(target.data, countToShort);

        // StockMarketManager의 ApplyMarketImpact를 호출합니다.
        market.ApplyMarketImpact(target, countToShort, false);
        // ➕ [추가] 행동 기록
        ai.lastTradeLog = $"<b>[{target.data.stockName}]</b>에 <color=blue>공매도 폭격</color>을 가했습니다.";
        Debug.Log($"🤖 📉 <b>{ai.name}</b>: {target.data.stockName} {countToShort}주 공매도");
    }

    void TryShortCover(AIInvestor ai, RuntimeStock target, bool coverAll)
    {
        if (!ai.shortPositions.ContainsKey(target.data)) return;
        int myShorts = ai.shortPositions[target.data];
        int countToCover = coverAll ? myShorts : myShorts / 2;
        if (countToCover == 0) countToCover = 1;

        long cost = (long)countToCover * target.currentPrice;
        if (ai.money >= cost)
        {
            ai.money -= cost; ai.shortPositions[target.data] -= countToCover;
            if (ai.shortPositions[target.data] <= 0) ai.shortPositions.Remove(target.data);
            target.remainShares += countToCover;
            // StockMarketManager의 ApplyMarketImpact를 호출합니다.
            market.ApplyMarketImpact(target, countToCover, true);
            // ➕ [추가] 행동 기록
            ai.lastTradeLog = $"<b>[{target.data.stockName}]</b>의 공매도 포지션을 <color=red>상환(숏커버)</color>했습니다.";
            Debug.Log($"🤖 🔄 {ai.name}: {target.data.stockName} {countToCover}주 숏커버링 (상환)");
        }
    }
    

    // Helper Trading Methods
    void TryCopyTopRanker(AIInvestor ai)
    {
        var topAssetAI = aiInvestors.OrderByDescending(a => GetAITotalAsset(a)).FirstOrDefault();
        // 랭킹 매니저가 없으므로 시장의 최고가 주식으로 대체
        if (topAssetAI == null || topAssetAI.name == ai.name) 
        {
             var hotStock = market.marketStocks.OrderByDescending(s => s.currentPrice).FirstOrDefault();
             if (hotStock != null) TryBuyStock(ai, hotStock, 0.2f);
        }
        // TODO: 랭킹 1위의 포트폴리오를 보고 따라사는 로직 구현 필요
    }

    void TrySellGeneral(AIInvestor ai)
    {
        if (ai.portfolio.Count == 0) return;
        List<StockData> myKeys = new List<StockData>(ai.portfolio.Keys);
        var targetData = myKeys[Random.Range(0, myKeys.Count)];
        var stock = market.marketStocks.Find(s => s.data == targetData);
        if (stock != null) TrySellStock(ai, stock, false);
    }

    void TryBuyGeneral(AIInvestor ai)
    {
        var candidates = market.marketStocks.Where(s => IsPreferredStock(ai, s)).ToList();
        if (candidates.Count == 0) candidates = market.marketStocks;
        var target = candidates[Random.Range(0, candidates.Count)];
        TryBuyStock(ai, target, Random.Range(0.1f, 0.3f));
    }

    bool IsPreferredStock(AIInvestor ai, RuntimeStock stock)
    {
        if (ai.preference.favoriteSector.HasValue && stock.data.sector != ai.preference.favoriteSector.Value) return false;
        if (ai.preference.preferPennyStock && stock.currentPrice > 5000) return false;
        if (ai.preference.preferBlueChip && stock.currentPrice < 50000) return false;
        return true;
    }

    #endregion

    #region Financial Management & Utils
    
    // ➕ [신규] AI의 총 자산 계산 헬퍼 (랭킹 산정용)
    public long GetAITotalAsset(AIInvestor ai)
    {
        long stockVal = CalculateStockValue(ai.portfolio);
        return ai.money + stockVal - ai.currentDebt;
    }

    void CheckBankruptcy()
    {
        for (int i = aiInvestors.Count - 1; i >= 0; i--)
        {
            AIInvestor ai = aiInvestors[i];
            long totalAsset = ai.money - ai.currentDebt;
            List<StockData> myKeys = new List<StockData>(ai.portfolio.Keys);
            foreach (var key in myKeys)
            {
                var stock = market.marketStocks.Find(s => s.data == key);
                if (stock != null) totalAsset += (long)stock.currentPrice * ai.portfolio[key];
                else
                {
                    ai.portfolio.Remove(key);
                    if (ai.avgCost.ContainsKey(key)) ai.avgCost.Remove(key);
                }
            }

            if (totalAsset <= bankruptcyThreshold)
            {
                Debug.Log($"💀 <color=red><b>[파산]</b></color> {ai.name} 시장 퇴출! (남은 자산: {totalAsset:N0}원)");
                aiInvestors.RemoveAt(i);
            }
        }
    }

    public void ProcessAILoans()
    {
        float currentRate = market.GetCurrentLoanRate(); // [변경] 변동 금리 가져오기

        foreach (var ai in aiInvestors)
        {
            if (ai.currentDebt > 0) 
            {
                // 1턴당 이자 = 연이율 / 턴수 개념이지만, 게임적 허용으로 단순 비율 적용
                // 너무 쎄면 (currentRate / 10) 정도로 조정
                long interest = (long)(ai.currentDebt * currentRate); 
                ai.money -= interest;
            }
            ManageDebt(ai);
        }
    }

    public void ApplyTaxToAI() { foreach (var ai in aiInvestors) { if (ai.money > 0) ai.money -= (long)(ai.money * 0.01f); } }

    public void DistributeAIDividends()
    {
        foreach (var ai in aiInvestors)
        {
            long totalDiv = 0;
            foreach (var item in ai.portfolio) { if (item.Key.dividendPerShare > 0) totalDiv += (long)item.Key.dividendPerShare * item.Value; }
            if (totalDiv > 0) ai.money += totalDiv;
        }
    }

    // RuntimeStock FindStock(StockData data) => market.marketStocks.Find(s => s.data == data); // StockMarketManager에 정의되어 있으므로 불필요

    string GetStyleIcon(InvestmentStyle style)
    {
        switch (style)
        {
            case InvestmentStyle.Aggressive: return "🔥";
            case InvestmentStyle.Defensive: return "🛡️";
            case InvestmentStyle.Balanced: return "⚖️";
            case InvestmentStyle.TrendFollower: return "📈";
            case InvestmentStyle.Contrarian: return "💎";
            case InvestmentStyle.ShortSeller: return "🦈";
            case InvestmentStyle.Copycat: return "👯";
            case InvestmentStyle.Rival: return "👑";
            case InvestmentStyle.MarketManipulator: return "😈";
            case InvestmentStyle.HFT: return "⚡";
            case InvestmentStyle.SectorSpecialist: return "🔬";
            case InvestmentStyle.DividendHunter: return "💰";
            default: return "⚖️";
        }
    }

    // 💳 [신규] AI 부채 상환 로직
    void ManageDebt(AIInvestor ai)
    {
        if (ai.currentDebt <= 0) return;

        float currentRate = market.GetCurrentLoanRate();
        
        // 🏦 [금리 반영] 금리가 8%를 넘어가면 AI들이 빚 갚기를 최우선으로 함 (High Interest Panic)
        bool highInterestPanic = currentRate > 0.08f;

        // 전략: 현금이 빚보다 충분히 많으면 상환
        // 고금리일 때는 기준을 낮춰서(1.1배만 있어도) 빨리 갚아버림
        float safeRatio = highInterestPanic ? 1.1f : 1.5f;

        if (ai.money > ai.currentDebt * safeRatio)
        {
            long repayment = ai.currentDebt;
            ai.money -= repayment;
            ai.currentDebt = 0;
        }
        else if (ai.money > ai.currentDebt && highInterestPanic)
        {
            // 고금리인데 전액 상환은 못하고, 돈은 좀 있으면 절반이라도 갚음
            long repayment = ai.money / 2;
            ai.money -= repayment;
            ai.currentDebt -= repayment;
        }
    }

    #endregion
}