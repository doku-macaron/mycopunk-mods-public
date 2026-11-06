using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MycopunkShared;
using Netcode.Transports.Facepunch;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace MycopunkSystemMonitor
{
    [BepInPlugin("f1w3.mycopunk.systemmonitor", "Mycopunk System Monitor", "0.1.0")]
    [BepInDependency(MycopunkShared.ModKit.Guid)]
    [MycoMod(null, ModFlags.IsClientSide)]
    public class Plugin : BaseUnityPlugin
    {
        private static readonly FieldInfo ConnectionField = AccessTools.Field(typeof(FacepunchTransport), "connectionManager");
        private float _elapsed;
        private int _frames;
        private string _text = "FPS --";
        private GUIStyle _style;
        private readonly GUIContent _content = new GUIContent();
        private ConfigEntry<bool> _top;
        private ConfigEntry<bool> _right;

        private void Awake()
        {
            _top = Config.Bind("Position", "Top", false, new ConfigDescription(
                "Show the monitor at the top of the screen instead of the bottom.", null, MenuText.Label("monitor.top")));
            _right = Config.Bind("Position", "Right", false, new ConfigDescription(
                "Show the monitor at the right of the screen instead of the left.", null, MenuText.Label("monitor.right")));
            ModsMenu.Install(new Harmony("f1w3.mycopunk.systemmonitor"), this);
        }

        private void Update()
        {
            _elapsed += Time.unscaledDeltaTime;
            _frames++;
            if (_elapsed < 0.5f) return;

            var manager = NetworkManager.Singleton;
            bool client = manager != null && manager.IsConnectedClient && !manager.IsServer;
            int ping = 0;
            if (client && manager.NetworkConfig.NetworkTransport is FacepunchTransport transport && ConnectionField != null)
            {
                var connection = ConnectionField.GetValue(transport) as ConnectionManager;
                if (connection != null && connection.Connected)
                    ping = connection.Connection.QuickStatus().Ping;
            }
            _text = MonitorText.Format(_frames / _elapsed, client, ping);
            _elapsed = 0f;
            _frames = 0;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_style == null)
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = false,
                    clipping = TextClipping.Overflow,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                };
            float scale = Screen.height / 1080f;
            _style.fontSize = Mathf.Max(1, Mathf.RoundToInt(14f * scale));
            _content.text = _text;
            Vector2 size = _style.CalcSize(_content);
            float width = size.x + 12f * scale;
            float height = size.y + 6f * scale;
            var rect = new Rect(_right.Value ? Screen.width - width : 0f,
                _top.Value ? 0f : Screen.height - height, width, height);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
            _style.normal.textColor = Color.white;
            GUI.Label(new Rect(rect.x + 6f * scale, rect.y + 3f * scale, size.x, size.y), _content, _style);
        }
    }
}
