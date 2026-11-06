using System;
using System.Globalization;
using MycopunkSystemMonitor;

internal static class MonitorTextTests
{
    private static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Check(MonitorText.Format(60, false, 40), "FPS 60");
        Check(MonitorText.Format(120, true, 45), "FPS 120 / PING 45 ms");
        Check(MonitorText.Format(30, true, 0), "FPS 30 / PING --");
        Console.WriteLine("Monitor text tests passed.");
    }

    private static void Check(string actual, string expected)
    {
        if (actual != expected) throw new Exception($"Expected '{expected}', got '{actual}'.");
    }
}
