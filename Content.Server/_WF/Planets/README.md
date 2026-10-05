# Planets

Planets you can fly to, land on and crash into. Each world of the sector's star system that has a Wolfgate surface is
a stack of CE z-levels: an orbit layer, air layers and the ground. Ships hop into orbit from the sector with the
shuttle console's orbit button (an unsanctioned world asks first), descend through the atmosphere on landing
thrusters, lift off again, and land hard, skid, break up or crash when lift is lost. Hulls that stop keeping station
fall out of orbit, and a world's gravity well pulls adrift hulls in from the sector.

Worlds have day and night, phased weather with thunder, fauna, ambience, terrain on the orbit radar and an approach
cinematic on the hop in; Carcinoma is a quarantined flesh world whose tendrils hold landed hulls down. Lower layers of a planet are drawn smaller
(`WFPlanetView`, the perspective Monolith turned off for other z-levels). Jetpacks only
work in orbit and lattice goes straight onto natural ground. A room built on a planet holds its own air once it has a
floor: bare ground counts as outdoors and keeps the planet's air. Players use landing thruster kits, parachutes and the
planet timepiece. Admins get the Planet Control panel in the Wolfgate admin tab (`planetcontrol`) for sanction, time
of day, weather and gravity, and `wfplanet` to build a star system's worlds.

Entry points: `WFPlanetNetworkSystem` and `WFPlanetRegistrySystem` (the z-level stack per world), `WFOrbitEntrySystem`
(hops in and out of orbit), `WFFlightSystem` with the `CEZLevelsSystem.WF*` partials (flight, liftoff, landing and
crashes), `WFOrbitDecaySystem`, `WFGravityWellSystem`, `WFPlanetWeatherSystem`, `WFParachuteSystem` and
`PlanetControlSystem`. Upstream systems are extended through partials in this module (`CEZLevelsSystem.WF*`,
`ThrusterSystem.WF*`, `ShuttleSystem.WFOrbit`). `WFTerrainAtmosphereSystem` gives every layer with ground a grid
atmosphere that `AtmosphereSystem.WFTerrain` keeps sparse: tiles the layer's biome lays (and what digging turns them
into) are bare ground, treated like tiles off the grid and never tracked until something is built beside them, so
loading terrain costs atmos nothing; `WFTerrainOpenTilesEvent` lets a module name more ground.
`BiomeSystem.WFUnload` unloads planet terrain nobody is near, in place of upstream's unloader, which let go of
almost none of it: untouched biome entities go with their chunk, and the ground under a hull, beside a build or
loaded by hand stays. Ground that is only unloaded still holds up whatever was left on it. `BiomeSystem.WFRoll`
makes a planet's rock spawner rolls in the loader, seeded from the tile, so surface rock unloads too and the same
rock comes back. `BiomeSystem.WFLoad` puts off the far part of a planet load area and loads it nearest first over
the next passes, so an arrival or a flight does not load it all in one tick. A hull that sets down flattens what is
under it as an FTL arrival does, breaks the trees and rock it would rest against (`WfClearLandingObstacles`), and
hurts a mob under it and shoves it clear instead of gibbing it (`ShuttleSystem.WFSetDown`).
Settings are in `PlanetCVars` (`wf.planet_networks`, `wf.planet_terrain_atmos`, `wf.planet_terrain_unload`,
`wf.planet_terrain_load_budget`); ecology and
landing notes and the playtest checklist are in `Docs/_WF/Planets`. `WFBiomeNoiseCacheSystem` keeps one seeded copy
of each biome layer's noise for `SharedBiomeSystem.GetNoise`, which copied it for every tile planets and caverns
generate or sample.

Other modules build on it through `WFDetachedTerrainComponent` (a grid that is ground, not a hull),
`WFLiftoffAttemptEvent`, `WFGridLiftLoadEvent`, `WFPlanetGroundSpawnedEvent`, `WFPlanetLowerLayersEvent` and
`WFPlanetNetworkBuiltEvent` (maps below the ground, kept in `WFPlanetNetworkComponent.LowerLayers`, which
`CEZLevelsSystem.WfClosedToHulls` keeps every hull out of and where an orbital faller is maimed as on the ground) and
`WFOrbitLayerComponent.RadarScars`.

Jetpacks in the atmosphere: a gas jetpack refuses below a world's orbit layer and cuts out if carried down. The
atmospheric jetpack (`WFJetpackAtmospheric`, `WFAtmosphericJetpackComponent`) is the reverse: it burns welding fuel,
flies on the ground and air layers, climbs and descends half a layer a second on the jetpack's ascend and descend
keys, and refuses in orbit, where there is no air to burn. It holds 100 units, burns one a second hovering and two
moving, a quarter more above 1 g, and refuels from any welding fuel tank the way a welder does. Above 1.5 g it will
not lift anyone, so a 3 g world grounds it. An empty tank switches it off mid-air and the wearer falls; a parachute
takes it from there. It flies in a cavern too, and up or down through a hole in the ground, but nowhere else: the
ground overhead is a ceiling (the Caverns `WfSealedAbove`). `WFAtmosphericJetpackSystem` (shared) decides where it lights and refuels it,
`WFAtmosphericJetpackFuelSystem` burns the fuel. John Wolfgate stocks it.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Planets/Administration/PlanetControlCommand.cs`](Administration/PlanetControlCommand.cs)
- [`Content.Server/_WF/Planets/Administration/PlanetControlSystem.cs`](Administration/PlanetControlSystem.cs)
- [`Content.Server/_WF/Planets/Atmosphere/AtmosphereSystem.WFTerrain.cs`](Atmosphere/AtmosphereSystem.WFTerrain.cs)
- [`Content.Server/_WF/Planets/Atmosphere/WFTerrainAtmosphereComponent.cs`](Atmosphere/WFTerrainAtmosphereComponent.cs)
- [`Content.Server/_WF/Planets/Atmosphere/WFTerrainAtmosphereSystem.cs`](Atmosphere/WFTerrainAtmosphereSystem.cs)
- [`Content.Server/_WF/Planets/Atmosphere/WFTerrainOpenTilesEvent.cs`](Atmosphere/WFTerrainOpenTilesEvent.cs)
- [`Content.Server/_WF/Planets/BiomeSystem.WFLoad.cs`](BiomeSystem.WFLoad.cs)
- [`Content.Server/_WF/Planets/BiomeSystem.WFRoll.cs`](BiomeSystem.WFRoll.cs)
- [`Content.Server/_WF/Planets/BiomeSystem.WFUnload.cs`](BiomeSystem.WFUnload.cs)
- [`Content.Server/_WF/Planets/BiomeSystem.Wolfgate.cs`](BiomeSystem.Wolfgate.cs)
- [`Content.Server/_WF/Planets/CEZLevelsSystem.WFTerrain.cs`](CEZLevelsSystem.WFTerrain.cs)
- [`Content.Server/_WF/Planets/CEZLevelsSystem.Wolfgate.cs`](CEZLevelsSystem.Wolfgate.cs)
- [`Content.Server/_WF/Planets/Commands/WFPlanetCommand.cs`](Commands/WFPlanetCommand.cs)
- [`Content.Server/_WF/Planets/Flight/CEZLevelsSystem.WFFlight.cs`](Flight/CEZLevelsSystem.WFFlight.cs)
- [`Content.Server/_WF/Planets/Flight/CEZLevelsSystem.WFGravityCache.cs`](Flight/CEZLevelsSystem.WFGravityCache.cs)
- [`Content.Server/_WF/Planets/Flight/CEZLevelsSystem.WFLandingClearance.cs`](Flight/CEZLevelsSystem.WFLandingClearance.cs)
- [`Content.Server/_WF/Planets/Flight/CEZLevelsSystem.WFLiftoff.cs`](Flight/CEZLevelsSystem.WFLiftoff.cs)
- [`Content.Server/_WF/Planets/Flight/CEZLevelsSystem.WFVirtualMass.cs`](Flight/CEZLevelsSystem.WFVirtualMass.cs)
- [`Content.Server/_WF/Planets/Flight/MoverController.WFAtmosphere.cs`](Flight/MoverController.WFAtmosphere.cs)
- [`Content.Server/_WF/Planets/Flight/ShuttleSystem.WFSetDown.cs`](Flight/ShuttleSystem.WFSetDown.cs)
- [`Content.Server/_WF/Planets/Flight/ThrusterSystem.WFAtmosphere.cs`](Flight/ThrusterSystem.WFAtmosphere.cs)
- [`Content.Server/_WF/Planets/Flight/ThrusterSystem.WFCrashThrust.cs`](Flight/ThrusterSystem.WFCrashThrust.cs)
- [`Content.Server/_WF/Planets/Flight/ThrusterSystem.WFPowerPulses.cs`](Flight/ThrusterSystem.WFPowerPulses.cs)
- [`Content.Server/_WF/Planets/Flight/WFAtmosphereThrusterComponent.cs`](Flight/WFAtmosphereThrusterComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFCrashApcFaultComponent.cs`](Flight/WFCrashApcFaultComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFCrashApcFaultSystem.cs`](Flight/WFCrashApcFaultSystem.cs)
- [`Content.Server/_WF/Planets/Flight/WFCrashFractures.cs`](Flight/WFCrashFractures.cs)
- [`Content.Server/_WF/Planets/Flight/WFCrashImpactComponent.cs`](Flight/WFCrashImpactComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFCrashThrustComponent.cs`](Flight/WFCrashThrustComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightAmbienceComponent.cs`](Flight/WFFlightAmbienceComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightAmbienceSystem.cs`](Flight/WFFlightAmbienceSystem.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightSystem.Breakup.cs`](Flight/WFFlightSystem.Breakup.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightSystem.cs`](Flight/WFFlightSystem.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightSystem.GroundContact.cs`](Flight/WFFlightSystem.GroundContact.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightSystem.Lattice.cs`](Flight/WFFlightSystem.Lattice.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightSystem.Scar.cs`](Flight/WFFlightSystem.Scar.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightSystem.Skid.cs`](Flight/WFFlightSystem.Skid.cs)
- [`Content.Server/_WF/Planets/Flight/WFGridLiftLoadEvent.cs`](Flight/WFGridLiftLoadEvent.cs)
- [`Content.Server/_WF/Planets/Flight/WFLandingThrusterKitComponent.cs`](Flight/WFLandingThrusterKitComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFLandingThrusterKitSystem.cs`](Flight/WFLandingThrusterKitSystem.cs)
- [`Content.Server/_WF/Planets/Flight/WFLiftoffComponent.cs`](Flight/WFLiftoffComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFOrbitalMobFallSystem.cs`](Flight/WFOrbitalMobFallSystem.cs)
- [`Content.Server/_WF/Planets/Flight/WFPlanetDragComponent.cs`](Flight/WFPlanetDragComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFPlanetDragSystem.cs`](Flight/WFPlanetDragSystem.cs)
- [`Content.Server/_WF/Planets/Flight/WFSkidComponent.cs`](Flight/WFSkidComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFThrustAmbienceSystem.cs`](Flight/WFThrustAmbienceSystem.cs)
- [`Content.Server/_WF/Planets/Infestation/WFCarcinomaInfestationComponent.cs`](Infestation/WFCarcinomaInfestationComponent.cs)
- [`Content.Server/_WF/Planets/Infestation/WFCarcinomaInfestationSystem.cs`](Infestation/WFCarcinomaInfestationSystem.cs)
- [`Content.Server/_WF/Planets/Jetpack/WFAtmosphericJetpackFuelSystem.cs`](Jetpack/WFAtmosphericJetpackFuelSystem.cs)
- [`Content.Server/_WF/Planets/ShuttleSystem.WFOrbit.cs`](ShuttleSystem.WFOrbit.cs)
- [`Content.Server/_WF/Planets/StarSystemMapSystem.Wolfgate.cs`](StarSystemMapSystem.Wolfgate.cs)
- [`Content.Server/_WF/Planets/WFBiomeGrownComponent.cs`](WFBiomeGrownComponent.cs)
- [`Content.Server/_WF/Planets/WFGravityWellSystem.cs`](WFGravityWellSystem.cs)
- [`Content.Server/_WF/Planets/WFGridAudienceSystem.cs`](WFGridAudienceSystem.cs)
- [`Content.Server/_WF/Planets/WFOrbitDecayComponent.cs`](WFOrbitDecayComponent.cs)
- [`Content.Server/_WF/Planets/WFOrbitDecaySystem.cs`](WFOrbitDecaySystem.cs)
- [`Content.Server/_WF/Planets/WFOrbitEntrySystem.Approach.cs`](WFOrbitEntrySystem.Approach.cs)
- [`Content.Server/_WF/Planets/WFOrbitEntrySystem.Atmosphere.cs`](WFOrbitEntrySystem.Atmosphere.cs)
- [`Content.Server/_WF/Planets/WFOrbitEntrySystem.cs`](WFOrbitEntrySystem.cs)
- [`Content.Server/_WF/Planets/WFOrbitEntrySystem.Liftoff.cs`](WFOrbitEntrySystem.Liftoff.cs)
- [`Content.Server/_WF/Planets/WFPlanetBiomassSystem.cs`](WFPlanetBiomassSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetFaunaSpawnerComponent.cs`](WFPlanetFaunaSpawnerComponent.cs)
- [`Content.Server/_WF/Planets/WFPlanetFaunaSystem.cs`](WFPlanetFaunaSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetGroundSpawnedEvent.cs`](WFPlanetGroundSpawnedEvent.cs)
- [`Content.Server/_WF/Planets/WFPlanetLowerLayersEvent.cs`](WFPlanetLowerLayersEvent.cs)
- [`Content.Server/_WF/Planets/WFPlanetNetworkBuiltEvent.cs`](WFPlanetNetworkBuiltEvent.cs)
- [`Content.Server/_WF/Planets/WFPlanetNetworkComponent.cs`](WFPlanetNetworkComponent.cs)
- [`Content.Server/_WF/Planets/WFPlanetNetworkSystem.cs`](WFPlanetNetworkSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetRegistrySystem.cs`](WFPlanetRegistrySystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetTimepieceSystem.cs`](WFPlanetTimepieceSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetWeatherComponent.cs`](WFPlanetWeatherComponent.cs)
- [`Content.Server/_WF/Planets/WFPlanetWeatherSystem.Admin.cs`](WFPlanetWeatherSystem.Admin.cs)
- [`Content.Server/_WF/Planets/WFPlanetWeatherSystem.cs`](WFPlanetWeatherSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetWeatherSystem.Daylight.cs`](WFPlanetWeatherSystem.Daylight.cs)
- [`Content.Server/_WF/Planets/WFPlanetWeatherSystem.Storms.cs`](WFPlanetWeatherSystem.Storms.cs)
- [`Content.Server/_WF/Planets/WFPlanetWildlifeComponent.cs`](WFPlanetWildlifeComponent.cs)

### Shared

- [`Content.Shared/_WF/Planets/Administration/PlanetControlEvents.cs`](../../../Content.Shared/_WF/Planets/Administration/PlanetControlEvents.cs)
- [`Content.Shared/_WF/Planets/CESharedZLevelsSystem.Terrain.cs`](../../../Content.Shared/_WF/Planets/CESharedZLevelsSystem.Terrain.cs)
- [`Content.Shared/_WF/Planets/Flight/WFApcRepairDoAfterEvent.cs`](../../../Content.Shared/_WF/Planets/Flight/WFApcRepairDoAfterEvent.cs)
- [`Content.Shared/_WF/Planets/Flight/WFEnterAtmosphereMessage.cs`](../../../Content.Shared/_WF/Planets/Flight/WFEnterAtmosphereMessage.cs)
- [`Content.Shared/_WF/Planets/Flight/WFLandingThrusterComponent.cs`](../../../Content.Shared/_WF/Planets/Flight/WFLandingThrusterComponent.cs)
- [`Content.Shared/_WF/Planets/Flight/WFLiftLostComponent.cs`](../../../Content.Shared/_WF/Planets/Flight/WFLiftLostComponent.cs)
- [`Content.Shared/_WF/Planets/Flight/WFLiftoffAttemptEvent.cs`](../../../Content.Shared/_WF/Planets/Flight/WFLiftoffAttemptEvent.cs)
- [`Content.Shared/_WF/Planets/Jetpack/WFAtmosphericJetpackComponent.cs`](../../../Content.Shared/_WF/Planets/Jetpack/WFAtmosphericJetpackComponent.cs)
- [`Content.Shared/_WF/Planets/Jetpack/WFAtmosphericJetpackSystem.cs`](../../../Content.Shared/_WF/Planets/Jetpack/WFAtmosphericJetpackSystem.cs)
- [`Content.Shared/_WF/Planets/Parachute/WFParachuteComponents.cs`](../../../Content.Shared/_WF/Planets/Parachute/WFParachuteComponents.cs)
- [`Content.Shared/_WF/Planets/Parachute/WFParachuteSystem.cs`](../../../Content.Shared/_WF/Planets/Parachute/WFParachuteSystem.cs)
- [`Content.Shared/_WF/Planets/SharedBiomeSystem.Wolfgate.cs`](../../../Content.Shared/_WF/Planets/SharedBiomeSystem.Wolfgate.cs)
- [`Content.Shared/_WF/Planets/SharedShuttleSystem.Wolfgate.cs`](../../../Content.Shared/_WF/Planets/SharedShuttleSystem.Wolfgate.cs)
- [`Content.Shared/_WF/Planets/WFBiomeKeepComponent.cs`](../../../Content.Shared/_WF/Planets/WFBiomeKeepComponent.cs)
- [`Content.Shared/_WF/Planets/WFBiomeNoiseCacheSystem.cs`](../../../Content.Shared/_WF/Planets/WFBiomeNoiseCacheSystem.cs)
- [`Content.Shared/_WF/Planets/WFConsoleOrbitTargetComponent.cs`](../../../Content.Shared/_WF/Planets/WFConsoleOrbitTargetComponent.cs)
- [`Content.Shared/_WF/Planets/WFDetachedTerrainComponent.cs`](../../../Content.Shared/_WF/Planets/WFDetachedTerrainComponent.cs)
- [`Content.Shared/_WF/Planets/WFOrbitLayerComponent.cs`](../../../Content.Shared/_WF/Planets/WFOrbitLayerComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetAmbiencePrototype.cs`](../../../Content.Shared/_WF/Planets/WFPlanetAmbiencePrototype.cs)
- [`Content.Shared/_WF/Planets/WFPlanetApproachComponent.cs`](../../../Content.Shared/_WF/Planets/WFPlanetApproachComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetBuiltTilesComponent.cs`](../../../Content.Shared/_WF/Planets/WFPlanetBuiltTilesComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetEnvironmentComponent.cs`](../../../Content.Shared/_WF/Planets/WFPlanetEnvironmentComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetLayerComponent.cs`](../../../Content.Shared/_WF/Planets/WFPlanetLayerComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetOrbitMessages.cs`](../../../Content.Shared/_WF/Planets/WFPlanetOrbitMessages.cs)
- [`Content.Shared/_WF/Planets/WFPlanetRadarSystem.cs`](../../../Content.Shared/_WF/Planets/WFPlanetRadarSystem.cs)
- [`Content.Shared/_WF/Planets/WFPlanetSurfacePrototype.cs`](../../../Content.Shared/_WF/Planets/WFPlanetSurfacePrototype.cs)
- [`Content.Shared/_WF/Planets/WFPlanetTimepieceComponent.cs`](../../../Content.Shared/_WF/Planets/WFPlanetTimepieceComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetView.cs`](../../../Content.Shared/_WF/Planets/WFPlanetView.cs)
- [`Content.Shared/_WF/Planets/WFPlanetWeatherPrototype.cs`](../../../Content.Shared/_WF/Planets/WFPlanetWeatherPrototype.cs)
- [`Content.Shared/_WF/Planets/WFSectorPlanetComponent.cs`](../../../Content.Shared/_WF/Planets/WFSectorPlanetComponent.cs)

### Client

- [`Content.Client/_WF/Planets/Administration/PlanetControlSystem.cs`](../../../Content.Client/_WF/Planets/Administration/PlanetControlSystem.cs)
- [`Content.Client/_WF/Planets/Administration/PlanetControlWindow.xaml`](../../../Content.Client/_WF/Planets/Administration/PlanetControlWindow.xaml)
- [`Content.Client/_WF/Planets/Administration/PlanetControlWindow.xaml.cs`](../../../Content.Client/_WF/Planets/Administration/PlanetControlWindow.xaml.cs)
- [`Content.Client/_WF/Planets/ContentAudioSystem.Planets.cs`](../../../Content.Client/_WF/Planets/ContentAudioSystem.Planets.cs)
- [`Content.Client/_WF/Planets/Flight/WFEnterAtmosphereConfirmWindow.xaml`](../../../Content.Client/_WF/Planets/Flight/WFEnterAtmosphereConfirmWindow.xaml)
- [`Content.Client/_WF/Planets/Flight/WFEnterAtmosphereConfirmWindow.xaml.cs`](../../../Content.Client/_WF/Planets/Flight/WFEnterAtmosphereConfirmWindow.xaml.cs)
- [`Content.Client/_WF/Planets/Parachute/WFParachuteVisualsSystem.cs`](../../../Content.Client/_WF/Planets/Parachute/WFParachuteVisualsSystem.cs)
- [`Content.Client/_WF/Planets/ShuttleNavControl.Terrain.cs`](../../../Content.Client/_WF/Planets/ShuttleNavControl.Terrain.cs)
- [`Content.Client/_WF/Planets/WFOrbitButton.cs`](../../../Content.Client/_WF/Planets/WFOrbitButton.cs)
- [`Content.Client/_WF/Planets/WFPlanetAmbienceSystem.cs`](../../../Content.Client/_WF/Planets/WFPlanetAmbienceSystem.cs)
- [`Content.Client/_WF/Planets/WFPlanetApproachOverlay.cs`](../../../Content.Client/_WF/Planets/WFPlanetApproachOverlay.cs)
- [`Content.Client/_WF/Planets/WFPlanetApproachSystem.cs`](../../../Content.Client/_WF/Planets/WFPlanetApproachSystem.cs)
- [`Content.Client/_WF/Planets/WFPlanetTimepieceHud.cs`](../../../Content.Client/_WF/Planets/WFPlanetTimepieceHud.cs)
- [`Content.Client/_WF/Planets/WFPlanetTimepieceHudSystem.cs`](../../../Content.Client/_WF/Planets/WFPlanetTimepieceHudSystem.cs)
- [`Content.Client/_WF/Planets/WFUnsanctionedOrbitConfirmWindow.xaml`](../../../Content.Client/_WF/Planets/WFUnsanctionedOrbitConfirmWindow.xaml)
- [`Content.Client/_WF/Planets/WFUnsanctionedOrbitConfirmWindow.xaml.cs`](../../../Content.Client/_WF/Planets/WFUnsanctionedOrbitConfirmWindow.xaml.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Planets/AtmosphereThrusterTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/AtmosphereThrusterTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/AtmosphericJetpackTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/AtmosphericJetpackTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/AudioExhaustionTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/AudioExhaustionTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/BiomeNoiseCacheTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/BiomeNoiseCacheTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/CarcinomaInfestationTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/CarcinomaInfestationTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/CarcinomaTendrilAttackTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/CarcinomaTendrilAttackTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/CrashApcFaultTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/CrashApcFaultTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/CrashAudioTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/CrashAudioTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/CrashBreakupFallTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/CrashBreakupFallTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/CrashThrustTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/CrashThrustTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/FlightAudioLoadTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/FlightAudioLoadTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/FlightSafetyTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/FlightSafetyTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/FlightTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/FlightTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/GroundContactTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/GroundContactTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/KyphrusPlanetsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/KyphrusPlanetsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/LiftoffTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/LiftoffTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/OrbitalMobFallTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/OrbitalMobFallTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/OrbitArrivalTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/OrbitArrivalTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/OrbitDecayTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/OrbitDecayTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/OrbitEntryTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/OrbitEntryTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/ParachuteTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/ParachuteTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetAmbiencePlaybackTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetAmbiencePlaybackTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetAmbiencePrototypeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetAmbiencePrototypeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetDragTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetDragTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetDroneBeltTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetDroneBeltTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetEcologyLifecycleTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetEcologyLifecycleTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetEcologyTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetEcologyTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetFixture.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetFixture.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetNetworkTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetNetworkTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetOreMarkerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetOreMarkerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetPopulationTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetPopulationTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetPrototypeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetPrototypeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetRadarDrawingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetRadarDrawingTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetRadarTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetRadarTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetTimepieceTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetTimepieceTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetWeatherTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetWeatherTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/SetDownTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/SetDownTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/StructuralCrashTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/StructuralCrashTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/TerrainAtmosphereTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/TerrainAtmosphereTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/TerrainLoadTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/TerrainLoadTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/TerrainUnloadTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/TerrainUnloadTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/ThrustAmbienceTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/ThrustAmbienceTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Planets/biomes.yml`](../../../Resources/Prototypes/_WF/Planets/biomes.yml)
- [`Resources/Prototypes/_WF/Planets/carcinoma.yml`](../../../Resources/Prototypes/_WF/Planets/carcinoma.yml)
- [`Resources/Prototypes/_WF/Planets/carcinoma_regions.yml`](../../../Resources/Prototypes/_WF/Planets/carcinoma_regions.yml)
- [`Resources/Prototypes/_WF/Planets/crash_effects.yml`](../../../Resources/Prototypes/_WF/Planets/crash_effects.yml)
- [`Resources/Prototypes/_WF/Planets/entities.yml`](../../../Resources/Prototypes/_WF/Planets/entities.yml)
- [`Resources/Prototypes/_WF/Planets/fauna.yml`](../../../Resources/Prototypes/_WF/Planets/fauna.yml)
- [`Resources/Prototypes/_WF/Planets/flight.yml`](../../../Resources/Prototypes/_WF/Planets/flight.yml)
- [`Resources/Prototypes/_WF/Planets/flight_alerts.yml`](../../../Resources/Prototypes/_WF/Planets/flight_alerts.yml)
- [`Resources/Prototypes/_WF/Planets/infestation.yml`](../../../Resources/Prototypes/_WF/Planets/infestation.yml)
- [`Resources/Prototypes/_WF/Planets/jetpack.yml`](../../../Resources/Prototypes/_WF/Planets/jetpack.yml)
- [`Resources/Prototypes/_WF/Planets/kyphrus_planets.yml`](../../../Resources/Prototypes/_WF/Planets/kyphrus_planets.yml)
- [`Resources/Prototypes/_WF/Planets/lathe_recipes.yml`](../../../Resources/Prototypes/_WF/Planets/lathe_recipes.yml)
- [`Resources/Prototypes/_WF/Planets/open_biomes.yml`](../../../Resources/Prototypes/_WF/Planets/open_biomes.yml)
- [`Resources/Prototypes/_WF/Planets/parachute.yml`](../../../Resources/Prototypes/_WF/Planets/parachute.yml)
- [`Resources/Prototypes/_WF/Planets/planet_ambience.yml`](../../../Resources/Prototypes/_WF/Planets/planet_ambience.yml)
- [`Resources/Prototypes/_WF/Planets/planet_surfaces.yml`](../../../Resources/Prototypes/_WF/Planets/planet_surfaces.yml)
- [`Resources/Prototypes/_WF/Planets/planet_weather.yml`](../../../Resources/Prototypes/_WF/Planets/planet_weather.yml)
- [`Resources/Prototypes/_WF/Planets/planets.yml`](../../../Resources/Prototypes/_WF/Planets/planets.yml)
- [`Resources/Prototypes/_WF/Planets/timepiece.yml`](../../../Resources/Prototypes/_WF/Planets/timepiece.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Planets/carcinoma.ftl`](../../../Resources/Locale/en-US/_WF/Planets/carcinoma.ftl)
- [`Resources/Locale/en-US/_WF/Planets/flight.ftl`](../../../Resources/Locale/en-US/_WF/Planets/flight.ftl)
- [`Resources/Locale/en-US/_WF/Planets/infestation.ftl`](../../../Resources/Locale/en-US/_WF/Planets/infestation.ftl)
- [`Resources/Locale/en-US/_WF/Planets/jetpack.ftl`](../../../Resources/Locale/en-US/_WF/Planets/jetpack.ftl)
- [`Resources/Locale/en-US/_WF/Planets/kyphrus.ftl`](../../../Resources/Locale/en-US/_WF/Planets/kyphrus.ftl)
- [`Resources/Locale/en-US/_WF/Planets/liftoff.ftl`](../../../Resources/Locale/en-US/_WF/Planets/liftoff.ftl)
- [`Resources/Locale/en-US/_WF/Planets/parachute.ftl`](../../../Resources/Locale/en-US/_WF/Planets/parachute.ftl)
- [`Resources/Locale/en-US/_WF/Planets/planets.ftl`](../../../Resources/Locale/en-US/_WF/Planets/planets.ftl)
- [`Resources/Locale/en-US/_WF/Planets/radar.ftl`](../../../Resources/Locale/en-US/_WF/Planets/radar.ftl)
- [`Resources/Locale/en-US/_WF/Planets/timepiece.ftl`](../../../Resources/Locale/en-US/_WF/Planets/timepiece.ftl)
- [`Resources/Locale/en-US/_WF/Planets/weather.ftl`](../../../Resources/Locale/en-US/_WF/Planets/weather.ftl)

### Textures

- [`Resources/Textures/_WF/Planets/Effects/parachute_canopy.rsi/`](../../../Resources/Textures/_WF/Planets/Effects/parachute_canopy.rsi/)
- [`Resources/Textures/_WF/Planets/Effects/thunderbolt.rsi/`](../../../Resources/Textures/_WF/Planets/Effects/thunderbolt.rsi/)
- [`Resources/Textures/_WF/Planets/Effects/weather_tg.rsi/`](../../../Resources/Textures/_WF/Planets/Effects/weather_tg.rsi/)
- [`Resources/Textures/_WF/Planets/Objects/jetpack_atmospheric.rsi/`](../../../Resources/Textures/_WF/Planets/Objects/jetpack_atmospheric.rsi/)
- [`Resources/Textures/_WF/Planets/Objects/parachute.rsi/`](../../../Resources/Textures/_WF/Planets/Objects/parachute.rsi/)
- [`Resources/Textures/_WF/Planets/Structures/flesh_trees.rsi/`](../../../Resources/Textures/_WF/Planets/Structures/flesh_trees.rsi/)

### Audio

- [`Resources/Audio/_WF/Planets/Flight/atmo_wind.ogg`](../../../Resources/Audio/_WF/Planets/Flight/atmo_wind.ogg)
- [`Resources/Audio/_WF/Planets/Flight/attributions.yml`](../../../Resources/Audio/_WF/Planets/Flight/attributions.yml)
- [`Resources/Audio/_WF/Planets/Flight/crash10.ogg`](../../../Resources/Audio/_WF/Planets/Flight/crash10.ogg)
- [`Resources/Audio/_WF/Planets/Flight/crash11.ogg`](../../../Resources/Audio/_WF/Planets/Flight/crash11.ogg)
- [`Resources/Audio/_WF/Planets/Flight/crash12.ogg`](../../../Resources/Audio/_WF/Planets/Flight/crash12.ogg)
- [`Resources/Audio/_WF/Planets/Flight/crash3.ogg`](../../../Resources/Audio/_WF/Planets/Flight/crash3.ogg)
- [`Resources/Audio/_WF/Planets/Flight/crash4.ogg`](../../../Resources/Audio/_WF/Planets/Flight/crash4.ogg)
- [`Resources/Audio/_WF/Planets/Flight/dont_sink.ogg`](../../../Resources/Audio/_WF/Planets/Flight/dont_sink.ogg)
- [`Resources/Audio/_WF/Planets/Flight/fall_rumble.ogg`](../../../Resources/Audio/_WF/Planets/Flight/fall_rumble.ogg)
- [`Resources/Audio/_WF/Planets/Flight/ground_grind_loop.ogg`](../../../Resources/Audio/_WF/Planets/Flight/ground_grind_loop.ogg)
- [`Resources/Audio/_WF/Planets/Flight/hard_landing.ogg`](../../../Resources/Audio/_WF/Planets/Flight/hard_landing.ogg)
- [`Resources/Audio/_WF/Planets/Flight/lift_lost.ogg`](../../../Resources/Audio/_WF/Planets/Flight/lift_lost.ogg)
- [`Resources/Audio/_WF/Planets/Flight/pull_up.ogg`](../../../Resources/Audio/_WF/Planets/Flight/pull_up.ogg)
- [`Resources/Audio/_WF/Planets/Flight/sink_rate.ogg`](../../../Resources/Audio/_WF/Planets/Flight/sink_rate.ogg)
- [`Resources/Audio/_WF/Planets/Flight/skid.ogg`](../../../Resources/Audio/_WF/Planets/Flight/skid.ogg)
- [`Resources/Audio/_WF/Planets/Flight/takeoff.ogg`](../../../Resources/Audio/_WF/Planets/Flight/takeoff.ogg)
- [`Resources/Audio/_WF/Planets/Flight/terrain.ogg`](../../../Resources/Audio/_WF/Planets/Flight/terrain.ogg)
- [`Resources/Audio/_WF/Planets/Flight/thrust_loop.ogg`](../../../Resources/Audio/_WF/Planets/Flight/thrust_loop.ogg)
- [`Resources/Audio/_WF/Planets/Flight/too_low_terrain.ogg`](../../../Resources/Audio/_WF/Planets/Flight/too_low_terrain.ogg)
- [`Resources/Audio/_WF/Planets/Weather/attributions.yml`](../../../Resources/Audio/_WF/Planets/Weather/attributions.yml)
- [`Resources/Audio/_WF/Planets/Weather/rain_end.ogg`](../../../Resources/Audio/_WF/Planets/Weather/rain_end.ogg)
- [`Resources/Audio/_WF/Planets/Weather/rain_mid.ogg`](../../../Resources/Audio/_WF/Planets/Weather/rain_mid.ogg)
- [`Resources/Audio/_WF/Planets/Weather/rain_start.ogg`](../../../Resources/Audio/_WF/Planets/Weather/rain_start.ogg)
- [`Resources/Audio/_WF/Planets/Weather/thunder_1.ogg`](../../../Resources/Audio/_WF/Planets/Weather/thunder_1.ogg)
- [`Resources/Audio/_WF/Planets/Weather/thunder_2.ogg`](../../../Resources/Audio/_WF/Planets/Weather/thunder_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_3.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_3.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_4.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_4.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_3.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_3.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_4.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_4.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/attributions.yml`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/attributions.yml)
- [`Resources/Audio/_WF/Planets/Worlds/asclepiu/asclepiu_ambience_day_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/asclepiu/asclepiu_ambience_day_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/asclepiu/asclepiu_ambience_night_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/asclepiu/asclepiu_ambience_night_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/asclepiu/attributions.yml`](../../../Resources/Audio/_WF/Planets/Worlds/asclepiu/attributions.yml)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/anchoring_tendril_loop.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/anchoring_tendril_loop.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/attributions.yml`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/attributions.yml)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_ambience_day_loop_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_ambience_day_loop_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_ambience_night_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_ambience_night_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_ambience_night_loop_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_ambience_night_loop_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_3.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_3.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_night_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_night_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_night_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_night_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_night_3.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_night_3.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/tendril_deploy_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/tendril_deploy_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/tendril_deploy_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/tendril_deploy_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/tendril_deploy_3.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/tendril_deploy_3.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/fervidus/attributions.yml`](../../../Resources/Audio/_WF/Planets/Worlds/fervidus/attributions.yml)
- [`Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_3.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_3.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_4.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_4.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/merak/attributions.yml`](../../../Resources/Audio/_WF/Planets/Worlds/merak/attributions.yml)
- [`Resources/Audio/_WF/Planets/Worlds/merak/merak_ambience_day_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/merak/merak_ambience_day_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/merak/merak_ambience_night_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/merak/merak_ambience_night_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/thrascias/attributions.yml`](../../../Resources/Audio/_WF/Planets/Worlds/thrascias/attributions.yml)
- [`Resources/Audio/_WF/Planets/Worlds/thrascias/thracias_ambience_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/thrascias/thracias_ambience_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/thrascias/thracias_ambience_loop_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/thrascias/thracias_ambience_loop_2.ogg)

### Tools

- [`Tools/_WF/Planets/gen_flight_placeholders.py`](../../../Tools/_WF/Planets/gen_flight_placeholders.py)

### Docs

- [`Docs/_WF/Planets/PLANET_ECOLOGY.md`](../../../Docs/_WF/Planets/PLANET_ECOLOGY.md)
- [`Docs/_WF/Planets/PLAYTEST_CHECKLIST.md`](../../../Docs/_WF/Planets/PLAYTEST_CHECKLIST.md)
- [`Docs/_WF/Planets/SHIP_LANDING_ASSESSMENT.md`](../../../Docs/_WF/Planets/SHIP_LANDING_ASSESSMENT.md)
- [`Docs/_WF/Planets/ship_landing_measurements.json`](../../../Docs/_WF/Planets/ship_landing_measurements.json)

## Non-modular edits

- [`Content.Client/_CE/ZLevels/Core/Overlays/CEZLevelShadowOverlay.cs`](../../../Content.Client/_CE/ZLevels/Core/Overlays/CEZLevelShadowOverlay.cs): orbit is above the atmosphere, not a storey up.
- [`Content.Client/_CE/ZLevels/Core/ScalingViewport.CEZLevels.cs`](../../../Content.Client/_CE/ZLevels/Core/ScalingViewport.CEZLevels.cs): a planet's layers keep the perspective Monolith turned off
- [`Content.Client/_FarHorizons/StarSystem/PlanetOverlay.cs`](../../../Content.Client/_FarHorizons/StarSystem/PlanetOverlay.cs): public (was private), so the planet approach can draw the very same body over the orbit hop.
- [`Content.Client/_Mono/Audio/AudioEchoSystem.cs`](../../../Content.Client/_Mono/Audio/AudioEchoSystem.cs)
  - a ray from a non-finite position is not cast, since a NaN crashes the client.
  - never hand MathF.Sign a NaN.
- [`Content.Client/Audio/ContentAudioSystem.AmbientMusic.cs`](../../../Content.Client/Audio/ContentAudioSystem.AmbientMusic.cs)
  - planet soundscapes replace ordinary ambient music.
  - no ordinary ambient music on a planet, whether a biome, grid, timer or combat-exit request asked.
- [`Content.Client/Light/RoofOverlay.cs`](../../../Content.Client/Light/RoofOverlay.cs)
  - the open-grating check below reads tile definitions.
  - open grating is no roof.
- [`Content.Client/Shuttles/UI/NavScreen.xaml`](../../../Content.Client/Shuttles/UI/NavScreen.xaml): enter/leave planet orbit, which needs no FTL drive (xmlns:wf above).
- [`Content.Client/Shuttles/UI/NavScreen.xaml.cs`](../../../Content.Client/Shuttles/UI/NavScreen.xaml.cs): the orbit button reads its state off the console entity.
- [`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs)
  - retain the moving origin while panned.
  - a map-pinned radar must follow travel to another map.
  - terrain toggle overlays the radar, not the settings column.
  - a grid's map can change between BUI updates (FTL / planet layer travel).
  - cached planetary terrain beneath radar contacts.
- [`Content.Server/_CE/ZLevels/Core/CEZGroundFrictionController.cs`](../../_CE/ZLevels/Core/CEZGroundFrictionController.cs)
  - crash skids slide before regaining ordinary parked-hull grip.
  - detached crash sections can yaw while skidding, then regain normal parked grip.
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs)
  - over a planet only landing thrusters lift.
  - landing thrusters are the planet-side lift.
  - grids parked on a planet orbit layer never fall.
  - partial landing-thruster lift slows the sink, and this is where lift lost begins.
  - the sink uses the lift-adjusted gravity.
  - planetary landings can clear, land hard, break up or skid instead of only crashing.
  - leave clearance around planetary impact wrecks.
  - a lift-lost hull that touched down slowly enough lands hard and skids instead of exploding.
  - survivable ship breakup.
  - a crash with planar speed left ploughs on instead of stopping dead.
  - one crash, one bang.
  - a comma for the added silent argument.
  - only one blast of a crash plays its sound.
  - cargo virtual mass counts against pooled lift.
  - same virtual mass the lift check uses, so the readout agrees.
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.GroundFriction.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.GroundFriction.cs): a crash skid scales down the landed body's tile friction instead of adding drag.
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.PilotControl.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.PilotControl.cs)
  - the liftoff latch needs the pilot.
  - grounded planet ascent is a latched console action; airborne input keeps CE's normal control.
  - an orbit layer is left through the console's enter-atmosphere button, never on the keys.
  - a console latch feeds the same CE takeoff spool and flight integrator.
  - the climb is scaled by the maneuvering factor below.
  - only thrust left after hovering can climb.
  - planetary lift replaces, rather than supplements, the station gravgen gate.
  - pilots can't descend below a planet's ground.
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Transit.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.Transit.cs)
  - a grid on a planet orbit layer holds its height instead of sinking.
  - hulls leave orbit through transit and never hop below ground.
  - riders' contacts with the old map must not survive the move.
  - hulls never descend below a planet's ground.
  - a held climb pops out into a planet's orbit layer instead of pinning under it.
  - a descending convoy lands on the ground instead of hopping below it.
  - a hull that sets down breaks the trees and rocks it would rest against
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.View.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.View.cs): a planet's lower layers are drawn smaller, so their eyes see wider
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.WallCollision.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.WallCollision.cs): a skidding hull flattens obstacles instead of bouncing.
- [`Content.Server/_FarHorizons/StarSystem/StarSystemMapSystem.cs`](../../_FarHorizons/StarSystem/StarSystemMapSystem.cs): register sector bodies that have a Wolfgate surface.
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.API.cs`](../../Atmos/EntitySystems/AtmosphereSystem.API.cs): bare planet ground nobody built on isn't tracked
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.Processing.cs`](../../Atmos/EntitySystems/AtmosphereSystem.Processing.cs)
  - bare planet ground keeps no map tile alive
  - bare planet ground shares the map's air, like a tile off the grid
  - a tile built on a planet starts with the air around it, not a vacuum
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.Utils.cs`](../../Atmos/EntitySystems/AtmosphereSystem.Utils.cs): bare planet ground is off the atmos grid but still carries walls
- [`Content.Server/Chemistry/TileReactions/CreateEntityTileReaction.cs`](../../Chemistry/TileReactions/CreateEntityTileReaction.cs): spilled chimera blood cannot seed an unbounded planetary hive.
- [`Content.Server/Explosion/EntitySystems/ExplosionSystem.cs`](../../Explosion/EntitySystems/ExplosionSystem.cs)
  - silent parameter, see QueuedExplosion.Silent.
  - one audible contributor is enough to make the merged blast audible.
  - carries the silent flag into the queued explosion.
  - a silent blast still carves its crater, it just does not shake, push or sound.
  - upstream sound, re-indented under the silent guard above.
- [`Content.Server/Explosion/EntitySystems/ExplosionSystem.Processing.cs`](../../Explosion/EntitySystems/ExplosionSystem.Processing.cs): silent flag, so a crash's storm of blasts makes one bang.
- [`Content.Server/Movement/Systems/JetpackSystem.cs`](../../Movement/Systems/JetpackSystem.cs)
  - an atmospheric pack burns welding fuel from a solution, not gas from a tank.
  - a wearer carried below orbit on a hull never changes parent, so the pack is cut here.
- [`Content.Server/Parallax/BiomeSystem.ChunkLoader.cs`](../../Parallax/BiomeSystem.ChunkLoader.cs): a planet's rock spawner is rolled here from its tile, so the rock unloads and comes back the same
- [`Content.Server/Parallax/BiomeSystem.ConfigManager.cs`](../../Parallax/BiomeSystem.ConfigManager.cs): the loader's spawner rolls follow reloaded entity prototypes
- [`Content.Server/Parallax/BiomeSystem.cs`](../../Parallax/BiomeSystem.cs)
  - a chunk's own entities need no bookkeeping as its unload deletes them
  - planet layers unload on their own schedule, see BiomeSystem.WFUnload.cs
  - a planet layer is left to its own unloader
  - the far part of a planet layer's load area is put off
  - what was put off loads nearest first, within the pass's budget
- [`Content.Server/Parallax/BiomeSystem.PlayerTracker.cs`](../../Parallax/BiomeSystem.PlayerTracker.cs)
  - a planet layer loads nearest its loaders first
  - less loads at once round an eye than round a body
- [`Content.Server/Physics/Controllers/MoverController.cs`](../../Physics/Controllers/MoverController.cs): reserve thrust for planetary lift.
- [`Content.Server/Shuttles/Systems/ShuttleSystem.FasterThanLight.cs`](../../Shuttles/Systems/ShuttleSystem.FasterThanLight.cs)
  - the docking branch never calls TrySetupFTL, so it asks the same gate.
  - a planet is left from orbit, never from the surface, the air or mid-transit.
  - a hull coming down on a planet hurts a mob under it and shoves it clear, and gibs nobody
- [`Content.Server/Shuttles/Systems/ThrusterSystem.cs`](../../Shuttles/Systems/ThrusterSystem.cs)
  - show atmospheric rating and conversion status.
  - atmospheric overload recovery must survive power-change callbacks.
  - atmospheric efficiency and continuous power demand.
  - severed engines retain their last firing command while powered.
  - preserve upgraded rating across atmosphere transitions.
- [`Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Movement.cs`](../../../Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Movement.cs): planet terrain that isn't loaded is still ground
- [`Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Update.cs`](../../../Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Update.cs): z-motion carried off the z-network is cleared, not kept for the next planet.
- [`Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.WallCollision.cs`](../../../Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.WallCollision.cs): retain the contacted obstacle for crash ploughing.
- [`Content.Shared/_CE/ZLevels/Throwing/CEZLevelThrowingSystem.cs`](../../../Content.Shared/_CE/ZLevels/Throwing/CEZLevelThrowingSystem.cs): a throw only has an arc where there are levels to arc through.
- [`Content.Shared/CCVar/CCVars.Game.cs`](../../../Content.Shared/CCVar/CCVars.Game.cs): defaults to MonoStandard (was nfpirate), whose round start spawns the star system.
- [`Content.Shared/Maps/TileSystem.cs`](../../../Content.Shared/Maps/TileSystem.cs): lattice laid on a planet's ground gives the ground back, not a hole through the world.
- [`Content.Shared/Movement/Systems/SharedJetpackSystem.cs`](../../../Content.Shared/Movement/Systems/SharedJetpackSystem.cs)
  - the condition continues on the next line.
  - a jetpack cuts out below a planet's orbit layer.
  - an atmosphere refusal is said plainly, rather than the gravity line, which is not why it failed.
  - jetpacks fly on a planet's orbit layer but not in its atmosphere.
  - no jetpack below a planet's orbit layer.
- [`Content.Shared/Movement/Systems/SharedMoverController.Input.cs`](../../../Content.Shared/Movement/Systems/SharedMoverController.Input.cs): a planet surface is a map that is also a grid, and its tiles are ground, not space.
- [`Content.Shared/Parallax/Biomes/SharedBiomeSystem.cs`](../../../Content.Shared/Parallax/Biomes/SharedBiomeSystem.cs)
  - reuse one copy per source noise and seed, since callers only read it
  - keep the copy for later calls
- [`Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs`](../../../Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs): you only FTL out of a planet network from orbit, and never FTL into an orbit layer.
- [`Content.Shared/Tiles/FloorTileSystem.cs`](../../../Content.Shared/Tiles/FloorTileSystem.cs)
  - reads the planet ground under a tile.
  - a planet's natural ground takes lattice directly, and remembers what is under it.
  - lattice also goes straight onto planet ground.
  - the ground under lattice is kept so cutting it gives the ground back.
  - only a planet's untouched biome ground takes lattice directly.
- [`Resources/ConfigPresets/Build/development.toml`](../../../Resources/ConfigPresets/Build/development.toml): planet networks are on in development builds.
- [`Resources/Prototypes/_DV/Entities/Mobs/Species/harpy.yml`](../../../Resources/Prototypes/_DV/Entities/Mobs/Species/harpy.yml): z-level flight is back on, which Monolith#4812 turned off
- [`Resources/Prototypes/_FarHorizons/Space/systems.yml`](../../../Resources/Prototypes/_FarHorizons/Space/systems.yml)
  - orbit well kept out of lock-on range of Monolith's drone belt.
  - orbit well moved out of Monolith's drone belt, a hazard world at its edge.
  - quarantined biothreat world, unsanctioned.
- [`Resources/Prototypes/_Mono/Entities/Mobs/Chimera/biomass.yml`](../../../Resources/Prototypes/_Mono/Entities/Mobs/Chimera/biomass.yml): prevent action-spawned biomass from colonizing planets, too.
- [`Resources/Prototypes/_NF/Loadouts/contractor_loadout_groups.yml`](../../../Resources/Prototypes/_NF/Loadouts/contractor_loadout_groups.yml): planetary clock and weather HUD.
- [`Resources/Prototypes/Entities/Mobs/Species/moth.yml`](../../../Resources/Prototypes/Entities/Mobs/Species/moth.yml): z-level flight is back on, which Monolith#4812 turned off
- [`Resources/Prototypes/Entities/Structures/Machines/lathe.yml`](../../../Resources/Prototypes/Entities/Structures/Machines/lathe.yml): landing thruster kits and parachutes.

<!-- WOLFGATE-GENERATED END -->
