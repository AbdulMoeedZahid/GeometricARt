using GeoART.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GeoART.App
{
    /// <summary>
    /// The colour wheel (decision 7): a full-spectrum picker behind the custom swatch at the end of the palette.
    ///   Custom swatch   a rainbow ring around the last picked colour (grey until one is picked). Tap: selects that
    ///                   colour when it is not the current one; otherwise (or with none yet) opens the wheel
    ///   Wheel popup     a hue/saturation disc (hue = angle, saturation = distance from the centre), a brightness slider,
    ///                   a preview and "Use colour". Dragging changes only the preview; Use makes it the current colour,
    ///                   restyles the selection if there is one (one "Style" step), keeps it as the custom swatch
    ///                   (PlayerPrefs, Play mode only) and closes. A touch outside closes without a change; the popup's
    ///                   full-screen blocker closes on the touch's RELEASE, so the whole touch stays on UI and never draws.
    /// The popup has its own canvas (sorting order 115) above the tool bar and the layer sidebar.
    /// </summary>
    public sealed partial class ColorBar : ILayerPointerTarget
    {
        const string CustomColorKey = "GeoART.CustomColor";
        const int WheelBlockerTag = -1, WheelTag = -2;
        const int WheelPx = 192, RingPx = 64;
        const float PanelW = 260f, PanelH = 330f, WheelSize = 200f;
        static readonly Color NoCustom = new Color(0.35f, 0.35f, 0.38f, 1f);

        Canvas _wheelCanvas;
        RectTransform _wheelPanel, _wheelRect, _wheelMarker;
        RawImage _wheelImage;
        GameObject _wheelBlocker;
        Slider _brightness;
        Image _preview, _customRing, _customFill;
        Texture2D _wheelTex, _ringTex;
        float _hue, _sat = 1f, _val = 1f;
        bool _hasCustom;
        RgbaColor _custom;

        /// <summary>True while the wheel popup is shown.</summary>
        public bool WheelOpen => _wheelPanel != null && _wheelPanel.gameObject.activeSelf;

        /// <summary>The colour kept on the custom swatch, or null.</summary>
        public RgbaColor? CustomColor => _hasCustom ? _custom : (RgbaColor?)null;

        /// <summary>The colour the wheel shows now (hue, saturation, brightness).</summary>
        public RgbaColor WheelColor
        {
            get
            {
                Color c = Color.HSVToRGB(_hue, _sat, _val);
                return new RgbaColor((byte)Mathf.RoundToInt(c.r * 255f), (byte)Mathf.RoundToInt(c.g * 255f),
                    (byte)Mathf.RoundToInt(c.b * 255f), 255);
            }
        }

        // ---------- custom swatch ----------

        void CreateCustomSwatch(float x)
        {
            _customRing = LayerUiKit.NewImage("Custom", _pill, Clear, true, Ring * 0.5f);
            LayerUiKit.Place(_customRing.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(x + SwatchSlot * 0.5f, 0f), new Vector2(Ring, Ring));
            var button = _customRing.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnCustomTap);

            _ringTex = MakeSpectrumTexture(RingPx, 0.72f);
            var spectrum = new GameObject("Spectrum", typeof(RectTransform), typeof(RawImage));
            var srt = (RectTransform)spectrum.transform;
            srt.SetParent(_customRing.transform, false);
            LayerUiKit.Place(srt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Swatch + 3f, Swatch + 3f));
            var raw = spectrum.GetComponent<RawImage>();
            raw.texture = _ringTex;
            raw.raycastTarget = false;

            _customFill = LayerUiKit.NewImage("Color", _customRing.transform, NoCustom, false, (Swatch - 10f) * 0.5f);
            LayerUiKit.Place(_customFill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(Swatch - 10f, Swatch - 10f));

            if (Application.isPlaying && PlayerPrefs.HasKey(CustomColorKey))
            {
                int v = PlayerPrefs.GetInt(CustomColorKey);
                _custom = new RgbaColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), 255);
                _hasCustom = true;
            }
        }

        void OnCustomTap()
        {
            if (_hasCustom && _session != null && !_custom.Equals(_session.Style.Color)) SelectColor(_custom);
            else OpenWheel();
        }

        void RefreshCustom(RgbaColor current)
        {
            if (_customRing == null) return;
            SetColor(_customRing, _hasCustom && _custom.Equals(current) ? Accent : Clear);
            SetColor(_customFill, _hasCustom ? ToColor(_custom) : NoCustom);
        }

        void SetCustom(RgbaColor c)
        {
            _hasCustom = true;
            _custom = c;
            if (Application.isPlaying)
            {
                PlayerPrefs.SetInt(CustomColorKey, (c.R << 24) | (c.G << 16) | (c.B << 8) | 255);
                PlayerPrefs.Save();
            }
        }

        // ---------- wheel popup ----------

        void BuildWheel()
        {
            _wheelCanvas = LayerUiKit.CreateCanvas(transform, "ColorWheel", 115, true);

            Image blocker = LayerUiKit.NewImage("WheelBlocker", _wheelCanvas.transform, Clear, true, 1f);
            blocker.sprite = null;
            blocker.type = Image.Type.Simple;
            blocker.rectTransform.anchorMin = Vector2.zero;
            blocker.rectTransform.anchorMax = Vector2.one;
            blocker.rectTransform.offsetMin = blocker.rectTransform.offsetMax = Vector2.zero;
            var relay = blocker.gameObject.AddComponent<LayerPointerRelay>();
            relay.Target = this;
            relay.Tag = WheelBlockerTag;
            _wheelBlocker = blocker.gameObject;

            _wheelPanel = LayerUiKit.NewImage("ColorWheelPopup", _wheelCanvas.transform, new Color(0.07f, 0.07f, 0.09f, 0.92f), true, 14f).rectTransform;
            LayerUiKit.Place(_wheelPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, PanelH));

            var title = LayerUiKit.NewText("Title", _wheelPanel, 14, TextAnchor.MiddleLeft, Color.white);
            title.fontStyle = FontStyle.Bold;
            title.text = "Colour";
            LayerUiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(120f, 22f));

            _wheelTex = MakeSpectrumTexture(WheelPx, 0f);
            var wheel = new GameObject("Wheel", typeof(RectTransform), typeof(RawImage));
            _wheelRect = (RectTransform)wheel.transform;
            _wheelRect.SetParent(_wheelPanel, false);
            LayerUiKit.Place(_wheelRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(WheelSize, WheelSize));
            _wheelImage = wheel.GetComponent<RawImage>();
            _wheelImage.texture = _wheelTex;
            _wheelImage.raycastTarget = true;
            var wheelRelay = wheel.AddComponent<LayerPointerRelay>();
            wheelRelay.Target = this;
            wheelRelay.Tag = WheelTag;

            Image marker = LayerUiKit.NewImage("Marker", _wheelRect, Color.white, false, 9f);
            _wheelMarker = marker.rectTransform;
            LayerUiKit.Place(_wheelMarker, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f));
            Image dot = LayerUiKit.NewImage("Dot", _wheelMarker, Color.black, false, 5f);
            LayerUiKit.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));

            _brightness = BuildBrightnessSlider(_wheelPanel);
            _brightness.onValueChanged.AddListener(v => { _val = v; SyncWheel(); });

            _preview = LayerUiKit.NewImage("Preview", _wheelPanel, Color.white, false, 8f);
            LayerUiKit.Place(_preview.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 12f), new Vector2(64f, 36f));

            Image use = LayerUiKit.NewImage("Use colour", _wheelPanel, Accent, true, 10f);
            LayerUiKit.Place(use.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 12f), new Vector2(150f, 36f));
            var useButton = use.gameObject.AddComponent<Button>();
            useButton.transition = Selectable.Transition.None;
            useButton.onClick.AddListener(UseWheelColor);
            var useLabel = LayerUiKit.NewText("Label", use.rectTransform, 14, TextAnchor.MiddleCenter, Color.white);
            useLabel.text = "Use colour";
            useLabel.rectTransform.anchorMin = Vector2.zero;
            useLabel.rectTransform.anchorMax = Vector2.one;
            useLabel.rectTransform.offsetMin = useLabel.rectTransform.offsetMax = Vector2.zero;

            _wheelBlocker.SetActive(false);
            _wheelPanel.gameObject.SetActive(false);
        }

        Slider BuildBrightnessSlider(RectTransform parent)
        {
            Image hit = LayerUiKit.NewImage("BrightnessSlider", parent, Clear, true, 1f);
            LayerUiKit.Place(hit.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -240f),
                new Vector2(PanelW - 28f, 32f));
            Image track = LayerUiKit.NewImage("Track", hit.rectTransform, new Color(1f, 1f, 1f, 0.35f), false, 4f);
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(-20f, 8f);
            RectTransform handleArea = LayerUiKit.NewRect("Handle Slide Area", hit.rectTransform);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.sizeDelta = new Vector2(-20f, 0f);
            Image handle = LayerUiKit.NewImage("Handle", handleArea, Color.white, false, 11f);
            handle.rectTransform.anchorMin = handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(22f, 22f);
            var slider = hit.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.SetValueWithoutNotify(1f);
            return slider;
        }

        /// <summary>Opens the wheel at the current colour (hue, saturation, brightness of Style.Color).</summary>
        public void OpenWheel()
        {
            RgbaColor start = _session != null ? _session.Style.Color : Palette.Black;
            Color.RGBToHSV(ToColor(start), out _hue, out _sat, out _val);
            _brightness.SetValueWithoutNotify(_val);
            SyncWheel();
            _wheelBlocker.SetActive(true);
            _wheelPanel.gameObject.SetActive(true);
        }

        public void CloseWheel()
        {
            if (_wheelBlocker != null) _wheelBlocker.SetActive(false);
            if (_wheelPanel != null) _wheelPanel.gameObject.SetActive(false);
        }

        /// <summary>Use colour: current colour, restyle the selection, keep it as the custom swatch, close.</summary>
        public void UseWheelColor()
        {
            RgbaColor c = WheelColor;
            SetCustom(c);
            CloseWheel();
            SelectColor(c);   // StateChanged -> Refresh (also redraws the custom swatch)
            RefreshCustom(_session != null ? _session.Style.Color : c);
        }

        /// <summary>
        /// Picks hue and saturation at <paramref name="p"/>, in wheel units: (0, 0) is the centre, length 1 the rim
        /// (points outside are pulled onto the rim).
        /// </summary>
        public void PickOnWheel(Vector2 p)
        {
            float r = p.magnitude;
            if (r > 1f) p /= r;
            _sat = Mathf.Min(1f, r);
            float a = Mathf.Atan2(p.y, p.x) / (2f * Mathf.PI);
            _hue = a < 0f ? a + 1f : a;
            SyncWheel();
        }

        void PickAtScreen(Vector2 screen)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_wheelRect, screen, null, out Vector2 local)) return;
            float radius = _wheelRect.rect.width * 0.5f;
            PickOnWheel((local - _wheelRect.rect.center) / Mathf.Max(1f, radius));
        }

        void SyncWheel()
        {
            float a = _hue * 2f * Mathf.PI, radius = WheelSize * 0.5f;
            _wheelMarker.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (_sat * radius);
            _wheelImage.color = new Color(_val, _val, _val, 1f);   // the disc darkens with the brightness
            _preview.color = ToColor(WheelColor);
        }

        void ILayerPointerTarget.OnRelayDown(int tag, PointerEventData e)
        {
            if (tag == WheelTag) PickAtScreen(e.position);
        }

        void ILayerPointerTarget.OnRelayDrag(int tag, PointerEventData e)
        {
            if (tag == WheelTag) PickAtScreen(e.position);
        }

        void ILayerPointerTarget.OnRelayUp(int tag, PointerEventData e)
        {
            if (tag == WheelBlockerTag) CloseWheel();   // on release: the whole touch stays on UI
        }

        /// <summary>
        /// A disc (or, with <paramref name="hole"/> &gt; 0, a ring) of hues around the circle, white at the centre and
        /// fully saturated at the rim, anti-aliased at the edges.
        /// </summary>
        static Texture2D MakeSpectrumTexture(int size, float hole)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[size * size];
            float aa = 2f / size;
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float x = (i + 0.5f) / size * 2f - 1f, y = (j + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(x * x + y * y);
                    float alpha = Mathf.Clamp01((1f - r) / aa);
                    if (hole > 0f) alpha = Mathf.Min(alpha, Mathf.Clamp01((r - hole) / aa));
                    float h = Mathf.Atan2(y, x) / (2f * Mathf.PI);
                    if (h < 0f) h += 1f;
                    Color c = Color.HSVToRGB(h, hole > 0f ? 1f : Mathf.Min(1f, r), 1f);
                    c.a = alpha;
                    px[j * size + i] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        void DestroyWheelTextures()
        {
            if (_wheelTex != null) Destroy(_wheelTex);
            if (_ringTex != null) Destroy(_ringTex);
        }
    }
}
