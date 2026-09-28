# Wolfgate Admin Tooling

## Admin menu tab
admin-menu-wolfgate-tab = Wolfgate
wf-admin-tab-spawn-vessel = Spawn Vessel

## Vessel spawn window
wf-vessel-spawn-title = Spawn Vessel
wf-vessel-spawn-search = Search by name, ID or class...
wf-vessel-spawn-size-label = Size:{" "}
wf-vessel-spawn-category-label = Category:{" "}
wf-vessel-spawn-owner-label = Owner:{" "}
wf-vessel-spawn-owner-none = None
wf-vessel-spawn-none = None
wf-vessel-spawn-button = Spawn
wf-vessel-spawn-count = Showing {$shown} of {$total} vessels
wf-vessel-spawn-none-selected = No vessel selected
wf-vessel-spawn-tooltip-none = Select a vessel to spawn it.
wf-vessel-spawn-tooltip-denied = You do not have permission to spawn vessels.

wf-vessel-spawn-detail-id = ID
wf-vessel-spawn-detail-category = Category
wf-vessel-spawn-detail-classes = Classes
wf-vessel-spawn-detail-size = Size
wf-vessel-spawn-detail-engines = Engines
wf-vessel-spawn-detail-price = Price
wf-vessel-spawn-detail-grid = Grid
wf-vessel-spawn-grid-value = {$width} x {$height} ({$count} tiles)

# Category filter and grouped-list headers
wf-vessel-spawn-category-all = All
wf-vessel-spawn-category-Salvage = Salvage
wf-vessel-spawn-category-Science = Science
wf-vessel-spawn-category-CargoEngineering = Cargo & Engineering
wf-vessel-spawn-category-Medical = Medical
wf-vessel-spawn-category-Civilian = Civilian & Service
wf-vessel-spawn-category-Expedition = Expedition
wf-vessel-spawn-category-Escort = Pursuit & Escort
wf-vessel-spawn-category-Security = Security
wf-vessel-spawn-category-Military = Military
wf-vessel-spawn-category-Antagonist = Antagonist
wf-vessel-spawn-category-Other = Other
wf-vessel-spawn-category-AdminOnly = Admin only

# Frontier defines the other shipyard class names but not this one.
shipyard-console-class-Mercenary = Mercenary

## spawnvessel command
cmd-spawnvessel-desc = Spawns a vessel prototype's grid at your current position, optionally registering it to a player: their account, and the ID card they carry as its deed.
cmd-spawnvessel-help = Usage: {$command} <vessel ID> [owner username]
cmd-spawnvessel-hint = <vessel ID>
cmd-spawnvessel-owner-hint = [owner username]
cmd-spawnvessel-invalid-args = Expected one or two arguments.
cmd-spawnvessel-no-entity = You need an entity in the world to spawn a vessel at.
cmd-spawnvessel-no-map = You are not on a map.
cmd-spawnvessel-unknown-vessel = No vessel prototype with ID "{$id}".
cmd-spawnvessel-failed = Failed to spawn vessel "{$id}". Check the server log.
cmd-spawnvessel-success = Spawned {$name} (entity {$uid}).
cmd-spawnvessel-owner-not-found = No connected player named "{$name}".
cmd-spawnvessel-owner-no-entity = {$name} has no body to hold an ID card.
cmd-spawnvessel-owner-ghost = {$name} is a ghost, and a ghost's ID card doesn't last.
cmd-spawnvessel-owner-no-id = {$name} has no ID card on them.
cmd-spawnvessel-owner-has-deed = {$name}'s ID card already holds a ship deed.
cmd-spawnvessel-owner-assigned = Deed assigned to {$name}, and the ship registered to their account.
cmd-spawnvessel-owner-registered = Registered to {$name}'s account, with no deed card: {$reason} Its access follows them into any body.
cmd-spawnvessel-owner-failed = Could not assign the deed to {$name}.

## Spawn as Outfit verb and window
wf-admin-verbs-spawn-outfit = Spawn as Outfit...
wf-admin-verbs-spawn-outfit-description = Spawns a humanoid wearing a chosen outfit at this entity, optionally taking control of it.
wf-spawn-outfit-title = Spawn as Outfit
wf-spawn-outfit-search = Search by job or gear ID...
wf-spawn-outfit-category-label = Category:{" "}
wf-spawn-outfit-category-all = All
wf-spawn-outfit-category-antagonists = Antagonists
wf-spawn-outfit-category-other = Other
wf-spawn-outfit-count = Showing {$shown} of {$total} outfits
wf-spawn-outfit-none-selected = No outfit selected
wf-spawn-outfit-contents = Contents
wf-spawn-outfit-in-hand = In hand
wf-spawn-outfit-storage = Storage ({$slot})
wf-spawn-outfit-body-label = Body:{" "}
wf-spawn-outfit-body-Random = Random human
wf-spawn-outfit-body-Default = Default human (bald John Doe)
wf-spawn-outfit-body-Own = My selected character
wf-spawn-outfit-control = Take control of the spawned mob
wf-spawn-outfit-confirm = Spawn
# Ghost mode (Ctrl+click a ghost as an admin)
wf-spawn-outfit-ghost-title = Spawn Ghost as Outfit
wf-spawn-outfit-ghost-info = Spawns [bold]{$name}[/bold]'s selected character wearing this outfit at their ghost and puts them in control.
wf-spawn-outfit-ghost-confirm = Spawn {$name}
# Set mode (the Set Outfit debug verb and setoutfit with only an entity)
wf-spawn-outfit-set-title = Set Outfit
wf-spawn-outfit-set-info = Dresses [bold]{$name}[/bold] in this outfit. Everything they currently wear drops to the floor.
wf-spawn-outfit-set-confirm = Apply to {$name}

## spawnoutfit command
cmd-spawnoutfit-desc = Spawns a humanoid wearing a starting gear outfit at the target entity.
cmd-spawnoutfit-help = Usage: {$command} <target entity> <starting gear ID> [take control: true/false] [body: Random/Default/Own]
cmd-spawnoutfit-hint-target = <target entity>
cmd-spawnoutfit-hint-gear = <starting gear ID>
cmd-spawnoutfit-hint-control = [true/false]
cmd-spawnoutfit-hint-body = [Random/Default/Own]
cmd-spawnoutfit-invalid-args = Expected two to four arguments.
cmd-spawnoutfit-no-player = Only players can run this command.
cmd-spawnoutfit-invalid-target = Invalid target entity.
cmd-spawnoutfit-unknown-gear = No starting gear prototype with ID "{$id}".
cmd-spawnoutfit-invalid-control = The take-control argument must be true or false.
cmd-spawnoutfit-invalid-body = The body argument must be Random, Default or Own.
cmd-spawnoutfit-control-forbidden = Taking control needs the Fun admin flag.
cmd-spawnoutfit-success = Spawned {$mob} wearing {$gear}.

## spawnoutfitui command (client)
cmd-spawnoutfitui-desc = Opens the Spawn as Outfit picker for a target entity.
cmd-spawnoutfitui-help = Usage: {$command} <target entity>
cmd-spawnoutfitui-invalid-args = Expected a target entity ID.
