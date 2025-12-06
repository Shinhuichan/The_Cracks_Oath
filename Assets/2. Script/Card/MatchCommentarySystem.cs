using UnityEngine;
using System.Text;
using System.Collections.Generic;
using GameCore;

public class MatchCommentarySystem : SingletonBehaviour<MatchCommentarySystem>
{
    // 싱글톤 설정
    protected override bool IsDontDestroy() => true;

    // 해설 템플릿 리스트 (상황별 다양한 멘트)
    private  readonly List<string> _stompQuotes = new()
    {
        "\"{0}의 무자비한 학살극이었습니다. {1}는(은) 숨 쉴 틈조차 없었군요.\"",
        "\"체급 차이가 너무 명확했습니다. {0}의 압도적인 퍼포먼스 앞에 {1}의 전략은 무의미했습니다.\"",
        "\"일방적인 경기였습니다. 데이터가 증명하듯 {0}이(가) {1}과의 매치를 완전히 지배했습니다.\"",
        "\"마치 어린아이와 어른의 싸움을 보는 듯했습니다. {1}은 힘도 못 쓰고 {0}의 완벽한 승리로 종료됩니다.\""
    };

    private readonly List<string> _closeQuotes = new()
    {
        "\"손에 땀을 쥐게 하는 명승부였습니다! {0}와(과) {1}, 누구도 쉽게 물러서지 않았습니다.\"",
        "\"단 한 번의 실수가 승패를 갈랐습니다. {0}와(과) {1}의 기량은 종이 한 장 차이였습니다.\"",
        "\"치열한 공방전 끝에 {0}이(가) {1}을(를) 상대로 간발의 차이로 승기를 잡았습니다.\"",
        "\"{0}과 {1}의 매치업은 데이터상으로도 우열을 가리기 힘든 난타전이었습니다.\""
    };

    private readonly List<string> _upsetQuotes = new()
    {
        "\"대이변입니다! {3} 티어의 {0}이(가) 상위 랭커 {1}을(를) 무너뜨렸습니다!\"",
        "\"다윗이 골리앗을 잡았습니다! {0}의 과감한 수가 {1}의 방심을 찔렀습니다.\"",
        "\"모두의 예상을 뒤엎고 {0}이(가) {1}을(를) 상대로 승리를 가져갑니다. 언더독의 반란이 시작됩니다!\"",
        "\"랭킹은 숫자에 불과하다는 것을 {0}이(가) {1}을(를) 상대로 증명해냅니다.\""
    };

    private readonly List<string> _sacrificeQuotes = new()
    {
        "\"{0}의 위험한 도박이 적중했습니다. '희생(Sacrifice)' 전략이 {1}의 허를 찔렀습니다.\"",
        "\"소름 끼치는 의식이 완성되었습니다. 특수 승리를 통해 {0}이(가) {1}과의 게임을 지배했습니다.\"",
        "\"{1}이(가) 대응하기도 전에 {0}은(는) 파멸의 카운트다운을 끝마쳤습니다.\""
    };

    private readonly List<string> _pacifistQuotes = new()
    {
        "\"총성 없는 전쟁이었습니다. {0}은(는) 협력과 신뢰를 이용해 {1}을(를) 제압했습니다.\"",
        "\"두 참가자 모두 신중했습니다. 하지만 {1}에 비해 {0}의 실력이 조금 더 앞섰던 것 같군요.\"",
        "\"장기전 양상에서 {1}보다 {0}의 운영 능력이 빛을 발했습니다.\""
    };

    private readonly List<string> _blitzQuotes = new()
    {
        "\"눈 깜짝할 새에 경기가 끝났습니다! {0}의 속공에 {1}은(는) 대응조차 하지 못했습니다.\"",
        "\"전광석화 같은 승리! {0}은(는) {1}에게 장기전을 허용하지 않았습니다.\"",
        "\"{1}에게 초반 라운드에 모든 것을 쏟아부은 {0}의 전략이 완벽히 먹혀들었습니다.\""
    };

    private readonly List<string> _defenseQuotes = new()
    {
        "\"{0}의 방어는 난공불락이었습니다. {1}의 모든 공격이 무위로 돌아갔습니다.\"",
        "\"완벽한 수비가 최고의 공격임을 {0}이(가) {1}을(를) 통해 증명합니다. (Perfect Win 다수 발생)\"",
        "\"상대의 공격을 모두 흘려내며 {0}이(가) {1}에게 실리적인 승리를 챙깁니다.\""
    };

    /// <summary>
    /// 매치 결과와 참가자 정보를 분석하여 다채로운 해설 멘트를 생성합니다.
    /// </summary>
    public string GenerateCommentary(
        AgentList p1, AgentList p2, 
        H2HMatchLoop.BatchResult result, 
        ThreatLevel t1, ThreatLevel t2, 
        double elo1, double elo2)
    {
        int totalGames = result.p1Wins + result.p2Wins + result.draws;
        if (totalGames == 0) return "\"데이터 부족으로 분석이 불가능합니다.\"";

        // 1. 기본 승자/패자 결정
        bool p1Won = result.p1Wins > result.p2Wins;
        AgentList winner = p1Won ? p1 : p2;
        AgentList loser = p1Won ? p2 : p1;
        
        int wWins = p1Won ? result.p1Wins : result.p2Wins;
        int lWins = p1Won ? result.p2Wins : result.p1Wins;
        
        float winRate = (float)wWins / totalGames * 100f;
        float diffRate = (float)(wWins - lWins) / totalGames * 100f;

        // 승자의 세부 스탯 (분석 근거)
        int wSac = p1Won ? result.p1SacrificeWins : result.p2SacrificeWins;
        int wPerf = p1Won ? result.p1PerfectWins : result.p2PerfectWins;
        int wFast = p1Won ? result.p1FastWins : result.p2FastWins;
        int wCoop = p1Won ? result.p1CoopCount : result.p2CoopCount;
        int wPhase = p1Won ? result.p1PhaseBonusCount : result.p2PhaseBonusCount;

        ThreatLevel winnerTier = p1Won ? t1 : t2;
        ThreatLevel loserTier = p1Won ? t2 : t1;

        // 2. 시나리오 우선순위 판별
        // 우선순위: 특수 승리(Sacrifice) > 압살(Stomp) > 이변(Upset) > 특수 전략(Blitz/Perfect) > 접전(Close) > 정치(Pacifist) > 일반

        // [Case 1: Sacrifice (특수 승리)]
        // 승리 중 20% 이상이 특수 승리였다면
        if (wSac > wWins * 0.2f)
        {
            return string.Format(Pick(_sacrificeQuotes), winner, loser);
        }

        // [Case 2: Stomp (학살)]
        // 승률 85% 이상 압도적 차이
        if (winRate >= 85f)
        {
            return string.Format(Pick(_stompQuotes), winner, loser) + 
                   $" <color=#888888>(승률 {winRate:F1}%)</color>";
        }

        // [Case 3: Upset (이변)]
        // 승자의 티어가 패자보다 2단계 이상 낮거나, ELO가 300점 이상 낮은데 이김
        int tierGap = (int)loserTier - (int)winnerTier;
        double eloGap = (p1Won ? elo2 : elo1) - (p1Won ? elo1 : elo2); // 패자ELO - 승자ELO

        if (tierGap >= 2 || eloGap >= 300)
        {
            string baseComment = string.Format(Pick(_upsetQuotes), winner, loser, GetTierName(loserTier), GetTierName(winnerTier));
            
            // 분석 추가
            if (result.draws > totalGames * 0.3f)
                return baseComment + " 끈질긴 장기전 유도가 승리의 열쇠였습니다.".CorrectJosa();
            else if (wFast > wWins * 0.3f)
                return baseComment + " 상대가 방어 태세를 갖추기 전에 끝내버렸습니다.".CorrectJosa();
            else
                return baseComment + " 상성 관계를 극복한 전략적 승리입니다.".CorrectJosa();
        }

        // [Case 4: Blitz (속전속결)]
        // 승리 중 40% 이상이 5라운드 이내
        if (wFast > wWins * 0.4f)
        {
            return string.Format(Pick(_blitzQuotes), winner, loser).CorrectJosa();
        }

        // [Case 5: Perfect (철벽)]
        // 승리 중 30% 이상이 퍼펙트
        if (wPerf > wWins * 0.3f)
        {
            return string.Format(Pick(_defenseQuotes), winner, loser).CorrectJosa();
        }

        // [Case 6: Close Match (접전)]
        // 승리 수 차이가 전체의 10% 미만일 때
        if (diffRate < 10f)
        {
            string baseComment = string.Format(Pick(_closeQuotes), winner, loser);
            if (result.draws > totalGames * 0.2f)
                baseComment += $" <color=#888888>(무승부 {result.draws}회)</color>";
            return baseComment.CorrectJosa();
        }

        // [Case 7: Pacifist/Political (정치질)]
        // 협력 카드가 전체 판수의 50% 이상 나왔거나, 우성인자(양초 보너스)를 많이 챙겼을 때
        if (wCoop > totalGames * 0.5f || wPhase > totalGames * 0.6f)
        {
            return string.Format(Pick(_pacifistQuotes), winner, loser).CorrectJosa();
        }

        // [Case 8: Generic (일반)]
        // 특별한 특징이 없을 때 티어/ELO 기반 분석
        StringBuilder sb = new StringBuilder();
        sb.Append($"\"{winner}이(가) {loser}를 상대로 ");
        
        if (winnerTier > loserTier) 
            sb.Append("체급 차이를 증명하며 ");
        else 
            sb.Append("안정적인 운영을 보여주며 ");

        sb.Append($"승리를 가져갑니다. (승률 {winRate:F0}%)");
        
        // 추가 코멘트
        if (result.draws > 0) 
            sb.Append($" 무승부도 {result.draws}회 발생했습니다.\"");
        else
            sb.Append("\"");

        return sb.ToString().CorrectJosa();
    }

    // 리스트에서 랜덤 문자열 하나 뽑기
     string Pick(List<string> list)
    {
        if (list == null || list.Count == 0) return "";
        return list[UnityEngine.Random.Range(0, list.Count)];
    }

     string GetTierName(ThreatLevel t)
    {
        return t.ToString();
    }
}