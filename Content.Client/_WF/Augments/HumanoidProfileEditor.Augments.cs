using System.Linq;
using Content.Shared._WF.Augments;

namespace Content.Client.Lobby.UI;

/// <summary>The Augments tab: cybernetic limbs per body slot.</summary>
public sealed partial class HumanoidProfileEditor
{
    private void InitializeAugments()
    {
        TabContainer.SetTabTitle(AugmentsTab.GetPositionInParent(), Loc.GetString("wf-augments-tab"));
        AugmentEditor.OnAugmentsChanged += augments =>
        {
            if (Profile == null)
                return;

            // A styled augment picked without a style gets the first that fits.
            Profile = Profile.WithAugments(WFAugmentRules.Validate(Profile.Species, augments, Profile.TraitPreferences,
                _prototypeManager, _entManager.ComponentFactory));
            SetDirty();
            AugmentEditor.SetProfile(Profile);
            ReloadProfilePreview();
        };
    }

    /// <summary>Drops augments the species or the traits now rule out, and refreshes the tab.</summary>
    private void UpdateAugments()
    {
        if (Profile == null)
            return;

        var valid = WFAugmentRules.Validate(Profile.Species, Profile.Augments, Profile.TraitPreferences,
            _prototypeManager, _entManager.ComponentFactory);
        if (valid.Count != Profile.Augments.Count || valid.Any(v => !Profile.Augments.TryGetValue(v.Key, out var c) || c != v.Value))
        {
            Profile = Profile.WithAugments(valid);
            ReloadProfilePreview();
        }

        AugmentEditor.SetProfile(Profile);
    }
}
