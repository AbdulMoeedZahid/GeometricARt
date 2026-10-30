using System;
using GeoART.Contracts;

namespace GeoART.Engine
{
    // OWNER: layers builder. Board.Layers index 0 = BOTTOM, last = TOP.
    public sealed partial class EditorSession
    {
        /// <summary>Most layers a board may hold; AddLayer refuses (with a toast) beyond this.</summary>
        public const int MaxLayers = 12;

        const string LayerNamePrefix = "Layer ";

        Board _namingBoard;        // board the counter below belongs to (LoadBoard swaps boards)
        int _lastLayerNumber;      // highest N handed out as "Layer N" on _namingBoard

        /// <summary>Adds an empty "Layer N" at the TOP and makes it active. Returns EntityId.None at the cap.</summary>
        public EntityId AddLayer()
        {
            if (_board.Layers.Count >= MaxLayers)
            {
                RaiseToast($"Max {MaxLayers} layers");
                return EntityId.None;
            }
            var layer = new Layer(_board.NewId(), NextLayerName());
            _board.Layers.Add(layer);
            _board.ActiveLayerId = layer.Id;
            RaiseBoardChanged(BoardChangeKind.Layers);
            RaiseStateChanged();
            return layer.Id;
        }

        public void SelectLayer(EntityId id)
        {
            if (_board.ActiveLayerId == id || _board.FindLayer(id) == null) return;
            _board.ActiveLayerId = id;
            RaiseBoardChanged(BoardChangeKind.Layers);
            RaiseStateChanged();
        }

        /// <summary>Moves the layer to <paramref name="newIndex"/> (0 = bottom), clamped to the valid range.</summary>
        public void MoveLayer(EntityId id, int newIndex)
        {
            int from = IndexOfLayer(id);
            if (from < 0) return;
            int to = Math.Max(0, Math.Min(_board.Layers.Count - 1, newIndex));
            if (to == from) return;
            Layer layer = _board.Layers[from];
            _board.Layers.RemoveAt(from);
            _board.Layers.Insert(to, layer);
            RaiseBoardChanged(BoardChangeKind.Layers);
        }

        /// <summary>Removes Layers[0] with all its shapes and snap points; the last remaining layer is never deleted.</summary>
        public void DeleteBottomLayer()
        {
            if (_board.Layers.Count <= 1)
            {
                RaiseToast("Can't delete the only layer");
                return;
            }
            Layer bottom = _board.Layers[0];
            _board.Layers.RemoveAt(0);
            for (int i = 0; i < bottom.Shapes.Count; i++)
                if (bottom.Shapes[i] != null) Selection.Ids.Remove(bottom.Shapes[i].Id);
            if (_board.ActiveLayerId == bottom.Id) _board.ActiveLayerId = _board.Layers[0].Id;
            RaiseBoardChanged(BoardChangeKind.Layers | BoardChangeKind.Shapes | BoardChangeKind.SnapPoints);
            RaiseStateChanged();
        }

        public void SetLayerVisible(EntityId id, bool visible)
        {
            Layer layer = _board.FindLayer(id);
            if (layer == null || layer.Visible == visible) return;
            layer.Visible = visible;
            RaiseBoardChanged(BoardChangeKind.Layers);
        }

        /// <summary>Sets the layer's stroke opacity, clamped to [0,1]; NaN is ignored.</summary>
        public void SetLayerOpacity(EntityId id, double opacity)
        {
            if (double.IsNaN(opacity)) return;
            Layer layer = _board.FindLayer(id);
            if (layer == null) return;
            opacity = Math.Max(0.0, Math.Min(1.0, opacity));
            if (layer.Opacity == opacity) return;
            layer.Opacity = opacity;
            RaiseBoardChanged(BoardChangeKind.Layers);
        }

        int IndexOfLayer(EntityId id)
        {
            for (int i = 0; i < _board.Layers.Count; i++)
                if (_board.Layers[i].Id == id) return i;
            return -1;
        }

        /// <summary>"Layer N" with N above every existing "Layer k" and above any N already used on this board.</summary>
        string NextLayerName()
        {
            if (!ReferenceEquals(_namingBoard, _board))
            {
                _namingBoard = _board;
                _lastLayerNumber = 0;
            }
            int n = Math.Max(_lastLayerNumber, _board.Layers.Count);
            for (int i = 0; i < _board.Layers.Count; i++)
            {
                string name = _board.Layers[i].Name;
                if (name != null && name.StartsWith(LayerNamePrefix, StringComparison.Ordinal)
                    && int.TryParse(name.Substring(LayerNamePrefix.Length), out int k) && k > n)
                    n = k;
            }
            _lastLayerNumber = n + 1;
            return LayerNamePrefix + _lastLayerNumber;
        }
    }
}
