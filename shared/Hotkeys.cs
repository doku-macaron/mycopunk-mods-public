using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace MycopunkShared
{
    /// <summary>
    /// Finds keys that more than one mod (or a mod and the game) listen to, and logs them once per launch.
    ///
    /// Each mod compiles its own copy of this class, so the registry lives in AppDomain data (a List of strings,
    /// a type every assembly shares) instead of a static field. Sources:
    ///  - keys a mod registers with <see cref="Register"/> (hard-coded hotkeys),
    ///  - every config entry of type Key, KeyCode or KeyboardShortcut of every loaded plugin (also other authors'),
    ///  - the keyboard bindings of the game's PlayerControls asset (with the player's rebinds).
    /// Keys that the game reads directly from Keyboard.current are not visible.
    /// </summary>
    public static class Hotkeys
    {
        private const string RegistryKey = "MycopunkShared.Hotkeys.v1.registry";
        private const string ReporterKey = "MycopunkShared.Hotkeys.v1.reporter";
        private const float GameInputTimeout = 120f;

        private sealed class Binding
        {
            public string Guid;      // plugin GUID, or null for the game
            public string Owner;     // what to print
            public Key Key;
            public string Modifiers; // "" or e.g. "LeftCtrl+LeftShift", sorted
            public bool FromConfig;

            public string KeyText => Modifiers.Length == 0 ? Key.ToString() : Modifiers + "+" + Key;
        }

        /// <summary>Registers a key that <paramref name="plugin"/> reads directly (not through a config entry).</summary>
        public static void Register(BaseUnityPlugin plugin, Key key, string purpose)
        {
            if (key == Key.None) return;
            try
            {
                var meta = plugin.Info.Metadata;
                string binding = string.Join("\t", meta.GUID, meta.Name, key.ToString(), "", purpose ?? "");
                if (!Registry().Contains(binding)) Registry().Add(binding);
            }
            catch
            {
                // Never let the check break a mod.
            }
        }

        /// <summary>
        /// Call from every mod's Awake. The first caller in the process waits until every plugin has loaded and the
        /// game has created its input, then logs the conflicts. Later callers do nothing.
        /// </summary>
        public static void ReportConflicts(BaseUnityPlugin host)
        {
            try
            {
                var domain = AppDomain.CurrentDomain;
                if (domain.GetData(ReporterKey) != null) return;
                domain.SetData(ReporterKey, host.Info.Metadata.GUID);
                host.StartCoroutine(ReportWhenReady());
            }
            catch
            {
            }
        }

        private static List<string> Registry()
        {
            var domain = AppDomain.CurrentDomain;
            if (!(domain.GetData(RegistryKey) is List<string> list))
            {
                list = new List<string>();
                domain.SetData(RegistryKey, list);
            }
            return list;
        }

        private static IEnumerator ReportWhenReady()
        {
            // The chainloader runs every Awake before the first frame, so one frame later all plugins are in.
            yield return null;
            float start = Time.realtimeSinceStartup;
            // Do not call PlayerInput.Controls: it would create the game's input early. Wait for the game to do it
            // (the backing field _controls stays null until then).
            while (global::PlayerInput._controls == null && Time.realtimeSinceStartup - start < GameInputTimeout)
                yield return new WaitForSecondsRealtime(1f);

            var log = BepInEx.Logging.Logger.CreateLogSource("Hotkeys");
            try
            {
                Report(log, global::PlayerInput._controls);
            }
            catch (Exception e)
            {
                log.LogWarning("Hotkey check failed: " + e);
            }
        }

        private static void Report(ManualLogSource log, Pigeon.Movement.PlayerControls controls)
        {
            var mods = new List<Binding>();
            foreach (var line in Registry())
            {
                var p = line.Split('\t');
                if (p.Length < 5 || !Enum.TryParse(p[2], out Key key)) continue;
                mods.Add(new Binding { Guid = p[0], Owner = p[1] + (p[4].Length > 0 ? " (" + p[4] + ")" : ""), Key = key, Modifiers = p[3] });
            }
            AddConfigEntries(mods);

            int gameCount;
            var game = GameBindings(controls, out string gameNote);
            gameCount = game.Count;

            var modKeys = mods.OrderBy(b => b.Key.ToString()).Select(b => b.KeyText + " " + b.Owner);
            log.LogInfo("Mod hotkeys: " + (mods.Count == 0 ? "none" : string.Join(", ", modKeys)) +
                ". Game keyboard bindings checked: " + gameCount + gameNote + ".");

            int conflicts = 0;
            foreach (var group in mods.Concat(game).GroupBy(b => b.Key))
            {
                var list = group.ToList();
                if (!list.Any(b => b.Guid != null)) continue; // the game against itself is not our business
                var clashing = new List<Binding>();
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                        if (Clash(list[i], list[j]))
                        {
                            if (!clashing.Contains(list[i])) clashing.Add(list[i]);
                            if (!clashing.Contains(list[j])) clashing.Add(list[j]);
                        }
                if (clashing.Count == 0) continue;
                conflicts++;
                log.LogWarning("Hotkey conflict on " + group.Key + ": " +
                    string.Join(", ", clashing.Select(b => (b.Modifiers.Length > 0 ? "[" + b.KeyText + "] " : "") + b.Owner)));
            }
            if (conflicts == 0) log.LogInfo("No hotkey conflicts found.");
        }

        // A key read with wasPressedThisFrame fires whatever modifiers are held, so a plain key clashes with any
        // shortcut on the same main key. Two entries of the same plugin and the same game action do not clash.
        private static bool Clash(Binding a, Binding b)
        {
            if (a.Guid == null && b.Guid == null) return false;
            if (a.Guid != null && a.Guid == b.Guid && a.Owner == b.Owner) return false;
            return a.Modifiers.Length == 0 || b.Modifiers.Length == 0 || a.Modifiers == b.Modifiers;
        }

        private static void AddConfigEntries(List<Binding> into)
        {
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                ConfigFile config;
                try { config = info.Instance != null ? info.Instance.Config : null; }
                catch { continue; }
                if (config == null) continue;
                foreach (var pair in config)
                {
                    var entry = pair.Value;
                    Key key = Key.None;
                    string modifiers = "";
                    object value = entry.BoxedValue;
                    if (value is Key k) key = k;
                    else if (value is KeyCode kc) key = FromKeyCode(kc);
                    else if (value is KeyboardShortcut ks)
                    {
                        key = FromKeyCode(ks.MainKey);
                        modifiers = string.Join("+", ks.Modifiers.Select(FromKeyCode).Where(m => m != Key.None)
                            .Select(m => m.ToString()).OrderBy(s => s, StringComparer.Ordinal));
                    }
                    if (key == Key.None) continue;
                    string guid = info.Metadata.GUID;
                    // A mod that registers a key and also keeps it in its config would be counted twice.
                    if (into.Any(b => b.Guid == guid && b.Key == key && b.Modifiers == modifiers)) continue;
                    into.Add(new Binding
                    {
                        Guid = guid,
                        Owner = info.Metadata.Name + " [" + pair.Key.Section + "." + pair.Key.Key + "]",
                        Key = key,
                        Modifiers = modifiers,
                        FromConfig = true,
                    });
                }
            }
        }

        private static List<Binding> GameBindings(Pigeon.Movement.PlayerControls controls, out string note)
        {
            var result = new List<Binding>();
            note = "";
            var keyboard = Keyboard.current;
            if (controls == null) { note = " (the game's input was not created yet)"; return result; }
            if (keyboard == null) { note = " (no keyboard)"; return result; }
            var asset = controls.asset;
            if (asset == null) { note = " (PlayerControls.asset not found)"; return result; }
            foreach (var map in asset.actionMaps)
                foreach (var action in map.actions)
                    foreach (var binding in action.bindings)
                    {
                        string path = binding.effectivePath;
                        if (binding.isComposite || string.IsNullOrEmpty(path) || !path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!(InputControlPath.TryFindControl(keyboard, path) is KeyControl control)) continue;
                        string owner = "game " + map.name + "/" + action.name;
                        if (result.Any(b => b.Key == control.keyCode && b.Owner == owner)) continue;
                        result.Add(new Binding { Owner = owner, Key = control.keyCode, Modifiers = "" });
                    }
            return result;
        }

        private static Key FromKeyCode(KeyCode code)
        {
            string name = code.ToString();
            if (name.StartsWith("Alpha")) name = "Digit" + name.Substring(5);
            else if (name.StartsWith("Keypad")) name = "Numpad" + name.Substring(6);
            switch (code)
            {
                case KeyCode.Return: return Key.Enter;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.BackQuote: return Key.Backquote;
                case KeyCode.LeftWindows: case KeyCode.LeftCommand: return Key.LeftMeta;
                case KeyCode.RightWindows: case KeyCode.RightCommand: return Key.RightMeta;
                case KeyCode.KeypadEnter: return Key.NumpadEnter;
                case KeyCode.KeypadPlus: return Key.NumpadPlus;
                case KeyCode.KeypadMinus: return Key.NumpadMinus;
                case KeyCode.KeypadMultiply: return Key.NumpadMultiply;
                case KeyCode.KeypadDivide: return Key.NumpadDivide;
                case KeyCode.KeypadPeriod: return Key.NumpadPeriod;
            }
            return Enum.TryParse(name, true, out Key key) ? key : Key.None;
        }
    }
}
