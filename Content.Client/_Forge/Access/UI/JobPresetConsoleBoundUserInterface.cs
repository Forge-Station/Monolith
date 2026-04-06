using Content.Shared.Roles;
using Content.Shared.Containers.ItemSlots;
using Content.Shared._Forge.Access;
using Content.Shared._Forge.Access.Components;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Client._Forge.Access.UI;

[UsedImplicitly]
public sealed class JobPresetConsoleBoundUserInterface : BoundUserInterface
{
    private JobPresetConsoleWindow? _window;

    public JobPresetConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        List<ProtoId<JobPrototype>> rankPresets;
        if (EntMan.TryGetComponent<JobPresetIdCardConsoleComponent>(Owner, out var console))
        {
            rankPresets = new List<ProtoId<JobPrototype>>(console.RankPresets);
        }
        else
        {
            rankPresets = new List<ProtoId<JobPrototype>>();
        }

        _window = new JobPresetConsoleWindow(this, rankPresets)
        {
            Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName,
        };

        _window.PrivilegedIdButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent(JobPresetIdCardConsoleComponent.PrivilegedIdCardSlotId));
        _window.TargetIdButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent(JobPresetIdCardConsoleComponent.TargetIdCardSlotId));

        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _window?.Dispose();
        _window = null;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not JobPresetIdCardConsoleBoundUserInterfaceState current)
            return;

        _window?.UpdateState(current);
    }

    public void ApplyPreset(ProtoId<JobPrototype> jobPrototype)
    {
        SendMessage(new JobPresetIdCardConsoleApplyMessage(jobPrototype));
    }

    public void CreateInjector(ProtoId<JobPrototype> jobPrototype)
    {
        SendMessage(new JobPresetIdCardConsoleCreateInjectorMessage(jobPrototype));
    }
}
