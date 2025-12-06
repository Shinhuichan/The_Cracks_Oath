using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class CompanyRankRow : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI txtRank;
    public TextMeshProUGUI txtName;
    public TextMeshProUGUI txtMarketCap; // 시가총액
    public Image bgImage; // 배경 이미지 (홀수/짝수 줄 색상 구분용)

    public void SetData(int rank, string name, string symbol, long marketCap, bool isOdd)
    {
        txtRank.text = $"{rank}위";
        txtName.text = $"{name} <size=70%><color=#666666>({symbol})</color></size>";
        txtMarketCap.text = $"{marketCap:N0} 원";

        // 1~3위 색상 강조
        if (rank == 1) txtRank.color = new Color(1f, 0.8f, 0f); // 금색
        else if (rank == 2) txtRank.color = new Color(0.75f, 0.75f, 0.75f); // 은색
        else if (rank == 3) txtRank.color = new Color(0.8f, 0.5f, 0.2f); // 동색
        else txtRank.color = Color.white;

        // 가독성을 위해 홀수/짝수 줄 배경색 다르게 (선택 사항)
        if (bgImage != null)
        {
            bgImage.color = isOdd ? new Color(0, 0, 0, 0.5f) : new Color(0, 0, 0, 0.3f);
        }
    }
}