using Content.Client._EinsteinEngines.Language.Systems; // Forge-Change
using Content.Client._Forge.Features; // Forge-Change
using Content.Shared.Paper;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
using static Content.Shared.Paper.PaperComponent;

namespace Content.Client.Paper.UI;

[UsedImplicitly]
public sealed class PaperBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private PaperWindow? _window;

    private PaperAction _mode; // Forge-Change
    private bool _obscureWhileEditing; // Forge-Change

    public PaperBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PaperWindow>();
        _window.OnSaved += InputOnTextEntered;
        EntMan.System<LanguageSystem>().OnLanguagesChanged += RefreshLanguageOptions; // Forge-Change

        if (EntMan.TryGetComponent<PaperComponent>(Owner, out var paper))
        {
            _window.MaxInputLength = paper.ContentSize;
        }
        if (EntMan.TryGetComponent<PaperVisualsComponent>(Owner, out var visuals))
        {
            _window.InitVisuals(Owner, visuals);
        }
    }
    // Forge-Change-Start
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            EntMan.System<LanguageSystem>().OnLanguagesChanged -= RefreshLanguageOptions;

        base.Dispose(disposing);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        var paperState = (PaperBoundUserInterfaceState) state;
        _mode = paperState.Mode;
        if (_window != null && EntMan.TryGetComponent<PaperComponent>(Owner, out var paperComp))
            _window.MaxInputLength = paperComp.ContentSize;

        var visuals = EntMan.System<PaperLanguageVisualsSystem>();

        // Always obfuscate unknown languages. Skipping this in Write mode let
        // anyone who opened the editor read foreign plaintext. Literate writers
        // still get the real markup (TryFormatForReader leaves it unchanged).
        _obscureWhileEditing = false;
        if (visuals.TryFormatForReader(Owner, paperState, out var formatted))
        {
            paperState = formatted;
            _obscureWhileEditing = _mode == PaperAction.Write;
        }

        _window?.Populate(paperState, _obscureWhileEditing);
        RefreshLanguageOptions();
    }

    private void RefreshLanguageOptions()
    {
        if (_window == null)
            return;

        if (_mode != PaperAction.Write || _obscureWhileEditing)
        {
            _window.SetLanguageOptions([], null);
            return;
        }

        var langs = EntMan.System<PaperLanguageVisualsSystem>().GetWritableLanguages(Owner, out var selected);
        _window.SetLanguageOptions(langs, selected);
    }
    // Forge-Change-End

    private void InputOnTextEntered(string text)
    {
        SendMessage(new PaperInputTextMessage(text, _window?.GetSelectedLanguage())); // Forge-Change

        if (_window != null)
        {
            _window.Input.TextRope = Rope.Leaf.Empty;
            _window.Input.CursorPosition = new TextEdit.CursorPos(0, TextEdit.LineBreakBias.Top);
        }
    }
}
