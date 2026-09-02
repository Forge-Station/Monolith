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
    .desc = Green-tinted goggles that overlay plant health and status squares on hydroponics trays: blue water, green weeds, yellow harvest, gold radiation.
ent-AgroScanCartridge = AgroScan cartridge
    .desc = A program that lets a PDA scan seeds and hydroponics trays like a plant analyzer.
ent-ComputerHydroponics = hydroponics tray console
    .desc = Lists linked trays with harvest / water / radiation / grab alerts, and stores named cultivar lines.
ent-HydroponicsConsoleCircuitboard = hydroponics tray console board
    .desc = A computer printed circuit board for a hydroponics tray console.
ent-BotanyCultivarDisk = cultivar disk
    .desc = Stores a named plant line for the hydroponics console journal.

hydroponics-console-alerts = Harvest {$harvest} · Water {$water} · Rad {$rad} · Grab {$grab}
hydroponics-console-filter-all = All
hydroponics-console-filter-harvest = Harvest
hydroponics-console-filter-water = Low water
hydroponics-console-filter-rad = Radiation
hydroponics-console-filter-grab = Carnivorous
hydroponics-console-view-trays = Trays
hydroponics-console-view-journal = Journal ({$count})
hydroponics-console-journal-header = Cultivars
hydroponics-console-journal-empty = No saved lines. Scan a tray and press Save line.
hydroponics-console-journal-none = Select a saved line.
hydroponics-console-journal-detail = Cultivar line
hydroponics-console-journal-name-placeholder = Line name
hydroponics-console-save-line = Save line
hydroponics-console-rename-line = Rename
hydroponics-console-print-packet = Print packet
hydroponics-console-eject-disk = Eject disk
hydroponics-console-delete-line = Delete
hydroponics-console-cycle-light = Cycle day/shade
hydroponics-console-field-light-mode = Light mode
hydroponics-console-field-ideal-light = Ideal light
hydroponics-console-save-ok = Saved cultivar "{$name}".
hydroponics-console-save-empty = That tray has no plant to save.
hydroponics-console-journal-full = Journal is full (24 lines). Delete one first.
hydroponics-console-print-packet-ok = Printed seed packet for "{$name}".
hydroponics-console-eject-disk-ok = Wrote "{$name}" to a cultivar disk.
hydroponics-console-disk-blank = That disk is blank.
hydroponics-console-disk-imported = Imported cultivar "{$name}".

botany-cultivar-disk-blank = The disk is blank.
botany-cultivar-disk-contents = Line: [color=lightgreen]{$name}[/color] ({$plant})

plant-holder-light-ambient = ambient
plant-holder-light-day = day
plant-holder-light-shade = shade
plant-holder-light-verb = Set light: {$mode}
plant-holder-light-mode-set = Tray light set to {$mode}.
plant-holder-light-mode-examine = Light mode: [color=lightgreen]{$mode}[/color]

plant-mutation-add-chemical-guidebook = Adds {$reagent} to harvested produce.
plant-mutation-add-consume-gas-guidebook = Makes the plant consume {$gas}.
plant-mutation-add-exude-gas-guidebook = Makes the plant exude {$gas}.
reagent-effect-guidebook-plant-lock-genes =
    { $chance ->
        [1] Locks
        *[other] lock
    } the plant's genes so mutagen no longer mutates that line

reagent-name-gene-stabilizer = gene stabilizer
reagent-desc-gene-stabilizer = Botanical fixative. Spray a living plant to lock its genome; unstable mutagen will no longer scramble that line.

ent-GeneStabilizerChemistryBottle = gene stabilizer bottle
    .desc = Locks a plant's genes so mutagen no longer mutates that line.
ent-AloeCream1 = aloe cream
    .desc = A topical cream for burns.
    .suffix = Single
ent-BotanyDriedProduce = dried produce
    .desc = Sun-dried plant matter. Color and flavor follow the cultivar it came from.

hydroponics-console-rename-tray = Rename tray
hydroponics-console-tray-name-placeholder = wheat-rad-3
hydroponics-console-rename-tray-ok = Tray renamed to "{$name}".
hydroponics-console-warn-pest-eater = pest-eater
hydroponics-console-warn-locked = genes locked

plant-holder-component-pest-eater-warning = [color=lightgreen]The plant is eating weeds and pests in the tray.[/color]
plant-holder-component-gene-locked-warning = [color=cyan]This line's genes are locked. Mutagen will not scramble it.[/color]
plant-analyzer-mutation-pest-eater = Pest-eater
plant-analyzer-mutation-gene-locked = Genes locked

botany-swab-from-packet = You lift pollen from the seed packet.
botany-swab-graft-packet = Pollen takes: mutations graft onto this {$name} packet.

seed-component-gene-locked = [color=cyan]Genes locked — mutagen will not scramble this line.[/color]

botany-plant-cloth-name = {$name} fiber
botany-plant-juice-name = {$name} juice
botany-plant-dried-name = dried {$name}
botany-aloe-tea-name = {$name} aloe tea
botany-antirad-tea-name = {$name} antirad tea
