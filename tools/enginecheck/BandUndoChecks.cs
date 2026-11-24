using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// Layer bands in the session (ui-line-plan 3.4, 3.10): Layer.DeepClone copies Bands, SetLayerBands is one step and a
    /// no-op when unchanged (compared sanitized), width ticks merge per layer, opacity then width is two steps, SelectLayer
    /// ends the merge, Undo/Redo restore every field and leave the weave unchanged, Clear/Load behave as documented.
    /// Restyle selection (3.9): colour / width / dash only, hidden layers untouched, no-op no step, one BoardChanged(Shapes).
    /// </summary>
    public static class BandUndoChecks
    {
        static LayerBands Full => new LayerBands
        {
            On = true, Mode = BandMode.Interlace, Width = 0.021, HideStructure = true, InnerLines = true, FlipWeave = true,
            OutlineColor = Palette.Red, OutlineWidth = LineWidth.Thick,
        };

        public static void DeepCloneCopiesBands()
        {
            var l = new Layer(new EntityId(3), "L") { Bands = Full };
            Check.That(l.DeepClone().Bands == Full, "Layer.DeepClone copies Bands");
            var b = new Board();
            b.Layers.Add(l);
            Check.That(b.DeepClone().Layers[0].Bands == Full, "Board.DeepClone too");
        }

        public static void SetLayerBandsIsOneStepAndNoOpWhenUnchanged()
        {
            var s = new EditorSession();
            EntityId id = s.ActiveLayer.Id;
            int changes = 0;
            s.BoardChanged += k => { if ((k & BoardChangeKind.Layers) != 0) changes++; };
            s.SetLayerBands(id, LayerBands.Default);   // a fresh layer's default(LayerBands) sanitizes to this
            Check.That(s.UndoCount == 0 && changes == 0, "turning Off a fresh layer is no change");
            var on = LayerBands.Default;
            on.On = true;
            s.SetLayerBands(id, on);
            Check.That(s.UndoCount == 1 && s.NextUndoLabel == "Bands" && changes == 1, "On: one 'Bands' step, one Layers change");
            s.SetLayerBands(id, on);
            Check.That(s.UndoCount == 1 && changes == 1, "same value again: nothing");
            s.SetLayerBands(new EntityId(9999), on);
            Check.That(s.UndoCount == 1, "unknown layer: nothing");
            var garbage = on;
            garbage.Width = double.NaN;
            garbage.Mode = (BandMode)42;
            s.SetLayerBands(id, garbage);
            Check.That(s.UndoCount == 1, "garbage sanitizes to the same value: nothing");
            Check.That(s.ActiveLayer.Bands == on, "stored sanitized");
        }

        public static void WidthTicksMergeAndOtherEditsSplitTheRun()
        {
            var s = new EditorSession();
            EntityId id = s.ActiveLayer.Id;
            var b = LayerBands.Default;
            b.On = true;
            s.SetLayerBands(id, b);
            for (int i = 1; i <= 10; i++)
            {
                b.Width = 0.015 + 0.001 * i;
                s.SetLayerBands(id, b);
            }
            Check.That(s.UndoCount == 2 && s.NextUndoLabel == "Band width", "ten width ticks: one 'Band width' step");
            s.SetLayerOpacity(id, 0.5);
            b.Width = 0.03;
            s.SetLayerBands(id, b);
            Check.That(s.UndoCount == 4, "opacity then width: two more steps");
            s.SelectLayer(id);
            b.Width = 0.031;
            s.SetLayerBands(id, b);
            Check.That(s.UndoCount == 5, "SelectLayer ends the width merge");
            b.HideStructure = true;
            b.Width = 0.032;
            s.SetLayerBands(id, b);
            Check.That(s.UndoCount == 6 && s.NextUndoLabel == "Bands", "width plus another field: a 'Bands' step");
            b.Width = 0.033;
            s.SetLayerBands(id, b);
            Check.That(s.UndoCount == 7, "the width run starts again after a 'Bands' step");
            s.Undo();
            Check.Near(s.ActiveLayer.Bands.Width, 0.032, "Undo reverts the whole width run", 0);
        }

        public static void UndoRedoRestoreEveryFieldAndTheWeave()
        {
            var board = GoldenBoards.StarOnFullBoard();
            var s = new EditorSession(board);
            Layer star = s.ActiveLayer;
            s.SetLayerBands(star.Id, Full);
            var before = BandGraph.Build(s.ActiveLayer.Shapes);
            s.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(0.1, 0.9), true, 0));
            s.Pointer(new PointerEvent(PointerPhase.Up, new Vec2(0.3, 0.95), true, 0));   // any later edit
            s.Undo();
            Check.That(s.ActiveLayer.Bands == Full, "Undo of a later edit keeps the bands (DeepClone)");
            s.Undo();
            Check.That(!s.ActiveLayer.Bands.On, "Undo of the bands step turns them off");
            s.Redo();
            Check.That(s.ActiveLayer.Bands == Full, "Redo restores every field");
            var after = BandGraph.Build(s.ActiveLayer.Shapes);
            Check.That(after.CrossingCount == before.CrossingCount, "same crossings");
            for (int c = 0; c < after.CrossingCount; c++)
            {
                Check.That(after.Graph.Node(after.CrossingNode(c)) == before.Graph.Node(before.CrossingNode(c)), "same crossing");
                Check.That(after.OverPass(c) == before.OverPass(c), "the weave never flips across Undo/Redo");
            }
        }

        public static void ClearAndLoadBehaveAsDocumented()
        {
            var s = new EditorSession();
            var b = LayerBands.Default;
            b.On = true;
            s.SetLayerBands(s.ActiveLayer.Id, b);
            s.ClearBoard();
            Check.That(!s.ActiveLayer.Bands.On && s.NextUndoLabel == "Clear board", "Clear: a fresh layer, undoable");
            s.Undo();
            Check.That(s.ActiveLayer.Bands.On, "Undo of Clear brings the bands back");
            var loaded = GoldenBoards.Empty();
            loaded.Layers[0].Bands = b;
            s.LoadBoard(loaded);
            Check.That(s.ActiveLayer.Bands.On && !s.CanUndo, "Load keeps the file's bands and resets history");
        }

        static EditorSession Selected(out LineShape a, out LineShape hidden, out Layer hiddenLayer)
        {
            var s = new EditorSession();
            a = new LineShape(s.Board.NewId(), new Vec2(0.2, 0.2), new Vec2(0.4, 0.4), StrokeStyle.Default);
            var b = new LineShape(s.Board.NewId(), new Vec2(0.5, 0.2), new Vec2(0.7, 0.4), StrokeStyle.Default.WithColor(Palette.Red));
            s.ActiveLayer.Shapes.Add(a);
            s.ActiveLayer.Shapes.Add(b);
            EntityId top = s.AddLayer();
            hiddenLayer = s.Board.FindLayer(top);
            hidden = new LineShape(s.Board.NewId(), new Vec2(0.2, 0.7), new Vec2(0.4, 0.8), StrokeStyle.Default);
            hiddenLayer.Shapes.Add(hidden);
            s.SetTool(ToolId.SelectMove);
            s.SetSelection(new[] { a.Id, b.Id, hidden.Id });
            s.SetLayerVisible(top, false);
            return s;
        }

        public static void RestyleAppliesOnlyTheRequestedPart()
        {
            var s = Selected(out var a, out var hidden, out _);
            int shapes = 0;
            var toasts = new List<string>();
            s.BoardChanged += k => { if ((k & BoardChangeKind.Shapes) != 0) shapes++; };
            s.Toast += t => toasts.Add(t);
            int steps = s.UndoCount;
            var thick = new StrokeStyle(Palette.Blue, LineWidth.Thick, LineStyle.Dotted);
            s.RestyleSelection(StyleParts.Width, thick);
            Check.That(a.Style.Width == LineWidth.Thick && a.Style.Color.Equals(Palette.Black) && a.Style.Dash == LineStyle.Solid,
                "width only");
            Check.That(hidden.Style.Width == LineWidth.Medium, "a shape on a hidden layer is untouched");
            Check.That(s.UndoCount == steps + 1 && s.NextUndoLabel == "Style" && shapes == 1, "one 'Style' step, one BoardChanged(Shapes)");
            Check.That(toasts.Count == 1 && toasts[0] == "Restyled 2 shapes", $"toast ({string.Join("|", toasts)})");
            s.RestyleSelection(StyleParts.Width, thick);
            Check.That(s.UndoCount == steps + 1 && shapes == 1, "nothing changes: no step, no event");
            s.RestyleSelection(StyleParts.Color, thick);
            Check.That(a.Style.Color.Equals(Palette.Blue) && a.Style.Dash == LineStyle.Solid, "colour only");
            s.RestyleSelection(StyleParts.Dash, thick);
            Check.That(a.Style.Dash == LineStyle.Dotted, "dash only");
            Check.That(s.SelectionCount == 2, "the selection is kept (visible shapes)");
            s.Undo();
            s.Undo();
            s.Undo();
            Check.That(s.ActiveLayer.Shapes[0].Style.Equals(StrokeStyle.Default), "three Undos restore the original style");
        }

        public static void RestyleWithoutSelectionDoesNothing()
        {
            var s = new EditorSession();
            s.ActiveLayer.Shapes.Add(new LineShape(s.Board.NewId(), new Vec2(0.2, 0.2), new Vec2(0.4, 0.4), StrokeStyle.Default));
            s.RestyleSelection(StyleParts.Color | StyleParts.Width | StyleParts.Dash, new StrokeStyle(Palette.Red, LineWidth.Thick, LineStyle.Dotted));
            Check.That(!s.CanUndo && s.ActiveLayer.Shapes[0].Style.Equals(StrokeStyle.Default), "no selection: nothing");
            s.RestyleSelection(StyleParts.None, StrokeStyle.Default);
            Check.That(!s.CanUndo, "no parts: nothing");
        }
    }
}
