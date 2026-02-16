// Safe0ne module registry + toggles.
//
// This is intentionally framework-less and "boring": plain JS + localStorage.
// The goal is to prevent one broken module from taking down the whole UI.

(function () {
  const LS_PREFIX = "safe0ne.module.enabled.";

  /**
   * Registry metadata.
   * - id: stable key
   * - name: display
   * - route: hash route segment
   * - script: script URL (used for dynamic loading if needed)
   * - global: global symbol exposed by the module file (legacy-compatible)
   */
  const REGISTRY = {
    dashboard: { id: "dashboard", name: "Dashboard", route: "dashboard", script: "/app/features/dashboard.js", global: "Safe0neDashboard" },
    parent: { id: "parent", name: "Parent", route: "parent", script: "/app/features/parent.js", global: "Safe0neParent" },
    children: { id: "children", name: "Children", route: "children", script: "/app/features/children.js", global: "Safe0neChildren" },
    alerts: { id: "alerts", name: "Alerts & Reports", route: "alerts", script: "/app/features/alerts.js", global: "Safe0neAlerts" },
    reports: { id: "reports", name: "Reports", route: "reports", script: "/app/features/reports.js", global: "Safe0neReports" },
    requests: { id: "requests", name: "Requests", route: "requests", script: "/app/features/requests.js", global: "Safe0neRequests" },
    support: { id: "support", name: "Support", route: "support", script: "/app/features/support.js", global: "Safe0neSupport" },
    admin: { id: "admin", name: "Admin", route: "admin", script: "/app/features/admin.js", global: "Safe0neAdmin" },
    devtools: { id: "devtools", name: "DevTools", route: "devtools", script: "/app/features/devtools.js", global: "Safe0neDevTools" },
  };

  // Health states: not_loaded | loading | loaded | disabled | failed
  const health = Object.create(null);

  function ensureHealthEntry(id) {
    if (health[id]) return;
    const enabled = readEnabled(id);
    const m = REGISTRY[id];
    const alreadyLoaded = !!(m && m.global && window[m.global]);
    health[id] = {
      state: enabled ? (alreadyLoaded ? "loaded" : "not_loaded") : "disabled",
      lastError: null,
      lastErrorAt: null,
    };
  }

  function initHealth() {
    for (const id of Object.keys(REGISTRY)) {
      const enabled = readEnabled(id);
      const m = REGISTRY[id];
      const alreadyLoaded = !!(m.global && window[m.global]);
      health[id] = {
        state: enabled ? (alreadyLoaded ? "loaded" : "not_loaded") : "disabled",
        lastError: null,
        lastErrorAt: null,
      };
    }
  }

  // Prevent duplicate script inserts + concurrent loads.
  const loadPromises = Object.create(null);

  function nowIso() {
    try {
      return new Date().toISOString();
    } catch {
      return "";
    }
  }

  function readEnabled(id) {
    const k = LS_PREFIX + id;
    const v = localStorage.getItem(k);
    if (v === null) return true; // default-on
    return v === "1";
  }

  function writeEnabled(id, enabled) {
    localStorage.setItem(LS_PREFIX + id, enabled ? "1" : "0");
  }

  // Initialize health immediately so DevTools can render reliably even before DOMContentLoaded.
  // (Sidebar hiding still waits for DOM.)
  initHealth();

  function getModule(id) {
    return REGISTRY[id] || null;
  }

  function listModules() {
    return Object.keys(REGISTRY).map((id) => {
      const m = REGISTRY[id];
      const enabled = readEnabled(id);
      ensureHealthEntry(id);
      const h = health[id] || { state: "not_loaded" };
      return {
        id: m.id,
        name: m.name,
        route: m.route,
        script: m.script,
        global: m.global,
        enabled,
        health: { ...h },
      };
    });
  }

  function getHealth(id) {
    ensureHealthEntry(id);
    return health[id] ? { ...health[id] } : null;
  }

  function setHealth(id, state, err) {
    ensureHealthEntry(id);
    if (!health[id]) return;
    health[id].state = state;
    if (err) {
      health[id].lastError = String(err && err.message ? err.message : err);
      health[id].lastErrorAt = nowIso();
    }
  }

  function clearError(id) {
    ensureHealthEntry(id);
    if (!health[id]) return;
    health[id].lastError = null;
    health[id].lastErrorAt = null;
  }

  function hideOrShowSidebarRoutes() {
    // Sidebar uses data-route attributes.
    try {
      for (const id of Object.keys(REGISTRY)) {
        const route = REGISTRY[id].route;
        const enabled = readEnabled(id);
        document.querySelectorAll(`[data-route="${route}"]`).forEach((el) => {
          const li = el.closest("li") || el;
          li.style.display = enabled ? "" : "none";
        });
      }
    } catch {
      // non-fatal
    }
  }

  async function ensureLoaded(id) {
    const m = REGISTRY[id];
    if (!m) throw new Error(`Unknown module: ${id}`);

    // Never load a disabled module.
    if (!readEnabled(id)) {
      setHealth(id, "disabled");
      const err = new Error(`Module '${id}' is disabled`);
      // Do not treat this as "failed"; disabled is a normal state.
      throw err;
    }

    // If module global already exists, consider it loaded.
    if (m.global && window[m.global]) {
      setHealth(id, "loaded");
      return;
    }

    // If already loading, await the in-flight promise.
    if (loadPromises[id]) {
      await loadPromises[id];
      if (m.global && window[m.global]) {
        setHealth(id, "loaded");
        return;
      }
    }

    setHealth(id, "loading");
    clearError(id);

    // Dynamically load the module script.
    // This is the ONLY supported loader. We de-dupe by URL + in-flight promise.
    const p = new Promise((resolve, reject) => {
      try {
        // If a matching script tag already exists, do not append another.
        const existing = document.querySelector(`script[data-safe0ne-module="${id}"]`) ||
          (m.script ? document.querySelector(`script[src="${m.script}"]`) : null);
        if (existing) {
          // If it already loaded, resolve; otherwise wait for its load/error.
          if (existing.getAttribute("data-safe0ne-loaded") === "1") return resolve();
          existing.addEventListener("load", () => resolve(), { once: true });
          existing.addEventListener("error", () => reject(new Error(`Failed to load ${m.script}`)), { once: true });
          return;
        }

        const s = document.createElement("script");
        s.src = m.script;
        s.async = true;
        s.setAttribute("data-safe0ne-module", id);
        s.onload = () => {
          s.setAttribute("data-safe0ne-loaded", "1");
          resolve();
        };
        s.onerror = () => reject(new Error(`Failed to load ${m.script}`));
        document.head.appendChild(s);
      } catch (e) {
        reject(e);
      }
    });

    loadPromises[id] = p;
    try {
      await p;
    } catch (e) {
      setHealth(id, "failed", e);
      throw e;
    } finally {
      // Leave the promise for a short time to help coalesce immediate callers.
      // If it failed, it will be replaced next call.
      setTimeout(() => {
        if (loadPromises[id] === p) delete loadPromises[id];
      }, 0);
    }

    if (m.global && window[m.global]) {
      setHealth(id, "loaded");
      return;
    }

    // Loaded script but did not expose expected global.
    const err = new Error(`Module loaded but global '${m.global}' was not found`);
    setHealth(id, "failed", err);
    throw err;
  }

  function setEnabled(id, enabled) {
    if (!REGISTRY[id]) return;
    writeEnabled(id, enabled);

    // Update health state immediately.
    if (!enabled) {
      setHealth(id, "disabled");
      clearError(id);
    } else {
      // If global already exists, it's loaded; otherwise mark not_loaded.
      const m = REGISTRY[id];
      if (m?.global && window[m.global]) setHealth(id, "loaded");
      else setHealth(id, "not_loaded");
    }

    hideOrShowSidebarRoutes();
    window.dispatchEvent(new CustomEvent("safe0ne:modules-changed", { detail: { id, enabled } }));
  }

  function isEnabled(id) {
    return readEnabled(id);
  }

function shouldLogContracts(){
  try{
    return localStorage.getItem("safe0ne_devtools_unlocked_v1") === "true";
  }catch{
    return false;
  }
}

function assertContracts(options){
  const st = runBootSelfTest();
  const log = !options || options.log !== false;
  if (log && !st.pass && shouldLogContracts()){
    try{
      const failed = (st.results || []).filter(r => !r.pass).map(r => r.name);
      console.error("[Safe0ne] Self-test FAIL:", failed.join(", ") || "unknown");
    }catch{}
  }
  return st;
}

function runBootSelfTest() {
    // Deterministic contract checks (these are our modular invariants)
    const results = [];
    const hasRegistry = !!REGISTRY && Object.keys(REGISTRY).length > 0;
    results.push({ name: "modules.registry", pass: hasRegistry });

    const M = window.Safe0neModules;
    const requiredModuleFns = ["list","get","isEnabled","setEnabled","ensureLoaded","getHealth","runBootSelfTest"];
    results.push({
      name: "modules.api",
      pass: !!M && requiredModuleFns.every((k) => typeof M[k] === "function"),
    });
    results.push({ name: "modules.canonical", pass: !!M && M.__canonical === true });

    const Ui = window.Safe0neUi;
    const requiredUiFns = ["escapeHtml","card","pageHeader","setSelfTestBadge"];
    results.push({
      name: "ui.helpers",
      pass: !!Ui && requiredUiFns.every((k) => typeof Ui[k] === "function"),
    });

    const R = window.Safe0neRouter;
    results.push({ name: "router.exposed", pass: !!R && typeof R.render === "function" });
    results.push({ name: "router.asyncSafe", pass: !!R && R.asyncSafe === true });
    results.push({ name: "router.routes.children", pass: !!R && Array.isArray(R.routeKeys) && R.routeKeys.includes("children") });
    results.push({ name: "router.routes.alerts", pass: !!R && Array.isArray(R.routeKeys) && R.routeKeys.includes("alerts") });
    results.push({ name: "router.routes.requests", pass: !!R && Array.isArray(R.routeKeys) && R.routeKeys.includes("requests") });
    results.push({ name: "router.routes.support", pass: !!R && Array.isArray(R.routeKeys) && R.routeKeys.includes("support") });
    results.push({ name: "router.routes.devtools", pass: !!R && Array.isArray(R.routeKeys) && R.routeKeys.includes("devtools") });

    const DT = window.Safe0neDevTools;
    results.push({ name: "devtools.api", pass: !!DT && typeof DT.renderDevTools === "function" });

    const A = window.Safe0neAlerts;
    results.push({ name: "alerts.api", pass: !!A && typeof A.renderAlerts === "function" });
    // Marker/contract expectations (kept in JS so we don't rely solely on build-time marker tests)
    results.push({ name: "alerts.delegates.buildAlerts", pass: !!A && typeof A.buildAlerts === "function" });
    results.push({ name: "alerts.delegates.isAcked", pass: !!A && typeof A.isAcked === "function" });

    const C = window.Safe0neChildren;
    results.push({ name: "children.api", pass: !!C && typeof C.renderChildren === "function" });

    const S = window.Safe0neSupport;
    results.push({ name: "support.api", pass: !!S && typeof S.renderSupport === "function" });

    // Local API availability probe (async): keep this out of the sync contract checks
    // so the router never blocks on network.
    try{ queueMicrotask(runAsyncSelfChecks); }catch{ setTimeout(runAsyncSelfChecks, 0); }

    const pass = results.every((r) => r.pass);
    window.Safe0neSelfTest = { pass, results, at: nowIso() };

    // Update badge if UI helpers exist.
    try{ Ui?.setSelfTestBadge?.(window.Safe0neSelfTest); }catch{}

    window.dispatchEvent(new CustomEvent("safe0ne:selftest", { detail: window.Safe0neSelfTest }));
    return window.Safe0neSelfTest;
  }

  async function runAsyncSelfChecks(){
    // This is a best-effort probe. It must never throw and must never impact routing.
    const ensure = (name, pass, info) => {
      try{
        const st = window.Safe0neSelfTest || { pass: true, results: [], at: nowIso() };
        const results = Array.isArray(st.results) ? st.results.slice() : [];
        const idx = results.findIndex(r => r.name === name);
        const entry = { name, pass: !!pass, info };
        if (idx >= 0) results[idx] = entry; else results.push(entry);
        const allPass = results.every(r => r.pass);
        window.Safe0neSelfTest = { pass: allPass, results, at: nowIso() };
        try{ window.Safe0neUi?.setSelfTestBadge?.(window.Safe0neSelfTest); }catch{}
        window.dispatchEvent(new CustomEvent("safe0ne:selftest", { detail: window.Safe0neSelfTest }));
      }catch{}
    };

    try{
      const res = await fetch("/api/local/_health", { cache: "no-store" });
      if (!res.ok){
        ensure("localApi.health", false, `HTTP ${res.status}`);
        return;
      }
      const json = await res.json().catch(() => null);
      const ok = !!(json && (json.ok === true || json.data?.ok === true));
      ensure("localApi.health", ok, ok ? null : "bad_payload");
    }catch(err){
      ensure("localApi.health", false, String(err && (err.message || err)));
    }

    // Optional: SSOT control-plane info endpoint. Best-effort, never throws, never blocks routing.
    try{
      const res = await fetch("/api/local/control-plane/info", { cache: "no-store" });
      if (!res.ok){
        ensure("localApi.controlPlaneInfo", false, `HTTP ${res.status}`);
      } else {
        const json = await res.json().catch(() => null);
        const ok = !!(json && (json.ok === true || json.data?.ok === true || json.backend || json.data?.backend));
        ensure("localApi.controlPlaneInfo", ok, ok ? null : "bad_payload");
      }
    }catch(err){
      ensure("localApi.controlPlaneInfo", false, String(err && (err.message || err)));
    }
  }

  // Expose public API
  window.Safe0neModules = {
    list: listModules,
    get: getModule,
    isEnabled,
    setEnabled,
    ensureLoaded,
    getHealth,
    _setHealth: setHealth,
    runBootSelfTest,
    assertContracts,
    __canonical: true,
  };

  // Initial sync once DOM is ready.
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", () => {
      hideOrShowSidebarRoutes();
      runBootSelfTest();
    });
  } else {
    hideOrShowSidebarRoutes();
    runBootSelfTest();
  }

  // If a feature throws during load/execution, capture it as "failed" where possible.
  window.addEventListener("error", (ev) => {
    const msg = ev?.message || "";
    // Best-effort map: look at filename
    const file = ev?.filename || "";
    for (const id of Object.keys(REGISTRY)) {
      const script = REGISTRY[id].script;
      if (file.includes(script) || file.includes(script.replace("/", ""))) {
        setHealth(id, "failed", msg);
      }
    }
  });

  // Capture async errors as module failures where possible.
  window.addEventListener("unhandledrejection", (ev) => {
    try {
      const reason = ev?.reason;
      const msg = reason && reason.message ? reason.message : String(reason || "Unhandled rejection");
      // Best-effort heuristic: if the stack mentions a module script URL, assign failure.
      const stack = reason && reason.stack ? String(reason.stack) : "";
      for (const id of Object.keys(REGISTRY)) {
        const script = REGISTRY[id].script;
        if ((stack && script && stack.includes(script)) || (typeof msg === "string" && script && msg.includes(script))) {
          setHealth(id, "failed", msg);
        }
      }
    } catch {
      // non-fatal
    }
  });


  // --------------------------------------------
  // UI state capture (best-effort diagnostics)
  // --------------------------------------------
  try {
    if (!window.Safe0neUiState) {
      window.Safe0neUiState = {
        recentRoutes: [],
        recentErrors: [],
        max: 10,
      };
    }
    const _ui = window.Safe0neUiState;
    function _pushBounded(arr, item) {
      try {
        arr.push(item);
        const max = _ui.max || 10;
        while (arr.length > max) arr.shift();
      } catch (_) {}
    }
    function _captureRoute() {
      _pushBounded(_ui.recentRoutes, {
        ts: new Date().toISOString(),
        hash: String(location.hash || ""),
        href: String(location.href || ""),
      });
    }
    window.addEventListener("hashchange", _captureRoute);
    // capture initial route once
    _captureRoute();
    window.addEventListener("error", function (ev) {
      _pushBounded(_ui.recentErrors, {
        ts: new Date().toISOString(),
        type: "error",
        message: String(ev && ev.message ? ev.message : "error"),
        filename: String(ev && ev.filename ? ev.filename : ""),
        lineno: (ev && ev.lineno) || null,
        colno: (ev && ev.colno) || null,
      });
    });
    window.addEventListener("unhandledrejection", function (ev) {
      let msg = "unhandledrejection";
      try {
        msg = (ev && ev.reason && (ev.reason.message || String(ev.reason))) || msg;
      } catch (_) {}
      _pushBounded(_ui.recentErrors, {
        ts: new Date().toISOString(),
        type: "unhandledrejection",
        message: String(msg),
      });
    });
  } catch (_) {}

})();
