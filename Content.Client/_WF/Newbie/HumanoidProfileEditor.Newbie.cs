using Content.Client._WF.Newbie;
using Content.Shared.Traits;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    /// <summary>Whether this player has the newbie tag and the trait is closed to them, as the server checks at spawn.</summary>
    private bool WfNewbieTraitBlocked(TraitPrototype trait)
    {
        return WFNewbieTraitRules.TraitBlocked(trait, _cfgManager, _requirements);
    }
}
