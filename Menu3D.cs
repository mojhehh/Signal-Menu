using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SignalSafetyMenu
{
    public class Menu3D : MonoBehaviour
    {
        public static Menu3D Instance;
        public static bool IsOpen { get; private set; }

        private GameObject _menuRoot;
        private GameObject _pointer;
        private SphereCollider _pointerCollider;

        private const float PanelWidth = 0.32f;
        private const float PanelHeight = 0.42f;
        private const float PanelDepth = 0.008f;
        private const float ButtonHeight = 0.032f;
        private const float ButtonSpacing = 0.004f;
        private const float TabHeight = 0.035f;
        private const float HeaderHeight = 0.045f;
        private const float Margin = 0.012f;
        private const float PointerRadius = 0.008f;

        private static Color PanelColor => ThemeManager.CurrentTheme.PanelColor;
        private static Color AccentCyan => ThemeManager.CurrentTheme.AccentColor;
        private static Color AccentGlow => ThemeManager.CurrentTheme.AccentGlow;
        private static Color ButtonIdle => ThemeManager.CurrentTheme.ButtonIdle;
        private static Color ButtonActive => ThemeManager.CurrentTheme.ButtonActive;
        private static Color TabIdle => ThemeManager.CurrentTheme.TabIdle;
        private static Color TabSelected => ThemeManager.CurrentTheme.TabSelected;
        private static Color TextWhite => ThemeManager.CurrentTheme.TextPrimary;
        private static Color TextDim => ThemeManager.CurrentTheme.TextDim;
        private static Color PointerColor => ThemeManager.CurrentTheme.PointerColor;
        private static Color DividerColor => ThemeManager.CurrentTheme.DividerColor;
        private static readonly Color WarningYellow = new Color(1f, 0.85f, 0f);

        private int _currentTab = 0;
        private int _pageOffset = 0;
        private float _scrollCooldown = 0f;
        private bool _lastToggleState = false;
        private Material _unlitMat;
        private Vector3 _targetScale;
        private bool _animating = false;

        private static readonly string[] TabNames = { "Shield", "Stealth", "Identity", "Extra", "Settings", "Patches" };

        private readonly List<GameObject> _slotCubes = new List<GameObject>();
        private readonly List<TextMeshPro> _slotLabels = new List<TextMeshPro>();
        private readonly List<GameObject> _slotStripes = new List<GameObject>();
        private readonly List<PanelSlot> _slots = new List<PanelSlot>();
        private TextMeshPro _titleText;
        private TextMeshPro _statusText;
        private TextMeshPro _pageText;
        private GameObject _updateBanner;
        private TextMeshPro _updateText;
        private readonly List<GameObject> _tabObjects = new List<GameObject>();
        private readonly List<TextMeshPro> _tabTexts = new List<TextMeshPro>();
        private int _maxButtons = 8;

        private GameObject _prevPageBtn;
        private GameObject _nextPageBtn;
        private TextMeshPro _prevPageText;
        private TextMeshPro _nextPageText;
        private TextMeshPro _pageNavText;

        public struct PanelSlot
        {
            public string Label;
            public string Description;
            public Func<bool> GetState;
            public Action<bool> SetState;
            public Action OnPress;
            public bool IsToggle;
            public bool IsHeader;
        }

        void Awake()
        {
            Instance = this;
        }

        void Update()
        {
            HandleInput();

            if (IsOpen && _menuRoot != null)
            {
                PositionMenu();
                if (_pointer != null) PositionPointer();
                SyncDynamicUI();
                ThemeManager.TickRainbow();
            }
        }

        void OnDestroy()
        {
            DestroyMenu();
            if (_pointer != null) Destroy(_pointer);
        }

        private void HandleInput()
        {
            bool toggle = false;
            try
            {
                toggle = ControllerInputPoller.instance != null &&
                         ControllerInputPoller.instance.rightControllerSecondaryButton;
            }
            catch { }

            if (toggle && !_lastToggleState)
            {
                if (!IsOpen)
                    OpenMenu();
                else
                    CloseMenu();
            }
            _lastToggleState = toggle;
        }

        public void OpenMenu()
        {
            if (IsOpen || _animating) return;
            IsOpen = true;
            BuildMenu();
            if (_pointer == null) BuildPointer();
            _pointer?.SetActive(true);

            try { GorillaTagger.Instance?.offlineVRRig?.PlayHandTapLocal(67, true, 0.04f); } catch { }

            StartCoroutine(AnimateScale(Vector3.zero, _targetScale, 0.15f));
        }

        public void CloseMenu()
        {
            if (!IsOpen || _animating) return;
            IsOpen = false;
            _pointer?.SetActive(false);

            try { GorillaTagger.Instance?.offlineVRRig?.PlayHandTapLocal(67, true, 0.03f); } catch { }

            if (_menuRoot != null)
                StartCoroutine(AnimateScale(_menuRoot.transform.localScale, Vector3.zero, 0.1f, true));
            else
                DestroyMenu();
        }

        private IEnumerator AnimateScale(Vector3 from, Vector3 to, float duration, bool destroyAfter = false)
        {
            _animating = true;
            if (_menuRoot == null) { _animating = false; yield break; }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                if (_menuRoot != null)
                    _menuRoot.transform.localScale = Vector3.Lerp(from, to, t);
                yield return null;
            }

            if (_menuRoot != null)
                _menuRoot.transform.localScale = to;

            if (destroyAfter)
                DestroyMenu();

            _animating = false;
        }

        private Material GetUnlitMaterial()
        {
            if (_unlitMat == null)
            {
                _unlitMat = new Material(Shader.Find("UI/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
                _unlitMat.color = Color.white;
            }
            return _unlitMat;
        }

        private Material MakeMat(Color c)
        {
            var m = new Material(GetUnlitMaterial());
            m.color = c;
            if (c.a < 1f)
            {
                m.SetFloat("_Mode", 3);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 3000;
            }
            return m;
        }

        private void BuildMenu()
        {
            DestroyMenu();

            _menuRoot = new GameObject("SignalMenu3D");
            DontDestroyOnLoad(_menuRoot);

            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(_menuRoot.transform, false);
            panel.transform.localScale = new Vector3(PanelWidth, PanelHeight, PanelDepth);
            panel.transform.localPosition = Vector3.zero;
            Destroy(panel.GetComponent<BoxCollider>());
            panel.GetComponent<Renderer>().material = MakeMat(PanelColor);

            var topLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            topLine.name = "TopAccent";
            topLine.transform.SetParent(_menuRoot.transform, false);
            topLine.transform.localScale = new Vector3(PanelWidth - 0.01f, 0.003f, PanelDepth + 0.001f);
            topLine.transform.localPosition = new Vector3(0, PanelHeight / 2f - 0.002f, 0);
            Destroy(topLine.GetComponent<BoxCollider>());
            topLine.GetComponent<Renderer>().material = MakeMat(AccentCyan);

            var botLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            botLine.name = "BotAccent";
            botLine.transform.SetParent(_menuRoot.transform, false);
            botLine.transform.localScale = new Vector3(PanelWidth - 0.01f, 0.002f, PanelDepth + 0.001f);
            botLine.transform.localPosition = new Vector3(0, -PanelHeight / 2f + 0.002f, 0);
            Destroy(botLine.GetComponent<BoxCollider>());
            botLine.GetComponent<Renderer>().material = MakeMat(AccentGlow);

            for (int side = -1; side <= 1; side += 2)
            {
                var sideLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
                sideLine.name = side < 0 ? "LeftAccent" : "RightAccent";
                sideLine.transform.SetParent(_menuRoot.transform, false);
                sideLine.transform.localScale = new Vector3(0.002f, PanelHeight - 0.01f, PanelDepth + 0.001f);
                sideLine.transform.localPosition = new Vector3(side * (PanelWidth / 2f - 0.002f), 0, 0);
                Destroy(sideLine.GetComponent<BoxCollider>());
                sideLine.GetComponent<Renderer>().material = MakeMat(AccentGlow);
            }

            var canvasObj = new GameObject("MenuCanvas");
            canvasObj.transform.SetParent(_menuRoot.transform, false);
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObj.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3000;
            canvasObj.AddComponent<GraphicRaycaster>();

            var crt = canvasObj.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            crt.localPosition = new Vector3(0, 0, -PanelDepth / 2f - 0.0005f);
            crt.localRotation = Quaternion.identity;
            crt.localScale = Vector3.one;

            _titleText = CreateTMP(canvasObj.transform, "Signal Safety", 0.014f, TextWhite, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _titleText.rectTransform.sizeDelta = new Vector2(PanelWidth - 0.08f, HeaderHeight);
            _titleText.rectTransform.localPosition = new Vector3(-PanelWidth / 2f + Margin + 0.006f, PanelHeight / 2f - HeaderHeight / 2f - 0.006f, 0);

            _statusText = CreateTMP(canvasObj.transform, "? ACTIVE", 0.009f, AccentCyan, TextAlignmentOptions.MidlineRight, FontStyles.Normal);
            _statusText.rectTransform.sizeDelta = new Vector2(0.1f, HeaderHeight);
            _statusText.rectTransform.localPosition = new Vector3(PanelWidth / 2f - Margin - 0.05f, PanelHeight / 2f - HeaderHeight / 2f - 0.006f, 0);

            _updateBanner = new GameObject("UpdateBanner");
            _updateBanner.transform.SetParent(canvasObj.transform, false);
            var bannerImg = _updateBanner.AddComponent<Image>();
            bannerImg.color = new Color(1f, 0.7f, 0f, 0.15f);
            var bannerRT = _updateBanner.GetComponent<RectTransform>();
            bannerRT.sizeDelta = new Vector2(PanelWidth - Margin * 2, 0.022f);
            bannerRT.localPosition = new Vector3(0, PanelHeight / 2f - HeaderHeight - 0.014f, 0);

            _updateText = CreateTMP(_updateBanner.transform, "", 0.007f, WarningYellow, TextAlignmentOptions.Midline, FontStyles.Bold);
            _updateText.rectTransform.sizeDelta = bannerRT.sizeDelta;
            _updateText.rectTransform.localPosition = Vector3.zero;
            _updateBanner.SetActive(false);

            var divider = GameObject.CreatePrimitive(PrimitiveType.Cube);
            divider.name = "HeaderDivider";
            divider.transform.SetParent(_menuRoot.transform, false);
            divider.transform.localScale = new Vector3(PanelWidth - Margin * 2, 0.001f, PanelDepth + 0.0005f);
            float divY = PanelHeight / 2f - HeaderHeight - 0.003f;
            divider.transform.localPosition = new Vector3(0, divY, 0);
            Destroy(divider.GetComponent<BoxCollider>());
            divider.GetComponent<Renderer>().material = MakeMat(DividerColor);

            float tabAreaStart = divY - TabHeight / 2f - 0.005f;
            BuildTabs(canvasObj.transform, tabAreaStart);

            var div2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            div2.name = "TabDivider";
            div2.transform.SetParent(_menuRoot.transform, false);
            div2.transform.localScale = new Vector3(PanelWidth - Margin * 2, 0.0008f, PanelDepth + 0.0005f);
            float div2Y = tabAreaStart - TabHeight / 2f - 0.004f;
            div2.transform.localPosition = new Vector3(0, div2Y, 0);
            Destroy(div2.GetComponent<BoxCollider>());
            div2.GetComponent<Renderer>().material = MakeMat(DividerColor);

            float contentTop = div2Y - 0.006f;
            float contentBottom = -PanelHeight / 2f + 0.035f;
            float pageNavRowHeight = ButtonHeight + ButtonSpacing;
            float availableHeight = contentTop - contentBottom - pageNavRowHeight;
            _maxButtons = Mathf.FloorToInt(availableHeight / (ButtonHeight + ButtonSpacing));
            if (_maxButtons < 1) _maxButtons = 1;

            BuildSlotRow(contentTop);

            float pageNavY = contentTop - (ButtonHeight / 2f) - _maxButtons * (ButtonHeight + ButtonSpacing) - 0.004f;
            BuildPageButtons(pageNavY, canvasObj.transform);

            _pageText = CreateTMP(canvasObj.transform, "", 0.007f, TextDim, TextAlignmentOptions.Midline, FontStyles.Normal);
            _pageText.rectTransform.sizeDelta = new Vector2(PanelWidth, 0.02f);
            _pageText.rectTransform.localPosition = new Vector3(0, -PanelHeight / 2f + 0.015f, 0);

            var footerText = CreateTMP(canvasObj.transform, "discord.gg/rYSRrr8Bhy", 0.005f, TextDim, TextAlignmentOptions.Midline, FontStyles.Italic);
            footerText.rectTransform.sizeDelta = new Vector2(PanelWidth, 0.015f);
            footerText.rectTransform.localPosition = new Vector3(0, -PanelHeight / 2f + 0.005f, 0);

            float playerScale = 1f;
            try
            {
                if (GorillaTagger.Instance?.transform != null)
                    playerScale = GorillaTagger.Instance.transform.localScale.x;
            }
            catch { }
            _targetScale = Vector3.one * 0.9f * playerScale;
            _menuRoot.transform.localScale = _targetScale;

            LoadPage();
            SyncSlots();
        }

        private void BuildTabs(Transform canvasParent, float yCenter)
        {
            _tabObjects.Clear();
            _tabTexts.Clear();

            float totalWidth = PanelWidth - Margin * 2;
            float tabWidth = totalWidth / TabNames.Length;

            for (int i = 0; i < TabNames.Length; i++)
            {
                var tab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tab.name = $"Tab_{TabNames[i]}";
                tab.transform.SetParent(_menuRoot.transform, false);
                tab.transform.localScale = new Vector3(tabWidth - 0.003f, TabHeight, PanelDepth + 0.002f);
                float xPos = -totalWidth / 2f + tabWidth / 2f + i * tabWidth;
                tab.transform.localPosition = new Vector3(xPos, yCenter, 0);

                var col = tab.GetComponent<BoxCollider>();
                col.isTrigger = true;
                tab.layer = 0;

                var bc = tab.AddComponent<TouchReactor>();
                int idx = i;
                bc.Init(() => SwitchTab(idx), false);

                tab.GetComponent<Renderer>().material = MakeMat(i == _currentTab ? TabSelected : TabIdle);
                _tabObjects.Add(tab);

                var label = CreateTMP(canvasParent, TabNames[i], 0.008f,
                    i == _currentTab ? TextWhite : TextDim,
                    TextAlignmentOptions.Midline, i == _currentTab ? FontStyles.Bold : FontStyles.Normal);
                label.rectTransform.sizeDelta = new Vector2(tabWidth, TabHeight);
                label.rectTransform.localPosition = new Vector3(xPos, yCenter, 0);
                _tabTexts.Add(label);
            }
        }

        private void SwitchTab(int idx)
        {
            if (idx == _currentTab) return;
            _currentTab = idx;
            _pageOffset = 0;
            RebuildMenu();
        }

        private void BuildPageButtons(float yCenter, Transform canvasParent)
        {
            float pageBtnWidth = 0.055f;
            float centerGap = 0.08f;

            _prevPageBtn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _prevPageBtn.name = "PageBtn_Prev";
            _prevPageBtn.transform.SetParent(_menuRoot.transform, false);
            _prevPageBtn.transform.localScale = new Vector3(pageBtnWidth, ButtonHeight, PanelDepth + 0.003f);
            _prevPageBtn.transform.localPosition = new Vector3(-centerGap / 2f - pageBtnWidth / 2f, yCenter, 0);
            _prevPageBtn.GetComponent<BoxCollider>().isTrigger = true;
            var prevBC = _prevPageBtn.AddComponent<TouchReactor>();
            prevBC.Init(() => ChangePage(-1), false);
            _prevPageBtn.GetComponent<Renderer>().material = MakeMat(TabIdle);
            prevBC.SetBaseColor(TabIdle);

            _prevPageText = CreateTMP(canvasParent, "?", 0.01f, AccentCyan, TextAlignmentOptions.Midline, FontStyles.Bold);
            _prevPageText.rectTransform.sizeDelta = new Vector2(pageBtnWidth, ButtonHeight);
            _prevPageText.rectTransform.localPosition = new Vector3(-centerGap / 2f - pageBtnWidth / 2f, yCenter, 0);

            _nextPageBtn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _nextPageBtn.name = "PageBtn_Next";
            _nextPageBtn.transform.SetParent(_menuRoot.transform, false);
            _nextPageBtn.transform.localScale = new Vector3(pageBtnWidth, ButtonHeight, PanelDepth + 0.003f);
            _nextPageBtn.transform.localPosition = new Vector3(centerGap / 2f + pageBtnWidth / 2f, yCenter, 0);
            _nextPageBtn.GetComponent<BoxCollider>().isTrigger = true;
            var nextBC = _nextPageBtn.AddComponent<TouchReactor>();
            nextBC.Init(() => ChangePage(1), false);
            _nextPageBtn.GetComponent<Renderer>().material = MakeMat(TabIdle);
            nextBC.SetBaseColor(TabIdle);

            _nextPageText = CreateTMP(canvasParent, "?", 0.01f, AccentCyan, TextAlignmentOptions.Midline, FontStyles.Bold);
            _nextPageText.rectTransform.sizeDelta = new Vector2(pageBtnWidth, ButtonHeight);
            _nextPageText.rectTransform.localPosition = new Vector3(centerGap / 2f + pageBtnWidth / 2f, yCenter, 0);

            _pageNavText = CreateTMP(canvasParent, "", 0.007f, TextDim, TextAlignmentOptions.Midline, FontStyles.Normal);
            _pageNavText.rectTransform.sizeDelta = new Vector2(centerGap, ButtonHeight);
            _pageNavText.rectTransform.localPosition = new Vector3(0, yCenter, 0);
        }

        private void ChangePage(int direction)
        {
            int pageCount = Mathf.CeilToInt((float)_slots.Count / _maxButtons);
            if (pageCount <= 1) return;

            _pageOffset += direction;
            if (_pageOffset < 0) _pageOffset = pageCount - 1;
            if (_pageOffset >= pageCount) _pageOffset = 0;

            SyncSlots();
        }

        private void BuildSlotRow(float contentTop)
        {
            _slotCubes.Clear();
            _slotLabels.Clear();

            float btnWidth = PanelWidth - Margin * 2 - 0.006f;
            var canvasT = _menuRoot.transform.Find("MenuCanvas");

            for (int i = 0; i < _maxButtons; i++)
            {
                float yPos = contentTop - (ButtonHeight / 2f) - i * (ButtonHeight + ButtonSpacing);

                var btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                btn.name = $"Btn_{i}";
                btn.transform.SetParent(_menuRoot.transform, false);
                btn.transform.localScale = new Vector3(btnWidth, ButtonHeight, PanelDepth + 0.003f);
                btn.transform.localPosition = new Vector3(0, yPos, 0);

                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = $"BtnStripe_{i}";
                stripe.transform.SetParent(btn.transform, false);
                stripe.transform.localScale = new Vector3(0.008f / btnWidth, 0.85f, 1.01f);
                stripe.transform.localPosition = new Vector3(-0.5f + 0.004f / btnWidth, 0, 0);
                Destroy(stripe.GetComponent<BoxCollider>());
                stripe.GetComponent<Renderer>().material = MakeMat(AccentCyan);

                var col = btn.GetComponent<BoxCollider>();
                col.isTrigger = true;

                var bc = btn.AddComponent<TouchReactor>();
                int idx = i;
                bc.Init(() => OnSlotActivated(idx), false);

                btn.GetComponent<Renderer>().material = MakeMat(ButtonIdle);
                _slotCubes.Add(btn);

                var txt = CreateTMP(canvasT, "", 0.008f, TextWhite, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
                txt.rectTransform.sizeDelta = new Vector2(btnWidth - 0.02f, ButtonHeight);
                txt.rectTransform.localPosition = new Vector3(-btnWidth / 2f + Margin + 0.012f, yPos, 0);
                _slotLabels.Add(txt);
            }
        }

        private void LoadPage()
        {
            _slots.Clear();

            switch (_currentTab)
            {
                case 0: BuildShieldPage(); break;
                case 1: BuildStealthPage(); break;
                case 2: BuildIdentityPage(); break;
                case 3: BuildExtraPage(); break;
                case 4: BuildSettingsPage(); break;
                case 5: BuildPatchesPage(); break;
            }
        }

        private void BuildShieldPage()
        {
            Section("CORE PROTECTION", header: true);
            Switch("Anti-Report", () => SafetyConfig.AntiReportEnabled, v => { SafetyConfig.AntiReportEnabled = v; SafetyConfig.Save(); });
            Switch("Block Telemetry", () => SafetyConfig.TelemetryBlockEnabled, v => { SafetyConfig.TelemetryBlockEnabled = v; SafetyConfig.Save(); });
            Switch("Block PlayFab Reports", () => SafetyConfig.PlayFabBlockEnabled, v => { SafetyConfig.PlayFabBlockEnabled = v; SafetyConfig.Save(); });
            Switch("Block Network Events", () => SafetyConfig.NetworkEventBlockEnabled, v => { SafetyConfig.NetworkEventBlockEnabled = v; SafetyConfig.Save(); });
            Switch("RPC Limit Bypass", () => SafetyConfig.RPCLimitBypassEnabled, v => { SafetyConfig.RPCLimitBypassEnabled = v; SafetyConfig.Save(); });
            Switch("Grace Period Bypass", () => SafetyConfig.GraceBypassEnabled, v => { SafetyConfig.GraceBypassEnabled = v; SafetyConfig.Save(); });
            Switch("KID Bypass", () => SafetyConfig.KIDBypassEnabled, v => { SafetyConfig.KIDBypassEnabled = v; SafetyConfig.Save(); });
            Switch("Name Ban Bypass", () => SafetyConfig.NameBanBypassEnabled, v => { SafetyConfig.NameBanBypassEnabled = v; SafetyConfig.Save(); });
            Switch("Core Property Filter", () => SafetyConfig.CoreProtectionEnabled, v => { SafetyConfig.CoreProtectionEnabled = v; SafetyConfig.Save(); });
            Switch("Device Spoofing", () => SafetyConfig.DeviceSpoofEnabled, v => { SafetyConfig.DeviceSpoofEnabled = v; SafetyConfig.Save(); });
            Switch("Anti-Crash", () => SafetyConfig.AntiCrashEnabled, v => { SafetyConfig.AntiCrashEnabled = v; SafetyConfig.Save(); });
            Switch("Anti-Kick", () => SafetyConfig.AntiKickEnabled, v =>
            {
                SafetyConfig.AntiKickEnabled = v; SafetyConfig.Save();
                if (v) Patches.AntiKickHelper.Enable(); else Patches.AntiKickHelper.Disable();
            });
            Section("SESSION PROTECTION", header: true);
            Switch("Anti-AFK Kick", () => SafetyConfig.AntiAFKKickEnabled, v => { SafetyConfig.AntiAFKKickEnabled = v; SafetyConfig.Save(); });
            Switch("Anti-Pause Disconnect", () => SafetyConfig.AntiPauseDisconnectEnabled, v => { SafetyConfig.AntiPauseDisconnectEnabled = v; SafetyConfig.Save(); });
            Switch("Version Check Bypass", () => SafetyConfig.VersionBypassEnabled, v => { SafetyConfig.VersionBypassEnabled = v; SafetyConfig.Save(); });
            Switch("Block Account Data Save", () => SafetyConfig.BlockModAccountSave, v => { SafetyConfig.BlockModAccountSave = v; SafetyConfig.Save(); });
        }

        private void BuildStealthPage()
        {
            Section("DETECTION", header: true);
            Switch("Moderator Detection", () => SafetyConfig.ModeratorDetectorEnabled, v => { SafetyConfig.ModeratorDetectorEnabled = v; SafetyConfig.Save(); });
            Switch("Anti-Content Creator", () => SafetyConfig.AntiContentCreatorEnabled, v => { SafetyConfig.AntiContentCreatorEnabled = v; SafetyConfig.Save(); });
            Switch("Cosmetic Notifications", () => SafetyConfig.CosmeticNotificationsEnabled, v => { SafetyConfig.CosmeticNotificationsEnabled = v; SafetyConfig.Save(); });
            Section("BEHAVIOR", header: true);
            Switch("Automod Bypass", () => SafetyConfig.AutomodBypassEnabled, v => { SafetyConfig.AutomodBypassEnabled = v; SafetyConfig.Save(); });
            Switch("Anti-Predictions", () => SafetyConfig.AntiPredictionsEnabled, v =>
            {
                SafetyConfig.AntiPredictionsEnabled = v; SafetyConfig.Save();
                if (!v) Patches.AntiPredictions.Reset();
            });
            Switch("Anti-Lurker", () => SafetyConfig.AntiLurkerEnabled, v => { SafetyConfig.AntiLurkerEnabled = v; SafetyConfig.Save(); });
            Section("FAKE BEHAVIORS", header: true);
            Switch("Fake Oculus Menu", () => SafetyConfig.FakeOculusMenuEnabled, v => { SafetyConfig.FakeOculusMenuEnabled = v; SafetyConfig.Save(); });
            Switch("Fake Broken Controller", () => SafetyConfig.FakeBrokenControllerEnabled, v => { SafetyConfig.FakeBrokenControllerEnabled = v; SafetyConfig.Save(); });
            Switch("Fake Report Menu", () => SafetyConfig.FakeReportMenuEnabled, v => { SafetyConfig.FakeReportMenuEnabled = v; SafetyConfig.Save(); });
            Switch("Fake Valve Tracking", () => SafetyConfig.FakeValveTrackingEnabled, v => { SafetyConfig.FakeValveTrackingEnabled = v; SafetyConfig.Save(); });
        }

        private void BuildIdentityPage()
        {
            Section("NAME", header: true);
            Switch("Identity Change", () => SafetyConfig.IdentityChangeEnabled, v =>
            {
                SafetyConfig.IdentityChangeEnabled = v; SafetyConfig.Save();
                if (v) IdentityChanger.ApplyRandomName();
            });
            Switch("Change on Disconnect", () => SafetyConfig.ChangeIdentityOnDisconnect, v => { SafetyConfig.ChangeIdentityOnDisconnect = v; SafetyConfig.Save(); });
            Switch("Randomize Color", () => SafetyConfig.ColorChangeEnabled, v => { SafetyConfig.ColorChangeEnabled = v; SafetyConfig.Save(); });
            Trigger("Generate Random Name", () => { SafetyConfig.CustomName = ""; IdentityChanger.ApplyRandomName(); });
            Section("SPOOFING", header: true);
            Switch("Spoof Support Page", () => SafetyConfig.SupportPageSpoofEnabled, v => { SafetyConfig.SupportPageSpoofEnabled = v; SafetyConfig.Save(); });
            Switch("Spoof FPS", () => SafetyConfig.FPSSpoofEnabled, v => { SafetyConfig.FPSSpoofEnabled = v; SafetyConfig.Save(); });
            Switch("Ranked Spoof", () => SafetyConfig.RankedSpoofEnabled, v => { SafetyConfig.RankedSpoofEnabled = v; SafetyConfig.Save(); });
            Switch("TOS/Age Bypass", () => SafetyConfig.TOSBypassEnabled, v => { SafetyConfig.TOSBypassEnabled = v; SafetyConfig.Save(); });
            Switch("Anti-Name Ban", () => SafetyConfig.AntiNameBanEnabled, v => { SafetyConfig.AntiNameBanEnabled = v; SafetyConfig.Save(); });
        }

        private void BuildExtraPage()
        {
            Section("TOOLS", header: true);
            Trigger("Flush RPCs", () => Patches.RPCFlusher.Flush());
            Trigger("Fix Lobby", () => Patches.LobbyFixer.Fix());
            Trigger("Leave & Rejoin", () => Patches.LobbyFixer.Rejoin());
            Trigger("Copy Discord Link", () => GUIUtility.systemCopyBuffer = "https://discord.gg/rYSRrr8Bhy");
            Section("PERFORMANCE", header: true);
            Switch("Auto GC Collect", () => SafetyConfig.AutoGCEnabled, v => { SafetyConfig.AutoGCEnabled = v; SafetyConfig.Save(); });
            Switch("Error Logging", () => SafetyConfig.ErrorLoggingEnabled, v => { SafetyConfig.ErrorLoggingEnabled = v; SafetyConfig.Save(); });
            Section("MENU DETECTION", header: true);
            Switch("Detect Other Menus", () => SafetyConfig.MenuDetectionEnabled, v => { SafetyConfig.MenuDetectionEnabled = v; SafetyConfig.Save(); });
            Switch("Alert on Detection", () => SafetyConfig.MenuDetectionAlertEnabled, v => { SafetyConfig.MenuDetectionAlertEnabled = v; SafetyConfig.Save(); });
            Switch("Auto-Override Patches", () => SafetyConfig.AutoOverrideOnDetection, v => { SafetyConfig.AutoOverrideOnDetection = v; SafetyConfig.Save(); });
            Trigger("Force Scan Now", () =>
            {
                var harmony = new HarmonyLib.Harmony("org.signal.safety.menu");
                MenuDetector.FullScan(harmony);
            });

            if (MenuDetector.ScanComplete)
            {
                string status = MenuDetector.MenuDetected
                    ? $"DETECTED: {MenuDetector.DetectedMenuName}"
                    : "No conflicts";
            Section(status, header: true);
            }
            Section("DANGER", header: true);
            Trigger("RESTART GORILLA TAG", () => Patches.GameRestarter.Restart());
        }

        private void BuildSettingsPage()
        {
            Section("THEME", header: true);
            Trigger($"Theme: {ThemeManager.PaletteName(SafetyConfig.ThemeIndex)} ?", () => { ThemeManager.StepPalette(true); });
            Trigger($"? Previous Theme", () => { ThemeManager.StepPalette(false); });
            Switch("Use Custom Colors", () => SafetyConfig.UseCustomTheme, v =>
            {
                SafetyConfig.UseCustomTheme = v;
                SafetyConfig.Save();
                if (v) ThemeManager.LoadUserPalette();
                else ThemeManager.LoadPalette(SafetyConfig.ThemeIndex);
                RebuildMenu();
            });
            Section("AUDIO CONTROL", header: true);
            Switch("Master Audio", () => SafetyConfig.AudioEnabled, v => { SafetyConfig.AudioEnabled = v; SafetyConfig.Save(); });
            Switch("Protection Sounds", () => SafetyConfig.PlayProtectionAudio, v => { SafetyConfig.PlayProtectionAudio = v; SafetyConfig.Save(); });
            Switch("Warning Sounds", () => SafetyConfig.PlayWarningAudio, v => { SafetyConfig.PlayWarningAudio = v; SafetyConfig.Save(); });
            Switch("Ban Alert Sounds", () => SafetyConfig.PlayBanAudio, v => { SafetyConfig.PlayBanAudio = v; SafetyConfig.Save(); });
            Switch("Menu Detection Sounds", () => SafetyConfig.PlayMenuDetectionAudio, v => { SafetyConfig.PlayMenuDetectionAudio = v; SafetyConfig.Save(); });
            Switch("Patch Override Sounds", () => SafetyConfig.PlayPatchOverrideAudio, v => { SafetyConfig.PlayPatchOverrideAudio = v; SafetyConfig.Save(); });
            Section("VOLUME LEVELS", header: true);
            Trigger($"Master Vol: {Mathf.RoundToInt(SafetyConfig.AudioVolume * 100)}%", () =>
            {
                SafetyConfig.AudioVolume = (SafetyConfig.AudioVolume >= 1f) ? 0.1f : SafetyConfig.AudioVolume + 0.1f;
                SafetyConfig.Save();
                SyncSlots();
            });
            Trigger($"Protection Vol: {Mathf.RoundToInt(SafetyConfig.ProtectionVolume * 100)}%", () =>
            {
                SafetyConfig.ProtectionVolume = (SafetyConfig.ProtectionVolume >= 1f) ? 0.1f : SafetyConfig.ProtectionVolume + 0.1f;
                SafetyConfig.Save();
                SyncSlots();
            });
            Trigger($"Warning Vol: {Mathf.RoundToInt(SafetyConfig.WarningVolume * 100)}%", () =>
            {
                SafetyConfig.WarningVolume = (SafetyConfig.WarningVolume >= 1f) ? 0.1f : SafetyConfig.WarningVolume + 0.1f;
                SafetyConfig.Save();
                SyncSlots();
            });
            Trigger($"Ban Alert Vol: {Mathf.RoundToInt(SafetyConfig.BanVolume * 100)}%", () =>
            {
                SafetyConfig.BanVolume = (SafetyConfig.BanVolume >= 1f) ? 0.1f : SafetyConfig.BanVolume + 0.1f;
                SafetyConfig.Save();
                SyncSlots();
            });
            Section("RESET", header: true);
            Trigger("Reset All Settings", () => { SafetyConfig.ResetToDefaults(); ThemeManager.LoadPalette(0); RebuildMenu(); });
        }

        private void BuildPatchesPage()
        {
            Section("REPORT PATCHES", header: true);
            Switch("SendReport Block", () => SafetyConfig.PatchSendReport, v => { SafetyConfig.PatchSendReport = v; SafetyConfig.Save(); });
            Switch("DispatchReport Block", () => SafetyConfig.PatchDispatchReport, v => { SafetyConfig.PatchDispatchReport = v; SafetyConfig.Save(); });
            Switch("CheckReports Block", () => SafetyConfig.PatchCheckReports, v => { SafetyConfig.PatchCheckReports = v; SafetyConfig.Save(); });
            Switch("PlayFab Report Block", () => SafetyConfig.PatchPlayFabReport, v => { SafetyConfig.PatchPlayFabReport = v; SafetyConfig.Save(); });
            Section("TELEMETRY PATCHES", header: true);
            Switch("Telemetry Block", () => SafetyConfig.PatchTelemetry, v => { SafetyConfig.PatchTelemetry = v; SafetyConfig.Save(); });
            Switch("BadName Check Block", () => SafetyConfig.PatchBadNameCheck, v => { SafetyConfig.PatchBadNameCheck = v; SafetyConfig.Save(); });
            Switch("AutoBan List Block", () => SafetyConfig.PatchAutoBanList, v => { SafetyConfig.PatchAutoBanList = v; SafetyConfig.Save(); });
            Section("PROTECTION PATCHES", header: true);
            Switch("CloseInvalidRoom Block", () => SafetyConfig.PatchCloseInvalidRoom, v => { SafetyConfig.PatchCloseInvalidRoom = v; SafetyConfig.Save(); });
            Switch("Grace Period Patch", () => SafetyConfig.PatchGracePeriod, v => { SafetyConfig.PatchGracePeriod = v; SafetyConfig.Save(); });
            Switch("RPC Limits Patch", () => SafetyConfig.PatchRPCLimits, v => { SafetyConfig.PatchRPCLimits = v; SafetyConfig.Save(); });
            Switch("QuitDelay Block", () => SafetyConfig.PatchQuitDelay, v => { SafetyConfig.PatchQuitDelay = v; SafetyConfig.Save(); });
            Switch("Mod Checker Block", () => SafetyConfig.PatchModCheckers, v => { SafetyConfig.PatchModCheckers = v; SafetyConfig.Save(); });
            Section("UI PATCHES", header: true);
            Switch("Ban Detection", () => SafetyConfig.PatchBanDetection, v => { SafetyConfig.PatchBanDetection = v; SafetyConfig.Save(); });
            Switch("Failure Text Override", () => SafetyConfig.PatchFailureText, v => { SafetyConfig.PatchFailureText = v; SafetyConfig.Save(); });
            Section("OVERRIDE ALL", header: true);
            Trigger("Enable ALL Patches", () =>
            {
                SafetyConfig.PatchSendReport = true;
                SafetyConfig.PatchDispatchReport = true;
                SafetyConfig.PatchCheckReports = true;
                SafetyConfig.PatchTelemetry = true;
                SafetyConfig.PatchPlayFabReport = true;
                SafetyConfig.PatchCloseInvalidRoom = true;
                SafetyConfig.PatchGracePeriod = true;
                SafetyConfig.PatchRPCLimits = true;
                SafetyConfig.PatchQuitDelay = true;
                SafetyConfig.PatchModCheckers = true;
                SafetyConfig.PatchBanDetection = true;
                SafetyConfig.PatchFailureText = true;
                SafetyConfig.PatchBadNameCheck = true;
                SafetyConfig.PatchAutoBanList = true;
                SafetyConfig.Save();
                SyncSlots();
            });
            Trigger("Disable ALL Patches", () =>
            {
                SafetyConfig.PatchSendReport = false;
                SafetyConfig.PatchDispatchReport = false;
                SafetyConfig.PatchCheckReports = false;
                SafetyConfig.PatchTelemetry = false;
                SafetyConfig.PatchPlayFabReport = false;
                SafetyConfig.PatchCloseInvalidRoom = false;
                SafetyConfig.PatchGracePeriod = false;
                SafetyConfig.PatchRPCLimits = false;
                SafetyConfig.PatchQuitDelay = false;
                SafetyConfig.PatchModCheckers = false;
                SafetyConfig.PatchBanDetection = false;
                SafetyConfig.PatchFailureText = false;
                SafetyConfig.PatchBadNameCheck = false;
                SafetyConfig.PatchAutoBanList = false;
                SafetyConfig.Save();
                SyncSlots();
            });
        }

        private void Section(string label, bool header = false)
        {
            _slots.Add(new PanelSlot { Label = label, IsHeader = header, IsToggle = false });
        }

        private void Switch(string label, Func<bool> get, Action<bool> set)
        {
            _slots.Add(new PanelSlot { Label = label, GetState = get, SetState = set, IsToggle = true });
        }

        private void Trigger(string label, Action action)
        {
            _slots.Add(new PanelSlot { Label = label, OnPress = action, IsToggle = false });
        }

        private void SyncSlots()
        {
            int startIdx = _pageOffset * _maxButtons;
            int totalEntries = _slots.Count;
            int pageCount = Mathf.CeilToInt((float)totalEntries / _maxButtons);

            for (int i = 0; i < _maxButtons; i++)
            {
                int entryIdx = startIdx + i;
                if (entryIdx < totalEntries)
                {
                    var entry = _slots[entryIdx];
                    _slotCubes[i].SetActive(true);

                    if (entry.IsHeader)
                    {
                        _slotLabels[i].text = $"<color=#{ColorUtility.ToHtmlStringRGB(AccentCyan)}>{entry.Label}</color>";
                        _slotLabels[i].fontSize = 0.007f;
                        _slotLabels[i].fontStyle = FontStyles.Bold;
                        _slotCubes[i].GetComponent<Renderer>().material.color = PanelColor;
                        _slotCubes[i].GetComponent<BoxCollider>().enabled = false;
                        var hbc = _slotCubes[i].GetComponent<TouchReactor>();
                        if (hbc != null) hbc.SetBaseColor(PanelColor);
                    }
                    else if (entry.IsToggle)
                    {
                        bool on = false;
                        try { on = entry.GetState(); } catch { }
                        string indicator = on ? $"<color=#{ColorUtility.ToHtmlStringRGB(AccentCyan)}>?</color>" : "<color=#555>?</color>";
                        _slotLabels[i].text = $"{indicator}  {entry.Label}";
                        _slotLabels[i].fontSize = 0.008f;
                        _slotLabels[i].fontStyle = FontStyles.Normal;
                        Color btnColor = on ? ButtonActive : ButtonIdle;
                        _slotCubes[i].GetComponent<Renderer>().material.color = btnColor;
                        _slotCubes[i].GetComponent<BoxCollider>().enabled = true;
                        var tbc = _slotCubes[i].GetComponent<TouchReactor>();
                        if (tbc != null) tbc.SetBaseColor(btnColor);
                    }
                    else
                    {
                        _slotLabels[i].text = $"?  {entry.Label}";
                        _slotLabels[i].fontSize = 0.008f;
                        _slotLabels[i].fontStyle = FontStyles.Normal;
                        _slotCubes[i].GetComponent<Renderer>().material.color = ButtonIdle;
                        _slotCubes[i].GetComponent<BoxCollider>().enabled = true;
                        var abc = _slotCubes[i].GetComponent<TouchReactor>();
                        if (abc != null) abc.SetBaseColor(ButtonIdle);
                    }
                }
                else
                {
                    _slotCubes[i].SetActive(false);
                }
            }

            bool multiPage = pageCount > 1;
            if (_prevPageBtn != null) _prevPageBtn.SetActive(multiPage);
            if (_nextPageBtn != null) _nextPageBtn.SetActive(multiPage);
            if (_prevPageText != null) _prevPageText.gameObject.SetActive(multiPage);
            if (_nextPageText != null) _nextPageText.gameObject.SetActive(multiPage);
            if (_pageNavText != null)
            {
                _pageNavText.gameObject.SetActive(multiPage);
                if (multiPage)
                    _pageNavText.text = $"{_pageOffset + 1}/{pageCount}";
            }
            _pageText.text = "";

            _statusText.text = Plugin.FailedPatches == 0 ? "? ACTIVE" : $"? {Plugin.FailedPatches} FAILED";
            _statusText.color = Plugin.FailedPatches == 0 ? AccentCyan : WarningYellow;

            if (UpdateChecker.UpdateAvailable && _updateBanner != null)
            {
                _updateBanner.SetActive(true);
                _updateText.text = $"? UPDATE v{UpdateChecker.LatestVersion} AVAILABLE";
            }

            for (int i = 0; i < _tabObjects.Count; i++)
            {
                Color tabColor = i == _currentTab ? TabSelected : TabIdle;
                _tabObjects[i].GetComponent<Renderer>().material.color = tabColor;
                var tabBC = _tabObjects[i].GetComponent<TouchReactor>();
                if (tabBC != null) tabBC.SetBaseColor(tabColor);
                _tabTexts[i].color = i == _currentTab ? TextWhite : TextDim;
                _tabTexts[i].fontStyle = i == _currentTab ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        private void OnSlotActivated(int slotIndex)
        {
            int entryIdx = _pageOffset * _maxButtons + slotIndex;
            if (entryIdx >= _slots.Count) return;

            var entry = _slots[entryIdx];
            if (entry.IsHeader) return;

            if (entry.IsToggle)
            {
                try
                {
                    bool current = entry.GetState();
                    entry.SetState(!current);
                }
                catch { }
            }
            else if (entry.OnPress != null)
            {
                try { entry.OnPress(); } catch { }
            }

            SyncSlots();
        }

        private void BuildPointer()
        {
            _pointer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _pointer.name = "SignalPointer";
            _pointer.transform.localScale = Vector3.one * PointerRadius * 2f;
            DontDestroyOnLoad(_pointer);

            _pointerCollider = _pointer.GetComponent<SphereCollider>();
            _pointerCollider.isTrigger = true;

            var rb = _pointer.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            _pointer.GetComponent<Renderer>().material = MakeMat(PointerColor);

            TouchReactor.Probe = _pointerCollider;
        }

        private void PositionMenu()
        {
            if (_menuRoot == null) return;

            try
            {
                var leftHand = GorillaTagger.Instance?.leftHandTransform;
                if (leftHand == null) return;

                Vector3 target = leftHand.position + leftHand.up * 0.12f + leftHand.forward * 0.02f;
                _menuRoot.transform.position = Vector3.Lerp(_menuRoot.transform.position, target, Time.deltaTime * 12f);

                var head = GorillaTagger.Instance?.headCollider?.transform;
                if (head != null)
                {
                    Vector3 lookDir = head.position - _menuRoot.transform.position;
                    if (lookDir.sqrMagnitude > 0.001f)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(-lookDir.normalized, Vector3.up);
                        _menuRoot.transform.rotation = Quaternion.Slerp(_menuRoot.transform.rotation, targetRot, Time.deltaTime * 10f);
                    }
                }
            }
            catch { }
        }

        private void PositionPointer()
        {
            if (_pointer == null) return;

            try
            {
                var rightHand = GorillaTagger.Instance?.rightHandTransform;
                if (rightHand == null) return;

                _pointer.transform.position = rightHand.position + rightHand.forward * 0.06f + rightHand.up * 0.01f;
            }
            catch { }
        }

        private void SyncDynamicUI()
        {
            try
            {
                float stickY = 0f;
                try { stickY = ControllerInputPoller.instance.rightControllerPrimary2DAxis.y; } catch { }

                if (Mathf.Abs(stickY) > 0.7f && Time.time > _scrollCooldown)
                {
                    ChangePage(stickY < 0 ? 1 : -1);
                    _scrollCooldown = Time.time + 0.3f;
                }
            }
            catch { }
        }

        public void RebuildMenu()
        {
            if (!IsOpen) return;
            BuildMenu();
            if (_menuRoot != null)
                _menuRoot.transform.localScale = _targetScale;
            if (_pointer != null) _pointer.SetActive(true);
        }

        private void DestroyMenu()
        {
            if (_menuRoot != null)
            {
                Destroy(_menuRoot);
                _menuRoot = null;
            }
            _slotCubes.Clear();
            _slotLabels.Clear();
            _tabObjects.Clear();
            _tabTexts.Clear();
        }

        private TextMeshPro CreateTMP(Transform parent, string text, float fontSize, Color color,
            TextAlignmentOptions align, FontStyles style)
        {
            var obj = new GameObject("TMP");
            obj.transform.SetParent(parent, false);

            var tmp = obj.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = style;
            tmp.enableAutoSizing = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.richText = true;

            var rt = tmp.rectTransform;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;

            return tmp;
        }
    }
}
