using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace MycopunkShared
{
    /// <summary>
    /// A movable HUD element: its position is an anchor point in a parent rect (0..1), kept in the owning mod's
    /// config as AnchorX / AnchorY (section "HUD Position: &lt;name&gt;"), and the element is a child of that parent so
    /// it moves with it (for example with the game's HUD box it belongs to) in the same frame.
    ///
    /// - While the values are negative (<see cref="HudAnchor.Unset"/>) the element sits at its default anchor, which
    ///   the mod gives (and may change, to follow the game's layout).
    /// - The position is set only when the element is attached and when the values change, never every frame, so
    ///   it does not fight anything that moves it, such as a drag in ModSettingsMenu's HUD reposition mode.
    /// - When ModSettingsMenu (Sparroh) is installed, the element is registered with its HudRepositionAPI, passing
    ///   these same ConfigEntry objects: a drag writes them (and saves the file), and this applies them. Nothing
    ///   else reads or writes the file, so no stale value can overwrite a drag. Found by reflection; it is not a
    ///   dependency. The keys deliberately don't end in "...AnchorX" with a prefix, so ModSettingsMenu's own
    ///   config auto-detection does not register the element a second time.
    /// - The element's anchoredPosition is 0 and its pivot is the caller's: the anchor point is where the pivot goes.
    /// </summary>
    public sealed class HudPlacement
    {
        private readonly string _id;
        private readonly string _displayName;
        private RectTransform _element;
        private float _defaultX, _defaultY;
        private bool _registered;

        public ConfigEntry<float> AnchorX { get; }
        public ConfigEntry<float> AnchorY { get; }
        public bool IsCustom => HudAnchor.IsCustom(AnchorX.Value, AnchorY.Value);

        private HudPlacement(string id, string displayName, ConfigEntry<float> x, ConfigEntry<float> y)
        {
            _id = id;
            _displayName = displayName;
            AnchorX = x;
            AnchorY = y;
            x.SettingChanged += (_, __) => Apply();
            y.SettingChanged += (_, __) => Apply();
        }

        /// <summary>The config entries of one movable element of <paramref name="plugin"/>.</summary>
        public static HudPlacement Bind(BaseUnityPlugin plugin, string name)
        {
            string section = "HUD Position: " + name;
            var x = plugin.Config.Bind(section, "AnchorX", HudAnchor.Unset, new ConfigDescription(
                "Horizontal position as a fraction of the HUD area it belongs to (0 = left, 1 = right). Negative: the default position."));
            var y = plugin.Config.Bind(section, "AnchorY", HudAnchor.Unset, new ConfigDescription(
                "Vertical position as a fraction of the HUD area it belongs to (0 = bottom, 1 = top). Negative: the default position."));
            return new HudPlacement(plugin.Info.Metadata.GUID + "/" + name, name, x, y);
        }

        /// <summary>
        /// Puts <paramref name="element"/> under <paramref name="parent"/> and places it. The default anchor is a point
        /// in the parent (0..1); call again with a new element after the game rebuilt its HUD.
        /// </summary>
        public void Attach(RectTransform element, RectTransform parent, Vector2 defaultAnchor)
        {
            if (element == null || parent == null)
                return;
            if (element.parent != parent)
                element.SetParent(parent, false);
            _element = element;
            _defaultX = defaultAnchor.x;
            _defaultY = defaultAnchor.y;
            Apply();
            Register();
        }

        /// <summary>A new default anchor (for example, the game's layout changed). Used only while not moved.</summary>
        public void SetDefault(Vector2 defaultAnchor)
        {
            if (Mathf.Approximately(_defaultX, defaultAnchor.x) && Mathf.Approximately(_defaultY, defaultAnchor.y))
                return;
            _defaultX = defaultAnchor.x;
            _defaultY = defaultAnchor.y;
            if (!IsCustom)
                Apply();
        }

        /// <summary>Back to the default position.</summary>
        public void Reset()
        {
            AnchorX.Value = HudAnchor.Unset;
            AnchorY.Value = HudAnchor.Unset;
        }

        /// <summary>Call when the element is destroyed.</summary>
        public void Detach()
        {
            if (_registered)
                ModSettingsMenuBridge.Unregister(_id);
            _registered = false;
            _element = null;
        }

        private void Apply()
        {
            if (_element == null)
                return;
            HudAnchor.Resolve(AnchorX.Value, AnchorY.Value, _defaultX, _defaultY, out float ax, out float ay);
            var anchor = new Vector2(ax, ay);
            _element.anchorMin = anchor;
            _element.anchorMax = anchor;
            _element.anchoredPosition = Vector2.zero;
        }

        private void Register()
        {
            if (_registered)
                ModSettingsMenuBridge.Unregister(_id); // the element is new: register the new rect
            _registered = ModSettingsMenuBridge.Register(_id, _displayName, _element, AnchorX, AnchorY);
        }

        /// <summary>Optional link to Sparroh's ModSettingsMenu (global type HudRepositionAPI), by reflection.</summary>
        private static class ModSettingsMenuBridge
        {
            private static bool _looked;
            private static MethodInfo _register, _unregister;

            private static void Find()
            {
                if (_looked)
                    return;
                _looked = true;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t;
                    try { t = asm.GetType("HudRepositionAPI", false); }
                    catch { continue; }
                    if (t == null)
                        continue;
                    _register = t.GetMethod("Register", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(string), typeof(string), typeof(RectTransform), typeof(ConfigEntry<float>), typeof(ConfigEntry<float>) }, null);
                    _unregister = t.GetMethod("Unregister", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                    break;
                }
            }

            public static bool Register(string id, string name, RectTransform rect, ConfigEntry<float> x, ConfigEntry<float> y)
            {
                try
                {
                    Find();
                    if (_register == null)
                        return false;
                    _register.Invoke(null, new object[] { id, name, rect, x, y });
                    return true;
                }
                catch
                {
                    return false; // never let the optional link break the HUD
                }
            }

            public static void Unregister(string id)
            {
                try { _unregister?.Invoke(null, new object[] { id }); }
                catch { }
            }
        }
    }
}
