using System.Linq;
using Content.Shared._Forge.OrePipe;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.OrePipe;

[UsedImplicitly]
public sealed class OreDisposalFilterBoundUserInterface : BoundUserInterface
{
    private OreDisposalFilterWindow? _window;
    private bool _populated;

    public OreDisposalFilterBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<OreDisposalFilterWindow>();
        _window.FilteredChanged += OnFilteredChanged;
    }

    private void OnFilteredChanged(HashSet<string> selected)
    {
        SendMessage(new OreDisposalFilterSetMessage(selected.ToList()));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (_window == null || state is not OreDisposalFilterBoundUserInterfaceState cast)
            return;

        if (!_populated)
        {
            _window.Populate(cast.Options);
            _populated = true;
            return;
        }

        var selected = new HashSet<string>();
        foreach (var option in cast.Options)
        {
            if (option.Filtered)
                selected.Add(option.StackId);
        }

        _window.SetSelected(selected);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
