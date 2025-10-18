// File: H2HMatchLoop.cs
using UnityEngine;
using System.Collections;
using GameCore;          // Agent, RoundCtx, CardSystem, Mode
using TMPro;

public class H2HMatchLoop : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] CardSystem cardSystem;        // 같은 씬의 CardSystem 참조
    [SerializeField] Mode playMode = Mode.Extend;   // 기본 Heavy

    [Header("Players (Inspector)")]
    [SerializeField] AgentList player1 = AgentList.백무적;
    [SerializeField] AgentList player2 = AgentList.박민재;

    [Header("Scoring")]
    [Min(1)] public int targetPoints = 15;         // 목표 도달 점수
    public int winPts = 3, drawPts = 1, losePts = 0;

    [Header("UI (TMP)")]
    [SerializeField] TMP_Text p1NameText;
    [SerializeField] TMP_Text p1ScoreText;
    [SerializeField] TMP_Text p2NameText;
    [SerializeField] TMP_Text p2ScoreText;
    [SerializeField] TMP_Text statusText;          // 선택 사항

    [Header("Loop Speeds")]
    [SerializeField, Range(0f, 1f)] float roundDelay = 0.05f;
    [SerializeField, Range(0f, 2f)] float matchDelay = 0.25f;

    Coroutine loop;
    int p1Score, p2Score;
    string p1Name, p2Name;
    private void Start()
    {
        StartLoop();
    }
    public void StartLoop()
    {
        StopLoop();

        p1Name = player1.ToString();
        p2Name = player2.ToString();
        p1Score = p2Score = 0;

        UpdateUI();

        // 모드 강제 적용
        if (cardSystem != null)
        {
            cardSystem.currentMode = playMode;
            cardSystem.ApplyModeIfAvailable(); // GameCore.CardSystem API
        }

        loop = StartCoroutine(RunUntilTarget());
    }

    public void StopLoop()
    {
        if (loop != null) { StopCoroutine(loop); loop = null; }
    }

    IEnumerator RunUntilTarget()
    {
        var A1 = AgentFactory.Create(p1Name); // AgentFactory.Create(string) 사용
        var A2 = AgentFactory.Create(p2Name);

        while (p1Score < targetPoints && p2Score < targetPoints)
        {
            yield return RunOneMatch(A1, A2);
            UpdateUI();
            if (p1Score >= targetPoints || p2Score >= targetPoints) break;
            yield return new WaitForSeconds(matchDelay);
        }

        if (statusText)
            statusText.text = (p1Score > p2Score) ? $"{p1Name} 승리"
                               : (p2Score > p1Score) ? $"{p2Name} 승리"
                               : "무승부";
    }

    IEnumerator RunOneMatch(Agent A1, Agent A2)
    {
        // 매치 초기화
        cardSystem.ResetForNewMatch();

        // 라운드 컨텍스트용 최근 히스토리(각 참가자 기준)
        CardType p1_lastSelf = CardType.None, p1_lastOpp = CardType.None, p1_last2Opp = CardType.None, p1_last3Opp = CardType.None;
        CardType p2_lastSelf = CardType.None, p2_lastOpp = CardType.None, p2_last2Opp = CardType.None, p2_last3Opp = CardType.None;

        int R = 1;
        while (!cardSystem.playerILost && !cardSystem.playerIILost && R <= cardSystem.maxRounds)
        {
            // 양측 컨텍스트 작성
            var ctx1 = new RoundCtx {
                round = R,
                selfLife = cardSystem.playerILife,
                oppLife  = cardSystem.playerIILife,
                lastSelf = p1_lastSelf,
                lastOpp  = p1_lastOpp,
                last2Opp = p1_last2Opp,
                last3Opp = p1_last3Opp
            };
            var ctx2 = new RoundCtx {
                round = R,
                selfLife = cardSystem.playerIILife,
                oppLife  = cardSystem.playerILife,
                lastSelf = p2_lastSelf,
                lastOpp  = p2_lastOpp,
                last2Opp = p2_last2Opp,
                last3Opp = p2_last3Opp
            };

            // 라운드 자동 해결(카드 선택 + 적용)
            cardSystem.ResolveRoundAuto(A1, A2, ctx1, ctx2); // 내부에서 BuildUnseen/EV 등 사용

            // 제출 카드로 히스토리 갱신
            var s1 = cardSystem.lastSubmittedP1;
            var s2 = cardSystem.lastSubmittedP2;

            // P1 기준
            p1_last3Opp = p1_last2Opp;
            p1_last2Opp = p1_lastOpp;
            p1_lastOpp  = s2;
            p1_lastSelf = s1;
            // P2 기준
            p2_last3Opp = p2_last2Opp;
            p2_last2Opp = p2_lastOpp;
            p2_lastOpp  = s1;
            p2_lastSelf = s2;

            R++;
            if (roundDelay > 0f) yield return new WaitForSeconds(roundDelay);
        }

        // 승패 판정
        bool p1Dead = cardSystem.playerILost;
        bool p2Dead = cardSystem.playerIILost;

        int p1Life = cardSystem.playerILife;
        int p2Life = cardSystem.playerIILife;

        if ((p1Dead && p2Dead) || (!p1Dead && !p2Dead && p1Life == p2Life))
        {
            p1Score += drawPts;
            p2Score += drawPts;
            if (statusText) statusText.text = "매치 결과: 무승부";
        }
        else if (p2Dead || (!p1Dead && p1Life > p2Life))
        {
            p1Score += winPts;
            p2Score += losePts;
            if (statusText) statusText.text = $"매치 결과: {p1Name} 승";
        }
        else
        {
            p2Score += winPts;
            p1Score += losePts;
            if (statusText) statusText.text = $"매치 결과: {p2Name} 승";
        }
    }

    void UpdateUI()
    {
        if (p1NameText)  p1NameText.text  = p1Name ?? player1.ToString();
        if (p2NameText)  p2NameText.text  = p2Name ?? player2.ToString();
        if (p1ScoreText) p1ScoreText.text = p1Score.ToString();
        if (p2ScoreText) p2ScoreText.text = p2Score.ToString();
    }
}