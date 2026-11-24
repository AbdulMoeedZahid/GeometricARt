using GeoART.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace GeoART.App
{
    /// <summary>
    /// The Line and Circle hold menus and the Bands button.
    /// With a selection (Select, Move, Scale, Duplicate), holding Line or Circle opens the chips WITHOUT switching tool,
    /// and a chip also restyles the selected shapes' dash (one "Style" step); a plain tap still switches tools.
    /// Bands: tap turns the active layer's bands on or off (keeping its remembered mode and width); hold opens the layer
    /// popup for the active layer (LayerSidebar.OpenSettings); the label shows Off / Double / Weave, orange while on.
    /// Line mode: Solid or Dotted (<see cref="StrokeStyle.Dash"/>), shared by Line and Circle as the Dotted toggle was.
    /// Tap Line/Circle selects the tool and keeps the mode. Hold either one (<see cref="ToolHoldMenu.HoldSeconds"/>) to
    /// select it and open a row of mode chips to its left; slide onto a chip and release to pick it, or release first and
    /// tap one (tap outside to close). The Line icon is a solid or dashed diagonal and the Circle icon a solid or dashed
    /// ring, refreshed on StateChanged, so a mode set elsewhere shows too. A small corner triangle marks buttons that have
    /// a hold menu. The menus (rows and their full-screen blockers) sit under one "HoldMenus" rect, the last child of the
    /// canvas and a nested canvas of its own with sorting order <see cref="HoldMenusSortingOrder"/> (112): above the
    /// column, the action bar and Undo (105), the Designs button (106) and the layer sidebar (110), below the colour wheel
    /// and the Designs panel (115) and the toasts (120). So an open row draws over the sidebar, and its blocker takes the
    /// closing touch even on the sidebar or the Designs button: that touch only closes the menu (until P1's fix it pressed
    /// them). A row is also kept right of the layer sidebar's pill (shrunk if it must be, see ToolHoldMenu.LeftLimit),
    /// so the palette's left chips never slide under it on narrow (21:9) screens. Opening one menu closes the
    /// others (the Colour palette, ToolBar.Colour.cs, is one of them), and a re-layout (safe area, rotation) closes them
    /// all, since a row is placed only when it opens.
    /// After the first placement a one-time toast says "Hold Line or Circle for line styles" (PlayerPrefs flag; only in
    /// Play mode, so EditMode tests never write it).
    /// </summary>
    public sealed partial class ToolBar
    {
        const float ChipW = 56f, ChipH = 52f, ChipSpacing = 8f, HoldMarkSize = 8f, SidebarGap = 4f;

        /// <summary>Sorting order of the open menus' canvas: over the layer sidebar (110), under the wheel (115).</summary>
        public const int HoldMenusSortingOrder = 112;
        const string LineMenuHintKey = "GeoART.Hint.LineMenu";
        const string LineMenuHint = "Hold Line or Circle for line styles";

        static readonly string[] s_ModeNames = { "Solid", "Dotted" };   // index = (int)LineStyle

        RectTransform _holdMenus;
        ToolHoldMenu _lineMenu, _circleMenu, _bandsMenu, _snapMenu;
        Image _bandsBg;
        RawImage _lineIcon, _circleIcon;
        Texture _lineSolidGlyph, _lineDottedGlyph, _circleSolidGlyph, _circleDottedGlyph;
        Text _bandsLabel;

        void BuildHoldMenus(Font font)
        {
            // A nested canvas with its own sorting order (and raycaster, which ranks its hits by that order), so the rows
            // and blockers sit above the other UI canvases, not only above this one's column.
            var go = new GameObject("HoldMenus", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            go.transform.SetParent(_canvas.transform, false);
            var menusCanvas = go.GetComponent<Canvas>();
            menusCanvas.overrideSorting = true;
            menusCanvas.sortingOrder = HoldMenusSortingOrder;
            _holdMenus = (RectTransform)go.transform;
            _holdMenus.anchorMin = Vector2.zero;
            _holdMenus.anchorMax = Vector2.one;
            _holdMenus.offsetMin = _holdMenus.offsetMax = Vector2.zero;

            _lineIcon = _lineBg.transform.Find("Icon").GetComponent<RawImage>();
            _circleIcon = _circleBg.transform.Find("Icon").GetComponent<RawImage>();
            _lineSolidGlyph = MakeGlyph(Glyph.Line);
            _lineDottedGlyph = MakeGlyph(Glyph.Dotted);
            _circleSolidGlyph = MakeGlyph(Glyph.Circle);
            _circleDottedGlyph = MakeGlyph(Glyph.CircleDotted);

            System.Func<int> dash = () => _session != null ? (int)_session.Style.Dash : -1;
            _lineMenu = AddHoldMenu(_lineBg, "Line", new[] { Glyph.Line, Glyph.Dotted }, s_ModeNames, font, dash, PickLineMode,
                () => HoldTool(ToolId.Line));
            _circleMenu = AddHoldMenu(_circleBg, "Circle", new[] { Glyph.Circle, Glyph.CircleDotted }, s_ModeNames, font, dash,
                PickLineMode, () => HoldTool(ToolId.Circle));
            // Snap: tap toggles snapping; hold opens the H/V lock chip (later the angle-lock steps join it).
            _snapMenu = AddHoldMenu(_snapBg, "Snap", new[] { Glyph.HV }, new[] { "H/V" }, font,
                () => _session != null && _session.OrthoLock ? 0 : -1, _ => ToggleOrthoLock(), null);
            BuildPalette(font);
            // One menu at a time (two fingers can hold two buttons).
            var menus = new[] { _lineMenu, _circleMenu, _bandsMenu, _snapMenu, _colourMenu };
            foreach (var m in menus)
            {
                m.LeftLimit = SidebarRightEdge;
                ToolHoldMenu self = m;
                m.Opened += () => { foreach (var other in menus) if (other != self) other.Close(); };
            }
        }

        /// <summary>
        /// A <see cref="ToolHoldMenu"/> on <paramref name="button"/> with one chip per option (named
        /// "<paramref name="name"/> menu: label"), a charging overlay and the corner mark. <paramref name="getCurrent"/>
        /// gives the highlighted chip, <paramref name="onPick"/> applies a chip. The chips form one row, or a grid of
        /// <paramref name="columns"/> chips per row (filled row by row from the top left) of <paramref name="chipW"/> x
        /// ChipH chips <paramref name="spacing"/> apart.
        /// </summary>
        ToolHoldMenu AddHoldMenu(Image button, string name, Glyph[] glyphs, string[] labels, Font font,
            System.Func<int> getCurrent, System.Action<int> onPick, System.Action onHold,
            int columns = 0, float chipW = ChipW, float spacing = ChipSpacing)
        {
            int cols = columns > 0 ? Mathf.Min(columns, glyphs.Length) : glyphs.Length;
            int rows = (glyphs.Length + cols - 1) / cols;
            var blocker = new GameObject(name + " menu blocker", typeof(RectTransform), typeof(Image));
            var brt = (RectTransform)blocker.transform;
            brt.SetParent(_holdMenus, false);
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            var bimg = blocker.GetComponent<Image>();
            bimg.color = new Color(0f, 0f, 0f, 0f);
            bimg.raycastTarget = true;
            bimg.canvasRenderer.cullTransparentMesh = false;   // a clear hit area must still be hit

            var rowGo = new GameObject(name + " menu", typeof(RectTransform));
            var row = (RectTransform)rowGo.transform;
            row.SetParent(_holdMenus, false);
            row.sizeDelta = new Vector2(cols * chipW + (cols - 1) * spacing, rows * ChipH + (rows - 1) * spacing);
            AddTouchBlocker(rowGo);   // the gap between chips is part of the menu, not "outside"
            var chips = new Image[glyphs.Length];
            for (int i = 0; i < glyphs.Length; i++)
            {
                chips[i] = MakeButton(labels[i], glyphs[i], font, row, () => { }, out Text text);
                chips[i].gameObject.name = name + " menu: " + labels[i];
                text.horizontalOverflow = HorizontalWrapMode.Overflow;   // "Custom" / "Wheel…" never wrap away on a narrow chip
                var rt = chips[i].rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);   // the hover enlargement grows around the chip's centre
                rt.sizeDelta = new Vector2(chipW, ChipH);
                rt.anchoredPosition = new Vector2(i % cols * (chipW + spacing) + chipW * 0.5f,
                    -(i / cols) * (ChipH + spacing) - ChipH * 0.5f);
            }

            var charge = AddOverlay(button.transform, "Charge");
            AddHoldMark(button.transform);
            var menu = button.gameObject.AddComponent<ToolHoldMenu>();
            menu.Configure(charge, row, chips, blocker, getCurrent, onPick, onHold, Accent, Idle);
            return menu;
        }

        /// <summary>
        /// World x a menu row must stay right of: the layer sidebar's pill plus <see cref="SidebarGap"/> (NaN when there
        /// is no sidebar). On the overlay canvases world x is screen x.
        /// </summary>
        float SidebarRightEdge()
        {
            var sidebar = GetComponent<LayerSidebar>();
            if (sidebar == null || !sidebar.TryGetPillWorldRect(out Rect pill)) return float.NaN;
            return pill.xMax + SidebarGap * _canvas.transform.lossyScale.x;
        }

        /// <summary>A non-raycast rounded overlay filling the button (the hold's "charging" tint), starts clear.</summary>
        Image AddOverlay(Transform button, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(button, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = _rounded;
            img.type = Image.Type.Sliced;
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = false;
            return img;
        }

        /// <summary>The "hold for more" corner triangle, bottom-right inside the button.</summary>
        void AddHoldMark(Transform button)
        {
            var go = new GameObject("HoldMark", typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform)go.transform;
            rt.SetParent(button, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(HoldMarkSize, HoldMarkSize);
            rt.anchoredPosition = new Vector2(-4f, 4f);
            var img = go.GetComponent<RawImage>();
            img.texture = MakeGlyph(Glyph.HoldMark);
            img.color = new Color(1f, 1f, 1f, 0.7f);
            img.raycastTarget = false;
        }

        /// <summary>Tap on Line/Circle: select the tool, unless this release ends a hold (then the menu handled it).</summary>
        void TapTool(ToolHoldMenu menu, ToolId tool)
        {
            if (menu != null && menu.SuppressClick) return;
            _session?.SetTool(tool);
        }

        bool HasSelection => _session != null && _session.Selection.Ids.Count > 0;

        /// <summary>A hold on Line/Circle selects the tool, unless something is selected: then the chips restyle it.</summary>
        void HoldTool(ToolId tool)
        {
            if (!HasSelection) _session?.SetTool(tool);
        }

        void PickLineMode(int index)
        {
            if (_session == null) return;
            _session.Style = _session.Style.WithDash((LineStyle)index);   // raises StateChanged -> Refresh (icons)
            if (HasSelection) _session.RestyleSelection(StyleParts.Dash, _session.Style);
        }

        void RefreshLineMode()
        {
            if (_lineIcon == null || _session == null) return;
            bool dotted = _session.Style.Dash == LineStyle.Dotted;
            Texture line = dotted ? _lineDottedGlyph : _lineSolidGlyph;
            Texture circle = dotted ? _circleDottedGlyph : _circleSolidGlyph;
            if (_lineIcon.texture != line) _lineIcon.texture = line;
            if (_circleIcon.texture != circle) _circleIcon.texture = circle;
        }

        void CloseHoldMenus()
        {
            if (_lineMenu != null) _lineMenu.Close();
            if (_circleMenu != null) _circleMenu.Close();
            if (_bandsMenu != null) _bandsMenu.Close();
            if (_snapMenu != null) _snapMenu.Close();
            if (_colourMenu != null) _colourMenu.Close();
        }

        void MaybeShowLineMenuHint()
        {
            if (!Application.isPlaying || _boardSpace == null || !_boardSpace.IsPlaced) return;
            if (PlayerPrefs.GetInt(LineMenuHintKey, 0) != 0) return;
            var toast = GetComponent<ToastView>();
            if (toast == null) return;   // remembered only once it was actually shown
            toast.Show(LineMenuHint);
            PlayerPrefs.SetInt(LineMenuHintKey, 1);
            PlayerPrefs.Save();
        }

        // ---------- Bands button ----------

        /// <summary>The Bands button (the old Dotted slot): tap toggles the active layer's bands, hold opens its popup.</summary>
        void BuildBandsButton(Font font, ref float y)
        {
            _bandsBg = CreateButton("Bands", Glyph.Bands, font, ref y, OnBandsTap, out _bandsLabel);
            _bandsLabel.text = "Off";
            Image charge = AddOverlay(_bandsBg.transform, "Charge");
            AddHoldMark(_bandsBg.transform);
            _bandsMenu = _bandsBg.gameObject.AddComponent<ToolHoldMenu>();
            _bandsMenu.Configure(charge, OpenBandSettings);
        }

        void OnBandsTap()
        {
            if (_session == null || _bandsMenu.SuppressClick) return;
            Layer layer = _session.Board.FindLayer(_session.Board.ActiveLayerId);
            if (layer == null) return;
            LayerBands b = layer.Bands.Sanitized();   // a fresh layer: the defaults; an Off layer keeps its mode and width
            b.On = !b.On;
            _session.SetLayerBands(layer.Id, b);
        }

        void OpenBandSettings()
        {
            if (_session == null) return;
            var sidebar = GetComponent<LayerSidebar>();
            if (sidebar != null) sidebar.OpenSettings(_session.Board.ActiveLayerId);
        }

        void RefreshBands()
        {
            if (_bandsLabel == null || _session == null) return;
            Layer layer = _session.Board?.FindLayer(_session.Board.ActiveLayerId);
            LayerBands b = layer != null ? layer.Bands.Sanitized() : default;
            string label = !b.On ? "Off" : b.Mode == BandMode.Interlace ? "Weave" : "Double";
            if (_bandsLabel.text != label) _bandsLabel.text = label;
            SetColor(_bandsBg, b.On ? Accent : Idle);
        }
    }
}
