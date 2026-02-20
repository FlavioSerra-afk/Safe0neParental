/*
  Safe0ne Dev Tools feature module

  Owns:
    - /devtools route UI rendering
    - page-specific event wiring

  Depends on router-provided globals:
    - isDevToolsUnlocked(): boolean
    - setDevToolsUnlocked(boolean): void
    - syncDevToolsNavVisibility(): void (optional)
    - getDevToolsStorageKey(): string (optional)

  IMPORTANT BEHAVIOR (must preserve):
    - DevTools rail item hidden unless unlocked.
    - Unlock via 7x taps in 5s on rail logo.
    - Toggle on page is for LOCKING only (turning OFF).
    - Turning OFF hides nav item and navigates back to dashboard.
    - Turning ON from the page is disallowed; must use the gesture.
*/

(function () {
  "use strict";

  function escapeHtml(s) {
    return String(s ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#039;");
  }

  function _hashDashboard() {
    // Router is hash-based and expects #/dashboard.
    return "#/dashboard";
  }

  function _setFeedback(msg) {
    const el = document.getElementById("dtFeedback");
    if (!el) return;
    el.textContent = msg || "";
    el.style.opacity = "1";
  }

  function _getDevToolsKey() {
    try {
      if (typeof window.getDevToolsStorageKey === "function") return window.getDevToolsStorageKey();
    } catch (_) {
      // ignore
    }
    return "safe0ne_devtools_unlocked_v1";
  }

  function renderDevTools(/* route */) {
    const enabled = typeof window.isDevToolsUnlocked === "function" ? !!window.isDevToolsUnlocked() : false;
    const toggleLabel = enabled ? "On" : "Off";

    // Wire up after the router mounts this HTML.
    // (The router currently does not have a generic afterRender hook.)
    try { setTimeout(mountDevTools, 0); } catch (_) { /* ignore */ }

    const modulesCard = (function () {
      const M = window.Safe0neModules;
      if (!M || typeof M.list !== "function") {
        return `
          <div class="card" style="margin-top:16px;">
            <h2 style="margin-top:0;">Modules</h2>
            <p class="muted">Module registry not available.</p>
          </div>
        `;
      }

      const rows = M.list()
        .filter((m) => m.id !== "devtools")
        .map((m) => {
          const enabled = !!M.isEnabled(m.id);
          const health = M.getHealth(m.id);
          const badge = health.status === "failed"
            ? `<span class="pill" style="margin-left:8px;">FAILED</span>`
            : health.status === "loaded"
              ? `<span class="pill" style="margin-left:8px;">LOADED</span>`
              : `<span class="pill" style="margin-left:8px;">IDLE</span>`;

          const err = health.lastError ? escapeHtml(String(health.lastError)) : "";
          const errHtml = err
            ? `<details style="margin-top:6px;"><summary class="muted">Last error</summary><pre class="code" style="white-space:pre-wrap;">${err}</pre></details>`
            : `<div class="muted" style="margin-top:6px;">No errors.</div>`;

          return `
            <div class="dtModRow" data-mod-id="${escapeHtml(m.id)}" style="display:flex;justify-content:space-between;align-items:flex-start;gap:12px;padding:10px 0;border-top:1px solid #eee;">
              <div style="flex:1;min-width:200px;">
                <div style="display:flex;align-items:center;gap:8px;">
                  <strong>${escapeHtml(m.name)}</strong>
                  ${badge}
                </div>
                <div class="muted" style="margin-top:2px;">${escapeHtml(m.id)} · ${escapeHtml(m.route || "")}</div>
                ${errHtml}
              </div>
              <div style="display:flex;align-items:center;gap:8px;">
                <label class="switch" title="Enable/disable module">
                  <input class="dtModToggle" type="checkbox" ${enabled ? "checked" : ""} />
                  <span class="slider"></span>
                </label>
              </div>
            </div>
          `;
        })
        .join("");

      return `
        <div class="card" style="margin-top:16px;">
          <h2 style="margin-top:0;">Modules</h2>
          <p class="muted">Disable a module to hide its nav item and prevent routing into it. Failed modules are isolated by design.</p>
          <div id="dtModules">${rows || `<div class="muted">No modules registered.</div>`}</div>
        </div>
      `;
    })();

    const jsErrorsCard = (function(){
      const has = !!window.Safe0neErrors;
      const hint = has ? 'Captured from window.onerror + unhandledrejection (in-memory only).' : 'Safe0neErrors capture not loaded.';
      return `
        <div class="card" style="margin-top:16px;">
          <h2 style="margin-top:0;">Console errors (captured)</h2>
          <p class="muted">${escapeHtml(hint)}</p>
          <div style="display:flex;gap:8px;flex-wrap:wrap;margin:10px 0;">
            <button class="btn btn--ghost" id="dtJsErrRefresh" type="button">Refresh</button>
            <button class="btn btn--ghost" id="dtJsErrExport" type="button">Export JSON</button>
            <button class="btn btn--danger" id="dtJsErrClear" type="button">Clear</button>
          </div>
          <div id="dtJsErrors"><div class="muted">No captured errors yet.</div></div>
        </div>
      `;
    })();

    const activityCard = `
      <div class="card" style="margin-top:16px;">
        <h2 style="margin-top:0;">Recent Activity (Local Mode)</h2>
        <p class="muted">Fetch the last 20 activity events for a child (read-only). Uses Local SSOT via <code>/api/local/children/&lt;id&gt;/activity</code>.</p>
        <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:center;">
          <input id="dtActChildId" type="text" placeholder="Child ID" style="min-width:260px;" />
          <button class="btn" id="dtActLoad">Load activity</button>
        </div>
        <div id="dtActStatus" class="muted" style="margin-top:10px;min-height:18px;"></div>
        <div id="dtActList" style="margin-top:10px;"></div>
      </div>
    `;

    const ssotPurityCard = `
      <div class="card" style="margin-top:16px;">
        <h2 style="margin-top:0;">SSOT Purity</h2>
        <p class="muted">Browser storage is <strong>preferences-only</strong>. Domain state (children/profiles/policies) must come from Local SSOT.</p>
        <div id="dtSsotPurityStatus" class="muted" style="margin-top:10px;min-height:18px;"></div>
        <div style="display:flex;gap:10px;flex-wrap:wrap;margin-top:8px;">
          <button class="btn" id="dtSsotPurityCheck">Check now</button>
          <button class="btn" id="dtSsotPurityPurge">Purge legacy domain keys</button>
        </div>
        <details style="margin-top:10px;">
          <summary class="muted">What gets purged?</summary>
          <pre class="code" style="white-space:pre-wrap;">safe0ne.children.v1\nsafe0ne.childProfiles.v1</pre>
        </details>
      </div>
    `;

    return `
      <div class="page">
        <div class="dtHeader">
          <h1 style="margin:0;">Dev Tools</h1>

          <!-- Toggle is only used to LOCK (turn off). Turning on requires the 7x-tap gesture. -->
          <div class="devtools-toggleWrap">
            <span class="muted devtools-toggleLabel">Dev Tools</span>
            <label class="switch" title="Turn OFF to hide Dev Tools. To enable again, use 7x taps on the logo.">
              <input id="dtEnabled" type="checkbox" ${enabled ? "checked" : ""} ${enabled ? "" : "disabled"} />
              <span class="slider"></span>
            </label>
            <span class="muted devtools-toggleState">${toggleLabel}</span>
          </div>
        </div>

        <p class="muted" style="margin-top:8px;">Internal diagnostics and toggles. (Unlocked: ${enabled ? "yes" : "no"})</p>

        <div class="card" style="margin-top:16px;">
          <h2 style="margin-top:0;">Quick actions</h2>
          <div style="display:flex;gap:10px;flex-wrap:wrap;">
            <button class="btn" id="dtReload">Reload UI</button>
            <button class="btn" id="dtClearCache">Clear UI cache</button>
          </div>
          <div id="dtFeedback" class="muted" style="margin-top:10px;min-height:18px;"></div>
          <p class="muted" style="margin-top:8px;">Notes: this page is front-end only. It is safe to ship because it is hidden behind an unlock gesture.</p>
        </div>

        ${ssotPurityCard}

        ${activityCard}

        ${modulesCard}${jsErrorsCard}

        <div class="card" style="margin-top:16px;">
          <h2 style="margin-top:0;">Environment</h2>
          <pre class="code" style="white-space:pre-wrap;">${escapeHtml(
            JSON.stringify(
              {
                href: location.href,
                ua: navigator.userAgent,
                time: new Date().toISOString(),
              },
              null,
              2
            )
          )}</pre>
        </div>
      </div>
    `;
  }

  function mountDevTools() {
    // JS error capture panel
    function _renderJsErrors(){
      const host = document.getElementById('dtJsErrors');
      if (!host) return;
      const cap = window.Safe0neErrors;
      if (!cap || typeof cap.list !== 'function'){
        host.innerHTML = `<div class="muted">JS error capture is not available (errors.js not loaded).</div>`;
        return;
      }
      const entries = cap.list();
      if (!entries.length){
        host.innerHTML = `<div class="muted">No captured errors.</div>`;
        return;
      }
      const items = entries.slice().reverse().map((e)=>{
        const t = escapeHtml(e.t || '');
        const kind = escapeHtml(e.kind || '');
        const msg = escapeHtml(e.message || '');
        const src = escapeHtml(e.source || '');
        const lc = (e.line||e.col) ? `:${Number(e.line||0)}:${Number(e.col||0)}` : '';
        const stack = e.error && e.error.stack ? escapeHtml(String(e.error.stack)) : '';
        return `
          <details style="padding:8px 0;border-top:1px solid #eee;">
            <summary><strong>${kind}</strong> — ${msg} <span class="muted">${t}</span></summary>
            <div class="muted" style="margin-top:6px;">${src}${lc}</div>
            ${stack ? `<pre class="code" style="white-space:pre-wrap;margin-top:8px;">${stack}</pre>` : ``}
          </details>
        `;
      }).join('');
      host.innerHTML = items;
    }

    function _download(name, text){
      try{
        const blob = new Blob([text], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = name;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(()=>{ try{ URL.revokeObjectURL(url); }catch{} }, 2000);
      }catch(e){
        console.warn('DevTools: export failed', e);
        _setFeedback('Export failed (see console).');
      }
    }

    // Wire up after DOM mount
    setTimeout(() => {
      const $reload = document.getElementById("dtReload");
      const $clear = document.getElementById("dtClearCache");
      const $toggle = document.getElementById("dtEnabled");

      $reload?.addEventListener("click", () => {
        _setFeedback("Reloading UI...");
        setTimeout(() => {
          try {
            location.reload();
          } catch (_) {
            // ignore
          }
        }, 150);
      });

      $clear?.addEventListener("click", () => {
        _setFeedback("Clearing UI cache...");
        try {
          // Preserve the DevTools unlock key; clear everything else.
          const keep = new Set([_getDevToolsKey()]);
          const keys = [];
          for (let i = 0; i < localStorage.length; i++) {
            const k = localStorage.key(i);
            if (k) keys.push(k);
          }
          keys.forEach((k) => {
            if (!keep.has(k)) localStorage.removeItem(k);
          });
          _setFeedback("UI cache cleared.");
        } catch (e) {
          console.warn("DevTools: Clear cache failed", e);
          _setFeedback("Failed to clear cache (see console). ");
        }
      });

      const forbiddenKeys = ["safe0ne.children.v1", "safe0ne.childProfiles.v1"];
      const $ssotStatus = document.getElementById("dtSsotPurityStatus");
      const $ssotCheck = document.getElementById("dtSsotPurityCheck");
      const $ssotPurge = document.getElementById("dtSsotPurityPurge");

      function refreshSsotPurityStatus(extraMsg) {
        try {
          const found = [];
          for (const k of forbiddenKeys) {
            try { if (localStorage.getItem(k) != null) found.push(k); } catch (_) {}
          }
          if (!$ssotStatus) return;
          if (found.length === 0) {
            $ssotStatus.textContent = extraMsg || "OK — no legacy domain keys found in browser storage.";
          } else {
            $ssotStatus.textContent = `FAIL — found legacy domain keys: ${found.join(", ")}. Purge is recommended.`;
          }
        } catch (e) {
          if ($ssotStatus) $ssotStatus.textContent = `SSOT purity check failed: ${String(e && (e.message || e))}`;
        }
      }

      $ssotCheck?.addEventListener("click", () => refreshSsotPurityStatus());
      $ssotPurge?.addEventListener("click", () => {
        try {
          forbiddenKeys.forEach((k) => {
            try { localStorage.removeItem(k); } catch (_) {}
          });
          refreshSsotPurityStatus("Purged legacy domain keys.");
        } catch (e) {
          refreshSsotPurityStatus(`Purge failed: ${String(e && (e.message || e))}`);
        }
      });

      // Update on load.
      refreshSsotPurityStatus();

      // Recent Activity (Local Mode)
      const $actChildId = document.getElementById("dtActChildId");
      const $actLoad = document.getElementById("dtActLoad");
      const $actStatus = document.getElementById("dtActStatus");
      const $actList = document.getElementById("dtActList");

      function _setActStatus(msg) {
        if (!$actStatus) return;
        $actStatus.textContent = msg || "";
        $actStatus.style.opacity = "1";
      }

      function _renderActivityItems(items) {
        if (!$actList) return;
        const list = Array.isArray(items) ? items : [];
        if (list.length === 0) {
          $actList.innerHTML = `<div class="muted">No recent events.</div>`;
          return;
        }
        $actList.innerHTML = list
          .map((ev) => {
            const ts = escapeHtml(ev?.ts ?? ev?.time ?? ev?.at ?? "");
            const type = escapeHtml(ev?.type ?? ev?.kind ?? "event");
            const msg = escapeHtml(ev?.msg ?? ev?.message ?? ev?.note ?? "");
            return `
              <div style="border:1px solid #eee;border-radius:12px;padding:10px;margin-top:8px;">
                <div class="muted" style="font-size:12px;">${ts} · ${type}</div>
                <div style="margin-top:4px;white-space:pre-wrap;">${msg}</div>
              </div>
            `;
          })
          .join("");
      }

      async function _loadActivity() {
        const childId = ($actChildId?.value || "").trim();
        if (!childId) {
          _setActStatus("Enter a Child ID first.");
          if ($actList) $actList.innerHTML = "";
          return;
        }

        _setActStatus("Loading recent activity...");
        if ($actList) $actList.innerHTML = "";

        try {
          const api = window.Safe0neApi;
          if (!api || typeof api.getChildActivityLocal !== "function") {
            _setActStatus("Local API client not available (Safe0neApi.getChildActivityLocal missing). ");
            return;
          }

          const res = await api.getChildActivityLocal(childId, { take: 20 });
          if (!res || !res.ok) {
            _setActStatus(`Failed: ${res && res.error ? res.error : "unknown error"}`);
            return;
          }

          const data = res.data;
          const items = Array.isArray(data) ? data : (Array.isArray(data?.items) ? data.items : []);
          _setActStatus(`Showing ${items.length} event(s).`);
          _renderActivityItems(items);
        } catch (e) {
          console.warn("DevTools: activity fetch failed", e);
          _setActStatus("Failed to load activity (see console). ");
        }
      }

      $actLoad?.addEventListener("click", () => {
        // keep async-safe; defer work
        try { setTimeout(_loadActivity, 0); } catch (_) { _loadActivity(); }
      });

      $toggle?.addEventListener("change", () => {
        const on = !!$toggle.checked;

        if (!on) {
          // Lock and hide rail entry. Re-enable requires 7x-tap gesture.
          if (typeof window.setDevToolsUnlocked === "function") window.setDevToolsUnlocked(false);
          if (typeof window.syncDevToolsNavVisibility === "function") window.syncDevToolsNavVisibility();
          _setFeedback("Dev Tools locked. Use 7x taps on logo to re-enable.");
          setTimeout(() => {
            try {
              location.hash = _hashDashboard();
            } catch (_) {
              // ignore
            }
          }, 250);
          return;
        }

        // Turning on from within the page is not allowed; must use gesture.
        if (typeof window.setDevToolsUnlocked === "function") window.setDevToolsUnlocked(false);
        $toggle.checked = false;
        _setFeedback("To enable Dev Tools, use the 7x tap gesture on the logo.");
      });

      // Module toggles
      try {
        const M = window.Safe0neModules;
        if (M && typeof M.setEnabled === "function") {
          document.querySelectorAll(".dtModRow").forEach((row) => {
            const id = row.getAttribute("data-mod-id");
            const toggle = row.querySelector(".dtModToggle");
            if (!id || !toggle) return;
            toggle.addEventListener("change", () => {
              const on = !!toggle.checked;
              M.setEnabled(id, on);
              // Refresh nav visibility immediately.
              if (typeof M.syncNav === "function") M.syncNav();
              // If disabling the current route, kick back to dashboard.
              if (!on) {
                const route = (location.hash || "").replace(/^#/, "");
                const info = M.get(id);
                if (info && info.route && route.startsWith(info.route)) {
                  location.hash = _hashDashboard();
                }
              }
            });
          });
        }
      } catch (e) {
        console.warn("DevTools: module toggle wiring failed", e);
      }
    }, 0);
  
    try{
      const rBtn = document.getElementById('dtJsErrRefresh');
      if (rBtn) rBtn.addEventListener('click', function(){ _renderJsErrors(); });
      const eBtn = document.getElementById('dtJsErrExport');
      if (eBtn) eBtn.addEventListener('click', function(){
        const cap = window.Safe0neErrors;
        const json = cap && typeof cap.exportJson==='function' ? cap.exportJson() : JSON.stringify({ entries: [] }, null, 2);
        const stamp = (new Date()).toISOString().replaceAll(':','').replaceAll('-','').replaceAll('.','');
        _download('safe0ne_js_errors_'+stamp+'.json', json);
      });
      const cBtn = document.getElementById('dtJsErrClear');
      if (cBtn) cBtn.addEventListener('click', function(){
        const cap = window.Safe0neErrors;
        if (cap && typeof cap.clear==='function') cap.clear();
        _renderJsErrors();
      });
      _renderJsErrors();
    }catch(e){
      console.warn('DevTools: JS errors panel wiring failed', e);
    }
}

  window.Safe0neDevTools = {
    renderDevTools,
    mountDevTools,
  };
})();
