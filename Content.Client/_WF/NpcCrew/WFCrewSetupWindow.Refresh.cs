using System.Linq;
using System.Numerics;
using Content.Shared._WF.NpcCrew;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.NpcCrew;

public sealed partial class WFCrewSetupWindow
{
    private readonly Dictionary<CrewKey, CrewCard> _crewCards = new();
    private readonly RichTextLabel _crewListEmpty = new();
    private QueueView? _queueView;

    /// <summary>Keeps popup buttons alive across polling so an in-progress click keeps its original target.</summary>
    private void RefreshGridChoices(OptionButton button, string placeholder, NetEntity? selected, NetEntity? exclude = null, bool skipLarge = false)
    {
        if (button.ItemCount == 0)
            button.AddItem(Text(placeholder), -1);

        // First index in _grids of every grid this button should offer.
        var wanted = new Dictionary<NetEntity, int>();
        for (var index = 0; index < _grids.Count; index++)
        {
            var grid = _grids[index];
            if (grid.Id != exclude && !(skipLarge && grid.Large))
                wanted.TryAdd(grid.Id, index);
        }
        var seen = new HashSet<NetEntity>();
        for (var index = button.ItemCount - 1; index > 0; index--)
        {
            if (button.GetItemMetadata(index) is WFCrewSetupGrid old && wanted.ContainsKey(old.Id) && seen.Add(old.Id))
                continue;
            if (button.SelectedId == button.GetItemId(index))
                button.SelectId(-1);
            button.RemoveItem(index);
        }
        var options = new Dictionary<NetEntity, int>();
        for (var option = 1; option < button.ItemCount; option++)
        {
            if (button.GetItemMetadata(option) is WFCrewSetupGrid kept)
                options[kept.Id] = option;
        }

        // Temporary IDs avoid collisions when the server changes its grid ordering.
        button.SelectId(-1);
        for (var index = 1; index < button.ItemCount; index++)
            button.SetItemId(index, -index - 1);
        var selection = -1;
        var changed = false;
        for (var index = 0; index < _grids.Count; index++)
        {
            var grid = _grids[index];
            if (!wanted.TryGetValue(grid.Id, out var first) || first != index)
                continue;
            if (!options.TryGetValue(grid.Id, out var existing))
            {
                button.AddItem($"{grid.Name} ({grid.Id})", index);
                existing = button.ItemCount - 1;
                changed = true;
            }
            else
            {
                button.SetItemId(existing, index);
                if (button.GetItemMetadata(existing) is not WFCrewSetupGrid current || current.Name != grid.Name)
                {
                    button.SetItemText(existing, $"{grid.Name} ({grid.Id})");
                    changed = true;
                }
            }
            button.SetItemMetadata(existing, grid);
            if (grid.Id == selected)
                selection = index;
        }
        button.SelectId(selection);
        if (changed)
            button.Filterable = true;
    }

    private sealed record CrewCard(ContainerButton Button, Label Name, RichTextLabel Detail);
    private sealed record QueueItemView(WFCrewObjectiveKind Kind, NetEntity? Target, Vector2 Position, float Range, float Duration, string Description);
    private sealed record QueueView(CrewKey? Crew, bool Draft, int? Editing, List<QueueItemView> Items);
}
