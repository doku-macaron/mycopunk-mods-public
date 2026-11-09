using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;

namespace MycopunkShared
{
    public static class ModSetup
    {
        public static void Try(ManualLogSource log, string what, Action action)
        {
            try { action(); }
            catch (Exception e) { log.LogError("Could not set up " + what + "; it is off: " + e); }
        }

        /// <summary>
        /// Applies patch classes (<c>[HarmonyPatch]</c>) that only work together. If one fails, the patches of these
        /// classes that were already applied are taken back (other patches of the same Harmony instance stay), and the
        /// exception is thrown on.
        /// </summary>
        public static void PatchTogether(Harmony harmony, params Type[] patches)
        {
            var tried = new HashSet<Type>();
            try
            {
                foreach (var patch in patches)
                {
                    tried.Add(patch);
                    harmony.CreateClassProcessor(patch).Patch();
                }
            }
            catch (Exception e)
            {
                var cleanup = new List<Exception>();
                foreach (var original in harmony.GetPatchedMethods().ToList())
                {
                    var info = Harmony.GetPatchInfo(original);
                    if (info == null) continue;
                    foreach (var applied in info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers))
                    {
                        if (applied.owner != harmony.Id || !tried.Contains(applied.PatchMethod.DeclaringType)) continue;
                        try { harmony.Unpatch(original, applied.PatchMethod); }
                        catch (Exception c) { cleanup.Add(c); }
                    }
                }
                if (cleanup.Count > 0)
                    throw new AggregateException("Patching failed, and taking back the applied patches failed too.", new[] { e }.Concat(cleanup));
                throw;
            }
        }
    }
}
