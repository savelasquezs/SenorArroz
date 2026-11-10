#!/usr/bin/env python3
"""Minimal stdio MCP adapter for the Señor Arroz diagnostic HTTP gateway.

Local developer use only. ChatGPT remote MCP requires a separately authenticated
HTTPS MCP server. Tokens are never logged or written to disk.
"""
import json
import os
import sys
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

BASE = os.environ.get("SENOR_ARROZ_GATEWAY_URL", "").rstrip("/")
TOKEN = os.environ.get("SENOR_ARROZ_GATEWAY_TOKEN", "")
PROTOCOL = "2025-03-26"

TOOLS = [
    {"name": "get_schema", "description": "List authorized diagnostic views and columns",
     "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "execute_query", "description": "Read rows from an explicitly allowed diagnostic view; no raw SQL",
     "inputSchema": {"type": "object", "properties": {
         "view": {"type": "string"}, "columns": {"type": "array", "items": {"type": "string"}},
         "filters": {"type": "array", "items": {"type": "object", "required": ["column", "op", "value"],
             "properties": {"column": {"type": "string"},
                            "op": {"type": "string", "enum": ["eq", "ne", "gt", "gte", "lt", "lte"]},
                            "value": {"type": ["string", "number", "boolean", "null"]}}}},
         "orderBy": {"type": "string"}, "descending": {"type": "boolean"},
         "maxRows": {"type": "integer", "minimum": 1, "maximum": 500}},
         "required": ["view"], "additionalProperties": False}},
    {"name": "explain_query", "description": "Get EXPLAIN without ANALYZE for a structured view query",
     "inputSchema": {"type": "object", "properties": {
         "view": {"type": "string"}, "columns": {"type": "array", "items": {"type": "string"}},
         "filters": {"type": "array", "items": {"type": "object"}},
         "orderBy": {"type": "string"}, "descending": {"type": "boolean"},
         "maxRows": {"type": "integer", "minimum": 1, "maximum": 500}},
         "required": ["view"], "additionalProperties": False}},
]


def gateway(path, payload=None):
    if not BASE.startswith("https://") and not BASE.startswith("http://localhost:"):
        raise ValueError("Use HTTPS gateway (or local loopback)")
    if not TOKEN:
        raise ValueError("SENOR_ARROZ_GATEWAY_TOKEN is required")
    headers = {"Authorization": "Bearer " + TOKEN, "Accept": "application/json"}
    body = None
    if payload is not None:
        headers["Content-Type"] = "application/json"
        body = json.dumps(payload).encode("utf-8")
    request = Request(BASE + "/api/internal/data-gateway/" + path,
                      headers=headers, data=body, method="POST" if body else "GET")
    try:
        with urlopen(request, timeout=7) as response:
            return json.loads(response.read(1024 * 1024).decode("utf-8"))
    except HTTPError as error:
        raise ValueError("Gateway HTTP " + str(error.code)) from None
    except URLError:
        raise ValueError("Gateway unavailable") from None


def handle(message):
    method = message.get("method")
    if method == "initialize":
        return {"protocolVersion": PROTOCOL,
                "capabilities": {"tools": {"listChanged": False}},
                "serverInfo": {"name": "senor-arroz-data-gateway", "version": "0.1.0"}}
    if method == "ping":
        return {}
    if method == "tools/list":
        return {"tools": TOOLS}
    if method == "tools/call":
        data = message.get("params") or {}
        name = data.get("name")
        if name not in ("get_schema", "execute_query", "explain_query"):
            raise ValueError("Unknown tool")
        args = data.get("arguments") or {}
        if not isinstance(args, dict):
            raise ValueError("Invalid arguments")
        path = {"get_schema": "schema", "execute_query": "query", "explain_query": "explain"}[name]
        result = gateway(path, None if name == "get_schema" else args)
        return {"content": [{"type": "text", "text": json.dumps(result, ensure_ascii=False)}]}
    raise ValueError("Unsupported method")


def main():
    for line in sys.stdin:
        try:
            message = json.loads(line)
            if not isinstance(message, dict) or "id" not in message:
                continue  # notifications, including notifications/initialized
            request_id = message["id"]
            try:
                result = handle(message)
                answer = {"jsonrpc": "2.0", "id": request_id, "result": result}
            except Exception as exc:
                answer = {"jsonrpc": "2.0", "id": request_id, "error": {
                    "code": -32603, "message": str(exc)[:180]}}
            sys.stdout.write(json.dumps(answer, ensure_ascii=False) + "\n")
            sys.stdout.flush()
        except (ValueError, TypeError):
            continue


if __name__ == "__main__":
    main()
