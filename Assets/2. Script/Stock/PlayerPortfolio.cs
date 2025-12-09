using System.Collections.Generic;
using UnityEngine;
using System.Linq; // Sum 사용을 위해

public class PlayerPortfolio : MonoBehaviour
{
    [Header("Player Assets")]
    public long money = 100000; 
    public long currentDebt = 0; // 현재 대출금 (빚)

    [Header("Loan Settings")]
    [Range(0f, 1.0f)] public float loanLimitRatio = 0.5f; // 자산의 50%까지 대출 가능

    // ➕ [신규] 플레이어 마지막 행동 기록
    public string lastActionLog = "아직 거래 내역이 없습니다.";

    // [변경] 시장 금리를 받아오는 프로퍼티
    public float loanInterestRate 
    {
        get 
        { 
            if (market != null) return market.GetCurrentLoanRate(); 
            return 0.05f; // 기본값
        }
    }

    // 보유 주식 목록
    private Dictionary<StockData, int> myStocks = new Dictionary<StockData, int>();
    private Dictionary<StockData, int> shortStocks = new Dictionary<StockData, int>();

    // 외부 참조 (주식 가치 계산용)
    private StockMarketManager market;

    void Start()
    {
        market = FindAnyObjectByType<StockMarketManager>();
    }

    // 보유량 확인 (롱)
    public int GetStockCount(StockData data) => myStocks.ContainsKey(data) ? myStocks[data] : 0;

    // 공매도 수량 확인 (숏)
    public int GetShortCount(StockData data) => shortStocks.ContainsKey(data) ? shortStocks[data] : 0;

    // 주식 매수 (Long 진입)
    public void AddStock(StockData data, int amount)
    {
        if (myStocks.ContainsKey(data)) myStocks[data] += amount;
        else myStocks.Add(data, amount);
    }

    // ➕ [신규] 행동 기록 업데이트 함수 (StockMarketManager에서 호출)
    public void SetLastAction(string action)
    {
        lastActionLog = action;
    }

    // 주식 매도 (Long 청산)
    public void RemoveStock(StockData data, int amount)
    {
        if (myStocks.ContainsKey(data))
        {
            myStocks[data] -= amount;
            if (myStocks[data] <= 0) myStocks.Remove(data);
        }
    }

    // 공매도 진입 (주식 빌려 팔기)
    public void AddShort(StockData data, int amount)
    {
        if (shortStocks.ContainsKey(data)) shortStocks[data] += amount;
        else shortStocks.Add(data, amount);
    }

    // 공매도 청산 (주식 갚기)
    public void RemoveShort(StockData data, int amount)
    {
        if (shortStocks.ContainsKey(data))
        {
            shortStocks[data] -= amount;
            if (shortStocks[data] <= 0) shortStocks.Remove(data);
        }
    }

    // ➕ [신규] 총 자산 계산 (현금 + 주식 평가액 - 빚)
    public long GetTotalAsset()
    {
        long stockVal = 0;
        foreach(var item in myStocks)
        {
            if(market != null)
            {
                var stock = market.marketStocks.Find(s => s.data == item.Key);
                if(stock != null) stockVal += (long)stock.currentPrice * item.Value;
            }
        }
        return money + stockVal - currentDebt; // 순자산
    }

    // ➕ [신규] 대출 가능 금액 계산
    public long GetMaxLoanAmount()
    {
        long totalAsset = GetTotalAsset() + currentDebt; // 총 자산(빚 포함)
        long limit = (long)(totalAsset * loanLimitRatio);
        
        // 수정됨: Mathf.Max는 float를 반환하므로 long으로 캐스팅
        return (long)Mathf.Max(0, limit - currentDebt);
    }

    // ➕ [신규] 대출 실행
    public bool BorrowMoney(long amount)
    {
        if (amount <= GetMaxLoanAmount())
        {
            currentDebt += amount;
            money += amount;
            Debug.Log($"💰 [대출] {amount:N0}원 대출 실행. (총 부채: {currentDebt:N0}원)");
            return true;
        }
        Debug.LogWarning("대출 한도 초과!");
        return false;
    }

    // ➕ [신규] 대출 상환
    public bool RepayMoney(long amount)
    {
        if (currentDebt <= 0) return false;
        
        // 수정됨: Mathf.Min은 float를 반환하므로 long으로 캐스팅
        long repayAmount = (long)Mathf.Min(amount, currentDebt); // 빚보다 많이 갚을 순 없음
        
        if (money >= repayAmount)
        {
            money -= repayAmount;
            currentDebt -= repayAmount;
            Debug.Log($"💸 [상환] {repayAmount:N0}원 상환 완료. (남은 빚: {currentDebt:N0}원)");
            return true;
        }
        return false;
    }

    public Dictionary<StockData, int> GetHoldings() => myStocks;
    public Dictionary<StockData, int> GetShortPositions() => shortStocks;
}