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


if __name__ == '__main__':
    unittest.main()
