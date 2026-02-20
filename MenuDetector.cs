using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PlayFab;
using GorillaNetworking;
using UnityEngine;

namespace SignalSafetyMenu
{
    public static class MenuDetector
    {
        private static readonly Dictionary<string, string> KnownMenus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "iiMenu",         "ii's Stupid Menu" },
            { "iisStupidMenu",  "ii's Stupid Menu" },
            { "Bark",           "Bark Menu" },
            { "BarkMenu",       "Bark Menu" },
            { "Aspect",         "Aspect Menu" },
            { "AspectMenu",     "Aspect Menu" },
            { "Lunacy",         "Lunacy" },
            { "GoldMenu",       "Gold Menu" },
            { "MiiMenu",        "Mii Menu" },
            { "Bobcat",         "Bobcat" },
            { "BobcatMenu",     "Bobcat" },
            { "Gecko",          "Gecko Menu" },
            { "GTag_AntiCheat", "Anti-Cheat Mod" },
            { "PigeonClient",   "Pigeon Client" },
            { "MonkeMenuV2",    "Monke Menu" },
            { "DaMonkeMenu",    "Da Monke" },
            { "CheatMenu",      "Generic Cheat" },
        };

        private static readonly string[] KnownHarmonyIds =
        {
            "org.iidk.gorillatag",
            "com.bark.gorillatag",
            "com.aspect.gorillatag",
            "com.lunacy.gorillatag",
            "com.goldmenu.gorillatag",
            "com.mii.gorillatag",
            "com.bobcat.gorillatag",
            "com.gecko.gorillatag",
            "com.pigeon.gorillatag",
        };

        public static bool MenuDetected { get; private set; }
        public static string DetectedMenuName { get; private set; } = "";
        public static List<string> AllDetected { get; private set; } = new List<string>();
        public static int OverriddenPatchCount { get; private set; }
        public static bool ScanComplete { get; private set; }

        private static float _lastScanTime = 0f;
        private static bool _alertPlayed = false;

        public static void FullScan(Harmony ourHarmony)
        {
            AllDetected.Clear();
            MenuDetected = false;
            DetectedMenuName = "";
            OverriddenPatchCount = 0;

            ScanAssemblies();
            ScanHarmonyPatches(ourHarmony);

            ScanComplete = true;
            _lastScanTime = Time.time;

            if (MenuDetected)
            {
                Plugin.Instance?.Log($"[MenuDetector] DETECTED: {DetectedMenuName} ({AllDetected.Count} total conflicts)");

                if (SafetyConfig.MenuDetectionAlertEnabled && !_alertPlayed)
                {
                    _alertPlayed = true;
                    AudioManager.Play("menu_detected", AudioManager.AudioCategory.Warning);
                }

                if (SafetyConfig.AutoOverrideOnDetection)
                {
                    OverriddenPatchCount = ForceOverrideAll(ourHarmony);
                    Plugin.Instance?.Log($"[MenuDetector] Overrode {OverriddenPatchCount} conflicting patches");
                }
            }
            else
            {
                Plugin.Instance?.Log("[MenuDetector] No conflicting menus detected");
            }
        }

        public static void PeriodicScan(Harmony ourHarmony)
        {
            if (!SafetyConfig.MenuDetectionEnabled) return;
            if (Time.time - _lastScanTime < 30f) return;

            bool wasPreviouslyDetected = MenuDetected;
            FullScan(ourHarmony);

            if (MenuDetected && !wasPreviouslyDetected)
            {
                Plugin.Instance?.Log($"[MenuDetector] NEW menu detected at runtime: {DetectedMenuName}");
                if (SafetyConfig.MenuDetectionAlertEnabled)
                {
                    AudioManager.Play("menu_detected", AudioManager.AudioCategory.Warning);
                }
            }
        }

        private static void ScanAssemblies()
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    if (string.IsNullOrEmpty(asmName)) continue;

                    if (asmName.Contains("SignalSafetyMenu")) continue;

                    foreach (var kv in KnownMenus)
                    {
                        if (asmName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (!AllDetected.Contains(kv.Value))
                                AllDetected.Add(kv.Value);
                            MenuDetected = true;
                            if (string.IsNullOrEmpty(DetectedMenuName))
                                DetectedMenuName = kv.Value;
                            break;
                        }
                    }

                    try
                    {
                        foreach (var type in asm.GetTypes())
                        {
                            string typeName = type.FullName ?? "";
                            if (typeName.Contains("iiMenu") || typeName.Contains("BarkMod") ||
                                typeName.Contains("AspectMod") || typeName.Contains("LunacyMenu"))
                            {
                                string label = typeName.Contains("iiMenu") ? "ii's Stupid Menu" :
                                               typeName.Contains("BarkMod") ? "Bark Menu" :
                                               typeName.Contains("AspectMod") ? "Aspect Menu" : "Lunacy";
                                if (!AllDetected.Contains(label))
                                    AllDetected.Add(label);
                                MenuDetected = true;
                                if (string.IsNullOrEmpty(DetectedMenuName))
                                    DetectedMenuName = label;
                            }
                        }
                    }
                    catch {  }
                }
            }
            catch (Exception e)
            {
                Plugin.Instance?.Log($"[MenuDetector] Assembly scan error: {e.Message}");
            }
        }

        private static void ScanHarmonyPatches(Harmony ourHarmony)
        {
            try
            {
                var allIds = Harmony.GetAllPatchedMethods()
                    .SelectMany(m =>
                    {
                        var info = Harmony.GetPatchInfo(m);
                        if (info == null) return Enumerable.Empty<string>();
                        return info.Prefixes.Select(p => p.owner)
                            .Concat(info.Postfixes.Select(p => p.owner))
                            .Concat(info.Transpilers.Select(p => p.owner));
                    })
                    .Distinct()
                    .Where(id => !string.IsNullOrEmpty(id) && !id.Contains("signal"))
                    .ToList();

                foreach (var id in allIds)
                {
                    foreach (var knownId in KnownHarmonyIds)
                    {
                        if (id.IndexOf(knownId, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string label = $"Harmony: {id}";
                            if (!AllDetected.Contains(label))
                                AllDetected.Add(label);
                            MenuDetected = true;
                            if (DetectedMenuName == "")
                                DetectedMenuName = id;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Instance?.Log($"[MenuDetector] Harmony scan error: {e.Message}");
            }
        }

        public static int ForceOverrideAll(Harmony ourHarmony)
        {
            int removed = 0;

            var criticalMethods = new (Type type, string method)[]
            {
                (typeof(GorillaNot), "SendReport"),
                (typeof(GorillaNot), "DispatchReport"),
                (typeof(GorillaNot), "CheckReports"),
                (typeof(GorillaNot), "SliceUpdate"),
                (typeof(GorillaNot), "IncrementRPCCallLocal"),
                (typeof(GorillaNot), "IncrementRPCCall"),
                (typeof(GorillaNot), "IncrementRPCTracker"),
                (typeof(GorillaNot), "CloseInvalidRoom"),
                (typeof(GorillaNot), "QuitDelay"),
                (typeof(GorillaNot), "ShouldDisconnectFromRoom"),
                (typeof(GorillaNot), "RefreshRPCs"),
                (typeof(GorillaNot), "LogErrorCount"),
                (typeof(GorillaTelemetry), "EnqueueTelemetryEvent"),
                (typeof(GorillaTelemetry), "PostBuilderKioskEvent"),
                (typeof(GorillaTelemetry), "SuperInfectionEvent"),
                (typeof(PlayFabClientInstanceAPI), "ReportPlayer"),
                (typeof(GorillaNetworking.GorillaComputer), "CheckAutoBanListForName"),
                (typeof(GorillaNetworking.GorillaComputer), "CheckAutoBanListForRoomName"),
                (typeof(GorillaNetworking.GorillaComputer), "CheckAutoBanListForPlayerName"),
                (typeof(GorillaNetworking.GorillaComputer), "CheckAutoBanListForTroopName"),
                (typeof(GorillaPlayerScoreboardLine), "ReportPlayer"),
                (typeof(GorillaNetworking.GorillaComputer), "GeneralFailureMessage"),
                (typeof(GorillaNetworking.GorillaComputer), "UpdateFailureText"),
            };

            foreach (var (type, method) in criticalMethods)
            {
                try
                {
                    var original = AccessTools.Method(type, method);
                    if (original == null) continue;

                    var patches = Harmony.GetPatchInfo(original);
                    if (patches == null) continue;

                    foreach (var prefix in patches.Prefixes)
                    {
                        if (prefix.owner.Contains("signal")) continue;
                        try
                        {
                            ourHarmony.Unpatch(original, prefix.PatchMethod);
                            removed++;
                            Plugin.Instance?.Log($"[Override] Removed {prefix.owner} prefix from {type.Name}.{method}");
                        }
                        catch { }
                    }

                    foreach (var postfix in patches.Postfixes)
                    {
                        if (postfix.owner.Contains("signal")) continue;
                        try
                        {
                            ourHarmony.Unpatch(original, postfix.PatchMethod);
                            removed++;
                        }
                        catch { }
                    }

                    foreach (var transpiler in patches.Transpilers)
                    {
                        if (transpiler.owner.Contains("signal")) continue;
                        try
                        {
                            ourHarmony.Unpatch(original, transpiler.PatchMethod);
                            removed++;
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return removed;
        }

        public static string GetDetectionSummary()
        {
            if (!ScanComplete) return "Scan pending...";
            if (!MenuDetected) return "No conflicts detected";
            return $"Detected: {string.Join(", ", AllDetected)} | Overrides: {OverriddenPatchCount}";
        }
    }
}
