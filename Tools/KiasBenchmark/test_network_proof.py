import json
import tempfile
import unittest
from pathlib import Path
from network_proof import metric, validate, continuity


class NetworkMetricTests(unittest.TestCase):
    def test_native_serialization_label(self):
        self.assertEqual(metric('robust_game_state_update_usage_sum{area="Serialize States"} 0.25\n',
                                'robust_game_state_update_usage_sum', '{area="Serialize States"}'), .25)

    def test_missing_counter_rejected(self):
        with self.assertRaisesRegex(AssertionError, "Missing or ambiguous"):
            metric('# HELP robust_net_sent_bytes test\n', 'robust_net_sent_bytes')

    def test_nan_rejected(self):
        with self.assertRaisesRegex(AssertionError, "Invalid metric"):
            metric('robust_net_sent_bytes NaN\n', 'robust_net_sent_bytes')

    def test_duplicate_counter_rejected(self):
        with self.assertRaisesRegex(AssertionError, "Missing or ambiguous"):
            metric('robust_net_sent_bytes 1\nrobust_net_sent_bytes 2\n', 'robust_net_sent_bytes')


class NetworkProofTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.write('run-manifest.json', dict(networkClients=1, clientSandboxEnabled=True, performanceEligible=False))
        self.write('started.json', dict(measuredTicks=600, ships=1))
        self.write('completed.json', dict(status='PARTIAL_NATIVE_DRY_RUN_COMPLETE', measuredTicks=600, ships=1))
        self.write('network-sessions.json', dict(required=1, connected=1, bindings=[
            dict(user='test', ConnectionId=1, connected=True, status='InGame', name='client')]))
        for name in ('server.log', 'server-error.log', 'client.log', 'client-error.log'):
            (self.root / name).write_text('[INFO] complete\n', encoding='utf-8')
        names = ['robust_net_sent_bytes', 'robust_net_recv_bytes', 'robust_net_sent_messages',
                 'robust_net_recv_messages', 'robust_net_dropped', 'robust_net_resent_delay',
                 'robust_net_resent_hole', 'robust_game_state_update_usage_sum{area="Serialize States"}',
                 'robust_game_state_update_usage_count{area="Serialize States"}']
        for filename, value in (('metrics-0.prom', 1), ('metrics-final.prom', 2)):
            (self.root / filename).write_text(''.join(f'{name} {value}\n' for name in names), encoding='utf-8')

    def write(self, name, value):
        (self.root / name).write_text(json.dumps(value), encoding='utf-8')

    def test_complete_functional_network_keeps_performance_limit(self):
        proof = validate(self.root)
        self.assertEqual(proof['status'], 'PASS')
        self.assertFalse(proof['performanceEligible'])
        self.assertEqual(proof['measuredTicks'], 600)

    def write_history(self):
        self.write('run-manifest.json', dict(networkClients=1,clientSandboxEnabled=True,performanceEligible=False,networkHistoryRequired=True))
        binding=dict(connected=True,status='InGame')
        rows=[dict(checks=index+1,engineTick=index*60,admitted=True,required=1,connected=1,bindings=[binding],
                   admissionElapsedSeconds=minute*60,admittedUtc='2026-10-13T00:00:00Z',utc=f'2026-10-13T00:{minute:02}:00Z') for index,minute in enumerate((0,26))]
        (self.root/'network-session-history.jsonl').write_text(''.join(json.dumps(row)+'\n' for row in rows),encoding='utf-8')
        return rows

    def test_continuous_history_proves_wall_soak(self):
        self.write_history()
        proof=validate(self.root)['continuousAdmission']
        self.assertTrue(proof['exceedsOriginalGuestAfkTimeout'])
        self.assertEqual(proof['wallSeconds'],1560)

    def test_observed_soak_does_not_certify_incomplete_game(self):
        rows=self.write_history()
        (self.root/'completed.json').unlink()
        self.assertTrue(continuity(rows,1)['exceedsOriginalGuestAfkTimeout'])
        with self.assertRaises(FileNotFoundError):validate(self.root)

    def test_admission_cannot_reset_to_hide_a_disconnect(self):
        rows=self.write_history();rows[1]['admitted']=False;rows[1]['connected']=0
        with self.assertRaisesRegex(AssertionError,'Admission was reset'):continuity(rows,1)

    def test_transient_disconnect_cannot_hide_behind_final_snapshot(self):
        rows=self.write_history();rows[0]['connected']=0
        (self.root/'network-session-history.jsonl').write_text(''.join(json.dumps(row)+'\n' for row in rows),encoding='utf-8')
        with self.assertRaisesRegex(AssertionError,'lost after admission'):validate(self.root)

    def test_missing_history_checks_fail(self):
        rows=self.write_history();rows[1]['checks']=3
        (self.root/'network-session-history.jsonl').write_text(''.join(json.dumps(row)+'\n' for row in rows),encoding='utf-8')
        with self.assertRaisesRegex(AssertionError,'missing checks'):validate(self.root)

    def test_incomplete_failed_or_unsafe_network_is_rejected(self):
        for name, value, message in (
            ('completed.json', dict(status='RUNNING'), 'incomplete'),
            ('run-failed.json', {}, 'marked failed'),
            ('run-manifest.json', dict(networkClients=1, clientSandboxEnabled=False), 'sandbox disabled')):
            with self.subTest(name=name):
                path = self.root / name
                previous = path.read_bytes() if path.exists() else None
                self.write(name, value)
                with self.assertRaisesRegex(AssertionError, message):
                    validate(self.root)
                if previous is None:
                    path.unlink()
                else:
                    path.write_bytes(previous)

    def test_discord_client_errors_are_not_ignored(self):
        (self.root / 'client.log').write_text('[ERRO] discord: Failed to write a handshake.\n', encoding='utf-8')
        with self.assertRaisesRegex(AssertionError, 'Client errors'):
            validate(self.root)

    def test_server_errors_invalidate_otherwise_positive_network_metrics(self):
        (self.root / 'server-error.log').write_text('[ERRO] exception\n', encoding='utf-8')
        with self.assertRaisesRegex(AssertionError, 'Server errors'):
            validate(self.root)


if __name__ == '__main__':
    unittest.main()
