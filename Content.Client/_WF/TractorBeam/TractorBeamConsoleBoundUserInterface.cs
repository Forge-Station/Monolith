using Content.Shared._WF.TractorBeam;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WF.TractorBeam;

[UsedImplicitly]
public sealed class TractorBeamConsoleBoundUserInterface : BoundUserInterface
{
    private TractorBeamConsoleWindow? _window;

    public TractorBeamConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<TractorBeamConsoleWindow>();
        _window.OnCommand += (emitter, target, pulling, lockInPlace, desiredRange) =>
            SendMessage(new TractorBeamConsoleMessage(emitter, target, pulling, lockInPlace, desiredRange));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is TractorBeamConsoleBoundUserInterfaceState tractorState)
            _window?.UpdateState(tractorState);
    }

}
