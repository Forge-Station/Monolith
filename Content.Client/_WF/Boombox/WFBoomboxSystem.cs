using Content.Client._WF.Boombox.UI;
using Content.Shared._WF.Audio.InternetSound;
using Content.Shared._WF.Boombox;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Boombox;

/// <summary>Refreshes an open boombox window when the box or its track changes.</summary>
public sealed partial class WFBoomboxSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFBoomboxComponent, AfterAutoHandleStateEvent>((uid, _, _) => Refresh(uid));
        SubscribeLocalEvent<InternetSoundSourceComponent, AfterAutoHandleStateEvent>((uid, _, _) => Refresh(uid));
    }

    private void Refresh(EntityUid uid)
    {
        if (_ui.TryGetOpenUi<WFBoomboxBoundUserInterface>(uid, WFBoomboxUiKey.Key, out var bui))
            bui.Update();
    }
}
