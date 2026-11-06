namespace MycopunkSystemMonitor
{
    internal static class MonitorText
    {
        public static string Format(float fps, bool client, int ping)
        {
            string text = $"FPS {fps:F0}";
            return client ? text + (ping > 0 ? $" / PING {ping} ms" : " / PING --") : text;
        }
    }
}
