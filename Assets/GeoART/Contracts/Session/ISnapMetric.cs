namespace GeoART.Contracts
{
    /// <summary>
    /// Screen-space measure used by snapping and tap detection. The App provides a real one (board -> screen pixels
    /// through <see cref="IBoardSpace.TryBoardToScreen"/>); the engine falls back to board units when none is set
    /// (console harness, flat tests). Pure C#: no Unity types.
    /// </summary>
    public interface ISnapMetric
    {
        /// <summary>Board -> screen pixels, perspective-correct. False if not projectable.</summary>
        bool TryToScreen(Vec2 board, out Vec2 screenPx);

        /// <summary>Tolerance radius in screen pixels for snapping and tap detection.</summary>
        double TolerancePx { get; }
    }
}
