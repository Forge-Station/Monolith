agro-scan-program-name = AgroScan

plant-mutation-exude-gas-guidebook = Mutates the plant so it can exude a gas. Rarer gases are much less likely.
plant-mutation-consume-gas-guidebook = Mutates the plant so it requires a gas, and retunes temperature/pressure needs.
plant-mutation-chemicals-guidebook = Adds a reagent to harvested produce. Unique phytochemicals need extra mutations or growing conditions.

hydroponics-console-title = Hydroponics Monitor
hydroponics-console-subtitle = Linked hydroponics network
hydroponics-console-os = HydroMonitor#OS ™ v1.0
hydroponics-console-no-trays = No trays linked. Use a network configurator to add hydroponics trays.
hydroponics-console-tray-count = Linked trays: {$count}
hydroponics-console-list-header = Trays
hydroponics-console-list-empty = No devices
hydroponics-console-list-empty-tray = empty
hydroponics-console-detail-header = Tray data
hydroponics-console-detail-none = Select a tray from the list.
hydroponics-console-detail-empty = This tray has no plant.
hydroponics-console-section-status = Status
hydroponics-console-section-warnings = Warnings
hydroponics-console-section-atmos = Atmosphere
hydroponics-console-section-genes = Genetics
hydroponics-console-field-status = State
hydroponics-console-field-health = Health
hydroponics-console-field-age = Age
hydroponics-console-field-water = Water
hydroponics-console-field-nutrition = Nutrients
hydroponics-console-field-toxins = Toxins
hydroponics-console-field-weeds = Weeds
hydroponics-console-field-pests = Pests
hydroponics-console-field-heat = Ideal heat
hydroponics-console-field-pressure = Pressure
hydroponics-console-field-consume = Consumes
hydroponics-console-field-exude = Exudes
hydroponics-console-field-chems = Reagents
hydroponics-console-field-mutations = Mutations
hydroponics-console-warnings-none = Environment stable
hydroponics-console-tray-header = {$tray} [{$address}] — {$plant}
hydroponics-console-tray-empty = {$tray} [{$address}] — empty
hydroponics-console-status-growing = Growing
hydroponics-console-status-harvest = Harvest
hydroponics-console-status-dead = Dead
hydroponics-console-tray-stats = {$status} | HP {$health}/{$endurance} | Age {$age} | Water {$water} | Nutrients {$nutrition} | Toxins {$toxins} | Weeds {$weeds} | Pests {$pests}
hydroponics-console-warnings = Warnings: {$warnings}
hydroponics-console-warn-heat = heat
hydroponics-console-warn-pressure = pressure
hydroponics-console-warn-light = light
hydroponics-console-warn-gas = missing gas
hydroponics-console-warn-radiation = radioactive
hydroponics-console-warn-grab = carnivorous
hydroponics-console-atmos = Atmos: {$heat}K ±{$heatTol} | {$lowP}-{$highP} kPa | consume {$consume} | exude {$exude}
hydroponics-console-genes = Reagents: {$chems} | Mutations: {$mutations}

plant-grab-caught = Vines lash out from {$plant} and wrap around you!
plant-grab-damage = The vines squeeze tighter!
plant-grab-break-free = You tear yourself free of the vines.

plant-holder-component-radioactive-warning = [color=yellow]The plant is emitting ionizing radiation.[/color]
plant-holder-component-grabber-warning = [color=red]The plant looks ready to grab anything that gets too close.[/color]

ent-ActionPlantGrabBreakFree = Tear free
    .desc = Rip yourself out of the plant's vines.

device-address-prefix-hydroponics = HYD-
device-address-prefix-hydroponics-console = HYC-

reagent-name-phyto-aether = phyto-aether
reagent-desc-phyto-aether = A rare phytochemical that only appears in high-potency plants. Mildly stimulating, toxic in overdose, and slightly mutagenic to other plants.
reagent-name-lignin-resin = lignin resin
reagent-desc-lignin-resin = Thick sap from ligneous plants. Slowly knits flesh while gumming up joints.
reagent-name-cryo-chlorophyll = cryo-chlorophyll
reagent-desc-cryo-chlorophyll = Cold-adapted chlorophyll extracted from plants that prefer near-freezing trays. Cools the body.
reagent-name-photozyme = photozyme
reagent-desc-photozyme = A light-fixing enzyme from high-lux cultivars. Filling, and an excellent fertilizer.
reagent-name-necrotoxin-sap = necrotoxin sap
reagent-desc-necrotoxin-sap = Kudzu-mutated sap that rots living tissue. Handle with gloves.
reagent-name-radbloom = radbloom extract
reagent-desc-radbloom = Distillate of radioactive flora. Small doses treat radiation; larger ones cause it.
reagent-name-vine-ichor = vine ichor
reagent-desc-vine-ichor = Ichor from carnivorous vines. Closes slashes while wrapping the lungs.
reagent-name-atmo-phyte = atmophyte
reagent-desc-atmo-phyte = Gas-processing phytochemical from atmosphere-mutated plants. Helps you breathe.

necrotoxin-sap-warning = Your veins burn with plant toxins!

ent-ClothingEyesGlassesBotany = botanical analyzer glasses
    .desc = Green-tinted goggles that overlay plant health on hydroponics trays.
ent-AgroScanCartridge = AgroScan cartridge
    .desc = A program that lets a PDA scan seeds and hydroponics trays like a plant analyzer.
ent-ComputerHydroponics = hydroponics tray console
    .desc = Lists every hydroponics tray linked with a network configurator and reports plant health, atmosphere needs, and mutations.
ent-HydroponicsConsoleCircuitboard = hydroponics tray console board
    .desc = A computer printed circuit board for a hydroponics tray console.
