using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.UI;

public class RankingManager : MonoBehaviour
{
    [Header("Settings")]
    public float refreshInterval = 3.0f; 

    [Header("UI References")]
    // 🛠️ [수정] 직접 Prefab을 쓰는 대신 ObjectPool 사용
    public SimpleObjectPool rowPool; 

    // 외부 참조
    private PlayerPortfolio player;
    private AIInvestorManager aiManager;
    private StockMarketManager market; 

    private class InvestorRankData
    {
        public string name;
        public long totalAsset;
        public bool isPlayer; 
    }

    void Start()
    {
        player = FindAnyObjectByType<PlayerPortfolio>();
        aiManager = FindAnyObjectByType<AIInvestorManager>();
        market = FindAnyObjectByType<StockMarketManager>();
        
        // 🛠️ [추가] 풀 컴포넌트 자동 찾기 (없으면 에러 로그)
        if (rowPool == null) rowPool = GetComponent<SimpleObjectPool>();
        if (rowPool == null) Debug.LogError("RankingManager에 SimpleObjectPool 컴포넌트가 필요합니다!");

        StartCoroutine(RankingUpdateLoop());
    }

    IEnumerator RankingUpdateLoop()
    {
        while (true)
        {
            UpdateRanking();
            yield return new WaitForSeconds(refreshInterval);
        }
    }

    void UpdateRanking()
    {
        if (market == null || player == null || aiManager == null) return;

        List<InvestorRankData> allInvestors = new List<InvestorRankData>();

        long playerAsset = player.GetTotalAsset(); 
        
        allInvestors.Add(new InvestorRankData 
        { 
            name = "나 (Player)", 
            totalAsset = playerAsset, 
            isPlayer = true 
        });

        foreach (var ai in aiManager.aiInvestors)
        {
            long aiStockValue = CalculateStockValue(ai.portfolio);
            long aiTotal = ai.money + aiStockValue - ai.currentDebt; 

            allInvestors.Add(new InvestorRankData 
            { 
                name = ai.name, 
                totalAsset = aiTotal, 
                isPlayer = false 
            });
        }

        var sortedList = allInvestors.OrderByDescending(x => x.totalAsset).ToList();
        UpdateScrollUI(sortedList);
    }

    long CalculateStockValue(Dictionary<StockData, long> portfolio)
    {
        long total = 0;
        foreach (var item in portfolio)
        {
            RuntimeStock stock = market.marketStocks.Find(s => s.data == item.Key);
            if (stock != null) total += (long)stock.currentPrice * item.Value;
        }
        return total;
    }

    void UpdateScrollUI(List<InvestorRankData> rankingData)
    {
        // 🛠️ [최적화] 기존 오브젝트를 모두 반환 (Destroy 아님)
        if (rowPool != null) rowPool.ReturnAll();

        for (int i = 0; i < rankingData.Count; i++)
        {
            var data = rankingData[i];
            
            // 🛠️ [최적화] 풀에서 꺼내오기
            GameObject newRow = rowPool.Get();
            
            // ⭐⭐⭐ [핵심 수정] 이 코드를 추가해주세요! ⭐⭐⭐
            // 방금 꺼낸 UI를 목록의 맨 아래로 이동시켜서, 위에서부터 차례대로 쌓이게 합니다.
            newRow.transform.SetAsLastSibling();
            
            InvestorRankRow rowScript = newRow.GetComponent<InvestorRankRow>();
            if (rowScript != null)
            {
                // (순위, 이름, 자산, 플레이어여부, 홀수여부)
                rowScript.SetData(i + 1, data.name, data.totalAsset, data.isPlayer, (i % 2 != 0));
            }
        }

        // 🔄 [추가] 레이아웃 강제 갱신 (ContentSizeFitter 버그 방지)
        // rowPool.parentTransform은 SimpleObjectPool에 연결된 Content 오브젝트입니다.
        if (rowPool.parentTransform != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rowPool.parentTransform as RectTransform);
        }
    }
}