"""Reserve Cosmic Digest's fixed 25k daily slice before CI inference.
Only aggregate reservation metadata is stored. The workflow must push this file
successfully before setting DIGEST_AI_LEASE for the model process.
"""
import datetime as dt
import json
import os
from pathlib import Path
import sys

CAP = 25_000

def reserve(path, lease, now):
    data = json.loads(path.read_text())
    if data.get('version') != 1 or not isinstance(data.get('days'), dict):
        raise ValueError('Invalid allowance ledger')
    day = now.strftime('%Y-%m-%d')
    if any(k > day for k in data['days']):
        raise ValueError('Clock moved behind the allowance ledger')
    if day in data['days'] or now.hour == 23 and now.minute >= 55:
        return False
    data['days'][day] = {'lease': lease, 'reserved_tokens': CAP}
    # Keep a bounded audit, never discard today's reservation.
    data['days'] = dict(sorted(data['days'].items())[-35:])
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(data, indent=2) + '\n')
    temporary.replace(path)
    return True

if __name__ == '__main__':
    lease = os.environ['GITHUB_RUN_ID'] + '-' + os.environ['GITHUB_RUN_ATTEMPT']
    allowed = reserve(Path('data/ai-allowance.json'), lease, dt.datetime.now(dt.timezone.utc))
    with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
        output.write(f'allowed={str(allowed).lower()}\nlease={lease if allowed else ""}\n')
