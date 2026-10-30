using GeoART.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace GeoART.App
{
    /// <summary>
    /// Shows the latest <see cref="IEditorSession.Toast"/> message top-centre (about 24% down the screen, below the
    /// coach text) for <see cref="ShowSeconds"/> with a short fade. Purely visual: no GraphicRaycaster and every
    /// raycastTarget off, so it never blocks touches. Update only runs the fade while a toast is up.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToastView : MonoBehaviour
    {
        const float ShowSeconds = 1.8f, FadeIn = 0.12f, FadeOut = 0.35f;
        const float CentreFromTop = 0.245f, MaxTextWidth = 300f;

        IEditorSession _session;
        RectTransform _anchor;
        CanvasGroup _group;
        Text _text;
        LayoutElement _textLayout;
        float _shownAt = -1f;

        void Awake()
        {
            Canvas canvas = LayerUiKit.CreateCanvas(transform, "ToastView", 120, false);
            _anchor = LayerUiKit.NewRect("Toast", canvas.transform);
            LayerUiKit.Place(_anchor, new Vector2(0.5f, 1f - CentreFromTop), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(MaxTextWidth, 36f));
            _group = _anchor.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            Image bg = LayerUiKit.NewImage("Background", _anchor, new Color(0.07f, 0.07f, 0.09f, 0.85f), false, 16f);
            LayerUiKit.Place(bg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(120f, 32f));
            var fitter = bg.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 8, 8);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            _text = LayerUiKit.NewText("Text", bg.rectTransform, 15, TextAnchor.MiddleCenter, Color.white);
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _textLayout = _text.gameObject.AddComponent<LayoutElement>();

            _anchor.gameObject.SetActive(false);
        }

        void Start()
        {
            if (ServiceHub.TryResolve(out _session)) _session.Toast += Show;
        }

        void OnDestroy()
        {
            if (_session != null) _session.Toast -= Show;
        }

        /// <summary>Replaces any visible toast and restarts the timer.</summary>
        public void Show(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _text.text = message;
            _textLayout.preferredWidth = -1f;
            _textLayout.preferredWidth = Mathf.Min(_text.preferredWidth, MaxTextWidth);   // wrap long messages
            _anchor.gameObject.SetActive(true);
            _shownAt = Time.unscaledTime;
            _group.alpha = 0f;
        }

        void Update()
        {
            if (_shownAt < 0f) return;
            float t = Time.unscaledTime - _shownAt;
            if (t >= ShowSeconds)
            {
                _shownAt = -1f;
                _anchor.gameObject.SetActive(false);
                return;
            }
            _group.alpha = Mathf.Min(Mathf.Clamp01(t / FadeIn), Mathf.Clamp01((ShowSeconds - t) / FadeOut));
        }
    }
}
