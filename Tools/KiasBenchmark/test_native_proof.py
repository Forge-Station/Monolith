import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from native_proof import validate


class NativeProofTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.scenario = self.root / "scenario.jsonl"
        self.write("scenario.jsonl", dict(id="one", ship="ship-0001", type="ftl.visitor_in", tick=120))
        self.write("started.json", dict(scenarioSha256=hashlib.sha256(self.scenario.read_bytes()).hexdigest(),
                                       plannedEvents=1, ships=1, measuredTicks=1000))
        self.write("completed.json", dict(status="PARTIAL_NATIVE_DRY_RUN_COMPLETE", deliveredEvents=1,
                                         ships=1, measuredTicks=1000, nativeCompleted=1))
        self.event = dict(id="one", ship="ship-0001", type="ftl.visitor_in", scheduledTick=120,
                          actualTick=120, engineTick=1922, nativeEffect=dict(asynchronous=True, deadlineTicks=300))
        self.outcome = dict(id="one", ship="ship-0001", type="ftl.visitor_in", scheduledTick=120,
                            nativeStartedTick=1922, nativeCompletedTick=2000, relativeCompletedTick=198, latencyTicks=78,
                            postconditionTrue=True, proof=dict(actualConsoleFtlCompleted=True))
        self.write("events.jsonl", self.event)
        self.write("native-outcomes.jsonl", self.outcome)
        (self.root / "server.log").write_text("[INFO] native complete\n")

    def write(self, name, value):
        (self.root / name).write_text(json.dumps(value) + "\n", encoding="utf-8")

    def test_valid_native_completion(self):
        self.assertEqual(validate(self.root, self.scenario)["nativeCompletions"], 1)

    def rotation_case(self, requested=37, actual=37, loaded=37):
        self.write('scenario.jsonl', dict(id='one', ship='ship-0001', type='collision.start', tick=120, fixtureRotationDegrees=requested))
        self.write('started.json', dict(scenarioSha256=hashlib.sha256(self.scenario.read_bytes()).hexdigest(), plannedEvents=1, ships=1, measuredTicks=1000))
        self.event['type'] = self.outcome['type'] = 'collision.start'
        self.outcome['proof'] = dict(fixtureRotationDegrees=actual, requestedFixtureRotationDegrees=loaded)
        self.write('events.jsonl', self.event)
        self.write('native-outcomes.jsonl', self.outcome)

    def test_requested_rotation_requires_actual_native_orientation(self):
        for angle in (0, 37, -.054, 359.9):
            with self.subTest(angle=angle):
                self.rotation_case(angle, angle + 360, angle)
                self.assertEqual(validate(self.root, self.scenario)['nativeCompletions'], 1)
        self.rotation_case(37, 0, 37)
        with self.assertRaisesRegex(AssertionError, 'Actual fixture rotation'):
            validate(self.root, self.scenario)

    def test_missing_loaded_rotation_cannot_pass_as_rotated_collision(self):
        self.rotation_case(37, 37, None)
        with self.assertRaisesRegex(AssertionError, 'Rotation lost'):
            validate(self.root, self.scenario)
        self.rotation_case(37, float('nan'), 37)
        with self.assertRaisesRegex(AssertionError, 'Missing actual fixture rotation'):
            validate(self.root, self.scenario)

    def room_observation(self, kind='door.open'):
        self.write('scenario.jsonl',dict(id='one',ship='ship-0001',type=kind,tick=120))
        self.write('started.json',dict(scenarioSha256=hashlib.sha256(self.scenario.read_bytes()).hexdigest(),plannedEvents=1,ships=1,measuredTicks=1000))
        self.event['type']=self.outcome['type']=kind
        self.outcome['proof']=dict(topology=dict(inputId='one',pendingAtDispatch=True,intentionalBreach=True,
            revisionBefore=1,revisionAfter=2,status='NoInteriorSeed',usable=False,advancedStatus='OpenToSpace'))
        self.write('events.jsonl',self.event)
        self.write('native-outcomes.jsonl',self.outcome)

    def test_door_observation_waits_for_topology_during_real_breach(self):
        self.room_observation()
        proof=validate(self.root,self.scenario)
        self.assertEqual(proof['nativeCompletions'],1)
        self.assertEqual(proof['roomObservationsStartedPending'],1)
        self.assertEqual(proof['doorsObservedDuringBreach'],1)

    def test_concurrent_room_inputs_cannot_share_the_wrong_observation(self):
        self.room_observation()
        self.outcome['proof']['topology']['inputId']='other'
        self.write('native-outcomes.jsonl',self.outcome)
        with self.assertRaisesRegex(AssertionError,'another input'):validate(self.root,self.scenario)

    def test_unusable_door_room_requires_actual_breach(self):
        self.room_observation()
        self.outcome['proof']['topology']['intentionalBreach']=False
        self.write('native-outcomes.jsonl',self.outcome)
        with self.assertRaisesRegex(AssertionError,'without an actual breach'):validate(self.root,self.scenario)

    def test_geometry_repair_requires_committed_new_revision(self):
        self.room_observation('geometry.repair')
        self.outcome['proof']['topology'].update(revisionAfter=1,status='Ok',usable=True,intentionalBreach=False)
        self.write('native-outcomes.jsonl',self.outcome)
        with self.assertRaisesRegex(AssertionError,'did not advance'):validate(self.root,self.scenario)

    def test_crew_transfer_rejects_deleted_platform(self):
        self.scenario.write_text(json.dumps(dict(id='one',ship='ship-0001',type='crew.return',tick=120))+'\n',encoding='utf-8')
        self.write('started.json',dict(scenarioSha256=hashlib.sha256(self.scenario.read_bytes()).hexdigest(),plannedEvents=1,ships=1,measuredTicks=1000))
        self.event['type']=self.outcome['type']='crew.return'
        for retained in (False,True):
            self.outcome['proof']=dict(nativeGridTransfer=True,transferPlatformRetained=retained,reusableTransferPlatform='42')
            self.write('events.jsonl',self.event);self.write('native-outcomes.jsonl',self.outcome)
            if retained:
                self.assertEqual(validate(self.root,self.scenario)['nativeCompletions'],1)
            else:
                with self.assertRaisesRegex(AssertionError,'platform removed'):
                    validate(self.root,self.scenario)

    def write_unsorted_scenario(self, reverse_dispatch=False):
        immediate = dict(id="before", ship="ship-0001", type="power.loss", tick=120)
        self.scenario.write_text(json.dumps(dict(id="one", ship="ship-0001", type="ftl.visitor_in", tick=120)) + "\n"
                                 + json.dumps(immediate) + "\n", encoding="utf-8")
        self.write("started.json", dict(scenarioSha256=hashlib.sha256(self.scenario.read_bytes()).hexdigest(),
                                       plannedEvents=2, ships=1, measuredTicks=1000))
        self.write("completed.json", dict(status="PARTIAL_NATIVE_DRY_RUN_COMPLETE", deliveredEvents=2,
                                         ships=1, measuredTicks=1000, nativeCompleted=1))
        immediate_event = dict(id="before", ship="ship-0001", type="power.loss", scheduledTick=120,
                               actualTick=120, engineTick=1922, nativeEffect=dict(disabled=True))
        events = [self.event, immediate_event] if reverse_dispatch else [immediate_event, self.event]
        (self.root / "events.jsonl").write_text("".join(json.dumps(event) + "\n" for event in events), encoding="utf-8")

    def test_validator_matches_native_tick_then_id_scheduling(self):
        self.write_unsorted_scenario()
        self.assertEqual(validate(self.root, self.scenario)["inputs"], 2)

    def test_reordered_native_dispatch_is_still_rejected(self):
        self.write_unsorted_scenario(reverse_dispatch=True)
        with self.assertRaisesRegex(AssertionError, "binding mismatch"):
            validate(self.root, self.scenario)

    def test_missing_completion_rejected(self):
        (self.root / "native-outcomes.jsonl").write_text("")
        with self.assertRaisesRegex(AssertionError, "Missing or unexpected"):
            validate(self.root, self.scenario)

    def test_late_dispatch_rejected(self):
        self.event["actualTick"] += 1
        self.write("events.jsonl", self.event)
        with self.assertRaisesRegex(AssertionError, "Late synchronous"):
            validate(self.root, self.scenario)

    def test_null_proof_rejected(self):
        self.outcome["proof"] = None
        self.write("native-outcomes.jsonl", self.outcome)
        with self.assertRaisesRegex(AssertionError, "Missing native proof"):
            validate(self.root, self.scenario)

    def test_completion_after_deadline_rejected(self):
        self.outcome.update(nativeCompletedTick=2300, latencyTicks=378)
        self.write("native-outcomes.jsonl", self.outcome)
        with self.assertRaisesRegex(AssertionError, "exceeded deadline"):
            validate(self.root, self.scenario)

    def test_server_error_rejected(self):
        (self.root / "server.log").write_text("[ERRO] runtime failure\n")
        with self.assertRaisesRegex(AssertionError, "Server errors"):
            validate(self.root, self.scenario)

    def test_completion_outside_measurement_is_rejected_even_within_deadline(self):
        self.event["nativeEffect"]["deadlineTicks"] = 2000
        self.write("events.jsonl", self.event)
        self.outcome.update(nativeCompletedTick=2900, relativeCompletedTick=1098, latencyTicks=978)
        self.write("native-outcomes.jsonl", self.outcome)
        with self.assertRaisesRegex(AssertionError, "outside measured window"):
            validate(self.root, self.scenario)

    def test_completion_clock_cannot_be_forged(self):
        self.outcome["relativeCompletedTick"] = 120
        self.write("native-outcomes.jsonl", self.outcome)
        with self.assertRaisesRegex(AssertionError, "completion clock"):
            validate(self.root, self.scenario)

    def test_boolean_and_fractional_ticks_are_rejected(self):
        for value in (True, 120.0, 120.5, -1):
            with self.subTest(value=value):
                self.event["actualTick"] = value
                self.write("events.jsonl", self.event)
                with self.assertRaisesRegex(AssertionError, "Invalid integer"):
                    validate(self.root, self.scenario)

    def test_events_at_different_ticks_must_share_engine_origin(self):
        self.write_unsorted_scenario()
        inputs = [json.loads(line) for line in self.scenario.read_text().splitlines()]
        inputs[0]["tick"] = 130
        self.scenario.write_text("".join(json.dumps(row) + "\n" for row in inputs))
        start = json.loads((self.root / "started.json").read_text())
        start["scenarioSha256"] = hashlib.sha256(self.scenario.read_bytes()).hexdigest()
        self.write("started.json", start)
        events = [json.loads(line) for line in (self.root / "events.jsonl").read_text().splitlines()]
        events[1].update(scheduledTick=130, actualTick=130, engineTick=1933)
        (self.root / "events.jsonl").write_text("".join(json.dumps(row) + "\n" for row in events))
        with self.assertRaisesRegex(AssertionError, "engine tick origin"):
            validate(self.root, self.scenario)


if __name__ == "__main__":
    unittest.main()

    def prepare_data_proof(self, kind="data.restore"):
        self.write("scenario.jsonl", dict(id="one",ship="ship-0001",type=kind,tick=120))
        start=json.loads((self.root/"started.json").read_text())
        start["scenarioSha256"]=hashlib.sha256(self.scenario.read_bytes()).hexdigest()
        self.write("started.json",start)
        self.event["type"]=kind
        self.outcome["type"]=kind
        self.outcome["proof"]=dict(preservedCards=26,allInitiallyOnlineDevicesRestored=True,
            dataIntegrity=dict(floorTilesUnchanged=True,floorBeforeSha256="a"*64,floorAfterSha256="a"*64,
                oldCableEntitiesDeleted=True,looseCableStacks=0,dynamicCableBodies=0,
                actualRemovedCables=264,actualRestoredCables=264,exactCableInventoryRestored=True,
                baselineDuplicateCableEntries=2,addedDuplicateCableEntries=0))
        self.write("events.jsonl",self.event)
        self.write("native-outcomes.jsonl",self.outcome)

    def test_exact_data_multiset_restored_under_unchanged_floor(self):
        self.prepare_data_proof()
        self.assertEqual(validate(self.root,self.scenario)["nativeCompletions"],1)

    def test_data_floor_mutation_is_rejected(self):
        self.prepare_data_proof()
        self.outcome["proof"]["dataIntegrity"]["floorAfterSha256"]="b"*64
        self.write("native-outcomes.jsonl",self.outcome)
        with self.assertRaisesRegex(AssertionError,"floor hash"):
            validate(self.root,self.scenario)

    def test_data_loose_coils_are_rejected(self):
        self.prepare_data_proof()
        self.outcome["proof"]["dataIntegrity"]["looseCableStacks"]=1
        self.write("native-outcomes.jsonl",self.outcome)
        with self.assertRaisesRegex(AssertionError,"physics coils"):
            validate(self.root,self.scenario)

    def test_data_extra_duplicate_is_rejected(self):
        self.prepare_data_proof()
        self.outcome["proof"]["dataIntegrity"]["addedDuplicateCableEntries"]=1
        self.write("native-outcomes.jsonl",self.outcome)
        with self.assertRaisesRegex(AssertionError,"duplicate"):
            validate(self.root,self.scenario)
