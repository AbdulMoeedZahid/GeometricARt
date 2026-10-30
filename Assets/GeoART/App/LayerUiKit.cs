using UnityEngine;
using UnityEngine.UI;

namespace GeoART.App
{
    /// <summary>
    /// Small runtime uGUI helpers shared by <see cref="LayerSidebar"/> and <see cref="ToastView"/>: overlay canvas on the
    /// 390x844 reference, a procedural rounded sprite (9-sliced, so pills and capsule dashes need no imported art),
    /// and the CanvasScaler factor for converting screen pixels to reference units.
    /// </summary>
    static class LayerUiKit
    {
        public const float RefWidth = 390f, RefHeight = 844f, MatchWidthOrHeight = 0.5f;
        const int RoundSize = 32;
        const float RoundBorder = 15f;

        static Sprite s_Round;
        static Font s_Font;

        public static Font Font => s_Font != null ? s_Font : (s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        /// <summary>White disc, 9-sliced with a 15 px border: a rounded rect / capsule at any size.</summary>
        public static Sprite Round => s_Round != null ? s_Round : (s_Round = CreateRound());

        /// <summary>Screen pixels per reference unit, exactly as CanvasScaler ScaleWithScreenSize (match 0.5) computes it.</summary>
        public static float ScaleFactor()
        {
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            float logW = Mathf.Log(w / RefWidth, 2f), logH = Mathf.Log(h / RefHeight, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logW, logH, MatchWidthOrHeight));
        }

        public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder, bool raycaster)
        {
            var go = raycaster
                ? new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
                : new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.matchWidthOrHeight = MatchWidthOrHeight;
            return canvas;
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Rounded (sliced) image. <paramref name="radius"/> is the corner radius in reference units.</summary>
        public static Image NewImage(string name, Transform parent, Color color, bool raycast, float radius = RoundBorder)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Round;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = RoundBorder / Mathf.Max(0.5f, radius);
            img.color = color;
            img.raycastTarget = raycast;
            if (raycast) img.canvasRenderer.cullTransparentMesh = false;   // clear hit areas must still be hit
            return img;
        }

        public static Text NewText(string name, Transform parent, int fontSize, TextAnchor alignment, Color color)
        {
            var rt = NewRect(name, parent);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>Places <paramref name="rt"/> by its pivot at <paramref name="pos"/> relative to a single anchor point.</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        static Sprite CreateRound()
        {
            var tex = new Texture2D(RoundSize, RoundSize, TextureFormat.RGBA32, false)
            {
                name = "GeoART Round",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[RoundSize * RoundSize];
            float c = RoundSize * 0.5f;
            for (int y = 0; y < RoundSize; y++)
                for (int x = 0; x < RoundSize; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float a = Mathf.Clamp01(c - Mathf.Sqrt(dx * dx + dy * dy));   // 1 px anti-aliased edge
                    px[y * RoundSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, RoundSize, RoundSize), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(RoundBorder, RoundBorder, RoundBorder, RoundBorder));
            sprite.name = "GeoART Round";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
