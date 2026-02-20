/* Safe0ne DashboardServer UI Feature Module: Support & Safety
   ADR-0003: Modular DashboardServer UI feature modules.

   Scope:
   - Render Support & Safety page (diagnostics exports)
   - Provide requestBundle action used by the UI

   Notes:
   - Uses existing UI endpoints via Safe0neApi (no new endpoints)
   - Keeps changes privacy-first: no extra local storage, no extra telemetry
*/

(function(){
  "use strict";

  function renderSupport(){
    const containerId = "support-diag";

    setTimeout(async () => {
      const root = document.getElementById(containerId);
      if (!root) return;

      root.innerHTML = `<div class="skeleton">Loading devices…</div>`;

      const childrenRes = await Safe0neApi.getChildren();
      if (!childrenRes.ok){
        root.innerHTML = `<div class="notice notice--danger">Could not load children: ${escapeHtml(childrenRes.error || "unknown error")}</div>`;
        return;
      }

      const items = childrenRes.data || [];
      if (items.length === 0){
        root.innerHTML = `<div class="card"><h2>No children yet</h2><p>Add a child profile first.</p></div>`;
        return;
      }

      // Load latest bundle info per child (best effort)
      const cards = await Promise.all(items.map(async c => {
        const id = c.id && c.id.value ? c.id.value : "";
        const name = c.displayName || "Child";

        let infoHtml = `<div class="muted">No diagnostics bundle yet.</div>`;
        const infoRes = await Safe0neApi.getLatestDiagnosticsInfo(id);
        if (infoRes.ok && infoRes.data){
          const d = infoRes.data;
          const sizeKb = Math.round((d.sizeBytes || 0) / 1024);
          infoHtml = `
            <div class="kv"><span>Latest bundle</span><span>${escapeHtml(d.createdAtUtc || "")} (${sizeKb} KB)</span></div>
            <div class="kv"><span>File</span><span>${escapeHtml(d.fileName || "bundle.zip")}</span></div>
            <div class="kv">
              <span>Download</span>
              <a class="btn" href="/api/v1/children/${encodeURIComponent(id)}/diagnostics/bundles/latest" download>Download ZIP</a>
            </div>
          `;
        }

        // History (filesystem-derived). Keep it small.
        let historyHtml = ``;
        const histRes = await Safe0neApi.getDiagnosticsBundles(id, 10);
        if (histRes.ok && Array.isArray(histRes.data) && histRes.data.length){
          const rows = histRes.data.slice(0, 10).map(b => {
            const fn = String(b.fileName || "bundle.zip");
            const sizeKb = Math.round((b.sizeBytes || 0) / 1024);
            const created = String(b.createdAtUtc || "");
            const dl = `/api/v1/children/${encodeURIComponent(id)}/diagnostics/bundles/${encodeURIComponent(fn)}`;
            return `<tr><td>${escapeHtml(created)}</td><td>${escapeHtml(fn)}</td><td>${sizeKb} KB</td><td><a class="btn btn--sm" href="${dl}" download>Download</a></td></tr>`;
          }).join("");
          historyHtml = `
            <div class="hr"></div>
            <h3 style="margin: 10px 0 6px 0;">History</h3>
            <div class="muted" style="margin-bottom:6px;">Last ${Math.min(10, histRes.data.length)} bundles (newest first).</div>
            <div style="overflow:auto;">
              <table class="table">
                <thead><tr><th>Created</th><th>File</th><th>Size</th><th></th></tr></thead>
                <tbody>${rows}</tbody>
              </table>
            </div>
          `;
        }

        return `
          <div class="card">
            <h2>${escapeHtml(name)}</h2>
            <p class="muted">Child ID: ${escapeHtml(id)}</p>
            <div class="kv">
              <span>Request new bundle</span>
              <button class="btn" type="button" data-action="request-bundle" data-child-id="${escapeHtmlAttr(id)}">Request</button>
            </div>
            ${infoHtml}
            ${historyHtml}
          </div>
        `;
      }));

      root.innerHTML = cards.join("")
;

      // Event delegation (avoid inline handlers)
      if (!root.dataset.safe0neSupportWired){
        root.dataset.safe0neSupportWired = "1";
        root.addEventListener("click", (ev) => {
          const btn = ev.target && ev.target.closest ? ev.target.closest('[data-action="request-bundle"]') : null;
          if (!btn) return;
          ev.preventDefault();
          const childId = btn.getAttribute("data-child-id") || "";
          if (!childId) return;
          try { requestBundle(childId); } catch (e) { /* best effort */ }
        });
      }
;
    }, 0);

    // pageHeader is defined in router.js and used as a shared helper across routes.
    return `
      ${pageHeader("Support & Safety",
        "Troubleshooting, safety resources, and privacy-first diagnostics exports.",
        null
      )}
      <div class="card">
        <h2>Diagnostics exports</h2>
        <p>When support asks for a bundle, click Request. The child device will upload a small ZIP (no secrets), then you can download it here.</p>
        <p class="muted">Tip: The child device must be online for the request to complete.</p>
      </div>
      <div class="grid" id="${containerId}"></div>
    `;
  }

  async function requestBundle(childId){
    if (!childId) return;
    try{
      const payload = { type: "diagnostics_bundle", expiresInMinutes: 10 };
      const res = await Safe0neApi.sendChildCommand(childId, payload);
      if (!res.ok){
        alert(`Could not send request: ${res.error || "unknown error"}`);
        return;
      }
      alert("Requested. Leave the child device running for 10–30 seconds, then refresh this page.");
    }catch{
      alert("Network error");
    }
  }

  function escapeHtml(s){
    try{ if (window.Safe0neUi && typeof Safe0neUi.escapeHtml==='function') return Safe0neUi.escapeHtml(s);}catch{}
    return String(s ?? '');
  }


  // For safe attribute embedding inside single quotes.
  function escapeHtmlAttr(s){
    try{ if (window.Safe0neUi && typeof Safe0neUi.escapeHtmlAttr==='function') return Safe0neUi.escapeHtmlAttr(s);}catch{}
    return escapeHtml(s);
  }


  // Global namespace required by router.js and inline onclick handlers.
  window.Safe0neSupport = {
    // Standard module contract: render(route) returns HTML string.
    render: renderSupport,
    renderSupport,
    // Optional module hook invoked by router after mount.
    afterRoute: function(route, query){
      try{
        const childFromLink = (query && query.child ? String(query.child) : '').trim();
        if (!childFromLink) return;
        // Best-effort: allow old/new element ids.
        setTimeout(() => {
          try{
            const sel = document.getElementById('support-child')
                   || document.getElementById('diag-child')
                   || document.getElementById('diagnostics-child');
            if (sel && sel.tagName === 'SELECT'){
              sel.value = childFromLink;
              sel.dispatchEvent(new Event('change'));
            }
          }catch{}
        }, 0);
      }catch{}
    },
    requestBundle,
  };
})();
