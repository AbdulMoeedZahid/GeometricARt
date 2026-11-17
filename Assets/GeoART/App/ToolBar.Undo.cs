using GeoART.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace GeoART.App
{
    /// <summary>
    /// The Undo button: one rounded button (U-turn arrow, label "Undo") at the top-left corner of the safe area, a direct
    /// child of the ToolBar canvas (not in the column), EdgeMargin in from the corner and scaled with the column's fit.
    /// The top band is free while the board is Placed (DevHud's coach line only shows text in TrackingLost), so the button
    /// is shown only when the canvas is visible and the state is Placed (or the scene has no IBoardSpace). It is an action,
    /// not a mode: always the idle colour, dimmed (CanvasGroup alpha <see cref="UndoDisabledAlpha"/>) when there is nothing
    /// to undo but still a raycast target, so a tap toasts "Nothing to undo" and never reaches the board. Refreshed on
    /// session/placement StateChanged and on every BoardChanged (CanUndo changes only together with a BoardChanged).
    /// </summary>
    public sealed partial class ToolBar
    {
        const float UndoDisabledAlpha = 0.4f;

        RectTransform _undo;
        CanvasGroup _undoGroup;

        void BuildUndoButton(Font font)
        {
            var bg = MakeButton("Undo", Glyph.Undo, font, (RectTransform)_canvas.transform, () => _session?.Undo(), out _);
            _undo = (RectTransform)bg.transform;
            _undo.anchorMin = _undo.anchorMax = _undo.pivot = new Vector2(0f, 1f);
            _undo.sizeDelta = new Vector2(ButtonW, ButtonH);
            _undoGroup = bg.gameObject.AddComponent<CanvasGroup>();
            _undoGroup.alpha = UndoDisabledAlpha;
            bg.gameObject.SetActive(false);
            ApplySafeArea();
        }

        /// <summary>Visibility (SetActive only on change) and the dimmed look (alpha only on change).</summary>
        void RefreshUndo(bool canvasVisible)
        {
            if (_undo == null) return;
            bool visible = canvasVisible && (_boardSpace == null || _boardSpace.State == PlacementState.Placed);
            if (_undo.gameObject.activeSelf != visible) _undo.gameObject.SetActive(visible);
            float alpha = _session != null && _session.CanUndo ? 1f : UndoDisabledAlpha;
            if (_undoGroup.alpha != alpha) _undoGroup.alpha = alpha;
        }

        /// <summary>
        /// From ApplySafeArea (screen px in, <paramref name="scale"/> = reference px -&gt; screen px): top-left corner at
        /// (safeArea.xMin, safeArea.yMax) moved EdgeMargin (scaled) inwards, pivot (0, 1), scaled by the column's fit.
        /// </summary>
        void PlaceUndoButton(float w, float h, float scale, float fit)
        {
            if (_undo == null) return;
            float margin = EdgeMargin * scale;
            _undo.anchorMin = _undo.anchorMax = new Vector2((_safeArea.xMin + margin) / w, (_safeArea.yMax - margin) / h);
            _undo.anchoredPosition = Vector2.zero;
            _undo.localScale = new Vector3(fit, fit, 1f);
        }
    }
}
