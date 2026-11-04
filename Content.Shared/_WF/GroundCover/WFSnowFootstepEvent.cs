using Robust.Shared.Audio;

namespace Content.Shared._WF.GroundCover;

/// <summary>Raised broadcast before a mob's footstep falls back to its tile's sound; set the sound to replace it.</summary>
[ByRefEvent]
public record struct WFSnowFootstepEvent(EntityUid User, EntityUid Grid, Vector2i Tile)
{
    public SoundSpecifier? Sound;
}
