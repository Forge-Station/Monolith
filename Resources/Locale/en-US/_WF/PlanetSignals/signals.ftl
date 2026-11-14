wf-planet-signals-tag = { $vessels } { $vessels ->
        [one] vessel
       *[other] vessels
    }, { $sites } { $sites ->
        [one] site
       *[other] sites
    }, { $people } { $people ->
        [one] person
       *[other] people
    }
wf-planet-signals-tag-expedition = Your expedition is here
wf-planet-signals-tag-depot = {$depot}: land on its berths
wf-planet-signals-title = Signals from { $planet }
wf-planet-signals-expedition = Your expedition: { $site } at { $x }, { $y }
wf-planet-signals-vessels = Vessels
wf-planet-signals-sites = Sites
wf-planet-signals-none = Nothing on this world is giving off a signal.
wf-planet-signals-unknown-vessel = Unknown vessel
wf-planet-signals-vessel = { $name }, { $layer }, at { $x }, { $y }
wf-planet-signals-site = { $name } at { $x }, { $y }
wf-planet-signals-site-claimed = { $name } at { $x }, { $y }, expedition under way
wf-planet-signals-layer-ground = on the ground
wf-planet-signals-layer-air = in flight
wf-planet-signals-layer-orbit = in orbit
wf-planet-signals-marker-vessel = { $name }, { $layer }
wf-planet-signals-marker-expedition = Expedition: { $site }
wf-planet-signals-marker-distance = Expedition { $distance } m
wf-planet-signals-people = { $count } { $count ->
        [one] person
       *[other] people
    } on this world. Only those with a medical tracker can be named.
wf-planet-signals-tracked = Medical trackers
wf-planet-signals-person = { $name }, { $condition }, { $layer }, at { $x }, { $y }
wf-planet-signals-marker-person = { $name } ({ $condition })
wf-planet-signals-condition-alive = alive
wf-planet-signals-condition-critical = critical
wf-planet-signals-condition-dead = dead
wf-planet-signals-layer-underground = underground
