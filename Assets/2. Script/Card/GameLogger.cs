using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using GameCore;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

public static class GameLogger
{
    // ---- 설정 ----
    public static bool Enabled = true;
    public static string SessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
    public static string RootDir {
        get {
#if UNITY_5_3_OR_NEWER
            return Path.Combine(Application.persistentDataPath, "TheCracksOath");
#else
            return Path.Combine(Directory.GetCurrentDirectory(), "TheCracksOath");
#endif
        }
    }

    // 파일 경로
    static string MatchesCsv => Path.Combine(RootDir, $"{SessionId}_matches.csv");
    static string RoundsCsv  => Path.Combine(RootDir, $"{SessionId}_rounds.csv");

    // 버퍼
    static readonly List<string> _matchBuf = new();
    static readonly List<string> _roundBuf = new();
    const int FlushEvery = 256;
    static bool _matchHeader, _roundHeader;

    public static void Init(string sessionId = null, bool enabled = true)
    {
        if (sessionId != null) SessionId = sessionId;
        Enabled = enabled;
        Directory.CreateDirectory(RootDir);
        _matchHeader = File.Exists(MatchesCsv);
        _roundHeader = File.Exists(RoundsCsv);
    }

    // ===== 스키마 =====
    public struct MatchStart
    {
        public string matchId;   // "001" 형식 권장
        public string p1;        // 참가자명
        public string p2;        // 참가자명
        public string mode;      // "Quick/Standard/Extend"
    }

    public struct MatchEnd
    {
        public string matchId;
        public int totalRounds;  // 이번 매치 실제 진행 라운드 수
        public string winner;    // "P1" or "P2" or "Draw"
        public string loser;     // "P1" or "P2" or ""(무승부)
    }

    public struct RoundRow
    {
        public string matchId;
        public int round;            // 1-based

        public string disaster;      // 없으면 ""
        public bool swappedByStorm;
        public List<CardType> p1Hand;
        public List<CardType> p2Hand;
        public CardType p1Card;
        public CardType p2Card;

        // 제출 결과
        public int p1Delta;          // P1의 카드로 인한 양초 변화량
        public int p2Delta;          // P2의 카드로 인한 양초 변화량
        public int p1LifeAfter;      // 라운드 종료 시
        public int p2LifeAfter;      // 라운드 종료 시
    }

    // ===== 로깅 =====
    public static void LogMatchStart(in MatchStart m)
    {
        if (!Enabled) return;
        if (!_matchHeader)
        {
            _matchHeader = true;
            FileAppend(MatchesCsv, "session,ts,matchId,mode,p1,p2,totalRounds,winner,loser\n");
        }
        // 매치 시작 시점에는 메타만 보존하고, 결과는 End에서 한 줄로 완성
        // 필요하면 여기서도 프리헤더 행을 남길 수 있음(권장: End만 사용)
    }

    public static void LogMatchEnd(in MatchEnd e, in MatchStart s)
    {
        if (!Enabled) return;
        var line = string.Join(",",
            SessionId, UtcNow(),
            Csv(s.matchId), Csv(s.mode), Csv(s.p1), Csv(s.p2),
            e.totalRounds, Csv(e.winner), Csv(e.loser)
        );
        _matchBuf.Add(line + "\n");
        TryFlush(MatchesCsv, _matchBuf);
    }

    public static void LogRound(in RoundRow r)
    {
        if (!Enabled) return;
        if (!_roundHeader)
        {
            _roundHeader = true;
            FileAppend(RoundsCsv,
              "session,ts,matchId,round,disaster,p1Hand,p2Hand,p1Card,p2Card,p1Delta,p2Delta,p1LifeAfter,p2LifeAfter\n");
        }
        var line = string.Join(",",
            SessionId, UtcNow(), Csv(r.matchId), r.round, Csv(r.disaster ?? ""),
            Csv(JoinCards(r.p1Hand)), Csv(JoinCards(r.p2Hand)),
            Csv(r.p1Card.ToString()), Csv(r.p2Card.ToString()),
            r.p1Delta, r.p2Delta, r.p1LifeAfter, r.p2LifeAfter
        );
        _roundBuf.Add(line + "\n");
        TryFlush(RoundsCsv, _roundBuf);
    }

    public static void FlushAll()
    {
        if (_matchBuf.Count > 0) FileAppend(MatchesCsv, string.Concat(_matchBuf.ToArray()));
        if (_roundBuf.Count  > 0) FileAppend(RoundsCsv,  string.Concat(_roundBuf.ToArray()));
        _matchBuf.Clear(); _roundBuf.Clear();
    }

    // ===== 유틸 =====
    static string JoinCards(List<CardType> list)
    {
        if (list == null || list.Count == 0) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) sb.Append('|');
            sb.Append(list[i].ToString());
        }
        return sb.ToString();
    }

    static void TryFlush(string path, List<string> buf)
    {
        if (buf.Count >= FlushEvery) { FileAppend(path, string.Concat(buf.ToArray())); buf.Clear(); }
    }

    static void FileAppend(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var w = new StreamWriter(path, append: true, Encoding.UTF8);
        w.Write(text);
    }

    static string Csv(string s)
    {
        if (s == null) return "";
        if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    static string UtcNow() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
}