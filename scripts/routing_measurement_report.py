#!/usr/bin/env python3
"""Temporary P0 exporter. Input: JSONL or console logs; never prints raw log lines.
Usage: python scripts/routing_measurement_report.py exported.log > report.json
Remove with the P0 instrumentation after its documented 14-day window.
"""
import argparse
import json
import math
from collections import Counter, defaultdict
from pathlib import Path

MARKER = 'RoutingMeasurementV1 '
FIELDS = ('totalMs', 'lockWaitMs', 'inputMs', 'googleMs', 'solverMsOnPlan')


def extract(line):
    try:
        row = json.loads(line)
    except (json.JSONDecodeError, TypeError):
        row = line
    if isinstance(row, dict) and row.get('schema') == 1 and 'sampleId' in row:
        return row
    if isinstance(row, dict):
        payload = row.get('Payload') or row.get('payload')
        if isinstance(payload, str):
            try:
                parsed = json.loads(payload)
                if parsed.get('schema') == 1 and 'sampleId' in parsed:
                    return parsed
            except (ValueError, AttributeError):
                pass
        row = row.get('message') or row.get('Message') or ''
    if isinstance(row, str) and MARKER in row:
        try:
            parsed = json.JSONDecoder().raw_decode(row.split(MARKER, 1)[1].lstrip())[0]
            return parsed if isinstance(parsed, dict) else None
        except ValueError:
            pass
    return None


def summarize(lines):
    groups, seen = defaultdict(list), set()
    ignored = duplicates = 0
    for line in lines:
        row = extract(line)
        if not row or row.get('schema') != 1 or not row.get('sampleId'):
            ignored += 1
            continue
        if row['sampleId'] in seen:
            duplicates += 1
            continue
        if row.get('outcome') not in ('generated', 'reused', 'failed', 'cancelled'):
            ignored += 1
            continue
        seen.add(row['sampleId'])
        key = tuple(row.get(k) for k in ('tenantId', 'branchId', 'algorithm', 'experimentStartedAtUtc'))
        groups[key].append(row)
    result = []
    for key, rows in groups.items():
        report = dict(zip(('tenantId', 'branchId', 'algorithm', 'experimentStartedAtUtc'), key))
        report.update(samples=len(rows), outcomes=dict(Counter(r['outcome'] for r in rows)),
                      first=min(r.get('timestampUtc', '') for r in rows),
                      last=max(r.get('timestampUtc', '') for r in rows))
        report['googleCalls'] = sum(r.get('googleCalls', 0) for r in rows)
        report['googleFailures'] = sum(r.get('googleFailures', 0) for r in rows)
        report['timings'] = {}
        for field in FIELDS:
            values = sorted(float(r[field]) for r in rows
                            if isinstance(r.get(field), (float, int))
                            and math.isfinite(r[field]) and r[field] >= 0)
            report['timings'][field] = {'n': len(values)}
            if values:
                report['timings'][field].update(
                    median=values[(len(values) - 1) // 2] if len(values) % 2
                    else (values[len(values) // 2 - 1] + values[len(values) // 2]) / 2,
                    p90=values[math.ceil(len(values) * .9) - 1])
        result.append(report)
    return {'groups': result, 'ignoredLines': ignored, 'duplicates': duplicates,
            'limitation': 'Only exported samples are counted. Missing logs are not evidence of success; '
                          'dispatch time, kitchen forecast accuracy and causal before/after improvement '
                          'are not measured by this P0/P1 report.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('log', type=Path)
    args = parser.parse_args()
    with args.log.open(encoding='utf-8') as stream:
        print(json.dumps(summarize(stream), ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
