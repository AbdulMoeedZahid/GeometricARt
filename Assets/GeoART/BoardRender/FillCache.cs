using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.BoardRender
{
    /// <summary>
    /// Fill triangles per layer for <see cref="BoardView"/> (docs/FILL.md). For a layer with fills: its
    /// <see cref="FillRegions"/> and, per region holding a seed, the region's triangles with the colour of the LAST seed
    /// in it (so overlapping seeds never double-blend). Seeds outside every region draw nothing. Rebuilt for every layer
    /// after <see cref="MarkDirty"/> (BoardView: Shapes, Fills, Reset, History). Layer-setting changes (Layers: band
    /// width, mode, visibility) only mark the fills <see cref="MarkStale"/>: they are rebuilt at most every
    /// <see cref="StaleSeconds"/>, so a band-width drag on a layer with fills keeps drawing the previous fill shapes and
    /// catches up a moment after the drag (re-planarizing tessellated circles every frame would cost ~10 ms in the
    /// editor). Entries of layers not drawn in a frame are dropped at <see cref="EndFrame"/>.
    /// </summary>
    public sealed class FillCache
    {
        /// <summary>Triangles (3 points each) and their colour.</summary>
        public sealed class Piece
        {
            public readonly List<Vec2> Triangles = new List<Vec2>();
            public RgbaColor Color;
            public int Seed;   // index of the seed in Layer.Fills (draw order)
        }

        sealed class Entry
        {
            public readonly List<Piece> Pieces = new List<Piece>();
            public bool Dirty = true, Stale, Seen;
            public float BuiltAt = float.NegativeInfinity;
        }

        /// <summary>Shortest time between two rebuilds of a stale (not dirty) entry.</summary>
        public const float StaleSeconds = 0.15f;

        readonly Dictionary<EntityId, Entry> _entries = new Dictionary<EntityId, Entry>();
        readonly List<EntityId> _drop = new List<EntityId>();
        readonly Dictionary<int, int> _lastSeed = new Dictionary<int, int>();

        public int BuildCount { get; private set; }

        public void MarkDirty()
        {
            foreach (var e in _entries.Values) e.Dirty = true;
        }

        /// <summary>Layer settings changed: rebuild soon, not necessarily this frame.</summary>
        public void MarkStale()
        {
            foreach (var e in _entries.Values) e.Stale = true;
        }

        /// <summary>True when a stale entry is due for its rebuild at <paramref name="now"/> (BoardView then redraws).</summary>
        public bool StaleDue(float now)
        {
            foreach (var e in _entries.Values)
                if (e.Stale && !e.Dirty && now - e.BuiltAt >= StaleSeconds) return true;
            return false;
        }

        public void BeginFrame()
        {
            foreach (var e in _entries.Values) e.Seen = false;
        }

        /// <summary>The fill pieces of <paramref name="layer"/> (empty when it has no fills or none resolve).</summary>
        public List<Piece> Get(Layer layer, float now)
        {
            if (!_entries.TryGetValue(layer.Id, out Entry e)) _entries[layer.Id] = e = new Entry();
            e.Seen = true;
            if (!e.Dirty && !(e.Stale && now - e.BuiltAt >= StaleSeconds)) return e.Pieces;
            e.Dirty = e.Stale = false;
            e.BuiltAt = now;
            e.Pieces.Clear();
            if (layer.Fills.Count == 0) return e.Pieces;
            BuildCount++;
            var regions = FillRegions.Build(layer);
            _lastSeed.Clear();
            for (int i = 0; i < layer.Fills.Count; i++)
            {
                int face = regions.Locate(layer.Fills[i].Point);
                if (face >= 0) _lastSeed[face] = i;
            }
            foreach (var kv in _lastSeed)
            {
                var piece = new Piece { Color = layer.Fills[kv.Value].Color, Seed = kv.Value };
                regions.Triangulate(kv.Key, piece.Triangles);
                if (piece.Triangles.Count > 0) e.Pieces.Add(piece);
            }
            e.Pieces.Sort((a, b) => a.Seed.CompareTo(b.Seed));
            return e.Pieces;
        }

        public void EndFrame()
        {
            _drop.Clear();
            foreach (var kv in _entries)
                if (!kv.Value.Seen) _drop.Add(kv.Key);
            foreach (var id in _drop) _entries.Remove(id);
        }

        public void Clear() => _entries.Clear();
    }
}
