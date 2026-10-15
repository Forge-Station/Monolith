import copy
import unittest
from load_report import validate_availability


class AvailabilityTests(unittest.TestCase):
    def setUp(self):
        healthy = dict(activeGrids=40, activeMachines=1040, faultedCards=0, runningCards=1040,
                       availabilityPending=False, unavailableCards=[])
        pending = copy.deepcopy(healthy)
        pending.update(runningCards=1014, availabilityPending=True,
                       unavailableCards=[dict(topologyDirty=True, machineActive=True, fault='') for _ in range(26)])
        self.progress = [dict(relativeTick=tick, telemetry=value) for tick, value in
                         [(0, healthy), (3600, pending), (3660, healthy)]]

    def test_dirty_topology_must_recover_within_two_seconds(self):
        validate_availability(self.progress, ticks=4000)

    def test_stopped_machine_is_rejected(self):
        self.progress[1]['telemetry']['activeMachines'] = 1014
        with self.assertRaises(AssertionError): validate_availability(self.progress, ticks=4000)

    def test_fault_is_rejected(self):
        self.progress[1]['telemetry']['faultedCards'] = 1
        with self.assertRaises(AssertionError): validate_availability(self.progress, ticks=4000)

    def test_unchecked_drop_is_rejected(self):
        with self.assertRaises(AssertionError): validate_availability(self.progress[:2], ticks=4000)

    def test_persistent_or_non_topology_drop_is_rejected(self):
        self.progress[-1]['relativeTick'] = 3780
        with self.assertRaises(AssertionError): validate_availability(self.progress, ticks=4000)
        self.progress[-1]['relativeTick'] = 3660
        self.progress[1]['telemetry']['unavailableCards'][0]['topologyDirty'] = False
        with self.assertRaises(AssertionError): validate_availability(self.progress, ticks=4000)
