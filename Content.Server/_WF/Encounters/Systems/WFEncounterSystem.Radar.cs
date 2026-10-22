using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.Encounters;
using Content.Shared.Shuttles.Components;

namespace Content.Server._WF.Encounters.Systems;

public sealed partial class WFEncounterSystem
{
    /// <summary>Objective target for the nearest ship with a player aboard when the orders are given.</summary>
    public const string PlayerTarget = "@player";

    /// <summary>How far @player looks, or twice the start radius if that is further: no ship across the sector is ambushed.</summary>
    private const float PlayerTargetRange = 3000f;

    /// <summary>
    /// Radar colours for the sides of an encounter of several, in the order the sides appear: a blue, a red, an amber
    /// and a violet. None is the gold other ships show in or the green of the viewer's own.
    /// </summary>
    public static readonly IReadOnlyList<Color> SideColors = new[]
    {
        Color.FromHex("#6fb6ff"),
        Color.FromHex("#ff5c5c"),
        Color.FromHex("#ffae3d"),
        Color.FromHex("#c792ff"),
    };

    /// <summary>
    /// Gives every ship its side's radar colour: the first one set on any ship of the side, else, in an encounter of
    /// several sides, the side's own of <see cref="SideColors"/>. A lone side with none set keeps the ordinary colour.
    /// </summary>
    private void ApplySideColors(WFEncounterComponent comp, WFEncounterPrototype prototype)
    {
        var sides = new List<string>();
        var colors = new Dictionary<string, Color>();
        foreach (var ship in prototype.Ships)
        {
            if (!comp.Ships.TryGetValue(ship.Key, out var state))
                continue;

            if (!sides.Contains(state.Side))
                sides.Add(state.Side);
            if (ship.IffColor is { } set)
                colors.TryAdd(state.Side, set);
        }

        for (var i = 0; sides.Count > 1 && i < sides.Count; i++)
        {
            colors.TryAdd(sides[i], SideColors[i % SideColors.Count]);
        }

        foreach (var state in comp.Ships.Values)
        {
            if (!colors.TryGetValue(state.Side, out var color) || TerminatingOrDeleted(state.Grid))
                continue;

            state.Color = color;
            // A simulated ship takes its colour when its hull is spawned.
            if (!state.Virtual)
                _shuttles.SetIFFColor(state.Grid, color);
        }
    }

    /// <summary>Has a ship lie in wait, its IFF label hidden, until it shows itself.</summary>
    private void Mask(WFEncounterShipState ship)
    {
        ship.Lurking = true;
        if (!ship.Virtual)
            _shuttles.AddIFFFlag(ship.Grid, IFFFlags.HideLabel);
    }

    /// <summary>A ship lying in wait shows itself.</summary>
    private void Unmask(WFEncounterShipState ship)
    {
        if (!ship.Lurking)
            return;

        ship.Lurking = false;
        if (!TerminatingOrDeleted(ship.Grid) && !ship.Virtual)
            _shuttles.RemoveIFFFlag(ship.Grid, IFFFlags.HideLabel);
    }
}
