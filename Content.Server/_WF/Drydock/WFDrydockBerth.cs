using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Server._WF.Drydock;

/// <summary>
/// A drydock work area derived from the cranes that serve it. <see cref="Area"/> is in the grid's local space.
/// </summary>
public readonly record struct WFDrydockBerth(EntityUid Grid, string Label, Box2 Area, Vector2 Approach, List<EntityUid> Cranes);
