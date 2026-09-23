#!/usr/bin/env python3
import argparse

parser = argparse.ArgumentParser(description="Build the Npgsql runtime connection string used by SenorArroz.API.")
parser.add_argument("--host", required=True)
parser.add_argument("--port", required=True, type=int)
parser.add_argument("--database", required=True)
parser.add_argument("--user", required=True)
parser.add_argument("--password", required=True)
args = parser.parse_args()


def quote_value(value: str) -> str:
    if any(ch in value for ch in ';"') or value != value.strip():
        return '"' + value.replace('"', '""') + '"'
    return value

print(
    ";".join(
        [
            f"Host={quote_value(args.host)}",
            f"Port={args.port}",
            f"Database={quote_value(args.database)}",
            f"Username={quote_value(args.user)}",
            f"Password={quote_value(args.password)}",
            "No Reset On Close=false",
        ]
    )
)
