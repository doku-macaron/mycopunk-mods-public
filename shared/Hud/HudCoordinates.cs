using UnityEngine;

namespace MycopunkShared
{
    public static class HudCoordinates
    {
        public static Camera CameraFor(Canvas root) => root.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : (root.worldCamera != null ? root.worldCamera : Camera.main);
    }
}
