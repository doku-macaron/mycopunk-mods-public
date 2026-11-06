namespace MycopunkShared
{
    public static class HudVisibility
    {
        public static bool ResultsOpen()
        {
            var manager = GameManager.Instance;
            var windows = manager != null ? manager.WindowSystem : null;
            if (windows == null) return false;
            for (int i = 0; i < windows.Count; i++)
                if (windows[i] is MissionRewardsWindow || windows[i] is MissionStatsWindow)
                    return true;
            return false;
        }
    }
}
