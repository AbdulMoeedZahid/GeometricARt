using System;

namespace GeoART.Contracts
{
    /// <summary>Colour of the board's guide lines (dashed grid and outline, frame, user snap points). Nothing is filled.</summary>
    public enum BoardGuideColor { Black, White }

    /// <summary>
    /// App-wide display settings for the board: the guide-line colour (Black, the original dark look and the default,
    /// or White for dark surfaces) and the shape colours offered by the colour picker. View-only: it is not part of the
    /// Board model, so undo, layers and snapping never see it. A static setting rather than a ServiceHub service,
    /// because the BoardView that reads it stays inactive (no Awake) until the board is placed, while the picker can be
    /// used before and after. GeoART.App resets it on SubsystemRegistration, like ServiceHub.
    /// </summary>
    public static class BoardAppearance
    {
        static BoardGuideColor s_Guides = BoardGuideColor.Black;

        public static BoardGuideColor Guides => s_Guides;

        /// <summary>Raised when <see cref="Guides"/> changes.</summary>
        public static event Action Changed;

        public static void SetGuides(BoardGuideColor guides)
        {
            if (guides == s_Guides) return;
            s_Guides = guides;
            Changed?.Invoke();
        }

        /// <summary>Shape colours offered by the picker, in display order (Palette values, D-08).</summary>
        public static readonly RgbaColor[] ShapeColors = { Palette.Black, Palette.White, Palette.Red, Palette.Green, Palette.Blue };
        public static readonly string[] ShapeColorNames = { "Black", "White", "Red", "Green", "Blue" };

        /// <summary>Back to Black and no listeners (new Play Mode session, tests).</summary>
        public static void ResetForNewSession()
        {
            s_Guides = BoardGuideColor.Black;
            Changed = null;
        }
    }
}
