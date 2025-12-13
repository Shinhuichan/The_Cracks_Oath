using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using CustomInspector;

#region Data Structures

public enum InvestmentStyle
{
    Aggressive,    // 공격형 (High Risk High Return)
    Defensive,     // 방어형 (우량주, 국채 선호)
    Balanced,      // 균형형
    TrendFollower, // 추세추종형 (오르는 말에 탐)
    Contrarian,    // 역발상 (떨어지는 칼날 잡기)
    ShortSeller,   // 공매도형 (하락장에 베팅)
    Copycat,       // 따라쟁이 (1등 따라함)
    Rival,         // 👑 라이벌 (플레이어를 위협하는 스마트 AI)
    MarketManipulator, // 😈 작전 세력 (시세 조종 시도)
    HFT,           // ⚡ 초단타 (기계적 매매)
    SectorSpecialist, // 🔬 섹터 전문가
    DividendHunter    // 💰 배당 사냥꾼
}

[System.Serializable]
public struct AIPreference
{
    public StockSector? favoriteSector;
    public bool preferPennyStock;
    public bool preferBlueChip;
    public float riskTolerance; // 0.0 ~ 1.0 (높을수록 위험 감수)
}

[System.Serializable]
public class AIInvestor
{
    [Header("Profile")]
    public string name;
    public InvestmentStyle style;
    public AIPreference preference;
    public long money;
    
    // AI 자산 관리 (플레이어와 동일한 기능 사용 가능)
    public long lockedMargin = 0; // 공매도 증거금
    public long currentDebt = 0;  // 은행 대출
    public long privateDebt = 0;  // 💀 사채
    public int privateDebtDeadline = 0;
    public long bondHoldings = 0; // 📜 국채
    public long hiddenCash = 0;   // 🌑 차명 계좌 (은닉 자산)

    public Dictionary<StockData, long> avgShortPrice = new Dictionary<StockData, long>(); // 공매도 평단가

    [Header("Attributes")]
    public float reactionDelay;
    [HideInInspector] public float nextActTime;
    [HideInInspector] public string lastReactedEventTitle = "";

    [Header("Status")]
    public Dictionary<StockData, long> portfolio = new Dictionary<StockData, long>();
    public Dictionary<StockData, int> avgCost = new Dictionary<StockData, int>(); // 평단가
    public Dictionary<StockData, long> shortPositions = new Dictionary<StockData, long>(); // 공매도 잔고
    
    [HideInInspector] public string lastTradeLog = "시장 관망 중";
    // 😨 공포 지수 (0~100)
    public float panicMeter = 0f;

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
    public long bankruptcyThreshold = 0;

    [Header("AI Roster")]
    public List<AIInvestor> aiInvestors = new List<AIInvestor>();

    private StockMarketManager market;
    
    #endregion

    #region Unity Lifecycle & Init

    void Start()
    {
        market = FindAnyObjectByType<StockMarketManager>();

        if (aiInvestors.Count == 0)
        {
            InitializeExpandedAI();
        }

        StartCoroutine(AITradingLoop());
    }

    void InitializeExpandedAI()
    {
        aiInvestors.Clear();

        // 총 99명 구성
        // 0. 절대자 (Dinosaurs) 1명 (1%) -> 자본금 1000억
        // 1. 자본가 (Whales): 5명 (약 5%) -> 자본금 10억 ~ 50억
        // 2. 중산층 (Middle): 20명 (약 20%) -> 자본금 5,000만 ~ 2억
        // 3. 개미 (Ants): 74명 (약 75%) -> 자본금 300만 ~ 1,000만
        // ==================================================================================
        // -. [절대자 계층] The Dinosaurs (1명)
        // 범위: 1000억 원
        // 특징: 모든 투자자들을 압도하는 자금으로 자본을 움직임.
        // ==================================================================================

        aiInvestors.Add(new AIInvestor("Unknown", InvestmentStyle.MarketManipulator, 100000000000, 0.1f, new AIPreference { riskTolerance = 0.95f }));

        // ==================================================================================
        // 1. [자본가 계층] The Whales (5명)
        // 범위: 10억 ~ 50억 원
        // 특징: 시장을 움직일 정도는 아니지만, 개인 투자자 중엔 정점. 우량주나 배당주 선호.
        // ==================================================================================

        aiInvestors.Add(new AIInvestor("여의도 큰손", InvestmentStyle.Balanced, 5000000000, 3.0f, new AIPreference { preferBlueChip = true })); // 50억 (Max)
        aiInvestors.Add(new AIInvestor("강남 건물주", InvestmentStyle.DividendHunter, 4500000000, 5.0f, new AIPreference { preferBlueChip = true })); // 45억
        aiInvestors.Add(new AIInvestor("슈퍼 개미 김씨", InvestmentStyle.SectorSpecialist, 3000000000, 2.0f, new AIPreference { favoriteSector = StockSector.IT, riskTolerance = 0.8f })); // 30억
        aiInvestors.Add(new AIInvestor("검은 머리 외국인", InvestmentStyle.MarketManipulator, 2000000000, 1.5f, new AIPreference { riskTolerance = 0.9f })); // 20억
        aiInvestors.Add(new AIInvestor("은퇴한 CEO", InvestmentStyle.Defensive, 1000000000, 8.0f, new AIPreference { preferBlueChip = true })); // 10억 (Min)
        aiInvestors.Add(new AIInvestor("명동 사채왕", InvestmentStyle.Aggressive, 4800000000, 2.0f, new AIPreference { riskTolerance = 0.9f }));
        aiInvestors.Add(new AIInvestor("판교 벤처 신화", InvestmentStyle.SectorSpecialist, 4200000000, 3.0f, new AIPreference { favoriteSector = StockSector.IT }));
        aiInvestors.Add(new AIInvestor("골드만 삭수", InvestmentStyle.MarketManipulator, 3500000000, 1.5f, new AIPreference { riskTolerance = 0.8f }));
        aiInvestors.Add(new AIInvestor("엔터 기획사 대표", InvestmentStyle.SectorSpecialist, 2800000000, 4.0f, new AIPreference { favoriteSector = StockSector.Game }));
        aiInvestors.Add(new AIInvestor("익명의 후원자", InvestmentStyle.Defensive, 1500000000, 10.0f, new AIPreference { preferBlueChip = true }));


        // ==================================================================================
        // 2. [중산층 계층] The Middle Class (20명)
        // 범위: 5,000만 ~ 2억 원
        // 특징: 직장인, 전업 투자자 등. 나름의 전략을 가지고 매매함.
        // ==================================================================================

        // [전문가 그룹 - 2억 근접]
        aiInvestors.Add(new AIInvestor("전업 10년차", InvestmentStyle.Aggressive, 200000000, 4.0f, new AIPreference { riskTolerance = 0.7f }));
        aiInvestors.Add(new AIInvestor("차트의 신", InvestmentStyle.TrendFollower, 180000000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("가치투자 전도사", InvestmentStyle.Defensive, 150000000, 10.0f, new AIPreference { preferPennyStock = false }));
        aiInvestors.Add(new AIInvestor("공매도 저격수", InvestmentStyle.ShortSeller, 160000000, 3.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("시스템 트레이더", InvestmentStyle.HFT, 140000000, 0.5f, new AIPreference()));

        // [섹터 전문가 - 1억 ~ 1.5억]
        aiInvestors.Add(new AIInvestor("바이오 연구원", InvestmentStyle.SectorSpecialist, 130000000, 6.0f, new AIPreference { favoriteSector = StockSector.Bio }));
        aiInvestors.Add(new AIInvestor("판교 개발자", InvestmentStyle.SectorSpecialist, 120000000, 5.5f, new AIPreference { favoriteSector = StockSector.IT }));
        aiInvestors.Add(new AIInvestor("자동차 동호회장", InvestmentStyle.SectorSpecialist, 110000000, 7.0f, new AIPreference { favoriteSector = StockSector.Automotive }));
        aiInvestors.Add(new AIInvestor("친환경 운동가", InvestmentStyle.SectorSpecialist, 100000000, 6.5f, new AIPreference { favoriteSector = StockSector.Energy }));
        aiInvestors.Add(new AIInvestor("게임 길드장", InvestmentStyle.SectorSpecialist, 90000000, 5.0f, new AIPreference { favoriteSector = StockSector.Game }));

        // [일반 투자자 - 5천 ~ 9천]
        aiInvestors.Add(new AIInvestor("대기업 김부장", InvestmentStyle.Balanced, 85000000, 8.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("영끌 성공", InvestmentStyle.Aggressive, 80000000, 3.0f, new AIPreference { riskTolerance = 0.8f }));
        aiInvestors.Add(new AIInvestor("복리의 마법", InvestmentStyle.DividendHunter, 75000000, 12.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("뉴스 헌터", InvestmentStyle.TrendFollower, 70000000, 2.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("안전 제일", InvestmentStyle.Defensive, 65000000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("주식 스터디장", InvestmentStyle.Balanced, 60000000, 7.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("퇴직금 투자", InvestmentStyle.Defensive, 55000000, 10.0f, new AIPreference { preferBlueChip = true }));
        aiInvestors.Add(new AIInvestor("손절의 달인", InvestmentStyle.Aggressive, 50000000, 4.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("지옥의 줍줍러", InvestmentStyle.Contrarian, 95000000, 6.0f, new AIPreference { riskTolerance = 1.0f }));
        aiInvestors.Add(new AIInvestor("적금 만기", InvestmentStyle.Balanced, 50000000, 9.0f, new AIPreference()));

        // [전문직/고소득]
        aiInvestors.Add(new AIInvestor("성형외과 원장", InvestmentStyle.SectorSpecialist, 190000000, 5.0f, new AIPreference { favoriteSector = StockSector.Bio }));
        aiInvestors.Add(new AIInvestor("대형 로펌 변호사", InvestmentStyle.Balanced, 180000000, 6.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("스타 강사", InvestmentStyle.Aggressive, 170000000, 4.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("치과의사", InvestmentStyle.Defensive, 160000000, 8.0f, new AIPreference { preferBlueChip = true }));
        aiInvestors.Add(new AIInvestor("웹툰 작가", InvestmentStyle.SectorSpecialist, 150000000, 5.0f, new AIPreference { favoriteSector = StockSector.Game }));
        aiInvestors.Add(new AIInvestor("항공기 기장", InvestmentStyle.TrendFollower, 140000000, 7.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("유명 유튜버", InvestmentStyle.Copycat, 130000000, 3.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("건축가", InvestmentStyle.SectorSpecialist, 120000000, 6.0f, new AIPreference { favoriteSector = StockSector.Energy }));

        // [일반 중산층]
        aiInvestors.Add(new AIInvestor("약국 약사", InvestmentStyle.SectorSpecialist, 110000000, 7.0f, new AIPreference { favoriteSector = StockSector.Bio }));
        aiInvestors.Add(new AIInvestor("대박집 사장님", InvestmentStyle.Balanced, 100000000, 9.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("공기업 차장", InvestmentStyle.Defensive, 95000000, 12.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("수출입 딜러", InvestmentStyle.TrendFollower, 90000000, 4.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("보험왕", InvestmentStyle.DividendHunter, 85000000, 10.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("인테리어 사장", InvestmentStyle.Aggressive, 80000000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("학원 원장님", InvestmentStyle.Balanced, 75000000, 8.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("명예퇴직자", InvestmentStyle.Defensive, 70000000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("골드미스", InvestmentStyle.SectorSpecialist, 65000000, 6.0f, new AIPreference { favoriteSector = StockSector.Food }));
        aiInvestors.Add(new AIInvestor("헬스장 관장", InvestmentStyle.SectorSpecialist, 60000000, 5.0f, new AIPreference { favoriteSector = StockSector.Bio })); // 단백질?
        aiInvestors.Add(new AIInvestor("자동차 딜러", InvestmentStyle.SectorSpecialist, 55000000, 4.0f, new AIPreference { favoriteSector = StockSector.Automotive }));
        aiInvestors.Add(new AIInvestor("전업 주식방송", InvestmentStyle.HFT, 50000000, 1.0f, new AIPreference()));

        // ==================================================================================
        // 3. [개미 계층] The Ants (74명)
        // 범위: 300만 ~ 1,000만 원
        // 특징: 자본이 적어 한 방을 노리거나(급등주, 동전주), 남들 따라다님.
        // ==================================================================================

        // [공격형 - 한방 노림]
        aiInvestors.Add(new AIInvestor("인생 한방", InvestmentStyle.Aggressive, 5000000, 10.0f, new AIPreference { preferPennyStock = true }));
        aiInvestors.Add(new AIInvestor("한강 뷰 가즈아", InvestmentStyle.Aggressive, 8000000, 11.0f, new AIPreference { riskTolerance = 1.0f }));
        aiInvestors.Add(new AIInvestor("마통 뚫음", InvestmentStyle.Aggressive, 9000000, 8.0f, new AIPreference { riskTolerance = 1.0f }));
        aiInvestors.Add(new AIInvestor("동전 수집가", InvestmentStyle.Aggressive, 3000000, 13.0f, new AIPreference { preferPennyStock = true }));
        aiInvestors.Add(new AIInvestor("상따 초보", InvestmentStyle.Aggressive, 4500000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("불나방", InvestmentStyle.Aggressive, 3500000, 4.0f, new AIPreference { riskTolerance = 1.0f }));
        aiInvestors.Add(new AIInvestor("야수의 심장", InvestmentStyle.Aggressive, 6000000, 6.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("몰빵맨", InvestmentStyle.Aggressive, 7000000, 7.0f, new AIPreference { riskTolerance = 1.0f }));
        aiInvestors.Add(new AIInvestor("급등주 탐지기", InvestmentStyle.Aggressive, 5500000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("테마주 헌터", InvestmentStyle.Aggressive, 8500000, 6.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("뇌동매매 장인", InvestmentStyle.Aggressive, 4000000, 2.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("손절은 없다", InvestmentStyle.Aggressive, 9500000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("기도 매매", InvestmentStyle.Aggressive, 5000000, 20.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("가상화폐 난민", InvestmentStyle.Aggressive, 6500000, 8.0f, new AIPreference { favoriteSector = StockSector.IT }));
        aiInvestors.Add(new AIInvestor("대박의 꿈", InvestmentStyle.Aggressive, 3000000, 7.0f, new AIPreference()));

        // [추종형 - 따라쟁이]
        aiInvestors.Add(new AIInvestor("무지성 탑승러", InvestmentStyle.Copycat, 4000000, 14.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("팔랑귀", InvestmentStyle.Copycat, 3000000, 12.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("옆집 아저씨", InvestmentStyle.Copycat, 10000000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("직장인 김씨", InvestmentStyle.Copycat, 9000000, 18.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("주린이 1일차", InvestmentStyle.Copycat, 5000000, 25.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("영차영차", InvestmentStyle.TrendFollower, 6000000, 8.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("추격 매수자", InvestmentStyle.TrendFollower, 5500000, 7.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("뉴스만 믿음", InvestmentStyle.TrendFollower, 8000000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("불타기 장인", InvestmentStyle.TrendFollower, 7500000, 6.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("고점 판독기", InvestmentStyle.Copycat, 3500000, 10.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("커뮤니티 유저", InvestmentStyle.TrendFollower, 4500000, 3.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("리딩방 피해자", InvestmentStyle.Copycat, 3000000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("떡상 기원", InvestmentStyle.TrendFollower, 6500000, 16.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("세력 형님(짭)", InvestmentStyle.Copycat, 4000000, 10.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("정보 매매", InvestmentStyle.TrendFollower, 9500000, 4.0f, new AIPreference()));

        // [소심/방어형]
        aiInvestors.Add(new AIInvestor("청개구리", InvestmentStyle.Contrarian, 5000000, 16.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("떨어지는 칼날", InvestmentStyle.Contrarian, 4000000, 8.0f, new AIPreference { riskTolerance = 0.9f }));
        aiInvestors.Add(new AIInvestor("하따 매니아", InvestmentStyle.Contrarian, 6000000, 7.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("인간 지표", InvestmentStyle.Contrarian, 3500000, 12.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("소문난 똥손", InvestmentStyle.Contrarian, 3000000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("쫄보", InvestmentStyle.Defensive, 3000000, 10.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("구조대 대기중", InvestmentStyle.Defensive, 8000000, 30.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("익절은 항상 옳다", InvestmentStyle.TrendFollower, 5000000, 9.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("비상금", InvestmentStyle.Defensive, 4000000, 19.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("깡통 3번 참", InvestmentStyle.Defensive, 3500000, 25.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("재무제표 분석", InvestmentStyle.Defensive, 9000000, 18.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("월급날 대기중", InvestmentStyle.Defensive, 3000000, 30.0f, new AIPreference()));

        // [소액/학생/알바 - 300~500만 구간 집중]
        aiInvestors.Add(new AIInvestor("편의점 알바", InvestmentStyle.Balanced, 3000000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("박민재(대학생)", InvestmentStyle.Aggressive, 4000000, 12.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("군대간 친구", InvestmentStyle.Defensive, 5000000, 50.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("사회 초년생", InvestmentStyle.Balanced, 8000000, 16.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("용돈 모음", InvestmentStyle.Aggressive, 3000000, 10.0f, new AIPreference { preferPennyStock = true }));
        aiInvestors.Add(new AIInvestor("학자금 대출", InvestmentStyle.Aggressive, 5000000, 11.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("짤짤이", InvestmentStyle.HFT, 3000000, 2.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("치킨값 벌기", InvestmentStyle.Balanced, 3500000, 14.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("PC방 요금", InvestmentStyle.Aggressive, 3000000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("환불금 투자", InvestmentStyle.Aggressive, 3000000, 8.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("수동 매매", InvestmentStyle.Balanced, 4000000, 10.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("실수 매매", InvestmentStyle.Aggressive, 3500000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("매크로 돌림", InvestmentStyle.HFT, 8000000, 1.5f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("AI 봇 7호", InvestmentStyle.HFT, 9000000, 1.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("점쟁이", InvestmentStyle.Aggressive, 5000000, 16.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("꿈에서 봄", InvestmentStyle.Aggressive, 4500000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("이름 예뻐서 삼", InvestmentStyle.Balanced, 3000000, 22.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("CEO 관상 봄", InvestmentStyle.Balanced, 4000000, 20.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("차트만 봄", InvestmentStyle.TrendFollower, 6000000, 12.0f, new AIPreference()));
        
        // [특정 섹터 선호 개미]
        aiInvestors.Add(new AIInvestor("우주 덕후", InvestmentStyle.SectorSpecialist, 7000000, 10.0f, new AIPreference { favoriteSector = StockSector.Automotive }));
        aiInvestors.Add(new AIInvestor("게임 폐인", InvestmentStyle.SectorSpecialist, 5000000, 9.0f, new AIPreference { favoriteSector = StockSector.Game }));
        aiInvestors.Add(new AIInvestor("바이오 광신도", InvestmentStyle.SectorSpecialist, 6000000, 11.0f, new AIPreference { favoriteSector = StockSector.Bio }));
        aiInvestors.Add(new AIInvestor("라면 매니아", InvestmentStyle.SectorSpecialist, 3500000, 13.0f, new AIPreference { favoriteSector = StockSector.Food }));
        aiInvestors.Add(new AIInvestor("로봇 사랑", InvestmentStyle.SectorSpecialist, 8000000, 12.0f, new AIPreference { favoriteSector = StockSector.IT }));
        aiInvestors.Add(new AIInvestor("화석연료 반대", InvestmentStyle.Contrarian, 4500000, 15.0f, new AIPreference { favoriteSector = StockSector.Energy }));
        aiInvestors.Add(new AIInvestor("미식가", InvestmentStyle.SectorSpecialist, 9000000, 8.0f, new AIPreference { favoriteSector = StockSector.Food }));
        aiInvestors.Add(new AIInvestor("배당 연금 생활", InvestmentStyle.DividendHunter, 10000000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("이혜원(주부)", InvestmentStyle.Balanced, 9500000, 18.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("은퇴 준비 김과장", InvestmentStyle.DividendHunter, 10000000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("졸부", InvestmentStyle.Copycat, 10000000, 4.0f, new AIPreference { riskTolerance = 0.8f }));
        aiInvestors.Add(new AIInvestor("복권 3등", InvestmentStyle.Aggressive, 5000000, 3.0f, new AIPreference { riskTolerance = 0.9f }));
        aiInvestors.Add(new AIInvestor("스타트업 인턴", InvestmentStyle.Aggressive, 4000000, 2.5f, new AIPreference { favoriteSector = StockSector.IT }));

        // [학생/청년]
        aiInvestors.Add(new AIInvestor("대학원생", InvestmentStyle.Defensive, 3000000, 20.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("취준생", InvestmentStyle.Aggressive, 4000000, 10.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("공시생", InvestmentStyle.Balanced, 3500000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("편의점 야간", InvestmentStyle.Aggressive, 3000000, 8.0f, new AIPreference { preferPennyStock = true }));
        aiInvestors.Add(new AIInvestor("배달 라이더", InvestmentStyle.TrendFollower, 6000000, 6.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("PC방 알바생", InvestmentStyle.SectorSpecialist, 3200000, 5.0f, new AIPreference { favoriteSector = StockSector.Game }));
        aiInvestors.Add(new AIInvestor("카페 알바생", InvestmentStyle.SectorSpecialist, 3800000, 7.0f, new AIPreference { favoriteSector = StockSector.Food }));
        aiInvestors.Add(new AIInvestor("말년 병장", InvestmentStyle.Aggressive, 3100000, 9.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("복학생", InvestmentStyle.Balanced, 4200000, 12.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("새내기", InvestmentStyle.Copycat, 3000000, 25.0f, new AIPreference())); // 아무것도 모름

        // [직장인/생활형]
        aiInvestors.Add(new AIInvestor("월급 스쳐감", InvestmentStyle.Aggressive, 4500000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("카드값 메꿈", InvestmentStyle.HFT, 3300000, 2.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("비상금 털음", InvestmentStyle.Defensive, 5000000, 18.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("보너스 투자", InvestmentStyle.Aggressive, 6000000, 7.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("결혼 자금", InvestmentStyle.Defensive, 9000000, 20.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("월세 보증금", InvestmentStyle.Defensive, 8000000, 30.0f, new AIPreference())); // 절대 잃으면 안됨
        aiInvestors.Add(new AIInvestor("중고차 판 돈", InvestmentStyle.SectorSpecialist, 7000000, 8.0f, new AIPreference { favoriteSector = StockSector.Automotive }));
        aiInvestors.Add(new AIInvestor("적금 깼음", InvestmentStyle.Aggressive, 5500000, 6.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("효도 자금", InvestmentStyle.Balanced, 4000000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("당근마켓 수익", InvestmentStyle.Aggressive, 3000000, 5.0f, new AIPreference { preferPennyStock = true }));

        // [밈/컨셉 개미]
        aiInvestors.Add(new AIInvestor("흑우", InvestmentStyle.Copycat, 5000000, 10.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("떡락 요정", InvestmentStyle.Contrarian, 4000000, 12.0f, new AIPreference())); // 사면 내림
        aiInvestors.Add(new AIInvestor("떡상 요정", InvestmentStyle.TrendFollower, 6000000, 8.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("손절 못함", InvestmentStyle.Defensive, 8000000, 25.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("물타기 달인", InvestmentStyle.Contrarian, 7000000, 9.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("고점 판독기 2호", InvestmentStyle.Copycat, 3500000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("인간 지표 2호", InvestmentStyle.Contrarian, 3200000, 6.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("팔면 오름", InvestmentStyle.ShortSeller, 5000000, 7.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("사면 내림", InvestmentStyle.Aggressive, 4500000, 7.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("존버 승리 2호", InvestmentStyle.Defensive, 6500000, 30.0f, new AIPreference()));

        // [절박함/목표형]
        aiInvestors.Add(new AIInvestor("컴퓨터 바꿀 돈", InvestmentStyle.SectorSpecialist, 3800000, 6.0f, new AIPreference { favoriteSector = StockSector.IT }));
        aiInvestors.Add(new AIInvestor("여행 자금", InvestmentStyle.Aggressive, 4200000, 8.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("강아지 병원비", InvestmentStyle.Defensive, 3100000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("치과 치료비", InvestmentStyle.Aggressive, 3300000, 7.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("헬스장 환불금", InvestmentStyle.Aggressive, 3000000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("축의금 낼 돈", InvestmentStyle.Balanced, 3500000, 12.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("로또 4등 당첨", InvestmentStyle.Aggressive, 3000000, 4.0f, new AIPreference { preferPennyStock = true }));
        aiInvestors.Add(new AIInvestor("마누라 몰래", InvestmentStyle.HFT, 5000000, 2.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("남편 몰래", InvestmentStyle.Defensive, 6000000, 18.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("비자금", InvestmentStyle.Aggressive, 9000000, 6.0f, new AIPreference()));

        // [SF/세계관 반영 개미]
        aiInvestors.Add(new AIInvestor("로봇 수리비", InvestmentStyle.SectorSpecialist, 4000000, 8.0f, new AIPreference { favoriteSector = StockSector.IT }));
        aiInvestors.Add(new AIInvestor("화성 여행비", InvestmentStyle.SectorSpecialist, 7000000, 10.0f, new AIPreference { favoriteSector = StockSector.Automotive }));
        aiInvestors.Add(new AIInvestor("사이보그 부품값", InvestmentStyle.SectorSpecialist, 5500000, 7.0f, new AIPreference { favoriteSector = StockSector.Bio }));
        aiInvestors.Add(new AIInvestor("VR 게임비", InvestmentStyle.SectorSpecialist, 3200000, 5.0f, new AIPreference { favoriteSector = StockSector.Game }));
        aiInvestors.Add(new AIInvestor("우주선 티켓값", InvestmentStyle.Aggressive, 8000000, 9.0f, new AIPreference { favoriteSector = StockSector.Automotive }));
        aiInvestors.Add(new AIInvestor("산소통 교체비", InvestmentStyle.Defensive, 3500000, 15.0f, new AIPreference { favoriteSector = StockSector.Energy }));
        aiInvestors.Add(new AIInvestor("방사능 치료비", InvestmentStyle.SectorSpecialist, 4500000, 12.0f, new AIPreference { favoriteSector = StockSector.Bio }));
        aiInvestors.Add(new AIInvestor("식량 배급권", InvestmentStyle.SectorSpecialist, 3000000, 10.0f, new AIPreference { favoriteSector = StockSector.Food }));
        aiInvestors.Add(new AIInvestor("데이터 요금", InvestmentStyle.SectorSpecialist, 3100000, 6.0f, new AIPreference { favoriteSector = StockSector.IT }));
        aiInvestors.Add(new AIInvestor("배터리 교체비", InvestmentStyle.SectorSpecialist, 3400000, 8.0f, new AIPreference { favoriteSector = StockSector.Energy }));

        // [추가 무작위 개미들 - 숫자 채우기]
        aiInvestors.Add(new AIInvestor("행복 회로", InvestmentStyle.Aggressive, 4000000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("희망 고문", InvestmentStyle.Defensive, 5000000, 20.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("가즈아아아", InvestmentStyle.Aggressive, 3000000, 4.0f, new AIPreference { riskTolerance = 1.0f }));
        aiInvestors.Add(new AIInvestor("구조 요청", InvestmentStyle.Balanced, 6000000, 15.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("마지막 승부", InvestmentStyle.Aggressive, 7000000, 6.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("이번엔 다르다", InvestmentStyle.TrendFollower, 5500000, 8.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("내가 사면 끝물", InvestmentStyle.Contrarian, 4500000, 10.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("졸업 예정자", InvestmentStyle.Balanced, 3800000, 14.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("휴가비", InvestmentStyle.Aggressive, 3200000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("아이폰 살 돈", InvestmentStyle.SectorSpecialist, 3600000, 7.0f, new AIPreference { favoriteSector = StockSector.IT }));
        aiInvestors.Add(new AIInvestor("전역 컴", InvestmentStyle.SectorSpecialist, 4200000, 6.0f, new AIPreference { favoriteSector = StockSector.Game }));
        aiInvestors.Add(new AIInvestor("알바비 입금", InvestmentStyle.Aggressive, 3000000, 4.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("장학금", InvestmentStyle.Defensive, 5000000, 18.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("공모주 광풍", InvestmentStyle.TrendFollower, 6500000, 5.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("따상 기원", InvestmentStyle.Aggressive, 4800000, 4.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("쩜상 기원", InvestmentStyle.Aggressive, 3900000, 3.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("하한가 줍줍", InvestmentStyle.Contrarian, 5200000, 9.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("상한가 따라잡기", InvestmentStyle.TrendFollower, 4100000, 4.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("뇌동매매 1급", InvestmentStyle.Aggressive, 3000000, 2.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("주식 초보", InvestmentStyle.Copycat, 5000000, 20.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("공부하는 개미", InvestmentStyle.Balanced, 4500000, 12.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("재야의 고수(자칭)", InvestmentStyle.Aggressive, 3500000, 6.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("단타왕(자칭)", InvestmentStyle.HFT, 3000000, 1.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("가치투자(물림)", InvestmentStyle.Defensive, 6000000, 25.0f, new AIPreference()));
        aiInvestors.Add(new AIInvestor("장기투자(물림)", InvestmentStyle.Defensive, 7000000, 30.0f, new AIPreference()));
    }


    #endregion

    #region Core AI Loop

    IEnumerator AITradingLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f); 

            float currentTime = Time.time;
            var publicInfo = market.GetCurrentEventInfo();

            CheckBankruptcy();

            for (int i = 0; i < aiInvestors.Count; i++)
            {
                AIInvestor ai = aiInvestors[i];

                if (ai.panicMeter > 0) ai.panicMeter -= 5f * Time.deltaTime; 
                if (ai.panicMeter < 0) ai.panicMeter = 0;
                
                if (currentTime < ai.nextActTime) continue;

                // 1. 리스크 관리 (사채 상환, 마진콜 방지)
                if (ManageRisk(ai)) 
                {
                    ai.nextActTime = currentTime + 1.0f; 
                    continue;
                }

                // 2. 이벤트 반응 (정보 매매)
                bool isNewEvent = publicInfo.hasEvent && (ai.lastReactedEventTitle != publicInfo.eventTitle);
                if (isNewEvent)
                {
                    if (Random.value < newsReactionProbability)
                    {
                        DecideAndTradeOnNews(ai, publicInfo.eventTitle);
                        ai.lastReactedEventTitle = publicInfo.eventTitle;
                    }
                    ai.nextActTime = currentTime + ai.reactionDelay;
                    continue;
                }

                // 3. 평시 전략 수행
                ProcessNormalDecision(ai);
                ai.nextActTime = currentTime + ai.reactionDelay;
            }
        }
    }

    #endregion

    #region Strategy & Logic

    void ProcessNormalDecision(AIInvestor ai)
    {
        // 1. 자산 재분배 (국채, 차명계좌 관리)
        RebalancePortfolio(ai);

        // 2. 스타일별 매매
        switch (ai.style)
        {
            case InvestmentStyle.Rival: ProcessRivalStrategy(ai); break;
            case InvestmentStyle.HFT: ProcessHFTStrategy(ai); break;
            case InvestmentStyle.DividendHunter: ProcessDividendStrategy(ai); break;
            case InvestmentStyle.Defensive: ProcessDefensiveStrategy(ai); break;
            case InvestmentStyle.Aggressive: ProcessAggressiveStrategy(ai); break;
            case InvestmentStyle.MarketManipulator: ProcessManipulatorStrategy(ai); break;
            default: ProcessBalancedStrategy(ai); break;
        }
    }

    // 🛡️ [리스크 관리] 사채, 마진콜 대응
    bool ManageRisk(AIInvestor ai)
    {
        // A. 사채 기한 임박 (3턴 이하 남았고 돈 부족)
        if (ai.privateDebt > 0 && ai.privateDebtDeadline <= 3)
        {
            long debtAmount = (long)(ai.privateDebt / 1.5f);
            if (ai.money < debtAmount)
            {
                // 차명 계좌에서 돈 빼옴
                if (ai.hiddenCash > 0) TryWithdrawShadow(ai, ai.hiddenCash);
                
                // 국채 매도
                if (ai.bondHoldings > 0) TrySellBond(ai, ai.bondHoldings);

                // 주식 매도
                LiquidateAssets(ai, debtAmount - ai.money);

                if (ai.money >= debtAmount) RepayPrivateDebt(ai);
                return true;
            }
        }

        // B. 마진콜 방지
        long totalShortValue = GetTotalShortValue(ai);
        if (totalShortValue > 0)
        {
            long requiredMaintenance = (long)(totalShortValue * 1.1f);
            if (ai.lockedMargin < requiredMaintenance * 1.2f)
            {
                // 숏 포지션 청산
                foreach (var key in new List<StockData>(ai.shortPositions.Keys))
                {
                    // 🛠️ [수정] 주식을 찾은 뒤 null이 아닐 때만 TryShortCover 호출
                    RuntimeStock targetStock = market.marketStocks.Find(s => s.data == key);
                    if (targetStock != null)
                    {
                        TryShortCover(ai, targetStock, true);
                    }
                }
                return true;
            }
        }

        // C. 공격적 투자자: 돈 없으면 사채 빌려서라도 투자 (파산 직전 아니면)
        if ((ai.style == InvestmentStyle.Aggressive || ai.style == InvestmentStyle.MarketManipulator) 
            && ai.money < 100000 && ai.privateDebt == 0 && GetAITotalAsset(ai) > 0)
        {
            // 리스크 감수도가 높으면 사채 대출
            if (ai.preference.riskTolerance > 0.8f)
            {
                long borrowAmount = GetAITotalAsset(ai) * 2; // 자산의 2배 정도
                TryBorrowPrivateLoan(ai, borrowAmount);
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Expanded Features (Bond, Shadow, Private Loan)

    // 📜 [국채] 포트폴리오 재분배
    void RebalancePortfolio(AIInvestor ai)
    {
        float currentRate = market.baseInterestRate;

        // A. 국채 매수 로직 (금리 높을 때)
        bool shouldBuyBond = false;
        float targetBondRatio = 0f;

        if (currentRate >= 0.05f) // 금리 5% 이상이면 매력적
        {
            if (ai.style == InvestmentStyle.Defensive || ai.style == InvestmentStyle.DividendHunter) targetBondRatio = 0.5f; // 절반까지 담음
            else if (ai.style == InvestmentStyle.Balanced) targetBondRatio = 0.2f;
        }
        else if (ai.panicMeter > 80f) // 시장이 공포에 질리면 안전자산으로 도피
        {
             targetBondRatio = 0.8f; 
        }

        // 현재 국채 비중 계산
        long totalAsset = GetAITotalAsset(ai);
        if (totalAsset <= 0) return;
        float currentBondRatio = (float)ai.bondHoldings / totalAsset;

        if (currentBondRatio < targetBondRatio)
        {
            long amountToBuy = (long)(totalAsset * (targetBondRatio - currentBondRatio));
            TryBuyBond(ai, amountToBuy);
        }
        else if (currentBondRatio > targetBondRatio && ai.style != InvestmentStyle.Defensive)
        {
            // 공격적 투자자는 금리가 낮거나 돈 필요하면 국채 팜
            long amountToSell = (long)(totalAsset * (currentBondRatio - targetBondRatio));
            TrySellBond(ai, amountToSell);
        }

        // B. 차명 계좌 (Shadow Account) 로직
        // 작전 세력, 라이벌은 돈이 너무 많으면(자산 1위 노출 회피) 은닉함
        if ((ai.style == InvestmentStyle.MarketManipulator || ai.style == InvestmentStyle.Rival) && ai.money > 100000000)
        {
            long hideAmount = (long)(ai.money * 0.3f);
            TryDepositShadow(ai, hideAmount);
        }
        // 돈 없을 땐 꺼내 씀
        else if (ai.money < 50000 && ai.hiddenCash > 0)
        {
            TryWithdrawShadow(ai, ai.hiddenCash);
        }
    }

    // --- Action Methods ---

    void TryBuyBond(AIInvestor ai, long amount)
    {
        if (amount <= 0 || ai.money < amount) return;
        ai.money -= amount;
        ai.bondHoldings += amount;
        ai.lastTradeLog = $"국채 매수: {amount:N0}원";
    }

    void TrySellBond(AIInvestor ai, long amount)
    {
        if (amount <= 0 || ai.bondHoldings < amount) return;
        ai.bondHoldings -= amount;
        ai.money += amount;
        ai.lastTradeLog = $"국채 매도: {amount:N0}원";
    }

    void TryDepositShadow(AIInvestor ai, long amount)
    {
        // 실제 한도 체크 (순자산의 20%)
        long realEquity = GetAITotalAsset(ai); // 여기엔 이미 hiddenCash 로직이 반영되어야 함
        long maxLimit = (long)(realEquity * 0.2f);
        
        if (ai.hiddenCash + amount > maxLimit) amount = maxLimit - ai.hiddenCash;
        if (amount <= 0 || ai.money < amount) return;

        ai.money -= amount;
        ai.hiddenCash += amount;
        // 로그는 남기지 않음 (비밀이니까)
    }

    void TryWithdrawShadow(AIInvestor ai, long amount)
    {
        if (amount <= 0 || ai.hiddenCash < amount) return;
        long fee = (long)(amount * 0.1f);
        ai.hiddenCash -= amount;
        ai.money += (amount - fee);
    }

    void TryBorrowPrivateLoan(AIInvestor ai, long amount)
    {
        // 사채는 한 번만 씀 (기존 빚 있으면 안 됨)
        if (ai.privateDebt > 0) return;
        
        // 자산 5배 한도
        long limit = GetAITotalAsset(ai) * 5;
        if (amount > limit) amount = limit;

        ai.money += amount;
        ai.privateDebt += (long)(amount * 1.5f);
        ai.privateDebtDeadline = 10;
        
        Debug.LogWarning($"💀 AI [{ai.name}] 사채 {amount:N0}원 대출 감행! (인생 한방)");
        ai.lastTradeLog = "사채 대출 실행";
    }

    void RepayPrivateDebt(AIInvestor ai)
    {
        long amount = System.Math.Min(ai.money, ai.privateDebt);
        ai.money -= amount;
        ai.privateDebt -= amount;
        if (ai.privateDebt <= 0)
        {
            ai.privateDebt = 0;
            ai.privateDebtDeadline = 0;
        }
    }

    #endregion

    #region Trading Strategies (Existing + Upgraded)

    void ProcessRivalStrategy(AIInvestor ai)
    {
        // 1. 배당 스캘핑
        int turnsToDiv = market.GetTurnsToNextDividend();
        if (turnsToDiv == 1)
        {
            var bestDivStock = market.marketStocks
                .Where(s => s.data.dividendPerShare > 0)
                .OrderByDescending(s => (float)s.data.dividendPerShare / s.currentPrice)
                .FirstOrDefault();
            if (bestDivStock != null) TryBuyStock(ai, bestDivStock, 0.9f);
            return;
        }

        // 2. 급등주 탑승
        var soaringStock = market.marketStocks.OrderByDescending(s => s.GetChangePercent()).FirstOrDefault();
        if (soaringStock != null && soaringStock.GetChangePercent() > 3.0f)
        {
            if (ai.money < soaringStock.currentPrice * 100) PerformAIBankLoan(ai, 0.5f); 
            TryBuyStock(ai, soaringStock, 0.4f);
        }

        // 3. 차익 실현
        foreach (var key in new List<StockData>(ai.portfolio.Keys))
        {
            if (!ai.avgCost.ContainsKey(key)) continue;
            var stock = market.marketStocks.Find(s => s.data == key);
            if (stock != null && stock.currentPrice >= ai.avgCost[key] * 1.15f)
                TrySellStock(ai, stock, true);
        }
    }

    void ProcessHFTStrategy(AIInvestor ai)
    {
        foreach (var key in new List<StockData>(ai.portfolio.Keys))
        {
            var stock = market.marketStocks.Find(s => s.data == key);
            if (stock == null) continue;
            float profitRate = (float)stock.currentPrice / ai.avgCost[key];
            if (profitRate >= 1.02f || profitRate <= 0.98f) TrySellStock(ai, stock, true);
        }
        var volatileStock = market.marketStocks.OrderByDescending(s => s.data.volatility).FirstOrDefault();
        if (volatileStock != null) TryBuyStock(ai, volatileStock, 0.2f);
    }

    void ProcessDividendStrategy(AIInvestor ai)
    {
        var target = market.marketStocks
            .Where(s => s.data.dividendPerShare > 0)
            .OrderByDescending(s => (float)s.data.dividendPerShare / s.currentPrice)
            .FirstOrDefault();
        if (target != null) TryBuyStock(ai, target, 0.3f);
    }

    void ProcessDefensiveStrategy(AIInvestor ai)
    {
        foreach (var key in ai.portfolio.Keys)
        {
            var stock = market.marketStocks.Find(s => s.data == key);
            if (stock != null && stock.currentPrice < ai.avgCost[key] * 0.9f)
            {
                TryBuyStock(ai, stock, 0.1f); // 물타기
                return;
            }
        }
        var blueChip = market.marketStocks.FirstOrDefault(s => s.data.companySize == CompanySize.Large);
        if (blueChip != null) TryBuyStock(ai, blueChip, 0.2f);
    }

    void ProcessAggressiveStrategy(AIInvestor ai)
    {
        var pennyStock = market.marketStocks.FirstOrDefault(s => s.currentPrice < 5000);
        if (pennyStock != null) TryBuyStock(ai, pennyStock, 0.5f);
        
        // 돈이 없는데 기회가 보이면 사채 씀
        if (ai.money < 10000 && ai.privateDebt == 0)
            TryBorrowPrivateLoan(ai, 500000);
    }

    void ProcessManipulatorStrategy(AIInvestor ai)
    {
        // 20% 수익 시 덤핑
        foreach (var key in new List<StockData>(ai.portfolio.Keys))
        {
            var stock = market.marketStocks.Find(s => s.data == key);
            if (stock != null && stock.currentPrice >= ai.avgCost[key] * 1.2f)
            {
                TrySellStock(ai, stock, true);
                return;
            }
        }
        var target = market.marketStocks.Where(s => s.data.companySize == CompanySize.SME).OrderBy(s => s.data.totalShares).FirstOrDefault();
        if (target != null) TryBuyStock(ai, target, 0.4f);
    }

    void ProcessBalancedStrategy(AIInvestor ai)
    {
        TryBuyGeneral(ai);
    }

    #endregion

    #region News Trading (DecideAndTradeOnNews)

    // 📰 [수정] AI가 성향에 맞춰 전략적으로 정보원을 선택하고 매매
    void DecideAndTradeOnNews(AIInvestor ai, string newsTitle)
    {
        // 1. 성향에 따른 정보원 선택 (0~7)
        int infoTier = GetPreferredInfoSource(ai);

        // 2. 정보 획득 (시장 관리자에게 돈 내고 정보 요청)
        // 주의: 돈이 부족하면 GetInfoForAI 내부에서 처리되지 않고 빈 정보를 반환함
        var info = market.GetInfoForAI(ai, infoTier);

        if (!info.hasEvent || info.targets == null || info.targets.Count == 0) return;

        // 3. 정보 해석 및 매매 판단
        foreach (var kvp in info.targets)
        {
            RuntimeStock stock = kvp.Key;
            float multiplier = kvp.Value;
            
            // 정보원이 준 multiplier가 1.0보다 크면 호재, 작으면 악재로 판단
            bool isGoodNews = multiplier > 1.0f;

            // 로비스트(6)나 스파이(4)는 간접 정보를 주므로 해석 방식이 다를 수 있음
            // 하지만 GetInfoForAI에서 AI용으로 해석된 targets를 준다고 가정하고 공통 로직 적용

            if (isGoodNews)
            {
                // 호재 -> 매수 (확신도에 따라 비중 조절)
                float buyRatio = 0.3f;
                if (infoTier == 3 || infoTier == 7) buyRatio = 0.6f; // 내부자, 브로커는 공격적 매수
                else if (infoTier == 5) buyRatio = 0.1f; // 신문팔이는 소액 매수

                TryBuyStock(ai, stock, buyRatio);
            }
            else
            {
                // 악재 -> 보유 중이면 매도, 공매도 성향이면 공매도
                if (ai.portfolio.ContainsKey(stock.data))
                {
                    TrySellStock(ai, stock, true);
                }
                
                // 공매도형, 라이벌, 작전세력은 악재 뉴스에 숏 베팅
                if (ai.style == InvestmentStyle.ShortSeller || 
                    ai.style == InvestmentStyle.Rival || 
                    ai.style == InvestmentStyle.MarketManipulator ||
                    ai.style == InvestmentStyle.HFT)
                {
                    TryShortSell(ai, stock, 0.3f);
                }
            }
        }
        
        // 로그 남기기 (선택 사항)
        // Debug.Log($"🤖 [{ai.name}] 정보원({GetAgentName(infoTier)}) 활용하여 매매 시도.");
    }

    // 🧠 [신규] 성향별 정보원 선택 알고리즘
    int GetPreferredInfoSource(AIInvestor ai)
    {
        float r = Random.value;

        switch (ai.style)
        {
            case InvestmentStyle.Rival: // 👑 라이벌
                // 1등을 견제하는 스파이(4) 혹은 확실한 내부자(3)
                if (r < 0.4f) return 4; // Spy (40%)
                else if (r < 0.7f) return 3; // Insider (30%)
                else return 7; // Broker (30%)

            case InvestmentStyle.Copycat: // 🦜 따라쟁이
                // 1등을 무조건 따라하고 싶어함 -> 스파이(4) 필수
                if (r < 0.8f) return 4; // Spy (80%)
                return 5; // Newsboy (20%) - 남은 돈으로 신문 봄

            case InvestmentStyle.SectorSpecialist: // 🔬 섹터 전문가
                // 섹터 전체 동향을 아는 로비스트(6) 선호
                if (r < 0.6f) return 6; // Lobbyist (60%)
                else if (r < 0.9f) return 1; // Analyst (30%)
                return 5; // Newsboy (10%)

            case InvestmentStyle.MarketManipulator: // 😈 작전 세력
                // 확실하고 큰 정보 -> 브로커(7), 내부자(3)
                if (r < 0.4f) return 7; // Broker (40%)
                else if (r < 0.8f) return 3; // Insider (40%)
                return 6; // Lobbyist (20%) - 판을 읽음

            case InvestmentStyle.Aggressive: // 🔥 공격형
                // 싼값에 대박 노리는 사기꾼(0) 혹은 한방 브로커(7)
                if (r < 0.4f) return 0; // Scammer (40%)
                else if (r < 0.7f) return 7; // Broker (30%)
                return 5; // Newsboy (30%)

            case InvestmentStyle.Defensive: // 🛡️ 방어형
                // 안전한 분석가(1) 혹은 신문팔이(5)로 악재 회피
                if (r < 0.5f) return 1; // Analyst (50%)
                return 5; // Newsboy (50%)

            case InvestmentStyle.HFT: // ⚡ 초단타
                // 데이터 뜯어보는 해커(2)
                if (r < 0.6f) return 2; // Hacker (60%)
                return 1; // Analyst (40%)

            case InvestmentStyle.TrendFollower: // 🌊 추세추종
                // 대중적인 뉴스(5)나 섹터 흐름(6)
                if (r < 0.5f) return 5; // Newsboy (50%)
                return 6; // Lobbyist (50%)

            case InvestmentStyle.DividendHunter: // 💰 배당주
                // 기업 분석이 중요함
                return 1; // Analyst (100%)

            default: // Balanced 등
                // 무난한 분석가(1)나 신문(5)
                if (r < 0.4f) return 1;
                else if (r < 0.8f) return 5;
                return 0; // 심심하면 사기꾼
        }
    }
    
    string GetAgentName(int tier)
    {
        switch (tier)
        {
            case 0: return "사기꾼"; case 1: return "분석가"; case 2: return "해커"; case 3: return "내부자";
            case 4: return "스파이"; case 5: return "신문팔이"; case 6: return "로비스트"; case 7: return "브로커";
            default: return "알수없음";
        }
    }

    #endregion

    #region Utils & Helper Methods

    // 은행 대출
    void PerformAIBankLoan(AIInvestor ai, float ratio)
    {
        long totalAsset = GetAITotalAsset(ai);
        long maxLoan = (long)(totalAsset * 0.5f);
        long borrowable = maxLoan - ai.currentDebt;
        if (borrowable > 0)
        {
            long amt = (long)(borrowable * ratio);
            ai.currentDebt += amt;
            ai.money += amt;
        }
    }

    public long GetAITotalAsset(AIInvestor ai)
    {
        long stockVal = 0;
        foreach (var kvp in ai.portfolio)
        {
            var s = market.marketStocks.Find(st => st.data == kvp.Key);
            if (s != null) stockVal += (long)s.currentPrice * kvp.Value;
        }
        
        long shortDebt = 0;
        foreach (var kvp in ai.shortPositions)
        {
            var s = market.marketStocks.Find(st => st.data == kvp.Key);
            if (s != null) shortDebt += (long)s.currentPrice * kvp.Value;
        }

        // 현금 + 국채 + 차명계좌 + 증거금 + 주식 - 빚
        return ai.money + ai.bondHoldings + ai.hiddenCash + ai.lockedMargin + stockVal - shortDebt - ai.currentDebt - ai.privateDebt;
    }

    long GetTotalShortValue(AIInvestor ai)
    {
        long total = 0;
        foreach (var kvp in ai.shortPositions)
        {
            var s = market.marketStocks.Find(st => st.data == kvp.Key);
            if (s != null) total += (long)s.currentPrice * kvp.Value;
        }
        return total;
    }

    void LiquidateAssets(AIInvestor ai, long amountNeeded)
    {
        // 1. 국채
        if (ai.bondHoldings > 0)
        {
            long sellAmt = System.Math.Min(ai.bondHoldings, amountNeeded);
            TrySellBond(ai, sellAmt);
            amountNeeded -= sellAmt;
        }
        if (amountNeeded <= 0) return;

        // 2. 수익 주식
        foreach (var key in new List<StockData>(ai.portfolio.Keys))
        {
            var stock = market.marketStocks.Find(s => s.data == key);
            if (stock != null) TrySellStock(ai, stock, true);
            if (ai.money >= amountNeeded) return; // TrySellStock에서 money 갱신됨
        }
        
        // 3. 전체 매도
        TrySellGeneral(ai);
    }

    void TryBuyStock(AIInvestor ai, RuntimeStock target, float ratio)
    {
        if (target == null || target.currentPrice <= 0) return;
        long budget = (long)(ai.money * ratio);
        if (budget <= 0) return;

        target.SortOrderBooks();
        if (target.SellOrders.Count == 0) return;

        long price = target.SellOrders[0].price;
        long qty = (long)(budget / price);
        qty = (long)Mathf.Min(qty, target.SellOrders[0].amount);

        if (qty > 0)
        {
            ai.money -= price * qty;
            if (ai.portfolio.ContainsKey(target.data))
            {
                long totalVal = ((long)ai.avgCost[target.data] * ai.portfolio[target.data]) + (price * qty);
                ai.portfolio[target.data] += qty;
                ai.avgCost[target.data] = (int)(totalVal / ai.portfolio[target.data]);
            }
            else
            {
                ai.portfolio.Add(target.data, qty);
                ai.avgCost.Add(target.data, (int)price);
            }
            target.SellOrders[0].amount -= qty;
            if (target.SellOrders[0].amount <= 0) target.SellOrders.RemoveAt(0);
            target.currentPrice = (int)price;
            ai.lastTradeLog = $"매수: {target.data.stockName} {qty}주";
        }
    }

    void TrySellStock(AIInvestor ai, RuntimeStock target, bool sellAll)
    {
        if (!ai.portfolio.ContainsKey(target.data)) return;
        long qty = sellAll ? ai.portfolio[target.data] : ai.portfolio[target.data] / 2;
        target.SortOrderBooks();
        if (target.BuyOrders.Count == 0) return;
        long price = target.BuyOrders[0].price;
        qty = (long)Mathf.Min(qty, target.BuyOrders[0].amount);

        if (qty > 0)
        {
            ai.money += price * qty;
            ai.portfolio[target.data] -= qty;
            if (ai.portfolio[target.data] <= 0) { ai.portfolio.Remove(target.data); ai.avgCost.Remove(target.data); }
            target.BuyOrders[0].amount -= qty;
            if (target.BuyOrders[0].amount <= 0) target.BuyOrders.RemoveAt(0);
            target.currentPrice = (int)price;
            ai.lastTradeLog = $"매도: {target.data.stockName} {qty}주";
        }
    }

    void TryShortSell(AIInvestor ai, RuntimeStock target, float ratio)
    {
        long budget = (long)(ai.money * ratio);
        target.SortOrderBooks();
        if (target.BuyOrders.Count == 0) return;
        long price = target.BuyOrders[0].price;
        long qty = (long)(budget / (price * 0.4f)); 
        qty = (long)Mathf.Min(qty, target.BuyOrders[0].amount);

        if (qty > 0)
        {
            long rawVal = price * qty;
            long requiredMargin = (long)(rawVal * 1.4f);
            long myCost = requiredMargin - rawVal; 

            if (ai.money >= myCost)
            {
                ai.money -= myCost;
                ai.lockedMargin += requiredMargin;
                if (ai.shortPositions.ContainsKey(target.data))
                {
                    long totalVal = (ai.avgShortPrice[target.data] * ai.shortPositions[target.data]) + (price * qty);
                    ai.shortPositions[target.data] += qty;
                    ai.avgShortPrice[target.data] = totalVal / ai.shortPositions[target.data];
                }
                else
                {
                    ai.shortPositions.Add(target.data, qty);
                    ai.avgShortPrice.Add(target.data, price);
                }
                target.BuyOrders[0].amount -= qty;
                if (target.BuyOrders[0].amount <= 0) target.BuyOrders.RemoveAt(0);
                target.currentPrice = (int)price;
                ai.lastTradeLog = $"공매도: {target.data.stockName} {qty}주";
            }
        }
    }

    void TryShortCover(AIInvestor ai, RuntimeStock target, bool coverAll)
    {
        // 🛠️ [수정] target이 null인지 먼저 확인 (이 줄 추가!)
        if (target == null) return;
        if (!ai.shortPositions.ContainsKey(target.data)) return;
        long currentShortQty = ai.shortPositions[target.data]; // long으로 받는 것이 안전
        long qty = coverAll ? currentShortQty : currentShortQty / 2;
        target.SortOrderBooks();
        if (target.SellOrders.Count == 0) return;
        long price = target.SellOrders[0].price;
        qty = (long)Mathf.Min(qty, target.SellOrders[0].amount);

        if (qty > 0)
        {
            long avgPrice = ai.avgShortPrice[target.data];
            long releaseMargin = (long)(avgPrice * qty * 1.4f);
            long costToBuy = price * qty;

            if (ai.money + releaseMargin >= costToBuy)
            {
                ai.lockedMargin -= releaseMargin;
                ai.money += (releaseMargin - costToBuy);
                ai.shortPositions[target.data] -= qty;
                if (ai.shortPositions[target.data] <= 0) { ai.shortPositions.Remove(target.data); ai.avgShortPrice.Remove(target.data); }
                target.SellOrders[0].amount -= qty;
                if (target.SellOrders[0].amount <= 0) target.SellOrders.RemoveAt(0);
                target.currentPrice = (int)price;
                ai.lastTradeLog = $"숏커버: {target.data.stockName} {qty}주";
            }
        }
    }

    void CheckBankruptcy()
    {
        for (int i = aiInvestors.Count - 1; i >= 0; i--)
        {
            if (GetAITotalAsset(aiInvestors[i]) <= bankruptcyThreshold)
            {
                Debug.Log($"💀 {aiInvestors[i].name} 파산!");
                aiInvestors.RemoveAt(i);
            }
        }
    }

    void TryBuyGeneral(AIInvestor ai)
    {
        var target = market.marketStocks[Random.Range(0, market.marketStocks.Count)];
        TryBuyStock(ai, target, 0.2f);
    }
    
    void TrySellGeneral(AIInvestor ai)
    {
        if (ai.portfolio.Count == 0) return;
        var key = ai.portfolio.Keys.ToList()[Random.Range(0, ai.portfolio.Count)];
        var stock = market.marketStocks.Find(s => s.data == key);
        if (stock != null) TrySellStock(ai, stock, false);
    }

    public void OnMarketShock(RuntimeStock targetStock, int newPrice)
    {
        float dropRate = targetStock.GetChangePercent(); 
        if (dropRate < -3.0f)
        {
            foreach (var ai in aiInvestors)
            {
                float panicSens = 10f; 
                if (ai.style == InvestmentStyle.Aggressive) panicSens = 5f; 
                if (ai.style == InvestmentStyle.Defensive || ai.style == InvestmentStyle.Copycat) panicSens = 20f; 

                ai.panicMeter += Mathf.Abs(dropRate) * panicSens;

                if (ai.panicMeter >= 100f && ai.portfolio.ContainsKey(targetStock.data))
                {
                    TrySellStock(ai, targetStock, true); 
                    Debug.Log($"😱 <b>{ai.name}</b>: 패닉 셀! {targetStock.data.stockName} 투매 동참.");
                    ai.panicMeter = 0f;
                }
                
                if (ai.style == InvestmentStyle.Rival || ai.style == InvestmentStyle.ShortSeller)
                {
                    TryShortSell(ai, targetStock, 0.3f); 
                }
            }
        }
    }
    
    // 호환성용 메서드들
    public void DistributeAIDividends() { foreach (var ai in aiInvestors) { long div = 0; foreach(var kvp in ai.portfolio) div += (long)kvp.Key.dividendPerShare * kvp.Value; if (div > 0) ai.money += div; } }
    public void PayAIBondYield(float rate) { foreach (var ai in aiInvestors) if (ai.bondHoldings > 0) ai.money += (long)(ai.bondHoldings * rate); }
    public void ProcessAILoans() 
    { 
        float rate = market.GetCurrentLoanRate(); 
        foreach (var ai in aiInvestors) 
        { 
            if (ai.currentDebt > 0) ai.money -= (long)(ai.currentDebt * rate); 
            if (ai.privateDebt > 0) { ai.privateDebtDeadline--; if (ai.privateDebtDeadline <= 0) ai.money = -999999999; } 
        } 
    }
    #endregion
}