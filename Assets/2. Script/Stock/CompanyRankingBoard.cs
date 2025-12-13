using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CompanyRankingBoard : MonoBehaviour
{
    [Header("Settings")]
    public float refreshInterval = 1.0f; // 1초마다 갱신

    [Header("UI References")]
    public Transform contentParent; // Scroll View의 Content 오브젝트
    public GameObject rowPrefab;    // 위에서 만든 Row 프리팹

    private StockMarketManager market;
    private List<GameObject> spawnedRows = new List<GameObject>();

    void Start()
    {
        market = FindAnyObjectByType<StockMarketManager>();
        StartCoroutine(UpdateRankingLoop());
    }

    IEnumerator UpdateRankingLoop()
    {
        while (true)
        {
            UpdateCompanyRanking();
            yield return new WaitForSeconds(refreshInterval);
        }
    }

    void UpdateCompanyRanking()
    {
        if (market == null) return;

        // 1. 시가총액(현재가 * 총발행수) 기준으로 정렬
        // 주의: remainShares(잔여량)가 아니라 data.totalShares(총발행량)를 곱해야 기업 가치가 나옵니다.
        var sortedStocks = market.marketStocks
            .OrderByDescending(s => (long)s.currentPrice * s.data.totalShares)
            .ToList();

        // 2. 기존 UI 지우기 (간단한 구현을 위해 재생성 방식 사용)
        // * 최적화를 원하면 Object Pooling을 써야 하지만, 20~30개 정도는 재생성해도 무방함
        foreach (var row in spawnedRows)
        {
            Destroy(row);
        }
        spawnedRows.Clear();

        // 3. 순위대로 생성
        for (int i = 0; i < sortedStocks.Count; i++)
        {
            RuntimeStock stock = sortedStocks[i];
            long marketCap = (long)stock.currentPrice * stock.data.totalShares;

            // 프리팹 생성
            GameObject newRow = Instantiate(rowPrefab, contentParent);
            spawnedRows.Add(newRow);

            // 데이터 입력
            CompanyRankRow rowScript = newRow.GetComponent<CompanyRankRow>();
            if (rowScript != null)
            {
                // (순위, 이름, 심볼, 시가총액, 홀수줄여부)
                rowScript.SetData(i + 1, stock.data.stockName, stock.data.symbol, marketCap, (i % 2 != 0));
            }
        }
    }
}