import assert from "node:assert/strict";
import { test } from "node:test";
import { DataGateway, codeModeGateway, type GatewayInvoker } from "./client.ts";
import { reviewStayLunches } from "./helpers/stay-lunch.ts";
import { stayLunchSummary } from "./helpers/stay-lunch-summary.ts";

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

const stay = { id: "9007199254740993", tenant_id: "1", deliveryman_id: "20", branch_id: "1",
  incident_type: "stay", started_at: "2026-11-10T17:00:00Z", ended_at: "2026-11-10T18:00:00Z",
  evidence_complete: "True", review_status: "pending", final_classification: null };
const lunch = { tenant_id: "1", user_id: "20", started_at: "2026-11-10T17:00:00Z", ended_at: "2026-11-10T18:00:00Z" };
const rows = (items: Record<string, string | null>[], truncated = false) => ({ columns: [], rows: items, truncated });
const cutoff = "2026-11-10T19:00:00Z";

test("lunch coverage is temporal evidence and never an automatic misconduct finding", () => {
  const covered = reviewStayLunches(rows([stay]), rows([lunch]), cutoff)[0];
  assert.equal(covered.incident_id, "9007199254740993");
  assert.equal(covered.assessment, "covered_by_recorded_lunch");
  assert.equal(covered.lunch_seconds, 3600);
  assert.equal(covered.outside_lunch_seconds, 0);
  const missing = reviewStayLunches(rows([stay]), rows([]), cutoff)[0];
  assert.equal(missing.assessment, "requires_investigation");
  assert.equal(missing.outside_lunch_seconds, 3600);
  const partial = reviewStayLunches(rows([stay]), rows([{ ...lunch, started_at: "2026-11-10T17:10:00Z", ended_at: "2026-11-10T17:50:00Z" }]), cutoff)[0];
  assert.equal(partial.assessment, "partial_lunch_requires_review");
  assert.equal(partial.outside_lunch_seconds, 1200);
});

test("merges lunch intervals, preserves reviews and excludes other employees and tenants", () => {
  const result = reviewStayLunches(rows([{ ...stay, review_status: "reviewed", final_classification: "authorized_place" }]), rows([
    { ...lunch, ended_at: "2026-11-10T17:40:00Z" },
    { ...lunch, started_at: "2026-11-10T17:20:00Z" }, lunch,
    { ...lunch, user_id: "21" }, { ...lunch, tenant_id: "2" },
  ]), cutoff)[0];
  assert.equal(result.lunch_seconds, 3600);
  assert.equal(result.review_status, "reviewed");
  assert.equal(result.final_classification, "authorized_place");
  assert.equal(reviewStayLunches(rows([stay]), rows([{ ...lunch, user_id: "21" }, { ...lunch, tenant_id: "2" }]), cutoff)[0].lunch_seconds, 0);
});

test("uses half-open intervals, timezone offsets and midnight crossings", () => {
  const encoded = { ...stay, started_at: JSON.stringify(stay.started_at), ended_at: JSON.stringify(stay.ended_at) };
  assert.equal(reviewStayLunches(rows([encoded]), rows([lunch]), cutoff)[0].lunch_seconds, 3600);
  const touching = [{ ...lunch, started_at: "2026-11-10T16:00:00Z", ended_at: stay.started_at },
    { ...lunch, started_at: stay.ended_at, ended_at: "2026-11-10T19:00:00Z" }];
  assert.equal(reviewStayLunches(rows([stay]), rows(touching), cutoff)[0].lunch_seconds, 0);
  const overnight = { ...stay, started_at: "2026-11-10T23:50:00-05:00", ended_at: "2026-11-11T00:10:00-05:00" };
  const crossing = { ...lunch, started_at: "2026-11-11T04:45:00Z", ended_at: "2026-11-11T05:15:00Z" };
  assert.equal(reviewStayLunches(rows([overnight]), rows([crossing]), "2026-11-11T06:00:00Z")[0].assessment, "covered_by_recorded_lunch");
});

test("open lunches and incomplete evidence require review even with full coverage", () => {
  assert.equal(reviewStayLunches(rows([stay]), rows([{ ...lunch, ended_at: null }]), cutoff)[0].assessment, "open_lunch_requires_review");
  assert.equal(reviewStayLunches(rows([{ ...stay, evidence_complete: "False" }]), rows([lunch]), cutoff)[0].assessment, "insufficient_evidence");
  assert.equal(reviewStayLunches(rows([{ ...stay, incident_type: "device_event" }]), rows([]), cutoff).length, 0);
});

test("refuses truncated data, invalid intervals, missing identities and timestamps without timezone", () => {
  assert.throws(() => reviewStayLunches(rows([stay], true), rows([lunch]), cutoff), /Incomplete/);
  assert.throws(() => reviewStayLunches(rows([stay]), rows([], true), cutoff), /Incomplete/);
  for (const invalid of [{ ...stay, ended_at: stay.started_at }, { ...stay, deliveryman_id: null },
    { ...stay, ended_at: "2026-11-10T20:00:00Z" }, { ...stay, started_at: "2026-11-10T17:00:00" }])
    assert.throws(() => reviewStayLunches(rows([invalid]), rows([]), cutoff));
  assert.throws(() => reviewStayLunches(rows([stay]), rows([{ ...lunch, ended_at: "invalid" }]), cutoff));
  assert.throws(() => reviewStayLunches(rows([stay]), rows([{ ...lunch, ended_at: lunch.started_at }]), cutoff));
});

const reportOptions = { from: "2026-11-10T00:00:00-05:00", to: "2026-11-11T00:00:00-05:00", asOf: "2026-11-12T00:00:00Z", names: true };
function reportQuery(change: Record<string, string | null> = {}) {
  const calls: import("./client.ts").Query[] = [];
  const query = async (request: import("./client.ts").Query) => {
    calls.push(request);
    if (request.view.endsWith("delivery_tracking_alert")) return rows([
      { id: "1", tenant_id: "1", branch_id: "1", deliveryman_id: "20", incident_id: stay.id },
      { id: "2", tenant_id: "1", branch_id: "1", deliveryman_id: "20", incident_id: stay.id },
    ]);
    if (request.view.endsWith("delivery_tracking_incident")) return rows([{ ...stay, ...change }]);
    if (request.view.endsWith("employee_lunch")) return rows(request.filters?.some(f => f.column === "ended_at" && f.value === null)
      ? [] : [{ id: "3", ...lunch }]);
    return rows([{ id: "20", name: "Empleado de prueba" }]);
  };
  return { query, calls };
}

test("hybrid report joins alerts, deduplicates incidents and emits only a compact summary", async () => {
  const { query, calls } = reportQuery();
  const summary = await stayLunchSummary(query, reportOptions);
  assert.equal(summary.alerts, 2); assert.equal(summary.incidents, 1);
  assert.equal(summary.lunch_seconds, 3600); assert.equal(summary.outside_lunch_seconds, 0);
  assert.deepEqual(summary.categories, { covered_by_recorded_lunch: 1 });
  assert.equal(summary.by_employee[0].name, "Empleado de prueba");
  assert.equal(summary.by_employee[0].pending_review, 1);
  assert.equal(calls.length, 5);
  assert.ok(calls.every(call => call.columns?.includes("id") && call.maxRows === 500));
  assert.ok(!JSON.stringify(summary).includes("started_at"));
  const lunches = calls.filter(call => call.view.endsWith("employee_lunch"));
  assert.equal(lunches.length, 2);
  assert.ok(lunches.every(call => !call.filters?.some(f => f.column === "branch_id")));
});

test("report preserves administrative classifications and refuses inconsistent evidence", async () => {
  const { query } = reportQuery({ review_status: "reviewed", final_classification: "authorized_place" });
  const summary = await stayLunchSummary(query, reportOptions);
  assert.deepEqual(summary.review_states, { reviewed: 1 });
  assert.deepEqual(summary.final_classifications, { authorized_place: 1 });
  assert.equal(summary.by_employee[0].pending_review, 0);
  for (const mismatch of [{ tenant_id: "2" }, { deliveryman_id: "21" }, { branch_id: "2" }, { incident_type: "device_event" }])
    await assert.rejects(stayLunchSummary(reportQuery(mismatch).query, reportOptions), /Inconsistent/);
});

test("report completes keyset pages and stops on repeated or excessive pages", async () => {
  const fixture = reportQuery();
  let page = 0;
  const summary = await stayLunchSummary(async request => {
    const result = await fixture.query(request);
    if (request.view.endsWith("delivery_tracking_alert")) {
      page++;
      if (page === 1) return rows(result.rows.slice(0, 1), true);
      assert.ok(request.filters?.some(f => f.column === "id" && f.op === "gt" && f.value === "1"));
      return rows(result.rows.slice(1));
    }
    return result;
  }, reportOptions);
  assert.equal(summary.alerts, 2); assert.equal(page, 2);
  await assert.rejects(stayLunchSummary(async () => rows([{ id: "1" }], true), reportOptions), /pagination/);
  let cursor = 0;
  await assert.rejects(stayLunchSummary(async () => rows([{ id: String(++cursor) }], true), reportOptions), /Incomplete/);
  assert.equal(cursor, 4);
});

test("empty and invalid reports avoid unnecessary calls and fail closed", async () => {
  let calls = 0;
  const summary = await stayLunchSummary(async () => { calls++; return rows([]); }, reportOptions);
  assert.equal(summary.complete, true); assert.equal(summary.alerts, 0); assert.equal(calls, 1);
  await assert.rejects(stayLunchSummary(reportQuery().query, { ...reportOptions, to: reportOptions.from }), /Invalid report/);
  await assert.rejects(stayLunchSummary(reportQuery().query, { ...reportOptions, branchId: -1 }), /scope/);
  await assert.rejects(stayLunchSummary(async () => { throw new Error("Gateway query failed"); }, reportOptions), /Gateway/);
});
