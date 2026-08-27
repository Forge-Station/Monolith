namespace Content.Shared._Forge.LivingNpc;

/// <summary>
/// High-level goal the living NPC brain is currently pursuing.
/// </summary>
public enum LivingNpcIntent : byte
{
    Idle,
    Fidget,
    Wander,
    Work,
    Rest,
    Socialize,
    Converse,
    Eat,
    Investigate,
    Help,
    Flee,
    Fight,
}
