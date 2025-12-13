using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class InvestorRankRow : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI txtRank;
    public TextMeshProUGUI txtName;
    public TextMeshProUGUI txtAsset; // 총 자산
    public Image bgImage; // 홀수/짝수 줄 배경색 구분용

    public void SetData(int rank, string name, long totalAsset, bool isPlayer, bool isOdd)
    {
        txtRank.text = $"{rank}위";
        txtName.text = name;
        txtAsset.text = $"{NumberUtils.ToCurrencyString(totalAsset)} 원";

        // 🎨 텍스트 색상 설정
        Color textColor = Color.white;

        if (isPlayer)
        {
            textColor = Color.yellow; // 플레이어는 항상 노란색
            // 플레이어 이름 강조 (굵게 등)
            txtName.text = $"<b>{name}</b>"; 
        }
        else
        {
            if (rank == 1) textColor = new Color(1f, 0.84f, 0f); // 금색
            else if (rank == 2) textColor = new Color(0.75f, 0.75f, 0.75f); // 은색
            else if (rank == 3) textColor = new Color(0.8f, 0.5f, 0.2f); // 동색
        }

        txtRank.color = textColor;
        txtName.color = textColor;
        txtAsset.color = textColor;

        // 배경색 (홀/짝 구분)
        if (bgImage != null)
        {
            bgImage.color = isOdd ? new Color(0, 0, 0, 0.5f) : new Color(0, 0, 0, 0.3f);
        }
    }
}