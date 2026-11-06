using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using MycopunkShared;
using Pigeon.Movement;
using Unity.Netcode;
using UnityEngine;

namespace MycopunkMissionLog
{
    /// <summary>
    /// One TSV row per mission. Start: MissionManager.OnMissionStarted_Client. End: FinalizeMissionData, which the
    /// game runs after PlayerData.OnMissionCompleted / OnMissionFailed on both outcomes and which turns the stats
    /// it noted at the start into this mission's numbers. Leaving before that (MissionManager.OnDestroy) writes a
    /// row with outcome "left" and the numbers computed the same way. Only reads; the save is never touched.
    /// </summary>
    internal static class MissionRecorder
    {
        private const int Mates = 3;

        // MissionPlayerData fields, in column order. FinalizeMissionData subtracts the start value of each from the
        // PlayerData field of the same name (the last two have no PlayerData counterpart).
        private static readonly string[] StatFields =
        {
            "enemiesKilled", "targetsKilled", "damageDealt", "damageTaken", "healingDealt", "friendlyFireDamageDealt",
            "deaths", "playersRepaired", "resourcesCollected", "upgradesCollected",
        };
        private static readonly string[] StatColumns =
        {
            "enemies_killed", "targets_killed", "damage_dealt", "damage_taken", "healing", "friendly_fire",
            "deaths", "repairs", "resources", "upgrades",
        };

        private static readonly FieldInfo MissionDataField = AccessTools.Field(typeof(MissionManager), "missionData");
        private static readonly FieldInfo PlayersField = AccessTools.Field(typeof(GameManager), "players");

        private static readonly FrameStats Frames = new FrameStats();
        private static bool _active;
        private static string _outcome;
        private static DateTime _startLocal;
        private static float _startReal;
        private static int _startGen2;

        internal static string FilePath { get; private set; }

        public static void Setup(Harmony harmony)
        {
            var dir = Path.Combine(Paths.BepInExRootPath, "MissionLog");
            Directory.CreateDirectory(dir);
            FilePath = Path.Combine(dir, "missions-v1.tsv");

            Patch(harmony, typeof(MissionManager), "OnMissionStarted_Client", postfix: nameof(OnStarted));
            Patch(harmony, typeof(PlayerData), "OnMissionCompleted", prefix: nameof(OnCompleted));
            Patch(harmony, typeof(PlayerData), "OnMissionFailed", prefix: nameof(OnFailed));
            Patch(harmony, typeof(MissionManager), "FinalizeMissionData", postfix: nameof(OnFinalized));
            // First, so DPS Checker (which clears its numbers in its own OnDestroy prefix) has not reset yet.
            Patch(harmony, typeof(MissionManager), "OnDestroy", prefix: nameof(OnDestroyed), priority: Priority.First);
        }

        private static void Patch(Harmony harmony, Type type, string method, string prefix = null, string postfix = null, int priority = Priority.Normal)
        {
            try
            {
                var target = AccessTools.Method(type, method);
                if (target == null) { Plugin.Log.LogWarning(type.Name + "." + method + " not found; the mission log may miss rows."); return; }
                harmony.Patch(target,
                    prefix: prefix != null ? new HarmonyMethod(typeof(MissionRecorder), prefix) { priority = priority } : null,
                    postfix: postfix != null ? new HarmonyMethod(typeof(MissionRecorder), postfix) { priority = priority } : null);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not hook " + type.Name + "." + method + ": " + e.Message);
            }
        }

        public static void Tick()
        {
            if (_active) Frames.Add(Time.unscaledDeltaTime * 1000f);
        }

        private static void OnStarted(MissionManager __instance)
        {
            if (__instance != MissionManager.Instance) return;
            _active = true;
            _outcome = null;
            _startLocal = DateTime.Now;
            _startReal = Time.realtimeSinceStartup;
            _startGen2 = GC.CollectionCount(2);
            Frames.Clear();
        }

        private static void OnCompleted() { if (_active) _outcome = "success"; }

        private static void OnFailed() { if (_active) _outcome = "fail"; }

        private static void OnFinalized(MissionManager __instance)
        {
            if (!_active || __instance != MissionManager.Instance) return;
            Finish(__instance, _outcome ?? "unknown", finalized: true);
        }

        private static void OnDestroyed(MissionManager __instance)
        {
            if (!_active || __instance != MissionManager.Instance) return;
            Finish(__instance, "left", finalized: false);
        }

        private static void Finish(MissionManager manager, string outcome, bool finalized)
        {
            _active = false;
            try
            {
                string row = BuildRow(manager, outcome, finalized);
                bool header = !File.Exists(FilePath);
                using (var w = new StreamWriter(FilePath, append: true, encoding: new UTF8Encoding(false)))
                {
                    w.NewLine = "\n";
                    if (header) w.WriteLine(Header());
                    w.WriteLine(row);
                }
                Plugin.Log.LogInfo("Mission logged (" + outcome + ", " + Frames.Frames + " frames).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Mission log row failed: " + e);
            }
        }

        private static string Header()
        {
            var cols = new List<string>
            {
                "start", "duration_s", "outcome", "mission_id", "mission", "region_id", "region", "seed", "threat", "intensity",
                "players", "character", "weapon1", "weapon2", "grenade",
            };
            cols.AddRange(StatColumns);
            cols.Add("my_max_dps");
            for (int i = 1; i <= Mates; i++)
                cols.AddRange(new[] { "mate" + i + "_name", "mate" + i + "_character", "mate" + i + "_max_dps", "mate" + i + "_kills", "mate" + i + "_estimated" });
            cols.AddRange(new[] { "frames", "fps_avg", "fps_1pct_low", "worst_ms", "over25ms", "over50ms", "over100ms", "gen2_gc" });
            return string.Join("\t", cols);
        }

        private static string BuildRow(MissionManager manager, string outcome, bool finalized)
        {
            var c = new List<string>();
            c.Add(_startLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            c.Add(Num(Time.realtimeSinceStartup - _startReal, "0"));
            c.Add(outcome);

            Safe(c, () =>
            {
                var mission = MissionManager.CurrentMission.Mission;
                return mission != null ? new[] { mission.ID, mission.MissionName } : new[] { "", "" };
            }, 2);
            Safe(c, () =>
            {
                var region = LevelData.Instance != null ? LevelData.Instance.Region : null;
                return region != null ? new[] { region.NameID, region.RegionName } : new[] { "", "" };
            }, 2);
            Safe(c, () => new[] { MissionManager.MissionSeed.ToString("X8", CultureInfo.InvariantCulture) }, 1);
            Safe(c, () => new[] { GameManager.ThreatLevel.ToString(CultureInfo.InvariantCulture), GameManager.IntensityLevel.ToString(CultureInfo.InvariantCulture) }, 2);

            List<Player> players = Players();
            c.Add(players.Count.ToString(CultureInfo.InvariantCulture));
            Safe(c, () =>
            {
                Player me = Player.LocalPlayer;
                if (me == null) return new[] { "", "", "", "" };
                return new[] { me.Character != null ? me.Character.APIName : "", GearName(me, 0), GearName(me, 1), GearName(me, 3) };
            }, 4);

            AddStats(c, manager, finalized);

            var dps = ModChannels.Get<Func<ulong, string>>(ModChannels.DpsStatsKey);
            ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            c.Add(DpsText(dps, localId, out _, out _));

            int mates = 0;
            foreach (var p in players)
            {
                if (p == null || p.IsLocalPlayer || mates >= Mates) continue;
                mates++;
                string name = "", character = "";
                try
                {
                    var info = p.GetPlayerInfo();
                    name = info.name;
                    character = info.character != null ? info.character.APIName : "";
                }
                catch { }
                c.Add(Clean(name));
                c.Add(Clean(character));
                c.Add(DpsText(dps, p.OwnerClientId, out string kills, out string estimated));
                c.Add(kills);
                c.Add(estimated);
            }
            for (; mates < Mates; mates++) c.AddRange(new[] { "", "", "", "", "" });

            c.Add(Frames.Frames.ToString(CultureInfo.InvariantCulture));
            c.Add(Num(Frames.AverageFps, "0.0"));
            c.Add(Num(Frames.OnePercentLowFps, "0.0"));
            c.Add(Num(Frames.WorstMs, "0.0"));
            c.Add(Frames.Over25.ToString(CultureInfo.InvariantCulture));
            c.Add(Frames.Over50.ToString(CultureInfo.InvariantCulture));
            c.Add(Frames.Over100.ToString(CultureInfo.InvariantCulture));
            c.Add((GC.CollectionCount(2) - _startGen2).ToString(CultureInfo.InvariantCulture));

            for (int i = 0; i < c.Count; i++) c[i] = Clean(c[i]);
            return string.Join("\t", c);
        }

        // After FinalizeMissionData the struct holds this mission's numbers. Before it (left early) it still holds
        // the values noted at the start, so do the same subtraction from PlayerData here, without touching it.
        private static void AddStats(List<string> c, MissionManager manager, bool finalized)
        {
            object data = null;
            try { data = MissionDataField != null ? MissionDataField.GetValue(manager) : null; } catch { }
            PlayerData pd = null;
            try { pd = PlayerData.Instance; } catch { }
            foreach (var name in StatFields)
            {
                string text = "";
                try
                {
                    var f = data != null ? data.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
                    if (f != null)
                    {
                        double value = Convert.ToDouble(f.GetValue(data), CultureInfo.InvariantCulture);
                        if (finalized) text = Num(value, "0.##");
                        else
                        {
                            var g = pd != null ? AccessTools.Field(typeof(PlayerData), name) : null;
                            if (g != null) text = Num(Convert.ToDouble(g.GetValue(pd), CultureInfo.InvariantCulture) - value, "0.##");
                        }
                    }
                }
                catch { }
                c.Add(text);
            }
        }

        private static string DpsText(Func<ulong, string> dps, ulong id, out string kills, out string estimated)
        {
            kills = estimated = "";
            if (dps == null) return "";
            string raw;
            try { raw = dps(id); } catch { return ""; }
            if (!ModChannels.TryParseDps(raw, out float max, out int k, out bool est)) return "";
            kills = k.ToString(CultureInfo.InvariantCulture);
            estimated = est ? "1" : "0";
            return Num(max, "0");
        }

        private static List<Player> Players()
        {
            var result = new List<Player>();
            try
            {
                if (PlayersField != null && PlayersField.GetValue(null) is System.Collections.IEnumerable list)
                    foreach (var o in list)
                        if (o is Player p && p != null) result.Add(p);
            }
            catch { }
            return result;
        }

        private static string GearName(Player player, int index)
        {
            var gear = player.Gear;
            if (gear == null || index >= gear.Length || gear[index] == null) return "";
            return gear[index].Info != null ? gear[index].Info.APIName : "";
        }

        private static void Safe(List<string> c, Func<string[]> get, int count)
        {
            string[] values = null;
            try { values = get(); } catch { }
            for (int i = 0; i < count; i++) c.Add(values != null && i < values.Length ? values[i] ?? "" : "");
        }

        private static string Num(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

        private static string Clean(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
