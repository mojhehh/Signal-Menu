using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace SignalSafetyMenu
{
    public class Menu3D : MonoBehaviour
    {
        public static Menu3D Instance;
        public static bool IsOpen;

        private GameObject _root;
        private GameObject _pointer;

        private int _page;
        private int _pageCount;
        private int _catOfPage;
        private bool _lastBtn;

        private static TMP_FontAsset _font;

        private const int PAGE_SIZE = 6;
        private const int BTN_OFF = 2;
        private const float BD = 0.1f;

        private static readonly string[] CAT = { "Main", "Advanced", "Identity", "Audio", "Extra" };

        private struct Entry
        {
            public string Label;
            public Action Press;
            public Func<bool> IsOn;
            public bool Toggle;
            public int Cat;
        }

        void Awake()
        {
            Instance = this;
            IsOpen = false;
        }

        static void GrabFont()
        {
            if (_font != null) return;
            try
            {
                var f = TMP_Settings.defaultFontAsset;
                if (f != null) { _font = f; return; }
            }
            catch { }
            try
            {
                foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                    if (f != null) { _font = f; return; }
            }
            catch { }
        }

        void Update()
        {
            if (_font == null) GrabFont();

            bool btn = false;
            try { btn = ControllerInputPoller.instance != null && ControllerInputPoller.instance.leftControllerSecondaryButton; }
            catch { }

            if (btn && !_lastBtn)
            {
                if (IsOpen) DoClose(); else DoOpen();
            }
            _lastBtn = btn;

            if (IsOpen && _root != null) Recenter();
            ThemeManager.TickRainbow();
        }

        private void DoOpen()
        {
            Build();
            BuildPointer();
            IsOpen = true;
        }

        private void DoClose()
        {
            if (_root != null) Destroy(_root);
            if (_pointer != null) Destroy(_pointer);
            _root = null;
            _pointer = null;
            IsOpen = false;
        }

        private void Recenter()
        {
            Transform h = null;
            try { h = GorillaTagger.Instance.rightHandTransform; } catch { }
            if (h == null || _root == null) return;

            float s = 1f;
            try { s = GorillaLocomotion.GTPlayer.Instance.scale; } catch { }

            _root.transform.position = h.position;
            Vector3 e = h.rotation.eulerAngles + new Vector3(0f, 0f, 180f);
            _root.transform.rotation = Quaternion.Euler(e);
            _root.transform.localScale = new Vector3(0.1f, 0.3f, 0.3825f) * s;
        }

        private void Build()
        {
            if (_root != null) Destroy(_root);

            var th = ThemeManager.CurrentTheme;
            var entries = AllEntries();
            _pageCount = Mathf.Max(1, Mathf.CeilToInt((float)entries.Count / PAGE_SIZE));
            if (_page >= _pageCount) _page = 0;
            int start = _page * PAGE_SIZE;
            int cnt = Mathf.Min(PAGE_SIZE, entries.Count - start);
            _catOfPage = cnt > 0 ? entries[start].Cat : 0;

            _root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(_root.GetComponent<BoxCollider>());
            UnityEngine.Object.Destroy(_root.GetComponent<Renderer>());

            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(bg.GetComponent<BoxCollider>());
            bg.transform.SetParent(_root.transform, false);
            bg.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            bg.transform.localScale = new Vector3(0.1f, 1.5f, 1f);
            Paint(bg, th.PanelColor);

            string title = "Signal / " + CAT[Mathf.Clamp(_catOfPage, 0, 4)] + "  (" + (_page + 1) + "/" + _pageCount + ")";
            float titleZ = 0.28f + BD * 1.5f;
            MakeLabel(_root.transform, title, new Vector3(0.62f, 0f, titleZ), new Vector2(0.28f, 0.04f), th.AccentColor);

            MakeButton(_root.transform, 0, "< Prev", th.ButtonIdle, th.TextPrimary, () =>
            {
                _page--;
                if (_page < 0) _page = _pageCount - 1;
                RebuildMenu();
            });
            MakeButton(_root.transform, 1, "Next >", th.ButtonIdle, th.TextPrimary, () =>
            {
                _page++;
                if (_page >= _pageCount) _page = 0;
                RebuildMenu();
            });

            for (int i = 0; i < cnt; i++)
            {
                var en = entries[start + i];
                int slot = i + BTN_OFF;
                bool on = en.Toggle && en.IsOn != null && en.IsOn();
                Color bc = on ? th.ButtonActive : th.ButtonIdle;
                Color tc = on ? th.TextPrimary : th.TextDim;
                string lbl = en.Label;
                if (en.Toggle && en.IsOn != null)
                    lbl = (on ? "[ON] " : "[OFF] ") + lbl;
                var cap = en;
                MakeButton(_root.transform, slot, lbl, bc, tc, () =>
                {
                    try { cap.Press?.Invoke(); } catch { }
                    RebuildMenu();
                });
            }
        }

        private void MakeButton(Transform root, int slot, string label, Color btnCol, Color txtCol, Action onPress)
        {
            float z = 0.28f - BD * slot;

            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.GetComponent<BoxCollider>().isTrigger = true;
            b.transform.SetParent(root, false);
            b.transform.localRotation = Quaternion.identity;
            b.transform.localScale = new Vector3(0.09f, 1.3f, BD * 0.8f);
            b.transform.localPosition = new Vector3(0.56f, 0f, z);
            Paint(b, btnCol);

            var tr = b.AddComponent<TouchReactor>();
            tr.Init(onPress, false);
            tr.SetBaseColor(btnCol);

            MakeLabel(root, label, new Vector3(0.62f, 0f, z), new Vector2(0.25f, BD * 0.7f), txtCol);
        }

        private void MakeLabel(Transform parent, string text, Vector3 pos, Vector2 size, Color color)
        {
            var go = new GameObject("Lbl");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            var t = go.AddComponent<TextMesh>();
            t.text = text;
            t.characterSize = 0.1f;
            t.fontSize = 40;
            t.color = color;
            t.alignment = TextAlignment.Center;
            t.anchor = TextAnchor.MiddleCenter;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sortingOrder = 10000;
                renderer.material = new Material(Shader.Find("GUI/Text Shader"));
            }
        }

        private void BuildPointer()
        {
            if (_pointer != null) Destroy(_pointer);

            Transform h = null;
            try { h = GorillaTagger.Instance.leftHandTransform; } catch { }

            _pointer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (h != null)
                _pointer.transform.SetParent(h, false);
            _pointer.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            _pointer.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            var rb = _pointer.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            _pointer.GetComponent<SphereCollider>().isTrigger = true;
            TouchReactor.Probe = _pointer.GetComponent<SphereCollider>();
            Paint(_pointer, ThemeManager.CurrentTheme.PointerColor);
        }

        public void RebuildMenu()
        {
            if (!IsOpen) return;
            Build();
        }

        private static void Paint(GameObject obj, Color c)
        {
            var r = obj.GetComponent<Renderer>();
            if (r == null) return;

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh != null)
                r.material = new Material(sh);
            r.material.color = c;
        }

        private List<Entry> AllEntries()
        {
            var L = new List<Entry>();

            L.Add(Tog("Anti-Report", 0, () => SafetyConfig.AntiReportEnabled, v => { SafetyConfig.AntiReportEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Block Telemetry", 0, () => SafetyConfig.TelemetryBlockEnabled, v => { SafetyConfig.TelemetryBlockEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Block PlayFab Reports", 0, () => SafetyConfig.PlayFabBlockEnabled, v => { SafetyConfig.PlayFabBlockEnabled = v; SafetyConfig.Save(); }));
            L.Add(new Entry { Label = "Theme: " + ThemeManager.CurrentTheme.Name, Press = () => ThemeManager.StepPalette(), Cat = 0 });

            L.Add(Tog("Device Spoofing", 1, () => SafetyConfig.DeviceSpoofEnabled, v => { SafetyConfig.DeviceSpoofEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Network Event Block", 1, () => SafetyConfig.NetworkEventBlockEnabled, v => { SafetyConfig.NetworkEventBlockEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("RPC Limit Bypass", 1, () => SafetyConfig.RPCLimitBypassEnabled, v => { SafetyConfig.RPCLimitBypassEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Grace Period Bypass", 1, () => SafetyConfig.GraceBypassEnabled, v => { SafetyConfig.GraceBypassEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("KID Bypass", 1, () => SafetyConfig.KIDBypassEnabled, v => { SafetyConfig.KIDBypassEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Name Ban Bypass", 1, () => SafetyConfig.NameBanBypassEnabled, v => { SafetyConfig.NameBanBypassEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Core Property Filter", 1, () => SafetyConfig.CoreProtectionEnabled, v => { SafetyConfig.CoreProtectionEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Error Logging", 1, () => SafetyConfig.ErrorLoggingEnabled, v => { SafetyConfig.ErrorLoggingEnabled = v; SafetyConfig.Save(); }));
            L.Add(new Entry { Label = "Reset All Defaults", Press = () => SafetyConfig.ResetToDefaults(), Cat = 1 });

            L.Add(Tog("Identity Change", 2, () => SafetyConfig.IdentityChangeEnabled, v => { SafetyConfig.IdentityChangeEnabled = v; SafetyConfig.Save(); if (v) IdentityChanger.ApplyRandomName(); }));
            L.Add(new Entry { Label = "Random Name", Press = () => { SafetyConfig.CustomName = ""; IdentityChanger.ApplyRandomName(); }, Cat = 2 });
            L.Add(Tog("Smart Mode", 2, () => AntiReport.SmartMode, v => { AntiReport.SmartMode = v; SafetyConfig.Save(); }));
            L.Add(Tog("Detection Zones", 2, () => AntiReport.VisualizerEnabled, v => { AntiReport.VisualizerEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Detect Mute Btn", 2, () => AntiReport.AntiMute, v => { AntiReport.AntiMute = v; SafetyConfig.Save(); }));
            L.Add(new Entry { Label = "Range: " + AntiReport.RangeName, Press = () => { AntiReport.CycleRange(); SafetyConfig.Save(); }, Cat = 2 });
            {
                string[] mn = { "Disconnect", "Reconnect", "Notify" };
                string m = mn[Mathf.Clamp(SafetyConfig.AntiReportMode, 0, mn.Length - 1)];
                L.Add(new Entry { Label = "Mode: " + m, Press = () => { SafetyConfig.AntiReportMode = (SafetyConfig.AntiReportMode + 1) % 3; SafetyConfig.Save(); }, Cat = 2 });
            }

            L.Add(Tog("All Audio", 3, () => SafetyConfig.AudioEnabled, v => { SafetyConfig.AudioEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Protection Sounds", 3, () => SafetyConfig.PlayProtectionAudio, v => { SafetyConfig.PlayProtectionAudio = v; SafetyConfig.Save(); }));
            L.Add(Tog("Warning Sounds", 3, () => SafetyConfig.PlayWarningAudio, v => { SafetyConfig.PlayWarningAudio = v; SafetyConfig.Save(); }));
            L.Add(Tog("Ban Detect Sounds", 3, () => SafetyConfig.PlayBanAudio, v => { SafetyConfig.PlayBanAudio = v; SafetyConfig.Save(); }));

            L.Add(Tog("Moderator Detect", 4, () => SafetyConfig.ModeratorDetectorEnabled, v => { SafetyConfig.ModeratorDetectorEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Anti-Creator", 4, () => SafetyConfig.AntiContentCreatorEnabled, v => { SafetyConfig.AntiContentCreatorEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Cosmetic Notifs", 4, () => SafetyConfig.CosmeticNotificationsEnabled, v => { SafetyConfig.CosmeticNotificationsEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Automod Bypass", 4, () => SafetyConfig.AutomodBypassEnabled, v => { SafetyConfig.AutomodBypassEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Anti-Predictions", 4, () => SafetyConfig.AntiPredictionsEnabled, v => { SafetyConfig.AntiPredictionsEnabled = v; SafetyConfig.Save(); if (!v) Patches.AntiPredictions.Reset(); }));
            L.Add(Tog("Anti-Lurker", 4, () => SafetyConfig.AntiLurkerEnabled, v => { SafetyConfig.AntiLurkerEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Fake Oculus Menu", 4, () => SafetyConfig.FakeOculusMenuEnabled, v => { SafetyConfig.FakeOculusMenuEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Fake Broken Ctrl", 4, () => SafetyConfig.FakeBrokenControllerEnabled, v => { SafetyConfig.FakeBrokenControllerEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Fake Report Menu", 4, () => SafetyConfig.FakeReportMenuEnabled, v => { SafetyConfig.FakeReportMenuEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Fake Valve Track", 4, () => SafetyConfig.FakeValveTrackingEnabled, v => { SafetyConfig.FakeValveTrackingEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Anti-Crash", 4, () => SafetyConfig.AntiCrashEnabled, v => { SafetyConfig.AntiCrashEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Anti-Kick", 4, () => SafetyConfig.AntiKickEnabled, v =>
            {
                SafetyConfig.AntiKickEnabled = v; SafetyConfig.Save();
                if (v) Patches.AntiKickHelper.Enable(); else Patches.AntiKickHelper.Disable();
            }));
            L.Add(Tog("Show AC Reports", 4, () => SafetyConfig.ShowACReportsEnabled, v => { SafetyConfig.ShowACReportsEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Auto GC", 4, () => SafetyConfig.AutoGCEnabled, v => { SafetyConfig.AutoGCEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Spoof Support", 4, () => SafetyConfig.SupportPageSpoofEnabled, v => { SafetyConfig.SupportPageSpoofEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Ranked Spoof", 4, () => SafetyConfig.RankedSpoofEnabled, v => { SafetyConfig.RankedSpoofEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Name on DC", 4, () => SafetyConfig.ChangeIdentityOnDisconnect, v => { SafetyConfig.ChangeIdentityOnDisconnect = v; SafetyConfig.Save(); }));
            L.Add(Tog("Random Color", 4, () => SafetyConfig.ColorChangeEnabled, v => { SafetyConfig.ColorChangeEnabled = v; SafetyConfig.Save(); }));
            L.Add(new Entry { Label = "Flush RPCs", Press = () => Patches.RPCFlusher.Flush(), Cat = 4 });
            L.Add(new Entry { Label = "Fix Lobby", Press = () => Patches.LobbyFixer.Fix(), Cat = 4 });
            L.Add(new Entry { Label = "Rejoin Lobby", Press = () => Patches.LobbyFixer.Rejoin(), Cat = 4 });
            L.Add(new Entry { Label = "RESTART", Press = () => Patches.GameRestarter.Restart(), Cat = 4 });
            L.Add(Tog("FPS Spoof", 4, () => SafetyConfig.FPSSpoofEnabled, v => { SafetyConfig.FPSSpoofEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("TOS/Age Bypass", 4, () => SafetyConfig.TOSBypassEnabled, v => { SafetyConfig.TOSBypassEnabled = v; SafetyConfig.Save(); }));
            L.Add(Tog("Anti-Name Ban", 4, () => SafetyConfig.AntiNameBanEnabled, v => { SafetyConfig.AntiNameBanEnabled = v; SafetyConfig.Save(); }));

            return L;
        }

        private static Entry Tog(string label, int cat, Func<bool> get, Action<bool> set)
        {
            return new Entry
            {
                Label = label,
                Press = () => set(!get()),
                IsOn = get,
                Toggle = true,
                Cat = cat,
            };
        }

        void OnDestroy()
        {
            DoClose();
            if (Instance == this) Instance = null;
        }
    }
}
