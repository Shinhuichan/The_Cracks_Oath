using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using GameCore;

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
    이하연,
    백무적
}

[RequireComponent(typeof(CardSystem))]
public class PlayerBattle : MonoBehaviour
{
    [System.Serializable]
    public struct CardVisual
    {
        [Tooltip("카드 이름 (Cooperation, Doubt, Betrayal, Chaos, Pollution)")]
        public string cardName;
        [Tooltip("해당 카드의 스프라이트")]
        public Sprite sprite;
    }

    [Header("설정")]
    public AgentList agentName = AgentList.김현수;
    public int maxRounds = 10;

    [Header("버튼(플레이어가 0/1/2 인덱스를 선택)")]
    public Button btn0;
    public Button btn1;
    public Button btn2;

    [Header("UI - 라벨")]
    public TMP_Text roundText;
    public TMP_Text opponentNameText;
    public TMP_Text playerHpText;
    public TMP_Text opponentHpText;

    [Header("UI - 전 라운드 결과")]
    public TMP_Text resultText;
    public TMP_Text playerCurrentHpText;
    public TMP_Text agentCurrentHpText;
    public TMP_Text playerHpAmountText;
    public TMP_Text agentHpAmountText;

    [Header("카드 비주얼 매핑")]
    public CardVisual[] cardVisuals;
    public Sprite fallbackSprite;

    [Header("연출 - 선택 카드 이미지")]
    [SerializeField] private Image playerChosenImage;    // 내 카드 표시용 Image
    [SerializeField] private Image opponentChosenImage;  // 상대 카드 표시용 Image

    [Header("UI - 재시도")]
    public Button retryButton; // 게임 종료 시 활성. 누르면 같은 상대와 재경기

    [Header("리그 시뮬레이션(플레이어 경기 종료 후)")]
    public bool simulateOthers = true;
    public TMP_Text othersMatchLog;   // 없으면 Debug.Log로만 출력

    private Dictionary<string, Sprite> nameToSprite;

    CardSystem sys;
    Agent agent;
    RoundCtx pc, ac;
    int round = 1;
    bool finished = false;

    void Awake()
    {
        sys = GetComponent<CardSystem>();
        if (btn0) btn0.onClick.AddListener(() => OnPick(0));
        if (btn1) btn1.onClick.AddListener(() => OnPick(1));
        if (btn2) btn2.onClick.AddListener(() => OnPick(2));
        BuildLookup();

        // 선택 이미지 초기화
        if (playerChosenImage)
        {
            playerChosenImage.preserveAspect = true;
            playerChosenImage.gameObject.SetActive(false);
        }
        if (opponentChosenImage)
        {
            opponentChosenImage.preserveAspect = true;
            opponentChosenImage.gameObject.SetActive(false);
        }

        // 재시도 버튼 초기화
        if (retryButton)
        {
            retryButton.gameObject.SetActive(false);
            retryButton.onClick.AddListener(OnRetry);
        }
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
        agent = AgentFactory.Create(agentName.ToString());

        pc = new RoundCtx { round = 1, selfLife = sys.startLife, oppLife = sys.startLife,
                            lastSelf = CardType.None, lastOpp = CardType.None,
                            last2Opp = CardType.None, last3Opp = CardType.None };
        ac = pc;

        if (opponentNameText) opponentNameText.text = agent.name;

        RefreshUI();
    }

    // ---------- 입력 ----------
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

        // 체력 교환 결과 표시
        playerCurrentHpText.text = $"HP: {hpPAfter}";
        agentCurrentHpText.text = $"HP: {hpAAfter}";
        playerHpAmountText.text = FmtDelta(dP);
        agentHpAmountText.text = FmtDelta(dA);

        // 선택 카드 연출
        RevealChosenCards(GetSprite(pCard), GetSprite(aCard));

        round++; pc.round = round; ac.round = round;

        CheckEnd();
        RefreshUI();
    }

    string FmtDelta(int d) => d == 0 ? "0" : (d > 0 ? $"+{d}" : d.ToString());

    void CheckEnd()
    {
        if (finished) return;
        if (round > maxRounds || sys.playerILost || sys.playerIILost)
        {
            finished = true;
            if (btn0) btn0.interactable = false;
            if (btn1) btn1.interactable = false;
            if (btn2) btn2.interactable = false;

            string verdict =
                sys.playerILost && sys.playerIILost ? "무승부(동반 탈락)" :
                sys.playerILost ? $"{agent.name} 승" :
                sys.playerIILost ? "플레이어 승" :
                (sys.playerILife == sys.playerIILife ? "무승부(판정)" :
                 sys.playerILife > sys.playerIILife ? "플레이어 승(판정)" : $"{agent.name} 승(판정)");

            if (resultText)
                resultText.text += $"\n결과: {verdict}  |  총 라운드 {round-1}";

            // 재시도 버튼 표시
            if (retryButton)
            {
                retryButton.gameObject.SetActive(true);
                retryButton.interactable = true;
            }

            // 나머지 참가자 무작위 매치업
            if (simulateOthers)
                StartCoroutine(SimulateOthersCoroutine());
        }
    }

    // ---------- 재시도 ----------
    void OnRetry()
    {
        if (!finished) return;
        StartCoroutine(RestartRoutine());
    }

    IEnumerator RestartRoutine()
    {
        // 버튼 잠금
        if (retryButton) retryButton.interactable = false;

        // 상태 초기화
        finished = false;
        round = 1;

        // 선택 연출 숨기기
        HideChosenCards();

        // 로그 초기화
        if (playerCurrentHpText) playerCurrentHpText.text = string.Empty;
        if (agentCurrentHpText)  agentCurrentHpText.text  = string.Empty;
        if (playerHpAmountText)  playerHpAmountText.text  = string.Empty;
        if (agentHpAmountText) agentHpAmountText.text = string.Empty;
        if (resultText) resultText.text = string.Empty;
        if (othersMatchLog) othersMatchLog.text = string.Empty;

        // 카드 시스템 재생성
        yield return EnsureFreshSystem();

        // 동일 에이전트 재생성
        agent = AgentFactory.Create(agentName.ToString());

        // 컨텍스트 초기화
        pc = new RoundCtx { round = 1, selfLife = sys.startLife, oppLife = sys.startLife,
                            lastSelf = CardType.None, lastOpp = CardType.None,
                            last2Opp = CardType.None, last3Opp = CardType.None };
        ac = pc;

        if (opponentNameText) opponentNameText.text = agent.name;

        // 플레이 입력 복구
        if (btn0) btn0.interactable = true;
        if (btn1) btn1.interactable = true;
        if (btn2) btn2.interactable = true;

        // 재시도 버튼 숨김
        if (retryButton)
        {
            retryButton.gameObject.SetActive(false);
            retryButton.interactable = true;
        }

        RefreshUI();
    }

    // ---------- UI ----------
    void RefreshUI()
    {
        if (roundText) roundText.text = $"Round {Mathf.Min(round, maxRounds)} / {maxRounds}";
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
        bool ok = idx < sys.playerIHands.Count && !finished;
        b.interactable = ok;

        var img = EnsureImage(b);
        img.preserveAspect = true;

        Sprite s = fallbackSprite;
        if (ok)
        {
            string key = sys.playerIHands[idx].ToString(); // CardType 이름
            if (nameToSprite != null && nameToSprite.TryGetValue(Norm(key), out var sp))
                s = sp;
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

    // ---------- 리그 시뮬 ----------
    IEnumerator SimulateOthersCoroutine()
    {
        // 참가자 풀 구성(플레이어 상대 제외)
        var all = new List<AgentList>((AgentList[])System.Enum.GetValues(typeof(AgentList)));
        all.Remove(agentName);

        // 셔플 후 2명씩 페어링. 홀수면 마지막은 부전승
        Shuffle(all);
        for (int i = 0; i < all.Count; i += 2)
        {
            if (i == all.Count - 1)
            {
                LogLine($"[부전승] {all[i]}");
                break;
            }
            yield return SimulateAIVsAI(all[i], all[i + 1]);
        }
    }

    IEnumerator SimulateAIVsAI(AgentList A1, AgentList A2)
    {
        // 임시 보드 생성
        var go = new GameObject($"Sim_{A1}_vs_{A2}");
        var sim = go.AddComponent<CardSystem>();
        yield return null; // Start() 초기화 대기
        yield return new WaitUntil(() => sim.publicDeck != null && sim.publicDeck.Count > 0);

        var ag1 = AgentFactory.Create(A1.ToString());
        var ag2 = AgentFactory.Create(A2.ToString());

        var c1 = new RoundCtx { round = 1, selfLife = sim.startLife, oppLife = sim.startLife };
        var c2 = c1;

        int r = 1;
        while (r <= maxRounds && !sim.playerILost && !sim.playerIILost)
        {
            // 각자 관측치로 선택
            var unseen1 = sim.BuildUnseen(isP1: true);
            var unseen2 = sim.BuildUnseen(isP1: false);

            var t1 = ag1.Choose(new DecisionInput(sim.playerIHands,  c1, unseen1));
            var t2 = ag2.Choose(new DecisionInput(sim.playerIIHands, c2, unseen2));

            int i1 = IndexOfType(sim.playerIHands,  t1); if (i1 < 0) i1 = 0;
            int i2 = IndexOfType(sim.playerIIHands, t2); if (i2 < 0) i2 = 0;

            sim.ResolveRoundByIndex(i1, i2);

            // 컨텍스트 업데이트
            c1.last3Opp = c1.last2Opp; c2.last3Opp = c2.last2Opp;
            c1.last2Opp = c1.lastOpp;  c2.last2Opp = c2.lastOpp;
            c1.lastOpp = t2;           c2.lastOpp = t1;
            c1.lastSelf = t1;          c2.lastSelf = t2;

            c1.selfLife = sim.playerILife;  c1.oppLife = sim.playerIILife;
            c2.selfLife = sim.playerIILife; c2.oppLife = sim.playerILife;

            r++; c1.round = r; c2.round = r;
        }

        string result =
            sim.playerILost && sim.playerIILost ? "무승부" :
            sim.playerILost ? $"{A2} 승" :
            sim.playerIILost ? $"{A1} 승" :
            (sim.playerILife == sim.playerIILife ? "무승부(판정)" :
             sim.playerILife > sim.playerIILife ? $"{A1} 승(판정)" : $"{A2} 승(판정)");

        LogLine($"[{A1} vs {A2}] → {result} | HP {sim.playerILife}:{sim.playerIILife}");

        Destroy(go);
    }

    void LogLine(string s)
    {
        if (othersMatchLog)
            othersMatchLog.text += (othersMatchLog.text.Length > 0 ? "\n" : string.Empty) + s;
        else
            Debug.Log(s);
    }

    // ---------- 매핑/유틸 ----------
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
    }

    IReadOnlyDictionary<CardType,int> BuildUnseen(bool forP1)
    {
        var map = new Dictionary<CardType,int>();
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

    // 카드 타입 → 스프라이트
    Sprite GetSprite(CardType t)
    {
        var key = t.ToString();
        if (nameToSprite != null && nameToSprite.TryGetValue(Norm(key), out var sp) && sp) return sp;
        return fallbackSprite;
    }

    // 리스트 셔플
    static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}