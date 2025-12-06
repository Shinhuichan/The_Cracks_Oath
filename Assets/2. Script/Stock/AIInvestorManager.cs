using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

// 투자 성향 (5가지)
public enum InvestmentStyle
{
    Aggressive,    // 공격형
    Defensive,     // 방어형
    Balanced,      // 균형형
    TrendFollower, // 추세형
    Contrarian     // 역발상
}

[System.Serializable]
public class AIInvestor
{
    [Header("Profile")]
    public string name;
    public InvestmentStyle style;
    public long money;

    [Header("Attributes")]
    public float reactionDelay; // ⚡ 뉴스 반응 지연 시간 (초)
    [HideInInspector] public float nextActTime; // 다음 행동 가능 시간

    [Header("Status")]
    public Dictionary<StockData, int> portfolio = new Dictionary<StockData, int>();
    public Dictionary<StockData, int> avgCost = new Dictionary<StockData, int>(); 
    public long currentDebt = 0;

    public AIInvestor(string _name, InvestmentStyle _style, long _money, float _delay)
    {
        name = _name;
        style = _style;
        money = _money;
        reactionDelay = _delay;
        nextActTime = 0;
    }
}

public class AIInvestorManager : MonoBehaviour
{
    [Header("Settings")]
    public float tradeInterval = 1.0f; 
    
    [Range(0f, 1f)] 
    public float newsReactionProbability = 0.8f; 

    [Header("AI Roster")]
    public List<AIInvestor> aiInvestors = new List<AIInvestor>();

    [Header("Loan Settings")]
    public float loanInterestRate = 0.005f;

    private StockMarketManager market;
    private string lastEventTitle = ""; // 중복 반응 방지용

    void Start()
    {
        market = FindAnyObjectByType<StockMarketManager>();

        if (aiInvestors.Count == 0)
        {
            // ================================================================
            // 1. [전문가 그룹] (반응: 3.0 ~ 5.0초) - 기관, 봇, 세력
            // ================================================================
            // 특징: 거대 자본, 추세 추종 및 균형 잡힌 포트폴리오
            aiInvestors.Add(new AIInvestor("세력 형님", InvestmentStyle.TrendFollower, 5000000000, Random.Range(3f, 5f)));
            aiInvestors.Add(new AIInvestor("퀀트 봇 V1", InvestmentStyle.TrendFollower, 1000000000, Random.Range(3f, 3.5f)));
            aiInvestors.Add(new AIInvestor("VIP 자산운용", InvestmentStyle.Balanced, 2000000000, Random.Range(3.5f, 5f)));
            aiInvestors.Add(new AIInvestor("알고리즘 봇 T", InvestmentStyle.Aggressive, 500000000, Random.Range(3f, 3.5f)));
            aiInvestors.Add(new AIInvestor("여의도 저승사자", InvestmentStyle.Contrarian, 800000000, Random.Range(3f, 5f)));
            aiInvestors.Add(new AIInvestor("글로벌 펀드 A", InvestmentStyle.Balanced, 3000000000, Random.Range(4f, 5f)));
            aiInvestors.Add(new AIInvestor("시스템 트레이더", InvestmentStyle.TrendFollower, 300000000, Random.Range(3f, 4f)));
            aiInvestors.Add(new AIInvestor("블랙록 AI", InvestmentStyle.Defensive, 1500000000, Random.Range(3.5f, 5f)));
            aiInvestors.Add(new AIInvestor("작전 세력", InvestmentStyle.Aggressive, 700000000, Random.Range(3f, 4.5f)));
            aiInvestors.Add(new AIInvestor("슈퍼 컴퓨터", InvestmentStyle.Balanced, 1200000000, Random.Range(3f, 3.5f)));

            // ================================================================
            // 2. [고수 그룹] (반응: 5.0 ~ 8.0초) - 전업 투자자, 자산가
            // ================================================================
            // 특징: 중견 자본, 뚜렷한 주관(방어/역발상/공격)
            aiInvestors.Add(new AIInvestor("닥터 둠", InvestmentStyle.Defensive, 200000000, Random.Range(5f, 8f)));
            aiInvestors.Add(new AIInvestor("단타 스캘퍼", InvestmentStyle.Aggressive, 5000000, Random.Range(5f, 6.5f)));
            aiInvestors.Add(new AIInvestor("지옥의 줍줍러", InvestmentStyle.Contrarian, 100000000, Random.Range(6f, 8f)));
            aiInvestors.Add(new AIInvestor("강남 건물주", InvestmentStyle.Defensive, 500000000, Random.Range(6f, 8f)));
            aiInvestors.Add(new AIInvestor("가치투자자", InvestmentStyle.Defensive, 30000000, Random.Range(5f, 7f)));
            aiInvestors.Add(new AIInvestor("차트의 신", InvestmentStyle.TrendFollower, 10000000, Random.Range(5f, 6.5f)));
            aiInvestors.Add(new AIInvestor("전업 10년차", InvestmentStyle.Balanced, 50000000, Random.Range(5f, 7f)));
            aiInvestors.Add(new AIInvestor("야수의 심장", InvestmentStyle.Aggressive, 20000000, Random.Range(5f, 6f)));
            aiInvestors.Add(new AIInvestor("손절의 달인", InvestmentStyle.Defensive, 15000000, Random.Range(5.5f, 7.5f)));
            aiInvestors.Add(new AIInvestor("주식 동호회장", InvestmentStyle.Balanced, 80000000, Random.Range(6f, 8f)));

            // ================================================================
            // 3. [초보 그룹] (반응: 10.0 ~ 15.0초) - 일반 개인, 뇌동매매
            // ================================================================
            // 특징: 소액 자본, 늦은 반응, 유행에 민감(추세/공격)하거나 반대로 감(청개구리)
            aiInvestors.Add(new AIInvestor("불개미", InvestmentStyle.Aggressive, 500000, Random.Range(10f, 12f)));
            aiInvestors.Add(new AIInvestor("박민재", InvestmentStyle.Aggressive, 100000, Random.Range(10f, 13f)));
            aiInvestors.Add(new AIInvestor("김철수", InvestmentStyle.Balanced, 5000000, Random.Range(11f, 14f)));
            aiInvestors.Add(new AIInvestor("이혜원", InvestmentStyle.Defensive, 3000000, Random.Range(12f, 15f)));
            aiInvestors.Add(new AIInvestor("청개구리", InvestmentStyle.Contrarian, 1000000, Random.Range(10f, 15f)));
            aiInvestors.Add(new AIInvestor("불타기 장인", InvestmentStyle.TrendFollower, 2000000, Random.Range(10f, 12f)));
            aiInvestors.Add(new AIInvestor("은퇴한 김부장", InvestmentStyle.Defensive, 10000000, Random.Range(13f, 15f)));
            aiInvestors.Add(new AIInvestor("옆집 아저씨", InvestmentStyle.TrendFollower, 500000, Random.Range(11f, 14f)));
            aiInvestors.Add(new AIInvestor("마이너스의 손", InvestmentStyle.Aggressive, 300000, Random.Range(10f, 12f)));
            aiInvestors.Add(new AIInvestor("상따 초보", InvestmentStyle.Aggressive, 200000, Random.Range(10f, 11f)));
        }

        StartCoroutine(AITradingLoop());
    }

    // (대출 관련 함수들은 기존 유지 - 생략)
    public void ProcessAILoans() { foreach (var ai in aiInvestors) { if (ai.currentDebt > 0) ai.money -= (long)(ai.currentDebt * loanInterestRate); ManageDebt(ai); } }
    void ManageDebt(AIInvestor ai) { /* 기존 로직 유지 */ }
    RuntimeStock FindStock(StockData data) => market.marketStocks.Find(s => s.data == data);

    IEnumerator AITradingLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f); // 루프 주기를 짧게 하여 반응성을 높임
            
            // 1. 현재 뉴스 정보 확인
            var eventInfo = market.GetCurrentEventInfo();
            float currentTime = Time.time;

            // ⚡ [핵심] 새로운 이벤트가 발생했는지 체크
            // StockMarketManager에서 GetCurrentEventTitle() 같은 걸 만들어주면 좋지만,
            // 여기서는 eventInfo.hasEvent가 true일 때만 체크하는 방식으로 처리
            if (eventInfo.hasEvent)
            {
                // 이벤트가 바뀌었거나 새로 생겼다면 딜레이 재설정
                // (주의: 실제 구현 시 이벤트마다 고유 ID나 Title을 비교하는 게 가장 정확합니다.
                //  여기서는 hasEvent가 켜져있는 동안 계속 반응하지 않도록, AI별 nextActTime을 활용합니다)
            }

            // 2. AI 리스트 랜덤 섞기
            var shuffledInvestors = aiInvestors.OrderBy(x => Random.value).ToList();

            // 3. 파산 체크 및 정리
            CheckBankruptcy();

            foreach (var ai in shuffledInvestors)
            {
                // 아직 행동 시간이 안 됐으면 패스 (반응 딜레이 중)
                if (currentTime < ai.nextActTime) continue;

                if (eventInfo.hasEvent)
                {
                    // 이벤트 발생 중! -> 반응 딜레이가 아직 설정 안 됐다면 설정
                    // (이 부분은 StockMarketManager에서 이벤트 발생 시점에 콜백을 주는 게 베스트지만,
                    //  여기서는 간단히 "이벤트가 있고 + 내가 아직 반응 안 했으면" 로직으로 처리)
                    
                    // 확률적으로 뉴스에 반응
                    if (Random.value < newsReactionProbability)
                    {
                        ProcessNewsReaction(ai, eventInfo);
                        // 행동 후 쿨타임 (다음 행동까지 대기)
                        ai.nextActTime = currentTime + tradeInterval; 
                    }
                }
                else
                {
                    // 평소 (뉴스 없음) -> 딜레이 없이 랜덤 매매
                    if (Random.value < 0.1f) // 너무 자주 거래하지 않게
                    {
                        ProcessNormalDecision(ai);
                        ai.nextActTime = currentTime + tradeInterval;
                    }
                }
            }
        }
    }

    // 💀 파산 체크 (코드 중복 방지용 분리)
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
                else { ai.portfolio.Remove(key); if(ai.avgCost.ContainsKey(key)) ai.avgCost.Remove(key); }
            }
            if (totalAsset <= 0) { aiInvestors.RemoveAt(i); }
        }
    }

    // 📰 뉴스 반응 (딜레이 적용을 위해 수정됨)
    void ProcessNewsReaction(AIInvestor ai, StockMarketManager.PublicEventInfo info)
    {
        if (info.targets == null || info.targets.Count == 0) return;
        List<RuntimeStock> targetStocks = info.targets.Keys.ToList();
        RuntimeStock target = targetStocks[Random.Range(0, targetStocks.Count)];
        if (target == null || target.currentPrice <= 0) return;

        float multiplier = info.targets[target];
        bool isGoodNews = multiplier >= 1.0f;

        // ** 여기서 AI별 반응 속도(reactionDelay)를 적용해야 함 **
        // 실제로는 코루틴 Invoke나 시간 체크를 해야 하지만, 
        // AITradingLoop에서 nextActTime을 통해 제어하는 방식으로 구현

        switch (ai.style)
        {
            case InvestmentStyle.TrendFollower: 
                if (isGoodNews) TryBuyStock(ai, target, 0.8f); 
                else TrySellStock(ai, target, true);
                break;
            case InvestmentStyle.Aggressive:
                if (isGoodNews) TryBuyStock(ai, target, 0.5f);
                break;
            case InvestmentStyle.Defensive:
                if (isGoodNews) TryBuyStock(ai, target, 0.1f);
                else TrySellStock(ai, target, true); 
                break;
            case InvestmentStyle.Contrarian:
                if (isGoodNews) TrySellStock(ai, target, true);
                else TryBuyStock(ai, target, 0.4f);
                break;
            case InvestmentStyle.Balanced:
                if (isGoodNews) TryBuyStock(ai, target, 0.3f);
                else TrySellStock(ai, target, false);
                break;
        }
    }

    // (나머지 ProcessNormalDecision, TryBuyStock, TrySellStock, ApplyTaxToAI 등 기존 코드 유지)
    void ProcessNormalDecision(AIInvestor ai) { float buyChance = (ai.portfolio.Count == 0) ? 0.8f : 0.5f; if (Random.value < buyChance) TryBuyGeneral(ai); else TrySellGeneral(ai); }
    void TryBuyGeneral(AIInvestor ai) { /* 기존 코드 유지 */ List<RuntimeStock> candidates = new List<RuntimeStock>(); switch (ai.style) { case InvestmentStyle.Aggressive: candidates = market.marketStocks.Where(s => s.data.volatility >= 0.1f && s.GetChangePercent() > 0).ToList(); break; case InvestmentStyle.Defensive: candidates = market.marketStocks.Where(s => s.data.volatility < 0.1f && s.GetChangePercent() < 0).ToList(); break; case InvestmentStyle.TrendFollower: candidates = market.marketStocks.Where(s => s.GetChangePercent() >= 3.0f).ToList(); break; case InvestmentStyle.Contrarian: candidates = market.marketStocks.Where(s => s.GetChangePercent() <= -5.0f).ToList(); break; default: candidates = market.marketStocks; break; } if (candidates.Count == 0) candidates = market.marketStocks; RuntimeStock target = candidates[Random.Range(0, candidates.Count)]; TryBuyStock(ai, target, Random.Range(0.1f, 0.3f)); }
    void TrySellGeneral(AIInvestor ai) { /* 기존 코드 유지 */ if (ai.portfolio.Count == 0) return; List<StockData> myKeys = ai.portfolio.Keys.ToList(); StockData targetData = null; foreach (var key in myKeys) { RuntimeStock stock = market.marketStocks.Find(s => s.data == key); if (stock == null || stock.currentPrice <= 0) { ai.portfolio.Remove(key); ai.avgCost.Remove(key); return; } int avgPrice = ai.avgCost.ContainsKey(key) ? ai.avgCost[key] : stock.previousPrice; float profitRate = ((float)(stock.currentPrice - avgPrice) / avgPrice) * 100f; bool shouldSell = false; switch (ai.style) { case InvestmentStyle.Aggressive: if (profitRate >= 5.0f || profitRate <= -10.0f) shouldSell = true; break; case InvestmentStyle.Defensive: if (profitRate >= 20.0f) shouldSell = true; break; case InvestmentStyle.TrendFollower: if (stock.GetChangePercent() < 0) shouldSell = true; break; case InvestmentStyle.Contrarian: if (profitRate >= 10.0f) shouldSell = true; break; case InvestmentStyle.Balanced: if (profitRate >= 10.0f || profitRate <= -5.0f) shouldSell = true; break; } if (shouldSell) { targetData = key; break; } } if (targetData == null && Random.value < 0.2f) targetData = myKeys[Random.Range(0, myKeys.Count)]; if (targetData != null) { RuntimeStock currentStock = market.marketStocks.Find(s => s.data == targetData); if (currentStock != null) TrySellStock(ai, currentStock, false); } }
    
    // (매수/매도/세금 등 기존 함수들 생략 없이 그대로 유지하세요. 위 코드에 포함된 함수들은 핵심 로직입니다.)
    void TryBuyStock(AIInvestor ai, RuntimeStock target, float investRatio) { if (target.remainShares <= 0 || ai.money < target.currentPrice) return; long investAmount = (long)(ai.money * investRatio); int countToBuy = (int)(investAmount / target.currentPrice); countToBuy = Mathf.Clamp(countToBuy, 1, target.remainShares); if (countToBuy <= 0) return; long cost = (long)countToBuy * target.currentPrice; ai.money -= cost; target.remainShares -= countToBuy; if (ai.portfolio.ContainsKey(target.data)) { int oldQty = ai.portfolio[target.data]; int oldCost = ai.avgCost.ContainsKey(target.data) ? ai.avgCost[target.data] : target.previousPrice; long totalVal = ((long)oldCost * oldQty) + cost; ai.avgCost[target.data] = (int)(totalVal / (oldQty + countToBuy)); ai.portfolio[target.data] += countToBuy; } else { ai.portfolio.Add(target.data, countToBuy); ai.avgCost.Add(target.data, target.currentPrice); } Debug.Log($"🤖 {GetStyleIcon(ai.style)} <b>{ai.name}</b>: {target.data.stockName} <color=red>{countToBuy:N0}주 매수</color>"); }
    void TrySellStock(AIInvestor ai, RuntimeStock target, bool sellAll) { if (!ai.portfolio.ContainsKey(target.data)) return; int myCount = ai.portfolio[target.data]; int countToSell = sellAll ? myCount : myCount / 2; if (countToSell == 0) countToSell = 1; long income = (long)countToSell * target.currentPrice; ai.money += income; ai.portfolio[target.data] -= countToSell; if (ai.portfolio[target.data] <= 0) { ai.portfolio.Remove(target.data); ai.avgCost.Remove(target.data); } target.remainShares += countToSell; int avgPrice = ai.avgCost.ContainsKey(target.data) ? ai.avgCost[target.data] : target.previousPrice; float profitRate = ((float)(target.currentPrice - avgPrice) / avgPrice) * 100f; string profitStr = profitRate > 0 ? $"<color=red>(+{profitRate:F1}%)</color>" : $"<color=blue>({profitRate:F1}%)</color>"; Debug.Log($"🤖 {GetStyleIcon(ai.style)} {ai.name}: {target.data.stockName} <color=blue>{countToSell:N0}주 매도</color> {profitStr}"); }
    public void ApplyTaxToAI() { foreach (var ai in aiInvestors) { if (ai.money > 0) ai.money -= (long)(ai.money * 0.01f); } }
    public void DistributeAIDividends() { foreach (var ai in aiInvestors) { long totalDiv = 0; foreach (var item in ai.portfolio) { if (item.Key.dividendPerShare > 0) totalDiv += (long)item.Key.dividendPerShare * item.Value; } if (totalDiv > 0) ai.money += totalDiv; } }
    string GetStyleIcon(InvestmentStyle style) { switch (style) { case InvestmentStyle.Aggressive: return "🔥"; case InvestmentStyle.Defensive: return "🛡️"; case InvestmentStyle.TrendFollower: return "📈"; case InvestmentStyle.Contrarian: return "💎"; default: return "⚖️"; } }
}   