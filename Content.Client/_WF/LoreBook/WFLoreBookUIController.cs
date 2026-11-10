using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._WF.LoreBook;
using Content.Shared.Guidebook;
using Robust.Shared.Configuration;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._WF.LoreBook;

/// <summary>Opens the lore book from its top-bar button, the game menu, the lobby, examine and lore links.</summary>
public sealed partial class WFLoreBookUIController : UIController, IOnStateExited<LobbyState>, IOnStateExited<GameplayState>,
    IOnStateEntered<LobbyState>, IOnStateEntered<GameplayState>
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>The book's edition. Raising it opens the book once for every player on their next lobby or round, at its front.</summary>
    public const int Edition = 1;

    private static readonly SoundSpecifier OpenSound = new SoundPathSpecifier("/Audio/_Goobstation/Items/handling/book_pickup.ogg");

    private WFLoreBookWindow? _window;
    private ProtoId<GuideEntryPrototype>? _lastEntry;

    /// <summary>The open book, if any.</summary>
    public WFLoreBookWindow? Window => _window;

    private MenuButton? LoreBookButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.LoreBookButton;

    public void OnStateEntered(LobbyState state)
    {
        ShowNewEdition();
    }

    public void OnStateEntered(GameplayState state)
    {
        ShowNewEdition();
    }

    /// <summary>Opens the book once for a player who has not seen this edition, then remembers that they have.</summary>
    private void ShowNewEdition()
    {
        if (_config.GetCVar(WFLoreBookCVars.SeenEdition) >= Edition)
            return;

        _config.SetCVar(WFLoreBookCVars.SeenEdition, Edition);
        _config.SaveToFile();
        Open();
        // Not the last page read: a new edition always opens at the front.
        _window?.Show(null);
    }

    public void OnStateExited(LobbyState state)
    {
        _window?.Close();
    }

    public void OnStateExited(GameplayState state)
    {
        _window?.Close();
    }

    public void LoadButton()
    {
        if (LoreBookButton is not { } button)
            return;

        button.Pressed = _window != null;
        button.OnPressed += ButtonPressed;
    }

    public void UnloadButton()
    {
        if (LoreBookButton is { } button)
            button.OnPressed -= ButtonPressed;
    }

    private void ButtonPressed(ButtonEventArgs args)
    {
        Toggle();
    }

    /// <summary>Opens the book at the last page read, or closes it.</summary>
    public void Toggle()
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        Open();
    }

    /// <summary>Opens the book, at the given page if it is one of the book's.</summary>
    public void Open(ProtoId<GuideEntryPrototype>? entry = null)
    {
        var lore = EntityManager.System<WFLoreBookSystem>();
        if (entry is { } wanted && !lore.IsLore(wanted))
            entry = null;

        if (_window == null)
        {
            var window = new WFLoreBookWindow();
            _window = window;
            window.OnClose += () =>
            {
                _lastEntry = window.Current;
                if (_window == window)
                    _window = null;

                SetButtonPressed(false);
            };
            window.OpenCentered();
            SetButtonPressed(true);
            // The book can open before the entity systems are up, as a new edition does on the way into the lobby.
            if (EntityManager.EntitySysManager.TryGetEntitySystem<SharedAudioSystem>(out var audio))
                audio.PlayGlobal(OpenSound, Filter.Local(), false, AudioParams.Default.WithVolume(-4f));
        }
        else
        {
            _window.MoveToFront();
        }

        _window.Show(entry ?? _lastEntry);
    }

    /// <summary>Opens the book at a link from another document if the link names a lore page.</summary>
    public bool TryOpenLink(string link)
    {
        if (!_proto.HasIndex<GuideEntryPrototype>(link) || !EntityManager.System<WFLoreBookSystem>().IsLore(link))
            return false;

        Open(link);
        return true;
    }

    private void SetButtonPressed(bool pressed)
    {
        if (LoreBookButton is { } button)
            button.Pressed = pressed;
    }
}
