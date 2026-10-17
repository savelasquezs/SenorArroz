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
from datetime import datetime

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
    marker = MARKER if isinstance(row, str) and MARKER in row else 'RoutingObservationV1 '
    if isinstance(row, str) and marker in row:
        try:
            parsed = json.JSONDecoder().raw_decode(row.split(marker, 1)[1].lstrip())[0]
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
        if row.get('outcome') not in ('generated', 'reused', 'failed', 'cancelled', 'observed'):
            ignored += 1
            continue
        seen.add(row['sampleId'])
        key = tuple(row.get(k) for k in ('tenantId', 'branchId', 'algorithm', 'experimentStartedAtUtc'))
        groups[key].append(row)
    result = []
    for key, rows in groups.items():
        report = dict(zip(('tenantId', 'branchId', 'algorithm', 'experimentStartedAtUtc'), key))
        observations = [r for r in rows if r['outcome'] == 'observed']
        requests = [r for r in rows if r['outcome'] != 'observed']
        report.update(samples=len(requests), outcomes=dict(Counter(r['outcome'] for r in requests)),
                      first=min(r.get('timestampUtc', '') for r in rows),
                      last=max(r.get('timestampUtc', '') for r in rows))
        google_returns = [r for r in observations if r.get('kind') == 'googleReturn']
        report['googleCalls'] = sum(r.get('googleCalls', 0) for r in requests) + len(google_returns)
        report['googleFailures'] = sum(r.get('googleFailures', 0) for r in requests) + sum(bool(r.get('googleFailed')) for r in google_returns)
        report['googleReturnMs'] = distribution(r['googleMs'] for r in google_returns if isinstance(r.get('googleMs'), (int, float)))
        report['timings'] = {}
        report['operations'] = operational_metrics(requests, observations)
        for field in FIELDS:
            values = sorted(float(r[field]) for r in requests
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
                          'operational metrics require matching observed events and include their coverage. '
                          'These measurements do not demonstrate causal before/after improvement.'}


def instant(value):
    try:
        return datetime.fromisoformat(value.replace('Z', '+00:00')).timestamp()
    except (ValueError, TypeError, AttributeError):
        return None


def distribution(values):
    ordered = sorted(v for v in values if math.isfinite(v))
    if not ordered:
        return {'n': 0}
    count = len(ordered)
    return {'n': count, 'median': (ordered[(count - 1) // 2] + ordered[count // 2]) / 2,
            'p90': ordered[math.ceil(count * .9) - 1]}


def operational_metrics(requests, observations):
    forecasts, returns, proposals = defaultdict(list), defaultdict(list), {}
    exclusions = Counter()
    for row in requests:
        at = instant(row.get('timestampUtc'))
        if at is None:
            continue
        for route in row.get('routeSnapshots') or []:
            proposals[route.get('proposalId')] = route
            for order in route.get('orders') or []:
                predicted = instant(order.get('EstimatedReadyAtUtc'))
                if predicted is not None:
                    forecasts[order.get('OrderId')].append((at, predicted))
        for courier in row.get('returnForecasts') or []:
            predicted = instant(courier.get('AvailableAtUtc'))
            if predicted is not None:
                returns[courier.get('CurrentRouteId')].append((at, predicted))
        exclusions.update(s.get('UnroutedReason') or 'unknown' for s in row.get('exclusions') or [])
    ready, departed, accepted, preparation_errors, return_errors = {}, {}, {}, [], []
    observed_ready = set()
    for row in observations:
        at = instant(row.get('occurredAtUtc'))
        if at is None:
            continue
        kind, route_id = row.get('kind'), row.get('routeId')
        if kind == 'ready':
            ready[(route_id, row.get('orderId'))] = at
            prior = [f for f in forecasts.get(row.get('orderId'), []) if f[0] < at]
            key = (row.get('orderId'), at)
            if prior and key not in observed_ready:
                preparation_errors.append(abs(max(prior)[1] - at))
            observed_ready.add(key)
        elif kind == 'departed':
            departed[route_id] = at
        elif kind == 'accepted':
            accepted[route_id] = row
        elif kind == 'returned':
            prior = [f for f in returns.get(route_id, []) if f[0] < at]
            if prior:
                return_errors.append(abs(max(prior)[1] - at))
    waits, actual_waits, recommended_waits, sizes = [], [], [], []
    for route_id, row in accepted.items():
        ids = row.get('orderIds') or []
        sizes.append(len(ids))
        if route_id not in departed:
            continue
        departure = departed[route_id]
        for order_id in ids:
            ready_at = ready.get((route_id, order_id), ready.get((None, order_id)))
            if ready_at is not None and departure >= ready_at:
                waits.append(departure - ready_at)
        accepted_at = instant(row.get('occurredAtUtc'))
        proposal = proposals.get(row.get('proposalId'), {})
        recommended = instant(proposal.get('ExpectedDepartureAtUtc'))
        if accepted_at is not None and departure >= accepted_at:
            actual_waits.append(departure - accepted_at)
            if recommended is not None:
                recommended_waits.append(max(0, recommended - accepted_at))
    return {'eventCounts': dict(Counter(r.get('kind', 'unknown') for r in observations)),
            'acceptedRoutes': len(accepted), 'modifiedRoutes': sum(bool(r.get('modified')) for r in accepted.values()),
            'ordersPerRoute': distribution(sizes), 'readyToDepartureSeconds': distribution(waits),
            'actualWaitSeconds': distribution(actual_waits), 'recommendedWaitSeconds': distribution(recommended_waits),
            'preparationAbsoluteErrorSeconds': distribution(preparation_errors),
            'returnAbsoluteErrorSeconds': distribution(return_errors), 'exclusionSnapshotsByReason': dict(exclusions)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('log', type=Path)
    args = parser.parse_args()
    with args.log.open(encoding='utf-8') as stream:
        print(json.dumps(summarize(stream), ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
