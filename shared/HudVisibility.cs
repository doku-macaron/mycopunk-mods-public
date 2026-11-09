using Pigeon.Movement;
using UnityEngine;
using UnityEngine.UI;

namespace MycopunkShared
{
    public static class HudVisibility
    {
        public static bool HealthBarVisible(Player player, Graphic bar, Canvas canvas) =>
            player != null && player.IsAlive && bar != null && bar.isActiveAndEnabled &&
            canvas != null && canvas.isActiveAndEnabled && !ResultsOpen();

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
