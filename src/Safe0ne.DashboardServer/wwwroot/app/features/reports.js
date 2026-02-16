// Safe0ne DashboardServer UI Feature Module: Reports (ADR-0003)
// Goals:
// - Keep router.js thin by moving the Reports route rendering here.
// - Render Alerts inbox + Recent activity from Local SSOT activity stream.
// Markers for guard tests (do not remove):
// - alerts-inbox
// - SafeOneAlerts.buildAlerts
(function(){
  const NS = (window.Safe0neReports = window.Safe0neReports || {});

  function escapeHtml(s){
    try{ if (window.Safe0neUi && typeof Safe0neUi.escapeHtml==='function') return Safe0neUi.escapeHtml(s);}catch{}
    return String(s ?? '');
  }


  // 16V: Alerts routing config (per child). Best-effort: if profile can't be read, default to inbox enabled.
  const _routingCache = { atMs: 0, map: {} };
  async function getInboxRoutingMap(children){
    const now = Date.now();
    if ((now - _routingCache.atMs) < 10_000 && _routingCache.map) return _routingCache.map;
    const map = {};
    try{
      if (!Safe0neApi || typeof Safe0neApi.getChildProfileLocal !== "function"){
        _routingCache.atMs = now; _routingCache.map = map; return map;
      }
      for (const c of (children || [])){
        const id = c && c.id ? String(c.id) : "";
        if (!id) continue;
        try{
          const res = await Safe0neApi.getChildProfileLocal(id);
          const prof = (res && res.ok) ? (res.data || {}) : {};
          const inboxEnabled = !(prof && prof.policy && prof.policy.alerts && prof.policy.alerts.routing && prof.policy.alerts.routing.inboxEnabled === false);
          map[id] = inboxEnabled;
        }catch{
          map[id] = true;
        }
      }
    }catch{}
    _routingCache.atMs = now;
    _routingCache.map = map;
    return map;
  }


  function pageHeader(title, lead, ctaLabel){
    try{ if (window.Safe0neUi && typeof Safe0neUi.pageHeader==='function') return Safe0neUi.pageHeader(title, lead, ctaLabel);}catch{}
    const t=escapeHtml(title);
    const l=escapeHtml(lead);
    return `<div class="page__header"><div><h1 class="h1">${t}</h1>${lead?`<p class="lead">${l}</p>`:''}</div></div>`;
  }

  function card(title, body, metaLabel){
    try{ if (window.Safe0neUi && typeof Safe0neUi.card==='function') return Safe0neUi.card(title, `<p>${escapeHtml(body)}</p>`, metaLabel);}catch{}
    return `<div class="card"><h2>${escapeHtml(title)}</h2><p>${escapeHtml(body)}</p></div>`;
  }

  function renderReports(){
    const containerId = "alerts-inbox";
    const scheduleId = "reports-schedule";

    // Populate the alerts list after initial mount; this matches existing router behavior.
    setTimeout(async () => {
      const root = document.getElementById(containerId);
      if (!root) return;

      root.innerHTML = `<div class="skeleton">Loading alerts…</div>`;

      // Existing data sources / endpoints only:
      const childrenRes = await Safe0neApi.getChildren();
      if (!childrenRes.ok){
        root.innerHTML = `<div class="notice notice--danger">Could not load children: ${escapeHtml(childrenRes.error || "unknown error")}</div>`;
        return;
      }

      const children = childrenRes.data || [];
      if (children.length === 0){
        root.innerHTML = `<div class="notice">No children configured yet.</div>`;
        return;
      }

      // Best-effort: render the Reports scheduling authoring UI (Phase 16W7).
      try{
        const schedRoot = document.getElementById(scheduleId);
        if (schedRoot) await renderReportsScheduleAuthoring(schedRoot, children);
      }catch{}

      // 16V: Alerts routing config (per child) — best-effort.
      const inboxRouting = await getInboxRoutingMap(children);

      // Patch 15A: pull SSOT-backed ack state (best-effort) so alerts
      // acknowledged on another device/session still hide consistently.
      // We mirror the server state into localStorage using the same ack key
      // scheme used by Safe0neAlerts (childId|title).
      try{
        if (Safe0neApi && typeof Safe0neApi.getChildAlertsStateLocal === "function"){
          for (const c of children){
            const sid = c && c.id ? String(c.id) : "";
            if (!sid) continue;
            const st = await Safe0neApi.getChildAlertsStateLocal(sid);
            if (!st || !st.ok) continue;
            const acked = (st.data && st.data.ackedKeys) ? st.data.ackedKeys : [];
            if (Array.isArray(acked)){
              for (const key of acked){
                try{ localStorage.setItem(String(key), "1"); }catch{}
              }
            }
          }
        }
      }catch{}

      const statuses = [];
      for (const c of children){
        const st = await Safe0neApi.getChildStatus(c.id);
        if (st.ok){
          statuses.push({ child: c, status: st.data || {} });
        } else {
          statuses.push({ child: c, status: { id: c.id, _error: st.error || "Unknown status error" }});
        }
      }

      const now = Date.now();

      // A) Derived alerts from status (offline, depleted, etc.)
      const statusAlerts = (window.Safe0neAlerts && typeof window.Safe0neAlerts.buildAlerts === "function")
        ? window.Safe0neAlerts.buildAlerts(statuses, now)
        : [];

      // B) Activity-backed alerts (geofence transitions, etc.)
      const activityAlerts = await buildActivityBackedAlerts(children);

      const alerts = [...activityAlerts, ...statusAlerts];

      // Render list with ack filtering and actions.
      const showAckId = "alerts-show-acked";
      const showAck = document.getElementById(showAckId);
      const showAcked = !!(showAck && showAck.checked);

      const list = alerts
        .filter(a => {
          const cid = a && a.childId ? String(a.childId) : "";
          if (cid && inboxRouting && inboxRouting[cid] === false) return false;
          if (!window.Safe0neAlerts || typeof window.Safe0neAlerts.isAcked !== "function") return true;
          return showAcked ? true : !window.Safe0neAlerts.isAcked(a);
        })
        .map(a => {
          const sev = String(a.sev || a.severity || "info");
          const sevClass = sev === "danger" || sev === "high" ? "notice--danger"
            : sev === "warning" ? "notice--warning"
            : "notice";
          const actions = (window.Safe0neAlerts && typeof window.Safe0neAlerts.renderAlertActions === "function")
            ? window.Safe0neAlerts.renderAlertActions(a)
            : "";
          const when = a && a.when ? `<div class="muted" style="margin-top:4px">${escapeHtml(String(a.when))}</div>` : "";
          return `
            <div class="notice ${sevClass}" data-child="${escapeHtml(a.childId || "")}" style="margin-bottom:10px">
              <div style="display:flex;justify-content:space-between;gap:10px;align-items:flex-start;flex-wrap:wrap">
                <div style="min-width:0">
                  <div style="font-weight:700">${escapeHtml(a.title || "")}</div>
                  <div style="opacity:.9">${escapeHtml(a.detail || a.message || "")}</div>
                  ${when}
                </div>
                <div class="row" style="gap:8px;justify-content:flex-end">
                  ${actions}
                </div>
              </div>
            </div>
          `;
        })
        .join("");

      root.innerHTML = `
        <div class="row" style="justify-content:space-between;align-items:center;margin-bottom:8px;gap:12px">
          <div style="font-weight:700">Alerts inbox</div>
          <label style="display:flex;align-items:center;gap:8px">
            <input type="checkbox" id="${showAckId}">
            <span>Show acknowledged</span>
          </label>
        </div>
        <div id="alerts-list">${list || `<div class="notice">No alerts right now.</div>`}</div>
      `;

      // Re-render on toggle.
      const chk = document.getElementById(showAckId);
      if (chk){
        chk.addEventListener("change", () => renderReports(), { once: true });
      }

      // Bind ack button handler.
      const listEl = document.getElementById("alerts-list");
      if (listEl && window.Safe0neAlerts && typeof window.Safe0neAlerts.bindAlertsList === "function"){
        window.Safe0neAlerts.bindAlertsList(listEl, () => renderReports());
      }
    }, 0);

    return `
      ${pageHeader("Family Alerts & Reports",
        "See what need attention and reports you can understand quickly."
      )}
      <div id="${containerId}"></div>
      <div class="grid" style="margin-top:12px">
        <div class="card">
          <h2>Reports</h2>
          <p class="muted">Scheduling runs locally and writes digests into the SSOT activity stream.</p>
          <div id="${scheduleId}">${escapeHtml("Loading…")}</div>
        </div>
        ${card("Audit log", "See who changed what and when. Helpful for trust and troubleshooting.", "Audit (later)")}
      </div>
    `;
  }

  NS.renderReports = renderReports;
  // Standard module contract
  NS.render = renderReports;

  // -------- Activity-backed alerts --------

  function fmtWhen(isoUtc){
    try{
      const d = new Date(String(isoUtc || ""));
      if (Number.isNaN(d.getTime())) return "";
      return d.toLocaleString();
    }catch{ return ""; }
  }

  function safeJsonParse(s){
    try{ return JSON.parse(String(s || "")); }catch{ return null; }
  }

  async function buildActivityBackedAlerts(children){
    const api = window.Safe0neApi;
    if (!api || typeof api.getChildActivityLocal !== "function") return [];

    const alerts = [];
    const seen = new Map(); // key -> occurredAtUtc
    const now = Date.now();
    const horizonMs = 24 * 60 * 60 * 1000;

    for (const c of (children || [])){
      const childId = c && c.id ? String(c.id) : "";
      if (!childId) continue;

      let res;
      try{ res = await api.getChildActivityLocal(childId, { take: 200 }); }catch{ res = null; }
      const events = (res && res.ok && Array.isArray(res.data)) ? res.data : [];
      for (const ev of events){
        const kind = String(ev && ev.kind ? ev.kind : "");
        if (kind !== "geofence_enter" && kind !== "geofence_exit") continue;

        const occurredAtUtc = String(ev && ev.occurredAtUtc ? ev.occurredAtUtc : "");
        const t = Date.parse(occurredAtUtc);
        if (!Number.isNaN(t) && (now - t) > horizonMs) continue;

        const details = safeJsonParse(ev && ev.details);
        const gfId = details && (details.geofenceId || details.id) ? String(details.geofenceId || details.id) : "";
        const name = details && details.name ? String(details.name) : (gfId || "Geofence");
        const mode = details && details.mode ? String(details.mode) : "inside";

        // Collapse to the latest transition per (child, geofence, kind) to avoid spamming the inbox.
        const dedupeKey = `${childId}|${gfId}|${kind}`;
        const prev = seen.get(dedupeKey);
        if (prev && occurredAtUtc && Date.parse(prev) >= t) continue;
        seen.set(dedupeKey, occurredAtUtc);

        const childName = (c && c.displayName) ? String(c.displayName) : "Child";
        const entered = kind === "geofence_enter";
        const verb = entered ? "Entered" : "Left";
        const title = `Geofence: ${verb} ${name}`;

        alerts.push({
          sev: "info",
          childId,
          childName,
          title,
          detail: `${childName} ${entered ? "entered" : "left"} “${name}”. Rule: ${mode === "outside" ? "Outside" : "Inside"}.`,
          when: occurredAtUtc ? fmtWhen(occurredAtUtc) : "",
          occurredAtUtc: occurredAtUtc || ""
        });
      }
    }

    // Newest first (activity-backed only)
    alerts.sort((a,b) => Date.parse(String(b.occurredAtUtc || "")) - Date.parse(String(a.occurredAtUtc || "")));
    return alerts;
  }

  // -------- Reports scheduling authoring (Phase 16W7 stub) --------
  async function renderReportsScheduleAuthoring(root, children){
    if (!root) return;

    const freqId = "rep-freq";
    const timeId = "rep-time";
    const dayId = "rep-day";
    const saveId = "rep-save";
    const runId = "rep-run";
    const statusId = "rep-status";
    const recentId = "rep-recent";

    // Load current settings from first child (best-effort); apply-to-all on save.
    let current = { frequency: "off", timeLocal: "18:00", weekday: "sun" };
    try{
      const first = children && children[0] ? children[0] : null;
      if (first && first.id && Safe0neApi && typeof Safe0neApi.getChildReportsScheduleLocal === "function"){
        const res = await Safe0neApi.getChildReportsScheduleLocal(first.id);
        const sch = (res && res.ok && res.data && res.data.schedule) ? res.data.schedule : null;
        const dig = sch && sch.digest ? sch.digest : null;
        if (dig){
          current.frequency = String(dig.frequency || current.frequency);
          current.timeLocal = String(dig.timeLocal || current.timeLocal);
          current.weekday = String(dig.weekday || current.weekday);
        }
      }
    }catch{}

    root.innerHTML = `
      <div class="row" style="gap:10px;flex-wrap:wrap;align-items:flex-end">
        <label style="display:flex;flex-direction:column;gap:6px;min-width:140px">
          <span class="muted">Frequency</span>
          <select id="${freqId}" class="input">
            <option value="off">Off</option>
            <option value="daily">Daily</option>
            <option value="weekly">Weekly</option>
          </select>
        </label>
        <label style="display:flex;flex-direction:column;gap:6px;min-width:140px">
          <span class="muted">Time</span>
          <input id="${timeId}" class="input" type="time" value="${escapeHtml(current.timeLocal)}" />
        </label>
        <label style="display:flex;flex-direction:column;gap:6px;min-width:140px">
          <span class="muted">Weekday</span>
          <select id="${dayId}" class="input">
            <option value="mon">Mon</option>
            <option value="tue">Tue</option>
            <option value="wed">Wed</option>
            <option value="thu">Thu</option>
            <option value="fri">Fri</option>
            <option value="sat">Sat</option>
            <option value="sun">Sun</option>
          </select>
        </label>
        <button id="${saveId}" class="btn btn--primary" title="Save schedule to all children (SSOT)">💾 Save</button>
        <button id="${runId}" class="btn" title="Run the report digest now (creates an SSOT activity item)">▶ Run now</button>
      </div>
      <div id="${statusId}" class="muted" style="margin-top:8px"></div>
      <div style="margin-top:10px">
        <div style="font-weight:700;margin-bottom:6px">Recent reports</div>
        <div id="${recentId}" class="muted">Loading…</div>
      </div>
    `;

    // Init current values
    try{
      const freqEl = document.getElementById(freqId);
      const dayEl = document.getElementById(dayId);
      if (freqEl) freqEl.value = current.frequency;
      if (dayEl) dayEl.value = current.weekday;
    }catch{}

    // Hide weekday when not weekly
    const setDayVisibility = () => {
      const freqEl = document.getElementById(freqId);
      const dayWrap = document.getElementById(dayId)?.closest('label');
      if (!freqEl || !dayWrap) return;
      dayWrap.style.display = (freqEl.value === 'weekly') ? '' : 'none';
    };
    setDayVisibility();
    document.getElementById(freqId)?.addEventListener('change', setDayVisibility);

    // Event delegation safe enough here: root is stable and rebuilt per render.
    root.addEventListener('click', async (ev) => {
      const t = ev && ev.target ? ev.target : null;
      if (!t || !t.id) return;
      if (t.id !== saveId && t.id !== runId) return;

      const status = document.getElementById(statusId);
      if (status) status.textContent = "Saving…";

      const freq = String(document.getElementById(freqId)?.value || 'off');
      const timeLocal = String(document.getElementById(timeId)?.value || '18:00');
      const weekday = String(document.getElementById(dayId)?.value || 'sun');

      const schedulePatch = { enabled: (freq !== 'off'), digest: { frequency: freq, timeLocal, weekday } };

      if (t.id === runId){
        let ran = 0, rfail = 0;
        for (const c of (children || [])){
          const cid = c && c.id ? String(c.id) : '';
          if (!cid) continue;
          try{
            if (!Safe0neApi || typeof Safe0neApi.runChildReportsNowLocal !== 'function') throw new Error('Local reports run-now API not available');
            const r = await Safe0neApi.runChildReportsNowLocal(cid);
            if (r && r.ok) ran++; else rfail++;
          }catch{ rfail++; }
        }
        if (status) status.textContent = (rfail === 0)
          ? `Triggered report digest for ${ran} child(ren).`
          : `Triggered for ${ran}, failed for ${rfail}.`;

        // Refresh recent list.
        try{ await renderRecentReportsList(document.getElementById(recentId), children); }catch{}
        return;
      }

      let ok = 0, fail = 0;
      for (const c of (children || [])){
        const cid = c && c.id ? String(c.id) : '';
        if (!cid) continue;
        try{
          if (!Safe0neApi || typeof Safe0neApi.putChildReportsScheduleLocal !== 'function') throw new Error('Local reports schedule API not available');
          const r = await Safe0neApi.putChildReportsScheduleLocal(cid, schedulePatch);
          if (r && r.ok) ok++; else fail++;
        }catch{ fail++; }
      }
      if (status) status.textContent = (fail === 0)
        ? `Saved schedule to SSOT for ${ok} child(ren).`
        : `Saved for ${ok}, failed for ${fail}. (If server was down, this was not persisted.)`;
    });
  }


  async function renderRecentReportsList(root, children){
    if (!root) return;
    if (!Safe0neApi || typeof Safe0neApi.getChildActivityLocal !== 'function'){
      root.textContent = 'Activity API unavailable.';
      return;
    }
    const from = new Date(Date.now() - 7*24*60*60*1000).toISOString();
    const items = [];
    for (const c of (children || [])){
      const cid = c && c.id ? String(c.id) : '';
      if (!cid) continue;
      try{
        const res = await Safe0neApi.getChildActivityLocal(cid, { from, take: 200 });
        const arr = (res && res.ok && Array.isArray(res.data)) ? res.data : [];
        for (const ev of arr){
          if (!ev || String(ev.kind || '') !== 'report_digest') continue;
          const when = String(ev.occurredAtUtc || '');
          let details = null;
          try{ details = ev.details ? JSON.parse(String(ev.details)) : null; }catch{}
          const summary = details && details.summary ? String(details.summary) : 'Digest generated.';
          items.push({ childName: c.displayName || 'Child', when, summary });
        }
      }catch{}
    }
    items.sort((a,b) => Date.parse(String(b.when||'')) - Date.parse(String(a.when||'')));
    if (items.length === 0){
      root.textContent = 'No reports generated yet.';
      return;
    }
    root.innerHTML = items.slice(0, 12).map(it => {
      const w = it.when ? fmtWhen(it.when) : '';
      return `<div style="margin-bottom:6px"><div style="font-weight:600">${escapeHtml(it.childName)} — ${escapeHtml(w)}</div><div class="muted">${escapeHtml(it.summary)}</div></div>`;
    }).join('');
  }
})();
