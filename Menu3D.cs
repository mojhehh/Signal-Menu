using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SignalSafetyMenu
{
    public class Menu3D : MonoBehaviour
    {
        public static Menu3D Instance;
        public static bool IsOpen;

        private GameObject _menuRoot;
        private GameObject _pointer;

        private int _page;
        private int _pageCount;
        private int _categoryOfPage;

        private bool _lastBtn;

        private static TMP_FontAsset _font;

        private const int PageSize = 6;
        private const int BtnOffset = 2;
        private const float BtnDist = 0.1f;

        private static readonly string[] CatNames = { "Main", "Advanced", "Identity", "Audio", "Extra" };

        private struct Entry
        {
            public string Label;
            public Action OnPress;
            public Func<bool> IsOn;
            public bool IsToggle;
            public int Category;
        }

        void Awake()
        {
            Instance = this;
            IsOpen = false;
            FindFont();
        }

        static void FindFont()
        {
            if (_font != null) return;
            try
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (all != null)
                {
                    foreach (var f in all)
                    {
                        if (f != null)
                        {
                            _font = f;
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        void Update()
        {
            bool btn = false;
            try
            {
                btn = ControllerInputPoller.instance != null &&
                    ControllerInputPoller.instance.leftControllerSecondaryButton;
            }
            catch { }

            if (btn && !_lastBtn)
            {
                if (IsOpen) Close();
                else Open();
            }
            _lastBtn = btn;

            if (IsOpen && _menuRoot != null)
                Recenter();

            ThemeManager.TickRainbow();
        }

        private void Open()
        {
            Build();
            MakePointer();
            IsOpen = true;
        }

        private void Close()
        {
            if (_menuRoot != null) Destroy(_menuRoot);
            if (_pointer != null) Destroy(_pointer);
            _menuRoot = null;
            _pointer = null;
            IsOpen = false;
        }

        private void Recenter()
        {
            Transform hand = null;
            try { hand = GorillaTagger.Instance.rightHandTransform; } catch { }
            if (hand == null || _menuRoot == null) return;

            float s = 1f;
            try { s = GorillaLocomotion.GTPlayer.Instance.scale; } catch { }

            _menuRoot.transform.position = hand.position;
            Vector3 e = hand.rotation.eulerAngles;
            e += new Vector3(0f, 0f, 180f);
            _menuRoot.transform.rotation = Quaternion.Euler(e);
            _menuRoot.transform.localScale = new Vector3(0.1f, 0.3f, 0.3825f) * s;
        }

        private void Build()
        {
            if (_menuRoot != null) Destroy(_menuRoot);

            var theme = ThemeManager.CurrentTheme;
            var entries = AllEntries();
            _pageCount = Mathf.Max(1, Mathf.CeilToInt((float)entries.Count / PageSize));
            if (_page >= _pageCount) _page = 0;

            int start = _page * PageSize;
            int count = Mathf.Min(PageSize, entries.Count - start);
            _categoryOfPage = count > 0 ? entries[start].Category : 0;

            _menuRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(_menuRoot.GetComponent<BoxCollider>());
            Destroy(_menuRoot.GetComponent<Renderer>());
            _menuRoot.transform.localScale = new Vector3(0.1f, 0.3f, 0.3825f);

            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(bg.GetComponent<BoxCollider>());
            bg.transform.SetParent(_menuRoot.transform, false);
            bg.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            bg.transform.localRotation = Quaternion.identity;
            bg.transform.localScale = new Vector3(0.1f, 1f, 1f);
            ApplyColor(bg, theme.PanelColor);

            var canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(_menuRoot.transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 2500f;
            canvasGO.AddComponent<GraphicRaycaster>();

            string catLabel = CatNames[Mathf.Clamp(_categoryOfPage, 0, CatNames.Length - 1)];
            string titleStr = "Signal / " + catLabel;
            MakeText(canvasGO.transform, titleStr, new Vector3(0.06f, 0f, 0.165f), new Vector2(0.28f, 0.05f), theme.AccentColor);

            string pageStr = "< " + (_page + 1) + "/" + _pageCount + " >";
            MakeText(canvasGO.transform, pageStr, new Vector3(0.06f, 0f, 0.135f), new Vector2(0.28f, 0.02f), theme.TextDim);

            MakeBtn(_menuRoot.transform, canvasGO.transform, 0, "<", theme.ButtonIdle, theme.TextPrimary, () =>
            {
                _page--;
                if (_page < 0) _page = _pageCount - 1;
                RebuildMenu();
            });
            MakeBtn(_menuRoot.transform, canvasGO.transform, 1, ">", theme.ButtonIdle, theme.TextPrimary, () =>
            {
                _page++;
                if (_page >= _pageCount) _page = 0;
                RebuildMenu();
            });

            for (int i = 0; i < count; i++)
            {
                var entry = entries[start + i];
                int slot = i + BtnOffset;

                bool active = entry.IsToggle && entry.IsOn != null && entry.IsOn();
                Color btnCol = active ? theme.ButtonActive : theme.ButtonIdle;
                Color txtCol = active ? theme.TextPrimary : theme.TextDim;

                string label = entry.Label;
                if (entry.IsToggle && entry.IsOn != null)
                    label = (active ? "[ON] " : "[OFF] ") + label;

                var capturedEntry = entry;
                MakeBtn(_menuRoot.transform, canvasGO.transform, slot, label, btnCol, txtCol, () =>
                {
                    try { capturedEntry.OnPress?.Invoke(); } catch { }
                    RebuildMenu();
                });
            }
        }

        private void MakeBtn(Transform root, Transform canvasT, int slot, string label, Color btnColor, Color txtColor, Action onPress)
        {
            float offset = BtnDist * slot;

            var btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            btn.GetComponent<BoxCollider>().isTrigger = true;
            btn.transform.SetParent(root, false);
            btn.transform.localRotation = Quaternion.identity;
            btn.transform.localScale = new Vector3(0.09f, 1.3f, BtnDist * 0.8f);
            btn.transform.localPosition = new Vector3(0.56f, 0f, 0.28f - offset);
            ApplyColor(btn, btnColor);

            var reactor = btn.AddComponent<TouchReactor>();
            reactor.Init(onPress, false);
            reactor.SetBaseColor(btnColor);

            float textZ = 0.111f - offset / 2.6f;
            MakeText(canvasT, label, new Vector3(0.064f, 0f, textZ), new Vector2(0.2f, 0.03f), txtColor);
        }

        private void MakeText(Transform parent, string text, Vector3 localPos, Vector2 size, Color color)
        {
            var go = new GameObject("T");
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 1;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0;
            tmp.color = color;
            tmp.richText = true;

            if (_font != null)
                tmp.font = _font;

            var rt = go.GetComponent<RectTransform>();
            rt.localPosition = localPos;
            rt.sizeDelta = size;
            rt.rotation = Quaternion.Euler(180f, 90f, 90f);
        }

        private void MakePointer()
        {
            if (_pointer != null) Destroy(_pointer);

            Transform hand = null;
            try { hand = GorillaTagger.Instance.leftHandTransform; } catch { }

            _pointer = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            if (hand != null)
                _pointer.transform.SetParent(hand, false);

            _pointer.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            _pointer.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            var rb = _pointer.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var sc = _pointer.GetComponent<SphereCollider>();
            sc.isTrigger = true;
            TouchReactor.Probe = sc;

            var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(vis.GetComponent<SphereCollider>());
            vis.transform.SetParent(_pointer.transform, false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localScale = Vector3.one;
            ApplyColor(vis, ThemeManager.CurrentTheme.PointerColor);
        }

        public void RebuildMenu()
        {
            if (!IsOpen) return;
            Build();
        }

        private static void ApplyColor(GameObject obj, Color c)
        {
            var r = obj.GetComponent<Renderer>();
            if (r == null) return;

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Standard");

            if (sh != null)
            {
                r.material = new Material(sh);
                r.material.color = c;
            }
            else
            {
                r.material.color = c;
            }
        }

        private List<Entry> AllEntries()
        {
            var list = new List<Entry>();

            list.Add(Tog("Anti-Report", 0, () => SafetyConfig.AntiReportEnabled, v => { SafetyConfig.AntiReportEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Block Telemetry", 0, () => SafetyConfig.TelemetryBlockEnabled, v => { SafetyConfig.TelemetryBlockEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Block PlayFab Reports", 0, () => SafetyConfig.PlayFabBlockEnabled, v => { SafetyConfig.PlayFabBlockEnabled = v; SafetyConfig.Save(); }));
            list.Add(new Entry { Label = "Theme: " + ThemeManager.CurrentTheme.Name, OnPress = () => ThemeManager.StepPalette(), Category = 0 });

            list.Add(Tog("Device Spoofing", 1, () => SafetyConfig.DeviceSpoofEnabled, v => { SafetyConfig.DeviceSpoofEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Network Event Block", 1, () => SafetyConfig.NetworkEventBlockEnabled, v => { SafetyConfig.NetworkEventBlockEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("RPC Limit Bypass", 1, () => SafetyConfig.RPCLimitBypassEnabled, v => { SafetyConfig.RPCLimitBypassEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Grace Period Bypass", 1, () => SafetyConfig.GraceBypassEnabled, v => { SafetyConfig.GraceBypassEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("KID Bypass", 1, () => SafetyConfig.KIDBypassEnabled, v => { SafetyConfig.KIDBypassEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Name Ban Bypass", 1, () => SafetyConfig.NameBanBypassEnabled, v => { SafetyConfig.NameBanBypassEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Core Property Filter", 1, () => SafetyConfig.CoreProtectionEnabled, v => { SafetyConfig.CoreProtectionEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Error Logging", 1, () => SafetyConfig.ErrorLoggingEnabled, v => { SafetyConfig.ErrorLoggingEnabled = v; SafetyConfig.Save(); }));
            list.Add(new Entry { Label = "Reset All Defaults", OnPress = () => { SafetyConfig.ResetToDefaults(); }, Category = 1 });

            list.Add(Tog("Identity Change", 2, () => SafetyConfig.IdentityChangeEnabled, v => { SafetyConfig.IdentityChangeEnabled = v; SafetyConfig.Save(); if (v) IdentityChanger.ApplyRandomName(); }));
            list.Add(new Entry { Label = "Random Name", OnPress = () => { SafetyConfig.CustomName = ""; IdentityChanger.ApplyRandomName(); }, Category = 2 });
            list.Add(Tog("Smart Mode", 2, () => AntiReport.SmartMode, v => { AntiReport.SmartMode = v; SafetyConfig.Save(); }));
            list.Add(Tog("Detection Zones", 2, () => AntiReport.VisualizerEnabled, v => { AntiReport.VisualizerEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Detect Mute Btn", 2, () => AntiReport.AntiMute, v => { AntiReport.AntiMute = v; SafetyConfig.Save(); }));
            list.Add(new Entry { Label = "Range: " + AntiReport.RangeName, OnPress = () => { AntiReport.CycleRange(); SafetyConfig.Save(); }, Category = 2 });
            {
                string[] mn = { "Disconnect", "Reconnect", "Notify" };
                string m = mn[Mathf.Clamp(SafetyConfig.AntiReportMode, 0, mn.Length - 1)];
                list.Add(new Entry { Label = "Mode: " + m, OnPress = () => { SafetyConfig.AntiReportMode = (SafetyConfig.AntiReportMode + 1) % 3; SafetyConfig.Save(); }, Category = 2 });
            }

            list.Add(Tog("All Audio", 3, () => SafetyConfig.AudioEnabled, v => { SafetyConfig.AudioEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Protection Sounds", 3, () => SafetyConfig.PlayProtectionAudio, v => { SafetyConfig.PlayProtectionAudio = v; SafetyConfig.Save(); }));
            list.Add(Tog("Warning Sounds", 3, () => SafetyConfig.PlayWarningAudio, v => { SafetyConfig.PlayWarningAudio = v; SafetyConfig.Save(); }));
            list.Add(Tog("Ban Detect Sounds", 3, () => SafetyConfig.PlayBanAudio, v => { SafetyConfig.PlayBanAudio = v; SafetyConfig.Save(); }));

            list.Add(Tog("Moderator Detect", 4, () => SafetyConfig.ModeratorDetectorEnabled, v => { SafetyConfig.ModeratorDetectorEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Anti-Creator", 4, () => SafetyConfig.AntiContentCreatorEnabled, v => { SafetyConfig.AntiContentCreatorEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Cosmetic Notifs", 4, () => SafetyConfig.CosmeticNotificationsEnabled, v => { SafetyConfig.CosmeticNotificationsEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Automod Bypass", 4, () => SafetyConfig.AutomodBypassEnabled, v => { SafetyConfig.AutomodBypassEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Anti-Predictions", 4, () => SafetyConfig.AntiPredictionsEnabled, v => { SafetyConfig.AntiPredictionsEnabled = v; SafetyConfig.Save(); if (!v) Patches.AntiPredictions.Reset(); }));
            list.Add(Tog("Anti-Lurker", 4, () => SafetyConfig.AntiLurkerEnabled, v => { SafetyConfig.AntiLurkerEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Fake Oculus Menu", 4, () => SafetyConfig.FakeOculusMenuEnabled, v => { SafetyConfig.FakeOculusMenuEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Fake Broken Ctrl", 4, () => SafetyConfig.FakeBrokenControllerEnabled, v => { SafetyConfig.FakeBrokenControllerEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Fake Report Menu", 4, () => SafetyConfig.FakeReportMenuEnabled, v => { SafetyConfig.FakeReportMenuEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Fake Valve Track", 4, () => SafetyConfig.FakeValveTrackingEnabled, v => { SafetyConfig.FakeValveTrackingEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Anti-Crash", 4, () => SafetyConfig.AntiCrashEnabled, v => { SafetyConfig.AntiCrashEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Anti-Kick", 4, () => SafetyConfig.AntiKickEnabled, v =>
            {
                SafetyConfig.AntiKickEnabled = v; SafetyConfig.Save();
                if (v) Patches.AntiKickHelper.Enable(); else Patches.AntiKickHelper.Disable();
            }));
            list.Add(Tog("Show AC Reports", 4, () => SafetyConfig.ShowACReportsEnabled, v => { SafetyConfig.ShowACReportsEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Auto GC", 4, () => SafetyConfig.AutoGCEnabled, v => { SafetyConfig.AutoGCEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Spoof Support", 4, () => SafetyConfig.SupportPageSpoofEnabled, v => { SafetyConfig.SupportPageSpoofEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Ranked Spoof", 4, () => SafetyConfig.RankedSpoofEnabled, v => { SafetyConfig.RankedSpoofEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Name on DC", 4, () => SafetyConfig.ChangeIdentityOnDisconnect, v => { SafetyConfig.ChangeIdentityOnDisconnect = v; SafetyConfig.Save(); }));
            list.Add(Tog("Random Color", 4, () => SafetyConfig.ColorChangeEnabled, v => { SafetyConfig.ColorChangeEnabled = v; SafetyConfig.Save(); }));
            list.Add(new Entry { Label = "Flush RPCs", OnPress = () => Patches.RPCFlusher.Flush(), Category = 4 });
            list.Add(new Entry { Label = "Fix Lobby", OnPress = () => Patches.LobbyFixer.Fix(), Category = 4 });
            list.Add(new Entry { Label = "Rejoin Lobby", OnPress = () => Patches.LobbyFixer.Rejoin(), Category = 4 });
            list.Add(new Entry { Label = "RESTART", OnPress = () => Patches.GameRestarter.Restart(), Category = 4 });
            list.Add(Tog("FPS Spoof", 4, () => SafetyConfig.FPSSpoofEnabled, v => { SafetyConfig.FPSSpoofEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("TOS/Age Bypass", 4, () => SafetyConfig.TOSBypassEnabled, v => { SafetyConfig.TOSBypassEnabled = v; SafetyConfig.Save(); }));
            list.Add(Tog("Anti-Name Ban", 4, () => SafetyConfig.AntiNameBanEnabled, v => { SafetyConfig.AntiNameBanEnabled = v; SafetyConfig.Save(); }));

            return list;
        }

        private static Entry Tog(string label, int cat, Func<bool> get, Action<bool> set)
        {
            return new Entry
            {
                Label = label,
                OnPress = () => set(!get()),
                IsOn = get,
                IsToggle = true,
                Category = cat,
            };
        }

        void OnDestroy()
        {
            Close();
            if (Instance == this) Instance = null;
        }
    }
}
