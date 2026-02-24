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
        private GameObject _menuBG;
        private GameObject _pointer;
        private GameObject _canvasObj;

        private readonly List<GameObject> _buttons = new List<GameObject>();

        private int _currentPage;
        private int _currentTab;
        private int _pageCount;

        private static readonly string[] TabNames = { "Main", "Advanced", "Identity", "Audio", "Extra" };

        private const int PageSize = 6;
        private const float ButtonDist = 0.1f;

        private const float RootX = 0.1f;
        private const float RootY = 0.3f;
        private const float RootZ = 0.3825f;

        private static readonly Vector3 TextRot = new Vector3(180f, 90f, 90f);
        private static readonly Vector3 PointerOffset = new Vector3(0f, -0.1f, 0f);
        private static readonly Vector3 PointerScale = new Vector3(0.01f, 0.01f, 0.01f);
        private static readonly Vector3 BGLocalPos = new Vector3(0.5f, 0f, 0f);
        private static readonly Vector3 BGLocalScale = new Vector3(0.1f, 1f, 1f);

        private bool _lastSecondary;

        private struct ButtonEntry
        {
            public string Label;
            public Action OnPress;
            public Func<bool> IsOn;
            public bool IsToggle;
        }

        void Awake()
        {
            Instance = this;
            IsOpen = false;
        }

        void Update()
        {
            bool secondary = false;
            try
            {
                secondary = ControllerInputPoller.instance != null &&
                    ControllerInputPoller.instance.leftControllerSecondaryButton;
            }
            catch { }

            if (secondary && !_lastSecondary)
            {
                if (IsOpen) CloseMenu();
                else OpenMenu();
            }
            _lastSecondary = secondary;

            if (IsOpen && _menuRoot != null)
                RecenterMenu();

            ThemeManager.TickRainbow();
        }

        private void OpenMenu()
        {
            CreateMenu();
            CreatePointer();
            IsOpen = true;
        }

        private void CloseMenu()
        {
            if (_menuRoot != null) Destroy(_menuRoot);
            if (_pointer != null) Destroy(_pointer);
            _menuRoot = null;
            _pointer = null;
            _buttons.Clear();
            IsOpen = false;
        }

        private void RecenterMenu()
        {
            Transform hand = null;
            try { hand = GorillaTagger.Instance.rightHandTransform; } catch { }
            if (hand == null || _menuRoot == null) return;

            float s = 1f;
            try { s = GorillaLocomotion.GTPlayer.Instance.scale; } catch { }

            _menuRoot.transform.position = hand.position;

            Vector3 euler = hand.rotation.eulerAngles;
            euler += new Vector3(0f, 0f, 180f);
            _menuRoot.transform.rotation = Quaternion.Euler(euler);

            _menuRoot.transform.localScale = new Vector3(RootX, RootY, RootZ) * s;
        }

        private void CreateMenu()
        {
            if (_menuRoot != null) Destroy(_menuRoot);
            _buttons.Clear();

            _menuRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(_menuRoot.GetComponent<BoxCollider>());
            UnityEngine.Object.Destroy(_menuRoot.GetComponent<Renderer>());
            _menuRoot.transform.localScale = new Vector3(RootX, RootY, RootZ);

            var theme = ThemeManager.CurrentTheme;

            _menuBG = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(_menuBG.GetComponent<BoxCollider>());
            _menuBG.transform.SetParent(_menuRoot.transform, false);
            _menuBG.transform.localPosition = BGLocalPos;
            _menuBG.transform.localRotation = Quaternion.identity;
            _menuBG.transform.localScale = BGLocalScale;
            SetColor(_menuBG, theme.PanelColor);

            _canvasObj = new GameObject("Canvas");
            _canvasObj.transform.SetParent(_menuRoot.transform, false);
            var canvas = _canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = _canvasObj.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 2500f;
            _canvasObj.AddComponent<GraphicRaycaster>();

            CreateTitle(theme);
            CreateTabs(theme);
            CreatePageButtons(theme);

            var entries = GetPageEntries(_currentTab, _currentPage);
            for (int i = 0; i < entries.Count; i++)
                CreateButton(i, entries[i], theme);
        }

        private void CreatePointer()
        {
            if (_pointer != null) Destroy(_pointer);

            Transform hand = null;
            try { hand = GorillaTagger.Instance.leftHandTransform; } catch { }

            _pointer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.Destroy(_pointer.GetComponent<Renderer>());

            if (hand != null)
                _pointer.transform.SetParent(hand, false);

            _pointer.transform.localPosition = PointerOffset;
            _pointer.transform.localScale = PointerScale;

            var rb = _pointer.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var sphere = _pointer.GetComponent<SphereCollider>();
            sphere.isTrigger = true;
            TouchReactor.Probe = sphere;

            var theme = ThemeManager.CurrentTheme;

            GameObject vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.Destroy(vis.GetComponent<SphereCollider>());
            vis.transform.SetParent(_pointer.transform, false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localScale = Vector3.one * 1.2f;
            SetColor(vis, theme.PointerColor);
        }

        private void CreateTitle(ThemeManager.Swatch theme)
        {
            var go = new GameObject("Title");
            go.transform.SetParent(_canvasObj.transform, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "Signal Safety";
            tmp.fontSize = 1;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0;
            tmp.color = theme.AccentColor;
            tmp.richText = true;

            var rt = go.GetComponent<RectTransform>();
            rt.localPosition = new Vector3(0.06f, 0f, 0.165f);
            rt.sizeDelta = new Vector2(0.28f, 0.05f);
            rt.rotation = Quaternion.Euler(TextRot);
        }

        private void CreateTabs(ThemeManager.Swatch theme)
        {
            float tabHeight = 0.028f;
            float tabWidth = 0.28f / TabNames.Length;

            for (int i = 0; i < TabNames.Length; i++)
            {
                int tabIndex = i;
                float yOff = -0.5f + tabWidth * 0.5f + tabWidth * i;

                var btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                btn.layer = 2;
                btn.GetComponent<BoxCollider>().isTrigger = true;
                btn.transform.SetParent(_menuRoot.transform, false);
                btn.transform.localRotation = Quaternion.identity;
                btn.transform.localScale = new Vector3(0.09f, 0.18f, tabHeight * 2.5f);
                btn.transform.localPosition = new Vector3(0.56f, yOff, 0.37f);

                bool selected = tabIndex == _currentTab;
                SetColor(btn, selected ? theme.TabSelected : theme.TabIdle);

                var reactor = btn.AddComponent<TouchReactor>();
                reactor.Init(() =>
                {
                    if (_currentTab != tabIndex)
                    {
                        _currentTab = tabIndex;
                        _currentPage = 0;
                        RebuildMenu();
                    }
                }, false);
                reactor.SetBaseColor(selected ? theme.TabSelected : theme.TabIdle);

                _buttons.Add(btn);

                var textGo = new GameObject("TabText");
                textGo.transform.SetParent(_canvasObj.transform, false);
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.text = TabNames[tabIndex];
                tmp.fontSize = 1;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 0;
                tmp.color = selected ? theme.TextPrimary : theme.TextDim;
                tmp.richText = true;

                var rt = textGo.GetComponent<RectTransform>();
                float textY = -0.5f * 0.28f + tabWidth * 0.5f * 0.28f / tabWidth + 0.28f / TabNames.Length * i;
                rt.localPosition = new Vector3(0.06f, yOff * 0.28f / 1f, 0.135f);
                rt.sizeDelta = new Vector2(tabWidth * 0.9f, 0.03f);
                rt.rotation = Quaternion.Euler(TextRot);
            }
        }

        private void CreatePageButtons(ThemeManager.Swatch theme)
        {
            var allEntries = GetAllEntries(_currentTab);
            _pageCount = Mathf.Max(1, Mathf.CeilToInt((float)allEntries.Count / PageSize));
            if (_currentPage >= _pageCount) _currentPage = 0;

            float offset0 = ButtonDist * 0;
            float offset1 = ButtonDist * 1;

            var prevBtn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prevBtn.layer = 2;
            prevBtn.GetComponent<BoxCollider>().isTrigger = true;
            prevBtn.transform.SetParent(_menuRoot.transform, false);
            prevBtn.transform.localRotation = Quaternion.identity;
            prevBtn.transform.localScale = new Vector3(0.09f, 1.3f, ButtonDist * 0.8f);
            prevBtn.transform.localPosition = new Vector3(0.56f, 0f, 0.28f - offset0);
            SetColor(prevBtn, theme.ButtonIdle);

            var prevReactor = prevBtn.AddComponent<TouchReactor>();
            prevReactor.Init(() =>
            {
                _currentPage--;
                if (_currentPage < 0) _currentPage = _pageCount - 1;
                RebuildMenu();
            }, false);
            prevReactor.SetBaseColor(theme.ButtonIdle);
            _buttons.Add(prevBtn);

            var prevText = new GameObject("PrevText");
            prevText.transform.SetParent(_canvasObj.transform, false);
            var prevTmp = prevText.AddComponent<TextMeshProUGUI>();
            prevTmp.text = $"< {_currentPage + 1}/{_pageCount}";
            prevTmp.fontSize = 1;
            prevTmp.alignment = TextAlignmentOptions.Center;
            prevTmp.enableAutoSizing = true;
            prevTmp.fontSizeMin = 0;
            prevTmp.color = theme.TextPrimary;
            var prevRt = prevText.GetComponent<RectTransform>();
            prevRt.sizeDelta = new Vector2(0.2f, 0.03f);
            prevRt.localPosition = new Vector3(0.064f, 0f, 0.111f - offset0 / 2.6f);
            prevRt.rotation = Quaternion.Euler(TextRot);

            var nextBtn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nextBtn.layer = 2;
            nextBtn.GetComponent<BoxCollider>().isTrigger = true;
            nextBtn.transform.SetParent(_menuRoot.transform, false);
            nextBtn.transform.localRotation = Quaternion.identity;
            nextBtn.transform.localScale = new Vector3(0.09f, 1.3f, ButtonDist * 0.8f);
            nextBtn.transform.localPosition = new Vector3(0.56f, 0f, 0.28f - offset1);
            SetColor(nextBtn, theme.ButtonIdle);

            var nextReactor = nextBtn.AddComponent<TouchReactor>();
            nextReactor.Init(() =>
            {
                _currentPage++;
                if (_currentPage >= _pageCount) _currentPage = 0;
                RebuildMenu();
            }, false);
            nextReactor.SetBaseColor(theme.ButtonIdle);
            _buttons.Add(nextBtn);

            var nextText = new GameObject("NextText");
            nextText.transform.SetParent(_canvasObj.transform, false);
            var nextTmp = nextText.AddComponent<TextMeshProUGUI>();
            nextTmp.text = $"{_currentPage + 1}/{_pageCount} >";
            nextTmp.fontSize = 1;
            nextTmp.alignment = TextAlignmentOptions.Center;
            nextTmp.enableAutoSizing = true;
            nextTmp.fontSizeMin = 0;
            nextTmp.color = theme.TextPrimary;
            var nextRt = nextText.GetComponent<RectTransform>();
            nextRt.sizeDelta = new Vector2(0.2f, 0.03f);
            nextRt.localPosition = new Vector3(0.064f, 0f, 0.111f - offset1 / 2.6f);
            nextRt.rotation = Quaternion.Euler(TextRot);
        }

        private void CreateButton(int index, ButtonEntry entry, ThemeManager.Swatch theme)
        {
            int btnOffset = 2;
            float offset = ButtonDist * (index + btnOffset);

            bool isActive = entry.IsToggle && entry.IsOn != null && entry.IsOn();
            Color btnColor = isActive ? theme.ButtonActive : theme.ButtonIdle;

            var btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            btn.layer = 2;
            btn.GetComponent<BoxCollider>().isTrigger = true;
            btn.transform.SetParent(_menuRoot.transform, false);
            btn.transform.localRotation = Quaternion.identity;
            btn.transform.localScale = new Vector3(0.09f, 1.3f, ButtonDist * 0.8f);
            btn.transform.localPosition = new Vector3(0.56f, 0f, 0.28f - offset);
            SetColor(btn, btnColor);

            var reactor = btn.AddComponent<TouchReactor>();
            reactor.Init(() =>
            {
                try { entry.OnPress?.Invoke(); } catch { }
                RebuildMenu();
            }, entry.IsToggle);
            reactor.SetBaseColor(btnColor);
            _buttons.Add(btn);

            string label = entry.Label;
            if (entry.IsToggle && entry.IsOn != null)
                label = (entry.IsOn() ? "[ON] " : "[OFF] ") + label;

            var textGo = new GameObject("BtnText");
            textGo.transform.SetParent(_canvasObj.transform, false);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 1;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0;
            tmp.color = isActive ? theme.TextPrimary : theme.TextDim;
            tmp.richText = true;

            var rt = textGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0.2f, 0.03f);
            rt.localPosition = new Vector3(0.064f, 0f, 0.111f - offset / 2.6f);
            rt.rotation = Quaternion.Euler(TextRot);
        }

        public void RebuildMenu()
        {
            if (!IsOpen) return;
            CreateMenu();
        }

        private List<ButtonEntry> GetPageEntries(int tab, int page)
        {
            var all = GetAllEntries(tab);
            int start = page * PageSize;
            int count = Mathf.Min(PageSize, all.Count - start);
            if (start >= all.Count) return new List<ButtonEntry>();
            return all.GetRange(start, count);
        }

        private List<ButtonEntry> GetAllEntries(int tab)
        {
            switch (tab)
            {
                case 0: return BuildMainEntries();
                case 1: return BuildAdvancedEntries();
                case 2: return BuildIdentityEntries();
                case 3: return BuildAudioEntries();
                case 4: return BuildExtraEntries();
                default: return new List<ButtonEntry>();
            }
        }

        private List<ButtonEntry> BuildMainEntries()
        {
            var list = new List<ButtonEntry>();

            list.Add(Toggle("Anti-Report", () => SafetyConfig.AntiReportEnabled, v => { SafetyConfig.AntiReportEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "safety_disabled", AudioManager.AudioCategory.Toggle); }));
            list.Add(Toggle("Block Telemetry", () => SafetyConfig.TelemetryBlockEnabled, v => { SafetyConfig.TelemetryBlockEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle); }));
            list.Add(Toggle("Block PlayFab Reports", () => SafetyConfig.PlayFabBlockEnabled, v => { SafetyConfig.PlayFabBlockEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle); }));

            list.Add(new ButtonEntry
            {
                Label = $"Theme: {ThemeManager.CurrentTheme.Name}",
                OnPress = () => { ThemeManager.StepPalette(); },
                IsOn = null,
                IsToggle = false,
            });

            return list;
        }

        private List<ButtonEntry> BuildAdvancedEntries()
        {
            var list = new List<ButtonEntry>();

            list.Add(Toggle("Device Spoofing", () => SafetyConfig.DeviceSpoofEnabled, v => { SafetyConfig.DeviceSpoofEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle); }));
            list.Add(Toggle("Network Event Block", () => SafetyConfig.NetworkEventBlockEnabled, v => { SafetyConfig.NetworkEventBlockEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle); }));
            list.Add(Toggle("RPC Limit Bypass", () => SafetyConfig.RPCLimitBypassEnabled, v => { SafetyConfig.RPCLimitBypassEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle); }));
            list.Add(Toggle("Grace Period Bypass", () => SafetyConfig.GraceBypassEnabled, v => { SafetyConfig.GraceBypassEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "safety_disabled", AudioManager.AudioCategory.Toggle); }));
            list.Add(Toggle("KID Bypass", () => SafetyConfig.KIDBypassEnabled, v => { SafetyConfig.KIDBypassEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle); }));
            list.Add(Toggle("Name Ban Bypass", () => SafetyConfig.NameBanBypassEnabled, v => { SafetyConfig.NameBanBypassEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle); }));
            list.Add(Toggle("Core Property Filter", () => SafetyConfig.CoreProtectionEnabled, v => { SafetyConfig.CoreProtectionEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "safety_disabled", AudioManager.AudioCategory.Toggle); }));
            list.Add(Toggle("Error Logging", () => SafetyConfig.ErrorLoggingEnabled, v => { SafetyConfig.ErrorLoggingEnabled = v; SafetyConfig.Save(); }));

            list.Add(new ButtonEntry
            {
                Label = "Reset All to Defaults",
                OnPress = () => { SafetyConfig.ResetToDefaults(); AudioManager.Play("done", AudioManager.AudioCategory.Toggle); },
                IsOn = null,
                IsToggle = false,
            });

            return list;
        }

        private List<ButtonEntry> BuildIdentityEntries()
        {
            var list = new List<ButtonEntry>();

            list.Add(Toggle("Identity Change", () => SafetyConfig.IdentityChangeEnabled, v =>
            {
                SafetyConfig.IdentityChangeEnabled = v;
                SafetyConfig.Save();
                if (v) { AudioManager.Play("done", AudioManager.AudioCategory.Toggle); IdentityChanger.ApplyRandomName(); }
            }));

            list.Add(new ButtonEntry
            {
                Label = "Generate Random Name",
                OnPress = () => { SafetyConfig.CustomName = ""; IdentityChanger.ApplyRandomName(); },
                IsOn = null,
                IsToggle = false,
            });

            list.Add(Toggle("Smart Mode", () => AntiReport.SmartMode, v => { AntiReport.SmartMode = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Show Detection Zones", () => AntiReport.VisualizerEnabled, v => { AntiReport.VisualizerEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Detect Mute Button", () => AntiReport.AntiMute, v => { AntiReport.AntiMute = v; SafetyConfig.Save(); }));

            list.Add(new ButtonEntry
            {
                Label = $"Range: {AntiReport.RangeName}",
                OnPress = () => { AntiReport.CycleRange(); SafetyConfig.Save(); },
                IsOn = null,
                IsToggle = false,
            });

            string[] modeNames = { "Disconnect", "Reconnect", "Notify Only" };
            string mode = modeNames[Mathf.Clamp(SafetyConfig.AntiReportMode, 0, modeNames.Length - 1)];
            list.Add(new ButtonEntry
            {
                Label = $"Mode: {mode}",
                OnPress = () => { SafetyConfig.AntiReportMode = (SafetyConfig.AntiReportMode + 1) % 3; SafetyConfig.Save(); },
                IsOn = null,
                IsToggle = false,
            });

            return list;
        }

        private List<ButtonEntry> BuildAudioEntries()
        {
            var list = new List<ButtonEntry>();

            list.Add(Toggle("Enable All Audio", () => SafetyConfig.AudioEnabled, v => { SafetyConfig.AudioEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Protection Sounds", () => SafetyConfig.PlayProtectionAudio, v => { SafetyConfig.PlayProtectionAudio = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Warning Sounds", () => SafetyConfig.PlayWarningAudio, v => { SafetyConfig.PlayWarningAudio = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Ban Detection Sounds", () => SafetyConfig.PlayBanAudio, v => { SafetyConfig.PlayBanAudio = v; SafetyConfig.Save(); }));

            return list;
        }

        private List<ButtonEntry> BuildExtraEntries()
        {
            var list = new List<ButtonEntry>();

            list.Add(Toggle("Moderator Detection", () => SafetyConfig.ModeratorDetectorEnabled, v => { SafetyConfig.ModeratorDetectorEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle); }));
            list.Add(Toggle("Anti-Content Creator", () => SafetyConfig.AntiContentCreatorEnabled, v => { SafetyConfig.AntiContentCreatorEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "safety_disabled", AudioManager.AudioCategory.Toggle); }));
            list.Add(Toggle("Cosmetic Notifications", () => SafetyConfig.CosmeticNotificationsEnabled, v => { SafetyConfig.CosmeticNotificationsEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Automod Bypass", () => SafetyConfig.AutomodBypassEnabled, v => { SafetyConfig.AutomodBypassEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "safety_disabled", AudioManager.AudioCategory.Toggle); }));
            list.Add(Toggle("Anti-Predictions", () => SafetyConfig.AntiPredictionsEnabled, v => { SafetyConfig.AntiPredictionsEnabled = v; SafetyConfig.Save(); if (!v) Patches.AntiPredictions.Reset(); }));
            list.Add(Toggle("Anti-Lurker", () => SafetyConfig.AntiLurkerEnabled, v => { SafetyConfig.AntiLurkerEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Fake Oculus Menu", () => SafetyConfig.FakeOculusMenuEnabled, v => { SafetyConfig.FakeOculusMenuEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Fake Broken Controller", () => SafetyConfig.FakeBrokenControllerEnabled, v => { SafetyConfig.FakeBrokenControllerEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Fake Report Menu", () => SafetyConfig.FakeReportMenuEnabled, v => { SafetyConfig.FakeReportMenuEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Fake Valve Tracking", () => SafetyConfig.FakeValveTrackingEnabled, v => { SafetyConfig.FakeValveTrackingEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Anti-Crash", () => SafetyConfig.AntiCrashEnabled, v => { SafetyConfig.AntiCrashEnabled = v; SafetyConfig.Save(); AudioManager.Play(v ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle); }));
            list.Add(Toggle("Anti-Kick", () => SafetyConfig.AntiKickEnabled, v =>
            {
                SafetyConfig.AntiKickEnabled = v;
                SafetyConfig.Save();
                if (v) Patches.AntiKickHelper.Enable(); else Patches.AntiKickHelper.Disable();
                AudioManager.Play(v ? "safety_enabled" : "warning", AudioManager.AudioCategory.Toggle);
            }));
            list.Add(Toggle("Show AC Reports", () => SafetyConfig.ShowACReportsEnabled, v => { SafetyConfig.ShowACReportsEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Auto GC", () => SafetyConfig.AutoGCEnabled, v => { SafetyConfig.AutoGCEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Spoof Support Page", () => SafetyConfig.SupportPageSpoofEnabled, v => { SafetyConfig.SupportPageSpoofEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Ranked Spoof", () => SafetyConfig.RankedSpoofEnabled, v => { SafetyConfig.RankedSpoofEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Change Name on DC", () => SafetyConfig.ChangeIdentityOnDisconnect, v => { SafetyConfig.ChangeIdentityOnDisconnect = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Randomize Color", () => SafetyConfig.ColorChangeEnabled, v => { SafetyConfig.ColorChangeEnabled = v; SafetyConfig.Save(); }));

            list.Add(new ButtonEntry { Label = "Flush RPCs", OnPress = () => { Patches.RPCFlusher.Flush(); AudioManager.Play("done", AudioManager.AudioCategory.Toggle); }, IsOn = null, IsToggle = false });
            list.Add(new ButtonEntry { Label = "Fix Lobby", OnPress = () => { Patches.LobbyFixer.Fix(); AudioManager.Play("done", AudioManager.AudioCategory.Toggle); }, IsOn = null, IsToggle = false });
            list.Add(new ButtonEntry { Label = "Rejoin Lobby", OnPress = () => { Patches.LobbyFixer.Rejoin(); AudioManager.Play("done", AudioManager.AudioCategory.Toggle); }, IsOn = null, IsToggle = false });
            list.Add(new ButtonEntry { Label = "RESTART", OnPress = () => { Patches.GameRestarter.Restart(); }, IsOn = null, IsToggle = false });

            list.Add(Toggle("FPS Spoof", () => SafetyConfig.FPSSpoofEnabled, v => { SafetyConfig.FPSSpoofEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("TOS/Age Bypass", () => SafetyConfig.TOSBypassEnabled, v => { SafetyConfig.TOSBypassEnabled = v; SafetyConfig.Save(); }));
            list.Add(Toggle("Anti-Name Ban", () => SafetyConfig.AntiNameBanEnabled, v => { SafetyConfig.AntiNameBanEnabled = v; SafetyConfig.Save(); }));

            return list;
        }

        private static ButtonEntry Toggle(string label, Func<bool> getter, Action<bool> setter)
        {
            return new ButtonEntry
            {
                Label = label,
                OnPress = () => setter(!getter()),
                IsOn = getter,
                IsToggle = true,
            };
        }

        private static void SetColor(GameObject obj, Color c)
        {
            var rend = obj.GetComponent<Renderer>();
            if (rend == null) return;
            rend.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
            rend.material.color = c;
        }

        void OnDestroy()
        {
            CloseMenu();
            if (Instance == this) Instance = null;
        }
    }
}
