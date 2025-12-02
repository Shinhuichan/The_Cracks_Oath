using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using GameCore;

public class H2HMatchLoop : MonoBehaviour
{
    [Header("References")]
    public CardSystem cardSystem;
    
    [Header("UI - Gauges (GameObject Scale)")]
    public GameObject p1WinRateBar;      
    public GameObject p2WinRateBar;      
    
    [Header("UI - Labels")]
    public TMP_Text winRateText;    
    public TMP_Text matchCountText; 
    public TMP_Text logText;        

    [Header("UI - P1 Stats")]
    public TMP_Text p1WinText;
    public TMP_Text p1DrawText;
    public TMP_Text p1LoseText;

    [Header("UI - P2 Stats")]
    public TMP_Text p2WinText;
    public TMP_Text p2DrawText;
    public TMP_Text p2LoseText;

    [Header("Settings")]
    public AgentList p1Name = AgentList.김현수;
    public AgentList p2Name = AgentList.이수진;
    public int targetMatches = 100;
    public float delayBetweenMatches = 0.0f;
    public bool autoStart = false;

    // 내부 변수
    private int p1Wins = 0;
    private int p2Wins = 0;
    private int draws = 0;
    private int matchesPlayed = 0;
    private bool isRunning = false;

    private void Start()
    {
        if (autoStart) StartMatchLoop();
    }

    public void StartMatchLoop()
    {
        if (isRunning) return;
        StartCoroutine(RunMatchLoop());
    }

    public void StopLoop()
    {
        isRunning = false;
    }

    IEnumerator RunMatchLoop()
    {
        isRunning = true;
        p1Wins = 0; p2Wins = 0; draws = 0; matchesPlayed = 0;

        UpdateUI();

        if (logText) logText.text = $"Match Start: {p1Name} vs {p2Name}\n";

        while (matchesPlayed < targetMatches && isRunning)
        {
            matchesPlayed++;

            // 에이전트 생성
            Agent p1Agent = AgentFactory.Create(p1Name.ToString());
            Agent p2Agent = AgentFactory.Create(p2Name.ToString());

            // 1. 매치 ID 생성 및 주입 (로그용)
            string matchID = System.Guid.NewGuid().ToString();

            // 2. CardSystem 초기화
            cardSystem.ResetForNewMatch();
            cardSystem.enableChoiceDrawForPlayer = false;
            cardSystem.enableChoiceDrawForAgent = true;

            RoundCtx ctx1 = new RoundCtx { round = 1, selfLife = cardSystem.startLife, oppLife = cardSystem.startLife };
            RoundCtx ctx2 = new RoundCtx { round = 1, selfLife = cardSystem.startLife, oppLife = cardSystem.startLife };

            // 3. 매치 진행 루프
            while (!cardSystem.IsMatchEnded)
            {
                cardSystem.ResolveRoundAuto(p1Agent, p2Agent, ctx1, ctx2);
                UpdateContext(ctx1, cardSystem.playerILife, cardSystem.playerIILife, cardSystem.lastSubmittedP1, cardSystem.lastSubmittedP2);
                UpdateContext(ctx2, cardSystem.playerIILife, cardSystem.playerILife, cardSystem.lastSubmittedP2, cardSystem.lastSubmittedP1);
            }

            // 4. 결과 집계
            bool p1Win = false, p2Win = false;
            string winnerName = "Draw";
            string loserName = "Draw";
            
            // AgentManager에 전달할 승패 결과
            AgentManager.MatchOutcome outcomeP1 = AgentManager.MatchOutcome.Draw;

            if (cardSystem.playerILost && cardSystem.playerIILost) 
            { 
                /* Draw */ 
                outcomeP1 = AgentManager.MatchOutcome.Draw;
            }
            else if (cardSystem.playerILost) 
            { 
                p2Win = true; 
                winnerName = p2Name.ToString(); 
                loserName = p1Name.ToString();
                outcomeP1 = AgentManager.MatchOutcome.Loss;
            }
            else if (cardSystem.playerIILost) 
            { 
                p1Win = true; 
                winnerName = p1Name.ToString(); 
                loserName = p2Name.ToString();
                outcomeP1 = AgentManager.MatchOutcome.Win;
            }
            else if (cardSystem.playerILife == cardSystem.playerIILife) 
            { 
                /* Draw */
                outcomeP1 = AgentManager.MatchOutcome.Draw;
            }
            else if (cardSystem.playerILife > cardSystem.playerIILife) 
            { 
                p1Win = true; 
                winnerName = p1Name.ToString(); 
                loserName = p2Name.ToString();
                outcomeP1 = AgentManager.MatchOutcome.Win;
            }
            else 
            { 
                p2Win = true; 
                winnerName = p2Name.ToString(); 
                loserName = p1Name.ToString();
                outcomeP1 = AgentManager.MatchOutcome.Loss;
            }

            if (p1Win) p1Wins++;
            else if (p2Win) p2Wins++;
            else draws++;

            // 5. [복구됨] AgentManager에 전적 기록 (AgentData 업데이트)
            if (AgentManager.I != null) { AgentManager.I.ApplyMatchResult(p1Name, p2Name, outcomeP1); }

            UpdateUI();

            if (delayBetweenMatches > 0) yield return new WaitForSeconds(delayBetweenMatches);
            else yield return null;
        }

        isRunning = false;
        if (logText) logText.text += $"\n[Finished] Total: {matchesPlayed} | {p1Name}:{p1Wins} | {p2Name}:{p2Wins} | Draw:{draws}";
    }

    void UpdateUI()
    {
        float total = matchesPlayed > 0 ? matchesPlayed : 1;

        // 게이지 바 (GameObject Scale)
        if (p1WinRateBar)
        {
            Vector3 s = p1WinRateBar.transform.localScale;
            s.x = (float)p1Wins / total;
            p1WinRateBar.transform.localScale = s;
        }
        if (p2WinRateBar)
        {
            Vector3 s = p2WinRateBar.transform.localScale;
            s.x = (float)p2Wins / total;
            p2WinRateBar.transform.localScale = s;
        }

        // 텍스트
        if (winRateText)
        {
            float p1Rate = (float)p1Wins / total * 100f;
            float p2Rate = (float)p2Wins / total * 100f;
            winRateText.text = $"{p1Name} {p1Rate:F1}%  vs  {p2Name} {p2Rate:F1}%";
        }

        if (matchCountText) matchCountText.text = $"{matchesPlayed} / {targetMatches}";

        // 상세 스탯
        if (p1WinText)  p1WinText.text  = $"Win: {p1Wins}";
        if (p1DrawText) p1DrawText.text = $"Draw: {draws}";
        if (p1LoseText) p1LoseText.text = $"Lose: {p2Wins}";

        if (p2WinText)  p2WinText.text  = $"Win: {p2Wins}";
        if (p2DrawText) p2DrawText.text = $"Draw: {draws}";
        if (p2LoseText) p2LoseText.text = $"Lose: {p1Wins}";
    }

    // 배치 시뮬레이션 결과 전달용 구조체
    public struct BatchResult
    {
        public int p1Wins;
        public int p2Wins;
        public int draws;
        public int p1Candles;
        public int p2Candles;
        
        // ★ [추가] 신규 어워드용 정밀 데이터
        public int p1SacrificeWins; // P1의 Sacrifice 승리 횟수
        public int p2SacrificeWins;
        public int p1CloseWins;     // P1의 신승(HP <= 2) 횟수
        public int p2CloseWins;
    }

    // [RunBatchSimulation 메서드 수정]
    public IEnumerator RunBatchSimulation(AgentList p1, AgentList p2, int matchCount, Action<BatchResult> onComplete)
    {
        BatchResult result = new BatchResult();
        
        if (cardSystem == null)
        {
            Debug.LogError("H2HMatchLoop: CardSystem이 할당되지 않았습니다.");
            yield break;
        }

        Agent agent1 = AgentFactory.Create(p1.ToString());
        Agent agent2 = AgentFactory.Create(p2.ToString());

        // ★ [핵심 수정] CardSystem에 현재 에이전트 ID를 등록해야 
        // CardSystem 내부에서 AgentManager.LearnFromRound를 호출할 수 있습니다.
        cardSystem.SetCurrentAgents(p1, p2);

        float startTime = Time.realtimeSinceStartup;
        float timeBudget = 0.015f; 

        for (int i = 0; i < matchCount; i++)
        {
            cardSystem.ResetForNewMatch();
            cardSystem.enableChoiceDrawForPlayer = false; 
            cardSystem.enableChoiceDrawForAgent = true;

            RoundCtx ctx1 = new RoundCtx { round = 1, selfLife = cardSystem.startLife, oppLife = cardSystem.startLife };
            RoundCtx ctx2 = new RoundCtx { round = 1, selfLife = cardSystem.startLife, oppLife = cardSystem.startLife };

            int safety = 0;
            while (!cardSystem.IsMatchEnded && safety < 1000)
            {
                safety++;
                cardSystem.ResolveRoundAuto(agent1, agent2, ctx1, ctx2);
                UpdateContext(ref ctx1, cardSystem.playerILife, cardSystem.playerIILife, cardSystem.lastSubmittedP1, cardSystem.lastSubmittedP2, cardSystem.roundCounter);
                UpdateContext(ref ctx2, cardSystem.playerIILife, cardSystem.playerILife, cardSystem.lastSubmittedP2, cardSystem.lastSubmittedP1, cardSystem.roundCounter);
            }

            // ★ [추가] 게임 종료 후 패턴 분석 (학습 데이터 저장)
            if (AgentManager.I != null)
            {
                AgentManager.I.AnalyzeMatchPatterns(p1, p2);
                AgentManager.I.AnalyzeMatchPatterns(p2, p1);
            }

            // 4. 결과 집계
            bool p1Win = false;
            bool p2Win = false;

            if (cardSystem.playerILost && cardSystem.playerIILost) { result.draws++; }
            else if (cardSystem.playerILost) { p2Win = true; result.p2Wins++; } // P2 승
            else if (cardSystem.playerIILost) { p1Win = true; result.p1Wins++; } // P1 승
            else
            {
                if (cardSystem.playerILife > cardSystem.playerIILife) { p1Win = true; result.p1Wins++; }
                else if (cardSystem.playerIILife > cardSystem.playerILife) { p2Win = true; result.p2Wins++; }
                else result.draws++;
            }

            result.p1Candles += cardSystem.playerILife;
            result.p2Candles += cardSystem.playerIILife;

            // ★ [정밀 구현] 어워드 데이터 집계
            if (p1Win)
            {
                // Doomsday Clock: CardSystem의 실제 플래그 확인
                if (cardSystem.IsSacrificeWinP1) result.p1SacrificeWins++;
                
                // The Undying: 승리했으나 체력이 3 이하인 경우
                if (cardSystem.playerILife <= 3) result.p1CloseWins++;
            }
            else if (p2Win)
            {
                if (cardSystem.IsSacrificeWinP2) result.p2SacrificeWins++;
                if (cardSystem.playerIILife <= 2) result.p2CloseWins++;
            }

            if (Time.realtimeSinceStartup - startTime > timeBudget)
            {
                yield return null;
                startTime = Time.realtimeSinceStartup;
            }
        }

        onComplete?.Invoke(result);
    }

    // 내부 컨텍스트 업데이트 헬퍼 (기존 코드 유지 및 리팩토링)
    private void UpdateContext(ref RoundCtx ctx, int myHp, int oppHp, CardType myCard, CardType oppCard, int currentRound)
    {
        ctx.round = currentRound; // CardSystem에서 증가된 라운드를 받음
        ctx.selfLife = myHp;
        ctx.oppLife = oppHp;
        ctx.last3Opp = ctx.last2Opp;
        ctx.last2Opp = ctx.lastOpp;
        ctx.lastOpp = oppCard;
        ctx.lastSelf = myCard;
        if(ctx.oppHistory == null) ctx.oppHistory = new List<CardType>();
        ctx.oppHistory.Add(oppCard);
    }
    private void UpdateContext(RoundCtx ctx, int selfHp, int oppHp, CardType lastSelf, CardType lastOpp)
    {
        ctx.round++;
        ctx.selfLife = selfHp;
        ctx.oppLife = oppHp;
        ctx.last3Opp = ctx.last2Opp;
        ctx.last2Opp = ctx.lastOpp;
        ctx.lastOpp = lastOpp;
        ctx.lastSelf = lastSelf;
    }
}