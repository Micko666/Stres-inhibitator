using System;
using System.Collections.Generic;
using System.Text;
using StressTraining.Core;
using StressTraining.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StressTraining.UI
{
    /// <summary>
    /// Profile list + new-profile username entry (spec §5).
    ///
    /// List mode: existing profiles sorted by last use, with last/next session info,
    /// plus "+ Novi profil".
    ///
    /// Create mode: an on-screen keyboard built from REAL uGUI Buttons, so the
    /// standard Quest right-controller ray + trigger can click every key. It used to
    /// be a plain Text strip with "[A]" brackets — nothing was clickable, controller
    /// navigation was switched off in UIManager, and the panel advertised A/B keys
    /// that were never polled, so the screen was a dead end. Every key, action and
    /// the explicit NAZAD button now goes through one path and one listener, added
    /// once in <see cref="Build"/> — a single press can never be delivered twice.
    ///
    /// Thumbstick + A/B remain a fallback (UiNavigationInput), and arrow keys/Enter/
    /// Esc work in the Editor.
    /// </summary>
    public sealed class ProfileSelectionPanel : MenuPanelBase
    {
        public event Action<string> ProfileSelected;              // userId
        public event Action<string> ProfileCreateRequested;       // proposed username
        public event Action DeveloperPanelRequested;

        private static readonly string[] CharRows =
        {
            "ABCDEFGHIJKLM",
            "NOPQRSTUVWXYZ",
            "0123456789-_."
        };
        private static readonly string[] ActionLabels = { "RAZMAK", "OBRIŠI", "SAČUVAJ", "OTKAŽI" };

        private const int ActionsRowIndex = 3;
        private const int BackRowIndex = 4;

        private static readonly Color KeyNormal = new Color(0.15f, 0.16f, 0.20f);
        private static readonly Color KeyHover = new Color(0.24f, 0.48f, 0.78f);
        private static readonly Color KeyPressed = new Color(0.12f, 0.68f, 0.48f);
        private static readonly Color KeyFocused = new Color(0.22f, 0.36f, 0.55f);
        private static readonly Color KeyDisabled = new Color(0.08f, 0.09f, 0.11f, 0.62f);
        private static readonly Color ActionNormal = new Color(0.17f, 0.20f, 0.27f);
        private static readonly Color SaveNormal = new Color(0.14f, 0.42f, 0.32f);
        private static readonly Color BackNormal = new Color(0.30f, 0.20f, 0.22f);

        private bool _createMode;
        private readonly StringBuilder _nameBuffer = new StringBuilder(UsernameValidator.MaxLength);
        private GameObject _createRoot;
        private Text _nameText;

        private readonly List<Button[]> _charButtons = new List<Button[]>(3);
        private Button[] _actionButtons;
        private Button _backButton;

        private int _rowIndex;
        private int _colIndex;

        /// <summary>Test hook: the name currently typed into the create-mode buffer.</summary>
        public string CurrentNameBuffer => _nameBuffer.ToString();
        /// <summary>Test hook: true while the new-profile keyboard is open.</summary>
        public bool IsCreateMode => _createMode;

        public override void Build(Transform canvasRoot, string panelName)
        {
            base.Build(canvasRoot, panelName);
            SetTitle("Izbor profila");

            _createRoot = new GameObject("CreateMode", typeof(RectTransform));
            _createRoot.transform.SetParent(PanelRoot.transform, false);
            UiBuilder.Stretch((RectTransform)_createRoot.transform, 20, 54, 20, 62);

            _nameText = UiBuilder.Text(_createRoot.transform, "Name", "_", 34, TextAnchor.MiddleCenter);
            SetRect(_nameText.rectTransform, 0.02f, 0.86f, 0.98f, 1f);

            BuildKeyboard();
            BuildActionRow();
            BuildBackButton();

            _createRoot.SetActive(false);
        }

        // ── create-mode construction (once; listeners are never re-added) ──

        private void BuildKeyboard()
        {
            // Three key rows; each key is its own Button so the ray can hit it.
            float[] rowTop = { 0.83f, 0.66f, 0.49f };
            const float rowHeight = 0.15f;

            for (int row = 0; row < CharRows.Length; row++)
            {
                string keys = CharRows[row];
                var buttons = new Button[keys.Length];
                float width = 1f / keys.Length;

                for (int col = 0; col < keys.Length; col++)
                {
                    char key = keys[col];
                    var button = CreateButton(_createRoot.transform, "Key_" + key,
                        key.ToString(), 24, KeyNormal);
                    SetRect(button.GetComponent<RectTransform>(),
                        col * width, rowTop[row] - rowHeight, (col + 1) * width, rowTop[row],
                        2f, 2f, -2f, -2f);
                    button.onClick.AddListener(() => AppendCharacter(key));
                    buttons[col] = button;
                }
                _charButtons.Add(buttons);
            }
        }

        private void BuildActionRow()
        {
            _actionButtons = new Button[ActionLabels.Length];
            float width = 1f / ActionLabels.Length;

            for (int i = 0; i < ActionLabels.Length; i++)
            {
                Color color = i == 2 ? SaveNormal : ActionNormal;
                var button = CreateButton(_createRoot.transform, "Action_" + ActionLabels[i],
                    ActionLabels[i], 20, color);
                SetRect(button.GetComponent<RectTransform>(),
                    i * width, 0.17f, (i + 1) * width, 0.31f, 3f, 2f, -3f, -2f);
                int index = i;
                button.onClick.AddListener(() => InvokeAction(index));
                _actionButtons[i] = button;
            }
        }

        private void BuildBackButton()
        {
            // Explicit, always-visible exit. Without it the panel had no working way
            // back to the profile list and the user could get stuck here.
            _backButton = CreateButton(_createRoot.transform, "BackButton",
                "← NAZAD (bez kreiranja profila)", 20, BackNormal);
            SetRect(_backButton.GetComponent<RectTransform>(), 0.24f, 0.01f, 0.76f, 0.14f);
            _backButton.onClick.AddListener(CancelCreate);
        }

        // ── list mode ────────────────────────────────────────────────────

        public void ShowProfiles(List<ProfileIndexEntry> profiles, bool developerMode)
        {
            _createMode = false;
            _createRoot.SetActive(false);
            ListRoot.gameObject.SetActive(true);
            SetTitle("Izbor profila");
            SetBody(profiles.Count == 0
                ? "Nema sačuvanih profila. Kreiraj novi profil za početak."
                : "Izaberi profil, ili kreiraj novi.");
            SetInfo("Usmjeri zrak desnim kontrolerom i pritisni okidač · B = nazad");

            var options = new List<(string, Action)>();
            foreach (var p in profiles)
            {
                string last = FormatWhen(p.lastSessionAtUtcIso, "nikad");
                string next = FormatWhen(p.nextRecommendedSessionAtUtcIso, "odmah");
                string id = p.userId;
                options.Add(($"{p.username}   (posljednja: {last} · naredna: {next})",
                    () => ProfileSelected?.Invoke(id)));
            }
            options.Add(("+  Novi profil", EnterCreateMode));
            if (developerMode)
                options.Add(("⚙  Developer panel", () => DeveloperPanelRequested?.Invoke()));
            SetOptions(options);
        }

        private static string FormatWhen(string utcIso, string fallback)
        {
            if (!UtcTime.TryParseIso(utcIso, out var utc)) return fallback;
            return utc.ToLocalTime().ToString("dd.MM. HH:mm");
        }

        // ── create mode ──────────────────────────────────────────────────

        private void EnterCreateMode()
        {
            _createMode = true;
            _nameBuffer.Clear();
            _rowIndex = 0;
            _colIndex = 0;
            ListRoot.gameObject.SetActive(false);
            _createRoot.SetActive(true);
            SetBody("");
            SetTitle("Novi profil — unesi ime");
            SetInfo("Zrak + okidač · A = potvrdi označeno · B = nazad · " +
                    UsernameValidator.MinLength + "–" + UsernameValidator.MaxLength + " znakova");
            RenderCreate();
        }

        /// <summary>OTKAŽI, NAZAD and B all land here — one exit, no dead end.</summary>
        private void CancelCreate()
        {
            if (!_createMode) return;
            _createMode = false;
            _nameBuffer.Clear();
            _createRoot.SetActive(false);
            OnBack?.Invoke();
        }

        private void AppendCharacter(char c)
        {
            if (!_createMode || _nameBuffer.Length >= UsernameValidator.MaxLength) return;
            _nameBuffer.Append(c);
            RenderCreate();
        }

        private void Backspace()
        {
            if (!_createMode || _nameBuffer.Length == 0) return;
            _nameBuffer.Length--;
            RenderCreate();
        }

        /// <summary>A space is only allowed between characters, never leading or doubled.</summary>
        private bool SpaceAllowed =>
            _nameBuffer.Length > 0 &&
            _nameBuffer.Length < UsernameValidator.MaxLength &&
            _nameBuffer[_nameBuffer.Length - 1] != ' ';

        private void AppendSpace()
        {
            if (!_createMode || !SpaceAllowed) return;
            _nameBuffer.Append(' ');
            RenderCreate();
        }

        /// <summary>Locally valid = non-empty and long enough. Duplicates are still
        /// rejected by the repository, which calls back into ShowUsernameError.</summary>
        private bool SaveAllowed =>
            UsernameValidator.Normalize(_nameBuffer.ToString()).Length >= UsernameValidator.MinLength;

        private void SaveProfileName()
        {
            if (!_createMode || !SaveAllowed) return;
            ProfileCreateRequested?.Invoke(UsernameValidator.Normalize(_nameBuffer.ToString()));
        }

        private void InvokeAction(int index)
        {
            switch (index)
            {
                case 0: AppendSpace(); break;
                case 1: Backspace(); break;
                case 2: SaveProfileName(); break;
                case 3: CancelCreate(); break;
            }
        }

        private void RenderCreate()
        {
            _nameText.text = _nameBuffer.Length == 0 ? "_" : _nameBuffer + "_";

            for (int row = 0; row < _charButtons.Count; row++)
                for (int col = 0; col < _charButtons[row].Length; col++)
                {
                    bool focused = _rowIndex == row && _colIndex == col;
                    Paint(_charButtons[row][col], KeyNormal, focused);
                    _charButtons[row][col].interactable =
                        _nameBuffer.Length < UsernameValidator.MaxLength;
                }

            for (int i = 0; i < _actionButtons.Length; i++)
            {
                bool focused = _rowIndex == ActionsRowIndex && _colIndex == i;
                Paint(_actionButtons[i], i == 2 ? SaveNormal : ActionNormal, focused);
            }
            _actionButtons[0].interactable = SpaceAllowed;
            _actionButtons[1].interactable = _nameBuffer.Length > 0;
            _actionButtons[2].interactable = SaveAllowed;
            _actionButtons[3].interactable = true;

            Paint(_backButton, BackNormal, _rowIndex == BackRowIndex);
            _backButton.interactable = true;
            FocusSelected();
        }

        private void FocusSelected()
        {
            Button focused = FocusedButton();
            if (focused == null || !focused.interactable || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(focused.gameObject);
        }

        private Button FocusedButton()
        {
            if (_rowIndex == BackRowIndex) return _backButton;
            if (_rowIndex == ActionsRowIndex)
                return _colIndex >= 0 && _colIndex < _actionButtons.Length
                    ? _actionButtons[_colIndex] : null;
            if (_rowIndex < 0 || _rowIndex >= _charButtons.Count) return null;
            var row = _charButtons[_rowIndex];
            return _colIndex >= 0 && _colIndex < row.Length ? row[_colIndex] : null;
        }

        private int ColumnCount(int row)
        {
            if (row == BackRowIndex) return 1;
            if (row == ActionsRowIndex) return ActionLabels.Length;
            return CharRows[row].Length;
        }

        // ── navigation fallback (thumbstick / A / B / keyboard) ───────────

        public override void OnNav(NavEvent nav)
        {
            if (!_createMode) { base.OnNav(nav); return; }

            const int rowCount = BackRowIndex + 1;
            switch (nav)
            {
                case NavEvent.Left:
                    _colIndex = (_colIndex - 1 + ColumnCount(_rowIndex)) % ColumnCount(_rowIndex);
                    break;
                case NavEvent.Right:
                    _colIndex = (_colIndex + 1) % ColumnCount(_rowIndex);
                    break;
                case NavEvent.Up:
                    _rowIndex = (_rowIndex - 1 + rowCount) % rowCount;
                    _colIndex = Mathf.Min(_colIndex, ColumnCount(_rowIndex) - 1);
                    break;
                case NavEvent.Down:
                    _rowIndex = (_rowIndex + 1) % rowCount;
                    _colIndex = Mathf.Min(_colIndex, ColumnCount(_rowIndex) - 1);
                    break;
                case NavEvent.Confirm:
                    ConfirmFocused();
                    return;                    // ConfirmFocused re-renders if needed
                case NavEvent.Back:
                    CancelCreate();
                    return;
            }
            RenderCreate();
        }

        /// <summary>A / Enter activates whatever is focused — the same single code
        /// path the ray click uses, so the on-screen A/B hint is truthful.</summary>
        private void ConfirmFocused()
        {
            if (_rowIndex == BackRowIndex) { CancelCreate(); return; }
            if (_rowIndex == ActionsRowIndex) { InvokeAction(_colIndex); return; }
            if (_rowIndex >= 0 && _rowIndex < CharRows.Length &&
                _colIndex >= 0 && _colIndex < CharRows[_rowIndex].Length)
                AppendCharacter(CharRows[_rowIndex][_colIndex]);
        }

        /// <summary>Shown when the coordinator rejects the proposed username. The
        /// keyboard stays open so the name can be corrected in place.</summary>
        public void ShowUsernameError(UsernameValidationResult result)
        {
            string msg;
            switch (result)
            {
                case UsernameValidationResult.Empty: msg = "Ime ne smije biti prazno."; break;
                case UsernameValidationResult.TooShort:
                    msg = "Ime je prekratko (min " + UsernameValidator.MinLength + " znaka)."; break;
                case UsernameValidationResult.TooLong:
                    msg = "Ime je predugačko (max " + UsernameValidator.MaxLength + " znaka)."; break;
                case UsernameValidationResult.DuplicateCaseInsensitive:
                    msg = "Profil sa tim imenom već postoji."; break;
                default: msg = "Ime sadrži nedozvoljene znakove."; break;
            }
            SetInfo("⚠ " + msg);
        }

        // ── small builders ───────────────────────────────────────────────

        private static void Paint(Button button, Color normal, bool focused)
        {
            if (button == null) return;
            var image = button.targetGraphic as Image;
            button.colors = new ColorBlock
            {
                normalColor = normal,
                highlightedColor = KeyHover,
                pressedColor = KeyPressed,
                selectedColor = focused ? KeyFocused : normal,
                disabledColor = KeyDisabled,
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
            if (image != null) image.color = focused ? KeyFocused : normal;
        }

        private static Button CreateButton(Transform parent, string name, string label,
            int fontSize, Color normal)
        {
            var image = UiBuilder.Image(parent, name, normal);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Paint(button, normal, false);

            var text = UiBuilder.Text(image.transform, "Label", label, fontSize,
                TextAnchor.MiddleCenter);
            UiBuilder.Stretch(text.rectTransform, 2f, 2f, 2f, 2f);
            text.raycastTarget = false;
            return button;
        }

        private static void SetRect(RectTransform rect, float minX, float minY,
            float maxX, float maxY, float left = 0f, float bottom = 0f,
            float right = 0f, float top = 0f)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }
    }
}
