import type { Result } from "../client.ts";

function instant(value: string | null | undefined): number {
  if (value?.startsWith('"')) value = JSON.parse(value);
  if (!value || !/(Z|[+-]\d{2}:\d{2})$/i.test(value)) throw new Error("Timestamp with timezone required");
  const timestamp = Date.parse(value);
  if (!Number.isFinite(timestamp)) throw new Error("Invalid timestamp");
  return timestamp;
}

function id(value: string | null | undefined): string {
  if (!value || !/^[1-9]\d*$/.test(value)) throw new Error("Missing or invalid identity");
  return value;
}

export function reviewStayLunches(incidents: Result, lunches: Result, asOf: string) {
  if (incidents.truncated || lunches.truncated) throw new Error("Incomplete gateway results; narrow the period or employee filter");
  const cutoff = instant(asOf);
  const byEmployee = new Map<string, { start: number; end: number; open: boolean }[]>();
  for (const lunch of lunches.rows) {
    const key = id(lunch.tenant_id) + ":" + id(lunch.user_id);
    const start = instant(lunch.started_at);
    const open = lunch.ended_at === null;
    const end = open ? cutoff : instant(lunch.ended_at);
    if (!open && end <= start) throw new Error("Invalid lunch interval");
    if (start >= cutoff) continue;
    const intervals = byEmployee.get(key) ?? [];
    intervals.push({ start, end: Math.min(end, cutoff), open });
    byEmployee.set(key, intervals);
  }
  return incidents.rows.filter(row => row.incident_type === "stay").map(row => {
    const incidentId = id(row.id);
    const deliverymanId = id(row.deliveryman_id);
    const key = id(row.tenant_id) + ":" + deliverymanId;
    const start = instant(row.started_at);
    const end = instant(row.ended_at);
    if (end <= start || end > cutoff) throw new Error("Invalid or unfinished stay interval");
    const overlaps = (byEmployee.get(key) ?? [])
      .map(lunch => ({ start: Math.max(start, lunch.start), end: Math.min(end, lunch.end), open: lunch.open }))
      .filter(lunch => lunch.start < lunch.end).sort((a, b) => a.start - b.start);
    let covered = 0;
    let until = start;
    for (const overlap of overlaps) {
      covered += Math.max(0, overlap.end - Math.max(until, overlap.start));
      until = Math.max(until, overlap.end);
    }
    const outside = end - start - covered;
    const openLunch = overlaps.some(lunch => lunch.open);
    return {
      incident_id: incidentId, deliveryman_id: deliverymanId, branch_id: row.branch_id,
      lunch_seconds: covered / 1000, outside_lunch_seconds: outside / 1000,
      assessment: row.evidence_complete?.toLowerCase() !== "true" ? "insufficient_evidence"
        : openLunch ? "open_lunch_requires_review"
        : outside === 0 ? "covered_by_recorded_lunch"
        : covered > 0 ? "partial_lunch_requires_review" : "requires_investigation",
      review_status: row.review_status, final_classification: row.final_classification,
    };
  });
}
