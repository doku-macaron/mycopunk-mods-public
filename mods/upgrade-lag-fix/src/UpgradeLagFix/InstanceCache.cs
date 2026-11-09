using System.Collections.Generic;

namespace UpgradeLagFix
{
    /// <summary>
    /// Index for PlayerData.GetUpgradeInstanceFromID (InstanceID -> UpgradeInstance).
    ///
    /// Correctness rules (the cached answer must equal what the original linear scan returns):
    /// - A hit is returned only if the cached instance is still reachable at
    ///   PlayerData.Instance.data[key] -> UpgradeInfo -> Instances, and still reports the same ID.
    /// - Anything else (unknown ID, stale entry) falls through to the original method.
    ///   If the original then finds an instance, the whole index is rebuilt.
    /// - The rebuild walks data in the exact order the original does and keeps the first match.
    ///   IDs that occur more than once are excluded, so the original always handles them.
    /// - If the rebuild meets a null element (where the original would throw), it stops
    ///   indexing there; IDs past that point keep going to the original.
    /// - The index is dropped whenever PlayerData.Instance or its data dictionary is replaced.
    /// </summary>
    internal static class InstanceCache
    {
        private struct Entry
        {
            public UpgradeInstance Instance;
            public int Key;
            public UpgradeInfo Info;
            public int InfoIndex;
            public int InstanceIndex;
        }

        private static readonly Dictionary<int, Entry> Map = new Dictionary<int, Entry>(4096);
        private static readonly HashSet<int> Duplicates = new HashSet<int>();
        // IDs the index cannot answer (duplicates, or past an incomplete rebuild).
        // They always go to the original method and never trigger another rebuild.
        private static readonly HashSet<int> Bypass = new HashSet<int>();
        private static PlayerData _ownerPlayerData;
        private static Dictionary<int, List<UpgradeInfo>> _ownerData;

        public static int IndexedCount => Map.Count;
        public static int DuplicateCount => Duplicates.Count;
        public static int BypassCount => Bypass.Count;
        public static bool LastRebuildIncomplete { get; private set; }

        public static void Clear()
        {
            Map.Clear();
            Duplicates.Clear();
            Bypass.Clear();
            _ownerPlayerData = null;
            _ownerData = null;
        }

        /// <summary>Returns true and sets result when the cache can answer exactly.</summary>
        public static bool TryGet(int id, out UpgradeInstance result)
        {
            result = null;
            var pd = PlayerData.Instance;
            if (pd == null) return false;
            var data = pd.data;
            if (data == null) return false;
            if (!ReferenceEquals(pd, _ownerPlayerData) || !ReferenceEquals(data, _ownerData))
            {
                Map.Clear();
                Duplicates.Clear();
                Bypass.Clear();
                _ownerPlayerData = pd;
                _ownerData = data;
                Stats.CacheResets++;
            }

            if (!Map.TryGetValue(id, out var e)) return false;
            if (!IsStillReachable(ref e, data, id))
            {
                Map.Remove(id);
                Stats.CacheStale++;
                return false;
            }
            result = e.Instance;
            return true;
        }

        private static bool IsStillReachable(ref Entry e, Dictionary<int, List<UpgradeInfo>> data, int id)
        {
            var inst = e.Instance;
            if (inst == null || inst.InstanceID != id) return false;
            if (!data.TryGetValue(e.Key, out var infos) || infos == null) return false;

            if (e.InfoIndex >= infos.Count || !ReferenceEquals(infos[e.InfoIndex], e.Info))
            {
                int idx = IndexOfRef(infos, e.Info);
                if (idx < 0) return false;
                e.InfoIndex = idx;
                Map[id] = e;
            }

            var instances = e.Info.Instances;
            if (instances == null) return false;
            if (e.InstanceIndex >= instances.Count || !ReferenceEquals(instances[e.InstanceIndex], inst))
            {
                int idx = IndexOfRef(instances, inst);
                if (idx < 0) return false;
                e.InstanceIndex = idx;
                Map[id] = e;
            }
            return true;
        }

        private static int IndexOfRef<T>(List<T> list, T item) where T : class
        {
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], item)) return i;
            return -1;
        }

        /// <summary>
        /// Called after the original method ran for an ID the cache could not answer.
        /// A non-null result means the index is stale, so rebuild it once.
        /// </summary>
        public static void OnOriginalResult(int id, UpgradeInstance originalResult)
        {
            if (originalResult == null || Bypass.Contains(id)) return;
            Rebuild();
            if (!Map.ContainsKey(id)) Bypass.Add(id);
        }

        /// <summary>Rebuilds the whole index, mirroring the iteration order of the original method.</summary>
        public static void Rebuild()
        {
            var pd = PlayerData.Instance;
            if (pd == null) return;
            var data = pd.data;
            Map.Clear();
            Duplicates.Clear();
            Bypass.Clear();
            _ownerPlayerData = pd;
            _ownerData = data;
            LastRebuildIncomplete = false;
            Stats.CacheRebuilds++;
            if (data == null) return;

            foreach (var kv in data)
            {
                var infos = kv.Value;
                if (infos == null) { LastRebuildIncomplete = true; return; }
                for (int i = 0; i < infos.Count; i++)
                {
                    var info = infos[i];
                    if (info == null) { LastRebuildIncomplete = true; return; }
                    var instances = info.Instances;
                    if (instances == null) continue;
                    for (int j = 0; j < instances.Count; j++)
                    {
                        var inst = instances[j];
                        if (inst == null) { LastRebuildIncomplete = true; return; }
                        int id = inst.InstanceID;
                        if (Duplicates.Contains(id)) continue;
                        if (Map.ContainsKey(id))
                        {
                            Map.Remove(id);
                            Duplicates.Add(id);
                            continue;
                        }
                        Map[id] = new Entry { Instance = inst, Key = kv.Key, Info = info, InfoIndex = i, InstanceIndex = j };
                    }
                }
            }
        }
    }
}
