"""Compare two V5.3 run-summary JSONs without uploading either file."""
import argparse
import collections
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('ptit', type=Path)
parser.add_argument('vnu', type=Path)
parser.add_argument('--output', type=Path, default=Path('access-comparison.json'))
args = parser.parse_args()
left = json.loads(args.ptit.read_text(encoding='utf-8-sig'))
right = json.loads(args.vnu.read_text(encoding='utf-8-sig'))
if left['profileId'] != 'PTIT_IP' or right['profileId'] != 'VNU_ACCOUNT':
    raise SystemExit('Pass the PTIT_IP summary first and the VNU_ACCOUNT summary second.')
if left.get('csvSha256') != right.get('csvSha256'):
    raise SystemExit('CSV SHA-256 differs: use the exact same input bytes before comparing success rates.')
def key(row):
    return (row.get('Doi') or row.get('Url') or str(row['Id'])).strip().lower()
def index(report):
    result = {}
    for row in report['items']:
        identity = key(row)
        if identity in result:
            raise SystemExit('Duplicate paper identity in this comparison; use unique DOI/URL rows.')
        result[identity] = row
    return result
a, b = index(left), index(right)
if a.keys() != b.keys():
    raise SystemExit('Loaded paper sets differ despite the selected summaries; inspect the run files.')
changes = []
for identity in a:
    old, new = a[identity], b[identity]
    if (old['status'], old.get('Error')) != (new['status'], new.get('Error')):
        changes.append({'paper': identity, 'PTIT': old['status'], 'VNU': new['status'],
                        'PTIT_error': old.get('Error',''), 'VNU_error': new.get('Error','')})
report = {'sameCsvSha256': left['csvSha256'], 'rows': len(a),
          'bothFinished': left['finished'] and right['finished'],
          'PTIT': {'elapsedSeconds': left['elapsedSeconds'], 'statuses': left['statuses'],
                   'maxTransfers': left['maxTransfers'], 'maxPerPublisher': left['maxPerPublisher']},
          'VNU': {'elapsedSeconds': right['elapsedSeconds'], 'statuses': right['statuses'],
                  'maxTransfers': right['maxTransfers'], 'maxPerPublisher': right['maxPerPublisher']},
          'statusPairs': dict(collections.Counter(a[k]['status']+' -> '+b[k]['status'] for k in a)),
          'differences': changes,
          'interpretation': 'Actual entitlement, network, server changes, authentication waiting and configured limits can affect results; a profile label does not prove the actual external IP.'}
args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='differences'}, ensure_ascii=False))
