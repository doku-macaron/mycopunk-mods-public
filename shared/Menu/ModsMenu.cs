using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MycopunkShared
{
    /// <summary>
    /// A "MODS" tab in the game's options window (PlayerOptions), listing the config entries of every loaded plugin
    /// that are marked with <see cref="ModsMenuTag"/>, grouped under a heading per mod. bool entries get an ON/OFF row,
    /// Color entries a colour picker. Values are read from and written to the BepInEx config; the game's own options
    /// file and the save are never touched.
    ///
    /// Built in a PlayerOptions.Awake prefix, before Awake sizes sectionPositions from sections:
    ///  - the tab button is a copy of the "Graphics" button, put where it was plus one step (70) down; buttons at or
    ///    below that (the smaller "Debug"/Advanced one) move one step down. Its SetTextBlock is removed so the label
    ///    stays "MODS", and its OnClickUp is replaced by SetSection(new index);
    ///  - the tab body is a copy of the "Debug" body with its items removed;
    ///  - rows are copies of the game's rows, made inside an inactive holder so that their Option component (which
    ///    reads and writes the game's options by id in OnEnable) can be removed before it ever runs:
    ///    * ON/OFF: "Gameplay/Items/HideHUD" (ToggleOption). The knob and icon are set the way ToggleOption.SetText does.
    ///    * colour: "Gameplay/Items/UIColor" (ColorOption). A ScrollBar handle on a hue/saturation field; the game
    ///      maps it to HSV(x, Lerp(0.1, 0.72, y), 1) and saves on release (ScrollBar.OnDrag). Here it is
    ///      HSV(x, y, 1), so the bottom edge is white; the alpha of the entry is kept. The handle's fill shows the
    ///      colour (its CharacterGraphic, which follows the UI colour, is removed).
    ///  - a heading per mod is a copy of a row's name text on an empty row.
    /// The game's UI types are mostly used through reflection (by name), so a game update fails here with a log line
    /// instead of breaking the build or the window.
    ///
    /// Each mod compiles its own copy; the first one to call <see cref="Install"/> in the process builds the tab
    /// (AppDomain data), so the tab exists once with every mod's entries.
    /// </summary>
    public static class ModsMenu
    {
        private const string BuilderKey = "MycopunkShared.ModsMenu.v1.builder";
        private const string TabName = "Mods";
        private const float Step = 70f;

        private static ManualLogSource _log;
        private static readonly List<Row> Rows = new List<Row>();
        private static readonly FieldInfo HoverText = AccessTools.Field(typeof(Pigeon.HoverInfoText), "text");

        private abstract class Row
        {
            public GameObject Go;
            public ConfigEntryBase Entry;
            public string Owner;
            public TextMeshProUGUI Name;
            public Pigeon.HoverInfoText Hover;
            public abstract void Refresh();
        }

        private sealed class ToggleRow : Row
        {
            public RectTransform Knob;
            public Image Icon;
            public float Thickness;
            public override void Refresh() => ShowToggle(this, (bool)Entry.BoxedValue);
        }

        private sealed class ColorRow : Row
        {
            public ScrollBar Bar;
            public Graphic Fill;
            public override void Refresh() => ShowColor(this, (Color)Entry.BoxedValue);
        }

        public static void Install(Harmony harmony, BaseUnityPlugin host)
        {
            try
            {
                var domain = AppDomain.CurrentDomain;
                if (domain.GetData(BuilderKey) != null) return;
                domain.SetData(BuilderKey, host.Info.Metadata.GUID);
                _log = BepInEx.Logging.Logger.CreateLogSource("ModsMenu");
                var awake = AccessTools.Method(typeof(PlayerOptions), "Awake");
                var onOpen = AccessTools.Method(typeof(PlayerOptions), "OnOpen");
                var languageInit = AccessTools.Method(typeof(TextBlocks), "Initialize");
                if (awake == null) { _log.LogWarning("PlayerOptions.Awake not found; no MODS tab."); return; }
                var menuHarmony = new Harmony("MycopunkShared.ModsMenu");
                menuHarmony.Patch(awake, prefix: new HarmonyMethod(typeof(ModsMenu), nameof(AwakePrefix)));
                if (onOpen != null) menuHarmony.Patch(onOpen, postfix: new HarmonyMethod(typeof(ModsMenu), nameof(OnOpenPostfix)));
                if (languageInit != null) menuHarmony.Patch(languageInit, postfix: new HarmonyMethod(typeof(ModsMenu), nameof(RefreshLabels)));
            }
            catch (Exception e)
            {
                (_log ?? (_log = BepInEx.Logging.Logger.CreateLogSource("ModsMenu"))).LogWarning("MODS tab disabled: " + e);
            }
        }

        private static void AwakePrefix(PlayerOptions __instance)
        {
            try { Build(__instance); }
            catch (Exception e) { _log.LogWarning("MODS tab could not be built: " + e); }
        }

        private static void OnOpenPostfix()
        {
            try
            {
                foreach (var row in Rows)
                {
                    if (row.Go == null) continue;
                    if (Chainloader.PluginInfos.TryGetValue(row.Owner, out var info) && info.Instance != null)
                    {
                        var entry = info.Instance.Config.FirstOrDefault(p => p.Key.Equals(row.Entry.Definition)).Value;
                        if (entry != null) row.Entry = entry;
                    }
                    row.Refresh();
                }
                RefreshLabels();
            }
            catch (Exception e) { _log.LogWarning("MODS tab refresh failed: " + e.Message); }
        }

        private sealed class Item
        {
            public ConfigEntryBase Entry;
            public string Label;
            public string Owner;
        }

        private sealed class Group
        {
            public string Mod;
            public readonly List<Item> Items = new List<Item>();
        }

        private static List<Group> CollectEntries()
        {
            var groups = new List<Group>();
            foreach (var info in Chainloader.PluginInfos.Values.OrderBy(i => i.Metadata.Name, StringComparer.OrdinalIgnoreCase))
            {
                ConfigFile config;
                try { config = info.Instance != null ? info.Instance.Config : null; } catch { continue; }
                if (config == null) continue;
                string mod = info.Metadata.Name.StartsWith("Mycopunk ", StringComparison.Ordinal) ? info.Metadata.Name.Substring(9) : info.Metadata.Name;
                var group = new Group { Mod = mod };
                foreach (var pair in config)
                {
                    var entry = pair.Value;
                    if (entry.SettingType != typeof(bool) && entry.SettingType != typeof(Color)) continue;
                    string label = ModsMenuTag.Find(entry.Description?.Tags);
                    if (label == null) continue;
                    group.Items.Add(new Item { Entry = entry, Label = label.Length > 0 ? label : pair.Key.Key, Owner = info.Metadata.GUID });
                }
                if (group.Items.Count > 0) groups.Add(group);
            }
            return groups;
        }

        private static void Build(PlayerOptions po)
        {
            var sectionsField = AccessTools.Field(typeof(PlayerOptions), "sections");
            var buttonsField = AccessTools.Field(typeof(PlayerOptions), "sectionButtons");
            var sections = sectionsField?.GetValue(po) as RectTransform[];
            var buttons = buttonsField?.GetValue(po) as Array;
            if (sections == null || buttons == null) { _log.LogWarning("PlayerOptions.sections / sectionButtons not found; no MODS tab."); return; }
            if (sections.Any(s => s != null && s.name == TabName)) return;

            var groups = CollectEntries();
            if (groups.Count == 0) { _log.LogInfo("No config entries are marked for the MODS tab."); return; }

            var buttonList = buttons.Cast<Component>().ToList();
            Component graphicsButton = buttonList.FirstOrDefault(b => b != null && b.name == "Graphics");
            RectTransform debugSection = sections.FirstOrDefault(s => s != null && s.name == "Debug");
            RectTransform gameplaySection = sections.FirstOrDefault(s => s != null && s.name == "Gameplay");
            Transform toggleTemplate = gameplaySection != null ? gameplaySection.Find("Items/HideHUD") : null;
            Transform colorTemplate = gameplaySection != null ? gameplaySection.Find("Items/UIColor") : null;
            if (graphicsButton == null || debugSection == null || toggleTemplate == null)
            {
                _log.LogWarning("MODS tab: Graphics button, Debug section or Gameplay/Items/HideHUD not found; no MODS tab.");
                return;
            }
            if (colorTemplate == null)
                _log.LogWarning("MODS tab: Gameplay/Items/UIColor not found; colour entries are left out.");
            int index = sections.Length;
            var setSection = AccessTools.Method(typeof(PlayerOptions), "SetSection", new[] { typeof(int) });
            if (setSection == null) { _log.LogWarning("PlayerOptions.SetSection(int) not found; no MODS tab."); return; }

            // Tab button: a copy of Graphics, one step below it; what was there moves down.
            var graphicsRect = (RectTransform)graphicsButton.transform;
            var buttonGo = UnityEngine.Object.Instantiate(graphicsButton.gameObject, graphicsRect.parent, false);
            buttonGo.name = TabName;
            var buttonRect = (RectTransform)buttonGo.transform;
            buttonRect.anchoredPosition = graphicsRect.anchoredPosition + new Vector2(0, -Step);
            foreach (var b in buttonList)
                if (b != null && b != graphicsButton && b.transform is RectTransform r && r.anchoredPosition.y <= buttonRect.anchoredPosition.y + 0.5f)
                    r.anchoredPosition += new Vector2(0, -Step);
            foreach (var c in buttonGo.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == "SetTextBlock") UnityEngine.Object.DestroyImmediate(c);
            var buttonText = buttonGo.GetComponentInChildren<TextMeshProUGUI>(true);
            if (buttonText != null) buttonText.text = "MODS";
            var tabButton = buttonGo.GetComponent(graphicsButton.GetType());
            ReplaceEvent(tabButton, "OnClickUp", () => setSection.Invoke(po, new object[] { index }));

            // Tab body: a copy of Debug without its items.
            var bodyGo = UnityEngine.Object.Instantiate(debugSection.gameObject, debugSection.parent, false);
            bodyGo.name = TabName;
            bodyGo.SetActive(false);
            var itemsParent = bodyGo.transform.Find("Items");
            if (itemsParent == null) { UnityEngine.Object.DestroyImmediate(bodyGo); UnityEngine.Object.DestroyImmediate(buttonGo); _log.LogWarning("Debug/Items not found; no MODS tab."); return; }
            for (int i = itemsParent.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(itemsParent.GetChild(i).gameObject);

            // Rows. The options window is rebuilt when its scene loads again (e.g. back to the title screen), so drop
            // the rows of a window that is gone and count only this one's.
            Rows.RemoveAll(r => r.Go == null);
            int added = 0;
            var holder = new GameObject("ModsMenu holder");
            holder.SetActive(false);
            try
            {
                foreach (var group in groups)
                {
                    var heading = MakeHeading(toggleTemplate.gameObject, holder.transform, group.Mod);
                    if (heading != null) heading.transform.SetParent(itemsParent, false);
                    foreach (var item in group.Items)
                    {
                        Row row = item.Entry.SettingType == typeof(bool)
                            ? MakeToggleRow(toggleTemplate.gameObject, holder.transform, item)
                            : colorTemplate != null ? MakeColorRow(colorTemplate.gameObject, holder.transform, item) : null;
                        if (row == null) continue;
                        row.Go.transform.SetParent(itemsParent, false);
                        Rows.Add(row);
                        added++;
                        row.Refresh();
                    }
                }
            }
            finally { UnityEngine.Object.Destroy(holder); }

            // Register with the window before its Awake sizes sectionPositions.
            sectionsField.SetValue(po, sections.Concat(new[] { (RectTransform)bodyGo.transform }).ToArray());
            var newButtons = Array.CreateInstance(buttons.GetType().GetElementType(), buttons.Length + 1);
            Array.Copy(buttons, newButtons, buttons.Length);
            newButtons.SetValue(tabButton, buttons.Length);
            buttonsField.SetValue(po, newButtons);
            _log.LogInfo("MODS tab added with " + added + " entries in " + groups.Count + " groups.");
        }

        /// <summary>A heading: a copy of the toggle row with everything but its name text taken off.</summary>
        private static GameObject MakeHeading(GameObject template, Transform holder, string mod)
        {
            var go = UnityEngine.Object.Instantiate(template, holder, false);
            go.name = "Mod heading " + mod;
            var name = go.transform.Find("Name");
            var text = name != null ? name.GetComponent<TextMeshProUGUI>() : null;
            if (text == null) { UnityEngine.Object.DestroyImmediate(go); return null; }
            for (int i = go.transform.childCount - 1; i >= 0; i--)
                if (go.transform.GetChild(i) != name) UnityEngine.Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            // Remove the row's own components (Option, button, outline...), keeping the rect and its layout size.
            // Behaviours first, then the graphics, then their CanvasRenderer.
            foreach (var c in go.GetComponents<Component>().Where(c => c is MonoBehaviour && !(c is Graphic) && !(c is LayoutElement)).ToList())
                UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in go.GetComponents<Graphic>().ToList())
                UnityEngine.Object.DestroyImmediate(c);
            var cr = go.GetComponent<CanvasRenderer>();
            if (cr != null) UnityEngine.Object.DestroyImmediate(cr);
            text.text = mod.ToUpperInvariant();
            text.fontStyle |= FontStyles.Bold;
            text.fontSize *= 1.15f;
            var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            var rt = (RectTransform)go.transform;
            layout.minHeight = layout.preferredHeight = rt.sizeDelta.y + 20f; // a little room above
            return go;
        }

        private static ToggleRow MakeToggleRow(GameObject template, Transform holder, Item item)
        {
            // Inside the inactive holder nothing on the copy runs (no Awake / OnEnable) until it is moved out.
            var go = UnityEngine.Object.Instantiate(template, holder, false);
            go.name = "Mod " + item.Label;
            foreach (var c in go.GetComponents<Component>())
                if (c != null && c.GetType().Name == "ToggleOption") UnityEngine.Object.DestroyImmediate(c);
            Component button = go.GetComponents<Component>().FirstOrDefault(c => c != null && FindField(c.GetType(), "OnClickUp") != null);
            var nameText = go.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            var knobGraphic = button != null ? FindField(button.GetType(), "mainGraphic")?.GetValue(button) as Graphic : null;
            var icon = go.transform.Find("Button/Icon")?.GetComponent<Image>();
            if (button == null || nameText == null || knobGraphic == null || icon == null)
            {
                _log.LogWarning("MODS tab: the toggle row layout is not as expected; skipping " + item.Label);
                UnityEngine.Object.DestroyImmediate(go);
                return null;
            }
            float thickness = 1.5f;
            foreach (var c in go.GetComponents<Component>())
                if (c != null && FindField(c.GetType(), "thickness")?.GetValue(c) is float t) { thickness = t; break; }
            var row = new ToggleRow { Go = go, Entry = item.Entry, Owner = item.Owner, Knob = knobGraphic.rectTransform, Icon = icon, Thickness = thickness, Name = nameText };
            AddLabel(row);
            ReplaceEvent(button, "OnClickUp", () =>
            {
                try
                {
                    bool value = !(bool)row.Entry.BoxedValue;
                    row.Entry.BoxedValue = value; // BepInEx saves the cfg file
                    ShowToggle(row, value);
                }
                catch (Exception e) { _log.LogWarning("MODS tab: could not change " + item.Label + ": " + e.Message); }
            });
            return row;
        }

        private static ColorRow MakeColorRow(GameObject template, Transform holder, Item item)
        {
            var go = UnityEngine.Object.Instantiate(template, holder, false);
            go.name = "Mod " + item.Label;
            foreach (var c in go.GetComponents<Component>())
                if (c != null && c.GetType().Name == "ColorOption") UnityEngine.Object.DestroyImmediate(c);
            var nameText = go.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            var bar = go.GetComponentInChildren<ScrollBar>(true);
            var fill = bar != null ? bar.transform.Find("F")?.GetComponent<Graphic>() : null;
            var onDrag = bar != null ? FindField(bar.GetType(), "OnDrag")?.GetValue(bar) as UnityEvent<Vector2> : null;
            if (nameText == null || bar == null || fill == null || onDrag == null)
            {
                _log.LogWarning("MODS tab: the colour row layout is not as expected; skipping " + item.Label);
                UnityEngine.Object.DestroyImmediate(go);
                return null;
            }
            // The fill follows the game's UI colour through CharacterGraphic; here it shows this entry's colour.
            foreach (var c in fill.GetComponents<Component>())
                if (c != null && c.GetType().Name == "CharacterGraphic") UnityEngine.Object.DestroyImmediate(c);
            // The game's listener (ColorOption.SaveColorValue on the copy, now gone) is switched off; ours saves the entry.
            for (int i = 0; i < onDrag.GetPersistentEventCount(); i++)
                onDrag.SetPersistentListenerState(i, UnityEventCallState.Off);
            var row = new ColorRow { Go = go, Entry = item.Entry, Owner = item.Owner, Bar = bar, Fill = fill, Name = nameText };
            AddLabel(row);
            onDrag.AddListener(v =>
            {
                try
                {
                    var old = (Color)row.Entry.BoxedValue;
                    var color = Color.HSVToRGB(Mathf.Clamp01(v.x), Mathf.Clamp01(v.y), 1f);
                    color.a = old.a;
                    row.Entry.BoxedValue = color; // BepInEx saves the cfg file
                    row.Fill.color = new Color(color.r, color.g, color.b, 1f);
                }
                catch (Exception e) { _log.LogWarning("MODS tab: could not change " + item.Label + ": " + e.Message); }
            });
            return row;
        }

        private static void RefreshLabels()
        {
            foreach (var row in Rows) if (row.Go != null) SetLabel(row);
        }

        /// <summary>The row's name in the game's language, and the entry's description as the hover text.</summary>
        private static void AddLabel(Row row)
        {
            if (HoverText != null && !string.IsNullOrEmpty(row.Entry.Description?.Description))
                row.Hover = row.Go.AddComponent<Pigeon.HoverInfoText>();
            SetLabel(row);
        }

        // No "Mod: " prefix: each mod has its own heading.
        private static void SetLabel(Row row)
        {
            string language = ModsMenuTag.Language(TextBlocks.LanguageIndex);
            string label = ModsMenuTag.Find(row.Entry.Description?.Tags, language);
            row.Name.text = string.IsNullOrEmpty(label) ? row.Entry.Definition.Key : label;
            if (row.Hover != null)
                HoverText.SetValue(row.Hover, ModsMenuTag.Description(row.Entry.Description.Tags, language, row.Entry.Description.Description));
        }

        // Same as ToggleOption.SetText: the knob sits at the right end (check mark) or the left end (cross).
        private static void ShowToggle(ToggleRow row, bool on)
        {
            var parent = row.Knob.parent as RectTransform;
            if (parent == null) return;
            float x = parent.rect.width * 0.5f - row.Knob.rect.width * 0.5f - row.Thickness;
            row.Knob.anchoredPosition = new Vector2(on ? x : -x, row.Knob.anchoredPosition.y);
            var global = AccessTools.Property(typeof(Global), "Instance")?.GetValue(null) ?? AccessTools.Field(typeof(Global), "Instance")?.GetValue(null);
            var sprite = global != null ? AccessTools.Field(typeof(Global), on ? "CheckmarkIcon" : "CrossIcon")?.GetValue(global) as Sprite : null;
            if (sprite != null) row.Icon.sprite = sprite;
        }

        // The handle at (hue, saturation) and the fill in the colour; brightness is not on the field (always 1).
        private static void ShowColor(ColorRow row, Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float _);
            row.Bar.SetScrollNoInvoke(new Vector2(h, s));
            row.Fill.color = new Color(color.r, color.g, color.b, 1f);
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        private static void ReplaceEvent(Component target, string field, Action action)
        {
            var f = FindField(target.GetType(), field);
            if (f == null || !typeof(UnityEvent).IsAssignableFrom(f.FieldType)) throw new MissingFieldException(target.GetType().Name, field);
            var ev = new UnityEvent();
            ev.AddListener(new UnityAction(action));
            f.SetValue(target, ev);
        }
    }
}
