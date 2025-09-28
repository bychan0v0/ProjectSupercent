using System;
using System.Collections.Generic;
using UnityEngine;

public static class Analytics
{
    // 웹 메모리 고려: 최근 N개만 보관(필요시 조절)
    private const int MaxBuffer = 4000;
    private static readonly List<string> _buffer = new(MaxBuffer);
    private static readonly string _sessionId = System.Guid.NewGuid().ToString("N");

    // 공통 필드 + payload를 합쳐 JSON 한 줄로 남김
    public static void Log(string evt, object payload = null)
    {
        // payload를 직렬화 가능한 형태(익명/struct/class)로 준다는 전제
        string data = payload != null ? JsonUtility.ToJson(payload) : "{}";
        string line =
            $"{{\"t\":{Time.time:F3},\"evt\":\"{evt}\",\"session\":\"{_sessionId}\",\"data\":{data}}}";
        Debug.Log(line);

        // (선택) 간단 버퍼 (Export/Copy용)
        if (_buffer.Count == MaxBuffer) _buffer.RemoveAt(0);
        _buffer.Add(line);
    }

    public static string Dump(bool clear = false)
    {
        string joined = string.Join("\n", _buffer);
        if (clear) _buffer.Clear();
        return joined;
    }
}