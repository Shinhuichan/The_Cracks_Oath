using System;
using UnityEngine;

public static class GetEnum
{
    public static int Count<TEnum>() where TEnum : Enum // TEnum이 Enum 타입이어야 한다고 제약 (C# 7.3 이상)
    {
        // Enum.GetValues(typeof(TEnum))는 TEnum 타입의 모든 상수 값들을 Array로 반환해.
        Array enumValues = Enum.GetValues(typeof(TEnum));
        return enumValues.Length;
    }
    public static int Index<TEnum>(TEnum targetValue) where TEnum : Enum
    {
        Array enumValues = Enum.GetValues(typeof(TEnum));
        
        for (int i = 0; i < enumValues.Length; i++)
        {
            if (enumValues.GetValue(i).Equals(targetValue))
            {
                return i;
            }
        }
        return -1;
    }
}
