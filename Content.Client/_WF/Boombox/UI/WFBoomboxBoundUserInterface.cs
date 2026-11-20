using Content.Shared._WF.Audio.InternetSound;
using Content.Shared._WF.Boombox;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Boombox.UI;

/// <summary>Opens the boombox window and turns its keys into messages.</summary>
public sealed class WFBoomboxBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WFBoomboxWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WFBoomboxWindow>();
        _window.SetOwner(Owner);
        _window.OnAdd += url => SendMessage(new WFBoomboxAddMessage(url));
        _window.OnSkip += () => SendMessage(new WFBoomboxSkipMessage());
        _window.OnStop += () => SendMessage(new WFBoomboxStopMessage());
        _window.OnShuffle += () => SendMessage(new WFBoomboxShuffleMessage());
        _window.OnRepeat += repeat => SendMessage(new WFBoomboxRepeatMessage(repeat));
        _window.OnClear += () => SendMessage(new WFBoomboxClearMessage());
        _window.OnRemove += id => SendMessage(new WFBoomboxRemoveMessage(id));
        _window.OnMoveUp += id => SendMessage(new WFBoomboxMoveUpMessage(id));
        _window.OnVolume += volume => SendMessage(new WFBoomboxVolumeMessage(volume));

        Update();
    }

    public override void Update()
    {
        base.Update();

        if (_window == null || !EntMan.TryGetComponent(Owner, out WFBoomboxComponent? box))
            return;

        EntMan.TryGetComponent(Owner, out InternetSoundSourceComponent? source);
        _window.UpdateState(box, source);
    }
}
