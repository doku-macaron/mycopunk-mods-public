using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using Pigeon.Movement;
using Unity.Netcode;
using UnityEngine;

namespace MycopunkHealthNumbers
{
    /// <summary>
    /// Writes the Player prefab's serialized stats (the base values every class starts from) next to the
    /// local player's current values (after upgrades) to a Markdown file.
    /// Player.ApplyUpgrades copies the base values from NetworkConfig.PlayerPrefab, so the prefab is the ground truth.
    /// </summary>
    internal static class StatDump
    {
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Write()
        {
            Player local = Player.LocalPlayer;
            Player prefab = GetPrefab();
            if (prefab == null && local == null)
            {
                Plugin.Log.LogWarning("Stat dump: neither the Player prefab nor the local player is available.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("# Mycopunk player stats");
            sb.AppendLine();
            sb.AppendLine($"- Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- Game version: {Application.version}");
            sb.AppendLine($"- Class: {(local != null ? DescribeCharacter(local) : "(no local player)")}");
            sb.AppendLine("- base = Player prefab (NetworkConfig.PlayerPrefab), current = local player after upgrades. Rows marked * differ.");
            sb.AppendLine();

            AppendTable(sb, "Player", typeof(Player), prefab, local);

            FieldInfo uv = typeof(Player).GetField("upgradeVariables", InstanceFields);
            if (uv != null)
            {
                object uvBase = prefab != null ? uv.GetValue(prefab) : null;
                object uvCur = local != null ? uv.GetValue(local) : null;
                AppendTable(sb, "Player.upgradeVariables", uv.FieldType, uvBase, uvCur);
            }

            if (local != null)
                AppendGear(sb, local);

            string dir = Path.Combine(Paths.BepInExRootPath, "HealthNumbers");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"stats-{DateTime.Now:yyyyMMdd-HHmmss}.md");
            File.WriteAllText(path, sb.ToString().Replace("\r\n", "\n"));
            Plugin.Log.LogInfo($"Stat dump written to {path}");
        }

        private static Player GetPrefab()
        {
            try
            {
                GameObject go = NetworkManager.Singleton != null ? NetworkManager.Singleton.NetworkConfig?.PlayerPrefab : null;
                return go != null ? go.GetComponent<Player>() : null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Stat dump: could not read the Player prefab. {e.Message}");
                return null;
            }
        }

        private static string DescribeCharacter(Player player)
        {
            try
            {
                object c = AccessProperty(player, "Character");
                if (c is UnityEngine.Object o && o != null)
                    return o.name;
                return c?.ToString() ?? "(unknown)";
            }
            catch
            {
                return "(unknown)";
            }
        }

        private static object AccessProperty(object obj, string name)
        {
            PropertyInfo p = obj.GetType().GetProperty(name, InstanceFields);
            return p?.GetValue(obj);
        }

        private static void AppendTable(StringBuilder sb, string title, Type type, object baseObj, object curObj)
        {
            sb.AppendLine($"## {title}");
            sb.AppendLine();
            sb.AppendLine("| Field | Base | Current | |");
            sb.AppendLine("|---|---|---|---|");
            foreach (FieldInfo f in SerializedSimpleFields(type))
            {
                string b = baseObj != null ? Format(f.GetValue(baseObj)) : "";
                string c = curObj != null ? Format(f.GetValue(curObj)) : "";
                string diff = baseObj != null && curObj != null && b != c ? "*" : "";
                sb.AppendLine($"| `{f.Name}` | {b} | {c} | {diff} |");
            }
            sb.AppendLine();
        }

        /// <summary>
        /// Lists every equipped gear with all its numeric fields, including runtime ones (e.g. Carver blood,
        /// Shocklance pierce count) and the fields of game-defined structs such as CarverData / ShocklanceData.
        /// Values are after upgrades, so compare dumps taken with different loadouts.
        /// </summary>
        private static void AppendGear(StringBuilder sb, Player player)
        {
            if (!(AccessProperty(player, "Gear") is System.Collections.IEnumerable gears))
                return;
            int slot = 0;
            foreach (object gear in gears)
            {
                int i = slot++;
                if (gear == null || (gear is UnityEngine.Object uo && uo == null))
                    continue;
                string name = gear is UnityEngine.Object o ? o.name : gear.ToString();
                sb.AppendLine($"## Gear {i}: {name} ({gear.GetType().Name})");
                sb.AppendLine();
                sb.AppendLine("| Field | Value |");
                sb.AppendLine("|---|---|");
                AppendFieldsRecursive(sb, gear, gear.GetType(), "", 0);
                sb.AppendLine();
            }
        }

        private static void AppendFieldsRecursive(StringBuilder sb, object obj, Type type, string prefix, int depth)
        {
            Assembly game = typeof(Player).Assembly;
            var chain = new List<Type>();
            for (Type t = type; t != null && t.Assembly == game; t = t.BaseType)
                chain.Insert(0, t);

            foreach (Type t in chain)
            {
                foreach (FieldInfo f in t.GetFields(InstanceFields | BindingFlags.DeclaredOnly))
                {
                    Type ft = f.FieldType;
                    object v;
                    try { v = f.GetValue(obj); }
                    catch { continue; }
                    string fname = prefix + CleanName(f.Name);
                    if (IsSimple(ft))
                        sb.AppendLine($"| `{fname}` | {Format(v)} |");
                    else if (depth < 2 && ft.IsValueType && !ft.IsPrimitive && ft.Assembly == game && v != null)
                        AppendFieldsRecursive(sb, v, ft, fname + ".", depth + 1);
                }
            }
        }

        private static string CleanName(string n)
        {
            // <Foo>k__BackingField -> Foo
            if (n.StartsWith("<") && n.Contains(">"))
                return n.Substring(1, n.IndexOf('>') - 1);
            return n;
        }

        private static IEnumerable<FieldInfo> SerializedSimpleFields(Type type)
        {
            // Walk up to (not including) Unity / Netcode base classes so inherited game fields are included.
            var types = new List<Type>();
            for (Type t = type; t != null && t.Assembly == type.Assembly; t = t.BaseType)
                types.Insert(0, t);

            foreach (Type t in types)
            {
                foreach (FieldInfo f in t.GetFields(InstanceFields | BindingFlags.DeclaredOnly))
                {
                    bool serialized = (f.IsPublic && !f.IsNotSerialized) || f.IsDefined(typeof(SerializeField), false);
                    if (serialized && IsSimple(f.FieldType))
                        yield return f;
                }
            }
        }

        private static bool IsSimple(Type t) =>
            t == typeof(float) || t == typeof(int) || t == typeof(bool) || t.IsEnum ||
            t == typeof(Vector2) || t == typeof(Vector3);

        private static string Format(object v)
        {
            switch (v)
            {
                case float f:
                    return f.ToString("0.#####", CultureInfo.InvariantCulture);
                case Vector2 v2:
                    return $"({Format(v2.x)}, {Format(v2.y)})";
                case Vector3 v3:
                    return $"({Format(v3.x)}, {Format(v3.y)}, {Format(v3.z)})";
                case null:
                    return "null";
                default:
                    return Convert.ToString(v, CultureInfo.InvariantCulture);
            }
        }
    }
}
