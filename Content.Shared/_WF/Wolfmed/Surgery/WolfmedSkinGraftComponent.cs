using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Surgery;

/// <summary>
/// A sheet of cultured skin. The surgery tool for the graft step, which is the only thing that clears
/// charring: no topical reaches dead tissue, it has to be replaced.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSkinGraftComponent : Component, ISurgeryToolComponent
{
    public string ToolName => Loc.GetString("wolfmed-surgery-tool-skin-graft");

    public bool? Used { get; set; }

    [DataField]
    public float Speed { get; set; } = 1f;
}
