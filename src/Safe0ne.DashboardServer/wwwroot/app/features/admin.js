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
    return `
      ${pageHeader("Admin / Advanced",
        "Advanced settings and diagnostics. Developer settings are hidden and clearly warned.",
        "Export diagnostics (stub)"
      )}
      <div class="grid">
        ${card("Anti-tamper (planned)", "Tune alerts for protection disable/uninstall attempts. Best effort by platform.", "Best-effort notes")}
        ${card("Diagnostics", "Export logs and a health snapshot for troubleshooting.", "Privacy-first")}
        ${renderAuditUi()}
        ${card("Developer settings (hidden)", "In production, developer settings require a deliberate unlock and show a warning banner.", "Hidden entry")}
      </div>
    `;
  }

  window.Safe0neAdmin = {
    // Standard module contract
    render: renderAdmin,
    renderAdmin,
  };
})();
