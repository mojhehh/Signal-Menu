using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using UnityEngine;
using PlayFab;
using PlayFab.ClientModels;
using PlayFab.Internal;
using PlayFab.CloudScriptModels;
using PlayFab.Json;
using GorillaNetworking;
using GorillaTagScripts;
using GorillaExtensions;

namespace SignalSafetyMenu.Patches
{
    public static class SafetyPatches
    {

        private static float _lastSafetyDisconnect = 0f;
        public static bool SafetyDisconnect(string reason)
        {
            if (Time.time - _lastSafetyDisconnect < 0.1f) return false;
            try { if (!NetworkSystem.Instance.InRoom) return false; } catch { return false; }
            _lastSafetyDisconnect = Time.time;
            Plugin.Instance?.Log($"[Safety] Disconnect: {reason}");
            try { NetworkSystem.Instance.ReturnToSinglePlayer(); } catch { }
            try { RPCFlusher.Flush(); } catch { }
            return true;
        }

        internal static bool _rpcProtectionApplied = false;

        public static void RPCProtection()
        {
            if (!PhotonNetwork.InRoom) { _rpcProtectionApplied = false; return; }
            if (_rpcProtectionApplied) return;
            try
            {
                MonkeAgent.instance.rpcErrorMax = 999999;
                MonkeAgent.instance.rpcCallLimit = 999999;
                MonkeAgent.instance.logErrorMax = 999999;
                PhotonNetwork.MaxResendsBeforeDisconnect = 25;
                PhotonNetwork.QuickResends = 3;
                _rpcProtectionApplied = true;
            }
            catch { }
        }

        public static void BypassModCheckers()
        {
            try
            {
                var localPlayer = PhotonNetwork.LocalPlayer;
                if (localPlayer?.CustomProperties == null || localPlayer.CustomProperties.Count == 0) return;
                var wipe = new ExitGames.Client.Photon.Hashtable();
                foreach (var key in localPlayer.CustomProperties.Keys)
                {
                    string k = key?.ToString();
                    if (k != null && !k.Equals("didTutorial"))
                        wipe[k] = null;
                }
                if (wipe.Count > 0)
                {
                    PatchSetCustomProperties._bypassActive = true;
                    try { localPlayer.SetCustomProperties(wipe); } finally { PatchSetCustomProperties._bypassActive = false; }
                }
            }
            catch { }
        }

        public static bool BanAlreadyAnnounced = false;

        public static void AnnounceBanOnce()
        {
            if (BanAlreadyAnnounced) return;
            BanAlreadyAnnounced = true;

            Plugin.Instance?.Log("[BAN] Account ban detected � playing alert");
            AudioManager.Play("banned", AudioManager.AudioCategory.Ban);
        }

        private static string _spoofedHWID = null;
        private static string _spoofedPlayFabId = null;
        private static string _spoofedMothershipId = null;

        private static readonly string HWIDFilePath = Path.Combine(Application.persistentDataPath, ".gl_cache");
        private static readonly string PlayFabIdFilePath = Path.Combine(Application.persistentDataPath, ".gl_session");
        private static readonly string MothershipIdFilePath = Path.Combine(Application.persistentDataPath, ".gl_pref");

        private static int _errorCount = 0;
        private static int _patchFailCount = 0;
        private static string _logFilePath = null;
        private static object _logLock = new object();

        public static int ErrorCount => _errorCount;
        public static int PatchFailCount => _patchFailCount;

        private static string GetLogPath()
        {
            if (_logFilePath == null)
            {
                _logFilePath = Path.Combine(Application.persistentDataPath, "signal_safety.log");
            }
            return _logFilePath;
        }

        public static void TrackError(string context = null)
        {
            System.Threading.Interlocked.Increment(ref _errorCount);

            if (SafetyConfig.ErrorLoggingEnabled)
            {
                try
                {
                    lock (_logLock)
                    {
                        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        string entry = $"[{timestamp}] ERROR #{_errorCount}: {context ?? "unknown"}";
                        File.AppendAllText(GetLogPath(), entry + Environment.NewLine);
                    }
                }
                catch { }
            }
        }

        public static void TrackPatchFail(string patchName = null)
        {
            System.Threading.Interlocked.Increment(ref _patchFailCount);

            if (SafetyConfig.ErrorLoggingEnabled)
            {
                try
                {
                    lock (_logLock)
                    {
                        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        string entry = $"[{timestamp}] PATCH FAIL #{_patchFailCount}: {patchName ?? "unknown"}";
                        File.AppendAllText(GetLogPath(), entry + Environment.NewLine);
                    }
                }
                catch { }
            }
        }

        public static string GetErrorSummary()
        {
            return $"Errors: {_errorCount}, Patch Fails: {_patchFailCount}";
        }

        public static void ClearLog()
        {
            try
            {
                if (File.Exists(GetLogPath()))
                    File.Delete(GetLogPath());
            }
            catch { }
        }

        private static byte[] GetEncryptionKey()
        {
            string seed = "GT_" + Environment.UserName + "_" + Environment.MachineName;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(seed));
            }
        }

        private static string EncryptString(string plainText)
        {
            try
            {
                byte[] key = GetEncryptionKey();
                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.GenerateIV();
                    using (var enc = aes.CreateEncryptor())
                    {
                        byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
                        byte[] cipherBytes = enc.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                        byte[] result = new byte[aes.IV.Length + cipherBytes.Length];
                        Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
                        Array.Copy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
                        return Convert.ToBase64String(result);
                    }
                }
            }
            catch { return plainText; }
        }

        private static string DecryptString(string cipherText)
        {
            try
            {
                byte[] key = GetEncryptionKey();
                byte[] fullCipher = Convert.FromBase64String(cipherText);
                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    byte[] iv = new byte[16];
                    byte[] cipher = new byte[fullCipher.Length - 16];
                    Array.Copy(fullCipher, 0, iv, 0, 16);
                    Array.Copy(fullCipher, 16, cipher, 0, cipher.Length);
                    aes.IV = iv;
                    using (var dec = aes.CreateDecryptor())
                    {
                        byte[] plainBytes = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                        return System.Text.Encoding.UTF8.GetString(plainBytes);
                    }
                }
            }
            catch { return null; }
        }

        private static void WriteEncrypted(string path, string value)
        {
            try { File.WriteAllText(path, EncryptString(value)); } catch { SafetyPatches.TrackError(); }
        }

        public static void WriteEncryptedPublic(string path, string value) => WriteEncrypted(path, value);
        public static string ReadEncryptedPublic(string path) => ReadEncrypted(path);

        private static string ReadEncrypted(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string encrypted = File.ReadAllText(path).Trim();
                if (string.IsNullOrEmpty(encrypted)) return null;
                return DecryptString(encrypted);
            }
            catch { return null; }
        }

        public static int SecureRandomInt(int max)
        {
            if (max <= 0) return 0;
            byte[] buf = GenerateRandomBytes(4);
            return (int)(BitConverter.ToUInt32(buf, 0) % (uint)max);
        }

        public static string GetSpoofedHWID()
        {
            if (_spoofedHWID == null)
            {
                _spoofedHWID = ReadEncrypted(HWIDFilePath);
                if (string.IsNullOrEmpty(_spoofedHWID))
                {
                    byte[] guidBytes = GenerateRandomBytes(16);
                    _spoofedHWID = new Guid(guidBytes).ToString();
                    WriteEncrypted(HWIDFilePath, _spoofedHWID);
                }
            }
            return _spoofedHWID;
        }

        public static string GetSpoofedPlayFabId()
        {
            if (_spoofedPlayFabId == null)
            {
                _spoofedPlayFabId = ReadEncrypted(PlayFabIdFilePath);
                if (string.IsNullOrEmpty(_spoofedPlayFabId))
                {
                    _spoofedPlayFabId = BitConverter.ToString(GenerateRandomBytes(8)).Replace("-", "");
                    WriteEncrypted(PlayFabIdFilePath, _spoofedPlayFabId);
                }
            }
            return _spoofedPlayFabId;
        }

        public static string GetSpoofedMothershipId()
        {
            if (_spoofedMothershipId == null)
            {
                _spoofedMothershipId = ReadEncrypted(MothershipIdFilePath);
                if (string.IsNullOrEmpty(_spoofedMothershipId))
                {
                    _spoofedMothershipId = Guid.NewGuid().ToString();
                    WriteEncrypted(MothershipIdFilePath, _spoofedMothershipId);
                }
            }
            return _spoofedMothershipId;
        }

        private static byte[] GenerateRandomBytes(int count)
        {
            byte[] bytes = new byte[count];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(bytes);
            }
            return bytes;
        }

        private static readonly Dictionary<byte, Queue<float>> _eventTimestamps = new Dictionary<byte, Queue<float>>();
        private static readonly object _throttleLock = new object();
        private const int MAX_EVENTS_PER_SECOND = 8;
        private const float THROTTLE_WINDOW = 1.0f;

        public static bool ShouldAllowEvent(byte eventCode)
        {
            if (eventCode >= 200) return true;

            try
            {
                lock (_throttleLock)
                {
                    float now = Time.unscaledTime;

                    if (!_eventTimestamps.ContainsKey(eventCode))
                        _eventTimestamps[eventCode] = new Queue<float>();

                    var queue = _eventTimestamps[eventCode];

                    while (queue.Count > 0 && now - queue.Peek() > THROTTLE_WINDOW)
                        queue.Dequeue();

                    if (queue.Count >= MAX_EVENTS_PER_SECOND)
                        return false;

                    queue.Enqueue(now);
                    return true;
                }
            }
            catch { return true; }
        }

        public static void HardOverride(Harmony harmony)
        {
            var criticalMethods = new (Type type, string method)[]
            {
                (typeof(MonkeAgent), "SendReport"),
                (typeof(MonkeAgent), "DispatchReport"),
                (typeof(MonkeAgent), "CheckReports"),
                (typeof(MonkeAgent), "SliceUpdate"),
                (typeof(MonkeAgent), "IncrementRPCCallLocal"),
                (typeof(MonkeAgent), "IncrementRPCCall"),
                (typeof(MonkeAgent), "IncrementRPCTracker"),
                (typeof(MonkeAgent), "CloseInvalidRoom"),
                (typeof(GorillaTelemetry), "EnqueueTelemetryEvent"),
                (typeof(GorillaTelemetry), "PostBuilderKioskEvent"),
                (typeof(GorillaTelemetry), "SuperInfectionEvent"),
                (typeof(PlayFabClientInstanceAPI), "ReportPlayer"),
                (typeof(GorillaServer), "UploadGorillanalytics"),
                (typeof(GorillaServer), "CheckForBadName"),
                (typeof(GorillaNetworking.GorillaComputer), "CheckAutoBanListForName"),
                (typeof(GorillaNetworking.GorillaComputer), "CheckAutoBanListForRoomName"),
                (typeof(GorillaNetworking.GorillaComputer), "CheckAutoBanListForPlayerName"),
                (typeof(GorillaNetworking.GorillaComputer), "CheckAutoBanListForTroopName"),
                (typeof(GorillaPlayerScoreboardLine), "ReportPlayer"),
            };

            foreach (var (type, method) in criticalMethods)
            {
                try
                {
                    var original = AccessTools.Method(type, method);
                    if (original == null) continue;

                    var patches = Harmony.GetPatchInfo(original);
                    if (patches != null)
                    {
                        foreach (var prefix in patches.Prefixes)
                        {
                            if (prefix.owner == harmony.Id)
                                continue;

                            Plugin.Instance?.Log($"Removing conflicting patch from {type.Name}.{method} (owner: {prefix.owner})");
                            harmony.Unpatch(original, prefix.PatchMethod);
                        }
                    }
                }
                catch { SafetyPatches.TrackError(); }
            }
        }

        public static bool DetectConflicts()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = asm.GetName().Name;

                if (name == "SignalSafety" || name.Contains("SignalSafetyRaw"))
                {
                    Plugin.Instance?.Log("Conflict detected - please use only one safety plugin");
                    AudioManager.Play("warning", AudioManager.AudioCategory.Warning);
                    return true;
                }

                if (name.Contains("Bark") || name.Contains("Aspect") || name.Contains("Lunacy"))
                {
                    Plugin.Instance?.Log($"[WARNING] Detected other mod: {name} � will override its patches");
                    AudioManager.Play("patch_conflict", AudioManager.AudioCategory.Warning);
                }
            }

            return false;
        }

        internal static bool ShouldBlockTelemetry() => SafetyConfig.PatchTelemetry && SafetyConfig.TelemetryBlockEnabled;
        internal static bool ShouldBlockSensitiveTelemetry() => ShouldBlockTelemetry();
        internal static bool ShouldAllowHarmlessTelemetry() => !ShouldBlockTelemetry();
        internal static bool ShouldBlockPlayFab() => SafetyConfig.PatchPlayFabReport;
    }

    [HarmonyPatch(typeof(PhotonNetworkController), "OnJoinedRoom")]
    [HarmonyPriority(Priority.First)]
    public class PatchJoinedRoom
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { SafetyPatches.BanAlreadyAnnounced = false; } catch { }
            try { SafetyPatches._rpcProtectionApplied = false; } catch { }
            try { SafetyPatches.BypassModCheckers(); } catch { }
            try
            {
                if (Plugin.Instance != null)
                {
                    Plugin.Instance.ScheduleDelayedBypass(3f);
                }
            }
            catch { }
            try { AntiReport.OnRoomJoined(); } catch { }
        }
    }

    [HarmonyPatch(typeof(PhotonNetworkController), "OnLeftRoom")]
    [HarmonyPriority(Priority.First)]
    public class PatchLeftRoom
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { AntiReport.OnRoomLeft(); } catch { }
        }
    }

    [HarmonyPatch(typeof(MonkeAgent), "SendReport")]
    [HarmonyPriority(Priority.First)]
    public class PatchSendReport
    {
        [HarmonyPrefix]
        public static bool Prefix(string susReason, string susId, string susNick)
        {
            if (!SafetyConfig.PatchSendReport) return true;
            try
            {
                string localId = Photon.Pun.PhotonNetwork.LocalPlayer?.UserId;

                if (susId == localId)
                {
                    if (SafetyConfig.ShowACReportsEnabled)
                    {
                        ACReportNotifier.NotifyReport(susReason);
                    }

                    Plugin.Instance?.Log($"[ANTI-CHEAT] Blocked self-report: reason=\"{susReason}\"");
                }
                else
                {
                    Plugin.Instance?.Log($"[ANTI-CHEAT] Blocked report: {susNick} ({susId}) for \"{susReason}\"");
                }
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(MonkeAgent), "DispatchReport")]
    [HarmonyPriority(Priority.First)]
    public class PatchDispatchReport { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchDispatchReport; }

    [HarmonyPatch(typeof(MonkeAgent), "CheckReports")]
    [HarmonyPriority(Priority.First)]
    public class PatchCheckReports { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchCheckReports; }

    [HarmonyPatch(typeof(MonkeAgent), "CloseInvalidRoom")]
    [HarmonyPriority(Priority.First)]
    public class PatchCloseInvalidRoom { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchCloseInvalidRoom; }

    [HarmonyPatch(typeof(MonkeAgent), "LogErrorCount")]
    [HarmonyPriority(Priority.First)]
    public class PatchLogErrorCount { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchRPCLimits; }

    [HarmonyPatch(typeof(MonkeAgent), "IncrementRPCCallLocal")]
    [HarmonyPriority(Priority.First)]
    public class PatchIncrementRPCCallLocal
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchRPCLimits;
    }

    [HarmonyPatch(typeof(MonkeAgent), "SliceUpdate")]
    [HarmonyPriority(Priority.First)]
    public class PatchSliceUpdate { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchRPCLimits; }

    [HarmonyPatch(typeof(MonkeAgent), "GetRPCCallTracker")]
    [HarmonyPriority(Priority.First)]
    public class PatchGetRPCCallTracker
    {
        [HarmonyPrefix]
        public static bool Prefix(ref object __result)
        {
            if (!SafetyConfig.PatchRPCLimits) return true;
            // Return null — MonkeAgent.IncrementRPCTracker already null-checks the result
            // and returns true (meaning "allow the RPC") when tracker is null
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(MonkeAgent), "QuitDelay")]
    [HarmonyPriority(Priority.First)]
    public class PatchQuitDelay { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchQuitDelay; }

    [HarmonyPatch(typeof(MonkeAgent), "ShouldDisconnectFromRoom")]
    [HarmonyPriority(Priority.First)]
    public class PatchShouldDisconnectFromRoom
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result) { if (!SafetyConfig.PatchQuitDelay) return true; __result = false; return false; }
    }

    [HarmonyPatch(typeof(MonkeAgent), "RefreshRPCs")]
    [HarmonyPriority(Priority.First)]
    public class PatchRefreshRPCs { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchRPCLimits; }

    [HarmonyPatch(typeof(VRRig), "IncrementRPC", new Type[] { typeof(PhotonMessageInfoWrapped), typeof(string) })]
    [HarmonyPriority(Priority.First)]
    public class PatchVRRigIncrementRPC
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchRPCLimits;
    }

    [HarmonyPatch(typeof(MonkeAgent), "IncrementRPCCall", new Type[] { typeof(PhotonMessageInfo), typeof(string) })]
    [HarmonyPriority(Priority.First)]
    public class PatchIncrementRPCCall1
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchRPCLimits;
    }

    [HarmonyPatch(typeof(MonkeAgent), "IncrementRPCCall", new Type[] { typeof(PhotonMessageInfoWrapped), typeof(string) })]
    [HarmonyPriority(Priority.First)]
    public class PatchIncrementRPCCall2
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchRPCLimits;
    }

    [HarmonyPatch(typeof(GorillaGameManager), "ForceStopGame_DisconnectAndDestroy")]
    [HarmonyPriority(Priority.First)]
    public class PatchForceStopGame
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!SafetyConfig.PatchQuitDelay) return true;
            Plugin.Instance?.Log("[ForceStop] Blocked force-disconnect attempt");
            return false;
        }
    }

    [HarmonyPatch(typeof(GorillaComputer), "GeneralFailureMessage")]
    public class PatchGeneralFailureMessage
    {
        [HarmonyPrefix]
        public static void Prefix(string failMessage)
        {
            if (!SafetyConfig.PatchBanDetection) return;
            CheckForBan(failMessage);
        }

        public static void CheckForBan(string failMessage)
        {
            try
            {
                if (failMessage != null)
                {
                    string upper = failMessage.ToUpperInvariant();
                    if ((upper.Contains("YOUR ACCOUNT") && upper.Contains("BANNED")) ||
                        upper.Contains("BAN EXPIRES") ||
                        upper.Contains("HAS BEEN BANNED"))
                    {
                        Plugin.Instance?.Log($"[BAN] Failure message ban: {failMessage.Substring(0, Math.Min(failMessage.Length, 80))}");
                        SafetyPatches.AnnounceBanOnce();
                    }
                }
            }
            catch { SafetyPatches.TrackError(); }
        }
    }

    [HarmonyPatch(typeof(GorillaComputer), "UpdateFailureText")]
    public class PatchUpdateFailureText
    {
        [HarmonyPrefix]
        public static void Prefix(string failMessage)
        {
            if (!SafetyConfig.PatchFailureText) return;
            PatchGeneralFailureMessage.CheckForBan(failMessage);
        }
    }

    [HarmonyPatch(typeof(GorillaNetworkPublicTestsJoin), "GracePeriod")]
    [HarmonyPriority(Priority.First)]
    public class PatchGracePeriod1
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchGracePeriod;
    }

    [HarmonyPatch(typeof(GorillaNetworkPublicTestJoin2), "GracePeriod")]
    [HarmonyPriority(Priority.First)]
    public class PatchGracePeriod2
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchGracePeriod;
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "EnqueueTelemetryEvent")]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry1
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyPatches.ShouldBlockTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "EnqueueTelemetryEventPlayFab")]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry2
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyPatches.ShouldBlockTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "FlushPlayFabTelemetry")]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry3
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyPatches.ShouldBlockTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "FlushMothershipTelemetry")]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry4
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyPatches.ShouldBlockTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "EnqueueZoneEvent")]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry5
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "PostNotificationEvent")]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry6
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "PostGameModeEvent")]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry7
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "PostShopEvent", new Type[] { typeof(VRRig), typeof(GTShopEventType), typeof(CosmeticsController.CosmeticItem) })]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry8
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "PostShopEvent", new Type[] { typeof(VRRig), typeof(GTShopEventType), typeof(System.Collections.Generic.IList<CosmeticsController.CosmeticItem>) })]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry9
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "PostKidEvent")]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry10
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyPatches.ShouldBlockTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "PostCustomMapPerformance")]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry11
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "PostCustomMapTracking")]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry12
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "PostCustomMapDownloadEvent")]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry13
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "WamGameStart")]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry15
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "WamLevelEnd")]
    [HarmonyPriority(Priority.First)]
    public class PatchTelemetry16
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "GhostReactorShiftStart")]
    [HarmonyPriority(Priority.First)]
    public class PatchGRTelemetry1
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "GhostReactorGameEnd")]
    [HarmonyPriority(Priority.First)]
    public class PatchGRTelemetry2
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "GhostReactorFloorStart")]
    [HarmonyPriority(Priority.First)]
    public class PatchGRTelemetry3
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "GhostReactorFloorComplete")]
    [HarmonyPriority(Priority.First)]
    public class PatchGRTelemetry4
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "GhostReactorToolPurchased")]
    [HarmonyPriority(Priority.First)]
    public class PatchGRTelemetry5
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "GhostReactorRankUp")]
    [HarmonyPriority(Priority.First)]
    public class PatchGRTelemetry6
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "GhostReactorToolUnlock")]
    [HarmonyPriority(Priority.First)]
    public class PatchGRTelemetry7
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "GhostReactorPodUpgradePurchased")]
    [HarmonyPriority(Priority.First)]
    public class PatchGRTelemetry8
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "GhostReactorToolUpgrade")]
    [HarmonyPriority(Priority.First)]
    public class PatchGRTelemetry9
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "GhostReactorChaosSeedStart")]
    [HarmonyPriority(Priority.First)]
    public class PatchGRTelemetry10
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "GhostReactorChaosJuiceCollected")]
    [HarmonyPriority(Priority.First)]
    public class PatchGRTelemetry11
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "GhostReactorOverdrivePurchased")]
    [HarmonyPriority(Priority.First)]
    public class PatchGRTelemetry12
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(GorillaTelemetry), "GhostReactorCreditsRefillPurchased")]
    [HarmonyPriority(Priority.First)]
    public class PatchGRTelemetry13
    {
        [HarmonyPrefix]
        public static bool Prefix() => SafetyPatches.ShouldAllowHarmlessTelemetry();
    }

    [HarmonyPatch(typeof(PlayFabClientInstanceAPI), "ReportDeviceInfo")]
    [HarmonyPriority(Priority.First)]
    public class PatchPlayFab1
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchPlayFabReport;
    }

    [HarmonyPatch(typeof(PlayFabClientAPI), "ReportDeviceInfo")]
    [HarmonyPriority(Priority.First)]
    public class PatchPlayFab2
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchPlayFabReport;
    }

    [HarmonyPatch(typeof(PlayFabClientInstanceAPI), "ReportPlayer")]
    [HarmonyPriority(Priority.First)]
    public class PatchPlayFab3
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchPlayFabReport;
    }

    [HarmonyPatch(typeof(PlayFabClientAPI), "ReportPlayer")]
    [HarmonyPriority(Priority.First)]
    public class PatchPlayFab4
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchPlayFabReport;
    }

    [HarmonyPatch(typeof(PlayFabHttp), "InitializeScreenTimeTracker")]
    [HarmonyPriority(Priority.First)]
    public class PatchPlayFab5
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchPlayFabReport;
    }

    [HarmonyPatch(typeof(PlayFabDeviceUtil), "SendDeviceInfoToPlayFab")]
    [HarmonyPriority(Priority.First)]
    public class PatchPlayFab6
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchPlayFabReport;
    }

    [HarmonyPatch(typeof(PlayFabClientAPI), "AttributeInstall")]
    [HarmonyPriority(Priority.First)]
    public class PatchPlayFab7
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchPlayFabReport;
    }

    [HarmonyPatch(typeof(PlayFabDeviceUtil), "DoAttributeInstall")]
    [HarmonyPriority(Priority.First)]
    public class PatchPlayFab8
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchPlayFabReport;
    }

    [HarmonyPatch(typeof(PlayFabDeviceUtil), "GetAdvertIdFromUnity")]
    [HarmonyPriority(Priority.First)]
    public class PatchPlayFab9
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchPlayFabReport;
    }

    [HarmonyPatch(typeof(PlayFabSettings), "DeviceUniqueIdentifier", MethodType.Getter)]
    [HarmonyPriority(Priority.First)]
    public class PatchPlayFabDeviceId
    {
        [HarmonyPrefix]
        public static bool Prefix(ref string __result)
        {
            __result = SafetyPatches.GetSpoofedHWID();
            return false;
        }
    }

    public static class DeviceProfiles
    {
        public struct Profile
        {
            public string Model, Name, OS, CPU, GPU, GPUVendor;
            public int VRAM, RAM, Cores;
        }

        public static readonly Profile[] All = new Profile[]
        {
            new Profile { Model = "Oculus Quest 2", Name = "Quest 2", OS = "Android OS 10 / API-29", CPU = "Qualcomm Snapdragon XR2", GPU = "Adreno (TM) 650", GPUVendor = "Qualcomm", VRAM = 1024, RAM = 6144, Cores = 8 },
            new Profile { Model = "Oculus Quest 2", Name = "My Quest", OS = "Android OS 12 / API-31", CPU = "Qualcomm Snapdragon XR2", GPU = "Adreno (TM) 650", GPUVendor = "Qualcomm", VRAM = 1024, RAM = 6144, Cores = 8 },
            new Profile { Model = "Meta Quest 3", Name = "Quest 3", OS = "Android OS 12 / API-31", CPU = "Qualcomm Snapdragon XR2 Gen 2", GPU = "Adreno (TM) 740", GPUVendor = "Qualcomm", VRAM = 2048, RAM = 8192, Cores = 8 },
            new Profile { Model = "Meta Quest 3", Name = "Meta Quest", OS = "Android OS 13 / API-33", CPU = "Qualcomm Snapdragon XR2 Gen 2", GPU = "Adreno (TM) 740", GPUVendor = "Qualcomm", VRAM = 2048, RAM = 8192, Cores = 8 },
            new Profile { Model = "Meta Quest 3S", Name = "Quest 3S", OS = "Android OS 12 / API-31", CPU = "Qualcomm Snapdragon XR2 Gen 2", GPU = "Adreno (TM) 740", GPUVendor = "Qualcomm", VRAM = 2048, RAM = 8192, Cores = 8 },
            new Profile { Model = "Meta Quest Pro", Name = "Quest Pro", OS = "Android OS 12 / API-31", CPU = "Qualcomm Snapdragon XR2 Gen 1", GPU = "Adreno (TM) 650", GPUVendor = "Qualcomm", VRAM = 2048, RAM = 12288, Cores = 8 },
            new Profile { Model = "Oculus Rift S", Name = "DESKTOP", OS = "Windows 10  (10.0.19045) 64bit", CPU = "AMD Ryzen 5 3600 6-Core Processor", GPU = "NVIDIA GeForce GTX 1660 SUPER", GPUVendor = "NVIDIA Corporation", VRAM = 6144, RAM = 16384, Cores = 12 },
            new Profile { Model = "Oculus Rift S", Name = "Gaming PC", OS = "Windows 10  (10.0.19044) 64bit", CPU = "Intel(R) Core(TM) i5-10400 CPU @ 2.90GHz", GPU = "NVIDIA GeForce RTX 2060", GPUVendor = "NVIDIA Corporation", VRAM = 6144, RAM = 16384, Cores = 12 },
            new Profile { Model = "Valve Index", Name = "DESKTOP", OS = "Windows 11  (10.0.22621) 64bit", CPU = "AMD Ryzen 7 5800X 8-Core Processor", GPU = "NVIDIA GeForce RTX 3070", GPUVendor = "NVIDIA Corporation", VRAM = 8192, RAM = 32768, Cores = 16 },
            new Profile { Model = "Valve Index", Name = "PC", OS = "Windows 10  (10.0.19045) 64bit", CPU = "Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz", GPU = "NVIDIA GeForce RTX 3080", GPUVendor = "NVIDIA Corporation", VRAM = 10240, RAM = 32768, Cores = 20 },
            new Profile { Model = "HTC VIVE Pro 2", Name = "Home PC", OS = "Windows 11  (10.0.22631) 64bit", CPU = "Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz", GPU = "NVIDIA GeForce RTX 4070", GPUVendor = "NVIDIA Corporation", VRAM = 12288, RAM = 32768, Cores = 20 },
            new Profile { Model = "HP Reverb G2", Name = "DESKTOP", OS = "Windows 11  (10.0.22000) 64bit", CPU = "AMD Ryzen 5 5600X 6-Core Processor", GPU = "AMD Radeon RX 6700 XT", GPUVendor = "AMD", VRAM = 12288, RAM = 16384, Cores = 12 },
            new Profile { Model = "Oculus Rift S", Name = "LAPTOP", OS = "Windows 10  (10.0.19043) 64bit", CPU = "AMD Ryzen 7 3700X 8-Core Processor", GPU = "AMD Radeon RX 5700 XT", GPUVendor = "AMD", VRAM = 8192, RAM = 16384, Cores = 16 },
            new Profile { Model = "Pico 4", Name = "Pico 4", OS = "Android OS 12 / API-31", CPU = "Qualcomm Snapdragon XR2", GPU = "Adreno (TM) 650", GPUVendor = "Qualcomm", VRAM = 1024, RAM = 8192, Cores = 8 },
        };

        private static int _selectedIndex = -1;
        private static readonly string ProfilePath = Path.Combine(Application.persistentDataPath, ".gl_dp");

        public static Profile Selected
        {
            get
            {
                if (_selectedIndex < 0)
                {
                    try
                    {
                        string stored = SafetyPatches.ReadEncryptedPublic(ProfilePath);
                        if (stored != null && int.TryParse(stored, out int idx) && idx >= 0 && idx < All.Length)
                            _selectedIndex = idx;
                        else
                        {
                            _selectedIndex = SafetyPatches.SecureRandomInt(All.Length);
                            SafetyPatches.WriteEncryptedPublic(ProfilePath, _selectedIndex.ToString());
                        }
                    }
                    catch { _selectedIndex = SafetyPatches.SecureRandomInt(All.Length); }
                }
                return All[_selectedIndex];
            }
        }
    }

    [HarmonyPatch(typeof(SystemInfo), "deviceUniqueIdentifier", MethodType.Getter)]
    [HarmonyPriority(Priority.First)]
    public class PatchDeviceId
    {
        [HarmonyPrefix]
        public static bool Prefix(ref string __result)
        {
            __result = SafetyPatches.GetSpoofedHWID();
            return false;
        }
    }

    [HarmonyPatch(typeof(SystemInfo), "deviceModel", MethodType.Getter)]
    [HarmonyPriority(Priority.First)]
    public class PatchDeviceModel
    {
        [HarmonyPrefix]
        public static bool Prefix(ref string __result)
        {
            __result = DeviceProfiles.Selected.Model;
            return false;
        }
    }

    [HarmonyPatch(typeof(SystemInfo), "deviceName", MethodType.Getter)]
    [HarmonyPriority(Priority.First)]
    public class PatchDeviceName
    {
        [HarmonyPrefix]
        public static bool Prefix(ref string __result)
        {
            __result = DeviceProfiles.Selected.Name;
            return false;
        }
    }

    [HarmonyPatch(typeof(SystemInfo), "operatingSystem", MethodType.Getter)]
    [HarmonyPriority(Priority.First)]
    public class PatchOperatingSystem
    {
        [HarmonyPrefix]
        public static bool Prefix(ref string __result)
        {
            __result = DeviceProfiles.Selected.OS;
            return false;
        }
    }

    [HarmonyPatch(typeof(SystemInfo), "processorType", MethodType.Getter)]
    [HarmonyPriority(Priority.First)]
    public class PatchProcessorType
    {
        [HarmonyPrefix]
        public static bool Prefix(ref string __result)
        {
            __result = DeviceProfiles.Selected.CPU;
            return false;
        }
    }

    [HarmonyPatch(typeof(SystemInfo), "graphicsDeviceName", MethodType.Getter)]
    [HarmonyPriority(Priority.First)]
    public class PatchGraphicsDeviceName
    {
        [HarmonyPrefix]
        public static bool Prefix(ref string __result)
        {
            __result = DeviceProfiles.Selected.GPU;
            return false;
        }
    }

    [HarmonyPatch(typeof(SystemInfo), "graphicsDeviceVendor", MethodType.Getter)]
    [HarmonyPriority(Priority.First)]
    public class PatchGraphicsDeviceVendor
    {
        [HarmonyPrefix]
        public static bool Prefix(ref string __result)
        {
            __result = DeviceProfiles.Selected.GPUVendor;
            return false;
        }
    }

    [HarmonyPatch(typeof(SystemInfo), "graphicsMemorySize", MethodType.Getter)]
    [HarmonyPriority(Priority.First)]
    public class PatchGraphicsMemorySize
    {
        [HarmonyPrefix]
        public static bool Prefix(ref int __result)
        {
            __result = DeviceProfiles.Selected.VRAM;
            return false;
        }
    }

    [HarmonyPatch(typeof(SystemInfo), "systemMemorySize", MethodType.Getter)]
    [HarmonyPriority(Priority.First)]
    public class PatchSystemMemorySize
    {
        [HarmonyPrefix]
        public static bool Prefix(ref int __result)
        {
            __result = DeviceProfiles.Selected.RAM;
            return false;
        }
    }

    [HarmonyPatch(typeof(SystemInfo), "processorCount", MethodType.Getter)]
    [HarmonyPriority(Priority.First)]
    public class PatchProcessorCount
    {
        [HarmonyPrefix]
        public static bool Prefix(ref int __result)
        {
            __result = DeviceProfiles.Selected.Cores;
            return false;
        }
    }

    [HarmonyPatch(typeof(CreateReportRequest), "ToHttpRequest")]
    [HarmonyPriority(Priority.First)]
    public class PatchHttpReport1 { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchSendReport; }

    [HarmonyPatch(typeof(CreateBanRequest), "ToHttpRequest")]
    [HarmonyPriority(Priority.First)]
    public class PatchHttpBan { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchSendReport; }

    [HarmonyPatch(typeof(ServerCreateReportRequest), "ToHttpRequest")]
    [HarmonyPriority(Priority.First)]
    public class PatchHttpReport2 { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchSendReport; }

    [HarmonyPatch(typeof(MothershipWriteEventsRequest), "ToHttpRequest")]
    [HarmonyPriority(Priority.First)]
    public class PatchMothershipEvents { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchTelemetry; }

    [HarmonyPatch(typeof(MothershipCreateReportCallback), "OnCompleteCallback")]
    [HarmonyPriority(Priority.First)]
    public class PatchMothershipReport { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchSendReport; }

    [HarmonyPatch(typeof(MothershipClientContext), "IsClientLoggedIn")]
    [HarmonyPriority(Priority.First)]
    public class PatchMothershipLogin
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result) { __result = true; return false; }
    }

    [HarmonyPatch(typeof(NetworkSystemRaiseEvent), "RaiseEvent", new Type[] { typeof(byte), typeof(object), typeof(NetEventOptions), typeof(bool) })]
    [HarmonyPriority(Priority.First)]
    public class PatchNetworkEvent1
    {
        [HarmonyPrefix]
        public static bool Prefix(byte code)
        {
            if (SafetyConfig.NetworkEventBlockEnabled && (code == 8 || code == 50 || code == 51))
                return false;
            if (!SafetyPatches.ShouldAllowEvent(code))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(NetworkSystemRaiseEvent), "RaiseEvent", new Type[] { typeof(byte), typeof(object) })]
    [HarmonyPriority(Priority.First)]
    public class PatchNetworkEvent2
    {
        [HarmonyPrefix]
        public static bool Prefix(byte code)
        {
            if (SafetyConfig.NetworkEventBlockEnabled && (code == 8 || code == 50 || code == 51))
                return false;
            if (!SafetyPatches.ShouldAllowEvent(code))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(GorillaPlayerScoreboardLine), "ReportPlayer")]
    [HarmonyPriority(Priority.First)]
    public class PatchScoreboardReport
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!SafetyConfig.PatchSendReport) return true;
            return false;
        }
    }

    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    public class PatchLckTelemetry
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var type = AccessTools.TypeByName("Liv.Lck.Telemetry.LckTelemetryClient");
            if (type != null)
            {
                var sendMethod = AccessTools.Method(type, "SendTelemetry");
                if (sendMethod != null) yield return sendMethod;

                var errorMethod = AccessTools.Method(type, "SendErrorTelemetry");
                if (errorMethod != null) yield return errorMethod;
            }
        }

        [HarmonyPrefix]
        public static bool Prefix() => !SafetyPatches.ShouldBlockTelemetry();
    }

    [HarmonyPatch(typeof(GorillaComputer), "CheckAutoBanListForName")]
    [HarmonyPriority(Priority.First)]
    public class PatchCheckAutoBanListForName
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            if (!SafetyConfig.PatchAutoBanList) return true;
            if (!SafetyConfig.NameBanBypassEnabled) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayFabClientAPI), "UpdateUserTitleDisplayName")]
    [HarmonyPriority(Priority.First)]
    public class PatchDisplayNameSpoof
    {
        private static readonly string[] FirstParts = { "Cool", "Dark", "Big", "Fast", "Red", "Blue", "Pro", "Epic", "Wild", "Hot", "Ice", "Max", "Neo", "Ace", "Top", "Sky", "Zen", "Fox", "Rex", "Jet" };
        private static readonly string[] SecondParts = { "Monkey", "Player", "Gamer", "Gorilla", "Tiger", "Wolf", "Bear", "Eagle", "Hawk", "Storm", "Fire", "Shadow", "Star", "King", "Boss", "Nova", "Fury", "Blaze", "Ninja", "Ghost" };

        [HarmonyPrefix]
        public static void Prefix(ref PlayFab.ClientModels.UpdateUserTitleDisplayNameRequest request)
        {
            if (!SafetyConfig.NameBanBypassEnabled) return;
            try
            {
                string part1 = FirstParts[SafetyPatches.SecureRandomInt(FirstParts.Length)];
                string part2 = SecondParts[SafetyPatches.SecureRandomInt(SecondParts.Length)];
                int num = SafetyPatches.SecureRandomInt(9999);
                request.DisplayName = part1 + part2 + num.ToString();
            }
            catch { SafetyPatches.TrackError(); }
        }
    }

    [HarmonyPatch(typeof(GorillaComputer), "OnErrorShared")]
    [HarmonyPriority(Priority.First)]
    public class PatchOnErrorShared
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayFabError error)
        {
            if (!SafetyConfig.PatchBanDetection) return true;
            try
            {
                if (error != null && error.ErrorMessage != null && error.ErrorMessage.Contains("is currently banned"))
                {
                    SafetyPatches.AnnounceBanOnce();

                    if (error.ErrorDetails != null)
                    {
                        foreach (var kvp in error.ErrorDetails)
                        {
                            string reason = kvp.Key;
                            string expiry = kvp.Value != null && kvp.Value.Count > 0 ? kvp.Value[0] : "Unknown";
                            bool indefinite = expiry == "Indefinite";

                            string banText = $"Account banned.\r\nReason: {reason}\r\n";
                            if (indefinite)
                                banText += "Duration: Indefinite\r\nUnban: Never";
                            else
                            {
                                try
                                {
                                    DateTime unbanDate = DateTime.Parse(expiry);
                                    TimeSpan remaining = unbanDate - DateTime.UtcNow;
                                    banText += $"Time Left: {remaining.Days}d {remaining.Hours}h {remaining.Minutes}m\r\nUnban: {unbanDate:MMMM dd, yyyy h:mm tt}";
                                }
                                catch { banText += $"Expiry: {expiry}"; }
                            }

                            Plugin.Instance?.Log(banText);
                            GorillaComputer.instance?.GeneralFailureMessage(banText);
                            return false;
                        }
                    }
                }
            }
            catch { SafetyPatches.TrackError(); }
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayFabUnityHttp), "MakeApiCall")]
    [HarmonyPriority(Priority.First)]
    public class PatchPlayFabUnityHttpCrash
    {
        [HarmonyPrefix]
        public static void Prefix(object reqContainerObj)
        {
            try
            {
                var containerType = reqContainerObj.GetType();
                var errorField = containerType.GetField("ErrorCallback");
                if (errorField != null)
                {
                    var originalCallback = errorField.GetValue(reqContainerObj) as Action<PlayFabError>;
                    errorField.SetValue(reqContainerObj, new Action<PlayFabError>((error) =>
                    {
                        try
                        {
                            if (error?.ErrorMessage?.Contains("banned") == true)
                            {
                                return;
                            }
                            originalCallback?.Invoke(error);
                        }
                        catch { SafetyPatches.TrackError(); }
                    }));
                }
            }
            catch { SafetyPatches.TrackError(); }
        }
    }

    [HarmonyPatch(typeof(PlayFabWebRequest), "MakeApiCall")]
    [HarmonyPriority(Priority.First)]
    public class PatchPlayFabWebRequestCrash
    {
        [HarmonyPrefix]
        public static void Prefix(object reqContainerObj)
        {
            try
            {
                var containerType = reqContainerObj.GetType();
                var errorField = containerType.GetField("ErrorCallback");
                if (errorField != null)
                {
                    var originalCallback = errorField.GetValue(reqContainerObj) as Action<PlayFabError>;
                    errorField.SetValue(reqContainerObj, new Action<PlayFabError>((error) =>
                    {
                        try
                        {
                            if (error?.ErrorMessage?.Contains("banned") == true)
                            {
                                return;
                            }
                            originalCallback?.Invoke(error);
                        }
                        catch { SafetyPatches.TrackError(); }
                    }));
                }
            }
            catch { SafetyPatches.TrackError(); }
        }
    }

    [HarmonyPatch(typeof(KIDManager), "UseKID")]
    [HarmonyPriority(Priority.First)]
    public class PatchKIDUseKID
    {
        [HarmonyPrefix]
        public static bool Prefix(ref System.Threading.Tasks.Task<bool> __result)
        {
            if (!SafetyConfig.KIDBypassEnabled) return true;
            __result = System.Threading.Tasks.Task.FromResult(false);
            return false;
        }
    }

    [HarmonyPatch(typeof(KIDManager), "HasPermissionToUseFeature")]
    [HarmonyPriority(Priority.First)]
    public class PatchKIDPermission
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (SafetyConfig.KIDBypassEnabled)
                __result = true;
        }
    }

    [HarmonyPatch(typeof(GorillaServer), "UploadGorillanalytics")]
    [HarmonyPriority(Priority.First)]
    public class PatchUploadAnalytics
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyPatches.ShouldBlockTelemetry();
    }

    [HarmonyPatch(typeof(GorillaServer), "CheckForBadName")]
    [HarmonyPriority(Priority.First)]
    public class PatchCheckForBadName
    {
        [HarmonyPrefix]
        public static bool Prefix(object request, Action<ExecuteFunctionResult> successCallback, Action<PlayFabError> errorCallback)
        {
            if (!SafetyConfig.PatchBadNameCheck) return true;
            if (!SafetyConfig.NameBanBypassEnabled) return true;
            try
            {
                if (successCallback != null)
                {
                    var result = new ExecuteFunctionResult();
                    var json = new JsonObject();
                    json.Add("result", 0);
                    result.FunctionResult = json;
                    successCallback.Invoke(result);
                }
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(GorillaServer), "CheckIsMothershipTelemetryEnabled")]
    [HarmonyPriority(Priority.First)]
    public class PatchMothershipTelemetryFlag
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (SafetyConfig.PatchTelemetry && SafetyConfig.TelemetryBlockEnabled) __result = false;
        }
    }

    [HarmonyPatch(typeof(GorillaServer), "CheckIsPlayFabTelemetryEnabled")]
    [HarmonyPriority(Priority.First)]
    public class PatchPlayFabTelemetryFlag
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (SafetyConfig.PatchPlayFabReport && SafetyConfig.TelemetryBlockEnabled) __result = false;
        }
    }

    [HarmonyPatch(typeof(GorillaComputer), "CheckAutoBanListForRoomName")]
    [HarmonyPriority(Priority.First)]
    public class PatchAutobanRoom
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchAutoBanList;
    }

    [HarmonyPatch(typeof(GorillaComputer), "CheckAutoBanListForPlayerName")]
    [HarmonyPriority(Priority.First)]
    public class PatchAutobanPlayer
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchAutoBanList;
    }

    [HarmonyPatch(typeof(GorillaComputer), "CheckAutoBanListForTroopName")]
    [HarmonyPriority(Priority.First)]
    public class PatchAutobanTroop
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchAutoBanList;
    }

    [HarmonyPatch(typeof(GorillaComputer), "CheckForBadRoomName")]
    [HarmonyPriority(Priority.First)]
    public class PatchBadRoomName
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchBadNameCheck;
    }

    [HarmonyPatch(typeof(GorillaComputer), "CheckForBadPlayerName")]
    [HarmonyPriority(Priority.First)]
    public class PatchBadPlayerName
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchBadNameCheck;
    }

    [HarmonyPatch(typeof(GorillaComputer), "CheckForBadTroopName")]
    [HarmonyPriority(Priority.First)]
    public class PatchBadTroopName
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchBadNameCheck;
    }

    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    public class PatchIncrementRPCTracker
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var m in typeof(MonkeAgent).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                if (m.Name == "IncrementRPCTracker") yield return m;
        }
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            if (!SafetyConfig.PatchRPCLimits) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    public class PatchBuilderTelemetry
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var m in typeof(GorillaTelemetry).GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "PostBuilderKioskEvent") yield return m;
        }
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyPatches.ShouldBlockTelemetry();
    }

    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    public class PatchSuperInfectionTelemetry
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var m in typeof(GorillaTelemetry).GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "SuperInfectionEvent") yield return m;
        }
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyPatches.ShouldBlockTelemetry();
    }

    [HarmonyPatch(typeof(PlayFabAuthenticator), "ShowBanMessage")]
    [HarmonyPriority(Priority.First)]
    public class PatchShowBanMessage
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!SafetyConfig.PatchBanDetection) return true;
            Plugin.Instance?.Log("[BAN] PlayFabAuthenticator.ShowBanMessage called � account IS banned server-side");
            SafetyPatches.AnnounceBanOnce();
            return false;
        }
    }

    [HarmonyPatch(typeof(MothershipWriteEventsCallback), "OnCompleteCallback")]
    [HarmonyPriority(Priority.First)]
    public class PatchMothershipWriteCallback { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchTelemetry; }

    [HarmonyPatch(typeof(ServerCreateBanRequest), "ToHttpRequest")]
    [HarmonyPriority(Priority.First)]
    public class PatchServerBanRequest { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchSendReport; }

    [HarmonyPatch(typeof(ServerBulkBansRequest), "ToHttpRequest")]
    [HarmonyPriority(Priority.First)]
    public class PatchBulkBansRequest { [HarmonyPrefix] public static bool Prefix() => !SafetyConfig.PatchSendReport; }

    [HarmonyPatch(typeof(Player), "SetCustomProperties")]
    [HarmonyPriority(Priority.First)]
    public class PatchSetCustomProperties
    {
        internal static readonly HashSet<string> SafeProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "didTutorial"
        };

        internal static bool _bypassActive = false;

        [HarmonyPrefix]
        public static bool Prefix(Player __instance, ref ExitGames.Client.Photon.Hashtable propertiesToSet)
        {
            if (!SafetyConfig.PatchModCheckers) return true;
            if (_bypassActive) return true;
            if (!__instance.IsLocal) return true;
            if (propertiesToSet.Keys.Cast<object>().Any(k => !SafeProperties.Contains(k.ToString())))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Player), "set_CustomProperties")]
    [HarmonyPriority(Priority.First)]
    public class PatchSetCustomPropertiesField
    {
        [HarmonyPrefix]
        public static bool Prefix(Player __instance, ref ExitGames.Client.Photon.Hashtable value)
        {
            if (!SafetyConfig.PatchModCheckers) return true;
            if (PatchSetCustomProperties._bypassActive) return true;
            if (!__instance.IsLocal) return true;
            if (value.Keys.Cast<object>().Any(k => !PatchSetCustomProperties.SafeProperties.Contains(k.ToString())))
                return false;
            return true;
        }
    }

    public static class ModeratorDetector
    {
        private static float _lastCheck = 0f;
        private static readonly string[] StaffCosmetics = { "LBAAK", "LBAAD", "LMAPY" };

        public static bool DetectedModerator { get; private set; } = false;
        public static string ModeratorName { get; private set; } = "";

        public static void Check()
        {
            if (!SafetyConfig.ModeratorDetectorEnabled) return;
            if (Time.time - _lastCheck < 2f) return;
            _lastCheck = Time.time;

            if (!NetworkSystem.Instance.InRoom) { DetectedModerator = false; return; }

            try
            {
                foreach (var rig in GorillaParent.instance.vrrigs)
                {
                    if (rig == null || rig.isOfflineVRRig || rig.isLocal) continue;

                    string cosmetics = rig.rawCosmeticString;
                    if (string.IsNullOrEmpty(cosmetics)) continue;

                    HashSet<string> ownedIds = new HashSet<string>(cosmetics.Split(','));

                    foreach (var staff in StaffCosmetics)
                    {
                        if (ownedIds.Contains(staff))
                        {
                            DetectedModerator = true;
                            string playerName = "Unknown";
                            string playerId = "Unknown";
                            float r = 0, g = 0, b = 0;

                            try
                            {
                                var player = rig.Creator;
                                if (player != null)
                                {
                                    playerName = player.NickName;
                                    playerId = player.UserId;
                                }
                                r = rig.playerColor.r * 255f;
                                g = rig.playerColor.g * 255f;
                                b = rig.playerColor.b * 255f;
                            }
                            catch { }

                            ModeratorName = playerName;

                            Plugin.Instance?.Log($"[MODERATOR] {playerName} ({playerId}) color=({r:F0},{g:F0},{b:F0}) cosmetic={staff}");

                            AudioManager.Play("moderator_detected", AudioManager.AudioCategory.Warning);
                            SafetyPatches.SafetyDisconnect($"Moderator detected: {playerName}");
                            return;
                        }
                    }
                }
                DetectedModerator = false;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(VRRig), "OnDisable")]
    [HarmonyPriority(Priority.First)]
    public class PatchVRRigOnDisable
    {
        [HarmonyPrefix]
        public static bool Prefix(VRRig __instance)
        {
            return !__instance.isLocal;
        }
    }

    public static class ContentCreatorDetector
    {
        private static float _lastCheck = 0f;

        public static readonly Dictionary<string, string> CreatorCosmetics = new Dictionary<string, string>
        {
            { "LBADE", "Early Supporter Badge" },
            { "LBAGE", "Content Creator Badge" },
            { "LBACK", "Special Event Badge" },
        };

        public static bool DetectedCreator { get; private set; } = false;
        public static string CreatorName { get; private set; } = "";

        public static void Check()
        {
            if (!SafetyConfig.AntiContentCreatorEnabled) return;
            if (Time.time - _lastCheck < 2f) return;
            _lastCheck = Time.time;

            if (!NetworkSystem.Instance.InRoom) { DetectedCreator = false; return; }

            try
            {
                foreach (var rig in GorillaParent.instance.vrrigs)
                {
                    if (rig == null || rig.isOfflineVRRig || rig.isLocal) continue;
                    string cosmetics = rig.rawCosmeticString;
                    if (string.IsNullOrEmpty(cosmetics)) continue;

                    HashSet<string> ownedIds = new HashSet<string>(cosmetics.Split(','));

                    foreach (var kvp in CreatorCosmetics)
                    {
                        if (ownedIds.Contains(kvp.Key))
                        {
                            DetectedCreator = true;
                            string playerName = "Unknown";
                            string playerId = "Unknown";
                            try
                            {
                                var player = rig.Creator;
                                if (player != null) { playerName = player.NickName; playerId = player.UserId; }
                            }
                            catch { }

                            CreatorName = playerName;

                            Plugin.Instance?.Log($"[CREATOR] {playerName} ({playerId}) cosmetic={kvp.Key} ({kvp.Value})");

                            AudioManager.Play("moderator_detected", AudioManager.AudioCategory.Warning);
                            SafetyPatches.SafetyDisconnect($"Creator detected: {playerName}");
                            return;
                        }
                    }
                }
                DetectedCreator = false;
            }
            catch { }
        }
    }

    public static class CosmeticNotifier
    {
        private static HashSet<string> _notifiedRigs = new HashSet<string>();
        private static float _lastCheck = 0f;

        public static readonly Dictionary<string, string> SpecialCosmetics = new Dictionary<string, string>
        {
            { "LBAAK", "Developer Badge" },
            { "LBAAD", "Staff Badge" },
            { "LMAPY", "Admin Badge" },
            { "LBADE", "Early Supporter" },
            { "LBAGE", "Creator Badge" },
            { "LBAAE", "Rare Cosmetic" },
            { "LBACK", "Event Badge" },
        };

        public static string LastNotification { get; private set; } = "";

        public static void Check()
        {
            if (!SafetyConfig.CosmeticNotificationsEnabled) return;
            if (Time.time - _lastCheck < 3f) return;
            _lastCheck = Time.time;

            if (!NetworkSystem.Instance.InRoom) { _notifiedRigs.Clear(); return; }

            try
            {
                foreach (var rig in GorillaParent.instance.vrrigs)
                {
                    if (rig == null || rig.isOfflineVRRig || rig.isLocal) continue;
                    string cosmetics = rig.rawCosmeticString;
                    if (string.IsNullOrEmpty(cosmetics)) continue;

                    string rigId = rig.Creator?.UserId ?? rig.GetHashCode().ToString();
                    if (_notifiedRigs.Contains(rigId)) continue;

                    HashSet<string> ownedIds = new HashSet<string>(cosmetics.Split(','));

                    foreach (var kvp in SpecialCosmetics)
                    {
                        if (ownedIds.Contains(kvp.Key))
                        {
                            string name = rig.Creator?.NickName ?? "Unknown";
                            LastNotification = $"{name} has {kvp.Value}";
                            Plugin.Instance?.Log($"[COSMETIC] {LastNotification}");
                            _notifiedRigs.Add(rigId);
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        public static void Reset() { _notifiedRigs.Clear(); LastNotification = ""; }
    }

    [HarmonyPatch(typeof(PhotonNetwork), "RunViewUpdate")]
    [HarmonyPriority(Priority.First)]
    public class PatchRunViewUpdate
    {
        public static Func<bool> OverrideSerialization;

        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!PhotonNetwork.InRoom) return true;

            if (OverrideSerialization != null)
            {
                try
                {
                    return OverrideSerialization.Invoke();
                }
                catch
                {
                    return true;
                }
            }
            return true;
        }
    }

    public static class AntiKickHelper
    {
        public static void Enable()
        {
            PatchRunViewUpdate.OverrideSerialization = () => true;
        }

        public static void Disable()
        {
            PatchRunViewUpdate.OverrideSerialization = null;
        }
        private static void SendSerialize(PhotonView pv)
        {
            if (pv == null || !PhotonNetwork.InRoom) return;

            try
            {
                List<object> data = PhotonNetwork.OnSerializeWrite(pv);
                if (data == null) return;

                PhotonNetwork.RaiseEventBatch batchKey = default;
                bool mixedReliable = pv.mixedModeIsReliable;
                batchKey.Reliable = (pv.Synchronization == (ViewSynchronization)1 || mixedReliable);
                batchKey.Group = pv.Group;

                IDictionary batches = PhotonNetwork.serializeViewBatches;
                PhotonNetwork.SerializeViewBatch batch = new PhotonNetwork.SerializeViewBatch(batchKey, 2);

                if (!batches.Contains(batchKey))
                {
                    batches[batchKey] = batch;
                }

                batch.Add(data);

                RaiseEventOptions options = PhotonNetwork.serializeRaiseEvOptions;
                bool reliable = batch.Batch.Reliable;
                object objectUpdate = batch.ObjectUpdates;

                byte levelPrefix = PhotonNetwork.currentLevelPrefix;
                ((object[])objectUpdate)[0] = PhotonNetwork.ServerTimestamp;
                ((object[])objectUpdate)[1] = (levelPrefix != 0) ? (object)levelPrefix : null;

                byte eventCode = reliable ? (byte)206 : (byte)201;
                SendOptions sendOpts = reliable ? SendOptions.SendReliable : SendOptions.SendUnreliable;
                PhotonNetwork.NetworkingClient.OpRaiseEvent(eventCode, objectUpdate, options, sendOpts);

                batch.Clear();
            }
            catch { }
        }
    }

    public static class ACReportNotifier
    {
        public static string LastReport { get; private set; } = "";
        public static float LastReportTime { get; private set; } = 0f;

        public static void NotifyReport(string reason)
        {
            LastReport = reason ?? "unknown";
            LastReportTime = Time.time;
            Plugin.Instance?.Log($"[AC-REPORT] Notification: reported for \"{reason}\"");
        }

        public static void Clear()
        {
            LastReport = "";
            LastReportTime = 0f;
        }
        public static bool HasActiveNotification => !string.IsNullOrEmpty(LastReport) && (Time.time - LastReportTime) < 30f;
    }

    public static class AutomodBypass
    {
        private static float _lastVol = 0f;
        private static float _silenceStart = -1f;
        private static bool _reloaded = false;

        private static FieldInfo _recorderField;
        private static PropertyInfo _sourceTypeProp;
        private static MethodInfo _restartMethod;
        private static bool _reflectionCached = false;

        private static void CacheReflection()
        {
            if (_reflectionCached) return;
            _reflectionCached = true;
            try
            {
                _recorderField = typeof(GorillaTagger).GetField("myRecorder",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            catch { }
        }

        public static void Update()
        {
            if (!SafetyConfig.AutomodBypassEnabled) return;

            try
            {
                GorillaTagger.moderationMutedTime = -1f;

                if (GorillaComputer.instance != null && GorillaComputer.instance.autoMuteType != "OFF")
                {
                    GorillaComputer.instance.autoMuteType = "OFF";
                    PlayerPrefs.SetInt("autoMute", 0);
                    PlayerPrefs.Save();
                }

                try
                {
                    CacheReflection();
                    if (_recorderField == null) return;

                    object recorder = _recorderField.GetValue(GorillaTagger.Instance);
                    if (recorder == null) return;

                    if (_sourceTypeProp == null)
                        _sourceTypeProp = recorder.GetType().GetProperty("SourceType");
                    if (_restartMethod == null)
                        _restartMethod = recorder.GetType().GetMethod("RestartRecording");

                    if (_sourceTypeProp != null)
                    {
                        int sourceType = (int)_sourceTypeProp.GetValue(recorder);
                        if (sourceType != 0) return;
                    }

                    float loudness = 0f;
                    var component = VRRig.LocalRig?.GetComponent<GorillaSpeakerLoudness>();
                    if (component != null) loudness = component.Loudness;

                    if (loudness == 0f)
                    {
                        if (_lastVol != 0f) { _silenceStart = Time.time; _reloaded = false; }
                        if (_silenceStart > 0f && !_reloaded && Time.time - _silenceStart >= 0.25f)
                        {
                            if (_restartMethod != null) _restartMethod.Invoke(recorder, new object[] { true });
                            _reloaded = true;
                        }
                    }
                    else
                    {
                        _silenceStart = -1f;
                        _reloaded = false;
                    }
                    _lastVol = loudness;
                }
                catch { }
            }
            catch { }
        }
    }

    public static class AntiPredictions
    {
        private static Vector3 _smoothedLeft;
        private static Vector3 _smoothedRight;
        private static bool _initialized = false;

        public static void LateUpdate()
        {
            if (!SafetyConfig.AntiPredictionsEnabled) return;

            try
            {
                if (VRRig.LocalRig == null) return;

                var leftTarget = VRRig.LocalRig.leftHand?.rigTarget;
                var rightTarget = VRRig.LocalRig.rightHand?.rigTarget;
                if (leftTarget == null || rightTarget == null) return;

                Vector3 leftPos = leftTarget.localPosition;
                Vector3 rightPos = rightTarget.localPosition;

                if (!_initialized)
                {
                    _smoothedLeft = leftPos;
                    _smoothedRight = rightPos;
                    _initialized = true;
                    return;
                }

                _smoothedLeft = Vector3.Lerp(_smoothedLeft, leftPos, 0.75f);
                _smoothedRight = Vector3.Lerp(_smoothedRight, rightPos, 0.75f);

                leftTarget.localPosition = _smoothedLeft;
                rightTarget.localPosition = _smoothedRight;
            }
            catch { }
        }

        public static void Reset() { _initialized = false; }
    }

    public static class AntiLurkerSystem
    {
        private static float _lastCheck = 0f;
        private static LurkerGhost _cached = null;

        public static void Update()
        {
            if (!SafetyConfig.AntiLurkerEnabled) return;
            if (Time.time - _lastCheck < 1f) return;
            _lastCheck = Time.time;

            try
            {
                if (_cached == null)
                {
                    _cached = UnityEngine.Object.FindAnyObjectByType<LurkerGhost>();
                    if (_cached == null) return;
                }

                if (_cached.currentState == LurkerGhost.ghostState.charge && _cached.targetPlayer == NetworkSystem.Instance.LocalPlayer)
                {
                    _cached.ChangeState(LurkerGhost.ghostState.patrol);
                    Plugin.Instance?.Log("[Anti-Lurker] Deflected lurker ghost");
                }
            }
            catch { _cached = null; }
        }
    }

    public static class AutoGC
    {
        private static float _lastCollect = 0f;

        public static void Update()
        {
            if (!SafetyConfig.AutoGCEnabled) return;
            if (Time.time - _lastCollect < 60f) return;
            _lastCollect = Time.time;
            GC.Collect();
        }
    }

    public static class SupportPageSpoofer
    {
        private static float _lastApply = 0f;

        public static void Update()
        {
            if (!SafetyConfig.SupportPageSpoofEnabled) return;
            if (Time.time - _lastApply < 5f) return;
            _lastApply = Time.time;

            try
            {
                if (GorillaComputer.instance?.screenText == null) return;
                string current = GorillaComputer.instance.screenText.currentText;
                if (string.IsNullOrEmpty(current) || !current.Contains("STEAM")) return;

                GorillaComputer.instance.screenText.Set(
                    current.Replace("STEAM", "QUEST").Replace("Steam", "Quest"));
            }
            catch { }
        }
    }

    public static class RankedSpoofer
    {
        public static int TargetElo = 4000;
        public static int TargetBadge = 7;

        private static readonly string[] BadgeNames = { "Wood", "Rock", "Bronze", "Silver", "Gold", "Platinum", "Crystal", "Banana" };

        public static string GetBadgeName() => BadgeNames[Mathf.Clamp(TargetBadge, 0, BadgeNames.Length - 1)];

        public static void CycleElo(bool up)
        {
            TargetElo += up ? 100 : -100;
            if (TargetElo > 4000) TargetElo = 0;
            if (TargetElo < 0) TargetElo = 4000;
        }

        public static void CycleBadge(bool up)
        {
            TargetBadge += up ? 1 : -1;
            if (TargetBadge >= BadgeNames.Length) TargetBadge = 0;
            if (TargetBadge < 0) TargetBadge = BadgeNames.Length - 1;
        }

        public static void Update()
        {
            if (!SafetyConfig.RankedSpoofEnabled) return;

            try
            {
                if (VRRig.LocalRig == null) return;

                if (!Mathf.Approximately(VRRig.LocalRig.currentRankedELO, (float)TargetElo) ||
                    VRRig.LocalRig.currentRankedSubTierQuest != TargetBadge ||
                    VRRig.LocalRig.currentRankedSubTierPC != TargetBadge)
                {
                    VRRig.LocalRig.SetRankedInfo((float)TargetElo, TargetBadge, TargetBadge, true);
                }
            }
            catch { }
        }
    }

    public static class RPCFlusher
    {
        private static float _lastFlush = 0f;

        public static bool Flush()
        {
            if (Time.time - _lastFlush < 5f) return false;
            _lastFlush = Time.time;

            try
            {
                if (PhotonNetwork.LocalPlayer != null)
                    PhotonNetwork.RemoveRPCs(PhotonNetwork.LocalPlayer);
                Plugin.Instance?.Log("[RPC] Flushed buffered RPCs");
                return true;
            }
            catch { return false; }
        }
    }

    public static class FakeBehaviors
    {
        public static bool FakeOculusMenuActive { get => SafetyConfig.FakeOculusMenuEnabled; set => SafetyConfig.FakeOculusMenuEnabled = value; }
        public static bool FakeBrokenControllerActive { get => SafetyConfig.FakeBrokenControllerEnabled; set => SafetyConfig.FakeBrokenControllerEnabled = value; }

        public static void FakePowerOff()
        {
            try
            {
                if (ControllerInputPoller.instance == null) return;
                bool held = ControllerInputPoller.instance.leftControllerIndexFloat > 0.5f &&
                            ControllerInputPoller.instance.leftControllerSecondaryButton;
                if (!held) { VRRig.LocalRig.enabled = true; return; }

                VRRig.LocalRig.enabled = false;
                var rb = GorillaTagger.Instance.rigidbody;
                rb.linearVelocity = Vector3.zero;
            }
            catch { }
        }

        public static void FakeOculusMenu()
        {
            if (!FakeOculusMenuActive) return;
            try
            {
                var inp = ControllerInputPoller.instance;
                if (inp == null) return;
                inp.leftControllerGripFloat = 0f;
                inp.rightControllerGripFloat = 0f;
                inp.leftControllerIndexFloat = 0f;
                inp.rightControllerIndexFloat = 0f;
                inp.leftControllerPrimaryButton = false;
                inp.rightControllerPrimaryButton = false;
                inp.leftControllerSecondaryButton = false;
                inp.rightControllerSecondaryButton = false;
                if (GorillaTagger.Instance?.rigidbody != null)
                    GorillaTagger.Instance.rigidbody.linearVelocity = Vector3.zero;
            }
            catch { }
        }

        public static void FakeBrokenController()
        {
            if (!FakeBrokenControllerActive) return;
            try
            {
                var inp = ControllerInputPoller.instance;
                if (inp == null) return;
                inp.leftControllerGripFloat = 0f;
                inp.leftControllerIndexFloat = 0f;
                inp.leftControllerPrimaryButton = false;
                inp.leftControllerSecondaryButton = false;
                inp.leftControllerPrimaryButtonTouch = false;
                inp.leftControllerSecondaryButtonTouch = false;
            }
            catch { }
        }

        public static void NoFinger()
        {
            var inp = ControllerInputPoller.instance;
            if (inp == null) return;
            inp.leftControllerGripFloat = 0f;
            inp.rightControllerGripFloat = 0f;
            inp.leftControllerIndexFloat = 0f;
            inp.rightControllerIndexFloat = 0f;
            inp.leftControllerPrimaryButton = false;
            inp.leftControllerSecondaryButton = false;
            inp.rightControllerPrimaryButton = false;
            inp.rightControllerSecondaryButton = false;
            inp.leftControllerPrimaryButtonTouch = false;
            inp.leftControllerSecondaryButtonTouch = false;
            inp.rightControllerPrimaryButtonTouch = false;
            inp.rightControllerSecondaryButtonTouch = false;
        }
    }

    [HarmonyPatch(typeof(VRRig), "PackCompetitiveData")]
    [HarmonyPriority(Priority.First)]
    public class PatchFPSSpoof
    {
        [HarmonyPostfix]
        public static void Postfix(VRRig __instance, ref short __result)
        {
            if (!SafetyConfig.FPSSpoofEnabled) return;
            if (!__instance.isLocal) return;

            try
            {
                int turnData = (__result >> 8) & 0x1F;
                int spoofedFps = SafetyConfig.SpoofedFPS + UnityEngine.Random.Range(-5, 6);
                spoofedFps = Mathf.Clamp(spoofedFps, 30, 255);
                __result = (short)(spoofedFps + (turnData << 8));
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(VRRig), "Awake")]
    [HarmonyPriority(Priority.First)]
    public class PatchVRRigAwake
    {
        private static bool _firstAwake = true;

        [HarmonyPrefix]
        public static bool Prefix(VRRig __instance)
        {
            if (_firstAwake) { _firstAwake = false; return true; }
            if (__instance.isLocal && NetworkSystem.Instance != null && NetworkSystem.Instance.InRoom)
            {
                Plugin.Instance?.Log("[VRRig] Blocked local rig re-Awake");
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(AgeSliderWithProgressBar), "Tick")]
    [HarmonyPriority(Priority.First)]
    public class PatchAgeSlider
    {
        private static float _lastNameCheck = 0f;

        [HarmonyPrefix]
        public static void Prefix(AgeSliderWithProgressBar __instance)
        {
            if (!SafetyConfig.TOSBypassEnabled) return;
            try
            {
                __instance._currentAge = 21;
            }
            catch { }

            if (Time.time - _lastNameCheck < 5f) return;
            _lastNameCheck = Time.time;

            try
            {
                if (GorillaComputer.instance == null) return;
                string currentName = GorillaComputer.instance.savedName;
                if (string.IsNullOrEmpty(currentName)) return;

                bool nameApproved = true;
                try
                {
                    nameApproved = GorillaComputer.instance.CheckAutoBanListForName(currentName);
                }
                catch { }

                if (!nameApproved)
                {
                    Plugin.Instance?.Log($"[AntiNameBan] Name '{currentName}' is on ban list � auto-resetting");
                    IdentityChanger.ApplyRandomName();
                    AudioManager.Play("warning", AudioManager.AudioCategory.Warning);
                }
            }
            catch { }
        }
    }

    public static class GameRestarter
    {
        public static void Restart()
        {
            try
            {
                Plugin.Instance?.Log("[Restart] Restarting Gorilla Tag...");
                System.Diagnostics.Process.Start("steam://rungameid/1533390");
                Application.Quit();
            }
            catch (Exception ex)
            {
                Plugin.Instance?.Log($"[Restart] Failed: {ex.Message}");
            }
        }
    }

    public static class LobbyFixer
    {
        private static float _lastFix = 0f;
        public static bool Fix()
        {
            if (Time.time - _lastFix < 5f) return false;
            _lastFix = Time.time;

            try
            {
                if (PhotonNetwork.LocalPlayer != null)
                    PhotonNetwork.RemoveRPCs(PhotonNetwork.LocalPlayer);

                try
                {
                    var props = PhotonNetwork.LocalPlayer.CustomProperties;
                    var cleanProps = new ExitGames.Client.Photon.Hashtable();
                    foreach (var key in props.Keys)
                    {
                        string k = key.ToString();
                        if (k == "didTutorial")
                            cleanProps[key] = props[key];
                    }
                    PatchSetCustomProperties._bypassActive = true;
                    try { PhotonNetwork.LocalPlayer.SetCustomProperties(cleanProps); } finally { PatchSetCustomProperties._bypassActive = false; }
                }
                catch { }

                GC.Collect();

                Plugin.Instance?.Log("[LobbyFix] Cleared RPCs, properties, and counters");
                return true;
            }
            catch { return false; }
        }
        public static void Rejoin()
        {
            try
            {
                Fix();
                SafetyPatches.SafetyDisconnect("LobbyFix rejoin");
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(VRRig), "DroppedByPlayer")]
    [HarmonyPriority(Priority.First)]
    public class PatchDroppedByPlayer
    {
        [HarmonyPrefix]
        public static bool Prefix(VRRig __instance, VRRig grabbedByRig, Vector3 throwVelocity)
        {
            if (!SafetyConfig.AntiCrashEnabled || !__instance.isLocal) return true;
            float max = 10000f;
            return GTExt.IsValid(in throwVelocity, in max);
        }
    }

    [HarmonyPatch(typeof(VRRig), "RequestCosmetics")]
    [HarmonyPriority(Priority.First)]
    public class PatchRequestCosmetics
    {
        private static readonly List<float> callTimestamps = new List<float>();

        [HarmonyPrefix]
        public static bool Prefix(VRRig __instance)
        {
            if (!SafetyConfig.AntiCrashEnabled || !__instance.isLocal) return true;
            callTimestamps.Add(Time.time);
            callTimestamps.RemoveAll(t => Time.time - t > 1f);
            return callTimestamps.Count < 15;
        }
    }

    [HarmonyPatch(typeof(VRRig), "RequestMaterialColor")]
    [HarmonyPriority(Priority.First)]
    public class PatchRequestMaterialColor
    {
        private static readonly List<float> callTimestamps = new List<float>();

        [HarmonyPrefix]
        public static bool Prefix(VRRig __instance)
        {
            if (!SafetyConfig.AntiCrashEnabled || !__instance.isLocal) return true;
            callTimestamps.Add(Time.time);
            callTimestamps.RemoveAll(t => Time.time - t > 1f);
            return callTimestamps.Count < 15;
        }
    }

    [HarmonyPatch(typeof(DeployedChild), "Deploy")]
    [HarmonyPriority(Priority.First)]
    public class PatchDeployVelocity
    {
        [HarmonyPostfix]
        public static void Postfix(DeployedChild __instance)
        {
            if (!SafetyConfig.AntiCrashEnabled) return;
            try
            {
                __instance._rigidbody.linearVelocity = GTExt.ClampMagnitudeSafe(__instance._rigidbody.linearVelocity, 100f);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(LuauVm), "OnEvent")]
    [HarmonyPriority(Priority.First)]
    public class PatchLuauVmOnEvent
    {
        [HarmonyPrefix]
        public static bool Prefix(EventData eventData)
        {
            if (!SafetyConfig.AntiCrashEnabled) return true;
            try
            {
                if (eventData.Code != 180) return false;
                Player sender = PhotonNetwork.NetworkingClient.CurrentRoom.GetPlayer(eventData.Sender, false);
                object[] data = eventData.CustomData != null ? (object[])eventData.CustomData : new object[0];
                string cmd = data.Length > 0 ? (string)data[0] : "";
                if (sender != PhotonNetwork.LocalPlayer && data.Length > 1 && data[1] is double num)
                {
                    if (num == (double)PhotonNetwork.LocalPlayer.ActorNumber && cmd == "leaveGame")
                        return false;
                }
            }
            catch { }
            return true;
        }
    }

    [HarmonyPatch(typeof(RoomSystem), "SearchForShuttle")]
    [HarmonyPriority(Priority.First)]
    public class PatchSearchForShuttle
    {
        [HarmonyPrefix]
        public static bool Prefix(object[] shuffleData, PhotonMessageInfoWrapped info)
        {
            if (!SafetyConfig.AntiCrashEnabled) return true;
            try
            {
                if (shuffleData == null || shuffleData.Length == 0) return false;
                if (!PhotonNetwork.InRoom) return false;
                if (info.Sender == null) return false;
            }
            catch { return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(RoomInfo), "InternalCacheProperties")]
    [HarmonyPriority(Priority.First)]
    public class PatchInternalCacheProperties
    {
        [HarmonyPrefix]
        public static bool Prefix(RoomInfo __instance, ExitGames.Client.Photon.Hashtable propertiesToCache)
        {
            if (!SafetyConfig.AntiCrashEnabled) return true;
            try
            {
                return __instance.masterClientId != PhotonNetwork.LocalPlayer.ActorNumber
                    || propertiesToCache.Count != 1
                    || !propertiesToCache.ContainsKey((byte)248);
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(GameEntityManager), "JoinWithItemsRPC")]
    [HarmonyPriority(Priority.First)]
    public class PatchJoinWithItemsRPC
    {
        [HarmonyPrefix]
        public static bool Prefix(byte[] stateData)
        {
            if (!SafetyConfig.AntiCrashEnabled) return true;
            return stateData == null || stateData.Length <= 1000;
        }
    }

    [HarmonyPatch(typeof(VRRig), "PostTick")]
    [HarmonyPriority(Priority.First)]
    public class PatchVRRigPostTick
    {
        [HarmonyPrefix]
        public static bool Prefix(VRRig __instance)
        {
            if (__instance.isLocal && !__instance.enabled) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(LegalAgreements), "Update")]
    [HarmonyPriority(Priority.First)]
    public class PatchLegalAgreements
    {
        [HarmonyPrefix]
        public static bool Prefix(LegalAgreements __instance)
        {
            if (!SafetyConfig.TOSBypassEnabled) return true;
            try
            {
                ControllerInputPoller.instance.leftControllerPrimary2DAxis.y = -1f;
                __instance.scrollSpeed = 10f;
                __instance._maxScrollSpeed = 10f;
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(ModIOTermsOfUse_v1), "PostUpdate")]
    [HarmonyPriority(Priority.First)]
    public class PatchModIOTerms
    {
        [HarmonyPrefix]
        public static bool Prefix(ModIOTermsOfUse_v1 __instance)
        {
            if (!SafetyConfig.TOSBypassEnabled) return true;
            try
            {
                __instance.TurnPage(999);
                ControllerInputPoller.instance.leftControllerPrimary2DAxis.y = -1f;
                __instance.holdTime = 0.1f;
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(AgeSlider), "PostUpdate")]
    [HarmonyPriority(Priority.First)]
    public class PatchAgeSliderPostUpdate
    {
        [HarmonyPrefix]
        public static bool Prefix(AgeSlider __instance)
        {
            if (!SafetyConfig.TOSBypassEnabled) return true;
            try
            {
                __instance._currentAge = 21;
                __instance.holdTime = 0.1f;
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(PrivateUIRoom), "StartOverlay")]
    [HarmonyPriority(Priority.First)]
    public class PatchPrivateUIOverlay
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.TOSBypassEnabled;
    }

    public static class FakeReportMenuBehavior
    {
        public static void Update()
        {
            if (!SafetyConfig.FakeReportMenuEnabled) return;
            try
            {
                var inp = ControllerInputPoller.instance;
                if (inp == null) return;
                if (inp.leftControllerSecondaryButton)
                {
                    FakeBehaviors.NoFinger();
                }
                GorillaLocomotion.GTPlayer.Instance.inOverlay = inp.leftControllerPrimaryButton;
            }
            catch { }
        }
    }

    public static class FakeValveTrackingBehavior
    {
        public static void LateUpdate()
        {
            if (!SafetyConfig.FakeValveTrackingEnabled) return;
            try
            {
                var inp = ControllerInputPoller.instance;
                if (inp != null && inp.rightControllerPrimary2DAxis.y > 0.8f)
                {
                    VRRig.LocalRig.head.rigTarget.transform.rotation = Quaternion.identity;
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PlayFabAuthenticator), "OnPlayFabError")]
    [HarmonyPriority(Priority.First)]
    public class PatchOnPlayFabError
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayFabError obj)
        {
            if (!SafetyConfig.PatchBanDetection) return true;
            try
            {
                if (obj == null || obj.ErrorMessage == null) return true;
                string msg = obj.ErrorMessage.ToLowerInvariant();

                if (msg.Contains("currently banned"))
                {
                    bool isIpBan = msg.Contains("ip");
                    string banType = isIpBan ? "IP" : "Account";
                    Plugin.Instance?.Log($"[BAN] {banType} ban detected via PlayFab error: {obj.ErrorMessage}");
                    SafetyPatches.AnnounceBanOnce();

                    if (obj.Error == PlayFabErrorCode.AccountBanned)
                    {
                        GorillaComputer.instance?.GeneralFailureMessage(
                            $"{banType} ban detected.\r\nSignal Menu intercepted this error.\r\nReason: {obj.ErrorMessage}");
                    }

                    return false;
                }
            }
            catch { SafetyPatches.TrackError("OnPlayFabError"); }
            return true;
        }
    }

    [HarmonyPatch(typeof(CosmeticsController), "ReauthOrBan")]
    [HarmonyPriority(Priority.First)]
    public class PatchReauthOrBan
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayFabError error)
        {
            if (!SafetyConfig.PatchBanDetection) return true;
            try
            {
                if (error?.Error == PlayFabErrorCode.AccountBanned)
                {
                    Plugin.Instance?.Log("[BAN] ReauthOrBan intercepted � prevented game shutdown");
                    SafetyPatches.AnnounceBanOnce();
                    return false;
                }
            }
            catch { SafetyPatches.TrackError("ReauthOrBan"); }
            return true;
        }
    }

    [HarmonyPatch(typeof(GorillaComputer), "OnReturnCurrentVersion")]
    [HarmonyPriority(Priority.First)]
    public class PatchOnReturnCurrentVersion
    {
        [HarmonyPrefix]
        public static bool Prefix(ExecuteFunctionResult result)
        {
            if (!SafetyConfig.VersionBypassEnabled) return true;
            try
            {
                if (result?.FunctionResult == null) return true;

                var json = result.FunctionResult as JsonObject;
                if (json == null) return true;

                object failObj;
                if (json.TryGetValue("Fail", out failObj) && failObj is bool fail && fail)
                {
                    Plugin.Instance?.Log("[VERSION] Version mismatch detected � bypassing lockout");
                    return false;
                }

                object codeObj;
                if (json.TryGetValue("ResultCode", out codeObj))
                {
                    int code = 0;
                    if (codeObj is int c) code = c;
                    else if (codeObj is long l) code = (int)l;
                    else if (codeObj is double d) code = (int)d;

                    if (code != 0)
                    {
                        Plugin.Instance?.Log($"[VERSION] ResultCode {code} � bypassing lockout");
                        return false;
                    }
                }
            }
            catch { SafetyPatches.TrackError("OnReturnCurrentVersion"); }
            return true;
        }
    }

    [HarmonyPatch(typeof(PhotonNetworkController), "FixedUpdate")]
    [HarmonyPriority(Priority.First)]
    public class PatchAFKKick
    {
        [HarmonyPrefix]
        public static void Prefix(PhotonNetworkController __instance)
        {
            if (!SafetyConfig.AntiAFKKickEnabled) return;
            try
            {
                __instance.disableAFKKick = true;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PhotonNetworkController), "OnApplicationPause")]
    [HarmonyPriority(Priority.First)]
    public class PatchAppPause
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.AntiPauseDisconnectEnabled;
    }

    [HarmonyPatch(typeof(PhotonNetworkController), "OnApplicationFocus")]
    [HarmonyPriority(Priority.First)]
    public class PatchAppFocus
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.AntiPauseDisconnectEnabled;
    }

    [HarmonyPatch(typeof(GorillaTagManager), "ReportTag")]
    [HarmonyPriority(Priority.First)]
    public class PatchReportTag
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!SafetyConfig.PatchSendReport) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(GorillaComputer), "SaveModAccountData")]
    [HarmonyPriority(Priority.First)]
    public class PatchSaveModAccountData
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.BlockModAccountSave;
    }

    [HarmonyPatch(typeof(MonkeAgent), "OnApplicationPause")]
    [HarmonyPriority(Priority.First)]
    public class PatchMonkeAgentOnPause
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchRPCLimits;
    }

    [HarmonyPatch(typeof(MonkeAgent), "OnPlayerEnteredRoom")]
    [HarmonyPriority(Priority.First)]
    public class PatchMonkeAgentPlayerEntered
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchCheckReports;
    }

    [HarmonyPatch(typeof(MonkeAgent), "OnPlayerLeftRoom")]
    [HarmonyPriority(Priority.First)]
    public class PatchMonkeAgentPlayerLeft
    {
        [HarmonyPrefix]
        public static bool Prefix() => !SafetyConfig.PatchCheckReports;
    }

    [HarmonyPatch(typeof(PlayFabAuthenticator), "SetSafety")]
    [HarmonyPriority(Priority.First)]
    public class PatchSetSafety
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool isSafety)
        {
            if (SafetyConfig.KIDBypassEnabled)
            {
                isSafety = false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GorillaPlayerScoreboardLine), "SetReportState")]
    [HarmonyPriority(Priority.First)]
    public class PatchSetReportState
    {
        [HarmonyPrefix]
        public static bool Prefix(bool reportState)
        {
            if (!SafetyConfig.PatchSendReport) return true;
            if (reportState) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(GorillaPlayerScoreboardLine), "NormalizeName")]
    [HarmonyPriority(Priority.First)]
    public class PatchNormalizeName
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool doIt)
        {
            if (SafetyConfig.NameBanBypassEnabled)
            {
                doIt = false;
            }
            return true;
        }
    }
}
