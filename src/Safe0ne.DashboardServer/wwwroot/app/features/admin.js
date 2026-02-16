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

  function renderAdmin(){
    return `
      ${pageHeader("Admin / Advanced",
        "Advanced settings and diagnostics. Developer settings are hidden and clearly warned.",
        "Export diagnostics (stub)"
      )}
      <div class="grid">
        ${card("Anti-tamper (planned)", "Tune alerts for protection disable/uninstall attempts. Best effort by platform.", "Best-effort notes")}
        ${card("Diagnostics", "Export logs and a health snapshot for troubleshooting.", "Privacy-first")}
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
