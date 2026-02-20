using UnityEngine;
using System;

namespace SignalSafetyMenu
{
    public class TouchReactor : MonoBehaviour
    {
        public static SphereCollider Probe;

        private Action _onActivate;
        private bool _isSwitch;
        private float _lastTap;
        private Renderer _rend;
        private Color _baseColor;
        private bool _gazing;

        private static readonly Color GazeHighlight = new Color(0.18f, 0.3f, 0.45f, 0.95f);
        private static readonly Color TapFlash = new Color(0.3f, 0.85f, 1f, 1f);
        private const float TapGate = 0.2f;
        private const float GazeFadeRate = 10f;

        private const int TapMaterialID = 67;
        private const float TapGain = 0.05f;

        public void Init(Action onActivate, bool isSwitch)
        {
            _onActivate = onActivate;
            _isSwitch = isSwitch;
            _rend = GetComponent<Renderer>();
            if (_rend != null)
                _baseColor = _rend.material.color;
        }

        void Update()
        {
            if (_rend == null || _rend.material == null) return;

            Color target = _gazing ? GazeHighlight : _baseColor;
            _rend.material.color = Color.Lerp(_rend.material.color, target, Time.deltaTime * GazeFadeRate);
            _gazing = false;
        }

        void OnTriggerStay(Collider other)
        {
            if (Probe == null || other != Probe) return;
            _gazing = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (Probe == null || other != Probe) return;
            if (Time.time - _lastTap < TapGate) return;
            _lastTap = Time.time;

            try
            {
                GorillaTagger.Instance?.offlineVRRig?.PlayHandTapLocal(TapMaterialID, false, TapGain);
            }
            catch { }

            try
            {
                GorillaTagger.Instance?.StartVibration(false, 0.15f, 0.04f);
            }
            catch { }

            if (_rend != null)
                _rend.material.color = TapFlash;

            try { _onActivate?.Invoke(); } catch { }
        }

        public void SetBaseColor(Color c)
        {
            _baseColor = c;
        }
    }
}
