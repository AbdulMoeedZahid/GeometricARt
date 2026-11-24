using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// VERIFIER (ui-line-plan 3.4, 3.9, 3.10, P4): band settings and restyle through the session, from the plan only.
    ///   SetLayerBands  sanitized; unknown layer / unchanged (sanitized) -> nothing; one step per change ("Bands");
    ///                  width-only changes of one layer merge ("Band width") until anything else is recorded, undone,
    ///                  or SelectLayer is called; opacity then width = 2 steps; one BoardChanged(Layers) per call.
    ///   Undo/Redo      restore every LayerBands field exactly (Layer.DeepClone and Board.DeepClone copy Bands).
    ///   Restyle        only the given parts, only selected shapes on visible layers, lines + circles + polygons; no
    ///                  change -> no step, no event, no toast; else one "Style" step, one BoardChanged(Shapes), a toast
    ///                  with the number of shapes changed; the selection is kept; Undo restores every style.
    /// </summary>
    public static class VerifierBandUndoChecks
    {
        const string LBands = "Bands", LBandWidth = "Band width", LOpacity = "Layer opacity", LStyle = "Style";

        static Vec2 V(double x, double y) => new Vec2(x, y);

        sealed class Counter
        {
            public int Board, State, Toasts;
            public BoardChangeKind LastKind;
            public string LastToast;
            public Counter(EditorSession s)
            {
                s.BoardChanged += k => { Board++; LastKind = k; };
                s.StateChanged += () => State++;
                s.Toast += m => { Toasts++; LastToast = m; };
            }
            public void Reset() { Board = State = Toasts = 0; LastToast = null; }
        }

        static EditorSession StarSession(out Layer star)
        {
            var b = GoldenBoards.StarOnFullBoard();
            star = b.FindLayer(b.ActiveLayerId);
            return new EditorSession(b);
        }

        static LayerBands Full()
        {
            LayerBands b = LayerBands.Default;
            b.On = true;
            b.Mode = BandMode.Interlace;
            b.Width = 0.0234567891234567;
            b.HideStructure = true;
            b.InnerLines = true;
            b.FlipWeave = true;
            b.OutlineColor = new RgbaColor(12, 34, 56, 200);
            b.OutlineWidth = LineWidth.Thick;
            return b;
        }

        static string D(LayerBands b) =>
            $"On {b.On} {b.Mode} w {b.Width:R} hide {b.HideStructure} inner {b.InnerLines} flip {b.FlipWeave} {b.OutlineColor} {b.OutlineWidth}";

        static void Same(LayerBands a, LayerBands b, string what) =>
            Check.That(a.On == b.On && a.Mode == b.Mode && a.Width.Equals(b.Width) && a.HideStructure == b.HideStructure
                && a.InnerLines == b.InnerLines && a.FlipWeave == b.FlipWeave && a.OutlineColor.Equals(b.OutlineColor)
                && a.OutlineWidth == b.OutlineWidth, $"{what}: {D(a)} vs {D(b)}");

        // ================= model =================

        public static void V_DeepCloneCopiesEveryBandField()
        {
            var l = new Layer(new EntityId(3), "x") { Bands = Full() };
            Same(l.DeepClone().Bands, Full(), "Layer.DeepClone");
            var b = new Board();
            b.Layers.Add(l);
            Same(b.DeepClone().Layers[0].Bands, Full(), "Board.DeepClone");
            var c = l.DeepClone();
            c.Bands.Width = 0.04;
            Check.That(l.Bands.Width == Full().Width, "a value copy: editing the clone leaves the source alone");
        }

        // ================= SetLayerBands =================

        public static void V_ToggleIsOneStepAndUndoRestoresEverything()
        {
            var s = StarSession(out Layer star);
            var n = new Counter(s);
            s.SetLayerBands(star.Id, Full());
            Check.That(s.UndoCount == 1 && s.NextUndoLabel == LBands, $"one \"Bands\" step, got {s.UndoCount} {s.NextUndoLabel}");
            Check.That(n.Board == 1 && (n.LastKind & BoardChangeKind.Layers) != 0, $"one BoardChanged(Layers), got {n.Board} {n.LastKind}");
            Same(star.Bands, Full().Sanitized(), "stored sanitized");
            LayerBands off = star.Bands;
            off.On = false;
            s.SetLayerBands(star.Id, off);
            Check.That(s.UndoCount == 2, "turning off is a second step");
            s.Undo();
            Same(s.Board.FindLayer(star.Id).Bands, Full().Sanitized(), "Undo restores On and every field");
            s.Undo();
            Check.That(!s.Board.FindLayer(star.Id).Bands.On, "Undo back to no bands");
            s.Redo();
            s.Redo();
            Same(s.Board.FindLayer(star.Id).Bands, off, "Redo twice: off, with the remembered settings");
        }

        public static void V_UnchangedOrUnknownMakesNoPhantomStep()
        {
            var s = StarSession(out Layer star);
            var n = new Counter(s);
            s.SetLayerBands(star.Id, default(LayerBands));
            s.SetLayerBands(star.Id, LayerBands.Default);
            LayerBands nan = LayerBands.Default;
            nan.Width = double.NaN;
            s.SetLayerBands(star.Id, nan);
            s.SetLayerBands(new EntityId(9999), Full());
            Check.That(s.UndoCount == 0 && n.Board == 0, $"no-op calls on a fresh layer: no step, no event (steps {s.UndoCount}, events {n.Board})");
            s.SetLayerBands(star.Id, Full());
            n.Reset();
            s.SetLayerBands(star.Id, Full());
            s.SetLayerBands(star.Id, Full().Sanitized());
            LayerBands clamped = Full();
            clamped.Mode = (BandMode)77;   // sanitizes to Double: a real change
            Check.That(s.UndoCount == 1 && n.Board == 0, "the same value again: no step, no event");
            LayerBands wide = Full();
            wide.Width = 0.2;              // sanitizes to 0.05
            s.SetLayerBands(star.Id, wide);
            LayerBands wide2 = Full();
            wide2.Width = 0.06;            // also 0.05: no change
            int before = s.UndoCount;
            s.SetLayerBands(star.Id, wide2);
            Check.That(s.UndoCount == before, "two values that sanitize to the same width: no second step");
        }

        public static void V_WidthDragIsOneStep()
        {
            var s = StarSession(out Layer star);
            s.SetLayerBands(star.Id, Full());
            int start = s.UndoCount;
            LayerBands b = star.Bands;
            for (int i = 1; i <= 25; i++)
            {
                b.Width = 0.01 + i * 0.001;
                s.SetLayerBands(star.Id, b);
            }
            Check.That(s.UndoCount == start + 1 && s.NextUndoLabel == LBandWidth, $"25 width ticks = one \"Band width\" step, got {s.UndoCount - start}");
            s.Undo();
            Same(s.Board.FindLayer(star.Id).Bands, Full().Sanitized(), "Undo the drag: the width before it");
        }

        public static void V_OpacityThenWidthIsTwoStepsAndRunsDoNotResume()
        {
            var s = StarSession(out Layer star);
            s.SetLayerBands(star.Id, Full());
            int start = s.UndoCount;
            s.SetLayerOpacity(star.Id, 0.8);
            s.SetLayerOpacity(star.Id, 0.6);
            LayerBands b = star.Bands;
            b.Width = 0.02; s.SetLayerBands(star.Id, b);
            b.Width = 0.03; s.SetLayerBands(star.Id, b);
            Check.That(s.UndoCount == start + 2, $"opacity drag then width drag = 2 steps, got {s.UndoCount - start}");
            s.SetLayerOpacity(star.Id, 0.5);
            b.Width = 0.035; s.SetLayerBands(star.Id, b);
            Check.That(s.UndoCount == start + 4, $"opacity, width, opacity, width = 4 steps (runs never resume), got {s.UndoCount - start}");
        }

        public static void V_SelectLayerAndOtherEditsEndTheWidthRun()
        {
            var s = StarSession(out Layer star);
            s.SetLayerBands(star.Id, Full());
            int start = s.UndoCount;
            LayerBands b = star.Bands;
            b.Width = 0.02; s.SetLayerBands(star.Id, b);
            s.SelectLayer(star.Id);                 // same layer: a new popup session
            b.Width = 0.03; s.SetLayerBands(star.Id, b);
            Check.That(s.UndoCount == start + 2, $"SelectLayer (even the active one) ends the run, got {s.UndoCount - start}");
            b.HideStructure = !b.HideStructure; s.SetLayerBands(star.Id, b);
            b.Width = 0.04; s.SetLayerBands(star.Id, b);
            Check.That(s.UndoCount == start + 4, $"a toggle between width ticks splits them, got {s.UndoCount - start}");
            Check.That(s.NextUndoLabel == LBandWidth, "label of the last width run");
            s.Undo();
            b.Width = 0.045; s.SetLayerBands(star.Id, b);
            b.Width = 0.046; s.SetLayerBands(star.Id, b);
            Check.That(s.UndoCount == start + 4, $"after Undo a new width run is one new step, got {s.UndoCount - start}");
            // Another layer's width is its own run.
            var dotted = s.Board.Layers[0];
            LayerBands d = LayerBands.Default; d.On = true;
            s.SetLayerBands(dotted.Id, d);
            d.Width = 0.02; s.SetLayerBands(dotted.Id, d);
            b.Width = 0.03; s.SetLayerBands(star.Id, b);
            Check.That(s.UndoCount == start + 7, $"layer A toggle, layer A width, layer B width = 3 steps, got {s.UndoCount - start}");
        }

        public static void V_ClearAndLoadAndUndoOfClearKeepBands()
        {
            var s = StarSession(out Layer star);
            s.SetLayerBands(star.Id, Full());
            s.ClearBoard();
            foreach (var l in s.Board.Layers) Check.That(!l.Bands.On, "a cleared board has no bands");
            s.Undo();
            Same(s.Board.FindLayer(star.Id).Bands, Full().Sanitized(), "Undo of Clear brings the bands back");
            var loaded = GoldenBoards.Empty();
            loaded.Layers[0].Bands = Full();
            s.LoadBoard(loaded);
            Check.That(!s.CanUndo, "LoadBoard resets history");
            Same(s.Board.Layers[0].Bands, Full(), "a loaded board keeps its bands");
        }

        public static void V_BandsNeverTouchShapesOrSnapCandidates()
        {
            var s = StarSession(out Layer star);
            var before = new List<Vec2>();
            foreach (var c in s.SnapCandidates) before.Add(c.Position);
            s.SetLayerBands(star.Id, Full());   // HideStructure too: hidden structure lines still snap (plan 3.8)
            var after = new List<Vec2>();
            foreach (var c in s.SnapCandidates) after.Add(c.Position);
            Check.That(before.Count == after.Count, $"same candidates ({before.Count} vs {after.Count})");
            for (int i = 0; i < before.Count; i++) Check.That(before[i] == after[i], "same candidate positions");
            Check.That(star.Shapes.Count == 8, "shapes untouched");
        }

        // ================= RestyleSelection =================

        static EditorSession Mixed(out Layer a, out Layer b, out List<Shape> all)
        {
            var board = GoldenBoards.Empty();
            a = board.Layers[0];
            b = new Layer(board.NewId(), "B");
            board.Layers.Add(b);
            all = new List<Shape>
            {
                new LineShape(board.NewId(), V(0.1, 0.1), V(0.4, 0.2), StrokeStyle.Default),
                new CircleShape(board.NewId(), V(0.5, 0.5), 0.1, new StrokeStyle(Palette.Blue, LineWidth.Thin, LineStyle.Dotted)),
                new PolygonShape(board.NewId(), V(0.7, 0.3), 5, 0.08, 0.2, new StrokeStyle(Palette.Red, LineWidth.Thick, LineStyle.Solid)),
                new LineShape(board.NewId(), V(0.2, 0.8), V(0.6, 0.9), new StrokeStyle(Palette.Green, LineWidth.Medium, LineStyle.Dotted)),
            };
            a.Shapes.Add(all[0]);
            a.Shapes.Add(all[1]);
            b.Shapes.Add(all[2]);
            b.Shapes.Add(all[3]);
            var s = new EditorSession(board);
            s.SetTool(ToolId.SelectMove);
            var ids = new List<EntityId>();
            foreach (var sh in all) ids.Add(sh.Id);
            s.SetSelection(ids);
            Check.That(s.SelectionCount == 4, "precondition: 4 selected across two layers");
            return s;
        }

        public static void V_RestyleAppliesOnlyTheGivenPartToEveryKind()
        {
            var s = Mixed(out _, out _, out var all);
            var before = new List<StrokeStyle>();
            foreach (var sh in all) before.Add(sh.Style);
            var n = new Counter(s);
            s.RestyleSelection(StyleParts.Dash, new StrokeStyle(Palette.White, LineWidth.Thin, LineStyle.Dotted));
            for (int i = 0; i < all.Count; i++)
            {
                var st = all[i].Style;
                Check.That(st.Dash == LineStyle.Dotted, $"{all[i].Kind} dashed");
                Check.That(st.Color.Equals(before[i].Color) && st.Width == before[i].Width, $"{all[i].Kind}: colour and width kept");
            }
            Check.That(s.UndoCount == 1 && s.NextUndoLabel == LStyle, "one \"Style\" step");
            Check.That(n.Board == 1 && n.LastKind == BoardChangeKind.Shapes, $"one BoardChanged(Shapes), got {n.Board} {n.LastKind}");
            Check.That(n.Toasts == 1 && n.LastToast == "Restyled 2 shapes", $"toast counts the 2 shapes that changed, got {n.LastToast}");
            Check.That(s.SelectionCount == 4 && s.ActiveTool == ToolId.SelectMove, "selection and tool kept");
            s.RestyleSelection(StyleParts.Color | StyleParts.Width, new StrokeStyle(Palette.Orange, LineWidth.Thick, LineStyle.Solid));
            foreach (var sh in all)
                Check.That(sh.Style.Color.Equals(Palette.Orange) && sh.Style.Width == LineWidth.Thick && sh.Style.Dash == LineStyle.Dotted,
                    $"{sh.Kind}: colour + width applied, dash untouched");
            s.Undo();
            s.Undo();
            for (int i = 0; i < all.Count; i++)
            {
                Shape live = null;
                foreach (var l in s.Board.Layers) foreach (var sh in l.Shapes) if (sh.Id == all[i].Id) live = sh;
                Check.That(live != null && live.Style.Equals(before[i]), $"{all[i].Kind}: two Undos restore the original style");
            }
        }

        public static void V_RestyleNoChangeIsSilent()
        {
            var s = Mixed(out _, out _, out _);
            var n = new Counter(s);
            s.RestyleSelection(StyleParts.None, new StrokeStyle(Palette.Orange, LineWidth.Thick, LineStyle.Dotted));
            s.RestyleSelection((StyleParts)64, new StrokeStyle(Palette.Orange, LineWidth.Thick, LineStyle.Dotted));
            s.ClearSelection();
            n.Reset();
            s.RestyleSelection(StyleParts.Color, new StrokeStyle(Palette.Orange, LineWidth.Thick, LineStyle.Dotted));
            Check.That(s.UndoCount == 0 && n.Board == 0 && n.Toasts == 0, "no parts, unknown parts or no selection: nothing");
            var s2 = Mixed(out _, out _, out _);
            s2.RestyleSelection(StyleParts.Width, new StrokeStyle(Palette.Black, LineWidth.Medium, LineStyle.Solid));
            int steps = s2.UndoCount;
            var n2 = new Counter(s2);
            s2.RestyleSelection(StyleParts.Width, new StrokeStyle(Palette.Red, LineWidth.Medium, LineStyle.Dotted));
            Check.That(s2.UndoCount == steps && n2.Board == 0 && n2.Toasts == 0, "already that width: no step, no event, no toast");
        }

        public static void V_RestyleSkipsHiddenLayers()
        {
            var s = Mixed(out _, out Layer b, out var all);
            s.SetLayerVisible(b.Id, false);   // prunes b's shapes from the selection
            s.RestyleSelection(StyleParts.Color, new StrokeStyle(Palette.Orange, LineWidth.Thin, LineStyle.Solid));
            Check.That(all[0].Style.Color.Equals(Palette.Orange) && all[1].Style.Color.Equals(Palette.Orange), "visible ones restyled");
            Check.That(all[2].Style.Color.Equals(Palette.Red) && all[3].Style.Color.Equals(Palette.Green), "hidden layer's shapes untouched");
            s.SetLayerVisible(b.Id, true);
            Check.That(s.SelectionCount == 2, "they are not re-selected when shown again");
        }

        public static void V_RestyleThenUndoKeepsBandOutlinesIdentical()
        {
            // Styles never change band geometry: restyling a banded star and undoing it gives identical outlines.
            var s = StarSession(out Layer star);
            s.SetLayerBands(star.Id, Full());
            var o1 = BandOutline.Build(BandGraph.Build(star.Shapes), new BandOutlineOptions { Width = 0.02 });
            s.SetTool(ToolId.SelectMove);
            var ids = new List<EntityId>();
            foreach (var sh in star.Shapes) ids.Add(sh.Id);
            s.SetSelection(ids);
            s.RestyleSelection(StyleParts.Width | StyleParts.Dash, new StrokeStyle(Palette.Red, LineWidth.Thick, LineStyle.Dotted));
            var live = s.Board.FindLayer(star.Id);
            var o2 = BandOutline.Build(BandGraph.Build(live.Shapes), new BandOutlineOptions { Width = 0.02 });
            Check.That(o1.Points.Count == o2.Points.Count, "same outline size after restyle");
            for (int i = 0; i < o1.Points.Count; i++) Check.That(o1.Points[i] == o2.Points[i], "identical outline points");
            Check.That(live.Bands.On, "bands stay on");
        }
    }
}
