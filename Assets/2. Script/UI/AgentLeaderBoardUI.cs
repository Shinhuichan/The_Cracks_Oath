using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GameCore; // AgentData, AgentManager가 있는 네임스페이스

public class AgentLeaderboardUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text leaderboardText; // 표를 표시할 TMP

    [Header("Settings")]
    [SerializeField] private float updateInterval = 1.0f; // 갱신 주기 (초)

    private void Start()
    {
        if (leaderboardText == null)
        {
            Debug.LogError("[AgentLeaderboardUI] Leaderboard TMP is not assigned!");
            return;
        }

        StartCoroutine(UpdateRoutine());
    }

    private IEnumerator UpdateRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(updateInterval);

        while (true)
        {
            RefreshLeaderboard();
            yield return wait;
        }
    }

    private void RefreshLeaderboard()
    {
        if (AgentManager.I == null || AgentManager.I.currentAgent == null) return;

        // 1. ELO 기준 내림차순 정렬
        var sortedList = AgentManager.I.currentAgent
            .Where(a => a != null)
            .OrderByDescending(a => a.elo)
            .ToList();

        StringBuilder sb = new StringBuilder();

        // 헤더 (선택 사항)
        sb.AppendLine("<size=120%><b>📊 실시간 랭킹</b></size>");
        sb.AppendLine("<color=#888888><size=80%>-순위-      -참가자-                                 -티어-                            -ELO- </size></color>");
        sb.AppendLine("-------------------------------------------------------");

        for (int i = 0; i < sortedList.Count; i++)
        {
            var agent = sortedList[i];
            int rank = i + 1;

            // 2. 랭킹 색상 지정
            string rankColor = "#FFFFFF"; // 기본 흰색
            string prefix = "";
            
            if (rank == 1) { rankColor = "#FFD700"; prefix = ""; }      // 금
            else if (rank == 2) { rankColor = "#C0C0C0"; prefix = ""; } // 은
            else if (rank == 3) { rankColor = "#CD7F32"; prefix = ""; } // 동
            else if (rank >= sortedList.Count - 2) { rankColor = "#FF4500"; } // 하위권 (빨강)

            // 3. 아이콘 (Sprite 태그 사용)
            // AgentList Enum 이름과 스프라이트 에셋 이름이 같아야 함
            string iconTag = $"<sprite name=\"{agent.agentName}\">  ";

            // 4. 티어 색상 지정
            string tierColor = GetTierColor(agent.threatLevel);
            string tierStr = agent.threatLevel.ToString();

            // 5. 한 줄 포맷팅 (표 형식 느낌)
            // <pos> 태그를 사용하여 열 정렬을 맞춥니다. (폰트에 따라 픽셀 값 조정 필요할 수 있음)
            // 순위(0px) | 아이콘+이름(80px) | 티어(350px) | ELO(550px)
            sb.AppendLine(
                $"<color={rankColor}><b>{rank}위</b></color>" +
                $"<pos=100>{iconTag} <b>{agent.agentName}</b></pos>" +
                $"<pos=400><color={tierColor}><size=80%>{tierStr}</size></color></pos>" +
                $"<pos=650><color=black>{agent.elo:F3} ELO</color></pos>"
            );
        }

        leaderboardText.text = sb.ToString();
    }

    // 티어별 색상 반환
    private string GetTierColor(ThreatLevel tier)
    {
        return tier switch
        {
            ThreatLevel.Absolute => "#FF0000",      // 빨강 (최상위)
            ThreatLevel.Grandmasters => "#FF4500",  // 주황
            ThreatLevel.Masters => "#9400D3",       // 보라
            ThreatLevel.Challengers => "#4169E1",   // 파랑
            ThreatLevel.Variables => "#32CD32",     // 라임
            ThreatLevel.Unstable => "#808000",      // 올리브
            ThreatLevel.Prey => "#808080",          // 회색 (최하위)
            _ => "#FFFFFF"
        };
    }
}