using System;
using System.Diagnostics;
using UnityEngine;

// Presentation clock only. Damage is always resolved by the session server.
public static class ServerClock
{
    private static readonly Stopwatch monotonic = Stopwatch.StartNew();
    private static double offset;
    private static long bestRtt = long.MaxValue;
    private static bool initialized;
    public static long LocalMs => monotonic.ElapsedMilliseconds;
    public static long NowMs => initialized ? (long)(LocalMs + offset) : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public static long RoundTripMs { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset() { initialized = false; bestRtt = long.MaxValue; RoundTripMs = 0; }

    public static void Observe(long serverTimeMs)
    {
        if (initialized || serverTimeMs <= 0) return;
        offset = serverTimeMs - LocalMs;
        initialized = true;
    }

    public static void Synchronize(long echoedClientMs, long serverTimeMs)
    {
        long received = LocalMs;
        long rtt = received - echoedClientMs;
        if (rtt < 0 || rtt > 10000 || serverTimeMs <= 0) return;
        RoundTripMs = rtt;
        // Prefer the least queued RTT sample; midpoint estimates one-way transport time.
        if (!initialized || bestRtt == long.MaxValue || rtt <= bestRtt + 5)
        {
            offset = serverTimeMs - (echoedClientMs + received) * 0.5;
            bestRtt = Math.Min(bestRtt, rtt);
            initialized = true;
        }
    }
}

public static class NetworkRuntime
{
    public static bool IsOnline { get; internal set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { IsOnline = false; }
}
