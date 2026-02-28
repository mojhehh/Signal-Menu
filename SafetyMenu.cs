using System;
using UnityEngine;

namespace SignalSafetyMenu
{
    public static class SafetyMenu
    {
        private static bool menuOpen = false;
        private static bool advancedWarningPlayed = false;
        private static Rect windowRect = new Rect(20, 20, 350, 520);
        private static bool lastSecondaryState = false;

        private static int currentPage = 0;
        private const int PAGE_MAIN = 0;
        private const int PAGE_ADVANCED = 1;
        private const int PAGE_IDENTITY = 2;
        private const int PAGE_AUDIO = 3;
        private const int PAGE_CONTROLS = 4;
        private const int PAGE_EXTRA = 5;

        private static Vector2 extraScrollPos = Vector2.zero;

        public static void Draw()
        {
            bool secondaryPressed = false;
            try
            {
                secondaryPressed = ButtonMapper.IsMenuButtonPressed();
            }
            catch { }

            if (secondaryPressed && !lastSecondaryState)
            {
                menuOpen = !menuOpen;
            }
            lastSecondaryState = secondaryPressed;

            if (!menuOpen) return;

            if (Menu3D.IsOpen) return;

            GUI.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
            windowRect = GUILayout.Window(9999, windowRect, DrawWindow, "Signal Safety Menu");
        }

        private static void DrawWindow(int id)
        {
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };
            headerStyle.normal.textColor = new Color(0.3f, 0.8f, 1f);

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14
            };

            GUIStyle toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 14
            };

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12
            };
            labelStyle.normal.textColor = Color.gray;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Main", buttonStyle)) currentPage = PAGE_MAIN;
            if (GUILayout.Button("Advanced", buttonStyle))
            {
                if (currentPage != PAGE_ADVANCED && !advancedWarningPlayed)
                {
                    advancedWarningPlayed = true;
                    AudioManager.Play("warning", AudioManager.AudioCategory.Warning);
                }
                currentPage = PAGE_ADVANCED;
            }
            if (GUILayout.Button("Identity", buttonStyle)) currentPage = PAGE_IDENTITY;
            if (GUILayout.Button("Audio", buttonStyle)) currentPage = PAGE_AUDIO;
            if (GUILayout.Button("Controls", buttonStyle)) currentPage = PAGE_CONTROLS;
            if (GUILayout.Button("Extra", buttonStyle)) currentPage = PAGE_EXTRA;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            switch (currentPage)
            {
                case PAGE_MAIN:
                    DrawMainPage(headerStyle, labelStyle, toggleStyle);
                    break;
                case PAGE_ADVANCED:
                    DrawAdvancedPage(headerStyle, labelStyle, toggleStyle);
                    break;
                case PAGE_IDENTITY:
                    DrawIdentityPage(headerStyle, labelStyle, buttonStyle);
                    break;
                case PAGE_AUDIO:
                    DrawAudioPage(headerStyle, labelStyle, toggleStyle);
                    break;
                case PAGE_CONTROLS:
                    DrawControlsPage(headerStyle, labelStyle, buttonStyle);
                    break;
                case PAGE_EXTRA:
                    DrawExtraPage(headerStyle, labelStyle, toggleStyle, buttonStyle);
                    break;
            }

            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Patches: {Plugin.SuccessfulPatches}/{Plugin.TotalPatches}", labelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(ButtonMapper.GetButtonName(SafetyConfig.MenuOpenButton) + " to close", labelStyle);
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        private static void DrawMainPage(GUIStyle headerStyle, GUIStyle labelStyle, GUIStyle toggleStyle)
        {
            if (UpdateChecker.UpdateAvailable)
            {
                GUI.backgroundColor = new Color(1f, 0.85f, 0f, 0.95f);
                GUIStyle updateStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                updateStyle.normal.textColor = Color.black;
                GUILayout.Box($"UPDATE AVAILABLE: v{UpdateChecker.LatestVersion}  (you have v{UpdateChecker.CurrentVersion})", updateStyle);
                GUI.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
                GUILayout.Space(5);
            }

            GUILayout.Label("═══ PROTECTION STATUS ═══", headerStyle);
            GUILayout.Space(10);

            GUI.color = Color.green;
            GUILayout.Label("✓ Core Protection: ALWAYS ON", labelStyle);
            GUI.color = Color.white;

            GUILayout.Space(10);

            GUILayout.Label("Quick Toggles:", labelStyle);

            bool antiReport = GUILayout.Toggle(SafetyConfig.AntiReportEnabled, " Anti-Report (Block nearby players)", toggleStyle);
            if (antiReport != SafetyConfig.AntiReportEnabled)
            {
                SafetyConfig.AntiReportEnabled = antiReport;
                SafetyConfig.Save();
                AudioManager.Play(antiReport ? "safety_enabled" : "safety_disabled", AudioManager.AudioCategory.Toggle);
            }

            bool telemetry = GUILayout.Toggle(SafetyConfig.TelemetryBlockEnabled, " Block Telemetry", toggleStyle);
            if (telemetry != SafetyConfig.TelemetryBlockEnabled)
            {
                SafetyConfig.TelemetryBlockEnabled = telemetry;
                SafetyConfig.Save();
                AudioManager.Play(telemetry ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle);
            }

            bool playfab = GUILayout.Toggle(SafetyConfig.PlayFabBlockEnabled, " Block PlayFab Reports", toggleStyle);
            if (playfab != SafetyConfig.PlayFabBlockEnabled)
            {
                SafetyConfig.PlayFabBlockEnabled = playfab;
                SafetyConfig.Save();
                AudioManager.Play(playfab ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle);
            }

            GUILayout.Space(20);

            GUILayout.Label("═══ INFO ═══", headerStyle);
            GUILayout.Label("Signal Safety Menu provides comprehensive", labelStyle);
            GUILayout.Label("anti-ban protection with 157+ patches.", labelStyle);
            GUILayout.Label("", labelStyle);
            GUILayout.Label("Use Advanced page for individual toggles.", labelStyle);
            GUILayout.Label("Use Identity page to change your name.", labelStyle);
            GUILayout.Label("Use Audio page to configure sounds.", labelStyle);

            GUILayout.Space(15);

            GUILayout.Label("═══ LINKS ═══", headerStyle);
            GUILayout.Space(5);
            GUI.color = new Color(0.45f, 0.55f, 1f);
            GUILayout.Label("Discord: discord.gg/rYSRrr8Bhy", labelStyle);
            GUI.color = Color.white;
            if (GUILayout.Button("Copy Discord Link"))
            {
                GUIUtility.systemCopyBuffer = "https://discord.gg/rYSRrr8Bhy";
            }
        }

        private static void DrawAdvancedPage(GUIStyle headerStyle, GUIStyle labelStyle, GUIStyle toggleStyle)
        {
            GUILayout.Label("═══ ADVANCED SETTINGS ═══", headerStyle);
            GUILayout.Space(5);

            GUI.color = Color.yellow;
            GUILayout.Label("⚠ Disabling protections increases ban risk!", labelStyle);
            GUI.color = Color.white;

            GUILayout.Space(10);

            bool deviceSpoof = GUILayout.Toggle(SafetyConfig.DeviceSpoofEnabled, " Device Spoofing", toggleStyle);
            if (deviceSpoof != SafetyConfig.DeviceSpoofEnabled)
            {
                SafetyConfig.DeviceSpoofEnabled = deviceSpoof;
                SafetyConfig.Save();
                AudioManager.Play(deviceSpoof ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle);
            }
            GUILayout.Label("    Spoofs HWID, device name, model", labelStyle);

            GUILayout.Space(5);

            bool networkBlock = GUILayout.Toggle(SafetyConfig.NetworkEventBlockEnabled, " Network Event Blocking", toggleStyle);
            if (networkBlock != SafetyConfig.NetworkEventBlockEnabled)
            {
                SafetyConfig.NetworkEventBlockEnabled = networkBlock;
                SafetyConfig.Save();
                AudioManager.Play(networkBlock ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle);
            }
            GUILayout.Label("    Blocks report/ban network events", labelStyle);

            GUILayout.Space(5);

            bool rpcBypass = GUILayout.Toggle(SafetyConfig.RPCLimitBypassEnabled, " RPC Limit Bypass", toggleStyle);
            if (rpcBypass != SafetyConfig.RPCLimitBypassEnabled)
            {
                SafetyConfig.RPCLimitBypassEnabled = rpcBypass;
                SafetyConfig.Save();
                AudioManager.Play(rpcBypass ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle);
            }
            GUILayout.Label("    Bypasses anti-cheat RPC tracking", labelStyle);

            GUILayout.Space(5);

            bool graceBypass = GUILayout.Toggle(SafetyConfig.GraceBypassEnabled, " Grace Period Bypass", toggleStyle);
            if (graceBypass != SafetyConfig.GraceBypassEnabled)
            {
                SafetyConfig.GraceBypassEnabled = graceBypass;
                SafetyConfig.Save();
                AudioManager.Play(graceBypass ? "safety_enabled" : "safety_disabled", AudioManager.AudioCategory.Toggle);
            }
            GUILayout.Label("    Skips anti-cheat grace period", labelStyle);

            GUILayout.Space(5);

            bool kidBypass = GUILayout.Toggle(SafetyConfig.KIDBypassEnabled, " KID Bypass", toggleStyle);
            if (kidBypass != SafetyConfig.KIDBypassEnabled)
            {
                SafetyConfig.KIDBypassEnabled = kidBypass;
                SafetyConfig.Save();
                AudioManager.Play(kidBypass ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle);
            }
            GUILayout.Label("    Bypasses KID age-gate restrictions", labelStyle);

            GUILayout.Space(5);

            bool nameBanBypass = GUILayout.Toggle(SafetyConfig.NameBanBypassEnabled, " Name Ban Bypass", toggleStyle);
            if (nameBanBypass != SafetyConfig.NameBanBypassEnabled)
            {
                SafetyConfig.NameBanBypassEnabled = nameBanBypass;
                SafetyConfig.Save();
                AudioManager.Play(nameBanBypass ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle);
            }
            GUILayout.Label("    Bypasses name/room/troop ban lists", labelStyle);

            GUILayout.Space(5);

            bool coreProt = GUILayout.Toggle(SafetyConfig.CoreProtectionEnabled, " Core Property Filter", toggleStyle);
            if (coreProt != SafetyConfig.CoreProtectionEnabled)
            {
                SafetyConfig.CoreProtectionEnabled = coreProt;
                SafetyConfig.Save();
                AudioManager.Play(coreProt ? "safety_enabled" : "safety_disabled", AudioManager.AudioCategory.Toggle);
            }
            GUILayout.Label("    Strip mod-checker custom properties", labelStyle);

            GUILayout.Space(5);

            bool errorLog = GUILayout.Toggle(SafetyConfig.ErrorLoggingEnabled, " Error Logging to Disk", toggleStyle);
            if (errorLog != SafetyConfig.ErrorLoggingEnabled)
            {
                SafetyConfig.ErrorLoggingEnabled = errorLog;
                SafetyConfig.Save();
            }
            GUI.color = Color.yellow;
            GUILayout.Label("    Leaves traces! Only for debugging", labelStyle);
            GUI.color = Color.white;

            GUILayout.Space(20);

            if (GUILayout.Button("Reset All to Defaults"))
            {
                SafetyConfig.ResetToDefaults();
                AudioManager.Play("done", AudioManager.AudioCategory.Toggle);
            }
        }

        private static void DrawIdentityPage(GUIStyle headerStyle, GUIStyle labelStyle, GUIStyle buttonStyle)
        {
            GUILayout.Label("═══ IDENTITY CHANGE ═══", headerStyle);
            GUILayout.Space(10);

            GUILayout.Label("Change your in-game identity:", labelStyle);
            GUILayout.Space(5);

            GUIStyle toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = 14 };
            bool identityEnabled = GUILayout.Toggle(SafetyConfig.IdentityChangeEnabled, " Enable Identity Change", toggleStyle);
            if (identityEnabled != SafetyConfig.IdentityChangeEnabled)
            {
                SafetyConfig.IdentityChangeEnabled = identityEnabled;
                SafetyConfig.Save();
                if (identityEnabled)
                {
                    AudioManager.Play("done", AudioManager.AudioCategory.Toggle);
                    IdentityChanger.ApplyRandomName();
                }
            }

            GUILayout.Space(10);

            if (SafetyConfig.IdentityChangeEnabled)
            {
                GUILayout.Label("Custom Name (leave blank for random):", labelStyle);
                SafetyConfig.CustomName = GUILayout.TextField(SafetyConfig.CustomName ?? "", 20);

                GUILayout.Space(10);

                if (GUILayout.Button("Apply Name Change", buttonStyle))
                {
                    if (string.IsNullOrEmpty(SafetyConfig.CustomName))
                    {
                        IdentityChanger.ApplyRandomName();
                    }
                    else
                    {
                        IdentityChanger.ApplyCustomName(SafetyConfig.CustomName);
                    }
                    SafetyConfig.Save();
                }

                if (GUILayout.Button("Generate Random Name", buttonStyle))
                {
                    SafetyConfig.CustomName = "";
                    IdentityChanger.ApplyRandomName();
                }

                GUILayout.Space(10);

                GUI.color = Color.cyan;
                GUILayout.Label($"Current: {IdentityChanger.GetCurrentName()}", labelStyle);
                GUI.color = Color.white;
            }

            GUILayout.Space(20);

            GUILayout.Label("═══ ANTI-REPORT ═══", headerStyle);
            GUILayout.Space(5);

            bool antiReport = GUILayout.Toggle(SafetyConfig.AntiReportEnabled, " Anti-Report Active", toggleStyle);
            if (antiReport != SafetyConfig.AntiReportEnabled)
            {
                SafetyConfig.AntiReportEnabled = antiReport;
                SafetyConfig.Save();
                if (antiReport)
                {
                    AntiReport.EnableSmartAntiReport();
                    AntiReport.EnableAntiOculusReport();
                }
                else
                {
                    AntiReport.DisableSmartAntiReport();
                    AntiReport.DisableAntiOculusReport();
                }
            }

            if (SafetyConfig.AntiReportEnabled)
            {
                GUILayout.Space(5);

                bool smart = GUILayout.Toggle(AntiReport.SmartMode, " Smart Mode (less false positives)", toggleStyle);
                if (smart != AntiReport.SmartMode) { AntiReport.SmartMode = smart; SafetyConfig.Save(); }

                bool vis = GUILayout.Toggle(AntiReport.VisualizerEnabled, " Show Detection Zones", toggleStyle);
                if (vis != AntiReport.VisualizerEnabled) { AntiReport.VisualizerEnabled = vis; SafetyConfig.Save(); }

                bool mute = GUILayout.Toggle(AntiReport.AntiMute, " Detect Mute Button Too", toggleStyle);
                if (mute != AntiReport.AntiMute) { AntiReport.AntiMute = mute; SafetyConfig.Save(); }

                GUILayout.Space(5);

                if (GUILayout.Button($"Detection Range: {AntiReport.RangeName}"))
                {
                    AntiReport.CycleRange();
                    SafetyConfig.Save();
                }

                GUILayout.Space(3);

                string[] modeNames = { "Disconnect", "Reconnect", "Notify Only" };
                string currentMode = modeNames[Mathf.Clamp(SafetyConfig.AntiReportMode, 0, modeNames.Length - 1)];
                if (GUILayout.Button($"Mode: {currentMode}"))
                {
                    SafetyConfig.AntiReportMode = (SafetyConfig.AntiReportMode + 1) % modeNames.Length;
                    SafetyConfig.Save();
                }

                GUILayout.Space(5);

                if (AntiReport.NearbyCount > 0)
                {
                    GUI.color = Color.red;
                    GUILayout.Label($"WARNING: {AntiReport.LastReporter} near report!", labelStyle);
                    GUI.color = Color.white;
                }
                else
                {
                    GUI.color = Color.green;
                    GUILayout.Label("No threats detected", labelStyle);
                    GUI.color = Color.white;
                }
                GUILayout.Label($"Nearby players: {AntiReport.NearbyCount}", labelStyle);
            }
            else
            {
                GUILayout.Label("Enable to detect report attempts.", labelStyle);
            }
        }

        private static void DrawAudioPage(GUIStyle headerStyle, GUIStyle labelStyle, GUIStyle toggleStyle)
        {
            GUILayout.Label("═══ AUDIO SETTINGS ═══", headerStyle);
            GUILayout.Space(10);

            bool audioEnabled = GUILayout.Toggle(SafetyConfig.AudioEnabled, " Enable All Audio", toggleStyle);
            if (audioEnabled != SafetyConfig.AudioEnabled)
            {
                SafetyConfig.AudioEnabled = audioEnabled;
                SafetyConfig.Save();
            }

            GUILayout.Space(10);

            if (SafetyConfig.AudioEnabled)
            {
                GUILayout.Label("Audio Categories:", labelStyle);
                GUILayout.Space(5);

                bool protectionAudio = GUILayout.Toggle(SafetyConfig.PlayProtectionAudio, " Protection Sounds", toggleStyle);
                if (protectionAudio != SafetyConfig.PlayProtectionAudio)
                {
                    SafetyConfig.PlayProtectionAudio = protectionAudio;
                    SafetyConfig.Save();
                }
                GUILayout.Label("    Startup, status announcements", labelStyle);

                GUILayout.Space(5);

                bool warningAudio = GUILayout.Toggle(SafetyConfig.PlayWarningAudio, " Warning Sounds", toggleStyle);
                if (warningAudio != SafetyConfig.PlayWarningAudio)
                {
                    SafetyConfig.PlayWarningAudio = warningAudio;
                    SafetyConfig.Save();
                }
                GUILayout.Label("    Alerts when protections disabled", labelStyle);

                GUILayout.Space(5);

                bool banAudio = GUILayout.Toggle(SafetyConfig.PlayBanAudio, " Ban Detection Sounds", toggleStyle);
                if (banAudio != SafetyConfig.PlayBanAudio)
                {
                    SafetyConfig.PlayBanAudio = banAudio;
                    SafetyConfig.Save();
                }
                GUILayout.Label("    Alert when ban is detected", labelStyle);

                GUILayout.Space(5);

                GUI.color = Color.gray;
                GUILayout.Label("Toggle sounds have been removed for stealth.", labelStyle);
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = Color.gray;
                GUILayout.Label("All audio is currently disabled.", labelStyle);
                GUILayout.Label("Enable audio above to see options.", labelStyle);
                GUI.color = Color.white;
            }

            GUILayout.Space(20);

            GUILayout.Label("Sound files location:", labelStyle);
            GUILayout.Label("[Gorilla Tag]/nexussounds/", labelStyle);
        }

        private static void DrawControlsPage(GUIStyle headerStyle, GUIStyle labelStyle, GUIStyle buttonStyle)
        {
            GUILayout.Label("═══ MENU CONTROLS ═══", headerStyle);
            GUILayout.Space(10);

            GUILayout.Label("Menu Open Button:", labelStyle);
            GUILayout.Space(5);

            string currentButtonName = ButtonMapper.GetButtonName(SafetyConfig.MenuOpenButton);
            GUILayout.Label($"Current: {currentButtonName}", labelStyle);
            GUILayout.Space(10);

            GUILayout.Label("Choose button mapping:", labelStyle);
            GUILayout.Space(5);

            if (GUILayout.Button("Y (Left Controller)", buttonStyle))
            {
                SafetyConfig.MenuOpenButton = ButtonMapper.MenuButton.Y_Left;
                SafetyConfig.Save();
                AudioManager.Play("safety_enabled", AudioManager.AudioCategory.Protection);
            }

            if (GUILayout.Button("B (Right Controller)", buttonStyle))
            {
                SafetyConfig.MenuOpenButton = ButtonMapper.MenuButton.B_Right;
                SafetyConfig.Save();
                AudioManager.Play("safety_enabled", AudioManager.AudioCategory.Protection);
            }

            if (GUILayout.Button("X (Left Controller)", buttonStyle))
            {
                SafetyConfig.MenuOpenButton = ButtonMapper.MenuButton.X_Left;
                SafetyConfig.Save();
                AudioManager.Play("safety_enabled", AudioManager.AudioCategory.Protection);
            }

            if (GUILayout.Button("A (Right Controller)", buttonStyle))
            {
                SafetyConfig.MenuOpenButton = ButtonMapper.MenuButton.A_Right;
                SafetyConfig.Save();
                AudioManager.Play("safety_enabled", AudioManager.AudioCategory.Protection);
            }

            if (GUILayout.Button("Grip Trigger (Both)", buttonStyle))
            {
                SafetyConfig.MenuOpenButton = ButtonMapper.MenuButton.PrimaryTrigger;
                SafetyConfig.Save();
                AudioManager.Play("safety_enabled", AudioManager.AudioCategory.Protection);
            }

            if (GUILayout.Button("Index Trigger (Both)", buttonStyle))
            {
                SafetyConfig.MenuOpenButton = ButtonMapper.MenuButton.SecondaryTrigger;
                SafetyConfig.Save();
                AudioManager.Play("safety_enabled", AudioManager.AudioCategory.Protection);
            }

            GUILayout.Space(20);

            GUI.color = Color.gray;
            GUILayout.Label("Note: Changes take effect immediately.", labelStyle);
            GUI.color = Color.white;
        }

        private static void DrawExtraPage(GUIStyle headerStyle, GUIStyle labelStyle, GUIStyle toggleStyle, GUIStyle buttonStyle)
        {
            GUILayout.Label("═══ EXTRA PROTECTION ═══", headerStyle);
            GUILayout.Space(5);

            extraScrollPos = GUILayout.BeginScrollView(extraScrollPos, GUILayout.Height(420));

            GUILayout.Label("Detection:", labelStyle);

            bool modDetect = GUILayout.Toggle(SafetyConfig.ModeratorDetectorEnabled, " Moderator Detection", toggleStyle);
            if (modDetect != SafetyConfig.ModeratorDetectorEnabled)
            {
                SafetyConfig.ModeratorDetectorEnabled = modDetect;
                SafetyConfig.Save();
                AudioManager.Play(modDetect ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle);
            }
            GUILayout.Label("    Auto-leave if GT staff detected", labelStyle);

            GUILayout.Space(3);

            bool antiCreator = GUILayout.Toggle(SafetyConfig.AntiContentCreatorEnabled, " Anti-Content Creator", toggleStyle);
            if (antiCreator != SafetyConfig.AntiContentCreatorEnabled)
            {
                SafetyConfig.AntiContentCreatorEnabled = antiCreator;
                SafetyConfig.Save();
                AudioManager.Play(antiCreator ? "safety_enabled" : "safety_disabled", AudioManager.AudioCategory.Toggle);
            }
            GUILayout.Label("    Auto-leave if creator joins lobby", labelStyle);

            GUILayout.Space(3);

            bool cosmeticNotif = GUILayout.Toggle(SafetyConfig.CosmeticNotificationsEnabled, " Cosmetic Notifications", toggleStyle);
            if (cosmeticNotif != SafetyConfig.CosmeticNotificationsEnabled)
            {
                SafetyConfig.CosmeticNotificationsEnabled = cosmeticNotif;
                SafetyConfig.Save();
            }
            GUILayout.Label("    Alert when rare cosmetics detected", labelStyle);

            if (!string.IsNullOrEmpty(Patches.CosmeticNotifier.LastNotification))
            {
                GUI.color = Color.cyan;
                GUILayout.Label($"    Last: {Patches.CosmeticNotifier.LastNotification}", labelStyle);
                GUI.color = Color.white;
            }

            GUILayout.Space(8);

            GUILayout.Label("Voice:", labelStyle);

            bool automod = GUILayout.Toggle(SafetyConfig.AutomodBypassEnabled, " Automod Bypass", toggleStyle);
            if (automod != SafetyConfig.AutomodBypassEnabled)
            {
                SafetyConfig.AutomodBypassEnabled = automod;
                SafetyConfig.Save();
                AudioManager.Play(automod ? "safety_enabled" : "safety_disabled", AudioManager.AudioCategory.Toggle);
            }
            GUILayout.Label("    Prevent auto-mute + restart mic on silence", labelStyle);

            GUILayout.Space(8);

            GUILayout.Label("Stealth:", labelStyle);

            bool antiPred = GUILayout.Toggle(SafetyConfig.AntiPredictionsEnabled, " Anti-Predictions", toggleStyle);
            if (antiPred != SafetyConfig.AntiPredictionsEnabled)
            {
                SafetyConfig.AntiPredictionsEnabled = antiPred;
                SafetyConfig.Save();
                if (!antiPred) Patches.AntiPredictions.Reset();
            }
            GUILayout.Label("    Smooth hand jitter vs prediction algos", labelStyle);

            GUILayout.Space(3);

            bool antiLurker = GUILayout.Toggle(SafetyConfig.AntiLurkerEnabled, " Anti-Lurker", toggleStyle);
            if (antiLurker != SafetyConfig.AntiLurkerEnabled)
            {
                SafetyConfig.AntiLurkerEnabled = antiLurker;
                SafetyConfig.Save();
            }
            GUILayout.Label("    Deflect lurker ghost targeting you", labelStyle);

            GUILayout.Space(8);

            GUILayout.Label("Fake Behaviors:", labelStyle);

            bool fakeOculus = GUILayout.Toggle(SafetyConfig.FakeOculusMenuEnabled, " Fake Oculus Menu", toggleStyle);
            if (fakeOculus != SafetyConfig.FakeOculusMenuEnabled)
            {
                SafetyConfig.FakeOculusMenuEnabled = fakeOculus;
                SafetyConfig.Save();
            }
            GUILayout.Label("    Freeze input — looks AFK in menu", labelStyle);

            GUILayout.Space(3);

            bool fakeBroken = GUILayout.Toggle(SafetyConfig.FakeBrokenControllerEnabled, " Fake Broken Controller", toggleStyle);
            if (fakeBroken != SafetyConfig.FakeBrokenControllerEnabled)
            {
                SafetyConfig.FakeBrokenControllerEnabled = fakeBroken;
                SafetyConfig.Save();
            }
            GUILayout.Label("    Kill left hand — looks like dead controller", labelStyle);

            GUILayout.Space(3);

            bool fakeReport = GUILayout.Toggle(SafetyConfig.FakeReportMenuEnabled, " Fake Report Menu", toggleStyle);
            if (fakeReport != SafetyConfig.FakeReportMenuEnabled)
            {
                SafetyConfig.FakeReportMenuEnabled = fakeReport;
                SafetyConfig.Save();
            }
            GUILayout.Label("    Look like you're in report UI", labelStyle);

            GUILayout.Space(3);

            bool fakeValve = GUILayout.Toggle(SafetyConfig.FakeValveTrackingEnabled, " Fake Valve Tracking", toggleStyle);
            if (fakeValve != SafetyConfig.FakeValveTrackingEnabled)
            {
                SafetyConfig.FakeValveTrackingEnabled = fakeValve;
                SafetyConfig.Save();
            }
            GUILayout.Label("    Simulate SteamVR tracking reset", labelStyle);

            GUILayout.Space(8);

            GUILayout.Label("Anti-Crash:", labelStyle);

            bool antiCrash = GUILayout.Toggle(SafetyConfig.AntiCrashEnabled, " Anti-Crash Protection", toggleStyle);
            if (antiCrash != SafetyConfig.AntiCrashEnabled)
            {
                SafetyConfig.AntiCrashEnabled = antiCrash;
                SafetyConfig.Save();
                AudioManager.Play(antiCrash ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle);
            }
            GUILayout.Label("    Block crash exploits (velocity, RPC, Luau)", labelStyle);

            GUILayout.Space(3);

            bool antiKick = GUILayout.Toggle(SafetyConfig.AntiKickEnabled, " Anti-Kick", toggleStyle);
            if (antiKick != SafetyConfig.AntiKickEnabled)
            {
                SafetyConfig.AntiKickEnabled = antiKick;
                SafetyConfig.Save();
                if (antiKick)
                    Patches.AntiKickHelper.Enable();
                else
                    Patches.AntiKickHelper.Disable();
                AudioManager.Play(antiKick ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle);
            }
            GUILayout.Label("    Only send essential network data", labelStyle);

            GUILayout.Space(3);

            bool showAC = GUILayout.Toggle(SafetyConfig.ShowACReportsEnabled, " Show AC Reports [Self]", toggleStyle);
            if (showAC != SafetyConfig.ShowACReportsEnabled)
            {
                SafetyConfig.ShowACReportsEnabled = showAC;
                SafetyConfig.Save();
            }
            GUILayout.Label("    Show reason when AC targets you", labelStyle);

            if (Patches.ACReportNotifier.HasActiveNotification)
            {
                GUI.color = Color.red;
                GUILayout.Label($"    !! Last AC Report: {Patches.ACReportNotifier.LastReport}", labelStyle);
                GUI.color = Color.white;
            }

            GUILayout.Space(8);

            GUILayout.Label("Gameplay:", labelStyle);

            bool autoGC = GUILayout.Toggle(SafetyConfig.AutoGCEnabled, " Auto GC Collect", toggleStyle);
            if (autoGC != SafetyConfig.AutoGCEnabled)
            {
                SafetyConfig.AutoGCEnabled = autoGC;
                SafetyConfig.Save();
            }
            GUILayout.Label("    Reduce stutters — GC every 60s", labelStyle);

            GUILayout.Space(3);

            bool supportSpoof = GUILayout.Toggle(SafetyConfig.SupportPageSpoofEnabled, " Spoof Support Page", toggleStyle);
            if (supportSpoof != SafetyConfig.SupportPageSpoofEnabled)
            {
                SafetyConfig.SupportPageSpoofEnabled = supportSpoof;
                SafetyConfig.Save();
            }
            GUILayout.Label("    Show QUEST instead of STEAM", labelStyle);

            GUILayout.Space(8);

            GUILayout.Label("Ranked:", labelStyle);

            bool ranked = GUILayout.Toggle(SafetyConfig.RankedSpoofEnabled, " Ranked Spoof", toggleStyle);
            if (ranked != SafetyConfig.RankedSpoofEnabled)
            {
                SafetyConfig.RankedSpoofEnabled = ranked;
                SafetyConfig.Save();
            }

            if (SafetyConfig.RankedSpoofEnabled)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("-", GUILayout.Width(30))) Patches.RankedSpoofer.CycleElo(false);
                GUILayout.Label($"  ELO: {Patches.RankedSpoofer.TargetElo}  ");
                if (GUILayout.Button("+", GUILayout.Width(30))) Patches.RankedSpoofer.CycleElo(true);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("<", GUILayout.Width(30))) Patches.RankedSpoofer.CycleBadge(false);
                GUILayout.Label($"  Badge: {Patches.RankedSpoofer.GetBadgeName()}  ");
                if (GUILayout.Button(">", GUILayout.Width(30))) Patches.RankedSpoofer.CycleBadge(true);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);

            GUILayout.Label("Identity Extras:", labelStyle);

            bool idOnDisc = GUILayout.Toggle(SafetyConfig.ChangeIdentityOnDisconnect, " Change Name on Disconnect", toggleStyle);
            if (idOnDisc != SafetyConfig.ChangeIdentityOnDisconnect)
            {
                SafetyConfig.ChangeIdentityOnDisconnect = idOnDisc;
                SafetyConfig.Save();
            }

            bool colorChange = GUILayout.Toggle(SafetyConfig.ColorChangeEnabled, " Randomize Color with Name", toggleStyle);
            if (colorChange != SafetyConfig.ColorChangeEnabled)
            {
                SafetyConfig.ColorChangeEnabled = colorChange;
                SafetyConfig.Save();
            }

            GUILayout.Space(8);

            GUILayout.Label("Actions:", labelStyle);

            if (GUILayout.Button("Flush Buffered RPCs", buttonStyle))
            {
                if (Patches.RPCFlusher.Flush())
                    AudioManager.Play("done", AudioManager.AudioCategory.Toggle);
            }

            if (GUILayout.Button("Fix Lobby Issues (Clear RPCs + Properties)", buttonStyle))
            {
                if (Patches.LobbyFixer.Fix())
                    AudioManager.Play("done", AudioManager.AudioCategory.Toggle);
            }

            if (GUILayout.Button("Leave & Rejoin Fresh Lobby", buttonStyle))
            {
                Patches.LobbyFixer.Rejoin();
                AudioManager.Play("done", AudioManager.AudioCategory.Toggle);
            }

            GUILayout.Space(5);

            GUI.backgroundColor = new Color(0.8f, 0.2f, 0.2f, 0.9f);
            if (GUILayout.Button("RESTART GORILLA TAG", buttonStyle))
            {
                Patches.GameRestarter.Restart();
            }
            GUI.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);

            GUILayout.Space(8);

            GUILayout.Label("FPS Spoof:", labelStyle);

            bool fpsSpoof = GUILayout.Toggle(SafetyConfig.FPSSpoofEnabled, " Spoof FPS", toggleStyle);
            if (fpsSpoof != SafetyConfig.FPSSpoofEnabled)
            {
                SafetyConfig.FPSSpoofEnabled = fpsSpoof;
                SafetyConfig.Save();
            }

            if (SafetyConfig.FPSSpoofEnabled)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("-10", GUILayout.Width(45)))
                {
                    SafetyConfig.SpoofedFPS = Mathf.Clamp(SafetyConfig.SpoofedFPS - 10, 30, 144);
                    SafetyConfig.Save();
                }
                GUILayout.Label($"  FPS: {SafetyConfig.SpoofedFPS}  ");
                if (GUILayout.Button("+10", GUILayout.Width(45)))
                {
                    SafetyConfig.SpoofedFPS = Mathf.Clamp(SafetyConfig.SpoofedFPS + 10, 30, 144);
                    SafetyConfig.Save();
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("    Fake FPS sent to anti-cheat", labelStyle);
            }

            GUILayout.Space(8);

            GUILayout.Label("Bypass:", labelStyle);

            bool tosBypass = GUILayout.Toggle(SafetyConfig.TOSBypassEnabled, " TOS/Age Bypass", toggleStyle);
            if (tosBypass != SafetyConfig.TOSBypassEnabled)
            {
                SafetyConfig.TOSBypassEnabled = tosBypass;
                SafetyConfig.Save();
            }
            GUILayout.Label("    Auto-set age to 21, skip legal screens", labelStyle);

            GUILayout.Space(3);

            bool antiNameBan = GUILayout.Toggle(SafetyConfig.AntiNameBanEnabled, " Anti-Name Ban (Proactive)", toggleStyle);
            if (antiNameBan != SafetyConfig.AntiNameBanEnabled)
            {
                SafetyConfig.AntiNameBanEnabled = antiNameBan;
                SafetyConfig.Save();
            }
            GUILayout.Label("    Auto-reset name if on ban list", labelStyle);

            GUILayout.EndScrollView();
        }
    }
}
