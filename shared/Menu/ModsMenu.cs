using System;
using System.Collections.Generic;
using System.Linq;
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
    /// The game's UI types are used directly (publicized game assembly). Building is wrapped in try/catch, so a game
    /// update that changes them fails here with a log line instead of breaking the window.
    ///
    /// The first caller of <see cref="Install"/> in the process builds the tab (AppDomain data), so the tab exists
    /// once with every mod's entries.
    /// </summary>
    public static class ModsMenu
    {
        private const string BuilderKey = "MycopunkShared.ModsMenu.v1.builder";
        private const string TabName = "Mods";
        private const float Step = 70f;

        private static ManualLogSource _log;
        private static readonly List<Row> Rows = new List<Row>();
        private static Transform _items, _toggleTemplate, _colorTemplate;
        private static string[] _groupNames = Array.Empty<string>();

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
            Harmony menuHarmony = null;
            try
            {
                var domain = AppDomain.CurrentDomain;
                if (domain.GetData(BuilderKey) != null) return;
                _log = BepInEx.Logging.Logger.CreateLogSource("ModsMenu");
                menuHarmony = new Harmony("MycopunkShared.ModsMenu");
                // Without the Awake prefix there is no tab, so a failure there stops here (logged below).
                menuHarmony.CreateClassProcessor(typeof(AwakePatch)).Patch();
                // The other two only refresh the rows; the tab works without them.
                TryPatch(menuHarmony, typeof(OnOpenPatch));
                TryPatch(menuHarmony, typeof(LanguagePatch));
                // Marked only once hooked: if this attempt fails, the next mod's Install tries again.
                domain.SetData(BuilderKey, host.Info.Metadata.GUID);
            }
            catch (Exception e)
            {
                // Take back what this attempt applied, so a later attempt does not patch twice.
                try { menuHarmony?.UnpatchSelf(); }
                catch (Exception cleanup) { (_log ?? (_log = BepInEx.Logging.Logger.CreateLogSource("ModsMenu"))).LogWarning("MODS tab patch cleanup failed: " + cleanup); }
                (_log ?? (_log = BepInEx.Logging.Logger.CreateLogSource("ModsMenu"))).LogWarning("MODS tab disabled: " + e);
            }
        }

        private static void TryPatch(Harmony harmony, Type patch)
        {
            try { harmony.CreateClassProcessor(patch).Patch(); }
            catch (Exception e) { _log.LogWarning("MODS tab: " + patch.Name + " not applied: " + e.Message); }
        }

        [HarmonyPatch(typeof(PlayerOptions), nameof(PlayerOptions.Awake))]
        private static class AwakePatch
        {
            [HarmonyPrefix]
            private static void Prefix(PlayerOptions __instance)
            {
                try { Build(__instance); }
                catch (Exception e) { _log.LogWarning("MODS tab could not be built: " + e); }
            }
        }

        [HarmonyPatch(typeof(PlayerOptions), nameof(PlayerOptions.OnOpen))]
        private static class OnOpenPatch
        {
            [HarmonyPostfix]
            private static void Postfix() => OnOpened();
        }

        [HarmonyPatch(typeof(TextBlocks), nameof(TextBlocks.Initialize))]
        private static class LanguagePatch
        {
            [HarmonyPostfix]
            private static void Postfix() => RefreshLabels();
        }

        private static void OnOpened()
        {
            try
            {
                Refresh();
            }
            catch (Exception e) { _log.LogWarning("MODS tab refresh failed: " + e.Message); }
        }

        /// <summary>Rebuilds the current MODS tab after plugins or their config entries change.</summary>
        public static void Refresh()
        {
            if (_items == null || _toggleTemplate == null) return;
            var groups = CollectEntries();
            var entries = groups.SelectMany(g => g.Items)
                .Where(i => i.Entry.SettingType == typeof(bool) || _colorTemplate != null).ToArray();
            var rows = Rows.Where(r => r.Go != null && r.Go.transform.parent == _items).ToArray();
            bool same = _groupNames.SequenceEqual(groups.Select(g => g.Mod)) && rows.Length == entries.Length;
            for (int i = 0; same && i < rows.Length; i++)
                same = rows[i].Owner == entries[i].Owner && rows[i].Entry.Definition.Equals(entries[i].Entry.Definition) &&
                    rows[i].Entry.SettingType == entries[i].Entry.SettingType;
            if (same)
            {
                for (int i = 0; i < rows.Length; i++) { rows[i].Entry = entries[i].Entry; rows[i].Refresh(); }
            }
            else Populate(_items, _toggleTemplate, _colorTemplate, groups);
            RefreshLabels();
        }

        private static bool Rebind(Row row)
        {
            if (!Chainloader.PluginInfos.TryGetValue(row.Owner, out var info) || info.Instance == null) return false;
            var entry = info.Instance.Config.FirstOrDefault(p => p.Key.Equals(row.Entry.Definition)).Value;
            if (entry == null || entry.SettingType != row.Entry.SettingType) return false;
            row.Entry = entry;
            return true;
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
            var sections = po.sections;
            var buttons = po.sectionButtons;
            if (sections == null || buttons == null) { _log.LogWarning("PlayerOptions.sections / sectionButtons not found; no MODS tab."); return; }
            if (sections.Any(s => s != null && s.name == TabName)) return;

            var groups = CollectEntries();
            if (groups.Count == 0) { _log.LogInfo("No config entries are marked for the MODS tab."); return; }

            var buttonList = buttons.ToList();
            Pigeon.UI.DefaultButton graphicsButton = buttonList.FirstOrDefault(b => b != null && b.name == "Graphics");
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

            // Tab button: a copy of Graphics, one step below it; what was there moves down.
            var graphicsRect = (RectTransform)graphicsButton.transform;
            var buttonGo = UnityEngine.Object.Instantiate(graphicsButton.gameObject, graphicsRect.parent, false);
            buttonGo.name = TabName;
            var buttonRect = (RectTransform)buttonGo.transform;
            buttonRect.anchoredPosition = graphicsRect.anchoredPosition + new Vector2(0, -Step);
            foreach (var b in buttonList)
                if (b != null && b != graphicsButton && b.transform is RectTransform r && r.anchoredPosition.y <= buttonRect.anchoredPosition.y + 0.5f)
                    r.anchoredPosition += new Vector2(0, -Step);
            foreach (var c in buttonGo.GetComponentsInChildren<SetTextBlock>(true))
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            var buttonText = buttonGo.GetComponentInChildren<TextMeshProUGUI>(true);
            if (buttonText != null) buttonText.text = "MODS";
            var tabButton = (Pigeon.UI.DefaultButton)buttonGo.GetComponent(graphicsButton.GetType());
            ReplaceEvent(tabButton, () => po.SetSection(index));

            // Tab body: a copy of Debug without its items.
            var bodyGo = UnityEngine.Object.Instantiate(debugSection.gameObject, debugSection.parent, false);
            bodyGo.name = TabName;
            bodyGo.SetActive(false);
            var itemsParent = bodyGo.transform.Find("Items");
            if (itemsParent == null) { UnityEngine.Object.DestroyImmediate(bodyGo); UnityEngine.Object.DestroyImmediate(buttonGo); _log.LogWarning("Debug/Items not found; no MODS tab."); return; }
            for (int i = itemsParent.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(itemsParent.GetChild(i).gameObject);

            int added = Populate(itemsParent, toggleTemplate, colorTemplate, groups);

            // Register with the window before its Awake sizes sectionPositions.
            po.sections = sections.Concat(new[] { (RectTransform)bodyGo.transform }).ToArray();
            po.sectionButtons = buttons.Concat(new[] { tabButton }).ToArray();
            _items = itemsParent;
            _toggleTemplate = toggleTemplate;
            _colorTemplate = colorTemplate;
            _log.LogInfo("MODS tab added with " + added + " entries in " + groups.Count + " groups.");
        }

        private static int Populate(Transform items, Transform toggleTemplate, Transform colorTemplate, List<Group> groups)
        {
            var holder = new GameObject("ModsMenu holder");
            holder.SetActive(false);
            var rows = new List<Row>();
            try
            {
                foreach (var group in groups)
                {
                    MakeHeading(toggleTemplate.gameObject, holder.transform, group.Mod);
                    foreach (var item in group.Items)
                    {
                        Row row = item.Entry.SettingType == typeof(bool)
                            ? MakeToggleRow(toggleTemplate.gameObject, holder.transform, item)
                            : colorTemplate != null ? MakeColorRow(colorTemplate.gameObject, holder.transform, item) : null;
                        if (row == null) continue;
                        rows.Add(row);
                        row.Refresh();
                    }
                }
                for (int i = items.childCount - 1; i >= 0; i--)
                {
                    var child = items.GetChild(i).gameObject;
                    child.SetActive(false);
                    UnityEngine.Object.Destroy(child);
                }
                Rows.RemoveAll(r => r.Go == null || r.Go.transform.parent == items);
                while (holder.transform.childCount > 0) holder.transform.GetChild(0).SetParent(items, false);
                Rows.AddRange(rows);
                _groupNames = groups.Select(g => g.Mod).ToArray();
                return rows.Count;
            }
            finally { UnityEngine.Object.Destroy(holder); }
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
            foreach (var c in go.GetComponents<ToggleOption>())
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            var button = go.GetComponent<Pigeon.UI.Button>();
            var nameText = go.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            // The game's row uses a ColorButton (ToggleOption.SetText). Other button kinds (DefaultButton, SpriteButton...)
            // each declare their own mainGraphic with no common base, so any other kind is read by name (reflection).
            var knobGraphic = button is Pigeon.UI.ColorButton colorButton ? colorButton.mainGraphic
                : button != null ? AccessTools.Field(button.GetType(), nameof(Pigeon.UI.ColorButton.mainGraphic))?.GetValue(button) as Graphic : null;
            var icon = go.transform.Find("Button/Icon")?.GetComponent<Image>();
            if (button == null || nameText == null || knobGraphic == null || icon == null)
            {
                _log.LogWarning("MODS tab: the toggle row layout is not as expected; skipping " + item.Label);
                UnityEngine.Object.DestroyImmediate(go);
                return null;
            }
            // Same as ToggleOption.SetText: the row's outline thickness, 1.5 without one.
            float thickness = go.TryGetComponent<Pigeon.UI.OutlineGraphic>(out var outline) ? outline.thickness : 1.5f;
            var row = new ToggleRow { Go = go, Entry = item.Entry, Owner = item.Owner, Knob = knobGraphic.rectTransform, Icon = icon, Thickness = thickness, Name = nameText };
            AddLabel(row);
            ReplaceEvent(button, () =>
            {
                try
                {
                    if (!Rebind(row)) return;
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
            foreach (var c in go.GetComponents<ColorOption>())
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            var nameText = go.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            var bar = go.GetComponentInChildren<ScrollBar>(true);
            var fill = bar != null ? bar.transform.Find("F")?.GetComponent<Graphic>() : null;
            var onDrag = bar != null ? bar.OnDrag : null;
            if (nameText == null || bar == null || fill == null || onDrag == null)
            {
                _log.LogWarning("MODS tab: the colour row layout is not as expected; skipping " + item.Label);
                UnityEngine.Object.DestroyImmediate(go);
                return null;
            }
            // The fill follows the game's UI colour through CharacterGraphic; here it shows this entry's colour.
            foreach (var c in fill.GetComponents<CharacterGraphic>())
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            // The game's listener (ColorOption.SaveColorValue on the copy, now gone) is switched off; ours saves the entry.
            for (int i = 0; i < onDrag.GetPersistentEventCount(); i++)
                onDrag.SetPersistentListenerState(i, UnityEventCallState.Off);
            var row = new ColorRow { Go = go, Entry = item.Entry, Owner = item.Owner, Bar = bar, Fill = fill, Name = nameText };
            AddLabel(row);
            onDrag.AddListener(v =>
            {
                try
                {
                    if (!Rebind(row)) return;
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
            if (!string.IsNullOrEmpty(row.Entry.Description?.Description))
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
                row.Hover.text = ModsMenuTag.Description(row.Entry.Description.Tags, language, row.Entry.Description.Description);
        }

        // Same as ToggleOption.SetText: the knob sits at the right end (check mark) or the left end (cross).
        private static void ShowToggle(ToggleRow row, bool on)
        {
            var parent = row.Knob.parent as RectTransform;
            if (parent == null) return;
            float x = parent.rect.width * 0.5f - row.Knob.rect.width * 0.5f - row.Thickness;
            row.Knob.anchoredPosition = new Vector2(on ? x : -x, row.Knob.anchoredPosition.y);
            var global = Global.Instance;
            var sprite = global != null ? (on ? global.CheckmarkIcon : global.CrossIcon) : null;
            if (sprite != null) row.Icon.sprite = sprite;
        }

        // The handle at (hue, saturation) and the fill in the colour; brightness is not on the field (always 1).
        private static void ShowColor(ColorRow row, Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float _);
            row.Bar.SetScrollNoInvoke(new Vector2(h, s));
            row.Fill.color = new Color(color.r, color.g, color.b, 1f);
        }

        // A new OnClickUp, so the copy drops the listeners it was cloned with (the game's SetSection / SaveValueToggle).
        private static void ReplaceEvent(Pigeon.UI.Button target, Action action)
        {
            var ev = new UnityEvent();
            ev.AddListener(new UnityAction(action));
            target.OnClickUp = ev;
        }
    }
}
