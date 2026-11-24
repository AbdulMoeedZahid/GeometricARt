using GeoART.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace GeoART.App
{
    /// <summary>
    /// The Colour button (third in the style group: Bands, Width, Colour) and its palette. The palette replaced the colour
    /// pill at the bottom-left, which the Select action bar covered exactly when a colour was needed to restyle the
    /// selection.
    ///   Button    its icon is a disc in the current Style.Color inside a light outline ring (so black reads on the dark
    ///             button), or inside a rainbow ring when the current colour is the custom one; label "Colour"; the
    ///             corner mark of a button with a menu. Refreshed on StateChanged, BoardAppearance.Changed and
    ///             ColorBar.CustomChanged, so a colour set anywhere shows.
    ///   Palette   a <see cref="ToolHoldMenu"/> with <see cref="ToolHoldMenu.OpenOnPress"/>: the PRESS opens it, left of
    ///             the button and vertically centred on it (kept on screen), without switching tool or touching the
    ///             selection. Slide onto a chip and release to pick it, or release first and tap one; a touch outside
    ///             closes it on release, so that touch never draws (the hold menus' blocker). Chips (48 x 52, in rows of
    ///             five, 256 px wide, so the grid ends right of the layer sidebar on a 390-wide screen):
    ///               row 1   the five palette colours (BoardAppearance.ShapeColors: Black, White, Red, Green, Blue)
    ///               row 2   Custom (the wheel's last colour in a rainbow ring, grey until there is one), "Wheel…" (opens
    ///                       the colour wheel, ColorBar.Wheel.cs), Board (guide lines Black &lt;-&gt; White: the icon is
    ///                       the grid in the guide colour), and "No fill" while the Fill tool is active (only built when
    ///                       <see cref="FillEnabled"/>)
    ///             The current colour's chip is orange. A colour (palette or Custom) sets Style.Color, restyles a
    ///             non-empty selection (one "Style" step; the selection and the action bar stay) and closes the palette.
    ///             Board toggles the guides and leaves the palette open; Wheel… closes it and opens the wheel.
    /// The colour logic and the custom colour live in <see cref="ColorBar"/> (same GameObject); without one the colours and
    /// Board still work, Custom and Wheel… do nothing.
    /// </summary>
    public sealed partial class ToolBar
    {
        const float PaletteChipW = 48f, PaletteSpacing = 4f, SwatchSize = 20f, CustomSwatchSize = 16f, PlateSize = 26f;
        const int PaletteColumns = 5;

        static readonly Color SwatchOutline = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color NoCustom = new Color(0.35f, 0.35f, 0.38f, 1f);
        static readonly Color LightPlate = new Color(0.86f, 0.86f, 0.89f, 1f), DarkPlate = new Color(0.27f, 0.27f, 0.31f, 1f);

        Image _colourBg, _boardPlate;
        RawImage _colourRing, _colourSwatch, _customSwatch, _boardIcon;
        GameObject _noFillChip;
        ToolHoldMenu _colourMenu;
        ColorBar _colorBar;
        Texture2D _spectrumRing, _spectrumDisc;

        // Chip indices after the palette colours.
        static int CustomChip => BoardAppearance.ShapeColors.Length;
        static int WheelChip => CustomChip + 1;
        static int BoardChip => CustomChip + 2;
        static int NoFillChip => CustomChip + 3;

        /// <summary>The ColorBar next to this ToolBar (resolved on first use: scene installers may add it after Awake).</summary>
        ColorBar Colours
        {
            get
            {
                if (_colorBar == null)
                {
                    _colorBar = GetComponent<ColorBar>();
                    if (_colorBar != null) _colorBar.CustomChanged += RefreshColour;
                }
                return _colorBar;
            }
        }

        void BuildColourButton(Font font, ref float y)
        {
            _colourBg = CreateButton("Colour", Glyph.Ring, font, ref y, OnColourTap, out _);
            _colourRing = _colourBg.transform.Find("Icon").GetComponent<RawImage>();
            _colourRing.color = SwatchOutline;
            _colourSwatch = AddSwatch(_colourRing.transform, SwatchSize);
            _spectrumRing = ColorBar.MakeSpectrumTexture(64, 0.72f);
            _spectrumDisc = ColorBar.MakeSpectrumTexture(64, 0f);
        }

        /// <summary>The palette (from BuildHoldMenus, so it lives under the HoldMenus rect over everything else).</summary>
        void BuildPalette(Font font)
        {
            RgbaColor[] colors = BoardAppearance.ShapeColors;
            int count = NoFillChip + (FillEnabled ? 1 : 0);
            var glyphs = new Glyph[count];
            var labels = new string[count];
            for (int i = 0; i < colors.Length; i++)
            {
                glyphs[i] = Glyph.Ring;
                labels[i] = BoardAppearance.ShapeColorNames[i];
            }
            glyphs[CustomChip] = Glyph.Ring;
            labels[CustomChip] = "Custom";
            glyphs[WheelChip] = Glyph.Ring;
            labels[WheelChip] = "Wheel…";
            glyphs[BoardChip] = Glyph.Square;
            labels[BoardChip] = "Board";
            if (count > NoFillChip)
            {
                glyphs[NoFillChip] = Glyph.NoFill;
                labels[NoFillChip] = "No fill";
            }
            _colourMenu = AddHoldMenu(_colourBg, "Colour", glyphs, labels, font, CurrentColourChip, PickPaletteChip,
                RefreshColour, PaletteColumns, PaletteChipW, PaletteSpacing);
            _colourMenu.OpenOnPress = true;
            _colourMenu.KeepOpenOnPick = i => i == BoardChip;   // a toggle: see the board change, toggle back

            Transform row = _holdMenus.Find("Colour menu");
            for (int i = 0; i < colors.Length; i++)
            {
                RawImage icon = ChipIcon(row, i);
                icon.color = SwatchOutline;
                AddSwatch(icon.transform, SwatchSize).color = ColorBar.ToColor(colors[i]);
            }
            RawImage custom = ChipIcon(row, CustomChip);
            custom.texture = _spectrumRing;
            custom.color = Color.white;
            _customSwatch = AddSwatch(custom.transform, CustomSwatchSize);
            RawImage wheel = ChipIcon(row, WheelChip);
            wheel.texture = _spectrumDisc;
            wheel.color = Color.white;

            // Board: the grid glyph in the guide colour on a plate of the other colour (a tiny board).
            _boardIcon = ChipIcon(row, BoardChip);
            var plate = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            var prt = (RectTransform)plate.transform;
            prt.SetParent(row.GetChild(BoardChip), false);
            prt.SetSiblingIndex(_boardIcon.transform.GetSiblingIndex());   // under the icon
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 1f);
            prt.sizeDelta = new Vector2(PlateSize, PlateSize);
            prt.anchoredPosition = new Vector2(0f, -3f - (28f - PlateSize) * 0.5f);
            _boardPlate = plate.GetComponent<Image>();
            _boardPlate.sprite = _rounded;
            _boardPlate.type = Image.Type.Sliced;
            _boardPlate.raycastTarget = false;

            if (count > NoFillChip) _noFillChip = row.GetChild(NoFillChip).gameObject;
            RefreshColour();
        }

        static RawImage ChipIcon(Transform row, int index) => row.GetChild(index).Find("Icon").GetComponent<RawImage>();

        /// <summary>A centred, non-raycast disc of <paramref name="size"/> px under <paramref name="parent"/>, tinted by the caller.</summary>
        RawImage AddSwatch(Transform parent, float size)
        {
            var go = new GameObject("Swatch", typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = Vector2.zero;
            var img = go.GetComponent<RawImage>();
            img.texture = MakeGlyph(Glyph.Disc);
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A click on Colour (one without a press, e.g. from a test or an accessibility action) toggles the palette.</summary>
        void OnColourTap()
        {
            if (_colourMenu == null || _colourMenu.SuppressClick) return;   // the press already opened it
            if (_colourMenu.IsOpen) _colourMenu.Close();
            else _colourMenu.Show();
        }

        /// <summary>The highlighted chip: No fill while erasing fills, else the current colour's chip (a palette colour wins).</summary>
        int CurrentColourChip()
        {
            if (_session == null) return -1;
            bool erasing = FillEnabled && _session.ActiveTool == ToolId.Fill && _session.FillErase;
            if (erasing) return NoFillChip;
            int palette = PaletteIndex(_session.Style.Color);
            if (palette >= 0) return palette;
            return IsCustom(_session.Style.Color) ? CustomChip : -1;
        }

        static int PaletteIndex(RgbaColor c)
        {
            RgbaColor[] colors = BoardAppearance.ShapeColors;
            for (int i = 0; i < colors.Length; i++)
                if (colors[i].Equals(c)) return i;
            return -1;
        }

        bool IsCustom(RgbaColor c)
        {
            ColorBar bar = Colours;
            RgbaColor? custom = bar != null ? bar.CustomColor : (RgbaColor?)null;
            return custom.HasValue && custom.Value.Equals(c);
        }

        void PickPaletteChip(int index)
        {
            if (_session == null) return;
            ColorBar bar = Colours;
            if (index < CustomChip) ColorBar.ApplyColor(_session, BoardAppearance.ShapeColors[index]);
            else if (index == CustomChip) { if (bar != null) bar.SelectCustom(); }
            else if (index == WheelChip) { if (bar != null) bar.OpenWheel(); }
            else if (index == BoardChip) ColorBar.ToggleBoardGuides();   // BoardAppearance.Changed -> RefreshColour
            else ColorBar.ApplyNoFill(_session);
        }

        /// <summary>The Colour button's icon and the palette's live chips (custom swatch, Board, No fill).</summary>
        void RefreshColour()
        {
            if (_colourSwatch == null || _session == null) return;
            RgbaColor c = _session.Style.Color;
            bool custom = PaletteIndex(c) < 0 && IsCustom(c);
            Texture ring = custom ? _spectrumRing : MakeGlyph(Glyph.Ring);
            if (_colourRing.texture != ring) _colourRing.texture = ring;
            SetColor(_colourRing, custom ? Color.white : SwatchOutline);
            SetColor(_colourSwatch, ColorBar.ToColor(c));

            if (_customSwatch != null)
            {
                ColorBar bar = Colours;
                RgbaColor? k = bar != null ? bar.CustomColor : (RgbaColor?)null;
                SetColor(_customSwatch, k.HasValue ? ColorBar.ToColor(k.Value) : NoCustom);
            }
            if (_boardIcon != null)
            {
                bool white = BoardAppearance.Guides == BoardGuideColor.White;
                SetColor(_boardIcon, ColorBar.ToColor(white ? Palette.White : Palette.Black));
                SetColor(_boardPlate, white ? DarkPlate : LightPlate);
            }
            if (_noFillChip != null)
            {
                bool fill = _session.ActiveTool == ToolId.Fill;
                if (_noFillChip.activeSelf != fill) _noFillChip.SetActive(fill);
            }
        }
    }
}
