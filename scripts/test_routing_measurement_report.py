import importlib.util
import json
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('report', Path(__file__).with_name('routing_measurement_report.py'))
report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(report)


class MeasurementReportTests(unittest.TestCase):
    def sample(self, sample_id, **values):
        return dict(schema=1, sampleId=sample_id, tenantId=1, branchId=2,
                    algorithm='routing-p1', experimentStartedAtUtc='2026-10-16T18:00:00Z',
                    timestampUtc='2026-10-16T18:00:00Z', outcome='generated',
                    totalMs=10, googleCalls=1, googleFailures=0, **values)

    def test_json_and_console_are_deduplicated(self):
        row = self.sample('a')
        result = report.summarize([json.dumps(row), report.MARKER + json.dumps(row), 'other log'])
        self.assertEqual(result['duplicates'], 1)
        self.assertEqual(result['ignoredLines'], 1)
        self.assertEqual(result['groups'][0]['samples'], 1)
        self.assertEqual(result['groups'][0]['googleCalls'], 1)

    def test_quantiles_and_coverage_do_not_invent_missing_values(self):
        rows = []
        for i in range(10):
            row = self.sample(str(i))
            row['totalMs'] = i + 1
            rows.append(json.dumps(row))
        result = report.summarize(rows)['groups'][0]
        self.assertEqual(result['timings']['totalMs'], {'n': 10, 'median': 5.5, 'p90': 9.0})
        self.assertEqual(result['timings']['solverMsOnPlan'], {'n': 0})

    def test_other_tenants_and_versions_are_not_pooled(self):
        first = self.sample('a')
        second = self.sample('b')
        second.update(tenantId=9, algorithm='another-version')
        self.assertEqual(len(report.summarize(map(json.dumps, [first, second]))['groups']), 2)

    def test_bad_and_unrelated_lines_are_ignored(self):
        result = report.summarize(['{broken', '{"customerPhone":"not-exported"}', report.MARKER + '{}'])
        self.assertEqual(result['groups'], [])
        self.assertNotIn('customerPhone', json.dumps(result))

    def test_operational_events_join_predictions_and_do_not_count_as_planning_requests(self):
        forecast = self.sample('plan')
        forecast['routeSnapshots'] = [dict(proposalId=10, ExpectedDepartureAtUtc='2026-10-16T18:03:00Z',
            orders=[dict(OrderId=3, EstimatedReadyAtUtc='2026-10-16T18:01:00Z')])]
        def event(sample_id, kind, at, **extra):
            row = self.sample(sample_id)
            row.update(outcome='observed', kind=kind, routeId=8, occurredAtUtc=at, **extra)
            return row
        events = [forecast,
            event('ready', 'ready', '2026-10-16T18:02:00Z', orderId=3),
            event('claim', 'accepted', '2026-10-16T18:02:30Z', proposalId=10, orderIds=[3], modified=True),
            event('left', 'departed', '2026-10-16T18:04:00Z')]
        group = report.summarize(map(json.dumps, events))['groups'][0]
        self.assertEqual(group['samples'], 1)
        self.assertEqual(group['operations']['modifiedRoutes'], 1)
        self.assertEqual(group['operations']['readyToDepartureSeconds']['median'], 120)
        self.assertEqual(group['operations']['preparationAbsoluteErrorSeconds']['median'], 60)
        self.assertEqual(group['operations']['recommendedWaitSeconds']['median'], 30)

    def test_google_return_calls_are_counted_once_outside_request_finalist_metrics(self):
        request = self.sample('plan')
        returned = self.sample('return-call')
        returned.update(outcome='observed', kind='googleReturn', googleMs=120, googleFailed=True)
        returned.pop('googleCalls')
        result = report.summarize(map(json.dumps, [request, returned]))['groups'][0]
        self.assertEqual(result['googleCalls'], 2)
        self.assertEqual(result['googleFailures'], 1)
        self.assertEqual(result['googleReturnMs'], {'n': 1, 'median': 120, 'p90': 120})


if __name__ == '__main__':
    unittest.main()
