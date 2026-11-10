import importlib.util
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch
from urllib.error import HTTPError

spec = importlib.util.spec_from_file_location('gateway_stdio', Path(__file__).with_name('mcp_stdio.py'))
mcp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mcp)


class AdapterTests(unittest.TestCase):
    def test_urls(self):
        for url in ['http://evil.example', 'http://localhost.evil', 'https://user:secret@example.com',
                    'https://example.com?token=x', 'https://example.com#x', 'file:///etc/passwd']:
            with self.subTest(url=url), self.assertRaises(ValueError):
                mcp.endpoint(url)
        self.assertEqual('http://127.0.0.1:8000/api/internal/data-gateway/mcp', mcp.endpoint('http://127.0.0.1:8000'))

    def test_rpc_errors(self):
        self.assertEqual(-32700, mcp.dispatch('{')['error']['code'])
        for request in [[], {'jsonrpc': '2.0', 'method': 'ping', 'id': True}, {'id': 1}]:
            self.assertEqual(-32600, mcp.dispatch(json.dumps(request))['error']['code'])

    def test_notifications_and_transport_forwarding(self):
        with patch.object(mcp, 'gateway', return_value={'jsonrpc': '2.0', 'id': 1, 'result': {}}) as call:
            self.assertIsNone(mcp.dispatch('{"jsonrpc":"2.0","method":"notifications/initialized"}'))
            call.assert_not_called()
            self.assertEqual({}, mcp.dispatch('{"jsonrpc":"2.0","id":1,"method":"ping"}')['result'])

    def test_secrets_never_appear_in_errors(self):
        with patch.object(mcp, 'gateway', side_effect=ValueError('secret')):
            self.assertNotIn('secret', json.dumps(mcp.dispatch('{"jsonrpc":"2.0","id":1,"method":"ping"}')))

    def test_redirects_are_disabled(self):
        self.assertIsNone(mcp.NoRedirect().redirect_request(None, None, 302, '', {}, 'https://evil.example'))

    def test_oversized_line_is_drained_once(self):
        stream = io.TextIOWrapper(io.BytesIO(b'x' * 40000 + b'\n{"jsonrpc":"2.0","method":"notifications/initialized"}\n'))
        output = io.StringIO()
        with patch.object(mcp.sys, 'stdin', stream), patch.object(mcp.sys, 'stdout', output):
            mcp.main()
        self.assertEqual(1, len(output.getvalue().splitlines()))


if __name__ == '__main__':
    unittest.main()
