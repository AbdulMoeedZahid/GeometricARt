using GeoART.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace GeoART.App
{
    /// <summary>
    /// The contextual action bar: a horizontal row of four buttons, Move | Scale | Duplicate | Deselect, on the ToolBar's
    /// canvas. Shown only while the board is placed, the selection is not empty and the tool is Select, Move, Scale or
    /// Duplicate. Move, Scale and Duplicate are modes: the active one is filled orange, tapping it again returns to Select,
    /// tapping another switches to it. Deselect empties the selection (the engine then falls back to Select).
    /// Placement (reference px, see <see cref="PlaceActionBar"/>): bottom edge on the column's lowest baseline (just above
    /// the placement row), centred in the space left of the tool column, shrunk (not below MinFit) when that space is
    /// narrower than the bar. Every button is a raycast target and a transparent raycast-target panel covers the whole bar,
    /// so BoardInputRouter ignores touches that start anywhere on it, including the gaps between buttons.
    /// </summary>
    public sealed partial class ToolBar
    {
        const float BarButtonW = 64f, BarButtonH = 52f, BarSpacing = 8f;
        const int BarButtonCount = 4;

        /// <summary>Bar width in reference px: four buttons and three spacings (280).</summary>
        public const float ActionBarWidth = BarButtonCount * BarButtonW + (BarButtonCount - 1) * BarSpacing;

        RectTransform _bar;
        Image _moveBg, _scaleBg, _dupBg;

        void BuildActionBar(Font font)
        {
            var go = new GameObject("ActionBar", typeof(RectTransform));
            go.transform.SetParent(_canvas.transform, false);
            _bar = (RectTransform)go.transform;
            _bar.pivot = new Vector2(0.5f, 0f);
            _bar.sizeDelta = new Vector2(ActionBarWidth, BarButtonH);
            AddTouchBlocker(go);   // the 8 px gaps between buttons must not start a transform drag on the board
            float x = 0f;
            _moveBg = CreateBarButton("Move", Glyph.Move, font, ref x, () => ToggleMode(ToolId.Move));
            _scaleBg = CreateBarButton("Scale", Glyph.Scale, font, ref x, () => ToggleMode(ToolId.Scale));
            _dupBg = CreateBarButton("Duplicate", Glyph.Duplicate, font, ref x, () => ToggleMode(ToolId.Duplicate));
            CreateBarButton("Deselect", Glyph.Deselect, font, ref x, () => _session?.ClearSelection());
            go.SetActive(false);
            ApplySafeArea();
        }

        Image CreateBarButton(string label, Glyph glyph, Font font, ref float x, UnityEngine.Events.UnityAction onClick)
        {
            var bg = MakeButton(label, glyph, font, _bar, onClick, out Text text);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;   // "Duplicate" never wraps away
            var rt = (RectTransform)bg.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(BarButtonW, BarButtonH);
            rt.anchoredPosition = new Vector2(x, 0f);
            x += BarButtonW + BarSpacing;
            return bg;
        }

        /// <summary>A mode button: the active mode returns to Select, any other mode is entered.</summary>
        void ToggleMode(ToolId mode)
        {
            if (_session == null) return;
            _session.SetTool(_session.ActiveTool == mode ? ToolId.SelectMove : mode);
        }

        /// <summary>From Refresh: visibility (SetActive only on change) and the mode highlight.</summary>
        void RefreshActionBar(bool canvasVisible)
        {
            if (_bar == null) return;
            ToolId tool = _session != null ? _session.ActiveTool : ToolId.None;
            var ids = _session?.Selection?.Ids;
            bool visible = canvasVisible && ids != null && ids.Count > 0 && IsSelectionTool(tool);
            if (_bar.gameObject.activeSelf != visible) _bar.gameObject.SetActive(visible);
            SetColor(_moveBg, tool == ToolId.Move ? Accent : Idle);
            SetColor(_scaleBg, tool == ToolId.Scale ? Accent : Idle);
            SetColor(_dupBg, tool == ToolId.Duplicate ? Accent : Idle);
        }

        /// <summary>
        /// From ApplySafeArea (screen px in, <paramref name="scale"/> = reference px -&gt; screen px). Bottom edge at
        /// safeArea.yMin + PlacementRowTop * h + EdgeMargin * scale (the column's lowest baseline); centred horizontally in
        /// [safeArea.xMin + EdgeMargin, safeArea.xMax - (EdgeMargin + ButtonW * columnFit + EdgeMargin)] (scaled), i.e. left
        /// of the tool column; shrunk to clamp(span / (ActionBarWidth * scale), MinFit, 1) when the span is narrower.
        /// </summary>
        void PlaceActionBar(float w, float h, float scale, float columnFit)
        {
            if (_bar == null) return;
            float bottom = _safeArea.yMin + PlacementRowTop * h + EdgeMargin * scale;
            float left = _safeArea.xMin + EdgeMargin * scale;
            float right = _safeArea.xMax - (EdgeMargin + ButtonW * columnFit + EdgeMargin) * scale;
            float span = Mathf.Max(0f, right - left);
            float fit = Mathf.Clamp(span / (ActionBarWidth * scale), MinFit, 1f);
            _bar.anchorMin = _bar.anchorMax = new Vector2((left + right) * 0.5f / w, bottom / h);
            _bar.anchoredPosition = Vector2.zero;
            _bar.localScale = new Vector3(fit, fit, 1f);
        }
    }
}
