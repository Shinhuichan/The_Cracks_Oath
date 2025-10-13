using UnityEngine;

public static class GridMath
{
    /// <summary>
    /// 격자 좌표 간 제곱 유클리드 거리(제곱근 연산 없음).
    /// Vector2Int Distance^2 = (dx*dx + dy*dy)
    /// </summary>
    public static int Dist2(Vector2Int a, Vector2Int b)
    {
        int dx = a.x - b.x;
        int dy = a.y - b.y;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// 확장 메서드 버전: a.SquaredDistanceTo(b)
    /// </summary>
    public static int SquaredDistanceTo(this Vector2Int a, Vector2Int b)
    {
        int dx = a.x - b.x;
        int dy = a.y - b.y;
        return dx * dx + dy * dy;
    }
}