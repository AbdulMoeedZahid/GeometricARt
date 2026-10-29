using GeoART.Contracts;
using UnityEngine;

namespace GeoART.App
{
    /// <summary>
    /// A's minimal on-screen feedback until C's UI exists: placement coach text (scan / tap to place / corner picking /
    /// tracking lost), the registered surface size once placed by corners, and an optional FPS readout. Turn
    /// <see cref="showCoach"/> off once C's placement coach text ships. Interactive controls live in PlacementControls
    /// (uGUI), because IMGUI buttons are invisible to the tap handler's UI hit-test.
    /// </summary>
    public sealed class DevHud : MonoBehaviour
    {
        [SerializeField] bool showCoach = true;
        [SerializeField] bool showFps;
        [SerializeField] bool showResetButton;
        [SerializeField, Tooltip("When placed by corners, show the measured surface and board size (device checklist).")]
        bool showMeasurements = true;

        const float LiveRefreshSeconds = 0.1f;   // edge/size readouts change every frame while moving; cap string rebuilds

        IBoardSpace _boardSpace;
        ICornerPlacement _corners;
        GUIStyle _coachStyle, _smallStyle, _buttonStyle;
        float _fpsSmoothed;

        // Cached coach/measurement strings, rebuilt only when this key changes (no per-frame string allocation).
        string _coach, _measure;
        PlacementState _kState = (PlacementState)(-1);
        PlacementMode _kMode;
        CornerIssue _kIssue;
        int _kCount, _kIssueCorner, _kEdgeMm, _kWMm, _kHMm, _kSMm;
        bool _kConfirm;
        float _lastRebuild = -1f;

        void Start()
        {
            ServiceHub.TryResolve(out _boardSpace);
            ServiceHub.TryResolve(out _corners);   // optional
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) _fpsSmoothed = _fpsSmoothed <= 0f ? 1f / dt : Mathf.Lerp(_fpsSmoothed, 1f / dt, 0.05f);
            if (_boardSpace != null && (showCoach || showMeasurements)) UpdateTexts();
        }

        void OnGUI()
        {
            EnsureStyles();
            float w = Screen.width, h = Screen.height;

            if (showCoach && !string.IsNullOrEmpty(_coach))
            {
                var rect = new Rect(w * 0.06f, h * 0.12f, w * 0.88f, h * 0.08f);
                GUI.Box(rect, GUIContent.none);
                GUI.Label(rect, _coach, _coachStyle);
            }

            if (showMeasurements && !string.IsNullOrEmpty(_measure) && _boardSpace != null && _boardSpace.IsPlaced)
            {
                var rect = new Rect(w * 0.06f, h * 0.21f, w * 0.88f, h * 0.04f);
                GUI.Box(rect, GUIContent.none);
                GUI.Label(rect, _measure, _smallStyle);
            }

            if (showFps)
                GUI.Label(new Rect(w * 0.04f, h - h * 0.05f, w * 0.5f, h * 0.04f), $"{_fpsSmoothed:0} FPS", _smallStyle);

            if (showResetButton && _boardSpace != null && _boardSpace.IsPlaced &&
                GUI.Button(new Rect(w * 0.62f, h - h * 0.07f, w * 0.34f, h * 0.05f), "Re-place board", _buttonStyle))
                _boardSpace.Reset();
        }

        void UpdateTexts()
        {
            PlacementState state = _boardSpace.State;
            PlacementMode mode = _corners != null ? _corners.Mode : PlacementMode.QuickPlace;
            CornerIssue issue = _corners != null ? _corners.Issue : CornerIssue.None;
            int count = _corners != null ? _corners.CornerCount : 0;
            int issueCorner = _corners != null ? _corners.IssueCorner : -1;
            bool confirm = _corners != null && _corners.CanConfirm;
            int edge = _corners != null && count < 4 ? Mm(_corners.LiveEdgeMeters) : -1;
            int wMm = _corners != null ? Mm(_corners.SurfaceWidthMeters) : -1;
            int hMm = _corners != null ? Mm(_corners.SurfaceHeightMeters) : -1;
            int sMm = _corners != null ? Mm(_corners.BoardSideMeters) : -1;

            bool discrete = state != _kState || mode != _kMode || issue != _kIssue || count != _kCount ||
                            issueCorner != _kIssueCorner || confirm != _kConfirm;
            bool live = edge != _kEdgeMm || wMm != _kWMm || hMm != _kHMm || sMm != _kSMm;
            if (!discrete && (!live || Time.unscaledTime - _lastRebuild < LiveRefreshSeconds)) return;

            _kState = state; _kMode = mode; _kIssue = issue; _kCount = count; _kIssueCorner = issueCorner;
            _kConfirm = confirm; _kEdgeMm = edge; _kWMm = wMm; _kHMm = hMm; _kSMm = sMm;
            _lastRebuild = Time.unscaledTime;

            _coach = CoachText(state, _corners, _boardSpace.IsPlaced);
            _measure = _boardSpace.IsPlaced && wMm >= 0 && hMm >= 0
                ? $"Surface {wMm / 10f:0.0} x {hMm / 10f:0.0} cm · board {sMm / 10f:0.0} cm"
                : null;
        }

        static int Mm(double meters) => double.IsNaN(meters) || double.IsInfinity(meters) ? -1 : (int)System.Math.Round(meters * 1000.0);

        /// <summary>Placement coach line. C replaces this with its own coach text bound to IBoardSpace / ICornerPlacement.</summary>
        static string CoachText(PlacementState state, ICornerPlacement corners, bool placed)
        {
            if (corners != null && !placed && corners.Issue != CornerIssue.None)
                return IssueText(corners.Issue, corners.IssueCorner);

            switch (state)
            {
                case PlacementState.Scanning: return "Move your phone slowly over a textured table";
                case PlacementState.PlaneFound:
                    return corners != null && corners.Mode == PlacementMode.Corners
                        ? "Aim the + at a corner of your sheet and tap (1 of 4)"
                        : "Tap to place the board";
                case PlacementState.PickingCorners:
                {
                    if (corners == null) return null;
                    int n = corners.CornerCount;
                    if (n < corners.RequiredCorners)
                    {
                        double edge = corners.LiveEdgeMeters;
                        return double.IsNaN(edge)
                            ? $"Corner {n + 1} of 4: aim the + at the next corner and tap"
                            : $"Corner {n + 1} of 4: aim the + at the next corner and tap · edge {edge * 100:0.0} cm";
                    }
                    return corners.CanConfirm
                        ? $"Surface {corners.SurfaceWidthMeters * 100:0.0} x {corners.SurfaceHeightMeters * 100:0.0} cm, " +
                          $"board {corners.BoardSideMeters * 100:0.0} cm. Tap to place"
                        : "Point the camera at the sheet to place";
                }
                case PlacementState.TrackingLost: return "Tracking lost: move slowly back toward the board";
                case PlacementState.Unsupported:
                    return Application.isEditor ? "AR is unavailable in the Editor (use Sandbox_A_Flat)" : "AR is not supported on this device";
                default: return null;
            }
        }

        static string IssueText(CornerIssue issue, int corner)
        {
            switch (issue)
            {
                case CornerIssue.NoSurface: return "Point the + at the table";
                case CornerIssue.AimTooShallow: return "Tilt the phone down: view is too flat";
                case CornerIssue.TooFar: return "Move closer (within 2.5 m)";
                case CornerIssue.Unsteady: return "Hold steady, then tap";
                case CornerIssue.TooCloseToCorner: return $"That is corner {corner + 1}: aim at the next corner";
                case CornerIssue.NotConvex:
                case CornerIssue.NotRectangular:
                    return corner >= 0
                        ? $"Corner {corner + 1} doesn't fit a rectangle: Undo or Restart"
                        : "The corners don't fit a rectangle: Undo or Restart";
                case CornerIssue.TooSmall: return "Too small (min 10 cm side)";
                case CornerIssue.TooLarge: return "Too large (max 150 cm side)";
                case CornerIssue.TooElongated: return "Too narrow (max 4:1)";
                case CornerIssue.TrackingInterrupted: return "Tracking interrupted: corners cleared, start again";
                default: return null;
            }
        }

        void EnsureStyles()
        {
            if (_coachStyle != null) return;
            int size = Mathf.RoundToInt(Screen.height * 0.022f);
            _coachStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = size,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            _smallStyle = new GUIStyle(_coachStyle) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(size * 0.8f) };
            _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(size * 0.8f) };
        }
    }
}
