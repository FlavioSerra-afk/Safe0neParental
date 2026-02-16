// Safe0ne UI utilities (idempotent)
(function(){
  if (window.Safe0neUi) return;

  function escapeHtml(str){
    return String(str ?? "")
      .replace(/&/g,'&amp;')
      .replace(/</g,'&lt;')
      .replace(/>/g,'&gt;')
      .replace(/"/g,'&quot;')
      .replace(/'/g,'&#39;');
  }

  function escapeHtmlAttr(str){
    return escapeHtml(str);
  }

  function nowIso(){
    try { return new Date().toISOString(); } catch { return ""; }
  }

  function pageHeader(title, lead, ctaLabel){
    const t = escapeHtml(title || "");
    const l = escapeHtml(lead || "");
    const cta = ctaLabel ? `<button class="btn btn--primary" id="pageCta">${escapeHtml(ctaLabel)}</button>` : "";
    return `
      <header class="page__header">
        <div class="page__headerText">
          <h1 class="page__title">${t}</h1>
          ${lead ? `<p class="page__lead">${l}</p>` : ""}
        </div>
        ${cta ? `<div class="page__headerActions">${cta}</div>` : ""}
      </header>
    `;
  }

  function card(title, body, metaLabel){
    const t = escapeHtml(title || "");
    const m = metaLabel ? `<span class="chip chip--info">${escapeHtml(metaLabel)}</span>` : "";
    return `
      <article class="card">
        <div class="card__header">
          <h3 class="card__title">${t}</h3>
          ${m}
        </div>
        <div class="card__body">${body || ""}</div>
      </article>
    `;
  }

  // Boot self-test badge helper (idempotent).
  function ensureChip(id, text, className){
    const host = document.querySelector('.chips');
    if (!host) return null;
    let el = document.getElementById(id);
    if (!el){
      el = document.createElement('span');
      el.id = id;
      el.className = className || 'chip chip--info';
      el.textContent = text || '';
      host.appendChild(el);
    }
    return el;
  }

  function setSelfTestBadge(selfTest){
    const pass = !!(selfTest && selfTest.pass);
    const el = ensureChip('chip-selftest', 'Self-test: …', pass ? 'chip chip--success' : 'chip chip--danger');
    if (!el) return;

    const label = pass ? 'PASS' : 'FAIL';
    el.textContent = `Self-test: ${label}`;
    el.className = pass ? 'chip chip--success' : 'chip chip--danger';
    try{
      const reasons = (selfTest && Array.isArray(selfTest.results))
        ? selfTest.results.filter(r => !r.pass).map(r => r.name)
        : [];
      el.title = pass
        ? `Self-test passed at ${selfTest?.at || ''}`
        : `Self-test failed: ${reasons.join(', ')}`;
    }catch{
      el.title = pass ? 'Self-test passed' : 'Self-test failed';
    }
  }

  window.Safe0neUi = { escapeHtml, escapeHtmlAttr, nowIso, pageHeader, card, ensureChip, setSelfTestBadge };
})();
