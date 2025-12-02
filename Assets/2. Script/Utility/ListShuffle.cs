using System.Collections.Generic;

public static class ListShuffle
{
    // Thread-safe하지 않거나 시드 문제가 있을 수 있으므로, 호출 때마다 랜덤 인스턴스 사용 권장
    // 혹은 UnityEngine.Random을 명시적으로 사용
    public static void Shuffle<T>(this IList<T> list)
    {
        // System.Random 대신 UnityEngine.Random 사용 (메인 스레드인 경우)
        // 또는 Guid 해시로 시드 생성하여 매번 다르게 섞임 보장
        int seed = System.Guid.NewGuid().GetHashCode(); 
        var rng = new System.Random(seed);

        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }
}