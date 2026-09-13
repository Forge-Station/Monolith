using Content.Shared.Humanoid.Markings;
using Robust.Shared.Serialization;

namespace Content.Shared.Humanoid
{
    [Serializable, NetSerializable]
    public enum HumanoidVisualLayers : byte
    {
        Special, // for the cat ears
        // WOLFGATE - ported from HardLight: underwear and genital layers.
        Genital,
        Penis,
        Breasts,
        UndergarmentTop,
        UndergarmentBottom,
        // End WOLFGATE
        TailExtras,
        Tail,
        Wings, // For IPC wings porting from SimpleStation
        Hair,
        FacialHair,
        Chest,
        Head,
        Snout,
        HeadSide, // side parts (i.e., frills)
        HeadTop,  // top parts (i.e., ears)
        // WOLFGATE - ported from HardLight/Floof: split tail layers so a tail can sit behind the
        // mob from most angles and over the suit when facing north, instead of being cookie-cut.
        TailBehind,
        TailOversuit,
        // End WOLFGATE
        Eyes,
        RArm,
        LArm,
        RHand,

        LHand,
        RLeg,
        LLeg,
        RFoot,
        LFoot,
        Handcuffs,
        StencilMask,
        Ensnare,
        Fire,
        LArmExtension, // Frontier: a species-specific extension layer, e.g. for harpy wings
        RArmExtension, // Frontier: a species-specific extension layer, e.g. for harpy wings

    }
}
