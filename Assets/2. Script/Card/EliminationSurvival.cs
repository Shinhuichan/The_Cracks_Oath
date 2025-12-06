using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GameCore;

public class EliminationSwissSurvival : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private H2HMatchLoop matchRunner; // ★ 시뮬레이터 연결 필수

    [Header("Simulation Settings")]
    [SerializeField] private int gamesPerMatch = 1000;

    [Header("Award Settings")]
    [SerializeField] private int butcherBonusScore = 1;      // 학살자 보너스 점수
    [SerializeField] private int giantSlayerBonusScore = 3;  // 자이언트 슬레이어 보너스 점수
    [SerializeField] private int doomsdayClockBonusScore = 1; // 종말의 시계 보너스 점수
    [SerializeField] private int zombieBonusScore = 1;       // 언다잉 보너스 점수;
    [SerializeField] private int kingSlayerBonusScore = 3;   // 킹 슬레이어 보너스 점수
    [SerializeField] private int swampMasterBonusScore = 1;  // 늪의 지배자 보너스 점수
    [SerializeField] private int blitzerBonusScore = 2;      // 전광석화 보너스 점수
    [SerializeField] private int perfectionistBonusScore = 1; // 완벽주의자 보너스 점수
    [SerializeField] private int hardRoadBonusScore = 2;     // 고난의 행군 보너스 점수
    [SerializeField] private int streakerBonusScore = 2;      // 파죽지세 보너스 점수
    [SerializeField] private int pacifistsBonusScore = 1;     // 평화주의자 보너스 점수
    [SerializeField] private int dominantsBonusScore = 1;     // 우성인자 보너스 점수


    // ★ [추가] 라운드별 해설 로그를 저장할 리스트
    private List<string> roundHighlights = new List<string>();  

    [Header("VFX / SFX")]
    [SerializeField] private GameObject eliminationAlertPanel; // (선택) 붉은 비상등 패널
    [SerializeField] private AudioClip eliminationSfx;         // (선택) 쾅! 소리
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip sfxAward;             // 어워드 팡파레 효과음

    [Header("Advanced VFX / SFX")]
    [SerializeField] private AudioClip sfxRouletteTick;  // 룰렛 돌아가는 소리 (틱, 틱)
    [SerializeField] private AudioClip sfxTargetLocked;  // 타겟 확정 소리 (삐-! 혹은 쿵!)
    [SerializeField] private AudioClip sfxUpsetAlert;    // 이변 발생 경보음 (사이렌 등)

    private class Participant
    {
        public AgentList id;
        public int score;
        public int wins;
        public int draws;
        public int totalCandleGap;
        public int buchholz;
        public int upsetScore;
        public List<AgentList> opponents = new List<AgentList>();
        
        // ★ [추가] 어워드 누적 데이터
        public int sacrificeWins;    
        public int closeWins;        
        public int kingSlayerPoints; 
        
        // ★ [추가] 신규 어워드용 누적 데이터
        public int perfectWins; // 완벽주의자
        public int fastWins;    // 전광석화
        public int currentStreak; // 현재 연승
        public int maxStreak;     // 최대 연승 (파죽지세)

        public int coopCount;       // ★ 평화주의자
        public int phaseBonusCount; // ★ 우성인자

        // ★★★ [필수 추가] 이게 없어서 오류 발생함 ★★★
        public HashSet<AgentList> beatenOpponents = new HashSet<AgentList>();

        public Participant(AgentList agent) { id = agent; }
    }

    private List<AgentList> survivors = new List<AgentList>();
    private List<string> eliminatedLog = new List<string>();
    
    private List<Participant> currentLeagueParticipants = new List<Participant>();
    private int currentSurvivalRound = 0;
    private bool isSimulating = false;
    
    // ★★★ [필수 추가] 라운드 수 공유를 위한 변수 ★★★
    private int calculatedTotalRounds = 0;

    private const string UI_GROUP = "EliminationUI";

    private void Start()
    {
        if (matchRunner == null) matchRunner = FindObjectOfType<H2HMatchLoop>();

        // ★ [추가] 시작할 때 경고 패널이 보이면 안 되므로 끕니다.
        if (eliminationAlertPanel != null) eliminationAlertPanel.SetActive(false);
        
        InitializeRoster();
        UpdateUI_Initial();
    }

    private void InitializeRoster()
    {
        survivors.Clear();
        eliminatedLog.Clear();
        currentSurvivalRound = 0;

        foreach (AgentList agent in Enum.GetValues(typeof(AgentList)))
        {
            // ★ 핵심 수정: Zero는 정식 참가자가 아니므로 리스트 생성 시 제외합니다.
            if (agent == AgentList.Zero) continue; 

            survivors.Add(agent);
        }
        survivors.Shuffle();
    }

    private void UpdateUI_Initial()
    {
        if (UIManager.I)
        {
            UIManager.I.TrySetOnClick(UI_GROUP, "NextRoundButton", OnClickRunLeague);
            UIManager.I.TrySetOnClick(UI_GROUP, "QuitButton", OnClickQuit);
            UIManager.I.TrySetText(UI_GROUP, "RankingText", "엘리미네이션 서바이벌 대기 중...");
            UIManager.I.TrySetText(UI_GROUP, "EliminatedLogText", $"생존자: {survivors.Count}명");
            UIManager.I.TrySetText(UI_GROUP, "StatusText", "준비 완료");
        }
    }

    public void OnClickRunLeague()
    {
        if (isSimulating) return;
        if (survivors.Count <= 1) return;
        if (matchRunner == null) 
        {
            Debug.LogError("H2HMatchLoop가 연결되지 않았습니다!");
            return;
        }

        StartCoroutine(RunOneSwissLeagueAndEliminate());
    }

    public void OnClickQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private IEnumerator RunOneSwissLeagueAndEliminate()
    {
        isSimulating = true;
        currentSurvivalRound++;

        // 1. 참가자 초기화
        currentLeagueParticipants.Clear();
        foreach (var agent in survivors)
            currentLeagueParticipants.Add(new Participant(agent));

        UIManager.I.TrySetInteractable(UI_GROUP, "NextRoundButton", false);
        UIManager.I.TrySetInteractable(UI_GROUP, "QuitButton", false);

        // ★ [추가] 문지기(Gatekeeper) 시스템
        // 인원이 홀수일 경우, 'Agent Zero'를 임시 참가자로 투입하여 짝을 맞춤
        Participant gatekeeper = null;
        if (currentLeagueParticipants.Count % 2 != 0)
        {
            // AgentList.Zero는 팩토리에 정의되어 있어야 합니다.
            gatekeeper = new Participant(AgentList.Zero); 
            currentLeagueParticipants.Add(gatekeeper);
            
            // UI나 로그에 표시 (선택 사항)
            Debug.Log($"[Survival] 인원 홀수({currentLeagueParticipants.Count-1}명) 감지: 문지기 {AgentList.Zero} 투입.");
        }

        // 스위스 라운드 수 계산
        int n = currentLeagueParticipants.Count; // 문지기 포함된 인원 기준
        int baseRounds = Mathf.CeilToInt(Mathf.Log(n, 2)) + 1;
        // ★ [수정] 지역 변수 대신 멤버 변수에 저장
        calculatedTotalRounds = Mathf.CeilToInt(baseRounds * 1.5f);
        
        // 라운드 수 보정
        if (calculatedTotalRounds >= n) calculatedTotalRounds = n - 1;
        if (calculatedTotalRounds < 3 && n > 3) calculatedTotalRounds = 3;

        // === 스위스 라운드 루프 ===
        for (int round = 1; round <= calculatedTotalRounds; round++)
        {
            // ★ 라운드 시작 시 하이라이트 초기화
            roundHighlights.Clear();

            // ★ [King Slayer 준비] Top 3 (왕좌) 식별
            HashSet<AgentList> currentKings = new HashSet<AgentList>();
            if (round > 1)
            {
                var sortedRank = SortParticipants(currentLeagueParticipants);
                for (int k = 0; k < 3 && k < sortedRank.Count; k++)
                {
                    currentKings.Add(sortedRank[k].id);
                }
            }

            // 매치업 생성
            var pairings = GenerateSwissPairings(currentLeagueParticipants);
            int matchIdx = 0;
            StringBuilder roundLog = new StringBuilder();
            
            // 라운드 헤더
            string gatekeeperMsg = gatekeeper != null ? " (문지기 투입됨)" : "";
            roundLog.AppendLine($"<b>[Round {round}/{calculatedTotalRounds} 결과{gatekeeperMsg}]</b>");

            // 매치 루프
            foreach (var pair in pairings)
            {
                // 참가자 추출
                Participant p1 = pair.Item1;
                Participant p2 = pair.Item2;

                // 부전승 처리
                if (p2 == null)
                {
                    p1.score += 3; p1.wins++;
                    roundLog.AppendLine($"<color=yellow>■ {p1.id} (부전승 - 매칭 실패)</color>");
                    continue;
                }

                // 대진 기록
                p1.opponents.Add(p2.id);
                p2.opponents.Add(p1.id);

                // UI 업데이트
                UIManager.I.TrySetText(UI_GROUP, "StatusText", 
                    $"<size=40><color=orange>▶ {round}R 진행 중 ({matchIdx + 1}/{pairings.Count})</color></size>\n" +
                    $"현재: {p1.id} vs {p2.id} ({gamesPerMatch}판)\n\n" + roundLog.ToString());

                // 시뮬레이션 실행 (H2HMatchLoop)
                H2HMatchLoop.BatchResult result = new H2HMatchLoop.BatchResult();
                yield return matchRunner.RunBatchSimulation(p1.id, p2.id, gamesPerMatch, (res) => {
                    result = res;});

                // ★ [데이터 반영] H2HMatchLoop에서 집계된 상세 데이터 누적
                p1.sacrificeWins += result.p1SacrificeWins;
                p1.closeWins += result.p1CloseWins;
                p1.perfectWins += result.p1PerfectWins; // ★
                p1.fastWins += result.p1FastWins;       // ★
                // ▼ 여기 두 줄이 빠져 있었습니다!
                p1.coopCount += result.p1CoopCount;       // 평화주의자 데이터
                p1.phaseBonusCount += result.p1PhaseBonusCount; // 우성인자 데이터
                
                p2.sacrificeWins += result.p2SacrificeWins;
                p2.closeWins += result.p2CloseWins;
                p2.perfectWins += result.p2PerfectWins; // ★
                p2.fastWins += result.p2FastWins;       // ★
                // ▼ 여기 두 줄이 빠져 있었습니다!
                p2.coopCount += result.p2CoopCount;
                p2.phaseBonusCount += result.p2PhaseBonusCount;

                if (AgentManager.I != null) AgentManager.I.ApplyBatchResult(p1.id, p2.id, result.p1Wins, result.p2Wins, result.draws);

                // 결과 변수
                int p1W = result.p1Wins;
                int p2W = result.p2Wins;
                int dr = result.draws;
                
                p1.totalCandleGap += (result.p1Candles - result.p2Candles);
                p2.totalCandleGap += (result.p2Candles - result.p1Candles);

                // 승자 판정 및 ELO/전적 갱신용 변수
                AgentManager.MatchOutcome outcomeP1 = AgentManager.MatchOutcome.Draw;
                string winnerStr = "";

                // ★ [추가] 무승부 기록 (총 게임 수 기준이 아니라 매치 결과 기준)
                // 여기서는 1000판 중 다수결이 아니라, 매치 자체의 무승부를 의미한다면 logic 수정 필요.
                // 현재 로직: (p1W > p2W) ? P1승 : (p2W > p1W) ? P2승 : 무승부
                
                // 티어 정보 가져오기
                int tierP1 = (int)GetAgentThreatLevel(p1.id);
                int tierP2 = (int)GetAgentThreatLevel(p2.id);
                bool isUpset = false;

                // ★ [승패 판정 전 점수 저장] King Slayer 조건 체크용
                int p1ScoreBefore = p1.score;
                int p2ScoreBefore = p2.score;

                // === 승패 판정 및 스택 누적 ===
                if (p1W > p2W)     
                {
                    p1.score += 3; p1.wins++; p2.score -= 1;
                    winnerStr = $"<color=#00FFFF>{p1.id} 승</color>";
                    
                    // [Giant Slayer] ThreatLevel이 더 높은 상대를 이겼을 때만 스택 누적
                    if (GetAgentThreatLevel(p2.id) > GetAgentThreatLevel(p1.id)) 
                    {
                        p1.upsetScore += (int)GetAgentThreatLevel(p2.id) - (int)GetAgentThreatLevel(p1.id);
                        if ((int)GetAgentThreatLevel(p2.id) - (int)GetAgentThreatLevel(p1.id) >= 2) isUpset = true;
                    }

                    // ★ [King Slayer 수정됨] 
                    // 1. 나는 왕이 아님 (!Contains(p1))
                    // 2. 상대는 왕임 (Contains(p2))
                    // 3. 상대의 점수가 나보다 1점 이상 높았음 (p2ScoreBefore - p1ScoreBefore >= 1)
                    if (!currentKings.Contains(p1.id) && currentKings.Contains(p2.id)) 
                    {
                        if (p2ScoreBefore - p1ScoreBefore >= 1)
                        {
                            p1.kingSlayerPoints += 1; 
                        }
                    }

                    // [Streak] 연승 계산
                    p1.currentStreak++;
                    if (p1.currentStreak > p1.maxStreak) p1.maxStreak = p1.currentStreak;
                    
                    p2.currentStreak = 0; // 패자는 연승 초기화
                    
                    outcomeP1 = AgentManager.MatchOutcome.Win;
                    p1.beatenOpponents.Add(p2.id);
                } 
                else if (p2W > p1W)
                {
                    p2.score += 3; p2.wins++; p1.score -= 1;
                    winnerStr = $"<color=#FF7F50>{p2.id} 승</color>";

                    // [Giant Slayer] P2 승리 시
                    if (GetAgentThreatLevel(p1.id) > GetAgentThreatLevel(p2.id))
                    {
                        p2.upsetScore += (int)GetAgentThreatLevel(p1.id) - (int)GetAgentThreatLevel(p2.id);
                        if ((int)GetAgentThreatLevel(p1.id) - (int)GetAgentThreatLevel(p2.id) >= 2) isUpset = true;
                    }

                    // ★ [King Slayer 수정됨] P2 승리 시
                    if (!currentKings.Contains(p2.id) && currentKings.Contains(p1.id)) 
                    {
                        if (p1ScoreBefore - p2ScoreBefore >= 1)
                        {
                            p2.kingSlayerPoints += 1;
                        }
                    }
                    
                    // [Streak]
                    p2.currentStreak++;
                    if (p2.currentStreak > p2.maxStreak) p2.maxStreak = p2.currentStreak;

                    p1.currentStreak = 0;

                    outcomeP1 = AgentManager.MatchOutcome.Loss;
                    p2.beatenOpponents.Add(p1.id);
                }
                else
                {
                    p1.score += 1; p2.score += 1;
                    p1.draws++; p2.draws++;
                    winnerStr = "무승부";
                    outcomeP1 = AgentManager.MatchOutcome.Draw;
                }

                // === [★ 신규] 시스템 해설 생성 로직 ===
                GenerateMatchCommentary(p1, p2, result, p1W > p2W ? p1 : (p2W > p1W ? p2 : null));

                // ★ [통합된 로그 출력 로직] ★
                // 문지기 매치인지 확인
                bool isGatekeeperMatch = (p1.id == AgentList.Zero || p2.id == AgentList.Zero);
                string logLine = "";

                if (isGatekeeperMatch)
                {
                    // Case A: 문지기 매치 (회색, 업셋 판정 제외)
                    logLine = $"<color=black>■ {p1.id} vs {p2.id} : {winnerStr} ({p1W}/{dr}/{p2W}) - 문지기 매치</color>";
                }
                else
                {
                    // Case B: 일반 매치
                    logLine = $"■ {p1.id} vs {p2.id} : {winnerStr} ({p1W}/{dr}/{p2W})";

                    // 업셋 발생 시 강조 추가
                    if (isUpset)
                    {
                        logLine += " <color=yellow><b>[UPSET!]</b></color>";
                        
                        // 이변 UI 연출 (잠시 멈춤)
                        UIManager.I.TrySetText(UI_GROUP, "RankingText", 
                            $"<size=50><color=yellow> 대이변 발생! </color></size>\n\n" +
                            $"<size=40>{(p1W > p2W ? p1.id : p2.id)}</size>이(가)\n상위 랭커를 격파했습니다!");
                        yield return new WaitForSeconds(1.5f);
                    }
                }

                // ★ [연출 강화] 업셋 발생 시 "슬로우 모션" 효과
                if (isUpset)
                {
                    logLine += " <color=yellow><b>[UPSET!]</b></color>";
                    
                    // 1. 사운드 재생
                    if (audioSource && sfxUpsetAlert) audioSource.PlayOneShot(sfxUpsetAlert);

                    // 2. UI에 크게 띄우기 (잠시 랭킹판을 가리고 속보 전달)
                    string upsetMsg = $"<size=50><color=yellow> 대이변 발생! </color></size>\n\n" +
                                      $"<size=40>{(p1W > p2W ? p1.id : p2.id)}</size> 가\n" +
                                      $"<size=30>상위 랭커 {(p1W > p2W ? p2.id : p1.id)}를 격파했습니다!</size>";
                    
                    UIManager.I.TrySetText(UI_GROUP, "RankingText", upsetMsg);

                    // 3. 화면 흔들림 (짧게)
                    StartCoroutine(ShakeUI(0.3f, 10f));

                    // 4. 플레이어가 읽을 시간 주기 (2초 정지)
                    yield return new WaitForSeconds(3.0f);
                }

                // 최종적으로 한 번만 추가
                roundLog.AppendLine(logLine);

                matchIdx++;
            }
            
            // 중간 순위 보여줄 때는 문지기도 포함해서 보여줌 (누가 문지기한테 졌는지 확인 가능하게)
            UpdateRankingBoard(round, false); 
        }

        // === 리그 종료 및 탈락자 처리 ===
        
        // 1. 부흐홀츠(Solkoff) 계산: 상대방들의 승점 합계
        // 문지기가 포함된 상태에서 계산해야 문지기를 만난 사람의 점수가 공정하게 계산됨
        CalculateBuchholz();

        // 1-1. ★ 문지기 제거
        // 순위 산정 및 꼴찌 탈락 처리를 위해 문지기는 이제 리스트에서 빠짐
        if (gatekeeper != null) currentLeagueParticipants.Remove(gatekeeper);

        // ★ 리그 종료 후에만 승점 보너스 지급
        yield return StartCoroutine(AwardCeremonySequence());

        // 3. 최종 순위판 갱신
        UpdateRankingBoard(calculatedTotalRounds, true);

        // ★ [추가] 리그 종료 후, 변동된 ELO를 바탕으로 차기 시즌을 위한 티어 재산정
        if (AgentManager.I != null) AgentManager.I.RecalculateThreatLevels();

        // 4. 탈락자 선정 및 연출
        yield return StartCoroutine(ProcessEliminationSequence());
        // ProcessElimination();

        isSimulating = false;
        
        bool isFinished = survivors.Count <= 1;
        UIManager.I.TrySetInteractable(UI_GROUP, "NextRoundButton", !isFinished);
        UIManager.I.TrySetInteractable(UI_GROUP, "QuitButton", true);
        
        if (isFinished)
            UIManager.I.TrySetText(UI_GROUP, "StatusText", "모든 서바이벌이 종료되었습니다.");
        else
            UIManager.I.TrySetText(UI_GROUP, "StatusText", $"<color=yellow>★ {currentSurvivalRound}회차 종료. 탈락자 발생 ★</color>");
    }

    // ★ [수정됨] MatchCommentarySystem을 활용한 고도화된 해설 생성
    private void GenerateMatchCommentary(Participant p1, Participant p2, H2HMatchLoop.BatchResult result, Participant winner)
    {
        // AgentManager에서 티어 및 ELO 정보 가져오기
        ThreatLevel t1 = GetAgentThreatLevel(p1.id);
        ThreatLevel t2 = GetAgentThreatLevel(p2.id);
        double elo1 = AgentManager.I != null ? AgentManager.I.GetElo(p1.id) : 1000;
        double elo2 = AgentManager.I != null ? AgentManager.I.GetElo(p2.id) : 1000;

        // 해설 생성 (정적 클래스 호출)
        string commentary = MatchCommentarySystem.I.GenerateCommentary(
            p1.id, p2.id, result, t1, t2, elo1, elo2
        );

        // 하이라이트 리스트에 추가
        roundHighlights.Add(commentary);
    }

    private IEnumerator AwardCeremonySequence()
    {
        // ----------------------------------------------------------------------
        // 1. 시상식 준비
        // ----------------------------------------------------------------------
        UIManager.I.TrySetText(UI_GROUP, "StatusText", "<color=#98FB98>★ 특별 보너스 점수 심사 중... ★</color>");
        yield return new WaitForSeconds(1.5f);

        // 시상 처리용 로컬 함수 (고정 보너스)
        void AwardList(List<Participant> winners, int bonus, string title, string desc, string color)
        {
            if (winners == null || winners.Count == 0) return;

            foreach (var p in winners) p.score += bonus;
            
            UpdateRankingBoard(calculatedTotalRounds, true);
            if (audioSource && sfxAward) audioSource.PlayOneShot(sfxAward);
            
            StringBuilder sprites = new StringBuilder();
            foreach (var p in winners) sprites.Append($"<sprite name=\"{p.id}\">   "); 

            string names = string.Join(", ", winners.Select(p => p.id.ToString()));

            string msg = $"<size=35><color={color}>{title}</color></size>\n\n" +
                         $"<size=50>{sprites} <b>{names}</b></size>\n" +
                         $"<size=25>{desc}</size>\n" +
                         $"<color=yellow><b>▶ 승점 +{bonus} 획득!</b></color>";
            
            UIManager.I.TrySetText(UI_GROUP, "RankingText", msg);
            StartCoroutine(ShakeUI(0.5f, 5f));
        }

        // ----------------------------------------------------------------------
        // 2. 어워드별 선정 및 시상
        // ----------------------------------------------------------------------

        // [1] 학살자 (The Butcher)
        int maxGap = currentLeagueParticipants.Max(p => p.totalCandleGap);
        var butchers = currentLeagueParticipants.Where(p => p.totalCandleGap == maxGap).ToList();
        AwardList(butchers, butcherBonusScore, " THE BUTCHER ", $"압도적인 파괴력 (득실 +{maxGap})", "red");
        yield return new WaitForSeconds(3.5f);

        // [2] 거인 살해자 (수정됨: 모든 달성자에게 개별 포인트 지급)
        var slayers = currentLeagueParticipants.Where(p => p.upsetScore > 0).OrderByDescending(p => p.upsetScore).ToList();
        if (slayers.Count > 0)
        {
            // 점수 부여 (각자의 upsetScore만큼)
            foreach (var p in slayers) p.score += p.upsetScore;

            UpdateRankingBoard(calculatedTotalRounds, true);
            if (audioSource && sfxAward) audioSource.PlayOneShot(sfxAward);

            StringBuilder sprites = new StringBuilder();
            foreach (var p in slayers) sprites.Append($"<sprite name=\"{p.id}\">   ");

            // 이름 뒤에 각자의 점수 표시
            string names = string.Join(", ", slayers.Select(p => $"{p.id}(+{p.upsetScore})"));

            string msg = $"<size=32><color=#00FFFF> GIANT SLAYER </color></size>\n\n" +
                         $"<size=36>{sprites} <b>{names}</b></size>\n" +
                         $"<size=30>강자를 꺾은 용기 ({slayers.Count}명)</size>\n" +
                         $"<color=yellow><b>▶ 누적된 업셋 포인트만큼 승점 획득!</b></color>";

            UIManager.I.TrySetText(UI_GROUP, "RankingText", msg);
            StartCoroutine(ShakeUI(0.5f, 5f));
            yield return new WaitForSeconds(3.5f);
        }

        // [3] 종말의 시계
        int maxSac = currentLeagueParticipants.Max(p => p.sacrificeWins);
        if (maxSac > 0)
        {
            var cultists = currentLeagueParticipants.Where(p => p.sacrificeWins == maxSac).ToList();
            AwardList(cultists, doomsdayClockBonusScore, " DOOMSDAY CLOCK ", $"완성된 의식 ({maxSac}회)", "#800080");
            yield return new WaitForSeconds(3.5f);
        }

        // [4] 불사신
        int undyingThreshold = Mathf.FloorToInt(gamesPerMatch * calculatedTotalRounds * 0.05f);
        var zombieCandidates = currentLeagueParticipants.Where(p => p.closeWins >= undyingThreshold).ToList();
        if (zombieCandidates.Count > 0)
        {
            int maxClose = zombieCandidates.Max(p => p.closeWins);
            var zombies = zombieCandidates.Where(p => p.closeWins == maxClose).ToList();
            AwardList(zombies, zombieBonusScore, " THE UNDYING ", $"죽음의 문턱에서 생환 ({maxClose}회)", "#2E8B57");
            yield return new WaitForSeconds(3.5f);
        }

        // [5] 왕위 찬탈자
        int maxKingKills = currentLeagueParticipants.Max(p => p.kingSlayerPoints);
        if (maxKingKills > 0)
        {
            var kingSlayers = currentLeagueParticipants.Where(p => p.kingSlayerPoints == maxKingKills).ToList();
            AwardList(kingSlayers, kingSlayerBonusScore, " USURPER OF THE THRONE ", $"왕관의 무게를 견뎌라 ({maxKingKills}회 처치)", "#DC143C");
            yield return new WaitForSeconds(3.5f);
        }

        // [6] 늪의 지배자
        float swampThreshold = gamesPerMatch * calculatedTotalRounds * 0.1f;
        var swampCandidates = currentLeagueParticipants.Where(p => p.draws >= swampThreshold).ToList();
        if (swampCandidates.Count > 0)
        {
            int maxDraws = swampCandidates.Max(p => p.draws);
            var swampMasters = swampCandidates.Where(p => p.draws == maxDraws).ToList();
            AwardList(swampMasters, swampMasterBonusScore, " SWAMP MASTER ", $"지지 않는 끈기 ({maxDraws}무)", "#8FBC8F");
            yield return new WaitForSeconds(3.5f);
        }

        // [7] 전광석화
        int maxFast = currentLeagueParticipants.Max(p => p.fastWins);
        if (maxFast > 0)
        {
            var blitzers = currentLeagueParticipants.Where(p => p.fastWins == maxFast).ToList();
            AwardList(blitzers, blitzerBonusScore, " THE BLITZ ", $"눈보다 빠른 승리 ({maxFast}회 조기 종료)", "#FFD700");
            yield return new WaitForSeconds(3.5f);
        }

        // [8] 완벽주의자
        int maxPerfect = currentLeagueParticipants.Max(p => p.perfectWins);
        if (maxPerfect > 0)
        {
            var perfectionists = currentLeagueParticipants.Where(p => p.perfectWins == maxPerfect).ToList();
            AwardList(perfectionists, perfectionistBonusScore, " THE PERFECTIONIST ", $"흠집 없는 승리 ({maxPerfect}회 퍼펙트)", "#E0FFFF");
            yield return new WaitForSeconds(3.5f);
        }

        // [9] 고난의 행군
        // ★ [수정] 상위권 제외 로직이 너무 엄격했음. (정렬 후 3위 밖에서 부흐홀츠 1위)
        // 만약 참가자가 적다면 스킵될 수 있음. 안전장치 추가.
        var sortedForHardRoad = SortParticipants(currentLeagueParticipants);
        if (sortedForHardRoad.Count > 3)
        {
            var hardRoadCandidates = sortedForHardRoad.Skip(3).ToList();
            int maxBuchholz = hardRoadCandidates.Max(p => p.buchholz);
            
            // 부흐홀츠가 0보다 커야 함
            if (maxBuchholz > 0)
            {
                var hardRoaders = hardRoadCandidates.Where(p => p.buchholz == maxBuchholz).ToList();
                AwardList(hardRoaders, hardRoadBonusScore, " THE HARD ROAD ", $"가시밭길을 걸어온 자 (부흐홀츠 {maxBuchholz})", "#8B4513");
                yield return new WaitForSeconds(3.5f);
            }
        }

        // [10] 파죽지세
        int maxStreak = currentLeagueParticipants.Max(p => p.maxStreak);
        if (maxStreak >= 3)
        {
            var streakers = currentLeagueParticipants.Where(p => p.maxStreak == maxStreak).ToList();
            AwardList(streakers, streakerBonusScore, " UNSTOPPABLE ", $"멈출 수 없는 기세 ({maxStreak}연승)", "#FF4500");
            yield return new WaitForSeconds(3.5f);
        }

        // --- ★ [누락 수정] 신규 어워드 시상 로직 추가 ---

        // [11] 평화주의자 (The Pacifist)
        int maxCoop = currentLeagueParticipants.Max(p => p.coopCount);
        if (maxCoop > 0)
        {
            var pacifists = currentLeagueParticipants.Where(p => p.coopCount == maxCoop).ToList();
            AwardList(pacifists, pacifistsBonusScore, " THE PACIFIST ", $"평화를 사랑하는 마음 ({maxCoop}회 협력)", "#98FB98");
            yield return new WaitForSeconds(3.5f);
        }

        // [12] 우성인자 (Dominant Gene)
        int maxBonus = currentLeagueParticipants.Max(p => p.phaseBonusCount);
        if (maxBonus > 0)
        {
            var dominants = currentLeagueParticipants.Where(p => p.phaseBonusCount == maxBonus).ToList();
            AwardList(dominants, dominantsBonusScore, " DOMINANT GENE ", $"우월한 생존력 ({maxBonus}회 보너스)", "#FFD700");
            yield return new WaitForSeconds(3.5f);
        }

        UIManager.I.TrySetText(UI_GROUP, "StatusText", "최종 순위 산정 완료.");
        yield return new WaitForSeconds(1.0f);
    }

    // UI 패널 흔들림 효과 (Camera 대신 Panel 사용)
    private IEnumerator ShakeUI(float duration, float magnitude)
    {
        if (eliminationAlertPanel == null) yield break;

        // 1. 패널 활성화 (숨겨져 있었다면 보이게)
        eliminationAlertPanel.SetActive(true);

        // 2. 원래 위치 저장 (LocalPosition 기준)
        Vector3 originalPos = eliminationAlertPanel.transform.localPosition;
        float elapsed = 0.0f;

        // 3. 흔들림 효과
        while (elapsed < duration)
        {
            // UI이므로 Z축은 건드리지 않고 X, Y만 흔듦
            float x = UnityEngine.Random.Range(-1f, 1f) * magnitude;
            float y = UnityEngine.Random.Range(-1f, 1f) * magnitude;

            eliminationAlertPanel.transform.localPosition = originalPos + new Vector3(x, y, 0);

            elapsed += Time.deltaTime;
            yield return null;
        }

        // 3. 위치 복구
        eliminationAlertPanel.transform.localPosition = originalPos;
    } 

    // --- 유틸리티 함수들 (기존 유지) ---
    private void CalculateBuchholz()
    {
        var map = currentLeagueParticipants.ToDictionary(p => p.id, p => p);
        foreach (var p in currentLeagueParticipants)
        {
            p.buchholz = 0;
            foreach (var oppId in p.opponents)
                if (map.TryGetValue(oppId, out var opp)) p.buchholz += opp.score;
        }
    }

    // ★ [수정됨] 탈락자 처리 함수
    private void ProcessElimination()
    {
        var sorted = SortParticipants(currentLeagueParticipants);
        Participant loser = sorted[sorted.Count - 1]; // 꼴찌 탈락
        
        survivors.Remove(loser.id);
        eliminatedLog.Add($"#{currentSurvivalRound} 탈락: {loser.id} (점수:{loser.score}, 벅:{loser.buchholz})");
        
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<color=red>▼ 탈락자 현황 ▼</color>");
        for (int i = eliminatedLog.Count - 1; i >= 0; i--) sb.AppendLine(eliminatedLog[i]);
        UIManager.I.TrySetText(UI_GROUP, "EliminatedLogText", sb.ToString());

        if (survivors.Count == 1)
        {
            string winner = survivors[0].ToString();
            UIManager.I.TrySetText(UI_GROUP, "RankingText", $"<size=60> 최종 우승 </size>\n\n<size=50><color=#FFD700>{winner}</color></size>");
        }
    }

    // ★ [수정됨] 스위스 매치업 생성 함수
    private List<Tuple<Participant, Participant>> GenerateSwissPairings(List<Participant> players)
    {
        var sorted = players.OrderByDescending(p => p.score).ThenByDescending(p => p.wins).ThenBy(p => Guid.NewGuid()).ToList();
        var pairs = new List<Tuple<Participant, Participant>>();
        var used = new HashSet<Participant>();

        // 매치업 생성
        for (int i = 0; i < sorted.Count; i++)
        {
            if (used.Contains(sorted[i])) continue;
            Participant p1 = sorted[i];
            Participant p2 = null;

            // 1. 안 만난 상대 우선
            for (int j = i + 1; j < sorted.Count; j++)
            {
                if (used.Contains(sorted[j])) continue;
                if (!p1.opponents.Contains(sorted[j].id)) { p2 = sorted[j]; break; }
            }
            // 2. 정 없으면 아무나
            if (p2 == null)
            {
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (!used.Contains(sorted[j])) { p2 = sorted[j]; break; }
                }
            }

            if (p2 != null) { pairs.Add(new Tuple<Participant, Participant>(p1, p2)); used.Add(p1); used.Add(p2); }
            else { pairs.Add(new Tuple<Participant, Participant>(p1, null)); used.Add(p1); }
        }
        return pairs;
    }

    // ★ [수정됨] 4단계 타이브레이크 룰이 적용된 정렬 함수
    private List<Participant> SortParticipants(List<Participant> list)
    {
        // 원본 리스트를 손상시키지 않기 위해 복사본 생성
        var sortedList = new List<Participant>(list);
        
        // 커스텀 비교자(Comparer)를 사용하여 정렬
        sortedList.Sort(new ParticipantComparer(this));
        
        return sortedList;
    }

    // ★ [신규] 순위 결정 로직을 담은 비교 클래스
    private class ParticipantComparer : IComparer<Participant>
    {
        private EliminationSwissSurvival _system;

        // 생성자
        public ParticipantComparer(EliminationSwissSurvival system)
        {
            _system = system;
        }
        
        // 비교 함수
        public int Compare(Participant x, Participant y)
        {
            if (x == y) return 0;
            if (x == null) return 1; // null은 뒤로
            if (y == null) return -1;

            // 1순위: 승점 (높은 순)
            int scoreCompare = y.score.CompareTo(x.score);
            if (scoreCompare != 0) return scoreCompare;

            // 2순위: 부흐홀츠 (높은 순)
            int buchholzCompare = y.buchholz.CompareTo(x.buchholz);
            if (buchholzCompare != 0) return buchholzCompare;

            // 3순위: 상대 전적 (승자승)
            // x가 y를 이긴 적이 있는지 확인 (또는 그 반대)
            // *주의: 스위스 리그라 서로 안 만났을 수도 있음. 만났을 때만 적용.
            bool xBeatY = _system.DidAgentWinAgainst(x, y);
            bool yBeatX = _system.DidAgentWinAgainst(y, x);

            if (xBeatY && !yBeatX) return -1; // x가 이겼으면 x가 앞섬 (오름차순 정렬에서 -1은 '앞')
            if (yBeatX && !xBeatY) return 1;  // y가 이겼으면 y가 앞섬

            // 4순위: 티어 (낮은 순 = 언더독 우대)
            // ThreatLevel이 낮을수록(Prey=0) 상위 랭크
            int tierX = (int)_system.GetAgentThreatLevel(x.id);
            int tierY = (int)_system.GetAgentThreatLevel(y.id);
            
            int tierCompare = tierX.CompareTo(tierY); // 오름차순 (낮은 게 1등)
            if (tierCompare != 0) return tierCompare;

            // 5순위(비상용): 득실차 (높은 순) -> 이것마저 같으면 이름순
            int gapCompare = y.totalCandleGap.CompareTo(x.totalCandleGap);
            if (gapCompare != 0) return gapCompare;
            
            return x.id.CompareTo(y.id);
        }
    }

    // ★ [신규] 상대 전적 확인용 헬퍼 함수
    // (Participant 클래스에 누가 누구를 이겼는지 기록하는 리스트가 필요함)
    private bool DidAgentWinAgainst(Participant winner, Participant loser)
    {
        // Participant 클래스에 'victims'(내가 이긴 상대 목록) 리스트를 추가해야 가장 정확하지만,
        // 현재 구조상으로는 경기 기록을 뒤져야 함.
        // 성능을 위해 Participant에 'beatenOpponents' 리스트를 추가하는 것을 권장합니다.
        
        // 현재 코드에서는 victims 리스트가 없으므로 아래와 같이 Participant 클래스 수정이 선행되어야 함.
        return winner.beatenOpponents.Contains(loser.id);
    }

    // ★ [수정됨] 랭킹 보드 업데이트 로직 (자이언트 슬레이어 모든 달성자 표시)
    private void UpdateRankingBoard(int round, bool final)
    {
        var sorted = SortParticipants(currentLeagueParticipants);
        StringBuilder sb = new StringBuilder();
        
        // --- 내부 헬퍼 함수 ---
        void AppendAwardLine(List<Participant> winners, string emoji, string title, string valueDesc)
        {
            if (winners == null || winners.Count == 0) return;

            StringBuilder sprites = new StringBuilder();
            foreach (var p in winners) sprites.Append($"<sprite name=\"{p.id}\">   ");

            string names = string.Join(", ", winners.Select(p => p.id.ToString()));
            sb.AppendLine($"{emoji} <size=28>{sprites}</size> <b>{title}:</b> {names} <size=18>({valueDesc})</size>");
        }
        // -----------------------

        // === 최종 순위 뷰 ===
        if (!final)
        {
            // === [진행 중] 요약 뷰 ===
            sb.AppendLine($"<size=36><b>=== Round {round} 요약 ===</b></size>");
            sb.AppendLine("<size=20><i>(상위권 / 주요 이변 / 스페셜 어워드)</i></size>\n");

            // [A] Top 3
            sb.AppendLine("<size=26><color=#FFD700>▼ THE RULERS (Top 3) ▼</color></size>");
            for (int i = 0; i < 3 && i < sorted.Count; i++)
                FormatRankLine(sb, sorted[i], i + 1, "#FFD700");
            sb.AppendLine(""); 

            // [B] Highlights & Awards
            sb.AppendLine("<size=24><color=#98FB98>▼ HIGHLIGHTS & AWARDS ▼</color></size>");
            if (sorted.Count > 0)
            {
                // 1. 학살자
                int maxGap = sorted.Max(p => p.totalCandleGap);
                var butchers = sorted.Where(p => p.totalCandleGap == maxGap).ToList();
                AppendAwardLine(butchers, "", "학살자", $"+{maxGap}");

                // 2. 자이언트 슬레이어 (수정됨: 모든 달성자 표시)
                // 점수가 높은 순으로 정렬하여 표시
                var slayers = sorted.Where(p => p.upsetScore > 0).OrderByDescending(p => p.upsetScore).ToList();
                if (slayers.Count > 0)
                {
                    // 커스텀 포맷 (이름 뒤에 점수)
                    StringBuilder sprites = new StringBuilder();
                    foreach (var p in slayers) sprites.Append($"<sprite name=\"{p.id}\">   ");
                    string names = string.Join(", ", slayers.Select(p => $"{p.id}({p.upsetScore})"));
                    
                    sb.AppendLine($" <size=28>{sprites}</size> <b>거인 살해자:</b> {names} <size=18></size>");
                }

                // 3. 종말의 시계
                int maxSac = sorted.Max(p => p.sacrificeWins);
                if (maxSac > 0)
                {
                    var cultists = sorted.Where(p => p.sacrificeWins == maxSac).ToList();
                    AppendAwardLine(cultists, "", "종말의 시계", $"{maxSac}회");
                }

                // 4. 불사신
                int maxClose = sorted.Max(p => p.closeWins);
                if (maxClose > 0)
                {
                    var zombies = sorted.Where(p => p.closeWins == maxClose).ToList();
                    AppendAwardLine(zombies, "", "불사신", $"{maxClose}회 신승");
                }

                // 5. 킹 슬레이어
                int maxKingKills = sorted.Max(p => p.kingSlayerPoints);
                if (maxKingKills > 0)
                {
                    var kingSlayers = sorted.Where(p => p.kingSlayerPoints == maxKingKills).ToList();
                    AppendAwardLine(kingSlayers, "", "왕위 찬탈자", $"{maxKingKills}회");
                }

                // 6. 늪의 지배자
                int maxDraws = sorted.Max(p => p.draws);
                if (maxDraws > 0)
                {
                    var swampMasters = sorted.Where(p => p.draws == maxDraws).ToList();
                    AppendAwardLine(swampMasters, "", "늪의 지배자", $"{maxDraws}무");
                }

                // 7. 전광석화
                int maxFast = sorted.Max(p => p.fastWins);
                if (maxFast > 0)
                {
                    var blitzers = sorted.Where(p => p.fastWins == maxFast).ToList();
                    AppendAwardLine(blitzers, "", "전광석화", $"{maxFast}회 속공 승");
                }

                // 8. 완벽주의자
                int maxPerfect = sorted.Max(p => p.perfectWins);
                if (maxPerfect > 0)
                {
                    var perfectionists = sorted.Where(p => p.perfectWins == maxPerfect).ToList();
                    AppendAwardLine(perfectionists, "", "완벽주의", $"{maxPerfect}회 퍼펙트");
                }

                // ★ [수정] 고난의 행군 (상위 3명 제외)
                if (sorted.Count > 3)
                {
                    var candidates = sorted.Skip(3).ToList();
                    int maxBuchholz = candidates.Max(p => p.buchholz);
                    if (maxBuchholz > 0)
                    {
                        var hardRoaders = candidates.Where(p => p.buchholz == maxBuchholz).ToList();
                        AppendAwardLine(hardRoaders, "", "고난의 행군", $"B:{maxBuchholz}");
                    }
                }

                // 10. 파죽지세
                int maxStreak = sorted.Max(p => p.maxStreak);
                if (maxStreak >= 3)
                {
                    var streakers = sorted.Where(p => p.maxStreak == maxStreak).ToList();
                    AppendAwardLine(streakers, "", "파죽지세", $"{maxStreak}연승");
                }

                // ★ [추가] 11. 평화주의자
                int maxCoop = sorted.Max(p => p.coopCount);
                if (maxCoop > 0)
                {
                    var pacifists = sorted.Where(p => p.coopCount == maxCoop).ToList();
                    AppendAwardLine(pacifists, "", "평화의 사도", $"{maxCoop}회 협력");
                }

                // ★ [추가] 12. 우성인자
                int maxBonus = sorted.Max(p => p.phaseBonusCount);
                if (maxBonus > 0)
                {
                    var dominants = sorted.Where(p => p.phaseBonusCount == maxBonus).ToList();
                    AppendAwardLine(dominants, "", "우성인자", $"{maxBonus}회 보너스");
                }
            }
            sb.AppendLine("");

            // [C] Danger Zone
            sb.AppendLine("<size=26><color=#FF4500>▼ DANGER ZONE (Bottom 3) ▼</color></size>");
            int startIdx = Mathf.Max(3, sorted.Count - 3); 
            for (int i = startIdx; i < sorted.Count; i++)
                FormatRankLine(sb, sorted[i], i + 1, "#FF4500");
        }
        else
        {
            // === [최종 결과] 전체 순위 상세 뷰 ===
            string title = (survivors.Count <= 1) ? "최종 우승자" : $"{currentSurvivalRound}회차 최종 순위";
            sb.AppendLine($"<size=40><b>=== {title} ===</b></size>");
            sb.AppendLine("<size=18>우선순위: 승점 > 부흐홀츠 > 승자승 > 티어(Low) > 득실</size>\n");

            for (int i = 0; i < sorted.Count; i++)
            {
                var p = sorted[i];
                int rank = i + 1;
                
                string color = "white";
                
                if (rank == 1) { color = "#FFD700"; }
                else if (rank == 2) { color = "#C0C0C0"; }
                else if (rank == 3) { color = "#CD7F32"; }
                else if (i >= sorted.Count - 3 && sorted.Count > 3) { color = "#FF4500"; } 

                string spriteTag = $"<sprite name=\"{p.id}\">   ";

                sb.Append($"<size=28>{spriteTag}</size> <color={color}><size=26><b>{rank}위. {p.id}</b></size></color>");
                sb.Append($" <size=22>: {p.score}점</size>");
                sb.Append($" <size=18>(승:{p.wins}, B:{p.buchholz})</size>");

                if (i > 0)
                {
                    var prev = sorted[i - 1];
                    if (prev.score == p.score)
                    {
                        string reason = GetTieBreakerReason(prev, p);
                        sb.Append($" <size=16><color=white>{reason}</color></size>");
                    }
                }
                sb.AppendLine();
            }
        }

        // 시스템 코멘트
        string[] comments = {
            "\"데이터 분석 결과, 이번 라운드의 이변 확률은 14%였습니다.\"",
            "\"하위권의 생존 본능이 감지됩니다.\"",
            "\"상위권 독주 체제가 굳어지고 있습니다.\"",
            "\"누군가의 탈락이 머지않았습니다.\"",
            "\"운도 실력의 일부입니다.\"",
            "\"승점 그래프가 비선형적 패턴을 보입니다.\"",
            "\"배신의 대가는 결국 치르게 될 것입니다.\"",
            "\"재해는 누구에게나 공평하게 잔혹합니다.\"",
            "\"희생(Sacrifice)은 가장 위험한 도박입니다.\"",
            "\"투자(Investment)의 결실을 맺을 타이밍입니다.\"",
            "\"혼돈(Chaos) 속에서 기회를 찾는 자가 승리합니다.\"",
            "\"시스템이 다음 처형 대상을 계산 중입니다.\"",
            "\"단 한 번의 실수가 치명적인 결과를 초래합니다.\"",
            "\"당신의 '최애' 참가자는 안전합니까?\"",
            "\"역사는 승리한 자의 기록일 뿐입니다.\"",
            "\"생존이 곧 정의입니다.\""
        };
        
        // 무작위 선택
        if (roundHighlights.Count > 0 && !final) sb.AppendLine($"\n<color=black><i>AI해설위원\n → {roundHighlights[UnityEngine.Random.Range(0, roundHighlights.Count)]}</i></color>");
        else sb.AppendLine($"\n<color=black><i>AI해설위원: {comments[UnityEngine.Random.Range(0, comments.Length)]}</i></color>");

        UIManager.I.TrySetText(UI_GROUP, "RankingText", sb.ToString());
    }

    // ★ [신규] 동점자 순위 결정 사유 반환 헬퍼
    private string GetTieBreakerReason(Participant winner, Participant loser)
    {
        // 1. 부흐홀츠
        if (winner.buchholz != loser.buchholz) 
            return $"(부흐홀츠 우위: {winner.buchholz} vs {loser.buchholz})";
        
        // 2. 승자승
        if (DidAgentWinAgainst(winner, loser)) return "(상대 전적 승리)";
        if (DidAgentWinAgainst(loser, winner)) return "(상대 전적 패배??)"; // 이론상 발생 안 함

        // 3. 티어 (낮을수록 상위)
        int t1 = (int)GetAgentThreatLevel(winner.id);
        int t2 = (int)GetAgentThreatLevel(loser.id);
        if (t1 != t2) return $"(티어 언더독 우대)";

        // 4. 득실차
        if (winner.totalCandleGap != loser.totalCandleGap) 
            return $"(득실차 우위: {winner.totalCandleGap})";

        return "(순위 추첨)";
    }

    // AgentManager에서 티어 정보를 가져오는 헬퍼
    private ThreatLevel GetAgentThreatLevel(AgentList id)
    {
        if (AgentManager.I == null) return ThreatLevel.Unstable;
        var data = AgentManager.I.GetAgentData(id);
        return data != null ? data.threatLevel : ThreatLevel.Unstable;
    }

    // 헬퍼 메서드 업데이트: Top 3 및 Danger Zone에도 스프라이트 적용
    private void FormatRankLine(StringBuilder sb, Participant p, int rank, string colorHex)
    {
        
        // ★ [추가] 스프라이트 태그
        string spriteTag = $"<size=28><sprite name=\"{p.id}\"></size>   ";

        string info = $"<size=20>: {p.score}점 (승:{p.wins})</size>";
        
        sb.AppendLine($"{spriteTag} <color={colorHex}><size=26><b>{rank}위. {p.id}</b></size></color> {info}");
    }

    private IEnumerator ProcessEliminationSequence()
    {
        // 1. 꼴찌 및 하위권 계산
        var sorted = SortParticipants(currentLeagueParticipants);
        Participant loser = sorted[sorted.Count - 1]; 
        
        // 하위권 후보군 (꼴찌 포함 3~5명)
        int candidateCount = Mathf.Min(sorted.Count, 5); 
        var candidates = sorted.GetRange(sorted.Count - candidateCount, candidateCount);

        // 2. 긴장감 조성 텍스트
        string[] suspenseTexts = {
            "모든 매치 종료. 데이터 집계 중...",
            "생존 점수 산출 완료.",
            "<color=red>⚠️ 경고: 하위권 참가자 식별됨 ⚠️</color>",
            "시스템이 탈락 대상을 조준합니다..."
        };

        foreach (var txt in suspenseTexts)
        {
            UIManager.I.TrySetText(UI_GROUP, "StatusText", txt);
            yield return new WaitForSeconds(0.6f);
        }

        // 3. ★ [신규] 데스 룰렛 연출 (Death Roulette)
        // 하위권 후보들의 이름을 무작위로 빠르게 보여주다가 loser에게 멈춤
        if (sorted.Count >= 2)
        {
            float delay = 0.05f; // 초기 회전 속도 (빠름)
            int spins = 25;      // 총 회전 횟수
            
            for (int i = 0; i < spins; i++)
            {
                // 랜덤하게 후보 중 한 명의 이름을 표시
                var randomCandidate = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                
                string rouletteVisual = $"<size=40><color=red>▼ TARGETING ▼</color></size>\n\n" +
                                        $"<size=60><b>{randomCandidate.id}</b></size>\n" +
                                        $"<size=20>생존 확률 계산 중...</size>";

                UIManager.I.TrySetText(UI_GROUP, "RankingText", rouletteVisual);

                // 틱 소리
                if (audioSource && sfxRouletteTick) audioSource.PlayOneShot(sfxRouletteTick);

                yield return new WaitForSeconds(delay);

                // 점점 느려지게 (긴장감 고조)
                delay *= 1.1f; 
            }
        }

        // 4. 타겟 락온 (확정) & 임팩트
        if (audioSource && sfxTargetLocked) audioSource.PlayOneShot(sfxTargetLocked);
        if (audioSource && eliminationSfx) audioSource.PlayOneShot(eliminationSfx); // 쾅!
        
        // 붉은 패널 흔들기
        StartCoroutine(ShakeUI(0.8f, 25f)); 

        string fatalMsg = $"<size=50><color=red><b>[ TARGET ELIMINATED ]</b></color></size>\n\n" +
                        $"<size=70><b>{loser.id}</b></size>\n" +
                        $"<size=25>승점: {loser.score} | 득실: {loser.totalCandleGap}</size>";

        UIManager.I.TrySetText(UI_GROUP, "StatusText", "탈락자 확정."); 
        UIManager.I.TrySetText(UI_GROUP, "RankingText", fatalMsg); 

        // 5. 데이터 삭제 및 로그 갱신
        survivors.Remove(loser.id);
        eliminatedLog.Add($"#{currentSurvivalRound} Round 탈락: {loser.id}");

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<color=red>▼ 탈락자 현황 ▼</color>");
        for (int i = eliminatedLog.Count - 1; i >= 0; i--) sb.AppendLine(eliminatedLog[i]);
        UIManager.I.TrySetText(UI_GROUP, "EliminatedLogText", sb.ToString());

        // 6. 여운 (붉은 패널 유지 시간)
        yield return new WaitForSeconds(3.5f);

        if (eliminationAlertPanel != null) eliminationAlertPanel.SetActive(false);

        // 7. 마무리 (다음 라운드 준비 or 우승자)
        if (survivors.Count > 1)
        {
            UpdateRankingBoard(0, true); 
            
            // 생존자 수에 따른 멘트 변경 (신호등 연출)
            string statusColor = survivors.Count <= 5 ? "red" : (survivors.Count <= 10 ? "orange" : "white");
            UIManager.I.TrySetText(UI_GROUP, "StatusText", $"<color={statusColor}>생존자 {survivors.Count}명. 다음 라운드 준비 완료.</color>");
        }
        else
        {
            string winner = survivors[0].ToString();
            UIManager.I.TrySetText(UI_GROUP, "RankingText", 
                $"<size=60><color=#FFD700>🏆 최종 생존자 🏆</color></size>\n\n" +
                $"<size=70><b>{winner}</b></size>\n" +
                $"<size=30>수고하셨습니다.</size>");
        }
    }
}