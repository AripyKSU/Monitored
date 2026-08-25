using System.Diagnostics;
using UnityEngine;

/// <summary>
/// 프로젝트의 런타임 로그 정책을 한곳에서 관리한다.
/// 일반 로그는 에디터와 Development Build에만 포함하고, 오류 로그는 릴리스 빌드에도 남긴다.
/// </summary>
public static class GameLog
{
    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void Log(object message, Object context = null)
    {
        UnityEngine.Debug.Log(message, context);
    }

    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void LogWarning(object message, Object context = null)
    {
        UnityEngine.Debug.LogWarning(message, context);
    }

    public static void LogError(object message, Object context = null)
    {
        UnityEngine.Debug.LogError(message, context);
    }
}
