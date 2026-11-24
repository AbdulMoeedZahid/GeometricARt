using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>
    /// Restyle selection (ui-line-plan 3.9): while something is selected, a colour swatch, the Width button or a line mode
    /// chip applies that one attribute to the selected shapes as well as to <see cref="Style"/> (the UI does both). Needed
    /// to highlight a band's structure line in colour or weight, as the book does.
    /// </summary>
    public sealed partial class EditorSession
    {
        /// <summary>"Restyled 1 shape" or "Restyled N shapes".</summary>
        public static string RestyledToast(int n) => n == 1 ? "Restyled 1 shape" : "Restyled " + n + " shapes";

        readonly List<SelectedShape> _restyleScratch = new List<SelectedShape>();

        /// <summary>
        /// Applies the <paramref name="parts"/> of <paramref name="style"/> to every selected shape on a visible layer.
        /// Shapes that already have them are left alone; if nothing changes there is no step and no event. Otherwise one
        /// "Style" undo step, BoardChanged(Shapes) and a toast with the number of shapes changed. The selection is kept.
        /// </summary>
        public void RestyleSelection(StyleParts parts, StrokeStyle style)
        {
            parts &= StyleParts.Color | StyleParts.Width | StyleParts.Dash;
            if (parts == StyleParts.None) return;
            GetSelectedShapes(_restyleScratch);   // visible layers only
            int changed = 0;
            for (int i = 0; i < _restyleScratch.Count; i++)
                if (!Restyled(_restyleScratch[i].Shape.Style, parts, style).Equals(_restyleScratch[i].Shape.Style)) changed++;
            if (changed == 0)
            {
                _restyleScratch.Clear();
                return;
            }
            RecordUndo(UndoLabels.Style);
            // RecordUndo cloned the board for history; the live shapes are still the ones in the scratch list.
            for (int i = 0; i < _restyleScratch.Count; i++)
            {
                Shape s = _restyleScratch[i].Shape;
                s.Style = Restyled(s.Style, parts, style);
            }
            _restyleScratch.Clear();
            RaiseBoardChanged(BoardChangeKind.Shapes);
            RaiseToast(RestyledToast(changed));
        }

        static StrokeStyle Restyled(StrokeStyle s, StyleParts parts, StrokeStyle style)
        {
            if ((parts & StyleParts.Color) != 0) s = s.WithColor(style.Color);
            if ((parts & StyleParts.Width) != 0) s = s.WithWidth(style.Width);
            if ((parts & StyleParts.Dash) != 0) s = s.WithDash(style.Dash);
            return s;
        }
    }
}
