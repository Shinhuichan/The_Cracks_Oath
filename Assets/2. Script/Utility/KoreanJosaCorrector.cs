using System.Text;
using System.Text.RegularExpressions;

public static class KoreanJosaCorrector
{
    /// <summary>
    /// 문자열 내의 조사 포맷을 앞 글자의 받침 유무에 따라 올바르게 수정합니다.
    /// 예: "백무적이(가)" -> "백무적이", "서유리을(를)" -> "서유리를"
    /// </summary>
    public static string CorrectJosa(this string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // 정규식으로 처리할 조사 패턴들 (특수문자 이스케이프 처리)
        // (이)랑, (이)다, (이)야, (이)여, (이)며, (이)나, (이)란, (이)든가, (이)든지, (이)나마, (이)야말로, (으)로부터 추가
        string pattern = @"(.)(이\(가\)|을\(를\)|은\(는\)|와\(과\)|" + 
                         @"\((이)\)랑|\((이)\)다|\((이)\)야|\((이)\)여|\((이)\)며|\((이)\)나|\((이)\)란|" +
                         @"\((이)\)든가|\((이)\)든지|\((이)\)나마|\((이)\)야말로|\((으)\)로부터)";

        return Regex.Replace(text, pattern, (match) =>
        {
            char prevChar = match.Groups[1].Value[0];
            string josaType = match.Groups[2].Value; // 이(가), 을(를) 등

            // 만약 Groups[2]가 비어있다면, 뒤쪽 그룹에서 매칭된 것임 (예: (이)랑 -> '이랑' 혹은 '랑')
            // 정규식 구조상 괄호가 많아져서 그룹 인덱스가 복잡할 수 있으므로, 
            // match.Value 전체에서 앞글자를 뺀 나머지 문자열을 조사 원형으로 보고 처리합니다.
            string originalJosa = match.Value.Substring(1); 

            // 한글이 아니면 원본 그대로 반환
            if (prevChar < 0xAC00 || prevChar > 0xD7A3) return match.Value;

            // 받침 유무 확인 (글자 - 0xAC00) % 28 > 0 이면 받침 있음
            bool hasJongsung = (prevChar - 0xAC00) % 28 > 0;

            return prevChar + SelectJosa(originalJosa, hasJongsung);
        });
    }

    private static string SelectJosa(string josaType, bool hasJongsung)
    {
        switch (josaType)
        {
            // 기본 조사
            case "이(가)": return hasJongsung ? "이" : "가";
            case "을(를)": return hasJongsung ? "을" : "를";
            case "은(는)": return hasJongsung ? "은" : "는";
            case "와(과)": return hasJongsung ? "과" : "와"; // 받침 O: 과, 받침 X: 와

            // '(이)' 계열 조사 (받침 있으면 '이' 붙음)
            case "(이)랑": return hasJongsung ? "이랑" : "랑";
            case "(이)다": return hasJongsung ? "이다" : "다";
            case "(이)야": return hasJongsung ? "이야" : "야"; // ex: 너야 / 사람이야
            case "(이)여": return hasJongsung ? "이여" : "여";
            case "(이)며": return hasJongsung ? "이며" : "며";
            case "(이)나": return hasJongsung ? "이나" : "나";
            case "(이)란": return hasJongsung ? "이란" : "란";
            case "(이)든가": return hasJongsung ? "이든가" : "든가";
            case "(이)든지": return hasJongsung ? "이든지" : "든지";
            case "(이)나마": return hasJongsung ? "이나마" : "나마";
            case "(이)야말로": return hasJongsung ? "이야말로" : "야말로";

            // '(으)' 계열 조사 (받침 있으면 '으' 붙음, 단 'ㄹ' 받침은 예외)
            case "(으)로부터": 
                // ㄹ 받침 예외 처리 필요? (일반적으로 ㄹ받침 뒤에는 '로부터'가 옴)
                // 여기서는 단순 받침 유무로만 구분 (ㄹ받침 포함 모든 받침: 으로부터)
                // 만약 'ㄹ' 받침을 구분하려면 CorrectJosa에서 종성 인덱스를 넘겨받아야 함.
                // 현재 로직상: 받침 O -> 으로부터, 받침 X -> 로부터
                return hasJongsung ? "으로부터" : "로부터";

            default: return josaType; // 매칭 안 되면 원본 반환
        }
    }
}