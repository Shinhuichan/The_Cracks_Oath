// File: H2HMatchLoop.cs
using UnityEngine;
using System.Collections;
using GameCore;          // Agent, RoundCtx, CardSystem, Mode
using TMPro;
using System.Drawing;

public class H2HMatchLoop : MonoBehaviour
{
    public enum EndCondition
    {
        TargetPoints,   // 목표 점수 도달 시 종료
        TargetMatches   // 목표 매칭 수 소화 후 종료
    }

    [Header("Core")]
    [SerializeField] CardSystem cardSystem;        // 같은 씬의 CardSystem 참조
    [SerializeField] Mode playMode = Mode.Extend;

    [Header("Players (Inspector)")]
    [SerializeField] AgentList player1 = AgentList.백무적;
    [SerializeField] AgentList player2 = AgentList.박민재;

    [Header("End Condition")]
    [SerializeField] EndCondition endCondition = EndCondition.TargetPoints;
    [Min(1)] public int targetPoints = 15;         // EndCondition == TargetPoints
    [Min(1)] public int targetMatches = 10;        // EndCondition == TargetMatches

    [Header("Scoring")]
    public int winPts = 3, drawPts = 1, losePts = 0;

    [Header("UI (TMP)")]
    [SerializeField] TMP_Text p1NameText;
    [SerializeField] TMP_Text p2NameText;
    [SerializeField] TMP_Text scoreText;
    [SerializeField] TMP_Text statusText;          // 선택 사항
    [SerializeField] GameObject p1Gauge, p2Gauge;

    [Header("Loop Speeds")]
    [SerializeField, Range(0f, 1f)] float roundDelay = 0.05f;
    [SerializeField, Range(0f, 2f)] float matchDelay = 0.25f;

    [Header("Auto")]
    [SerializeField] bool autoStart = true;

    Coroutine loop;
    int p1Score, p2Score;
    int matchesPlayed;
    string p1Name, p2Name;

    void Awake()
    {
        if (cardSystem == null) cardSystem = FindObjectOfType<CardSystem>();
    }

    void Start()
    {
        if (autoStart) StartLoop();
    }

void OnDisable()  { GameLogger.FlushAll(); }
void OnApplicationQuit() { GameLogger.FlushAll(); }

    public void StartLoop()
    {
        StopLoop();
        if (cardSystem == null) { Debug.LogError("CardSystem not found"); return; }

GameLogger.Init();
        // 완전 자동 모드
        cardSystem.enableChoiceDrawForPlayer = false;
        cardSystem.enableChoiceDrawForAgent  = true;

        // 모드 적용 + 매치 리셋
        cardSystem.currentMode = playMode;
        cardSystem.ApplyModeIfAvailable();
        cardSystem.ResetForNewMatch();

        p1Name = player1.ToString();
        p2Name = player2.ToString();
        p1Score = p2Score = 0;
        matchesPlayed = 0;
        UpdateUI();

        loop = StartCoroutine(StartNextFrame());
    }

    IEnumerator StartNextFrame()
    {
        yield return null; // 한 프레임 양보
        yield return RunLoop();
    }

    public void StopLoop()
    {
        cardSystem.SetCurrentAgents(player1, player2);
        if (loop != null) { StopCoroutine(loop); loop = null; }
    }

    IEnumerator RunLoop()
    {
        var A1 = AgentFactory.Create(player1.ToString());
        var A2 = AgentFactory.Create(player2.ToString());
        if (A1 == null || A2 == null) { Debug.LogError("Agent create failed"); yield break; }

        bool KeepGoing()
        {
            switch (endCondition)
            {
                case EndCondition.TargetPoints:
                    return p1Score < targetPoints && p2Score < targetPoints;
                case EndCondition.TargetMatches:
                    return matchesPlayed < targetMatches;
                default:
                    return false;
            }
        }

        while (KeepGoing())
        {
            yield return RunOneMatch(A1, A2);
            matchesPlayed++;
            UpdateUI();
            if (!KeepGoing()) break;
            if (matchDelay > 0f) yield return new WaitForSeconds(matchDelay);
        }

        if (statusText)
        {
            string tail = endCondition == EndCondition.TargetMatches
                ? $"  ({matchesPlayed}/{targetMatches} 경기)"
                : "";
            statusText.text =
                (p1Score > p2Score) ? $"{p1Name} 최종 승리{tail}"
              : (p2Score > p1Score) ? $"{p2Name} 최종 승리{tail}"
              : $"최종 무승부{tail}";
        }
    }

    IEnumerator RunOneMatch(Agent A1, Agent A2)
    {
        // 매치 초기화
// 매치 식별자(중복 방지: 시각+이름)
string matchId = System.DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "_" + player1 + "_vs_" + player2;

// 시작 로그(시작행은 End에서 완성해도 되지만, 식별자 고정용으로 남김)
GameLogger.LogMatchStart(new GameLogger.MatchStart {
    matchId = matchId,
    p1 = player1.ToString(),
    p2 = player2.ToString(),
    mode = playMode.ToString(),
});
        cardSystem.ResetForNewMatch();
        cardSystem.SetCurrentAgents(player1, player2);

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
            
// --- 라운드 전 상태 캡처 ---
int aLifeBefore = cardSystem.playerILife;
int bLifeBefore = cardSystem.playerIILife;
// (조건치가 노출되지 않으므로 0으로 기록) 
float aCondBefore = 0f, bCondBefore = 0f;

// 라운드 자동 해결
cardSystem.ResolveRoundAuto(A1, A2, ctx1, ctx2);

// --- 제출 카드/라운드 후 상태 ---
var aCard = cardSystem.lastSubmittedP1;  // 이미 존재(히스토리 갱신에 사용)
var bCard = cardSystem.lastSubmittedP2;

int aLifeAfter = cardSystem.playerILife;
int bLifeAfter = cardSystem.playerIILife;

GameLogger.LogRound(new GameLogger.RoundRow {
    matchId = matchId,
    round = R,
    p1Hand = cardSystem.playerIHands,
    p2Hand = cardSystem.playerIIHands,
    p1Card = aCard,
    p2Card = bCard,
    p1LifeAfter = aLifeAfter,
    p2LifeAfter = bLifeAfter,
    p1Delta = aLifeAfter - aLifeBefore,
    p2Delta = bLifeAfter - bLifeBefore,
    disaster = cardSystem.currentDisaster.ToString(),              // CardSystem에서 직접 노출 안 됨
    swappedByStorm = false,     // 동일
});

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

        // 승패 판정 및 점수 부여
        bool p1Dead = cardSystem.playerILost;
        bool p2Dead = cardSystem.playerIILost;
        int p1Life = cardSystem.playerILife;
        int p2Life = cardSystem.playerIILife;

        // ▼ 매치 단위 점수(이 값을 합계에 더하고, ELO 결과 판정에도 사용)
        int mP1 = 0, mP2 = 0;

        if ((p1Dead && p2Dead) || (!p1Dead && !p2Dead && p1Life == p2Life))
        {
            mP1 = drawPts; mP2 = drawPts;
            p1Score += mP1; p2Score += mP2;
            if (statusText) statusText.text = "매치 결과: 무승부";
        }
        else if (p2Dead || (!p1Dead && p1Life > p2Life))
        {
            mP1 = winPts; mP2 = losePts;
            p1Score += mP1; p2Score += mP2;
            if (statusText) statusText.text = $"매치 결과: {p1Name} 승";
        }
        else
        {
            mP1 = losePts; mP2 = winPts;
            p1Score += mP1; p2Score += mP2;
            if (statusText) statusText.text = $"매치 결과: {p2Name} 승";
        }

// 매치 종료 로그
var winner =
    (p1Dead && p2Dead) || (!p1Dead && !p2Dead && p1Life == p2Life) ? "Draw" :
    (p2Dead || (!p1Dead && p1Life > p2Life)) ? "P1" : "P2";

GameLogger.LogMatchEnd(
    new GameLogger.MatchEnd {
        matchId = matchId,
        totalRounds = R - 1,
        winner = winner,
        loser = (winner.Equals("A")) ? "P2" : (winner.Equals("Draw")) ? "Draw" : "P1",
    },
    new GameLogger.MatchStart {
        matchId = matchId,
        p1 = player1.ToString(), p2 = player2.ToString(),
        mode = playMode.ToString()
    }
);
        // ★ 여기 추가: ELO 갱신
        var am = AgentManager.I;
        var outcomeP1 = am.OutcomeFromMatchPoints(mP1, mP2);
        am.ApplyMatchResult(player1, player2, outcomeP1);
    }

    void UpdateUI()
    {
        if (p1NameText) p1NameText.text = p1Name ?? player1.ToString();
        if (p2NameText) p2NameText.text = p2Name ?? player2.ToString();
        if (scoreText)
        {
            int sum = p1Score + p2Score;
            if (sum > 0)
            {
                float p1Pct = (float)p1Score / sum;
                float p2Pct = 1f - p1Pct;
                scoreText.text = $"{p1Score}<color=red>[{p1Pct * 100f:0.#}%]</color> : {p2Score}<color=blue>[{p2Pct * 100f:0.#}%]</color>";
            }
            else { scoreText.text = $"{p1Score}<color=red>[0%]</color> : {p2Score}<color=blue>[0%]</color>"; }
        }

        // 진행률(게이지) 계산
        int goal = (endCondition == EndCondition.TargetPoints)
            ? Mathf.Max(1, targetPoints)
            : Mathf.Max(1, targetMatches * winPts);
        float p1Progress = Mathf.Clamp01((float)p1Score / goal);
        float p2Progress = Mathf.Clamp01((float)p2Score / goal);

        // 진행률(두 게이지 합이 항상 1)
        float a = p1Score;
        float b = p2Score;

        float p1Share, p2Share;
        if (a <= 0f && b <= 0f) {          // 0:0 → 0.5 / 0.5
            p1Share = 0.5f; p2Share = 0.5f;
        } else if (a <= 0f) {              // 0:n → 0 / 1
            p1Share = 0f;   p2Share = 1f;
        } else if (b <= 0f) {              // n:0 → 1 / 0
            p1Share = 1f;   p2Share = 0f;
        } else {                           // 일반 비율
            float sum = a + b;
            p1Share = a / sum;
            p2Share = b / sum;
        }

        // 적용
        if (p1Gauge) p1Gauge.transform.localScale = new Vector3(Mathf.Clamp01(p1Share), 1f, 1f);
        if (p2Gauge) p2Gauge.transform.localScale = new Vector3(Mathf.Clamp01(p2Share), 1f, 1f);

        if (statusText && endCondition == EndCondition.TargetMatches)
            statusText.text = $"경기 진행: {matchesPlayed}/{targetMatches}";
    }
}