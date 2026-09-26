using Content.Shared._Forge.Genetics;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Genetics.UI;

[UsedImplicitly]
public sealed class GeneticsConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private GeneticsConsoleWindow? _window;

    public GeneticsConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<GeneticsConsoleWindow>();
        _window.Title = Loc.GetString("genetics-console-title");

        _window.IrradiateButton.OnPressed += _ =>
            SendMessage(new GeneticsConsoleActionMessage(GeneticsConsoleAction.Irradiate));
        _window.ActivateButton.OnPressed += _ => SendSelected(GeneticsConsoleAction.Activate);
        _window.DeactivateButton.OnPressed += _ => SendSelected(GeneticsConsoleAction.Deactivate);
        _window.IsolateButton.OnPressed += _ => SendSelected(GeneticsConsoleAction.Isolate);
        _window.CleanButton.OnPressed += _ => SendSelected(GeneticsConsoleAction.Clean);
        _window.EjectButton.OnPressed += _ =>
            SendMessage(new GeneticsConsoleActionMessage(GeneticsConsoleAction.Eject));
        _window.OnPulseBlock += index =>
        {
            if (_window.SelectedGeneId is not { } geneId)
                return;

            SendMessage(new GeneticsConsoleActionMessage(GeneticsConsoleAction.PulseBlock, geneId, index));
        };
        _window.OnPrintJournal += (geneId, activate) =>
            SendMessage(new GeneticsConsoleActionMessage(
                activate ? GeneticsConsoleAction.Isolate : GeneticsConsoleAction.Clean,
                geneId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is GeneticsConsoleBoundUserInterfaceState cast)
            _window?.Populate(cast);
    }

    private void SendSelected(GeneticsConsoleAction action)
    {
        if (_window?.SelectedGeneId is not { } geneId)
            return;

        SendMessage(new GeneticsConsoleActionMessage(action, geneId));
    }
}
