/* Safe0ne DashboardServer UI Feature Module: Requests (ADR-0003)
   Goals:
   - Keep router.js thin by moving Requests inbox logic here.
   - Support deep links like #/requests?child=<childId>&status=Pending
*/
(function(){
  const NS = (window.Safe0neRequests = window.Safe0neRequests || {});

  function _parseHashQuery(){
    // Supports #/requests?child=<id>&status=<Status>
    const h = String(window.location.hash || "");
    const qIndex = h.indexOf("?");
    if (qIndex < 0) return {};
    const qs = h.substring(qIndex + 1);
    const out = {};
    for (const part of qs.split("&")){
      if (!part) continue;
      const [k,v] = part.split("=", 2);
      if (!k) continue;
      try{
        out[decodeURIComponent(k)] = decodeURIComponent(v || "");
      }catch(_){
        out[k] = v || "";
      }
    }
    return out;
  }

  function _normalizeChildId(id){
    return String(id || "").trim();
  }

  // Renders per-item actions (Approve durations + Deny).
  function _renderRequestActions(r){
    const id = escapeHtml(String(r.id || ""));
    const kind = String(r.type || r.kind || "").trim();
    // MoreTime => ExtraMinutes; UnblockApp/UnblockSite => DurationMinutes
    if (kind === "MoreTime"){
      return `
        <div class="row" style="grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 8px; align-items:center;">
          <button class="btn btn--primary" data-request-id="${id}" data-extra="15">+15 min</button>
          <button class="btn btn--primary" data-request-id="${id}" data-extra="30">+30 min</button>
          <button class="btn btn--primary" data-request-id="${id}" data-extra="60">+60 min</button>
          <button class="btn btn--danger" data-request-id="${id}" data-deny="1">Deny</button>
        </div>
      `;
    }

    // Default to duration-based (UnblockApp/UnblockSite)
    return `
      <div class="row" style="grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 8px; align-items:center;">
        <button class="btn btn--primary" data-request-id="${id}" data-duration="30">Allow 30m</button>
        <button class="btn btn--primary" data-request-id="${id}" data-duration="60">Allow 60m</button>
        <span class="muted" style="display:flex;align-items:center;justify-content:center;">&nbsp;</span>
        <button class="btn btn--danger" data-request-id="${id}" data-deny="1">Deny</button>
      </div>
    `;
  }

  // Produces the whole card list.
  function _renderRequestsList(items, childMap){
    if (!items || !items.length){
      return `<div class="notice">No requests found.</div>`;
    }

    return `
      <div class="card">
        <h3>Requests</h3>
        <div class="stack" style="gap: 12px;">
          ${items.map(r => {
            const childId = String((r.childId && r.childId.value) || r.childId || "");
            const child = childMap.get(String(childId).toLowerCase());
            const childName = child ? (child.displayName || child.name || childId) : (childId || "Unknown child");
            const kind = r.kind || r.type || "Request";
            const status = r.status || "Unknown";
            const createdUtc = r.createdUtc || r.createdAtUtc || r.createdAt || null;
            const reason = r.reason || r.message || r.note || "";
            const target = r.target || r.app || r.domain || "";

            return `
              <div class="card card--flat" style="border:1px solid var(--border);">
                <div class="row" style="grid-template-columns: 1fr auto; gap: 12px; align-items:start;">
                  <div>
                    <div style="font-weight:700">${escapeHtml(String(kind))}</div>
                    <div class="muted">${escapeHtml(String(childName))}</div>
                    ${createdUtc ? `<div class="muted">Created: ${escapeHtml(String(createdUtc))}</div>` : ``}
                    ${target ? `<div style="margin-top:6px" class="muted">${escapeHtml(String(target))}</div>` : ``}
                    ${reason ? `<div style="margin-top:6px">${escapeHtml(String(reason))}</div>` : ``}
                    <div class="muted" style="margin-top:6px">Status: ${escapeHtml(String(status))}</div>
                  </div>
                  <div style="min-width: 220px;">
                    ${String(status).toLowerCase() === "pending" ? _renderRequestActions(r) : `<span class="pill">${escapeHtml(String(status))}</span>`}
                  </div>
                </div>
              </div>
            `;
          }).join("")}
        </div>
      </div>
    `;
  }

  NS.renderRequests = function renderRequests(){
    const containerId = "requests-inbox";

    setTimeout(async () => {
      const root = document.getElementById(containerId);
      if (!root) return;

      root.innerHTML = `<div class="skeleton">Loading requests…</div>`;

      const childrenRes = await Safe0neApi.getChildren();
      if (!childrenRes.ok){
        root.innerHTML = `<div class="notice notice--danger">Could not load children: ${escapeHtml(childrenRes.error || "unknown error")}</div>`;
        return;
      }

      const children = childrenRes.data || [];
      const childMap = new Map(children.map(c => [String((c.id && c.id.value) || "").toLowerCase(), c]));

      // Initial selection from hash query (supports Alerts CTA deep link)
      const q = _parseHashQuery();
      const qChild = _normalizeChildId(q.child || q.childId || "");
      const qStatus = String(q.status || "");

      // UI elements
      root.innerHTML = `
        <div class="card">
          <h2>Inbox</h2>
          <p class="muted">Approve or deny requests. Approvals create time-boxed grants that take effect immediately.</p>
          <div class="row" style="grid-template-columns: 1fr 1fr 160px; gap: 12px; align-items:end;">
            <div class="cell">
              <label class="label">Child</label>
              <select id="req-filter-child">
                <option value="">All children</option>
                ${children.map(c => `<option value="${escapeHtml((c.id && c.id.value) || "")}">${escapeHtml(c.displayName || c.name || (c.id && c.id.value) || "")}</option>`).join("")}
              </select>
            </div>
            <div class="cell">
              <label class="label">Status</label>
              <select id="req-filter-status">
                <option value="">All</option>
                <option value="Pending">Pending</option>
                <option value="Approved">Approved</option>
                <option value="Denied">Denied</option>
              </select>
            </div>
            <div class="cell">
              <button id="req-refresh" class="btn btn--secondary">Refresh</button>
            </div>
          </div>
        </div>
        <div id="req-list" style="margin-top: 12px;"></div>
      `;

      const childSel = document.getElementById("req-filter-child");
      const statusSel = document.getElementById("req-filter-status");
      const refreshBtn = document.getElementById("req-refresh");
      const listEl = document.getElementById("req-list");

      // Apply deep link query values if valid
      if (childSel && qChild){
        childSel.value = qChild;
      }
      if (statusSel && qStatus){
        statusSel.value = qStatus;
      }

      async function load(){
        if (!listEl) return;

        listEl.innerHTML = `<div class="skeleton">Loading…</div>`;
        const childId = childSel ? childSel.value : "";
        const status = statusSel ? statusSel.value : "";

        const res = await Safe0neApi.getRequests(childId, status, 100);
        if (!res.ok){
          listEl.innerHTML = `<div class="notice notice--danger">Could not load requests: ${escapeHtml(res.error || "unknown error")}</div>`;
          return;
        }

        const items = res.data || [];
        listEl.innerHTML = _renderRequestsList(items, childMap);
      }

      // Delegate approve/deny buttons from list
      if (listEl){
        listEl.addEventListener("click", async (ev) => {
          const t = ev.target;
          if (!(t instanceof HTMLElement)) return;

          const requestId = t.getAttribute("data-request-id");
          if (!requestId) return;

          // Approve
          if (t.matches("button[data-extra]")){
            const mins = parseInt(t.getAttribute("data-extra") || "0", 10);
            t.setAttribute("disabled","disabled");
            const dec = await Safe0neApi.decideRequest(requestId, { approve:true, extraMinutes: mins, decidedBy: "Parent" });
            await load();
            if (!dec.ok) alert(`Approve failed: ${dec.error || "unknown error"}`);
            return;
          }

          if (t.matches("button[data-duration]")){
            const mins = parseInt(t.getAttribute("data-duration") || "0", 10);
            t.setAttribute("disabled","disabled");
            const dec = await Safe0neApi.decideRequest(requestId, { approve:true, durationMinutes: mins, decidedBy: "Parent" });
            await load();
            if (!dec.ok) alert(`Approve failed: ${dec.error || "unknown error"}`);
            return;
          }

          // Deny
          if (t.matches("button[data-deny]")){
            t.setAttribute("disabled","disabled");
            const dec = await Safe0neApi.decideRequest(requestId, { approve:false, decidedBy: "Parent" });
            await load();
            if (!dec.ok) alert(`Deny failed: ${dec.error || "unknown error"}`);
            return;
          }
        });
      }

      if (refreshBtn) refreshBtn.addEventListener("click", load);
      if (childSel) childSel.addEventListener("change", load);
      if (statusSel) statusSel.addEventListener("change", load);
      await load();
    }, 0);

    return `
      ${pageHeader("Requests",
        "Review and approve child requests. Approvals create time-boxed grants (extra minutes or temporary unblocks).",
        null
      )}
      <div id="${containerId}"></div>
    `;
  };

  // Standard module contract
  NS.render = NS.renderRequests;
})(); 
