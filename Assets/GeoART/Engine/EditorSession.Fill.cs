using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>
    /// The Fill tool (decision 6, docs/FILL.md): a tap inside a closed region of the ACTIVE layer fills it with the current
    /// colour (Style.Color, so the colour wheel's colour too). The fill is stored as a seed (point + colour) on the layer
    /// and found again after every edit (<see cref="FillRegions"/>). A tap on a region that is already filled replaces its
    /// colour; with "no fill" (<see cref="FillErase"/>, the colour bar's choice while Fill is active) it clears it. One
    /// undo step each ("Fill", "Clear fill"); BoardChanged(Fills). No snapping; a drag does nothing. A tap outside every
    /// closed region toasts <see cref="FillOutsideToast"/>; a hidden active layer gets the hidden-layer toast.
    /// </summary>
    public sealed partial class EditorSession
    {
        public const string FillHintToast = "Fill: tap inside a closed shape";
        public const string FillOutsideToast = "Tap inside a closed shape to fill it";
        public const string NothingToClearToast = "Nothing to clear here";

        bool _fillErase;

        public bool FillErase => _fillErase;

        /// <summary>"No fill": Fill taps clear instead of fill. Reset whenever the tool leaves Fill. StateChanged on change.</summary>
        public void SetFillErase(bool erase)
        {
            if (_fillErase == erase) return;
            _fillErase = erase;
            RaiseStateChanged();
        }

        /// <summary>A Fill tap at <paramref name="p"/> on the gesture's layer.</summary>
        void FillAt(Vec2 p)
        {
            Layer layer = _board.FindLayer(_gestureLayer);
            if (layer == null) return;
            if (!layer.Visible)
            {
                RaiseToast(HiddenLayerToast);
                return;
            }
            var regions = FillRegions.Build(layer);
            int face = regions.Locate(p);
            if (face < 0)
            {
                RaiseToast(FillOutsideToast);
                return;
            }
            var same = new List<int>();
            for (int i = 0; i < layer.Fills.Count; i++)
                if (regions.Locate(layer.Fills[i].Point) == face) same.Add(i);

            if (_fillErase)
            {
                if (same.Count == 0)
                {
                    RaiseToast(NothingToClearToast);
                    return;
                }
                RecordUndo(UndoLabels.ClearFill);
                for (int i = same.Count - 1; i >= 0; i--) layer.Fills.RemoveAt(same[i]);
                RaiseBoardChanged(BoardChangeKind.Fills);
                return;
            }
            if (same.Count == 1 && layer.Fills[same[0]].Color.Equals(_style.Color)) return;   // already this colour
            RecordUndo(UndoLabels.Fill);
            for (int i = same.Count - 1; i >= 0; i--) layer.Fills.RemoveAt(same[i]);
            layer.Fills.Add(new FillSeed(p, _style.Color));
            RaiseBoardChanged(BoardChangeKind.Fills);
        }
    }
}
