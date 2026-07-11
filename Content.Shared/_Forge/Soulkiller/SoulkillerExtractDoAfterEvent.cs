// Author: @lenta313. Все права не защищены / No rights reserved.
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Soulkiller;

/// <summary>
/// Do-after for forcibly cracking open a Soulkiller capsule that has an operator sealed inside.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SoulkillerExtractDoAfterEvent : SimpleDoAfterEvent
{
}
