#!/usr/bin/env python3
import json
import os
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener

MAX_BYTES = 1048576
PROTOCOL = "2025-06-18"


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def endpoint(base):
    url = urlsplit(base)
    if (url.username or url.password or url.query or url.fragment or not url.hostname
        or (url.scheme != "https" and not
            (url.scheme == "http" and url.hostname in {"localhost", "127.0.0.1", "::1"}))):
        raise ValueError("HTTPS gateway or loopback required")
    return base.rstrip("/") + "/api/internal/data-gateway/mcp"


def gateway(message):
    address = endpoint(os.environ.get("SENOR_ARROZ_GATEWAY_URL", ""))
    token = os.environ.get("SENOR_ARROZ_GATEWAY_TOKEN", "")
    if not token:
        raise ValueError("Gateway token required")
    request = Request(address, data=json.dumps(message).encode("utf-8"), headers={
        "Authorization": "Bearer " + token, "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream", "MCP-Protocol-Version": PROTOCOL
    }, method="POST")
    try:
        with build_opener(NoRedirect()).open(request, timeout=7) as response:
            if response.status == 202:
                return None
            data = response.read(MAX_BYTES + 1)
            if len(data) > MAX_BYTES:
                raise ValueError("Gateway response exceeds limit")
            return json.loads(data)
    except HTTPError as error:
        raise ValueError("Gateway HTTP " + str(error.code)) from None
    except (URLError, TimeoutError, OSError):
        raise ValueError("Gateway unavailable") from None


def dispatch(line):
    global PROTOCOL
    try:
        message = json.loads(line)
    except (ValueError, TypeError):
        return {"jsonrpc": "2.0", "id": None, "error": {"code": -32700, "message": "Parse error"}}
    if (not isinstance(message, dict) or message.get("jsonrpc") != "2.0"
        or not isinstance(message.get("method"), str)
        or ("id" in message and (isinstance(message["id"], bool)
            or not isinstance(message["id"], (str, int, float))))):
        return {"jsonrpc": "2.0", "id": None, "error": {"code": -32600, "message": "Invalid request"}}
    if "id" not in message:
        return None
    try:
        answer = gateway(message)
        if message['method'] == 'initialize' and isinstance(answer, dict):
            version = answer.get('result', {}).get('protocolVersion')
            if version in {'2025-03-26', '2025-06-18', '2025-11-25'}:
                PROTOCOL = version
        return answer
    except Exception:
        return {"jsonrpc": "2.0", "id": message["id"], "error": {
            "code": -32603, "message": "Gateway unavailable or request rejected"}}


def main():
    while line := sys.stdin.buffer.readline(32769):
        if len(line) > 32768:
            while line and not line.endswith(b"\n"):
                line = sys.stdin.buffer.readline(32769)
            answer = {"jsonrpc": "2.0", "id": None, "error": {"code": -32600, "message": "Request exceeds limit"}}
        else:
            answer = dispatch(line)
        if answer is not None:
            sys.stdout.write(json.dumps(answer, ensure_ascii=True) + "\n")
            sys.stdout.flush()


if __name__ == "__main__":
    main()
