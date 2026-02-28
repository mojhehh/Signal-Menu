using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SignalSafetyMenu
{
    public class Popup3D : MonoBehaviour
    {
        public static Popup3D Instance;

        private GameObject _root;
        private Action _onYes;
        private Action _onNo;
        private bool _active;

        private static TMP_FontAsset _font;

        public static bool IsActive => Instance != null && Instance._active;

        public static void Show(string message, Action onYes, Action onNo = null)
        {
            if (Instance == null) return;
            Instance.CreatePopup(message, onYes, onNo);
        }

        public static void Dismiss()
        {
            if (Instance == null) return;
            Instance.DestroyPopup();
        }

        void Awake()
        {
            Instance = this;
        }

        void Update()
        {
            if (!_active || _root == null) return;
            Recenter();
        }

        private void Recenter()
        {
            Transform head = null;
            try { head = GorillaTagger.Instance.headCollider?.transform; } catch { }
            if (head == null || _root == null) return;

            float s = 1f;
            try { s = GorillaLocomotion.GTPlayer.Instance.scale; } catch { }

            Vector3 fwd = head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();

            _root.transform.position = head.position + fwd * 1.2f * s + Vector3.up * 0.1f * s;
            _root.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            _root.transform.localScale = Vector3.one * 0.4f * s;
        }

        private void GrabFont()
        {
            if (_font != null) return;
            try { _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF"); } catch { }
            if (_font != null) return;
            try
            {
                foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                    if (f != null) { _font = f; return; }
            }
            catch { }
        }

        private void CreatePopup(string message, Action onYes, Action onNo)
        {
            DestroyPopup();
            GrabFont();

            _onYes = onYes;
            _onNo = onNo;

            var th = ThemeManager.CurrentTheme;

            _root = new GameObject("Popup3D_Root");
            DontDestroyOnLoad(_root);

            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(bg.GetComponent<BoxCollider>());
            bg.transform.SetParent(_root.transform, false);
            bg.transform.localScale = new Vector3(1.6f, 0.9f, 0.02f);
            bg.transform.localPosition = Vector3.zero;
            Paint(bg, new Color(th.PanelColor.r * 0.8f, th.PanelColor.g * 0.8f, th.PanelColor.b * 0.8f, 1f));

            var border = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(border.GetComponent<BoxCollider>());
            border.transform.SetParent(_root.transform, false);
            border.transform.localScale = new Vector3(1.65f, 0.95f, 0.015f);
            border.transform.localPosition = new Vector3(0f, 0f, 0.003f);
            Paint(border, th.AccentColor);

            var canvas = new GameObject("PopupCanvas");
            canvas.transform.SetParent(_root.transform, false);
            var c = canvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            canvas.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2500f;
            canvas.AddComponent<GraphicRaycaster>();

            var titleTmp = new GameObject("Title").AddComponent<TextMeshPro>();
            titleTmp.transform.SetParent(canvas.transform, false);
            if (_font != null) titleTmp.font = _font;
            titleTmp.text = "Anti-Ban Tutorial";
            titleTmp.fontSize = 3f;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = th.AccentColor;
            titleTmp.enableAutoSizing = false;
            var titleRt = titleTmp.GetComponent<RectTransform>();
            titleRt.sizeDelta = new Vector2(1.4f, 0.15f);
            titleRt.localPosition = new Vector3(0f, 0.25f, -0.015f);
            titleRt.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

            var msgTmp = new GameObject("Message").AddComponent<TextMeshPro>();
            msgTmp.transform.SetParent(canvas.transform, false);
            if (_font != null) msgTmp.font = _font;
            msgTmp.text = message;
            msgTmp.fontSize = 2f;
            msgTmp.alignment = TextAlignmentOptions.Center;
            msgTmp.fontStyle = FontStyles.Normal;
            msgTmp.color = th.TextPrimary;
            msgTmp.enableAutoSizing = true;
            msgTmp.fontSizeMin = 1f;
            msgTmp.fontSizeMax = 2.5f;
            msgTmp.textWrappingMode = TextWrappingModes.Normal;
            var msgRt = msgTmp.GetComponent<RectTransform>();
            msgRt.sizeDelta = new Vector2(1.3f, 0.3f);
            msgRt.localPosition = new Vector3(0f, 0.02f, -0.015f);
            msgRt.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

            MakePopupButton("Yes", new Vector3(-0.35f, -0.28f, 0f), th.ButtonActive, th.TextPrimary, () =>
            {
                _onYes?.Invoke();
                DestroyPopup();
            });

            MakePopupButton("No", new Vector3(0.35f, -0.28f, 0f), th.ButtonIdle, th.TextDim, () =>
            {
                _onNo?.Invoke();
                DestroyPopup();
            });

            _active = true;
        }

        private void MakePopupButton(string label, Vector3 localPos, Color btnColor, Color txtColor, Action onPress)
        {
            var btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            btn.GetComponent<BoxCollider>().isTrigger = true;
            btn.transform.SetParent(_root.transform, false);
            btn.transform.localPosition = localPos;
            btn.transform.localScale = new Vector3(0.5f, 0.18f, 0.03f);
            Paint(btn, btnColor);

            var tr = btn.AddComponent<TouchReactor>();
            tr.Init(onPress, false);
            tr.SetBaseColor(btnColor);

            var canvas = new GameObject("BtnCanvas");
            canvas.transform.SetParent(btn.transform, false);
            var c = canvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            canvas.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2500f;

            var tmp = new GameObject("BtnLabel").AddComponent<TextMeshPro>();
            tmp.transform.SetParent(canvas.transform, false);
            if (_font != null) tmp.font = _font;
            tmp.text = label;
            tmp.fontSize = 3f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = txtColor;
            tmp.enableAutoSizing = false;
            var rt = tmp.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0.4f, 0.12f);
            rt.localPosition = new Vector3(0f, 0f, -0.6f);
            rt.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        }

        private void DestroyPopup()
        {
            if (_root != null) Destroy(_root);
            _root = null;
            _active = false;
            _onYes = null;
            _onNo = null;
        }

        private static void Paint(GameObject obj, Color c)
        {
            var r = obj.GetComponent<Renderer>();
            if (r == null) return;
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh != null) r.material = new Material(sh);
            r.material.color = c;
        }

        void OnDestroy()
        {
            DestroyPopup();
            if (Instance == this) Instance = null;
        }
    }
}
