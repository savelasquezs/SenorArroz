import argparse
import json
from pathlib import Path
import tiktoken

parser = argparse.ArgumentParser(description="Measure tokenized payloads; does not measure billed model usage.")
parser.add_argument("capture", type=Path)
parser.add_argument("--program", required=True, type=Path)
parser.add_argument("--encoding", default="o200k_base")
parser.add_argument("--bootstrap", type=Path)
parser.add_argument("--bootstrap-output", type=Path)
args = parser.parse_args()
encoding = tiktoken.get_encoding(args.encoding)
capture = json.loads(args.capture.read_text(encoding="utf-8-sig"))
count = lambda value: len(encoding.encode(value, disallowed_special=()))
program_tokens = count(args.program.read_text(encoding="utf-8-sig"))
bootstrap_tokens = (count(args.bootstrap.read_text(encoding="utf-8-sig"))
    + count(args.bootstrap_output.read_text(encoding="utf-8-sig"))) if args.bootstrap and args.bootstrap_output else None
results = []
for case in capture:
    direct = sum(count(block["text"]) for result in case["responses"] for block in result.get("content", []) if block["type"] == "text")
    structured = sum(count(json.dumps(result["structuredContent"], ensure_ascii=False, separators=(",", ":"))) for result in case["responses"])
    summary = count(json.dumps(case["summary"], ensure_ascii=False, separators=(",", ":")))
    arguments = sum(count(json.dumps(result["request"], ensure_ascii=False, separators=(",", ":"))) for result in case["responses"])
    results.append({"case": case["case"], "tool_calls": len(case["responses"]), "elapsed_ms": case["elapsed_ms"],
        "direct_text_tokens": direct, "structured_only_tokens": structured, "summary_tokens": summary,
        "direct_arguments_tokens": arguments, "direct_tool_traffic_tokens": direct + arguments,
        "payload_reduction_percent": round(100 * (1 - summary / direct), 2) if direct else None,
        "execution_program_tokens": program_tokens, "program_plus_summary_tokens": program_tokens + summary,
        "estimated_net_saved_tokens": direct + arguments - program_tokens - summary,
        "bootstrap_tokens": bootstrap_tokens,
        "cold_program_plus_summary_tokens": program_tokens + summary + bootstrap_tokens if bootstrap_tokens is not None else None,
        "estimated_cold_net_saved_tokens": direct + arguments - program_tokens - summary - bootstrap_tokens if bootstrap_tokens is not None else None,
        "summary": case["summary"]})
print(json.dumps({"measurement": "tokenized_content_not_billing", "encoding": args.encoding, "cases": results}, ensure_ascii=False, indent=2))
