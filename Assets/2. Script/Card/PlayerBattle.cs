using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using GameCore;

// === 참가자 목록 ===
public enum AgentList
{
    김현수 = 1,
    이수진,
    최용호,
    한지혜,
    박민재,
    정다은,
    오태훈,
    유민정,
    김태양,
    이하린,
    백무적
}

[RequireComponent(typeof(CardSystem))]
public class PlayerBattle : MonoBehaviour
{
    [System.Serializable]
    public struct CardVisual
    {
        public string cardName;
        public Sprite sprite;
    }

    // ---------- 설정 ----------
    [Header("설정")]
    public AgentList agentName = AgentList.김현수;
    public int maxRounds = 10;
    [Tooltip("미니 라운드로빈에서 플레이어를 대리할 AI")]
    public AgentList playerProxyForMini = AgentList.김현수;

    [Header("버튼(플레이어가 0/1/2 인덱스를 선택)")]
    public Button btn0, btn1, btn2;

    [Header("UI - 라벨")]
    public TMP_Text roundText, opponentNameText, playerHpText, opponentHpText;

    [Header("UI - 전 라운드 결과")]
    public TMP_Text resultText, playerCurrentHpText, agentCurrentHpText, playerHpAmountText, agentHpAmountText;

    [Header("카드 비주얼 매핑")]
    public CardVisual[] cardVisuals;
    public Sprite fallbackSprite;

    [Header("연출 - 선택 카드 이미지")]
    [SerializeField] private Image playerChosenImage;
    [SerializeField] private Image opponentChosenImage;

    [Header("UI - 재시도/다음 매치")]
    public Button retryButton;     // 리그 전체 리셋
    public Button nextMatchButton; // 다음 상대 매치

    [Header("리그 시뮬레이션(플레이어 경기 종료 후)")]
    public bool simulateOthers = true;
    public TMP_Text othersMatchLog;

    [Header("UI - 현재 순위(개별)")]
    public TMP_Text playerRankText;      // 플레이어 순위/점수
    public TMP_Text opponentRankText;    // 현재 상대 순위/점수

    [Header("UI - 현재 순위(호환용)")]
    public TMP_Text rankText;            // 기존 단일 표기(플레이어와 동일 내용 표시)

    // ---------- 내부 상태 ----------
    private Dictionary<string, Sprite> nameToSprite;
    private CardSystem sys;
    private Agent agent;
    private RoundCtx pc, ac;
    private int round = 1;
    private bool finished = false;

    // 최종 표시 시 라운드별 타전 로그 숨김
    private bool suppressOtherMatchOutput = false;

    // 리그 진행/순위용
    private readonly string PLAYER = "플레이어";
    private HashSet<AgentList> playedOnce = new();     // 플레이어가 최소 1회 상대한 참가자
    private List<MatchRec> leagueMatches = new();      // 전체 매치 기록(플레이어 포함)
    private List<AgentList> roster;                    // AI 11명

    // 플레이어의 11판 상대 순서(시작 시 무작위 확정)
    private List<AgentList> playerOpponentOrder;
    private int playerOppIdx = 0;

    // 12인 라운드로빈 스케줄(플레이어 포함): 11라운드 * 6경기
    private List<(string A, string B)[]> rr12Rounds;         // 전 라운드
    private HashSet<int> rr12Consumed = new HashSet<int>();  // 소모된 라운드 인덱스

    // 미니 라운드로빈 진행 표시
    private bool inMini = false;
    private int miniCount = 0, miniTotal = 0;
    private string roundTextBackup = null;

    // 매치 기록 구조
    private struct MatchRec
    {
        public string A, B;
        public int ptsA, ptsB;
        public MatchRec(string a, string b, int pa, int pb) { A = a; B = b; ptsA = pa; ptsB = pb; }
    }

    // ---------- 생명주기 ----------
    void Awake()
    {
        sys = GetComponent<CardSystem>();
        if (btn0) btn0.onClick.AddListener(() => OnPick(0));
        if (btn1) btn1.onClick.AddListener(() => OnPick(1));
        if (btn2) btn2.onClick.AddListener(() => OnPick(2));
        BuildLookup();

        if (playerChosenImage) { playerChosenImage.preserveAspect = true; playerChosenImage.gameObject.SetActive(false); }
        if (opponentChosenImage){ opponentChosenImage.preserveAspect = true; opponentChosenImage.gameObject.SetActive(false); }

        if (retryButton)
        {
            retryButton.gameObject.SetActive(false);
            retryButton.onClick.AddListener(OnRetry);
        }
        if (nextMatchButton)
        {
            nextMatchButton.gameObject.SetActive(false);
            nextMatchButton.onClick.AddListener(OnNextMatch);
        }

        roster = new List<AgentList>((AgentList[])System.Enum.GetValues(typeof(AgentList)));

        var names12 = new List<string> { PLAYER };
        names12.AddRange(roster.Select(x => x.ToString()));
        rr12Rounds = BuildRoundRobinEven(names12);

        BuildPlayerOpponentOrder();
    }

    void OnValidate()
    {
        BuildLookup();
        if (btn0) EnsureImage(btn0).preserveAspect = true;
        if (btn1) EnsureImage(btn1).preserveAspect = true;
        if (btn2) EnsureImage(btn2).preserveAspect = true;
    }

    IEnumerator Start()
    {
        yield return EnsureFreshSystem();

        if (playerOpponentOrder == null || playerOpponentOrder.Count != roster.Count)
            BuildPlayerOpponentOrder();
        playerOppIdx = 0;
        agentName = playerOpponentOrder[playerOppIdx];

        agent = AgentFactory.Create(agentName.ToString());

        pc = new RoundCtx { round = 1, selfLife = sys.startLife, oppLife = sys.startLife,
                            lastSelf = CardType.None, lastOpp = CardType.None,
                            last2Opp = CardType.None, last3Opp = CardType.None };
        ac = pc;

        if (opponentNameText) opponentNameText.text = agent.name;
        RefreshUI();
        UpdateBothRankUI();
    }

    // ---------- 라운드 입력 ----------
    void OnPick(int playerIndex)
    {
        if (finished) return;
        if (playerIndex < 0 || playerIndex >= sys.playerIHands.Count) return;

        var pCard = sys.playerIHands[playerIndex];
        var unseenA = BuildUnseen(false);
        var aCard = agent.Choose(new DecisionInput(sys.playerIIHands, ac, unseenA));
        int aIndex = IndexOfType(sys.playerIIHands, aCard); if (aIndex < 0) aIndex = 0;

        int hpPBefore = sys.playerILife, hpABefore = sys.playerIILife;
        sys.ResolveRoundByIndex(playerIndex, aIndex);

        pc.last3Opp = pc.last2Opp; ac.last3Opp = ac.last2Opp;
        pc.last2Opp = pc.lastOpp;  ac.last2Opp = ac.lastOpp;
        pc.lastOpp  = aCard;       ac.lastOpp  = pCard;
        pc.lastSelf = pCard;       ac.lastSelf = aCard;

        pc.selfLife = sys.playerILife;  pc.oppLife = sys.playerIILife;
        ac.selfLife = sys.playerIILife; ac.oppLife = sys.playerILife;

        int hpPAfter = sys.playerILife, hpAAfter = sys.playerIILife;
        int dP = hpPAfter - hpPBefore;
        int dA = hpAAfter - hpABefore;

        if (playerCurrentHpText) playerCurrentHpText.text = $"{hpPAfter}";
        if (agentCurrentHpText)  agentCurrentHpText.text  = $"{hpAAfter}";
        if (playerHpAmountText)  playerHpAmountText.text  = FmtDelta(dP);
        if (agentHpAmountText)   agentHpAmountText.text   = FmtDelta(dA);

        RevealChosenCards(GetSprite(pCard), GetSprite(aCard));

        round++; pc.round = round; ac.round = round;

        CheckEnd();
        RefreshUI();
    }

    string FmtDelta(int d) => d == 0 ? "0" : (d > 0 ? $"+{d}" : d.ToString());

    // ---------- 종료 판정 ----------
    void CheckEnd()
    {
        if (finished) return;
        if (round > maxRounds || sys.playerILost || sys.playerIILost)
        {
            finished = true;
            if (btn0) btn0.interactable = false;
            if (btn1) btn1.interactable = false;
            if (btn2) btn2.interactable = false;

            AwardPlayerMatchPoints();
            UpdateBothRankUI();

            bool playerDoneAll = AllOpponentsCleared();

            if (!playerDoneAll)
            {
                string verdict =
                    sys.playerILost && sys.playerIILost ? "무승부(동반 탈락)" :
                    sys.playerILost ? $"{agent.name} 승" :
                    sys.playerIILost ? "플레이어 승" :
                    (sys.playerILife == sys.playerIILife ? "무승부(판정)" :
                     sys.playerILife > sys.playerIILife ? "플레이어 승(판정)" : $"{agent.name} 승(판정)");

                if (resultText)
                    resultText.text = $"결과: {verdict}  |  총 라운드 {round - 1}";
            }

            if (retryButton) { retryButton.gameObject.SetActive(true); retryButton.interactable = true; }
            if (nextMatchButton) { nextMatchButton.gameObject.SetActive(true); nextMatchButton.interactable = true; }

            if (playerDoneAll) suppressOtherMatchOutput = true;

            if (simulateOthers) StartCoroutine(SimulateAIRoundCoroutine());

            if (playerDoneAll && rr12Consumed.Count >= rr12Rounds.Count)
            {
                if (nextMatchButton) { nextMatchButton.interactable = false; nextMatchButton.gameObject.SetActive(false); }
                StartCoroutine(ShowFinalStandingsCoroutine());
            }
        }
    }

    void AwardPlayerMatchPoints()
    {
        int pPts, aPts;
        if ((sys.playerILost && sys.playerIILost) || (sys.playerILife == sys.playerIILife))
        { pPts = 1; aPts = 1; }
        else if (sys.playerIILost || sys.playerILife > sys.playerIILife)
        { pPts = 2; aPts = 0; }
        else
        { pPts = 0; aPts = 2; }

        leagueMatches.Add(new MatchRec(PLAYER, agent.name, pPts, aPts));
        playedOnce.Add(agentName);
    }

    bool AllOpponentsCleared()
    {
        int totalOpponents = roster.Count; // 11
        return playedOnce.Count >= totalOpponents;
    }

    // ---------- 재시도(리그 전체 리셋) ----------
    void OnRetry()
    {
        if (!finished) return;
        StartCoroutine(RestartAllRoutine());
    }

    IEnumerator RestartAllRoutine()
    {
        if (retryButton) retryButton.interactable = false;
        if (nextMatchButton) nextMatchButton.interactable = false;

        playedOnce.Clear();
        leagueMatches.Clear();
        rr12Consumed.Clear();
        suppressOtherMatchOutput = false;

        BuildPlayerOpponentOrder();
        playerOppIdx = 0;

        HideChosenCards();
        if (playerCurrentHpText) playerCurrentHpText.text = string.Empty;
        if (agentCurrentHpText)  agentCurrentHpText.text  = string.Empty;
        if (playerHpAmountText)  playerHpAmountText.text  = string.Empty;
        if (agentHpAmountText)   agentHpAmountText.text   = string.Empty;
        if (resultText) resultText.text = string.Empty;
        if (othersMatchLog) othersMatchLog.text = string.Empty;
        if (playerRankText) playerRankText.text = "-";
        if (opponentRankText) opponentRankText.text = "-";
        if (rankText) rankText.text = "-";

        finished = false;
        round = 1;

        agentName = playerOpponentOrder[playerOppIdx];

        yield return EnsureFreshSystem();

        agent = AgentFactory.Create(agentName.ToString());

        pc = new RoundCtx { round = 1, selfLife = sys.startLife, oppLife = sys.startLife,
                            lastSelf = CardType.None, lastOpp = CardType.None,
                            last2Opp = CardType.None, last3Opp = CardType.None };
        ac = pc;

        if (opponentNameText) opponentNameText.text = agent.name;

        if (btn0) btn0.interactable = true;
        if (btn1) btn1.interactable = true;
        if (btn2) btn2.interactable = true;

        if (retryButton) { retryButton.gameObject.SetActive(false); retryButton.interactable = true; }
        if (nextMatchButton) { nextMatchButton.gameObject.SetActive(false); nextMatchButton.interactable = true; }

        RefreshUI();
        UpdateBothRankUI();
    }

    // ---------- 다음 매치 ----------
    void OnNextMatch()
    {
        if (!finished) return;

        playerOppIdx++;
        if (playerOppIdx >= roster.Count)
        {
            if (othersMatchLog)
                othersMatchLog.text += (othersMatchLog.text.Length > 0 ? "\n" : "") + "[알림] 모든 참가자와의 대전을 완료했습니다.";
            return;
        }

        agentName = playerOpponentOrder[playerOppIdx];
        StartCoroutine(RestartRoutine());
    }

    IEnumerator RestartRoutine()
    {
        if (retryButton) retryButton.interactable = false;
        if (nextMatchButton) nextMatchButton.interactable = false;

        finished = false;
        round = 1;
        suppressOtherMatchOutput = false;

        HideChosenCards();

        if (playerCurrentHpText) playerCurrentHpText.text = string.Empty;
        if (agentCurrentHpText)  agentCurrentHpText.text  = string.Empty;
        if (playerHpAmountText)  playerHpAmountText.text  = string.Empty;
        if (agentHpAmountText)   agentHpAmountText.text   = string.Empty;
        if (resultText) resultText.text = string.Empty;
        if (othersMatchLog) othersMatchLog.text = string.Empty;

        yield return EnsureFreshSystem();

        agent = AgentFactory.Create(agentName.ToString());

        pc = new RoundCtx { round = 1, selfLife = sys.startLife, oppLife = sys.startLife,
                            lastSelf = CardType.None, lastOpp = CardType.None,
                            last2Opp = CardType.None, last3Opp = CardType.None };
        ac = pc;

        if (opponentNameText) opponentNameText.text = agent.name;

        if (btn0) btn0.interactable = true;
        if (btn1) btn1.interactable = true;
        if (btn2) btn2.interactable = true;

        if (retryButton) { retryButton.gameObject.SetActive(false); retryButton.interactable = true; }
        if (nextMatchButton) { nextMatchButton.gameObject.SetActive(false); nextMatchButton.interactable = true; }

        RefreshUI();
        UpdateBothRankUI();
    }

    // ---------- UI ----------
    void RefreshUI()
    {
        if (inMini)
        {
            if (roundTextBackup == null && roundText) roundTextBackup = roundText.text;
            if (roundText) roundText.text = $"Extra ({miniCount}/{miniTotal})";
        }
        else
        {
            if (roundText)
            {
                roundText.text = $"Round {Mathf.Min(round, maxRounds)} / {maxRounds}";
            }
            roundTextBackup = null;
        }
        UpdateHpTexts();
        UpdateButtonsAndSprites();
    }

    void UpdateHpTexts()
    {
        if (playerHpText)   playerHpText.text   = $"{sys.playerILife}";
        if (opponentHpText) opponentHpText.text = $"{sys.playerIILife}";
    }

    void UpdateButtonsAndSprites()
    {
        SetButton(btn0, 0);
        SetButton(btn1, 1);
        SetButton(btn2, 2);
    }

    void SetButton(Button b, int idx)
    {
        if (!b) return;
        bool ok = idx < sys.playerIHands.Count && !finished && !inMini;
        b.interactable = ok;

        var img = EnsureImage(b);
        img.preserveAspect = true;

        Sprite s = fallbackSprite;
        if (idx < sys.playerIHands.Count)
        {
            string key = sys.playerIHands[idx].ToString();
            if (nameToSprite != null && nameToSprite.TryGetValue(Norm(key), out var sp)) s = sp;
        }
        img.sprite = s;
        img.enabled = s != null;
    }

    // ---------- 선택 카드 연출 ----------
    public void RevealChosenCards(Sprite my, Sprite opp)
    {
        if (playerChosenImage)
        {
            playerChosenImage.sprite = my;
            playerChosenImage.enabled = my != null;
            playerChosenImage.gameObject.SetActive(true);
        }
        if (opponentChosenImage)
        {
            opponentChosenImage.sprite = opp;
            opponentChosenImage.enabled = opp != null;
            opponentChosenImage.gameObject.SetActive(true);
        }
    }
    public void HideChosenCards()
    {
        if (playerChosenImage)   playerChosenImage.gameObject.SetActive(false);
        if (opponentChosenImage) opponentChosenImage.gameObject.SetActive(false);
    }

    // ---------- AI 라운드 1회 진행 ----------
    IEnumerator SimulateAIRoundCoroutine()
    {
        if (rr12Consumed.Count >= rr12Rounds.Count) yield break;

        string oppName = agent.name;
        int idx = FindRoundIndexWithPair(PLAYER, oppName);
        if (idx < 0) yield break;

        foreach (var (A, B) in rr12Rounds[idx])
        {
            if (A == PLAYER || B == PLAYER) continue;

            if (System.Enum.TryParse<AgentList>(A, out var a1) &&
                System.Enum.TryParse<AgentList>(B, out var a2))
            {
                yield return SimulateAIVsAI(a1, a2);
            }
        }

        rr12Consumed.Add(idx);
        UpdateBothRankUI();

        if (AllOpponentsCleared() && rr12Consumed.Count >= rr12Rounds.Count)
        {
            if (nextMatchButton) { nextMatchButton.interactable = false; nextMatchButton.gameObject.SetActive(false); }
            StartCoroutine(ShowFinalStandingsCoroutine());
        }
    }

    int FindRoundIndexWithPair(string n1, string n2)
    {
        for (int i = 0; i < rr12Rounds.Count; i++)
        {
            if (rr12Consumed.Contains(i)) continue;
            var pairs = rr12Rounds[i];
            for (int k = 0; k < pairs.Length; k++)
            {
                var p = pairs[k];
                if ((p.A == n1 && p.B == n2) || (p.A == n2 && p.B == n1))
                    return i;
            }
        }
        return -1;
    }

    void BuildPlayerOpponentOrder()
    {
        playerOpponentOrder = new List<AgentList>(roster);
        for (int i = playerOpponentOrder.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (playerOpponentOrder[i], playerOpponentOrder[j]) = (playerOpponentOrder[j], playerOpponentOrder[i]);
        }
    }

    IEnumerator SimulateAIVsAI(AgentList A1, AgentList A2)
    {
        var go = new GameObject($"Sim_{A1}_vs_{A2}");
        var sim = go.AddComponent<CardSystem>();
        yield return null;
        yield return new WaitUntil(() => sim.publicDeck != null && sim.publicDeck.Count > 0);

        var ag1 = AgentFactory.Create(A1.ToString());
        var ag2 = AgentFactory.Create(A2.ToString());

        var c1 = new RoundCtx { round = 1, selfLife = sim.startLife, oppLife = sim.startLife };
        var c2 = c1;

        int r = 1;
        while (r <= maxRounds && !sim.playerILost && !sim.playerIILost)
        {
            var unseen1 = sim.BuildUnseen(true);
            var unseen2 = sim.BuildUnseen(false);

            var t1 = ag1.Choose(new DecisionInput(sim.playerIHands,  c1, unseen1));
            var t2 = ag2.Choose(new DecisionInput(sim.playerIIHands, c2, unseen2));

            int i1 = IndexOfType(sim.playerIHands,  t1); if (i1 < 0) i1 = 0;
            int i2 = IndexOfType(sim.playerIIHands, t2); if (i2 < 0) i2 = 0;

            sim.ResolveRoundByIndex(i1, i2);

            c1.last3Opp = c1.last2Opp; c2.last3Opp = c2.last2Opp;
            c1.last2Opp = c1.lastOpp;  c2.last2Opp = c2.lastOpp;
            c1.lastOpp = t2;           c2.lastOpp = t1;
            c1.lastSelf = t1;          c2.lastSelf = t2;

            c1.selfLife = sim.playerILife;  c1.oppLife = sim.playerIILife;
            c2.selfLife = sim.playerIILife; c2.oppLife = sim.playerILife;

            r++; c1.round = r; c2.round = r;
        }

        int pa, pb;
        if ((sim.playerILost && sim.playerIILost) || (sim.playerILife == sim.playerIILife)) { pa = 1; pb = 1; }
        else if (sim.playerIILost || sim.playerILife > sim.playerIILife) { pa = 2; pb = 0; }
        else { pa = 0; pb = 2; }
        leagueMatches.Add(new MatchRec(A1.ToString(), A2.ToString(), pa, pb));

        if (!suppressOtherMatchOutput)
        {
            string result =
                sim.playerILost && sim.playerIILost ? "무승부" :
                sim.playerILost ? $"{A2} 승" :
                sim.playerIILost ? $"{A1} 승" :
                (sim.playerILife == sim.playerIILife ? "무승부(판정)" :
                 sim.playerILife > sim.playerIILife ? $"{A1} 승(판정)" : $"{A2} 승(판정)");
            LogLine($"[{A1} vs {A2}] → {result} | HP {sim.playerILife}:{sim.playerIILife}");
        }

        Destroy(go);
    }

    // === 미니매치(최종 동률 그룹 전용, 플레이어 포함) ===
    IEnumerator SimulateMiniPair(string A, string B, System.Action<int,int> onDone)
    {
        var go = new GameObject($"Mini_{A}_vs_{B}");
        var sim = go.AddComponent<CardSystem>();
        yield return null;
        yield return new WaitUntil(() => sim.publicDeck != null && sim.publicDeck.Count > 0);

        Agent ag1 = (A == PLAYER) ? AgentFactory.Create(playerProxyForMini.ToString())
                                  : AgentFactory.Create(A);
        Agent ag2 = (B == PLAYER) ? AgentFactory.Create(playerProxyForMini.ToString())
                                  : AgentFactory.Create(B);

        var c1 = new RoundCtx { round = 1, selfLife = sim.startLife, oppLife = sim.startLife };
        var c2 = c1;

        int r = 1;
        while (r <= maxRounds && !sim.playerILost && !sim.playerIILost)
        {
            var t1 = ag1.Choose(new DecisionInput(sim.playerIHands,  c1, sim.BuildUnseen(true)));
            var t2 = ag2.Choose(new DecisionInput(sim.playerIIHands, c2, sim.BuildUnseen(false)));
            int i1 = IndexOfType(sim.playerIHands,  t1); if (i1 < 0) i1 = 0;
            int i2 = IndexOfType(sim.playerIIHands, t2); if (i2 < 0) i2 = 0;
            sim.ResolveRoundByIndex(i1, i2);

            c1.last3Opp = c1.last2Opp; c2.last3Opp = c2.last2Opp;
            c1.last2Opp = c1.lastOpp;  c2.last2Opp = c2.lastOpp;
            c1.lastOpp = t2;           c2.lastOpp = t1;
            c1.lastSelf = t1;          c2.lastSelf = t2;

            c1.selfLife = sim.playerILife;  c1.oppLife = sim.playerIILife;
            c2.selfLife = sim.playerIILife; c2.oppLife = sim.playerILife;

            r++; c1.round = r; c2.round = r;
        }

        int pa, pb;
        if ((sim.playerILost && sim.playerIILost) || (sim.playerILife == sim.playerIILife)) { pa = 1; pb = 1; }
        else if (sim.playerIILost || sim.playerILife > sim.playerIILife) { pa = 2; pb = 0; }
        else { pa = 0; pb = 2; }

        onDone?.Invoke(pa, pb);
        Destroy(go);

        miniCount++;
        RefreshUI();
        yield return null;
    }

    IEnumerator MiniRoundRobinCoroutine(List<string> names, Dictionary<string,int> miniPts)
    {
        inMini = true;
        miniCount = 0;
        miniTotal = names.Count * (names.Count - 1) / 2;
        RefreshUI();

        for (int i = 0; i < names.Count; i++)
            for (int j = i + 1; j < names.Count; j++)
            {
                yield return SimulateMiniPair(names[i], names[j], (a,b)=>{ miniPts[names[i]] += a; miniPts[names[j]] += b; });
            }

        inMini = false;
        miniCount = miniTotal = 0;
        RefreshUI();
    }

    // ---------- 최종 순위(코루틴) ----------
    IEnumerator ShowFinalStandingsCoroutine()
    {
        if (resultText) resultText.text = string.Empty;
        if (othersMatchLog) othersMatchLog.text = string.Empty;

        var totals = BuildPointTable();

        var byScore = totals.GroupBy(kv => kv.Value)
                            .OrderByDescending(g => g.Key)
                            .ToList();

        LogLine("=== 최종 순위(승: 2/무: 1/패: 0) ===");

        int rankCursor = 1;
        foreach (var grp in byScore)
        {
            var names = grp.Select(kv => kv.Key).ToList();
            int groupSize = names.Count;

            if (groupSize == 1)
            {
                string n = names[0];
                LogLine($"{rankCursor}. {n} — {totals[n]}점");
                rankCursor += 1;
                continue;
            }

            // 미니 라운드로빈(플레이어 포함)
            var miniPts = names.ToDictionary(n => n, _ => 0);
            yield return MiniRoundRobinCoroutine(names, miniPts);

            // 미니 점수 내림차순으로 서브그룹화
            var subGroups = names.GroupBy(n => miniPts[n])
                                 .OrderByDescending(g => g.Key)
                                 .ToList();

            foreach (var sub in subGroups)
            {
                var subNames = sub.ToList();
                if (subNames.Count == 1)
                {
                    string n = subNames[0];
                    LogLine($"{rankCursor}. {n} — {totals[n]}점 (미니:{miniPts[n]})");
                    rankCursor += 1;
                }
                else
                {
                    int worstRank = rankCursor + subNames.Count - 1;
                    foreach (var n in subNames)
                        LogLine($"{worstRank}. {n} — {totals[n]}점 (동점, 미니도 동률:{miniPts[n]} → 공동 최하)");
                    rankCursor += subNames.Count;
                }
            }
        }

        UpdateBothRankUI();
    }

    // ---------- 순위 UI 갱신(플레이어+상대) ----------
    void UpdateBothRankUI()
    {
        var totals = BuildPointTable();

        int prank = GetWorstSharedRank(totals, PLAYER, out int ppts);
        if (playerRankText) playerRankText.text = prank > 0 ? $"{prank}위 / {ppts}점" : "-";
        if (rankText)       rankText.text       = prank > 0 ? $"{prank}위 / {ppts}점" : "-";

        string opp = agent != null ? agent.name : null;
        if (!string.IsNullOrEmpty(opp))
        {
            int orank = GetWorstSharedRank(totals, opp, out int opts);
            if (opponentRankText) opponentRankText.text = orank > 0 ? $"{orank}위 / {opts}점" : "-";
        }
        else
        {
            if (opponentRankText) opponentRankText.text = "-";
        }
    }

    // 동점자는 그룹 최하 순위를 반환
    int GetWorstSharedRank(Dictionary<string,int> totals, string name, out int pts)
    {
        pts = 0;
        if (!totals.TryGetValue(name, out pts)) return 0;

        var byScore = totals.GroupBy(kv => kv.Value)
                            .OrderByDescending(g => g.Key)
                            .ToList();

        int passed = 0;
        foreach (var g in byScore)
        {
            int size = g.Count();
            if (g.Key == pts)
                return 1 + passed + (size - 1);
            passed += size;
        }
        return 0;
    }

    // ---------- 순위 계산 공용 함수 ----------
    Dictionary<string, int> BuildPointTable()
    {
        var totals = new Dictionary<string, int>();
        void add(string name, int pts)
        {
            if (!totals.ContainsKey(name)) totals[name] = 0;
            totals[name] += pts;
        }
        foreach (var m in leagueMatches) { add(m.A, m.ptsA); add(m.B, m.ptsB); }
        return totals;
    }

    // ---------- 공용 유틸 ----------
    void LogLine(string s)
    {
        if (othersMatchLog) othersMatchLog.text += (othersMatchLog.text.Length > 0 ? "\n" : "") + s;
        else Debug.Log(s);
    }

    void BuildLookup()
    {
        if (nameToSprite == null) nameToSprite = new Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);
        nameToSprite.Clear();
        if (cardVisuals == null) return;
        foreach (var v in cardVisuals)
        {
            var k = Norm(v.cardName);
            if (!string.IsNullOrEmpty(k)) nameToSprite[k] = v.sprite;
        }
    }

    static string Norm(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static Image EnsureImage(Button b)
    {
        if (!b) return null;
        var img = b.targetGraphic as Image;
        if (!img) img = b.GetComponent<Image>();
        if (!img) img = b.gameObject.AddComponent<Image>();
        return img;
    }

    IEnumerator EnsureFreshSystem()
    {
        if (sys != null) Destroy(sys);
        sys = gameObject.AddComponent<CardSystem>();
        yield return null;
        yield return new WaitUntil(() => sys.publicDeck != null && sys.publicDeck.Count > 0);
        sys.ClearLoseFlags();
    }

    IReadOnlyDictionary<CardType, int> BuildUnseen(bool forP1)
    {
        var map = new Dictionary<CardType, int>();
        void Acc(IEnumerable<CardType> src)
        {
            foreach (var c in src)
            {
                if (c == CardType.None) continue;
                map[c] = map.ContainsKey(c) ? map[c] + 1 : 1;
            }
        }
        Acc(sys.publicDeck);
        Acc(forP1 ? sys.playerIIHands : sys.playerIHands);
        foreach (CardType t in System.Enum.GetValues(typeof(CardType)))
            if (t != CardType.None && !map.ContainsKey(t)) map[t] = 0;
        return map;
    }

    static int IndexOfType(List<CardType> hand, CardType t)
    {
        for (int i = 0; i < hand.Count; i++) if (hand[i] == t) return i;
        return -1;
    }

    Sprite GetSprite(CardType t)
    {
        var key = t.ToString();
        if (nameToSprite != null && nameToSprite.TryGetValue(Norm(key), out var sp) && sp) return sp;
        return fallbackSprite;
    }

    // 12명(짝수) 라운드로빈 스케줄 생성
    static List<(string A, string B)[]> BuildRoundRobinEven(List<string> players)
    {
        var arr = new List<string>(players);
        int n = arr.Count;
        int rounds = n - 1;
        int half = n / 2;

        var outRounds = new List<(string, string)[]>(rounds);

        for (int r = 0; r < rounds; r++)
        {
            var pairs = new List<(string, string)>(half);
            for (int i = 0; i < half; i++)
                pairs.Add((arr[i], arr[n - 1 - i]));
            outRounds.Add(pairs.ToArray());

            var last = arr[n - 1];
            arr.RemoveAt(n - 1);
            arr.Insert(1, last);
        }
        return outRounds;
    }
}