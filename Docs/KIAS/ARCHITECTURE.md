# KIAS architecture — current model

KIAS is a ship-local automation/data system with programmable controller graphs.

## Network plane

The core and DATA topology determine which KIAS endpoints are reachable. Device discovery and controller selectors are scoped to the rack's current grid.

## Power plane

Native KIAS machines normally require their own functional power to be online.

Integrated third-party devices are special: KIAS may disable the target's working power while retaining a live integration/control endpoint. This is required so KIAS can issue the later ON command. Control-plane availability must therefore not be identical to target functional power for `KiasIntegrated` endpoints.

## Automation plane

Controller cards store graph programs. They execute only inside a powered online rack. Graphs are strict typed dataflow/event programs with persistent card data and volatile runtime state.

## Native systems remain authoritative

KIAS does not replace DeviceList/DeviceNetwork, AirAlarm logic, light/power systems, speech, or DeviceLink. It observes and controls them through supported APIs.

## Lighting model

Two distinct capabilities exist:

- direct `Lighting`: individual compatible light fixtures;
- group controllers (serialized profile `LightController`): KIAS controllers that operate a named group of fixtures.

## UI model

Player UIs should expose gameplay concepts rather than internal C# names. Technical IDs remain serialization/runtime details.
