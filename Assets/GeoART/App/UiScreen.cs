using UnityEngine;

namespace GeoART.App
{
    /// <summary>
    /// Screen size and safe area for the runtime-built UI (ToolBar, ColorBar's wheel, LayerSidebar, PlacementControls,
    /// DesignsPanel, ToolHoldMenu). Normally just <see cref="Screen"/>; the Editor's UI layout shot (GeoART.EditorTools.UiLayoutShot)
    /// sets <see cref="Override"/> to lay the UI out for a phone screen, since a batch Editor has no phone-sized Game view.
    /// Nothing at runtime sets it.
    /// </summary>
    public static class UiScreen
    {
        /// <summary>A pretend screen (size in px and safe area in px), or null for the real one.</summary>
        public static (Vector2Int Size, Rect SafeArea)? Override;

        public static int Width => Override.HasValue ? Override.Value.Size.x : Screen.width;
        public static int Height => Override.HasValue ? Override.Value.Size.y : Screen.height;
        public static Rect SafeArea => Override.HasValue ? Override.Value.SafeArea : Screen.safeArea;
    }
}
