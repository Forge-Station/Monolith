"""Verify that the native round persisted every probe log past the old lobby cap."""
import argparse
import json
import sqlite3
from pathlib import Path


def validate(directory):
    round_state = json.loads((directory / 'round.json').read_text(encoding='utf-8-sig'))
    expected = round_state['adminLogProbe']
    assert expected > 5000 and round_state['runLevel'] == 'InRound'
    assert round_state['preset'] == 'Sandbox' and round_state['roundId'] > 0
    log = (directory / 'server.log').read_text(encoding='utf-8-sig')
    assert '[ERRO]' not in log and '[FATL]' not in log
    metrics = dict(line.split(' ', 1) for line in (directory / 'metrics-final.prom').read_text().splitlines()
                   if line.startswith('admin_logs_') and '{' not in line)
    assert float(metrics['admin_logs_pre_round_queue']) == 0
    assert float(metrics['admin_logs_sent']) >= expected
    database = directory / 'data/preferences.db'
    with sqlite3.connect(database.resolve().as_uri() + '?mode=ro', uri=True) as connection:
        count, distinct, rounds = connection.execute(
            "SELECT count(*), count(DISTINCT message), count(DISTINCT round_id) FROM admin_log "
            "WHERE message LIKE 'NativeLab admin-log regression probe %' AND round_id = ?",
            (round_state['roundId'],)).fetchone()
    assert count == distinct == expected and rounds == 1, (count, distinct, rounds, expected)
    result = dict(status='PASS', round=round_state, persistedProbeLogs=count,
                  preRoundQueue=0, serverErrors=0)
    (directory / 'adminlogs-validation.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('directory', type=Path)
    print(json.dumps(validate(parser.parse_args().directory)))
