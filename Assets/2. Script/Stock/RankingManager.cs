using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class RankingManager : MonoBehaviour
{
    [Header("Settings")]
    public float refreshInterval = 3.0f; // 3초마다 갱신

    [Header("UI References")]
    public Transform contentParent; // Scroll View의 Content 오브젝트
    public GameObject rowPrefab;    // InvestorRankRow 프리팹

    // 외부 참조
    private PlayerPortfolio player;
    private AIInvestorManager aiManager;
    private StockMarketManager market; 

    // 랭킹용 데이터 구조체
    private class InvestorRankData
    {
        public string name;
        public long totalAsset;
        public bool isPlayer; 
    }

    private List<GameObject> spawnedRows = new List<GameObject>();

    void Start()
    {
        player = FindAnyObjectByType<PlayerPortfolio>();
        aiManager = FindAnyObjectByType<AIInvestorManager>();
        market = FindAnyObjectByType<StockMarketManager>();

        StartCoroutine(RankingUpdateLoop());
    }

    IEnumerator RankingUpdateLoop()
    {
        while (true)
        {
            // 시작하자마자 한번 갱신하고 대기
            UpdateRanking();
            yield return new WaitForSeconds(refreshInterval);
        }
    }

    void UpdateRanking()
    {
        if (market == null || player == null || aiManager == null) return;

        List<InvestorRankData> allInvestors = new List<InvestorRankData>();

        // 1. 플레이어 자산 계산 (부채 포함된 순자산 사용)
        long playerAsset = player.GetTotalAsset(); // PlayerPortfolio에 이 함수가 있어야 함
        
        allInvestors.Add(new InvestorRankData 
        { 
            name = "나 (Player)", 
            totalAsset = playerAsset, 
            isPlayer = true 
        });

        // 2. AI 자산 계산
        foreach (var ai in aiManager.aiInvestors)
        {
            long aiStockValue = CalculateStockValue(ai.portfolio);
            long aiTotal = ai.money + aiStockValue - ai.currentDebt; // AI도 부채 차감

            allInvestors.Add(new InvestorRankData 
            { 
                name = ai.name, 
                totalAsset = aiTotal, 
                isPlayer = false 
            });
        }

        // 3. 자산순 정렬 (내림차순)
        var sortedList = allInvestors.OrderByDescending(x => x.totalAsset).ToList();

        // 4. UI 갱신 (스크롤 뷰 방식)
        UpdateScrollUI(sortedList);
    }

    // 주식 평가금 계산 헬퍼
    long CalculateStockValue(Dictionary<StockData, int> portfolio)
    {
        long total = 0;
        foreach (var item in portfolio)
        {
            // 시장에서 현재가 조회
            RuntimeStock stock = market.marketStocks.Find(s => s.data == item.Key);
            // 상장 폐지되었거나 없으면 가치 0
            if (stock != null)
            {
                total += (long)stock.currentPrice * item.Value;
            }
        }
        return total;
    }

    void UpdateScrollUI(List<InvestorRankData> rankingData)
    {
        // 기존 목록 삭제 (풀링을 안 쓰므로 간단히 Destroy)
        foreach (var row in spawnedRows)
        {
            Destroy(row);
        }
        spawnedRows.Clear();

        // 새 목록 생성
        for (int i = 0; i < rankingData.Count; i++)
        {
            var data = rankingData[i];
            GameObject newRow = Instantiate(rowPrefab, contentParent);
            spawnedRows.Add(newRow);

            InvestorRankRow rowScript = newRow.GetComponent<InvestorRankRow>();
            if (rowScript != null)
            {
                // (순위, 이름, 자산, 플레이어여부, 홀수줄여부)
                rowScript.SetData(i + 1, data.name, data.totalAsset, data.isPlayer, (i % 2 != 0));
            }
        }
    }
}