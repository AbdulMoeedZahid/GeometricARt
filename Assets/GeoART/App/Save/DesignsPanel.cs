using System;
using System.Collections.Generic;
using System.Globalization;
using GeoART.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GeoART.App.Save
{
    /// <summary>
    /// Save and reopen designs. A "Designs" button in the top-right corner of the safe area (same look as the ToolBar
    /// buttons, shown on the same rule: only while the board is placed) opens a centred panel:
    ///   name field            optional name for the next save (blank = "Design N")
    ///   Save current design   writes the board to a new file with that name
    ///   Clear board           tap twice (the first tap turns it red for 3 s) to start over with an empty board
    ///   list                  newest first; tap a name to rename it, Load replaces the board and closes the
    ///                         panel, Delete needs two taps
    /// A dark full-screen blocker sits behind the card, so nothing is drawn while the panel is open; tapping it closes
    /// the panel. Files live in <see cref="DesignLibrary.DefaultRoot"/> (or <see cref="DesignLibrary.LibraryRootOverride"/>).
    /// Nobody adds this component by hand: <see cref="DesignsPanelBootstrap"/> puts it next to every ToolBar when a
    /// scene loads, so no scene or prefab has to change.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DesignsPanel : MonoBehaviour
    {
        static readonly Color Accent = new Color(1f, 0.42f, 0f, 0.95f);
        static readonly Color Idle = new Color(0.08f, 0.08f, 0.1f, 0.78f);
        static readonly Color Danger = new Color(0.84f, 0.19f, 0.19f, 0.95f);   // Palette.Red, like the sidebar "-" chip
        // Solid colours on the card: the project renders in linear space, where low-alpha white looks far brighter.
        static readonly Color CardColor = new Color(0.07f, 0.07f, 0.09f, 1f);
        static readonly Color ListColor = new Color(0.045f, 0.045f, 0.055f, 1f);
        static readonly Color RowColor = new Color(0.13f, 0.13f, 0.155f, 1f);
        static readonly Color SoftButton = new Color(0.24f, 0.24f, 0.28f, 1f);   // neutral button on the card
        static readonly Color FieldColor = new Color(0.17f, 0.17f, 0.2f, 1f);    // text fields
        static readonly Color HintColor = new Color(1f, 1f, 1f, 0.45f);          // placeholder text
        static readonly Color SelectionColor = new Color(1f, 0.42f, 0f, 0.45f);
        static readonly Color BlockerColor = new Color(0f, 0f, 0f, 0.45f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.65f);

        // reference units (390x844 canvas), same sizes as the ToolBar where they overlap
        const float ButtonSize = 56f, EdgeMargin = 8f, ColumnGap = 8f;
        const float CardWidth = 340f, CardMaxHeight = 560f, CardScreenMargin = 40f, CardRadius = 18f, Pad = 12f;
        const float HeaderH = 44f, NameFieldH = 40f, ActionH = 44f, StatusH = 22f, RowH = 64f;
        const float ConfirmSeconds = 3f;

        const string ClearArm = "\nclear";   // armed key for the Clear button (row keys are design ids, never start with \n)

        IEditorSession _session;
        IBoardSpace _boardSpace;
        DesignLibrary _library;

        Canvas _buttonCanvas, _panelCanvas;
        RectTransform _buttonArea, _button, _panelArea, _card, _listRect, _content;
        Image _clearBg;
        Text _clearLabel, _status, _emptyText, _namePlaceholder;
        InputField _nameInput;
        InputField _renameField;   // the open rename field, if any
        string _renamingId;
        Texture2D _glyph;
        Rect _safeArea;
        int _screenW, _screenH;

        readonly Dictionary<string, (Image bg, Text label)> _deleteButtons = new Dictionary<string, (Image, Text)>();
        readonly List<DesignEntry> _entries = new List<DesignEntry>();
        string _armed;
        float _armedUntil;

        public bool IsOpen => _panelCanvas != null && _panelCanvas.gameObject.activeSelf;

        public string StatusText => _status != null ? _status.text : "";

        /// <summary>The name field above the Save button (blank = "Design N").</summary>
        public InputField NameInput => _nameInput;

        /// <summary>The entries shown in the list (as of the last Open/Save/Delete).</summary>
        public IReadOnlyList<DesignEntry> Entries => _entries;

        /// <summary>The folder is read every time, so setting the override after Play starts still takes effect.</summary>
        public DesignLibrary Library
        {
            get
            {
                string root = DesignLibrary.LibraryRootOverride ?? DesignLibrary.DefaultRoot;
                if (_library == null || _library.Root != root) _library = new DesignLibrary(root);
                return _library;
            }
        }

        void Awake()
        {
            BuildButton();
            BuildPanel();
            ApplySafeArea();
            _buttonCanvas.gameObject.SetActive(false);
            _panelCanvas.gameObject.SetActive(false);
        }

        void Start()
        {
            ServiceHub.TryResolve(out _session);
            ServiceHub.TryResolve(out _boardSpace);
            if (_boardSpace != null) _boardSpace.StateChanged += OnPlacementChanged;
            RefreshVisibility();
        }

        void OnDestroy()
        {
            if (_boardSpace != null) _boardSpace.StateChanged -= OnPlacementChanged;
            if (_glyph != null) Destroy(_glyph);
        }

        void LateUpdate()
        {
            if (UiScreen.SafeArea != _safeArea || UiScreen.Width != _screenW || UiScreen.Height != _screenH) ApplySafeArea();
            if (_armed != null && Time.unscaledTime > _armedUntil) Disarm();
        }

        void OnPlacementChanged(PlacementState _) => RefreshVisibility();

        void RefreshVisibility()
        {
            bool visible = _session != null && (_boardSpace == null || _boardSpace.IsPlaced);
            if (_buttonCanvas.gameObject.activeSelf != visible) _buttonCanvas.gameObject.SetActive(visible);
            if (!visible && IsOpen) Close();
        }

        // ---------- public API (buttons call these; so do the Play Mode checks) ----------

        public void Open()
        {
            if (_session == null) return;
            Disarm();
            _panelCanvas.gameObject.SetActive(true);
            RebuildList();
            SetStatus(_entries.Count > 0 ? "Tap a name to rename it." : "");
        }

        public void Close()
        {
            Disarm();
            FinishRename();   // a rename in progress is kept, like tapping somewhere else
            if (_panelCanvas.gameObject.activeSelf) _panelCanvas.gameObject.SetActive(false);
        }

        /// <summary>Saves the board under <paramref name="name"/>, or under the name field's text when null.</summary>
        public bool SaveCurrent(string name = null)
        {
            Disarm();
            if (_session == null || _session.Board == null)
            {
                SetStatus("There is no board to save.");
                return false;
            }
            if (name == null && _nameInput != null) name = _nameInput.text;
            if (!Library.TrySave(_session.Board, out DesignEntry entry, out string error, name))
            {
                SetStatus(error);
                return false;
            }
            if (_nameInput != null) _nameInput.text = "";
            RebuildList();
            SetStatus($"Saved \"{entry.Name}\"");
            return true;
        }

        /// <summary>Renames a saved design. The name buttons in the list end up here after editing.</summary>
        public bool RenameDesign(string id, string newName)
        {
            if (!Library.TryRename(id, newName, out DesignEntry entry, out string error))
            {
                SetStatus(error);
                return false;
            }
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Id == id) _entries[i] = entry;
            // Update the row in place instead of rebuilding the list: this can run while a field is being
            // deactivated, and Unity does not allow re-parenting objects at that moment.
            Transform row = _content != null ? _content.Find("Row_" + id) : null;
            Transform label = row != null ? row.Find("NameButton/Name") : null;
            if (label != null) label.GetComponent<Text>().text = entry.Name;
            if (_namePlaceholder != null) _namePlaceholder.text = PlaceholderText();
            SetStatus($"Renamed to \"{entry.Name}\"");
            return true;
        }

        public bool LoadDesign(string id)
        {
            Disarm();
            if (_session == null) return false;
            if (!Library.TryLoad(id, out Board board, out string error))
            {
                SetStatus(error);
                return false;
            }
            string name = NameOf(id);
            _session.LoadBoard(board);   // also cancels a stroke in progress
            SetStatus($"Loaded \"{name}\"");
            Close();
            var toast = GetComponent<ToastView>();
            if (toast != null) toast.Show($"Loaded \"{name}\"");
            return true;
        }

        public bool DeleteDesign(string id)
        {
            Disarm();
            string name = NameOf(id);
            if (!Library.TryDelete(id, out string error))
            {
                SetStatus(error);
                RebuildList();
                return false;
            }
            RebuildList();
            SetStatus($"Deleted \"{name}\"");
            return true;
        }

        public void ClearBoard()
        {
            Disarm();
            if (_session == null) return;
            _session.ClearBoard();
            SetStatus("Board cleared");
        }

        // ---------- list ----------

        void RebuildList()
        {
            DropRename();
            _entries.Clear();
            _entries.AddRange(Library.List());
            _deleteButtons.Clear();
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                Transform child = _content.GetChild(i);
                child.SetParent(null, false);   // out of the list now; Destroy only happens at the end of the frame
                Destroy(child.gameObject);
            }
            foreach (var e in _entries) BuildRow(e);
            _emptyText.gameObject.SetActive(_entries.Count == 0);
            if (_namePlaceholder != null) _namePlaceholder.text = PlaceholderText();
            _content.anchoredPosition = Vector2.zero;   // back to the top (newest)
        }

        string PlaceholderText() => "Name it, or leave blank for \"" + Library.NextDefaultName() + "\"";

        string NameOf(string id)
        {
            foreach (var e in _entries)
                if (e.Id == id) return e.Name;
            return id;
        }

        void BuildRow(DesignEntry e)
        {
            Image bg = LayerUiKit.NewImage("Row_" + e.Id, _content, RowColor, true, 10f);
            var layout = bg.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = RowH;
            RectTransform row = bg.rectTransform;

            const float loadW = 56f, deleteW = 64f, buttonH = 40f, gap = 6f;
            float textRight = 8f + deleteW + gap + loadW + 8f;

            string id = e.Id;
            // The name is a button: tapping it swaps in a text field to rename the design.
            Image nameHit = LayerUiKit.NewImage("NameButton", row, Color.clear, true, 6f);
            StretchTop(nameHit.rectTransform, 4f, textRight - 4f, 3f, 25f);
            var nameButton = nameHit.gameObject.AddComponent<Button>();
            nameButton.transition = Selectable.Transition.None;
            nameButton.navigation = new Navigation { mode = Navigation.Mode.None };
            nameButton.onClick.AddListener(() => BeginRename(id));
            Text name = LayerUiKit.NewText("Name", nameHit.rectTransform, 15, TextAnchor.MiddleLeft, Color.white);
            name.fontStyle = FontStyle.Bold;
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.verticalOverflow = VerticalWrapMode.Truncate;
            name.text = e.Name;
            StretchTop(name.rectTransform, 6f, 0f, 3f, 20f);

            Text info = LayerUiKit.NewText("Info", row, 11, TextAnchor.UpperLeft, Dim);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;
            info.verticalOverflow = VerticalWrapMode.Truncate;
            info.resizeTextForBestFit = true;
            info.resizeTextMinSize = 9;
            info.resizeTextMaxSize = 11;
            info.text = InfoLine(e);
            StretchTop(info.rectTransform, 10f, textRight, 27f, 32f);

            var (deleteBg, deleteLabel) = NewButton("DeleteButton", row, "Delete", 13, SoftButton, 10f, () => OnDeleteTapped(id));
            LayerUiKit.Place(deleteBg.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f),
                new Vector2(deleteW, buttonH));
            _deleteButtons[id] = (deleteBg, deleteLabel);

            var (loadBg, _) = NewButton("LoadButton", row, "Load", 13, Accent, 10f, () => LoadDesign(id));
            LayerUiKit.Place(loadBg.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-8f - deleteW - gap, 0f), new Vector2(loadW, buttonH));
        }

        /// <summary>"Nov 17, 3:04 PM" on the first line, "2 layers · 14 shapes · Hex 3x3" on the second.</summary>
        static string InfoLine(DesignEntry e)
        {
            var c = CultureInfo.InvariantCulture;
            string when = e.SavedUtc == DateTime.MinValue ? "unknown date" : e.SavedUtc.ToLocalTime().ToString("MMM d, h:mm tt", c);
            string layers = e.LayerCount == 1 ? "1 layer" : e.LayerCount.ToString(c) + " layers";
            string shapes = e.ShapeCount == 1 ? "1 shape" : e.ShapeCount.ToString(c) + " shapes";
            string grid = (e.GridKind == GridKind.Hex ? "Hex " : "Square ") + e.GridN.ToString(c) + "x" + e.GridN.ToString(c);
            return when + "\n" + layers + " \u00b7 " + shapes + " \u00b7 " + grid;
        }

        // ---------- rename in the list ----------

        /// <summary>Shows a text field over the design's name, focused with the whole name selected.</summary>
        public void BeginRename(string id)
        {
            Disarm();
            FinishRename();
            Transform row = _content.Find("Row_" + id);
            Transform nameButton = row != null ? row.Find("NameButton") : null;
            if (nameButton == null) return;

            InputField field = NewInputField("RenameInput", row, "Type a name", 15);
            var rt = (RectTransform)field.transform;
            var src = (RectTransform)nameButton;
            rt.anchorMin = src.anchorMin;
            rt.anchorMax = src.anchorMax;
            rt.pivot = src.pivot;
            rt.offsetMin = src.offsetMin;
            rt.offsetMax = src.offsetMax;
            field.textComponent.fontStyle = FontStyle.Bold;
            field.text = NameOf(id);
            nameButton.gameObject.SetActive(false);

            _renameField = field;
            _renamingId = id;
            field.onEndEdit.AddListener(OnRenameEnded);
            SetStatus("Type the new name, then press Enter (Done on the phone).");
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(field.gameObject);
            field.ActivateInputField();
        }

        void OnRenameEnded(string text)
        {
            InputField field = _renameField;
            string id = _renamingId;
            if (field == null || id == null) return;
            _renameField = null;
            _renamingId = null;
            field.onEndEdit.RemoveListener(OnRenameEnded);

            bool unchanged = field.wasCanceled || DesignLibrary.CleanName(text) == NameOf(id);
            if (!unchanged) RenameDesign(id, text);
            else SetStatus("");

            Transform row = field.transform.parent;
            if (row != null)
            {
                Transform nameButton = row.Find("NameButton");
                if (nameButton != null) nameButton.gameObject.SetActive(true);
            }
            field.gameObject.SetActive(false);   // hidden now, destroyed at the end of the frame
            Destroy(field.gameObject);
        }

        /// <summary>Ends an open rename the normal way and keeps what was typed.</summary>
        void FinishRename()
        {
            if (_renameField != null) _renameField.DeactivateInputField();   // raises onEndEdit -> OnRenameEnded
            if (_renameField != null) OnRenameEnded(_renameField.text);      // was not focused yet: end it by hand
        }

        /// <summary>Forgets an open rename without saving it (the list is about to be rebuilt).</summary>
        void DropRename()
        {
            if (_renameField != null) _renameField.onEndEdit.RemoveListener(OnRenameEnded);
            _renameField = null;
            _renamingId = null;
        }

        // ---------- two-tap confirm ----------

        void OnClearTapped()
        {
            if (_armed == ClearArm)
            {
                ClearBoard();
                return;
            }
            Arm(ClearArm);
        }

        void OnDeleteTapped(string id)
        {
            if (_armed == id)
            {
                DeleteDesign(id);
                return;
            }
            Arm(id);
        }

        void Arm(string key)
        {
            Disarm();
            _armed = key;
            _armedUntil = Time.unscaledTime + ConfirmSeconds;
            if (key == ClearArm)
            {
                _clearBg.color = Danger;
                _clearLabel.text = "Tap again to clear";
            }
            else if (_deleteButtons.TryGetValue(key, out var b))
            {
                b.bg.color = Danger;
                b.label.text = "Sure?";
            }
        }

        void Disarm()
        {
            if (_armed == null) return;
            if (_armed == ClearArm)
            {
                _clearBg.color = SoftButton;
                _clearLabel.text = "Clear board";
            }
            else if (_deleteButtons.TryGetValue(_armed, out var b) && b.bg != null)
            {
                b.bg.color = SoftButton;
                b.label.text = "Delete";
            }
            _armed = null;
        }

        void SetStatus(string s)
        {
            if (_status != null) _status.text = s ?? "";
        }

        // ---------- building ----------

        void BuildButton()
        {
            _buttonCanvas = LayerUiKit.CreateCanvas(transform, "DesignsButton", 106, true);
            _buttonArea = LayerUiKit.NewRect("SafeArea", _buttonCanvas.transform);

            var (bg, label) = NewButton("DesignsButton", _buttonArea, "Designs", 11, Idle, 10f, Open);
            _button = bg.rectTransform;
            LayerUiKit.Place(_button, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-EdgeMargin, -EdgeMargin),
                new Vector2(ButtonSize, ButtonSize));
            // same layout as a ToolBar button: 30 px icon at the top, 11 pt label at the bottom
            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.sizeDelta = new Vector2(0f, 18f);
            lrt.anchoredPosition = new Vector2(0f, 3f);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
            var irt = (RectTransform)iconGo.transform;
            irt.SetParent(_button, false);
            LayerUiKit.Place(irt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -5f), new Vector2(30f, 30f));
            var icon = iconGo.GetComponent<RawImage>();
            _glyph = MakeStackGlyph();
            icon.texture = _glyph;
            icon.raycastTarget = false;
        }

        void BuildPanel()
        {
            _panelCanvas = LayerUiKit.CreateCanvas(transform, "DesignsPanel", 115, true);

            // Full screen and a raycast target: board input ignores touches that start on UI, so nothing is drawn
            // while the panel is open. A tap on it closes the panel.
            Image blocker = LayerUiKit.NewImage("Blocker", _panelCanvas.transform, BlockerColor, true);
            blocker.sprite = null;
            blocker.type = Image.Type.Simple;
            RectTransform brt = blocker.rectTransform;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            var blockerButton = blocker.gameObject.AddComponent<Button>();
            blockerButton.transition = Selectable.Transition.None;
            blockerButton.navigation = new Navigation { mode = Navigation.Mode.None };
            blockerButton.onClick.AddListener(Close);

            _panelArea = LayerUiKit.NewRect("SafeArea", _panelCanvas.transform);
            _card = LayerUiKit.NewImage("Card", _panelArea, CardColor, true, CardRadius).rectTransform;
            LayerUiKit.Place(_card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(CardWidth, CardMaxHeight));

            Text title = LayerUiKit.NewText("Title", _card, 18, TextAnchor.MiddleLeft, Color.white);
            title.fontStyle = FontStyle.Bold;
            title.text = "Designs";
            StretchTop(title.rectTransform, Pad + 4f, 56f, 8f, 30f);

            var (close, _) = NewButton("CloseButton", _card, "", 1, Color.white, 12f, Close);
            ColorBlock cb = close.GetComponent<Button>().colors;
            cb.normalColor = cb.highlightedColor = cb.selectedColor = Color.clear;   // only shows while pressed
            cb.pressedColor = new Color(1f, 1f, 1f, 0.2f);
            close.GetComponent<Button>().colors = cb;
            LayerUiKit.Place(close.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-6f, -6f),
                new Vector2(38f, 38f));
            Bar(close.rectTransform, 15f, 2.5f, 45f);
            Bar(close.rectTransform, 15f, 2.5f, -45f);

            float y = HeaderH;
            _nameInput = NewInputField("NameInput", _card, "Name it, or leave blank", 14);
            _namePlaceholder = (Text)_nameInput.placeholder;
            StretchTop((RectTransform)_nameInput.transform, Pad, Pad, y, NameFieldH);
            y += NameFieldH + 8f;

            const float saveW = 184f;
            var (save, _) = NewButton("SaveButton", _card, "Save current design", 14, Accent, 12f, () => SaveCurrent());
            LayerUiKit.Place(save.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, -y),
                new Vector2(saveW, ActionH));
            var (clear, clearLabel) = NewButton("ClearButton", _card, "Clear board", 14, SoftButton, 12f, OnClearTapped);
            LayerUiKit.Place(clear.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad + saveW + 8f, -y),
                new Vector2(CardWidth - 2f * Pad - saveW - 8f, ActionH));
            clearLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            clearLabel.resizeTextForBestFit = true;
            clearLabel.resizeTextMinSize = 10;
            clearLabel.resizeTextMaxSize = 14;
            _clearBg = clear;
            _clearLabel = clearLabel;
            y += ActionH + 6f;

            _status = LayerUiKit.NewText("Status", _card, 12, TextAnchor.MiddleLeft, Dim);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            _status.verticalOverflow = VerticalWrapMode.Truncate;
            StretchTop(_status.rectTransform, Pad + 2f, Pad, y, StatusH);
            y += StatusH + 4f;

            // List: ScrollRect (vertical) > Viewport (RectMask2D) > Content (VerticalLayoutGroup + ContentSizeFitter)
            Image listBg = LayerUiKit.NewImage("List", _card, ListColor, true, 12f);
            _listRect = listBg.rectTransform;
            _listRect.anchorMin = new Vector2(0f, 0f);
            _listRect.anchorMax = new Vector2(1f, 1f);
            _listRect.offsetMin = new Vector2(Pad, Pad);
            _listRect.offsetMax = new Vector2(-Pad, -y);

            RectTransform viewport = LayerUiKit.NewRect("Viewport", _listRect);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>();

            _content = LayerUiKit.NewRect("Content", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = _content.offsetMax = Vector2.zero;
            var vlg = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 6f;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = listBg.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = _content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            _emptyText = LayerUiKit.NewText("EmptyText", _listRect, 14, TextAnchor.MiddleCenter, Dim);
            _emptyText.text = "No saved designs yet";
            RectTransform ert = _emptyText.rectTransform;
            ert.anchorMin = Vector2.zero;
            ert.anchorMax = Vector2.one;
            ert.offsetMin = ert.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Single-line text field in the panel's style (legacy uGUI InputField, so no TextMeshPro). On the phone it opens
        /// the system keyboard; in the Editor it takes the computer keyboard. Names stop at
        /// <see cref="DesignLibrary.MaxNameLength"/> characters.
        /// </summary>
        static InputField NewInputField(string name, Transform parent, string placeholder, int fontSize)
        {
            Image bg = LayerUiKit.NewImage(name, parent, FieldColor, true, 10f);
            var field = bg.gameObject.AddComponent<InputField>();
            field.targetGraphic = bg;
            ColorBlock cb = field.colors;
            cb.normalColor = cb.highlightedColor = cb.selectedColor = Color.white;
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            cb.fadeDuration = 0.06f;
            field.colors = cb;
            field.navigation = new Navigation { mode = Navigation.Mode.None };

            Text text = LayerUiKit.NewText("Text", bg.rectTransform, fontSize, TextAnchor.MiddleLeft, Color.white);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            Text hint = LayerUiKit.NewText("Placeholder", bg.rectTransform, fontSize, TextAnchor.MiddleLeft, HintColor);
            hint.fontStyle = FontStyle.Italic;
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.verticalOverflow = VerticalWrapMode.Truncate;
            hint.text = placeholder;
            foreach (RectTransform rt in new[] { text.rectTransform, hint.rectTransform })
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(10f, 4f);
                rt.offsetMax = new Vector2(-10f, -4f);
            }

            field.textComponent = text;
            field.placeholder = hint;
            field.lineType = InputField.LineType.SingleLine;
            field.contentType = InputField.ContentType.Standard;
            field.characterLimit = DesignLibrary.MaxNameLength;
            field.customCaretColor = true;
            field.caretColor = Color.white;
            field.caretWidth = 2;
            field.selectionColor = SelectionColor;
            return field;
        }

        /// <summary>Rounded button with a centred label. Returns the background (the Button's target) and the label.</summary>
        static (Image bg, Text label) NewButton(string name, Transform parent, string text, int fontSize, Color color,
            float radius, UnityEngine.Events.UnityAction onClick)
        {
            Image bg = LayerUiKit.NewImage(name, parent, color, true, radius);
            var button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            ColorBlock cb = button.colors;
            cb.normalColor = cb.highlightedColor = cb.selectedColor = Color.white;
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.fadeDuration = 0.06f;
            button.colors = cb;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(onClick);

            Text label = LayerUiKit.NewText("Label", bg.rectTransform, fontSize, TextAnchor.MiddleCenter, Color.white);
            label.text = text;
            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(4f, 0f);
            lrt.offsetMax = new Vector2(-4f, 0f);
            return (bg, label);
        }

        static void Bar(RectTransform parent, float length, float thickness, float angle)
        {
            Image bar = LayerUiKit.NewImage("Bar", parent, new Color(1f, 1f, 1f, 0.95f), false, thickness * 0.5f);
            LayerUiKit.Place(bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(length, thickness));
            bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>Stretches <paramref name="rt"/> across its parent, <paramref name="top"/> units down from the top.</summary>
        static void StretchTop(RectTransform rt, float left, float right, float top, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -top - height);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>
        /// Both canvases follow UiScreen.SafeArea. The button sits in the top-right corner; on a short screen where the
        /// ToolBar column (vertically centred on the right) would reach up to it, it moves one column to the left.
        /// The card is centred and as tall as fits, up to <see cref="CardMaxHeight"/>.
        /// </summary>
        void ApplySafeArea()
        {
            _safeArea = UiScreen.SafeArea;
            _screenW = UiScreen.Width;
            _screenH = UiScreen.Height;
            float w = Mathf.Max(1, UiScreen.Width), h = Mathf.Max(1, UiScreen.Height);
            var min = new Vector2(_safeArea.xMin / w, _safeArea.yMin / h);
            var max = new Vector2(_safeArea.xMax / w, _safeArea.yMax / h);
            foreach (var area in new[] { _buttonArea, _panelArea })
            {
                area.anchorMin = min;
                area.anchorMax = max;
                area.offsetMin = area.offsetMax = Vector2.zero;
            }

            float safeH = _safeArea.height / LayerUiKit.ScaleFactor();
            float columnTop = (safeH - ToolBar.ColumnHeight) * 0.5f;   // free space above the ToolBar column
            bool clash = columnTop < EdgeMargin + ButtonSize + ColumnGap;
            _button.anchoredPosition = new Vector2(clash ? -(EdgeMargin + ButtonSize + ColumnGap) : -EdgeMargin, -EdgeMargin);

            _card.sizeDelta = new Vector2(CardWidth, Mathf.Max(200f, Mathf.Min(CardMaxHeight, safeH - CardScreenMargin)));
        }

        // ---------- icon: a stack of three sheets (white on transparent, anti-aliased with a signed distance) ----------

        const int GlyphPx = 64;

        static Texture2D MakeStackGlyph()
        {
            var tex = new Texture2D(GlyphPx, GlyphPx, TextureFormat.RGBA32, false)
            {
                name = "Designs glyph",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[GlyphPx * GlyphPx];
            var half = new Vector2(0.25f, 0.25f);
            const float radius = 0.07f, stroke = 0.04f;
            for (int j = 0; j < GlyphPx; j++)
            for (int i = 0; i < GlyphPx; i++)
            {
                var p = new Vector2((i + 0.5f) / GlyphPx, (j + 0.5f) / GlyphPx);
                float front = RoundBox(p, new Vector2(0.41f, 0.41f), half, radius);
                float middle = RoundBox(p, new Vector2(0.5f, 0.5f), half, radius);
                float back = RoundBox(p, new Vector2(0.59f, 0.59f), half, radius);
                // each sheet's outline is hidden where a sheet in front of it covers it
                float d = Mathf.Abs(front) - stroke;
                d = Mathf.Min(d, Mathf.Max(Mathf.Abs(middle) - stroke, -(front - stroke)));
                d = Mathf.Min(d, Mathf.Max(Mathf.Abs(back) - stroke, -(Mathf.Min(front, middle) - stroke)));
                float a = Mathf.Clamp01(0.5f - d * GlyphPx);
                px[j * GlyphPx + i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Signed distance to a rounded rectangle (negative inside).</summary>
        static float RoundBox(Vector2 p, Vector2 center, Vector2 half, float r)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - center.x) - half.x + r, Mathf.Abs(p.y - center.y) - half.y + r);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }
    }

    /// <summary>
    /// Adds a <see cref="DesignsPanel"/> to every GameObject that has a ToolBar, after each scene load. This is how the
    /// feature reaches Main, Sandbox_A_AR and Sandbox_A_Flat without editing them.
    /// </summary>
    static class DesignsPanelBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => SceneManager.sceneLoaded -= OnSceneLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterFirstScene()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;   // never twice, even when the domain is not reloaded
            SceneManager.sceneLoaded += OnSceneLoaded;
            AttachAll();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachAll();

        static void AttachAll()
        {
            foreach (var toolBar in UnityEngine.Object.FindObjectsByType<ToolBar>())
                if (toolBar != null && toolBar.GetComponent<DesignsPanel>() == null)
                    toolBar.gameObject.AddComponent<DesignsPanel>();
        }
    }
}
