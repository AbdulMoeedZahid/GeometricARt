using System.Collections.Generic;
using System.Diagnostics;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.BoardRender
{
    /// <summary>
    /// Band geometry per layer for <see cref="BoardView"/>, in two stages so a width drag never re-planarizes:
    ///   topology  BandGraph of the layer's shapes; rebuilt only after <see cref="MarkTopologyDirty"/> (the view calls it
    ///             on BoardChanged with Shapes, Reset or History) or for a layer seen for the first time
    ///   outline   BandOutline for the layer's current options; rebuilt when the topology was rebuilt or the options
    ///             changed (width, mode, inner lines, flip)
    /// Entries of layers not drawn in a frame (deleted, hidden, bands off) are dropped at <see cref="EndFrame"/>.
    /// <see cref="PlanarizeCount"/> / <see cref="OutlineCount"/> count rebuilds and <see cref="LastFrameMs"/> times the
    /// band work of the last frame that did any (tests and the width-slider cost estimate).
    /// </summary>
    public sealed class BandCache
    {
        sealed class Entry
        {
            public BandGraph Graph;
            public BandOutline Outline;
            public BandOutlineOptions Options;
            public bool TopologyDirty = true, Seen;
        }

        readonly Dictionary<EntityId, Entry> _entries = new Dictionary<EntityId, Entry>();
        readonly List<EntityId> _drop = new List<EntityId>();
        readonly Stopwatch _watch = new Stopwatch();

        public int PlanarizeCount { get; private set; }
        public int OutlineCount { get; private set; }
        public double LastFrameMs { get; private set; }
        public int EntryCount => _entries.Count;

        public void MarkTopologyDirty()
        {
            foreach (var e in _entries.Values) e.TopologyDirty = true;
        }

        public void BeginFrame()
        {
            foreach (var e in _entries.Values) e.Seen = false;
            _watch.Reset();
        }

        /// <summary>The outline of <paramref name="layer"/>'s bands for <paramref name="options"/>, rebuilt only as needed.</summary>
        public BandOutline Get(Layer layer, in BandOutlineOptions options)
        {
            if (!_entries.TryGetValue(layer.Id, out Entry e)) _entries[layer.Id] = e = new Entry();
            e.Seen = true;
            if (e.TopologyDirty || e.Graph == null)
            {
                _watch.Start();
                e.Graph = BandGraph.Build(layer.Shapes);
                _watch.Stop();
                PlanarizeCount++;
                e.TopologyDirty = false;
                e.Outline = null;
            }
            if (e.Outline == null || !SameOptions(e.Options, options))
            {
                _watch.Start();
                e.Outline = BandOutline.Build(e.Graph, options);
                _watch.Stop();
                OutlineCount++;
                e.Options = options;
            }
            return e.Outline;
        }

        public void EndFrame()
        {
            _drop.Clear();
            foreach (var kv in _entries)
                if (!kv.Value.Seen) _drop.Add(kv.Key);
            foreach (var id in _drop) _entries.Remove(id);
            if (_watch.ElapsedTicks > 0) LastFrameMs = _watch.Elapsed.TotalMilliseconds;
        }

        public void Clear() => _entries.Clear();

        /// <summary>The outline drawn for a layer in the last frame, if any (tests).</summary>
        public bool TryGetOutline(EntityId layer, out BandOutline outline)
        {
            outline = _entries.TryGetValue(layer, out Entry e) ? e.Outline : null;
            return outline != null;
        }

        static bool SameOptions(in BandOutlineOptions a, in BandOutlineOptions b) =>
            a.Width == b.Width && a.Interlace == b.Interlace && a.FlipWeave == b.FlipWeave && a.InnerLines == b.InnerLines &&
            a.InnerInsetFloor == b.InnerInsetFloor && a.InnerMinHalf == b.InnerMinHalf &&
            a.SkipSelfIntersections == b.SkipSelfIntersections && a.StructureShown == b.StructureShown;
    }
}
