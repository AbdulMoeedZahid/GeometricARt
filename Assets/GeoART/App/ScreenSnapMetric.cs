using GeoART.Contracts;
using UnityEngine;

namespace GeoART.App
{
    /// <summary>
    /// The App's <see cref="ISnapMetric"/>: snap and tap tolerances in real screen pixels, so a dot is as easy to hit at
    /// the far edge of a tilted board as at the near edge. Projects through <see cref="IBoardSpace.TryBoardToScreen"/>
    /// (perspective-correct on device, the flat camera in the Editor sandbox).
    ///
    /// The board space is resolved lazily from <see cref="ServiceHub"/> on each call (a dictionary lookup, no
    /// allocation). The installers that register IBoardSpace (FlatBoardSpace, ARBoardSpace) run Awake at the same
    /// execution order as EngineInstaller, so the space may not exist yet when the metric is created, and it may be
    /// re-registered later.
    ///
    /// <see cref="TolerancePx"/> = <see cref="ToleranceReferencePx"/> reference pixels times the scale factor of the
    /// ToolBar's CanvasScaler (ScaleWithScreenSize, 390x844, match 0.5), so it matches the visual size of the UI.
    /// </summary>
    public sealed class ScreenSnapMetric : ISnapMetric
    {
        /// <summary>Snap/tap radius in reference pixels (390x844 reference screen).</summary>
        public const float ToleranceReferencePx = 24f;

        /// <summary>UI reference resolution shared with ToolBar's CanvasScaler.</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(390f, 844f);

        /// <summary>CanvasScaler.matchWidthOrHeight shared with ToolBar.</summary>
        public const float MatchWidthOrHeight = 0.5f;

        IBoardSpace _space;

        /// <param name="space">Optional fixed board space (tests). Null = resolve from ServiceHub on each call.</param>
        public ScreenSnapMetric(IBoardSpace space = null) { _space = space; }

        public double TolerancePx => ToleranceReferencePx * ScaleFactor(Screen.width, Screen.height);

        public bool TryToScreen(Vec2 board, out Vec2 screenPx)
        {
            IBoardSpace space = _space;
            if (space == null && !ServiceHub.TryResolve(out space))
            {
                screenPx = default;
                return false;
            }
            return space.TryBoardToScreen(board, out screenPx);
        }

        /// <summary>CanvasScaler ScaleWithScreenSize / MatchWidthOrHeight formula for the shared reference resolution.</summary>
        public static float ScaleFactor(float screenW, float screenH)
        {
            screenW = Mathf.Max(1f, screenW);
            screenH = Mathf.Max(1f, screenH);
            float logW = Mathf.Log(screenW / ReferenceResolution.x, 2f);
            float logH = Mathf.Log(screenH / ReferenceResolution.y, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logW, logH, MatchWidthOrHeight));
        }
    }
}
