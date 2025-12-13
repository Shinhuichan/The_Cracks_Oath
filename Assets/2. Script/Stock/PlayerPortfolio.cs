using System.Collections.Generic;
using UnityEngine;
using System.Linq; // Sum 사용을 위해

public class PlayerPortfolio : MonoBehaviour
{
    [Header("Player Assets")]
    public long money = 100000; 
    public long currentDebt = 0; // 현재 대출금 (빚)

    // 💀 [신규] 사채 (Private Loan)
    public long privateDebt = 0;        // 갚아야 할 총액 (원금 + 50% 이자)
    public int privateDebtDeadline = 0; // 남은 기한 (턴)

    // 📜 [신규] 국채 보유액 (안전 자산)
    public long bondHoldings = 0;
    // 🔒 [신규] 증거금 계좌 (공매도 담보금)
    public long lockedMargin = 0;

    // 🌑 [신규] 차명 계좌 (숨겨진 자산)
    public long hiddenCash = 0;

    [Header("Margin Settings")]
    public float initialMarginRatio = 1.4f; // 개시 증거금 (140%)
    public float maintenanceMarginRatio = 1.1f; // 유지 증거금 (110%)

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
    private Dictionary<StockData, long> myStocks = new Dictionary<StockData, long>();
    private Dictionary<StockData, long> shortStocks = new Dictionary<StockData, long>();
    // 📉 [신규] 공매도 평단가 (증거금 계산용)
    private Dictionary<StockData, long> avgShortPrice = new Dictionary<StockData, long>();

    // 외부 참조 (주식 가치 계산용)
    private StockMarketManager market;

    void Start()
    {
        market = FindAnyObjectByType<StockMarketManager>();
    }

    // 보유량 확인 (롱)
    public long GetStockCount(StockData data) => myStocks.ContainsKey(data) ? myStocks[data] : 0;

    // 공매도 수량 확인 (숏)
    public long GetShortCount(StockData data) => shortStocks.ContainsKey(data) ? shortStocks[data] : 0;
    // 📉 [신규] 공매도 평단가 가져오기
    public long GetAvgShortPrice(StockData data) => avgShortPrice.ContainsKey(data) ? avgShortPrice[data] : 0;

    // 주식 매수 (Long 진입)
    public void AddStock(StockData data, long amount)
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
    public void RemoveStock(StockData data, long amount)
    {
        if (myStocks.ContainsKey(data))
        {
            myStocks[data] -= amount;
            if (myStocks[data] <= 0) myStocks.Remove(data);
        }
    }

    // 📉 [수정] 공매도 진입 (평단가 계산 포함)
    public void AddShort(StockData data, long amount, long price)
    {
        if (shortStocks.ContainsKey(data))
        {
            // 평단가 갱신 (가중 평균)
            long totalVal = (avgShortPrice[data] * shortStocks[data]) + (price * amount);
            long newTotalAmount = shortStocks[data] + amount;
            avgShortPrice[data] = totalVal / newTotalAmount;
            
            shortStocks[data] += amount;
        }
        else
        {
            shortStocks.Add(data, amount);
            avgShortPrice.Add(data, price);
        }
    }

    // 📉 [수정] 공매도 청산
    public void RemoveShort(StockData data, long amount)
    {
        if (shortStocks.ContainsKey(data))
        {
            shortStocks[data] -= amount;
            if (shortStocks[data] <= 0) 
            {
                shortStocks.Remove(data);
                avgShortPrice.Remove(data);
            }
        }
    }

    // 📜 [신규] 채권 매수
    public void BuyBond(long amount)
    {
        if (money >= amount)
        {
            money -= amount;
            bondHoldings += amount;
        }
    }

    // 📜 [신규] 채권 매도 (현금화)
    public void SellBond(long amount)
    {
        if (bondHoldings >= amount)
        {
            bondHoldings -= amount;
            money += amount;
        }
    }

    // 💰 [수정] 총 자산(순자산) 계산
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
        
        long shortDebtVal = 0;
        foreach(var item in shortStocks)
        {
            if(market != null)
            {
                var stock = market.marketStocks.Find(s => s.data == item.Key);
                if(stock != null) shortDebtVal += (long)stock.currentPrice * item.Value;
            }
        }

        // 자산 = 현금 + 채권 + 증거금 + 주식 - 공매도부채 - 은행대출 - 💀사채
        return money + bondHoldings + lockedMargin + stockVal - shortDebtVal - currentDebt - privateDebt;
    }

    // 💀 [신규] 사채 대출 실행
    public void BorrowPrivateLoan(long amount)
    {
        // 이자 50% 선반영
        long debtWithInterest = (long)(amount * 1.5f);
        
        money += amount;
        privateDebt += debtWithInterest;

        // 이미 빌린 상태가 아니라면 카운트다운 시작 (10턴)
        if (privateDebtDeadline <= 0)
        {
            privateDebtDeadline = 10; 
        }
    }

    // 💀 [신규] 사채 상환
    public bool RepayPrivateLoan(long amount)
    {
        if (privateDebt <= 0) return false;

        long repayAmount = (long)Mathf.Min(amount, privateDebt);
        
        if (money >= repayAmount)
        {
            money -= repayAmount;
            privateDebt -= repayAmount;

            // 전액 상환 시 데드라인 초기화
            if (privateDebt <= 0)
            {
                privateDebt = 0;
                privateDebtDeadline = 0;
            }
            return true;
        }
        return false;
    }

    public long GetMaxLoanAmount()
    {
        long totalAsset = GetTotalAsset() + currentDebt; 
        long limit = (long)(totalAsset * loanLimitRatio);
        return (long)Mathf.Max(0, limit - currentDebt);
    }
    public bool BorrowMoney(long amount)
    {
        if (amount <= GetMaxLoanAmount())
        {
            currentDebt += amount; money += amount;
            Debug.Log($"💰 [대출] {amount:N0}원 대출 실행. (총 부채: {currentDebt:N0}원)");
            return true;
        }
        Debug.LogWarning("대출 한도 초과!"); return false;
    }
    public bool RepayMoney(long amount)
    {
        if (currentDebt <= 0) return false;
        long repayAmount = (long)Mathf.Min(amount, currentDebt);
        if (money >= repayAmount)
        {
            money -= repayAmount; currentDebt -= repayAmount;
            Debug.Log($"💸 [상환] {repayAmount:N0}원 상환 완료. (남은 빚: {currentDebt:N0}원)");
            return true;
        }
        return false;
    }

    // 🌑 [신규] 실제 총 자본 (한도 20% 계산용)
    // 보여지는 자산 + 숨겨진 자산
    public long GetRealTotalEquity()
    {
        return GetTotalAsset() + hiddenCash;
    }
    
    public Dictionary<StockData, long> GetHoldings() => myStocks;
    public Dictionary<StockData, long> GetShortPositions() => shortStocks;
}