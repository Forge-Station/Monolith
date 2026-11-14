# BloodTrail

Blood footprints and drag trails ported from Wolfgate (originally Colonial Marines Universe).
Touching a puddle stains a mob; walking leaves bare-foot, shoe or pressure-suit prints,
while crawling or dragging an incapacitated body leaves streaks. Stains fade with each
print, consume some of the puddle, and do not spread while floating. Nearby prints are capped.

`PuddleFootPrintsSystem` stains mobs on puddle contact. `FootPrintsSystem` adds
`FootPrintsComponent` at runtime and places decals as the mob moves. The nine decal
prototypes are in `Resources/Prototypes/_WF/BloodTrail/decals.yml`; their textures and
attribution are in `Resources/Textures/_WF/BloodTrail/footprints.rsi`.

Space cleaner, cleanbots and wet mops remove the decals through the existing cleaning
systems. `AbsorbentSystem.BloodTrail.cs` adds mopping of bare floor, and
`WolfmedGoreSystem.CleanTile` includes footprints in its blood wash so water removes
them without removing decorative paint. The existing shower integration clears a
mob's stain before it can leave more prints.
