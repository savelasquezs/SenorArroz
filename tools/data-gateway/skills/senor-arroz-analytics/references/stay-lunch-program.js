                                           

function instant(value                           )         {
  if (value?.startsWith('"')) value = JSON.parse(value);
  if (!value || !/(Z|[+-]\d{2}:\d{2})$/i.test(value)) throw new Error("Timestamp with timezone required");
  const timestamp = Date.parse(value);
  if (!Number.isFinite(timestamp)) throw new Error("Invalid timestamp");
  return timestamp;
}

function id(value                           )         {
  if (!value || !/^[1-9]\d*$/.test(value)) throw new Error("Missing or invalid identity");
  return value;
}

function reviewStayLunches(incidents        , lunches        , asOf        ) {
  if (incidents.truncated || lunches.truncated) throw new Error("Incomplete gateway results; narrow the period or employee filter");
  const cutoff = instant(asOf);
  const byEmployee = new Map                                                         ();
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

                                                  

async function stayLunchSummary(query                                     ,
  options                                                                                                        ) {
  const from = instant(options.from), to = instant(options.to), cutoff = instant(options.asOf);
  if (from >= to || to > cutoff || to - from > 31 * 86400000) throw new Error("Invalid report window; maximum 31 days ending by asOf");
  for (const value of [options.branchId, options.deliverymanId])
    if (value !== undefined && (!Number.isSafeInteger(value) || value <= 0)) throw new Error("Invalid scope identity");
  async function all(request       )                  {
    const rows                 = [], seen = new Set        ();
    let cursor                    ;
    for (let page = 0; page < 4; page++) {
      const result = await query({ ...request, orderBy: "id", descending: false, maxRows: 500,
        filters: [...(request.filters ?? []), ...(cursor ? [{ column: "id", op: "gt"         , value: cursor }] : [])] });
      if (!Array.isArray(result.rows) || !Array.isArray(result.columns) || typeof result.truncated !== "boolean") throw new Error("Invalid gateway response");
      for (const row of result.rows) {
        const rowId = id(row.id);
        if (seen.has(rowId) || (cursor && BigInt(rowId) <= BigInt(cursor))) throw new Error("Invalid pagination");
        seen.add(rowId); cursor = rowId; rows.push(row);
      }
      if (!result.truncated) return { columns: result.columns, rows, truncated: false };
      if (!result.rows.length) throw new Error("Invalid pagination");
    }
    throw new Error("Incomplete report; narrow the period or employee filter");
  }
  const scope                                = [];
  if (options.branchId) scope.push({ column: "branch_id", op: "eq", value: options.branchId });
  if (options.deliverymanId) scope.push({ column: "deliveryman_id", op: "eq", value: options.deliverymanId });
  const alerts = await all({ view: "diagnostics.delivery_tracking_alert",
    columns: ["id", "tenant_id", "branch_id", "deliveryman_id", "incident_id"],
    filters: [{ column: "alert_type", op: "eq", value: "unexpected_stay" },
      { column: "occurred_at", op: "gte", value: options.from }, { column: "occurred_at", op: "lt", value: options.to }, ...scope] });
  const references = new Map                        ();
  for (const alert of alerts.rows) {
    const key = id(alert.incident_id);
    references.set(key, [...(references.get(key) ?? []), alert]);
  }
  const ids = [...references.keys()].sort((a, b) => BigInt(a) < BigInt(b) ? -1 : BigInt(a) > BigInt(b) ? 1 : 0);
  let incidents         = { columns: [], rows: [], truncated: false };
  let lunches         = { columns: [], rows: [], truncated: false };
  let users         = { columns: [], rows: [], truncated: false };
  if (ids.length) {
    incidents = await all({ view: "diagnostics.delivery_tracking_incident",
      columns: ["id", "tenant_id", "branch_id", "deliveryman_id", "incident_type", "started_at", "ended_at", "evidence_complete", "review_status", "final_classification"],
      filters: [{ column: "id", op: "gte", value: ids[0] }, { column: "id", op: "lte", value: ids[ids.length - 1] }] });
    incidents.rows = incidents.rows.filter(row => references.has(id(row.id)));
    if (incidents.rows.length !== ids.length) throw new Error("Missing incident evidence");
    for (const row of incidents.rows) {
      if (row.incident_type !== "stay" || !row.review_status || references.get(row.id ) .some(alert =>
        alert.tenant_id !== row.tenant_id || alert.deliveryman_id !== row.deliveryman_id || alert.branch_id !== row.branch_id))
        throw new Error("Inconsistent incident evidence");
    }
    const first = new Date(Math.min(...incidents.rows.map(row => instant(row.started_at)))).toISOString();
    const last = new Date(Math.max(...incidents.rows.map(row => instant(row.ended_at)))).toISOString();
    const employeeFilter                                = options.deliverymanId
      ? [{ column: "user_id", op: "eq", value: options.deliverymanId }] : [];
    const base = { view: "diagnostics.employee_lunch", columns: ["id", "tenant_id", "user_id", "started_at", "ended_at"] };
    const [closed, open] = await Promise.all([
      all({ ...base, filters: [{ column: "started_at", op: "lt", value: last }, { column: "ended_at", op: "gt", value: first }, ...employeeFilter] }),
      all({ ...base, filters: [{ column: "started_at", op: "lt", value: last }, { column: "ended_at", op: "eq", value: null }, ...employeeFilter] }),
    ]);
    if (closed.rows.some(row => open.rows.some(other => other.id === row.id))) throw new Error("Lunch changed during reads; retry the report");
    lunches = { columns: closed.columns, rows: [...closed.rows, ...open.rows], truncated: false };
    if (options.names) users = await all({ view: "diagnostics.users", columns: ["id", "name"] });
  }
  const analysed = reviewStayLunches(incidents, lunches, options.asOf);
  const categories                         = {};
  const reviewStates                         = {}, classifications                         = {};
  const groups = new Map                                                                                  
                                                                                                                      ();
  for (const row of analysed) {
    categories[row.assessment] = (categories[row.assessment] ?? 0) + 1;
    const state = row.review_status ?? "unknown", classification = row.final_classification ?? "unclassified";
    reviewStates[state] = (reviewStates[state] ?? 0) + 1;
    classifications[classification] = (classifications[classification] ?? 0) + 1;
    const key = row.branch_id + ":" + row.deliveryman_id;
    const group = groups.get(key) ?? { deliveryman_id: row.deliveryman_id, branch_id: row.branch_id,
      ...(options.names ? { name: users.rows.find(user => user.id === row.deliveryman_id)?.name ?? null } : {}),
      incidents: 0, alerts: 0, lunch_seconds: 0, outside_lunch_seconds: 0, pending_review: 0 };
    group.incidents++; group.alerts += references.get(row.incident_id) .length;
    group.lunch_seconds += row.lunch_seconds; group.outside_lunch_seconds += row.outside_lunch_seconds;
    if (row.review_status === "pending") group.pending_review++;
    groups.set(key, group);
  }
  const grouped = [...groups.values()];
  return { complete: true, from: options.from, to: options.to, as_of: options.asOf,
    alerts: alerts.rows.length, incidents: analysed.length, categories, review_states: reviewStates, final_classifications: classifications,
    lunch_seconds: grouped.reduce((sum, row) => sum + row.lunch_seconds, 0),
    outside_lunch_seconds: grouped.reduce((sum, row) => sum + row.outside_lunch_seconds, 0),
    by_employee: grouped.slice(0, 50), employee_details_truncated: grouped.length > 50 };
}

