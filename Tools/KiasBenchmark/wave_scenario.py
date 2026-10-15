"""Native seven-pair stress workload with fleet-wide same-tick waves; coverage remains partial."""
import argparse
import hashlib
import json
import random
from pathlib import Path
from expanded_scenario import PAIRS


def generate(ticks, ships, seed):
    if ticks < 7200 or not 1 <= ships <= 40:
        raise ValueError('At least 7200 ticks and 1..40 ships required')
    rng = random.Random(seed)
    events = []
    waves = []

    def pair(number, start, kinds, identifier):
        for kind, tick in zip(kinds, (start, start + 120)):
            event = dict(id=f'{identifier}-{number:04d}-{kind}', tick=tick,
                         ship=f'ship-{number:04d}', type=kind)
            if kind == 'anomaly.start':
                event['prototype'] = 'AnomalyFlesh'
            events.append(event)

    for index, kinds in enumerate(PAIRS):
        start = ticks // 10 + index * (ticks * 7 // 10 // len(PAIRS))
        waves.append(start)
        for number in range(1, ships + 1):
            pair(number, start, kinds, f'wave-{index:02d}')
    for number in range(1, ships + 1):
        for index, base in enumerate(range(ticks // 10, ticks * 9 // 10 - 300, 1200)):
            start = base + rng.randrange(600)
            if any(abs(start - wave) < 180 for wave in waves):
                continue
            pair(number, start, PAIRS[rng.randrange(len(PAIRS))], f'random-{index:04d}')
    events.sort(key=lambda event: (event['tick'], event['id']))
    busy = {}
    for event in events:
        key = event['ship']
        if event['type'].endswith(('start', 'loss', 'critical', 'power_off', 'damage')):
            assert busy.get(key, -1) < event['tick'], event
            busy[key] = event['tick'] + 120
    return events


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--ticks', type=int, default=108000)
    parser.add_argument('--ships', type=int, default=40)
    parser.add_argument('--seed', type=int, default=20261010)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    events = generate(args.ticks, args.ships, args.seed)
    data = ''.join(json.dumps(event, separators=(',', ':')) + '\n' for event in events).encode()
    args.output.write_bytes(data)
    print(json.dumps(dict(events=len(events), ships=args.ships, ticks=args.ticks,
                          seed=args.seed, fleetWaves=7, fullGameplayCoverage=False,
                          sha256=hashlib.sha256(data).hexdigest())))
