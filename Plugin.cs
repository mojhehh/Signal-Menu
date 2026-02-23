using System;
using System.Collections;
using System.IO;
using BepInEx;
using GorillaNetworking;
using HarmonyLib;
using UnityEngine;
using SignalSafetyMenu.Patches;

namespace SignalSafetyMenu
{
    [BepInPlugin("com.vr.performance.toolkit", "VR Performance Toolkit", "1.0.8")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        internal static Harmony harmony;

        public static int TotalPatches = 0;
        public static int SuccessfulPatches = 0;
        public static int FailedPatches = 0;

        private float _nextBypassTime = 0f;
        private bool _bypassPending = false;
        private bool _afkKickDisabled = false;

        void Awake()
        {
            Instance = this;
            Log("Safety menu loading...");
            Log("Patches + Settings active");

            GorillaTagger.OnPlayerSpawned(new Action(OnGameReady));
        }

        private void OnGameReady()
        {
            Log("Checking environment...");

            gameObject.AddComponent<Menu3D>();

            try
            {
                string ver = Application.version;
                Log($"Version: {ver}");
            }
            catch { }

            if (SafetyPatches.DetectConflicts())
            {
                Log("Other mod detected � will override its patches after applying ours");
            }

            ApplyPatches();

            VerifyPatches();

            SafetyPatches.HardOverride(harmony);

            MenuDetector.FullScan(harmony);

            StartCoroutine(AnnounceProtection());

            AntiReport.Initialize();
        }

        private void ApplyPatches()
        {
            try
            {
                harmony = new Harmony("com.vr.performance.toolkit");

                var assembly = typeof(Plugin).Assembly;
                foreach (var type in assembly.GetTypes())
                {
                    try
                    {
                        if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                        {
                            TotalPatches++;
                            harmony.PatchAll(type);
                            SuccessfulPatches++;
                        }
                    }
                    catch (Exception ex)
                    {
                        FailedPatches++;
                        Log($"Patch failed: {type.Name} - {ex.Message}");
                        Patches.SafetyPatches.TrackPatchFail(type.Name);
                    }
                }

                Log($"Applied {SuccessfulPatches}/{TotalPatches} ({FailedPatches} failed)");
            }
            catch (Exception ex)
            {
                Log($"Critical error: {ex.Message}");
                Patches.SafetyPatches.TrackError("ApplyPatches");
            }
        }

        private void VerifyPatches()
        {
            try
            {
                int missing = 0;

                var criticalChecks = new (Type type, string method)[]
                {
                    (typeof(MonkeAgent), "SendReport"),
                    (typeof(GorillaTelemetry), "EnqueueTelemetryEvent"),
                    (typeof(MonkeAgent), "CheckReports"),
                };

                foreach (var (type, method) in criticalChecks)
                {
                    try
                    {
                        var original = HarmonyLib.AccessTools.Method(type, method);
                        if (original == null) continue;

                        var patchInfo = Harmony.GetPatchInfo(original);
                        if (patchInfo != null && patchInfo.Prefixes.Count > 0)
                        {
                            bool hasOurs = false;
                            foreach (var p in patchInfo.Prefixes)
                            {
                                if (p.owner == harmony.Id) { hasOurs = true; break; }
                            }
                            if (!hasOurs) missing++;
                        }
                        else
                        {
                            missing++;
                        }
                    }
                    catch { }
                }

                if (missing > 0)
                {
                    Log($"Verify: {missing} critical patches missing");
                    AudioManager.Play("warning", AudioManager.AudioCategory.Warning);
                }
            }
            catch { }
        }

        private IEnumerator AnnounceProtection()
        {
            yield return new WaitForSeconds(2f);

            if (SafetyConfig.IsFirstOpen)
            {
                AudioManager.Play("first_open", AudioManager.AudioCategory.Protection);
                SafetyConfig.Save();
                yield return new WaitForSeconds(4f);
            }

            if (FailedPatches == 0)
            {
                AudioManager.Play("protection_enabled", AudioManager.AudioCategory.Protection);
                Log("All protection systems online!");
            }
            else
            {
                AudioManager.Play("someoffline", AudioManager.AudioCategory.Warning);
                Log($"Partial protection - {FailedPatches} patches failed");
            }

            yield return new WaitForSeconds(3f);
            CheckForBan();

            yield return UpdateChecker.CheckForUpdate();
        }

        private void CheckForBan()
        {
            try
            {
                if (GorillaNetworking.GorillaComputer.instance?.screenText != null)
                {
                    string text = GorillaNetworking.GorillaComputer.instance.screenText.currentText;
                    if (!string.IsNullOrEmpty(text))
                    {
                        string upper = text.ToUpperInvariant();
                        if (upper.Contains("YOUR ACCOUNT") && upper.Contains("BANNED") || upper.Contains("BAN EXPIRES"))
                        {
                            Log($"[BAN] Screen text ban detected: {text.Substring(0, Math.Min(text.Length, 80))}");
                            SignalSafetyMenu.Patches.SafetyPatches.AnnounceBanOnce();
                        }
                    }
                }
            }
            catch { }
        }

        void OnGUI()
        {
            SafetyMenu.Draw();
        }

        void Update()
        {
            if (SafetyConfig.MenuDetectionEnabled && harmony != null)
            {
                try { MenuDetector.PeriodicScan(harmony); } catch { }
            }

            if (SafetyConfig.AntiReportEnabled)
            {
                try { AntiReport.RunAntiReport(); } catch { }
                try { AntiReport.VisualizeAntiReport(); } catch { }
            }

            try
            {
                if (PhotonNetworkController.Instance != null && SafetyConfig.AntiAFKKickEnabled && !_afkKickDisabled)
                {
                    PhotonNetworkController.Instance.disableAFKKick = true;
                    _afkKickDisabled = true;
                }
            }
            catch { }
            try { SafetyPatches.RPCProtection(); } catch { }
            if (_bypassPending && Time.time >= _nextBypassTime)
            {
                _bypassPending = false;
                try { SafetyPatches.BypassModCheckers(); } catch { }
            }

            try { ModeratorDetector.Check(); } catch { }
            try { ContentCreatorDetector.Check(); } catch { }
            try { CosmeticNotifier.Check(); } catch { }
            try { AutomodBypass.Update(); } catch { }
            try { AntiLurkerSystem.Update(); } catch { }
            try { AutoGC.Update(); } catch { }
            try { SupportPageSpoofer.Update(); } catch { }
            try { RankedSpoofer.Update(); } catch { }
            try { IdentityChanger.CheckDisconnect(); } catch { }
            try { FakeBehaviors.FakeOculusMenu(); } catch { }
            try { FakeBehaviors.FakeBrokenController(); } catch { }
            try { FakeReportMenuBehavior.Update(); } catch { }
        }

        void LateUpdate()
        {
            try { AntiPredictions.LateUpdate(); } catch { }
            try { FakeValveTrackingBehavior.LateUpdate(); } catch { }
        }

        public void ScheduleDelayedBypass(float delay)
        {
            _bypassPending = true;
            _nextBypassTime = Time.time + delay;
            _afkKickDisabled = false;
        }

        public void Log(string message)
        {
            Logger.LogInfo(message);
        }
    }
}
