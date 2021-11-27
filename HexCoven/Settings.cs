namespace HexCoven
{
    public static class Settings
    {
        // Values will be set by HexCoven CLI
        internal static bool LogCloseCalls = true;
        internal static bool LogOutbound = true;
        internal static bool LogOutboundPing = false;
        internal static bool LogInbound = true;
        internal static bool LogInboundPing = false;
        internal static bool LogNameUpdates = false;

        internal static double MatchingIntervalMs = 100;
        internal static bool ShowReadyInName = false;
        internal static bool ShowClock = true;
        internal static float TimerDuration = 0f;
    }
}
