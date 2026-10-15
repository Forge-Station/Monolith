"""Deterministic seven-driver workload; explicitly incomplete gameplay coverage."""
import argparse
import hashlib
import json
from pathlib import Path
from lab import XorShift32

PAIRS = [('fire.start', 'fire.clear'), ('power.loss', 'power.restore'),
         ('crew.critical', 'crew.recover'), ('light.power_off', 'light.power_on'),
         ('hull.damage', 'hull.repair'), ('radiation.start', 'radiation.clear'),
         ('anomaly.start', 'anomaly.clear')]


def generate(ticks, seed=20261009):
    if ticks < 1200:
        raise ValueError('Expanded preflight requires at least 1200 measured ticks.')
    rng = XorShift32(seed)
    events = []
    schedule = ([(start, 120) for start in range(7200, 54000-120, 120)] +
                [(start, 120) for start in range(54000, 64800-120, 15)]
                if ticks == 72000 else [(start, 30) for start in range(ticks//10, ticks*9//10-60, 120)])
    busy_until = {}
    for index, (start, lifetime) in enumerate(schedule):
        candidates = [number for number in range(1, 41) if busy_until.get(number, 0) <= start]
        number = candidates[rng.next() % len(candidates)]
        busy_until[number] = start + lifetime
        ship = f'ship-{number:04d}'
        pair = PAIRS[index % len(PAIRS)]
        for kind, tick in zip(pair, (start, start + lifetime)):
            event = dict(id=f'expanded-{index:05d}-{kind}', tick=tick, ship=ship, type=kind)
            if kind == 'anomaly.start':
                event['prototype'] = 'AnomalyFlesh'
            events.append(event)
    return events


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--ticks', type=int, default=1200)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    data = ''.join(json.dumps(event, separators=(',', ':'))+'\n' for event in generate(args.ticks)).encode()
    args.output.write_bytes(data)
    print(json.dumps({'events': len(data.splitlines()), 'ticks': args.ticks,
                      'sha256': hashlib.sha256(data).hexdigest(), 'coverage': 'SEVEN_NATIVE_DRIVER_PAIRS_ONLY'}))
