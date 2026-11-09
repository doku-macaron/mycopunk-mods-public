using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

        private static readonly string[] StatColumns =
        {
            "enemies_killed", "targets_killed", "damage_dealt", "damage_taken", "healing", "friendly_fire",
            "deaths", "repairs", "resources", "upgrades",
        };

        private static readonly FrameStats Frames = new FrameStats();
        private sealed class PendingRow
        {
            public string Row;
            public long Offset = -1;
            public byte[] Bytes;
        }
        private static readonly Queue<PendingRow> Pending = new Queue<PendingRow>();
        private static float _nextRetry;
        private static bool _active;
        internal static bool Active => _active || Pending.Count != 0;
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

            Patch(harmony, typeof(StartedPatch), nameof(MissionManager), nameof(MissionManager.OnMissionStarted_Client));
            Patch(harmony, typeof(CompletedPatch), nameof(PlayerData), nameof(PlayerData.OnMissionCompleted));
            Patch(harmony, typeof(FailedPatch), nameof(PlayerData), nameof(PlayerData.OnMissionFailed));
            Patch(harmony, typeof(FinalizedPatch), nameof(MissionManager), nameof(MissionManager.FinalizeMissionData));
            Patch(harmony, typeof(DestroyedPatch), nameof(MissionManager), nameof(MissionManager.OnDestroy));
        }

        // One patch class at a time, so one that fails does not stop the others.
        private static void Patch(Harmony harmony, Type patch, string type, string method)
        {
            try
            {
                harmony.CreateClassProcessor(patch).Patch();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not hook " + type + "." + method + ": " + e.Message);
            }
        }

        [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.OnMissionStarted_Client))]
        private static class StartedPatch
        {
            [HarmonyPostfix]
            private static void Postfix(MissionManager __instance) => OnStarted(__instance);
        }

        [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.OnMissionCompleted))]
        private static class CompletedPatch
        {
            [HarmonyPrefix]
            private static void Prefix() => OnCompleted();
        }

        [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.OnMissionFailed))]
        private static class FailedPatch
        {
            [HarmonyPrefix]
            private static void Prefix() => OnFailed();
        }

        [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.FinalizeMissionData))]
        private static class FinalizedPatch
        {
            [HarmonyPostfix]
            private static void Postfix(MissionManager __instance) => OnFinalized(__instance);
        }

        // First, so DPS Checker (which clears its numbers in its own OnDestroy prefix) has not reset yet.
        [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.OnDestroy))]
        private static class DestroyedPatch
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static void Prefix(MissionManager __instance) => OnDestroyed(__instance);
        }

        public static void Tick()
        {
            if (_active) Frames.Add(Time.unscaledDeltaTime * 1000f);
            if (Pending.Count != 0 && Time.unscaledTime >= _nextRetry) FlushPending();
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
                Pending.Enqueue(new PendingRow { Row = BuildRow(manager, outcome, finalized) });
                FlushPending();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Mission log row could not be prepared: " + e);
            }
        }

        internal static void FlushPending()
        {
            _nextRetry = Time.unscaledTime + 5f;
            while (Pending.Count != 0)
            {
                var pending = Pending.Peek();
                try
                {
                    using (var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                    {
                        if (pending.Offset < 0)
                        {
                            pending.Offset = stream.Length;
                            pending.Bytes = new UTF8Encoding(false).GetBytes((pending.Offset == 0 ? Header() + "\n" : "") + pending.Row + "\n");
                        }
                        if (stream.Length < pending.Offset) throw new IOException("Mission log was shortened while a row was pending.");
                        stream.Position = pending.Offset;
                        int existing = (int)Math.Min(stream.Length - pending.Offset, pending.Bytes.Length);
                        for (int i = 0; i < existing; i++)
                            if (stream.ReadByte() != pending.Bytes[i]) throw new IOException("Mission log changed while a row was pending.");
                        if (existing < pending.Bytes.Length)
                        {
                            stream.Position = pending.Offset + existing;
                            stream.Write(pending.Bytes, existing, pending.Bytes.Length - existing);
                            stream.Flush();
                        }
                    }
                    Pending.Dequeue();
                    Plugin.Log.LogInfo("Mission log row saved.");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Mission log row pending; retrying: " + e.Message);
                    break;
                }
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
            MissionManager.MissionPlayerData data = manager.missionData;
            PlayerData pd = null;
            try { pd = PlayerData.Instance; } catch { }
            // In column order. FinalizeMissionData subtracts the start value of each from the PlayerData field of the
            // same name (the last two have no PlayerData counterpart).
            double[] values =
            {
                data.enemiesKilled, data.targetsKilled, data.damageDealt, data.damageTaken, data.healingDealt,
                data.friendlyFireDamageDealt, data.deaths, data.playersRepaired, data.resourcesCollected, data.upgradesCollected,
            };
            double?[] totals = pd == null ? null : new double?[]
            {
                pd.enemiesKilled, pd.targetsKilled, pd.damageDealt, pd.damageTaken, pd.healingDealt,
                pd.friendlyFireDamageDealt, pd.deaths, pd.playersRepaired, null, null,
            };
            for (int i = 0; i < values.Length; i++)
            {
                string text = "";
                if (finalized) text = Num(values[i], "0.##");
                else if (totals != null && totals[i].HasValue) text = Num(totals[i].Value - values[i], "0.##");
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
                if (GameManager.players != null)
                    foreach (var p in GameManager.players)
                        if (p != null) result.Add(p);
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
