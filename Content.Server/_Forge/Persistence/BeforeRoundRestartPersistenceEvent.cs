namespace Content.Server._Forge.Persistence;

/// <summary>
/// Raised before normal round cleanup so persistent entities can be serialized
/// while the complete world still exists.
/// </summary>
public sealed class BeforeRoundRestartPersistenceEvent : EntityEventArgs;

/// <summary>
/// Raised while players are still attached, immediately before restart cleanup.
/// </summary>
public sealed class BeforeRoundRestartWarningEvent : EntityEventArgs;
