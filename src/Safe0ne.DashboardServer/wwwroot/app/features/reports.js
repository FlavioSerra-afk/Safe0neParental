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


  
      // EPIC-WEB-FILTER-CIRCUMVENTION: show VPN/proxy/DNS/hosts-write-failure signals (best-effort).
      try{
        const cRoot = document.getElementById(circId);
        if (cRoot) await renderCircumventionDigest(cRoot, children);
      }catch{}

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
    const activityId = "reports-activity";
    const screenId = "reports-screen-time";
    const webBlocksId = "reports-web-blocks";
    const circId = "reports-circumvention";

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

      // Best-effort: render recent Activity feed (SSOT-backed). Kept separate from alerts list so
      // the inbox remains actionable while activity remains informative.
      try{
        const actRoot = document.getElementById(activityId);
        if (actRoot) await renderActivityFeed(actRoot, children);
      }catch{}

      // EPIC-SCREEN-TIME: render a quick screen time digest panel from agent status rollups.
      try{
        const stRoot = document.getElementById(screenId);
        if (stRoot) await renderScreenTimeDigest(stRoot, children);
      }catch{}

      // EPIC-ENFORCE-WEB: show web-block events + unblock-site request loop is observable.
      try{
        const wbRoot = document.getElementById(webBlocksId);
        if (wbRoot) await renderWebBlocksDigest(wbRoot, children);
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
        <div class="card">
          <h2>Recent activity</h2>
          <p class="muted">SSOT-backed events from kid devices (best-effort). Use this to spot screen-time, geofence, tamper, and request activity at a glance.</p>
          <div id="${activityId}">${escapeHtml("Loading…")}</div>
        </div>
        <div class="card">
          <h2>Screen time</h2>
          <p class="muted">Budget status reported by the kid agent (privacy-first rollup). If a limit is reached, a More Time request is auto-created.</p>
          <div id="${screenId}">${escapeHtml("Loading…")}</div>
        </div>
        <div class="card">
          <h2>Web blocks</h2>
          <p class="muted">When a website is blocked on a kid device, we emit a SSOT activity marker and auto-create an Unblock Site request (best-effort).</p>
          <div id="${webBlocksId}">${escapeHtml("Loading…")}</div>
        </div>
        <div class="card">
          <h2>Circumvention signals</h2>
          <p class="muted">Best-effort integrity signals (VPN/proxy/public DNS/hosts-write failures). Edge-triggered activity is emitted on transitions.</p>
          <div id="${circId}">${escapeHtml("Loading…")}</div>
        </div>
        ${card("Audit log", "See who changed what and when. Helpful for trust and troubleshooting.", "Audit (later)")}
      </div>
    `;
  }

  NS.renderReports = renderReports;
  // Standard module contract

  function fmtMins(m){
    const n = Number(m);
    if (!isFinite(n) || n < 0) return "—";
    const h = Math.floor(n / 60);
    const mm = Math.floor(n % 60);
    return h > 0 ? `${h}h ${mm}m` : `${mm}m`;
  }

  async function renderScreenTimeDigest(root, children){
    if (!root) return;
    root.innerHTML = `<div class="skeleton">Loading screen time…</div>`;

    const rows = [];
    for (const c of (children || [])){
      const id = c && c.id ? String(c.id) : "";
      if (!id) continue;
      const st = await Safe0neApi.getChildStatus(id);
      if (st && st.ok){
        rows.push({ child: c, status: st.data || {} });
      }else{
        rows.push({ child: c, status: { _error: (st && st.error) ? st.error : "Unknown" } });
      }
    }

    const html = rows.map(r => {
      const name = escapeHtml(r.child && r.child.name ? r.child.name : "Child");
      const s = r.status || {};
      if (s._error){
        return `<tr><td>${name}</td><td colspan="4" class="muted">${escapeHtml(String(s._error))}</td></tr>`;
      }

      const limit = s.screenTimeLimitMinutes;
      const used = s.screenTimeUsedMinutes;
      const rem = s.screenTimeRemainingMinutes;
      const depleted = !!s.screenTimeBudgetDepleted;
      const badge = depleted ? `<span class="badge badge--danger">Depleted</span>` : `<span class="badge">OK</span>`;
      const mode = escapeHtml(String(s.effectiveMode || ""));
      return `<tr>
        <td>${name}</td>
        <td>${badge}</td>
        <td>${fmtMins(used)}</td>
        <td>${fmtMins(rem)}</td>
        <td class="muted">${fmtMins(limit)} · ${mode}</td>
      </tr>`;
    }).join("");

    root.innerHTML = `
      <div style="overflow:auto">
        <table class="table" style="min-width:640px">
          <thead><tr>
            <th>Child</th><th>Status</th><th>Used</th><th>Remaining</th><th>Limit · Mode</th>
          </tr></thead>
          <tbody>${html || `<tr><td colspan="5" class="muted">No data.</td></tr>`}</tbody>
        </table>
      </div>
      <div class="muted" style="margin-top:8px">If a child hits their daily limit, the kid device shows a blocked screen and can request more time. Requests appear in the Requests inbox.</div>
    `;
  }

  async function renderWebBlocksDigest(root, children){
    if (!root) return;
    root.innerHTML = `<div class="skeleton">Loading web blocks…</div>`;

    const sinceMs = Date.now() - (7 * 24 * 60 * 60 * 1000);
    const fromIso = new Date(sinceMs).toISOString();

    const rows = [];
    for (const c of (children || [])){
      const id = c && c.id ? String(c.id) : "";
      if (!id) continue;
      const res = await Safe0neApi.getChildActivityLocal(id, { from: fromIso, take: 200 });
      if (!res || !res.ok) {
        rows.push({ child: c, error: res && res.error ? res.error : "Unknown" });
        continue;
      }
      const arr = Array.isArray(res.data) ? res.data : (res.data && Array.isArray(res.data.items) ? res.data.items : []);
      const web = (arr || []).filter(x => String(x && x.kind || "").toLowerCase() === "web_blocked");
      const byDomain = {};
      for (const ev of web){
        try{
          const d = (ev && ev.details) ? JSON.parse(ev.details) : {};
          const dom = String(d.domain || "").trim().toLowerCase();
          if (!dom) continue;
          byDomain[dom] = (byDomain[dom] || 0) + (Number(d.count) || 1);
        }catch{}
      }
      const domains = Object.keys(byDomain).sort((a,b) => (byDomain[b]||0) - (byDomain[a]||0));
      const top = domains.length ? `${domains[0]} (${byDomain[domains[0]]||0})` : "—";
      const total = Object.values(byDomain).reduce((a,b) => a + (Number(b)||0), 0);
      rows.push({ child: c, total, top });
    }

    const html = rows.map(r => {
      const name = escapeHtml(r.child && r.child.name ? r.child.name : "Child");
      if (r.error){
        return `<tr><td>${name}</td><td colspan="2" class="muted">${escapeHtml(String(r.error))}</td></tr>`;
      }
      const total = Number(r.total)||0;
      const badge = total > 0 ? `<span class="badge badge--warning">${total}</span>` : `<span class="badge">0</span>`;
      return `<tr><td>${name}</td><td>${badge}</td><td class="muted">${escapeHtml(String(r.top||"—"))}</td></tr>`;
    }).join("");

    root.innerHTML = `
      <div style="overflow:auto">
        <table class="table" style="min-width:520px">
          <thead><tr><th>Child</th><th>Blocks (7d)</th><th>Top domain</th></tr></thead>
          <tbody>${html || `<tr><td colspan="3" class="muted">No data.</td></tr>`}</tbody>
        </table>
      </div>
      <div class="muted" style="margin-top:8px">A blocked site triggers an Unblock Site request on the kid device (best-effort). Review in the Requests inbox.</div>
    `;
  }


  async function renderCircumventionDigest(root, children){
    if (!root) return;
    root.innerHTML = `<div class="skeleton">Loading circumvention…</div>`;

    const rows = [];
    for (const c of (children || [])){
      const id = c && c.id ? String(c.id) : "";
      if (!id) continue;
      let st = null;
      try{
        const res = await Safe0neApi.getChildStatus(id);
        st = (res && res.ok) ? (res.data || null) : null;
      }catch{}

      const circ = st && st.circumvention ? st.circumvention : null;
      const vpn = !!(circ && circ.vpnSuspected);
      const proxy = !!(circ && circ.proxyEnabled);
      const dns = !!(circ && circ.publicDnsDetected);
      const hosts = !!(circ && circ.hostsWriteFailed);
      const any = vpn || proxy || dns || hosts;

      rows.push({
        child: c.displayName || c.name || "Child",
        vpn, proxy, dns, hosts, any,
        lastSeenUtc: st ? (st.lastSeenUtc || st.lastSeenAtUtc || "") : ""
      });
    }

    if (rows.length === 0){
      root.textContent = "No children.";
      return;
    }

    const head = `<div class="muted" style="margin-bottom:8px">Signals are best-effort and may produce false positives. We emit activity on transitions to reduce noise.</div>`;

    const table = `
      <table class="table">
        <thead><tr>
          <th>Child</th>
          <th>VPN</th>
          <th>Proxy</th>
          <th>Public DNS</th>
          <th>Hosts write</th>
          <th>Last seen</th>
        </tr></thead>
        <tbody>
          ${rows.map(r => {
            const pill = (b) => b ? `<span class="pill pill--danger">Yes</span>` : `<span class="pill">No</span>`;
            const ls = r.lastSeenUtc ? fmtWhen(r.lastSeenUtc) : "—";
            return `<tr>
              <td style="font-weight:600">${escapeHtml(r.child)}</td>
              <td>${pill(r.vpn)}</td>
              <td>${pill(r.proxy)}</td>
              <td>${pill(r.dns)}</td>
              <td>${pill(r.hosts)}</td>
              <td class="muted">${escapeHtml(ls)}</td>
            </tr>`;
          }).join('')}
        </tbody>
      </table>
    `;

    root.innerHTML = head + table;
  }


  NS.render = renderReports;

  // -------- Activity-backed alerts --------

  // -------- Activity feed (reports surface) --------

  function activityBadge(kind){
    const k = String(kind || "");
    const cls = (k.includes("failed") || k.includes("tamper") || k.includes("circumvent")) ? "pill pill--danger"
      : (k.includes("warning") || k.includes("depleted") || k.includes("blocked") || k.includes("geofence")) ? "pill pill--warning"
      : "pill";
    return `<span class="${cls}">${escapeHtml(k || "event")}</span>`;
  }

  async function renderActivityFeed(rootEl, children){
    // A small, fast, cross-child feed. Avoid heavy pagination; the deeper exports live per child.
    const takePerChild = 12;
    const sinceMs = Date.now() - (24 * 60 * 60 * 1000);
    const fromIso = new Date(sinceMs).toISOString();

    // UI state (in-memory only; SSOT purity).
    const filterId = "reports-activity-filter";
    const showId = "reports-activity-show";
    const exportId = "reports-activity-export";

    rootEl.innerHTML = `
      <div class="row" style="justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap">
        <label style="display:flex;align-items:center;gap:8px">
          <span class="muted">Filter</span>
          <input id="${filterId}" class="input" placeholder="screen_time, geofence, request…" style="min-width:220px">
        </label>
        <div class="row" style="gap:8px;justify-content:flex-end;flex-wrap:wrap">
          <button class="btn" id="${showId}">Refresh</button>
          <button class="btn btn--ghost" id="${exportId}">Export…</button>
        </div>
      </div>
      <div id="reports-activity-list" style="margin-top:10px">
        <div class="skeleton">Loading activity…</div>
      </div>
    `;

    async function loadAndRender(){
      const listEl = rootEl.querySelector("#reports-activity-list");
      if (!listEl) return;
      listEl.innerHTML = `<div class="skeleton">Loading activity…</div>`;

      const filt = (rootEl.querySelector(`#${filterId}`)?.value || "").trim().toLowerCase();

      const items = [];
      for (const c of (children || [])){
        const cid = c && c.id ? String(c.id) : "";
        if (!cid) continue;
        const res = await Safe0neApi.getChildActivityLocal(cid, { from: fromIso, take: takePerChild });
        if (!res || !res.ok) continue;
        const arr = Array.isArray(res.data) ? res.data : (res.data && Array.isArray(res.data.items) ? res.data.items : []);
        for (const it of (arr || [])){
          items.push({ child: c, item: it });
        }
      }

      // Normalize timestamps and sort desc.
      const norm = items.map(x => {
        const it = x.item || {};
        const ts = it.atUtc || it.whenUtc || it.tsUtc || it.timestampUtc || it.createdUtc || it.timeUtc || it.at || it.when;
        const ms = ts ? Date.parse(String(ts)) : 0;
        return {
          child: x.child,
          kind: String(it.kind || it.type || it.event || it.name || ""),
          title: String(it.title || ""),
          detail: String(it.detail || it.message || it.reason || ""),
          at: ts ? String(ts) : "",
          atMs: isFinite(ms) ? ms : 0,
          raw: it
        };
      }).sort((a,b)=> (b.atMs||0) - (a.atMs||0));

      const filtered = filt
        ? norm.filter(x => (x.kind + " " + x.title + " " + x.detail).toLowerCase().includes(filt))
        : norm;

      const top = filtered.slice(0, 40);
      if (!top.length){
        listEl.innerHTML = `<div class="notice">No activity in the last 24h.</div>`;
        return;
      }

      listEl.innerHTML = top.map(x => {
        const childName = x.child && (x.child.name || x.child.displayName) ? String(x.child.name || x.child.displayName) : "Child";
        const when = x.at ? fmtWhen(x.at) : "";
        const line = x.title || x.detail || "";
        return `
          <div class="row" style="justify-content:space-between;align-items:flex-start;gap:10px;margin-bottom:8px;flex-wrap:wrap">
            <div style="min-width:0">
              <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap">
                ${activityBadge(x.kind)}
                <span style="font-weight:700">${escapeHtml(childName)}</span>
                ${when ? `<span class="muted">${escapeHtml(when)}</span>` : ``}
              </div>
              <div style="opacity:.95;margin-top:2px;word-break:break-word">
                ${escapeHtml(line || x.kind || "event")}
              </div>
            </div>
          </div>
        `;
      }).join("");
    }

    // Bind handlers.
    const btn = rootEl.querySelector(`#${showId}`);
    if (btn) btn.addEventListener("click", () => loadAndRender());
    const inp = rootEl.querySelector(`#${filterId}`);
    if (inp) inp.addEventListener("keydown", (e)=>{ if (e.key === "Enter") loadAndRender(); });
    const exp = rootEl.querySelector(`#${exportId}`);
    if (exp) exp.addEventListener("click", async () => {
      // Simple picker: export the first child if only one, else ask via prompt.
      let targetId = "";
      if ((children || []).length === 1){
        targetId = String(children[0].id || "");
      } else {
        const options = (children || []).map(c => `${c.name || c.displayName || c.id} (${c.id})`).join("\n");
        const pick = prompt(`Export activity for which child? Paste an id:\n\n${options}`);
        targetId = String(pick || "").trim();
      }
      if (!targetId) return;
      try{
        const res = await Safe0neApi.exportChildActivityLocal(targetId);
        if (!res || !res.ok){
          alert(`Export failed: ${res && res.error ? res.error : "unknown error"}`);
          return;
        }
        // Best-effort download; keep zero-dependency.
        const blob = new Blob([JSON.stringify(res.data || {}, null, 2)], { type: "application/json" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = `activity_${targetId}_${new Date().toISOString().slice(0,10)}.json`;
        document.body.appendChild(a);
        a.click();
        setTimeout(()=>{ try{ URL.revokeObjectURL(url);}catch{} try{ a.remove(); }catch{} }, 0);
      }catch(err){
        alert(`Export failed: ${err && err.message ? err.message : String(err || "error")}`);
      }
    });

    await loadAndRender();
  }

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
        if (kind !== "geofence_enter" && kind !== "geofence_exit" && kind !== "circumvention_detected" && kind !== "tamper_detected") continue;

        const occurredAtUtc = String(ev && ev.occurredAtUtc ? ev.occurredAtUtc : "");
        const t = Date.parse(occurredAtUtc);
        if (!Number.isNaN(t) && (now - t) > horizonMs) continue;

        const details = safeJsonParse(ev && ev.details);

        const childName = (c && c.displayName) ? String(c.displayName) : "Child";

        if (kind === "geofence_enter" || kind === "geofence_exit"){
          const gfId = details && (details.geofenceId || details.id) ? String(details.geofenceId || details.id) : "";
          const name = details && details.name ? String(details.name) : (gfId || "Geofence");
          const mode = details && details.mode ? String(details.mode) : "inside";

          // Collapse to the latest transition per (child, geofence, kind) to avoid spamming the inbox.
          const dedupeKey = `${childId}|${gfId}|${kind}`;
          const prev = seen.get(dedupeKey);
          if (prev && occurredAtUtc && Date.parse(prev) >= t) continue;
          seen.set(dedupeKey, occurredAtUtc);

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
          continue;
        }

        if (kind === "circumvention_detected"){
          // De-dupe: only latest per child within horizon
          const dedupeKey = `${childId}|circumvention_detected`;
          const prev = seen.get(dedupeKey);
          if (prev && occurredAtUtc && Date.parse(prev) >= t) continue;
          seen.set(dedupeKey, occurredAtUtc);

          const parts = [];
          try{
            if (details && details.vpnSuspected) parts.push("VPN suspected");
            if (details && details.proxyEnabled) parts.push("Proxy enabled");
            if (details && details.publicDnsDetected) parts.push("Public DNS detected");
            if (details && details.hostsWriteFailed) parts.push("Hosts protection failed");
          }catch{}
          const title = "Circumvention detected";
          const msg = parts.length ? parts.join(", ") : "Network circumvention signals were detected.";

          alerts.push({
            sev: "warning",
            childId,
            childName,
            title,
            detail: `${childName}: ${msg}`,
            when: occurredAtUtc ? fmtWhen(occurredAtUtc) : "",
            occurredAtUtc: occurredAtUtc || ""
          });
          continue;
        }

        if (kind === "tamper_detected"){
          const dedupeKey = `${childId}|tamper_detected`;
          const prev = seen.get(dedupeKey);
          if (prev && occurredAtUtc && Date.parse(prev) >= t) continue;
          seen.set(dedupeKey, occurredAtUtc);

          const parts = [];
          try{
            if (details && details.notRunningElevated) parts.push("Agent not elevated");
            if (details && details.enforcementError) parts.push("Enforcement error");
          }catch{}
          const title = "Tamper / integrity issue";
          let msg = parts.length ? parts.join(", ") : "Integrity signals were detected.";
          try{
            if (details && details.lastError) msg += `. Last error: ${String(details.lastError)}`;
          }catch{}

          alerts.push({
            sev: "danger",
            childId,
            childName,
            title,
            detail: `${childName}: ${msg}`,
            when: occurredAtUtc ? fmtWhen(occurredAtUtc) : "",
            occurredAtUtc: occurredAtUtc || ""
          });
          continue;
        }
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
