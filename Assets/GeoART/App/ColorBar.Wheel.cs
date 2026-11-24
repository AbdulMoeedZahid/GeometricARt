using GeoART.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GeoART.App
{
    /// <summary>
    /// The colour wheel (decision 7): a full-spectrum picker, opened from the palette's "Wheel…" chip (or its Custom chip
    /// while no custom colour exists yet).
    ///   Custom colour   the last colour picked on the wheel (<see cref="CustomColor"/>; the palette shows it as a swatch
    ///                   in a rainbow ring, grey until one is picked). Kept in PlayerPrefs (Play mode only)
    ///   Wheel popup     a hue/saturation disc (hue = angle, saturation = distance from the centre), a brightness slider,
    ///                   a preview and "Use colour". Dragging changes only the preview; Use makes it the current colour,
    ///                   restyles the selection if there is one (one "Style" step), keeps it as the custom colour and
    ///                   closes. A touch outside closes without a change; the popup's full-screen blocker closes on the
    ///                   touch's RELEASE, so the whole touch stays on UI and never draws.
    /// The popup has its own canvas (sorting order 115) above the tool bar and the layer sidebar. It opens centred in the
    /// safe area left of the ToolBar column (shrunk if that space is too small), so it never covers the column.
    /// </summary>
    public sealed partial class ColorBar : ILayerPointerTarget
    {
        const string CustomColorKey = "GeoART.CustomColor";
        const int WheelBlockerTag = -1, WheelTag = -2;
        const int WheelPx = 192;
        const float PanelW = 260f, PanelH = 330f, WheelSize = 200f;

        Canvas _wheelCanvas;
        RectTransform _wheelPanel, _wheelRect, _wheelMarker;
        RawImage _wheelImage;
        GameObject _wheelBlocker;
        Slider _brightness;
        Image _preview;
        Texture2D _wheelTex;
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

        // ---------- custom colour ----------

        void LoadCustom()
        {
            if (!Application.isPlaying || !PlayerPrefs.HasKey(CustomColorKey)) return;
            int v = PlayerPrefs.GetInt(CustomColorKey);
            _custom = new RgbaColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), 255);
            _hasCustom = true;
        }

        void SetCustom(RgbaColor c)
        {
            bool changed = !_hasCustom || !_custom.Equals(c);
            _hasCustom = true;
            _custom = c;
            if (Application.isPlaying)
            {
                PlayerPrefs.SetInt(CustomColorKey, (c.R << 24) | (c.G << 16) | (c.B << 8) | 255);
                PlayerPrefs.Save();
            }
            if (changed) CustomChanged?.Invoke();
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
            handle.rectTransform.sizeDelta = new Vector2(22f, 22f - 32f);   // the Slider stretches it over the 32 px area's height: 22 x 22
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
            PlaceWheel();
            _wheelBlocker.SetActive(true);
            _wheelPanel.gameObject.SetActive(true);
        }

        /// <summary>
        /// Centres the panel in the safe area left of the ToolBar column (EdgeMargin 8 ref px from both), shrunk (not
        /// below 0.4) when that space is smaller than the panel.
        /// </summary>
        void PlaceWheel()
        {
            float w = Mathf.Max(1, UiScreen.Width), h = Mathf.Max(1, UiScreen.Height);
            float scale = ScreenSnapMetric.ScaleFactor(w, h);
            float margin = 8f * scale;
            Rect safe = UiScreen.SafeArea;
            float left = safe.xMin + margin, right = safe.xMax - margin, bottom = safe.yMin + margin, top = safe.yMax - margin;
            var toolBar = GetComponent<ToolBar>();
            if (toolBar != null && toolBar.TryGetColumnScreenRect(out Rect column) && column.xMin - margin > left)
                right = Mathf.Min(right, column.xMin - margin);
            float fit = Mathf.Clamp(Mathf.Min((right - left) / (PanelW * scale), (top - bottom) / (PanelH * scale)), 0.4f, 1f);
            _wheelPanel.anchorMin = _wheelPanel.anchorMax = new Vector2((left + right) * 0.5f / w, (bottom + top) * 0.5f / h);
            _wheelPanel.pivot = new Vector2(0.5f, 0.5f);
            _wheelPanel.anchoredPosition = Vector2.zero;
            _wheelPanel.localScale = new Vector3(fit, fit, 1f);
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
            SelectColor(c);   // StateChanged -> the ToolBar redraws the Colour button
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
        internal static Texture2D MakeSpectrumTexture(int size, float hole)
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
        }
    }
}
