/* Safe0ne DashboardServer UI Feature Module: Admin
   ADR-0003: Modular DashboardServer UI feature modules.
   Purpose: keep router.js thin; isolate Admin / Advanced view rendering.
*/
(function(){
  function escapeHtml(s){
    try{ if (window.Safe0neUi && typeof Safe0neUi.escapeHtml==='function') return Safe0neUi.escapeHtml(s);}catch{}
    return String(s ?? '');
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



  function renderAuditUi(){
    const containerId = 'audit-log';
    const selectId = 'audit-child-select';
    // async after mount
    setTimeout(async () => {
      const root = document.getElementById(containerId);
      const sel = document.getElementById(selectId);
      if (!root || !sel || !window.Safe0neApi) return;

      root.innerHTML = '<div class="skeleton">Loading audit log…</div>';

      const childrenRes = await Safe0neApi.getChildren({ includeArchived: true });
      if (!childrenRes || !childrenRes.ok){
        root.innerHTML = `<div class="notice notice--danger">Could not load children: ${escapeHtml(childrenRes && childrenRes.error ? childrenRes.error : 'unknown error')}</div>`;
        return;
      }

      const children = childrenRes.data || [];
      sel.innerHTML = children.map(c => `<option value="${escapeHtml(c.id)}">${escapeHtml(c.displayName || c.name || c.id)}</option>`).join('');

      async function load(){
        const childId = sel.value;
        if (!childId){ root.innerHTML = '<div class="notice">Select a child to view audit entries.</div>'; return; }

        root.innerHTML = '<div class="skeleton">Loading entries…</div>';
        const res = await Safe0neApi.getChildAuditLocal(childId, { take: 200 });
        if (!res || !res.ok){
          root.innerHTML = `<div class="notice notice--danger">Could not load audit: ${escapeHtml(res && res.error ? res.error : 'unknown error')}</div>`;
          return;
        }
        const payload = res.data || {};
        const entries = payload.entries || [];
        if (!Array.isArray(entries) || entries.length === 0){
          root.innerHTML = '<div class="notice">No audit entries yet.</div>';
          return;
        }

        const rows = entries.map(e => {
          const at = escapeHtml(e.occurredAtUtc || '');
          const actor = escapeHtml(e.actor || '');
          const action = escapeHtml(e.action || '');
          const scope = escapeHtml(e.scope || '');
          const before = escapeHtml((e.beforeHashSha256 || '').slice(0, 12));
          const after = escapeHtml((e.afterHashSha256 || '').slice(0, 12));
          return `<tr><td class="mono">${at}</td><td>${actor}</td><td class="mono">${action}</td><td class="mono">${scope}</td><td class="mono">${before}</td><td class="mono">${after}</td></tr>`;
        }).join('');

        root.innerHTML = `
          <div class="table-wrap">
            <table class="table">
              <thead><tr><th>At (UTC)</th><th>Actor</th><th>Action</th><th>Scope</th><th>Before</th><th>After</th></tr></thead>
              <tbody>${rows}</tbody>
            </table>
          </div>
          <p class="muted">Append-only. Hash fields are a lightweight tamper-evident chain for ordering.</p>
        `;
      }

      sel.addEventListener('change', load);
      await load();
    }, 0);

    return `
      <div class="card">
        <h2>Audit log (policy/settings)</h2>
        <p class="muted">Shows append-only policy/settings saves captured by the local control plane.</p>
        <label class="label">Child</label>
        <select id="${selectId}" class="select"><option value="">Loading…</option></select>
        <div id="${containerId}" style="margin-top:12px"></div>
      </div>
    `;
  }
  function renderAdmin(){

// Wire one-click downloads (no inline handlers)
setTimeout(() => {
  try{
    const root = document.body;
    if (!root || root.dataset.safe0neAdminDiagWired) return;
    root.dataset.safe0neAdminDiagWired = "1";
    root.addEventListener("click", async (ev) => {
      const btn = ev.target && ev.target.closest ? ev.target.closest("button[data-action]") : null;
      if (!btn) return;
      const action = btn.getAttribute("data-action");
      if (action === "download-local-diag"){
        ev.preventDefault();
        await downloadJson("/api/local/diag/bundle", `safe0ne_diag_bundle_${nowStamp()}.json`);
      }
      if (action === "download-ssot-snapshot"){
        ev.preventDefault();
        await downloadJson("/api/local/admin/export/ssot-snapshot", `safe0ne_ssot_snapshot_${nowStamp()}.json`);
      }
    });
  }catch{}
}, 0);
    return `
      ${pageHeader("Admin / Advanced",
        "Advanced settings and diagnostics. Developer settings are hidden and clearly warned.",
        null
      )}
      <div class="grid">
        ${card("Anti-tamper (planned)", "Tune alerts for protection disable/uninstall attempts. Best effort by platform.", "Best-effort notes")}
        <div class="card">
  <h2>Diagnostics exports</h2>
  <p class="muted">Privacy-first downloads for troubleshooting and support. These exports do not include secrets.</p>
  <div class="kv"><span>Local health bundle (JSON)</span><span><button class="btn" type="button" data-action="download-local-diag">Download</button></span></div>
  <div class="kv"><span>SSOT snapshot (redacted JSON)</span><span><button class="btn" type="button" data-action="download-ssot-snapshot">Download</button></span></div>
  <div class="hr"></div>
  <p class="muted">Need a per-child device ZIP bundle? Use <b>Support</b> → Diagnostics exports.</p>
</div>
        ${renderAuditUi()}
        ${card("Developer settings (hidden)", "In production, developer settings require a deliberate unlock and show a warning banner.", "Hidden entry")}
      </div>
    `;
  }


function nowStamp(){
  try{
    const d = new Date();
    const pad = (n)=> String(n).padStart(2,'0');
    return `${d.getUTCFullYear()}${pad(d.getUTCMonth()+1)}${pad(d.getUTCDate())}_${pad(d.getUTCHours())}${pad(d.getUTCMinutes())}${pad(d.getUTCSeconds())}Z`;
  }catch{ return "unknown"; }
}

async function downloadJson(url, fileName){
  try{
    const res = await fetch(url, { method: "GET" });
    if (!res.ok){
      alert(`Download failed (${res.status})`);
      return;
    }
    const blob = await res.blob();
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = fileName || "download.json";
    document.body.appendChild(a);
    a.click();
    setTimeout(() => { try{ URL.revokeObjectURL(a.href); a.remove(); }catch{} }, 0);
  }catch{
    alert("Network error");
  }
}

  window.Safe0neAdmin = {
    // Standard module contract
    render: renderAdmin,
    renderAdmin,
  };
})();
