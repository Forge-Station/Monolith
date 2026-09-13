using Robust.Shared.Serialization;

namespace Content.Shared._WF.Genitals;

/// <summary>Anchor layers for anatomy sprites, referenced from species sprite lists as enum.GenitalVisualLayers.*.</summary>
[Serializable, NetSerializable]
public enum GenitalVisualLayers : byte
{
    Behind, // after TailBehind: north-facing BEHIND art, drawn below the body
    Under,  // directly after gloves: exposed anatomy, above the arms, hands, gloves, underwear and jumpsuit, below
            // shoes, belts, bags, hair, neckwear, outer clothing and the tail layers
    Over,   // directly after outerClothing: the show-through-clothing set, still below the neck slots, hair and tails
}
