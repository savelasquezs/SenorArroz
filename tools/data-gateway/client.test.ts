import assert from "node:assert/strict";
import { test } from "node:test";
import { DataGateway, codeModeGateway, type GatewayInvoker } from "./client.ts";

test("rejects unsafe URLs and credentials", () => {
  for (const url of ["http://example.com", "http://localhost.evil", "https://u:p@example.com", "https://example.com?a=x", "https://example.com#x"])
    assert.throws(() => new DataGateway(url, () => "token"));
});

test("Code Mode facade only exposes structured MCP calls", async () => {
  const calls: string[] = [];
  const facade = codeModeGateway((async (tool) => { calls.push(tool); return {}; }) as GatewayInvoker);
  await facade.schema(); await facade.query({ view: "diagnostics.orders_summary" });
  await facade.explain({ view: "diagnostics.orders_summary" });
  assert.deepEqual(calls, ["get_schema", "execute_query", "explain_query"]);
  assert.ok(Object.isFrozen(facade));
});

test("forwards structured requests, disables redirects and refreshes token", async () => {
  const calls: RequestInit[] = [];
  const gateway = new DataGateway("https://example.com", () => "token", (async (url, init) => {
    calls.push(init!);
    assert.match(String(url), /data-gateway/);
    return new Response('{"views":[]}');
  }) as typeof fetch);
  assert.deepEqual(await gateway.schema(), { views: [] });
  await gateway.query({ view: "diagnostics.orders_summary" });
  assert.equal(calls[0].redirect, "error");
  assert.equal(calls[0].method, "GET");
  assert.equal(calls[1].body, '{"view":"diagnostics.orders_summary"}');
});

test("bounds response and hides upstream errors", async () => {
  const oversized = new DataGateway("https://example.com", () => "token", (async () => new Response("x".repeat(1048577))) as typeof fetch);
  await assert.rejects(oversized.schema(), /exceeds limit/);
  const failed = new DataGateway("https://example.com", () => "token", (async () => new Response("private details", { status: 401 })) as typeof fetch);
  await assert.rejects(failed.schema(), /^Error: Gateway HTTP 401$/);
});
