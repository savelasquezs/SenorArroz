#!/usr/bin/env python3
import argparse
import sys
from urllib.parse import quote, urlsplit, urlunsplit

parser = argparse.ArgumentParser(description="Rewrite PostgreSQL connection credentials and optionally host/port.")
parser.add_argument("--base", required=True)
parser.add_argument("--user", required=True)
parser.add_argument("--password", required=True)
parser.add_argument("--host")
parser.add_argument("--port", type=int)
args = parser.parse_args()

parts = urlsplit(args.base)
if parts.scheme not in {"postgres", "postgresql"}:
    sys.exit("Unsupported database URL scheme; expected postgres or postgresql.")
if not parts.hostname or not parts.path:
    sys.exit("Invalid PostgreSQL URL: host and database path are required.")

host = args.host or parts.hostname
port = args.port if args.port is not None else parts.port
if ":" in host and not host.startswith("["):
    host = f"[{host}]"

credentials = f"{quote(args.user, safe='')}:{quote(args.password, safe='')}"
netloc = f"{credentials}@{host}"
if port is not None:
    netloc += f":{port}"

print(urlunsplit((parts.scheme, netloc, parts.path, parts.query, parts.fragment)))
