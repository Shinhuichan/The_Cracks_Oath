using UnityEngine;

public static class NumberUtils
{
    // 단위 배열 확장 (만, 억, 조, 경, 해, 자)
    private static readonly string[] UnitNames = new string[] { "", "만", "억", "조", "경", "해", "자" };

    // 입력 타입을 long에서 decimal로 변경하여 '해', '자' 단위까지 지원 (long은 자동 변환됨)
    // 사용법: NumberUtils.ToCurrencyString(150000000); // "1억 5000만"
    public static string ToCurrencyString(decimal number)
    {
        if (number == 0) return "0";

        string sign = number < 0 ? "-" : "";
        decimal absNumber = System.Math.Abs(number);

        int unitIndex = 0;
        decimal temp = absNumber;

        // 10000으로 나누면서 가장 큰 단위 찾기
        while (temp >= 10000 && unitIndex < UnitNames.Length - 1)
        {
            temp /= 10000;
            unitIndex++;
        }

        // temp는 현재 가장 큰 단위의 값 (예: 1.5조)
        // 정수부(1)와 소수부(0.5 -> 5000)를 분리
        decimal head = System.Math.Floor(temp);
        decimal remainder = (temp - head) * 10000;
        decimal nextHead = System.Math.Floor(remainder);

        // 가장 큰 단위 출력 (예: 1조)
        string result = $"{sign}{head:N0}{UnitNames[unitIndex]}";

        // 하위 단위가 있으면 추가 출력 (예: 5000억)
        if (nextHead > 0)
        {
            result += $" {nextHead:N0}{UnitNames[unitIndex - 1]}";
        }

        return result;
    }

    // 수익률 색상 태그 포함 문자열 반환 (기존 유지)
    public static string GetColoredPercent(float percent)
    {
        if (percent > 0) return $"<color=#FF4500>+{percent:F2}%</color>"; // 빨강 (상승)
        if (percent < 0) return $"<color=#1E90FF>{percent:F2}%</color>"; // 파랑 (하락)
        return $"<color=white>{percent:F2}%</color>";
    }
}