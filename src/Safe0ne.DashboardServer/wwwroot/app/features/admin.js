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
    const qId = 'audit-q';
    const fromId = 'audit-from';
    const toId = 'audit-to';
    const actionId = 'audit-action';
    const takeId = 'audit-take';
    const purgeDaysId = 'audit-purge-days';
    // async after mount
    setTimeout(async () => {
      const root = document.getElementById(containerId);
      const sel = document.getElementById(selectId);
      const qEl = document.getElementById(qId);
      const fromEl = document.getElementById(fromId);
      const toEl = document.getElementById(toId);
      const actionEl = document.getElementById(actionId);
      const takeEl = document.getElementById(takeId);
      const purgeDaysEl = document.getElementById(purgeDaysId);
      if (!root || !sel || !window.Safe0neApi) return;

      root.innerHTML = '<div class="skeleton">Loading audit log…</div>';

      const childrenRes = await Safe0neApi.getChildren({ includeArchived: true });
      if (!childrenRes || !childrenRes.ok){
        root.innerHTML = `<div class="notice notice--danger">Could not load children: ${escapeHtml(childrenRes && childrenRes.error ? childrenRes.error : 'unknown error')}</div>`;
        return;
      }

      const children = childrenRes.data || [];
      sel.innerHTML = children.map(c => `<option value="${escapeHtml(c.id)}">${escapeHtml(c.displayName || c.name || c.id)}</option>`).join('');

      function readIsoFromDatetimeLocal(inputEl){
        try{
          const v = (inputEl && inputEl.value) ? String(inputEl.value) : '';
          if (!v) return null;
          const d = new Date(v);
          if (isNaN(d.getTime())) return null;
          return d.toISOString();
        }catch{ return null; }
      }

      function readFilters(){
        const take = takeEl && takeEl.value ? parseInt(takeEl.value, 10) : 200;
        return {
          take: isFinite(take) ? take : 200,
          q: qEl && qEl.value ? String(qEl.value).trim() : null,
          from: readIsoFromDatetimeLocal(fromEl),
          to: readIsoFromDatetimeLocal(toEl),
          action: actionEl && actionEl.value ? String(actionEl.value).trim() : null
        };
      }

      async function load(){
        const childId = sel.value;
        if (!childId){ root.innerHTML = '<div class="notice">Select a child to view audit entries.</div>'; return; }

        root.innerHTML = '<div class="skeleton">Loading entries…</div>';
        const f = readFilters();
        const res = await Safe0neApi.getChildAuditLocal(childId, {
          take: f.take,
          q: f.q,
          from: f.from,
          to: f.to,
          action: f.action
        });
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

      async function exportJson(){
        const childId = sel.value;
        if (!childId) return;
        const f = readFilters();
        const res = await Safe0neApi.getChildAuditLocal(childId, {
          take: 1000,
          q: f.q,
          from: f.from,
          to: f.to,
          action: f.action
        });
        if (!res || !res.ok){ alert('Export failed'); return; }
        const blob = new Blob([JSON.stringify(res.data || {}, null, 2)], { type: 'application/json' });
        downloadBlob(blob, `safe0ne_audit_${childId}_${nowStamp()}.json`);
      }

      async function exportCsv(){
        const childId = sel.value;
        if (!childId) return;
        const f = readFilters();
        const res = await Safe0neApi.getChildAuditLocal(childId, {
          take: 1000,
          q: f.q,
          from: f.from,
          to: f.to,
          action: f.action
        });
        if (!res || !res.ok){ alert('Export failed'); return; }
        const env = res.data || {};
        const entries = Array.isArray(env.entries) ? env.entries : [];
        const esc = (v)=> '"' + String(v ?? '').replace(/"/g,'""') + '"';
        const lines = [
          ['occurredAtUtc','actor','action','scope','beforeHashSha256','afterHashSha256'].map(esc).join(',')
        ];
        for (const e of entries){
          lines.push([
            e.occurredAtUtc, e.actor, e.action, e.scope, e.beforeHashSha256, e.afterHashSha256
          ].map(esc).join(','));
        }
        const blob = new Blob([lines.join('\n')], { type: 'text/csv' });
        downloadBlob(blob, `safe0ne_audit_${childId}_${nowStamp()}.csv`);
      }

      async function purgeOld(){
        const childId = sel.value;
        if (!childId) return;
        const days = purgeDaysEl && purgeDaysEl.value ? parseInt(purgeDaysEl.value, 10) : 30;
        const safeDays = isFinite(days) ? Math.max(0, Math.min(days, 3650)) : 30;
        if (!confirm(`Purge audit entries older than ${safeDays} days? This cannot be undone.`)) return;
        const res = await Safe0neApi.purgeChildAuditLocal(childId, { olderThanDays: safeDays });
        if (!res || !res.ok){ alert('Purge failed'); return; }
        await load();
        alert(`Purged ${res.data && res.data.deletedCount != null ? res.data.deletedCount : 0} entries.`);
      }

      sel.addEventListener('change', load);
      if (qEl) qEl.addEventListener('input', debounce(load, 300));
      if (fromEl) fromEl.addEventListener('change', load);
      if (toEl) toEl.addEventListener('change', load);
      if (actionEl) actionEl.addEventListener('input', debounce(load, 300));
      if (takeEl) takeEl.addEventListener('change', load);

      // Buttons
      const expJsonBtn = document.getElementById('audit-export-json');
      const expCsvBtn = document.getElementById('audit-export-csv');
      const purgeBtn = document.getElementById('audit-purge');
      if (expJsonBtn) expJsonBtn.addEventListener('click', exportJson);
      if (expCsvBtn) expCsvBtn.addEventListener('click', exportCsv);
      if (purgeBtn) purgeBtn.addEventListener('click', purgeOld);
      await load();
    }, 0);

    return `
      <div class="card">
        <h2>Audit log (policy/settings)</h2>
        <p class="muted">Append-only policy/settings saves captured by the local control plane. Filters are best-effort. Times are displayed as UTC.</p>
        <div class="grid" style="grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 10px; align-items: end;">
          <div>
            <label class="label">Child</label>
            <select id="${selectId}" class="select"><option value="">Loading…</option></select>
          </div>
          <div>
            <label class="label">Search</label>
            <input id="${qId}" class="input" placeholder="actor/action/scope/hash" />
          </div>
          <div>
            <label class="label">Action contains</label>
            <input id="${actionId}" class="input" placeholder="local_policy_put" />
          </div>
          <div>
            <label class="label">From</label>
            <input id="${fromId}" class="input" type="datetime-local" />
          </div>
          <div>
            <label class="label">To</label>
            <input id="${toId}" class="input" type="datetime-local" />
          </div>
          <div>
            <label class="label">Take</label>
            <select id="${takeId}" class="select">
              <option value="200">200</option>
              <option value="500">500</option>
              <option value="1000">1000</option>
            </select>
          </div>
        </div>

        <div class="hr"></div>

        <div class="grid" style="grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 10px; align-items: end;">
          <div>
            <label class="label">Export</label>
            <div style="display:flex; gap:8px;">
              <button id="audit-export-json" class="btn" type="button">JSON</button>
              <button id="audit-export-csv" class="btn" type="button">CSV</button>
            </div>
          </div>
          <div>
            <label class="label">Retention purge (days)</label>
            <input id="${purgeDaysId}" class="input" type="number" min="0" max="3650" value="30" />
          </div>
          <div>
            <label class="label">&nbsp;</label>
            <button id="audit-purge" class="btn btn--danger" type="button">Purge old entries</button>
          </div>
        </div>
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

function downloadBlob(blob, fileName){
  try{
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = fileName || 'download';
    document.body.appendChild(a);
    a.click();
    setTimeout(() => { try{ URL.revokeObjectURL(a.href); a.remove(); }catch{} }, 0);
  }catch{}
}

function debounce(fn, ms){
  let t = null;
  return function(){
    const args = arguments;
    if (t) clearTimeout(t);
    t = setTimeout(() => fn.apply(null, args), ms || 0);
  };
}

  window.Safe0neAdmin = {
    // Standard module contract
    render: renderAdmin,
    renderAdmin,
  };
})();
