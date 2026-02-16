/* Safe0ne P2 Router: hash-based so the server can stay static.
   Rule: no horizontal scroll; only vertical scroll in main content.
*/
let routes = [];


// Dev Tools gating
// NOTE: keep this key in sync everywhere (gesture, route rendering, nav visibility).
const DEVTOOLS_STORAGE_KEY = "safe0ne_devtools_unlocked_v1";

function isDevToolsUnlocked(){
  try{ return localStorage.getItem(DEVTOOLS_STORAGE_KEY) === "true"; }
  catch{ return false; }
}

function setDevToolsUnlocked(value){
  try{ localStorage.setItem(DEVTOOLS_STORAGE_KEY, value ? "true" : "false"); }
  catch{}
  syncDevToolsNavVisibility();
}

function syncDevToolsNavVisibility(){
  const nav = document.getElementById("nav-devtools");
  if (!nav) return;
  const unlocked = isDevToolsUnlocked();
  nav.style.display = unlocked ? "flex" : "none";
}

function attachDevToolsUnlockGesture(){
  const logo = document.getElementById("railLogo") || document.querySelector(".rail__logo");
  if (!logo) return;

  let tapCount = 0;
  let windowStart = 0;
  const WINDOW_MS = 5000;
  const REQUIRED_TAPS = 7;

  const onTap = (ev) => {
    // Ignore if Dev Tools already unlocked.
    if (isDevToolsUnlocked()) return;
    // For touch/click, prevent accidental drag selection.
    try{ ev.preventDefault(); }catch{}

    const now = Date.now();
    if (!windowStart || (now - windowStart) > WINDOW_MS){
      windowStart = now;
      tapCount = 0;
    }
    tapCount += 1;

    if (tapCount >= REQUIRED_TAPS){
      setDevToolsUnlocked(true);
      tapCount = 0;
      windowStart = 0;

      // If user is already on /devtools via a direct hash, re-render to reflect unlocked state.
      if (parseRoute().key === "devtools"){
        void render();
      }
    }
  };

  // Click for mouse, touchend for touch.
  logo.addEventListener("click", onTap, { passive: false });
  logo.addEventListener("touchend", onTap, { passive: false });
}

function parseRoute(){
  const raw = (location.hash || "#/dashboard").replace("#/","");
  const parts = raw.split("?")[0].split("/").map(p => p.trim()).filter(Boolean);

  // #/child/<guid>
  if (parts.length >= 2 && parts[0].toLowerCase() === "child"){
    return { key: "child", childId: parts[1] };
  }

  const key = (parts[0] || "dashboard").toLowerCase();
  return { key: routes.some(r => r.key === key) ? key : "dashboard" };
}

// Parses location.hash query params for routes like "#/requests?child=...&status=Pending".
function parseHashQuery(){
  try{
    const h = String(location.hash || "");
    const qIndex = h.indexOf("?");
    if (qIndex < 0) return {};
    const qs = h.slice(qIndex + 1);
    const out = {};
    qs.split("&").forEach(part => {
      if (!part) return;
      const eq = part.indexOf("=");
      const k = decodeURIComponent((eq >= 0 ? part.slice(0, eq) : part).replace(/\+/g, " "));
      const v = decodeURIComponent((eq >= 0 ? part.slice(eq + 1) : "").replace(/\+/g, " "));
      if (k) out[k] = v;
    });
    return out;
  }catch{ return {}; }
}


function setActiveNav(key){
  const navKey = key === "child" ? "children" : key;
  document.querySelectorAll(".rail__item").forEach(a => {
    const isActive = a.dataset.route === navKey;
    if (isActive) a.setAttribute("aria-current","page");
    else a.removeAttribute("aria-current");
  });
}

function mount(html){
  const root = document.getElementById("page-root");
  // Never allow undefined/null/Promise to stringify into the DOM.
  if (html == null) html = "";
  if (typeof html !== "string") html = String(html);
  root.innerHTML = html;
  root.focus({ preventScroll: true });
}

async function resolveHtml(out){
  // Await promises from async renderers.
  if (out && typeof out.then === "function") out = await out;
  if (out == null) return "";
  return (typeof out === "string") ? out : String(out);
}

function pageHeader(title, lead, ctaLabel){
  const cta = ctaLabel
    ? `<button class="btn btn--primary" type="button" disabled aria-disabled="true" title="Coming soon">${ctaLabel}</button>`
    : "";
  return `
    <div class="page__header">
      <div style="display:flex;align-items:flex-start;justify-content:space-between;gap:16px;flex-wrap:wrap;">
        <div style="min-width:0">
          <h1 class="h1">${escapeHtml(title)}</h1>
          <p class="lead">${escapeHtml(lead)}</p>
        </div>
        ${cta}
      </div>
    </div>
  `;
}

function card(title, body, metaLabel){
  return `
    <div class="card">
      <h2>${escapeHtml(title)}</h2>
      <p>${escapeHtml(body)}</p>
      <div class="kv">
        <span>${escapeHtml(metaLabel || "What you can do here")}</span>
        <button class="btn" type="button" disabled aria-disabled="true" title="Coming soon">Open</button>
      </div>
    </div>
  `;
}

function renderDashboard(){
  /* ADR-0003: delegate Dashboard route rendering to feature module when available. */
  try{
    if (window.Safe0neDashboard && typeof window.Safe0neDashboard.renderDashboard === "function"){
      const html = window.Safe0neDashboard.renderDashboard();
      if (typeof html === "string") return html;
    }
    if (window.Safe0neDashboard && typeof window.Safe0neDashboard.render === "function"){
      const html = window.Safe0neDashboard.render();
      if (typeof html === "string") return html;
    }
  }catch(err){
    console.warn("Safe0neDashboard module render failed; using fallback.", err);
  }
  return _renderDashboardFallback();
}

function _renderDashboardFallback(){
  return `
    ${pageHeader("Dashboard",
      "A calm overview: what’s protected, what needs attention, and what you can do next.",
      "Quick actions (stub)"
    )}
    <div class="grid">
      ${card("Protection status",
        "See whether protection is running and which devices are online. If something needs attention, you’ll see a clear next step.",
        "Shows status chips"
      )}
      ${card("What’s next checklist",
        "No onboarding wizard. Just a short checklist that helps you set things up safely, one step at a time.",
        "Setup checklist (later)"
      )}
      ${card("Weekly snapshot",
        "Screen time, top apps, top sites, blocked attempts, and requests — in plain language.",
        "Metrics (stub)"
      )}
    </div>
  `;
}

function renderParent(){
  return `
    ${pageHeader("Parent Profile",
      "Manage your account, privacy choices, notifications, and recovery tools in one place.",
      "Edit settings (stub)"
    )}
    <div class="grid">
      ${card("Privacy & data", "Choose safe defaults and understand what gets stored. Export is available later.", "Explains settings")}
      ${card("Notifications", "Pick what you want to hear about, and set quiet hours so you aren’t spammed.", "Plain-language toggles")}
      ${card("Co‑parent access", "Add another trusted adult with clear roles and permissions.", "Roles scaffold")}
    </div>
  `;
}

function renderChildren(route) {
  // Prefer modular feature implementation when available.
  try {
    const mod = window.Safe0neChildren;
    if (mod && typeof mod.renderChildren === 'function' && mod.renderChildren !== renderChildren) {
      return mod.renderChildren(route);
    }
  } catch (_) { /* ignore */ }

  const containerId = "children-list";
  // Kick off async load after mount.
  setTimeout(async () => {
    const root = document.getElementById(containerId);
    if (!root) return;

    root.innerHTML = `<div class="skeleton">Loading children…</div>`;
    const res = await Safe0neApi.getChildren();
    if (!res.ok){
      root.innerHTML = `<div class="notice notice--danger">Could not load children: ${escapeHtml(res.error || "unknown error")}</div>`;
      return;
    }

    const items = res.data;
    if (!items || items.length === 0){
      root.innerHTML = `<div class="card"><h2>No children yet</h2><p>Add a child profile to begin.</p></div>`;
      return;
    }

    root.innerHTML = items.map(c => `
      <div class="card">
        <h2>${escapeHtml(c.displayName || "Child")}</h2>
        <p>Manage devices, policies, and activity inside this child profile.</p>
        <div class="kv">
          <span>Child profile</span>
          <a class="btn" href="#/child/${encodeURIComponent(c.id.value)}">Open</a>
        </div>
      </div>
    `).join("");
  });

  return `
    ${pageHeader("Children",
      "Add a child, pair devices, and manage all rules inside each child profile (Devices, Policies, Activity).",
      "Add child (later)"
    )}
    <div class="grid" id="${containerId}"></div>
  `;
}


function renderRequests(){
  /* ADR-0003: delegate Requests route rendering to feature module when available. */
  try{
    if (window.Safe0neRequests && typeof window.Safe0neRequests.renderRequests === "function"){
      const html = window.Safe0neRequests.renderRequests();
      if (typeof html === "string") return html;
    }
    if (window.Safe0neRequests && typeof window.Safe0neRequests.render === "function"){
      const html = window.Safe0neRequests.render();
      if (typeof html === "string") return html;
    }
  }catch(err){
    console.warn("Safe0neRequests module render failed; using fallback.", err);
  }
  return _renderRequestsFallback();
}

function _renderRequestsFallback(){
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
              ${children.map(c => `<option value="${escapeHtml((c.id && c.id.value) || "")}">${escapeHtml(c.displayName || "Child")}</option>`).join("")}
            </select>
          </div>
          <div class="cell">
            <label class="label">Status</label>
            <select id="req-filter-status">
              <option value="Pending" selected>Pending</option>
              <option value="Approved">Approved</option>
              <option value="Denied">Denied</option>
              <option value="Cancelled">Cancelled</option>
              <option value="">All</option>
            </select>
          </div>
          <div class="cell">
            <button class="btn btn--primary" type="button" id="req-refresh">Refresh</button>
          </div>
        </div>
      </div>
      <div id="req-results"></div>
    `;

    const childSel = document.getElementById("req-filter-child");
    const statusSel = document.getElementById("req-filter-status");const _statusSel = document.getElementById("req-filter-status");
    const refreshBtn = document.getElementById("req-refresh");
    const results = document.getElementById("req-results");

    function getProp(obj, ...names){
      for (const n of names){
        if (obj && Object.prototype.hasOwnProperty.call(obj, n)) return obj[n];
      }
      return undefined;
    }

    function normalizeStatus(s){
      if (s === null || s === undefined) return "";
      if (typeof s === "string") return s;
      // enums may come across as numbers
      const map = { 0:"Pending", 1:"Approved", 2:"Denied", 3:"Cancelled" };
      return map[String(s)] || String(s);
    }

    function normalizeType(t){
      if (t === null || t === undefined) return "";
      if (typeof t === "string") return t;
      const map = { 0:"MoreTime", 1:"UnblockApp", 2:"UnblockSite" };
      return map[String(t)] || String(t);
    }

    function fmtUtc(iso){
      if (!iso) return "";
      try{ return new Date(iso).toLocaleString(); }catch{ return String(iso); }
    }


    function localMidnightNext(){
      const d = new Date();
      d.setHours(24,0,0,0);
      return d;
    }

    function renderDecisionSummary(req){
      const type = normalizeType(getProp(req, "type", "Type"));
      const statusTxt = normalizeStatus(getProp(req, "status", "Status"));
      const statusLower = String(statusTxt||"").toLowerCase();
      const decision = getProp(req, "decision", "Decision") || null;
      const decidedAt = getProp(req, "decidedAtUtc", "DecidedAtUtc");

      if (statusLower === "approved"){
        if (type === "MoreTime"){
          const extra = decision ? (decision.extraMinutes ?? decision.ExtraMinutes) : null;
          const mins = (extra != null && Number.isFinite(Number(extra))) ? Number(extra) : 15;
          const until = localMidnightNext().toLocaleTimeString([], { hour:"2-digit", minute:"2-digit" });
          return `<div class="notice notice--success"><b>Approved</b> — +${escapeHtml(String(mins))} minutes (until ${escapeHtml(until)})</div>`;
        }

        // UnblockApp / UnblockSite: show duration + computed expiry
        const durRaw = decision ? (decision.durationMinutes ?? decision.DurationMinutes) : null;
        const dur = (durRaw != null && Number.isFinite(Number(durRaw))) ? Number(durRaw) : 30;

        let expiresTxt = "";
        if (decidedAt){
          try{
            const exp = new Date(new Date(decidedAt).getTime() + (dur * 60000));
            expiresTxt = exp.toLocaleTimeString([], { hour:"2-digit", minute:"2-digit" });
          }catch{
            expiresTxt = "";
          }
        }
        const tail = expiresTxt ? ` (until ${escapeHtml(expiresTxt)})` : "";
        return `<div class="notice notice--success"><b>Approved</b> — allowed for ${escapeHtml(String(dur))}m${tail}</div>`;
      }

      if (statusLower === "denied"){
        return `<div class="notice notice--danger"><b>Denied</b></div>`;
      }

      if (statusLower === "cancelled"){
        return `<div class="notice notice--info"><b>Cancelled</b></div>`;
      }

      return `<div class="notice notice--info">This request is not pending.</div>`;
    }
    function childNameFromId(childId){
      const c = childMap.get(String(childId || "").toLowerCase());
      return c ? (c.displayName || "Child") : "Child";
    }

    function renderActions(req){
      const requestId = getProp(req, "requestId", "RequestId");
      const type = normalizeType(getProp(req, "type", "Type"));

      // Default approve parameters (can be tweaked later)
      const moreTimeButtons = `
        <button class="btn" data-approve="15">+15</button>
        <button class="btn" data-approve="30">+30</button>
        <button class="btn" data-approve="60">+60</button>
      `;
      const durationButtons = `
        <button class="btn" data-duration="15">15m</button>
        <button class="btn" data-duration="30">30m</button>
        <button class="btn" data-duration="60">60m</button>
        <button class="btn" data-duration="120">120m</button>
      `;

      const approveControls = (type === "MoreTime")
        ? `<div class="btn-row">${moreTimeButtons}</div>`
        : `<div class="btn-row">${durationButtons}</div>`;

      return `
        <div class="kv" style="justify-content:flex-start; gap:10px; align-items:center; flex-wrap:wrap;">
          <span class="muted">Approve:</span>
          ${approveControls}
          <button class="btn btn--danger" data-deny="1">Deny</button>
          <input type="hidden" data-request-id value="${escapeHtml(String(requestId||""))}">
          <input type="hidden" data-request-type value="${escapeHtml(type)}">
        </div>
      `;
    }

    async function load(){
      const childId = childSel && childSel.value ? childSel.value : null;
      const status = statusSel && statusSel.value ? statusSel.value : null;
      results.innerHTML = `<div class="skeleton">Loading…</div>`;
      const res = await Safe0neApi.getRequests(childId, status, 200);
      if (!res.ok){
        results.innerHTML = `<div class="notice notice--danger">Could not load requests: ${escapeHtml(res.error || "unknown error")}</div>`;
        return;
      }

      const items = res.data || [];
      if (items.length === 0){
        results.innerHTML = `<div class="card"><h2>No requests</h2><p class="muted">Nothing matches your filters.</p></div>`;
        return;
      }

      results.innerHTML = items.map(req => {
        const requestId = getProp(req, "requestId", "RequestId");
        const childIdObj = getProp(req, "childId", "ChildId");
        const childIdVal = childIdObj && childIdObj.value ? childIdObj.value : childIdObj;
        const type = normalizeType(getProp(req, "type", "Type"));
        const statusTxt = normalizeStatus(getProp(req, "status", "Status"));
        const target = getProp(req, "target", "Target") || "";
        const reason = getProp(req, "reason", "Reason") || "";
        const createdAt = getProp(req, "createdAtUtc", "CreatedAtUtc");
        const decidedAt = getProp(req, "decidedAtUtc", "DecidedAtUtc");

        const isPending = String(statusTxt).toLowerCase() === "pending";

        return `
          <div class="card" data-req-card>
            <h2>${escapeHtml(childNameFromId(childIdVal))} — ${escapeHtml(type)}</h2>
            <p class="muted">Status: <b>${escapeHtml(statusTxt || "")}</b> · Requested: ${escapeHtml(fmtUtc(createdAt))}${decidedAt ? ` · Decided: ${escapeHtml(fmtUtc(decidedAt))}` : ""}</p>
            <div class="grid" style="grid-template-columns: 1fr 1fr; gap: 12px;">
              <div class="card" style="margin:0;">
                <h2 style="font-size:14px;">Target</h2>
                <p style="white-space:pre-wrap;">${escapeHtml(target || "(none)")}</p>
              </div>
              <div class="card" style="margin:0;">
                <h2 style="font-size:14px;">Reason</h2>
                <p style="white-space:pre-wrap;">${escapeHtml(reason || "(none)")}</p>
              </div>
            </div>
            ${isPending ? renderActions(req) : renderDecisionSummary(req)}
            <div class="muted" style="margin-top:10px;">Request ID: ${escapeHtml(String(requestId||""))}</div>
          </div>
        `;
      }).join("");

      // Wire events (event delegation)
      results.querySelectorAll("[data-req-card]").forEach(card => {
        card.addEventListener("click", async (ev) => {
          const t = ev.target;
          if (!(t instanceof HTMLElement)) return;

          const reqIdEl = card.querySelector("input[data-request-id]");
          const typeEl = card.querySelector("input[data-request-type]");
          const requestId = reqIdEl ? reqIdEl.value : "";
          const type = typeEl ? typeEl.value : "";
          if (!requestId) return;

          // Approve MoreTime
          if (t.matches("button[data-approve]")){
            const mins = parseInt(t.getAttribute("data-approve") || "0", 10);
            t.setAttribute("disabled","disabled");
            const dec = await Safe0neApi.decideRequest(requestId, { approve:true, extraMinutes: mins, decidedBy: "Parent" });
            await load();
            if (!dec.ok) alert(`Approve failed: ${dec.error || "unknown error"}`);
          }

          // Approve duration (UnblockApp/UnblockSite)
          if (t.matches("button[data-duration]")){
            const mins = parseInt(t.getAttribute("data-duration") || "0", 10);
            t.setAttribute("disabled","disabled");
            const dec = await Safe0neApi.decideRequest(requestId, { approve:true, durationMinutes: mins, decidedBy: "Parent" });
            await load();
            if (!dec.ok) alert(`Approve failed: ${dec.error || "unknown error"}`);
          }

          // Deny
          if (t.matches("button[data-deny]")){
            t.setAttribute("disabled","disabled");
            const dec = await Safe0neApi.decideRequest(requestId, { approve:false, decidedBy: "Parent" });
            await load();
            if (!dec.ok) alert(`Deny failed: ${dec.error || "unknown error"}`);
          }
        });
      });
    }

    refreshBtn.addEventListener("click", load);
    childSel.addEventListener("change", load);
    statusSel.addEventListener("change", load);
    await load();
  });

  return `
    ${pageHeader("Requests",
      "Review and approve child requests. Approvals create time-boxed grants (extra minutes or temporary unblocks).",
      null
    )}
    <div id="${containerId}"></div>
  `;
}


function renderWebCategoryRow(categoryId, label, map) {
  const current = (map && map[categoryId]) != null ? String(map[categoryId]) : "0";
  // actions: 0 Allow, 1 Alert, 2 Block
  return `
    <div class="row">
      <div class="cell">${label}</div>
      <div class="cell">
        <select data-web-cat="${categoryId}">
          <option value="0" ${current === "0" ? "selected" : ""}>Allow</option>
          <option value="1" ${current === "1" ? "selected" : ""}>Alert</option>
          <option value="2" ${current === "2" ? "selected" : ""}>Block</option>
        </select>
      </div>
    </div>
  `;
}

function renderChildProfile(route){
  // Prefer modular feature implementation when available.
  try {
    const mod = window.Safe0neChildren;
    if (mod && typeof mod.renderChildProfile === 'function' && mod.renderChildProfile !== renderChildProfile) {
      return mod.renderChildProfile(route);
    }
  } catch (_) { /* ignore */ }

  const childId = route.childId;
  const containerId = "child-profile";

  setTimeout(async () => {
    const root = document.getElementById(containerId);
    if (!root) return;

    root.innerHTML = `<div class="skeleton">Loading policy…</div>`;

    const [childrenRes, policyRes, effRes, statusRes, childReqsRes] = await Promise.all([
      Safe0neApi.getChildren(),
      Safe0neApi.getChildPolicy(childId),
      Safe0neApi.getEffectiveChildState(childId),
      Safe0neApi.getChildStatus(childId),
      Safe0neApi.getChildRequests(childId),
    ]);

    if (!childrenRes.ok){
      root.innerHTML = `<div class="notice notice--danger">Could not load child list: ${escapeHtml(childrenRes.error || "unknown error")}</div>`;
      return;
    }
    const child = (childrenRes.data || []).find(c => (c.id && c.id.value || "").toLowerCase() === String(childId).toLowerCase());
    const childName = child ? (child.displayName || "Child") : "Child";

    if (!policyRes.ok){
      root.innerHTML = `
        <div class="card">
          <h2>${escapeHtml(childName)}</h2>
          <p class="muted">Child ID: ${escapeHtml(childId)}</p>
          <div class="notice notice--danger">Policy not found.</div>
        </div>`;
      return;
    }

    const policy = policyRes.data;
    if (!policy){
      root.innerHTML = `<div class="card"><h2>${escapeHtml(childName)}</h2><p class="muted">Child ID: ${escapeHtml(childId)}</p><div class="notice notice--danger">Policy response was empty (data=null).</div></div>`;
      return;
    }
    const mode = policy.mode;
    const version = (policy.version && policy.version.value) ? policy.version.value : "?";
    const updatedAt = policy.updatedAtUtc ? new Date(policy.updatedAtUtc).toLocaleString() : "—";
    const updatedBy = policy.updatedBy || "—";
    const alwaysAllowed = !!policy.alwaysAllowed;
    const grantUntilUtc = policy.grantUntilUtc ? new Date(policy.grantUntilUtc) : null;
    const blockedProcessNames = Array.isArray(policy.blockedProcessNames) ? policy.blockedProcessNames : [];
    const blockedText = blockedProcessNames.join("\n");

    // K5/P7: apps & games
    const appsAllowListEnabled = !!policy.appsAllowListEnabled;
    const allowedProcessNames = Array.isArray(policy.allowedProcessNames) ? policy.allowedProcessNames : [];
    const allowedText = allowedProcessNames.join("\n");
    const perAppDailyLimits = Array.isArray(policy.perAppDailyLimits) ? policy.perAppDailyLimits : [];
    const perAppText = perAppDailyLimits.map(x => `${x.processName}=${x.limitMinutes}`).join("\n");

// K6/P8: web & content filtering
const webAdultBlockEnabled = !!policy.webAdultBlockEnabled;
const webSafeSearchEnabled = (() => {
  // Preferred (SSOT surface): policy.web.safeSearch.enabled
  try {
    if (policy && policy.web && policy.web.safeSearch && typeof policy.web.safeSearch.enabled === "boolean") return !!policy.web.safeSearch.enabled;
  } catch {}
  // Legacy (v1 UI payload): policy.webSafeSearchEnabled
  return !!policy.webSafeSearchEnabled;
})();

const webYouTubeRestrictedModeEnabled = (() => {
  // Preferred (SSOT surface): policy.web.safeSearch.youtubeRestrictedModeEnabled
  try {
    if (policy && policy.web && policy.web.safeSearch && typeof policy.web.safeSearch.youtubeRestrictedModeEnabled === "boolean")
      return !!policy.web.safeSearch.youtubeRestrictedModeEnabled;
  } catch {}
  // Legacy (v1 UI payload): policy.webYouTubeRestrictedModeEnabled
  return !!policy.webYouTubeRestrictedModeEnabled;
})();
const webCircumventionDetectionEnabled = (policy.webCircumventionDetectionEnabled == null) ? true : !!policy.webCircumventionDetectionEnabled;
const webAllowedDomains = Array.isArray(policy.webAllowedDomains) ? policy.webAllowedDomains : [];
const webBlockedDomains = Array.isArray(policy.webBlockedDomains) ? policy.webBlockedDomains : [];
const webAllowedText = webAllowedDomains.join("\n");
const webBlockedText = webBlockedDomains.join("\n");
const webCategoryRules = Array.isArray(policy.webCategoryRules) ? policy.webCategoryRules : [];
const webRuleMap = {};
webCategoryRules.forEach(r => {
  if (r && r.category != null && r.action != null) webRuleMap[String(r.category)] = String(r.action);
});


    const eff = effRes.ok ? effRes.data : null;
    const effMode = eff ? eff.effectiveMode : mode;
    const effReason = eff ? eff.reasonCode : "unknown";
    const effAt = eff ? new Date(eff.evaluatedAtUtc).toLocaleString() : "—";

    // Active grants (K8/P11): visible to parents for debugging/clarity
    const activeGrants = (eff && Array.isArray(eff.activeGrants)) ? eff.activeGrants : [];

    // Recent decisions: use the child's request list (includes approved/denied with timestamps).
    const childRequests = (childReqsRes && childReqsRes.ok && Array.isArray(childReqsRes.data)) ? childReqsRes.data : [];
    const cutoffMs = Date.now() - (24 * 60 * 60 * 1000);
    const recentDecisions = childRequests
      .filter(r => r && r.decidedAtUtc && (new Date(r.decidedAtUtc).getTime() >= cutoffMs))
      .sort((a,b) => new Date(b.decidedAtUtc).getTime() - new Date(a.decidedAtUtc).getTime())
      .slice(0, 20);

    function reqTypeLabel(t){
      const v = (t == null) ? "" : String(t);
      if (v === "0" || v.toLowerCase() === "moretime") return "More time";
      if (v === "1" || v.toLowerCase() === "unblockapp") return "Unblock app";
      if (v === "2" || v.toLowerCase() === "unblocksite") return "Unblock site";
      return v;
    }
    function reqStatusLabel(s){
      const v = (s == null) ? "" : String(s);
      if (v === "0" || v.toLowerCase() === "pending") return "Pending";
      if (v === "1" || v.toLowerCase() === "approved") return "Approved";
      if (v === "2" || v.toLowerCase() === "denied") return "Denied";
      if (v === "3" || v.toLowerCase() === "cancelled") return "Cancelled";
      return v;
    }
    function findGrantForRequest(requestId){
      if (!requestId) return null;
      const rid = String(requestId).toLowerCase();
      return activeGrants.find(g => g && g.sourceRequestId && String(g.sourceRequestId).toLowerCase() === rid) || null;
    }
    function decisionSummary(r){
      const d = r && r.decision ? r.decision : null;
      const g = findGrantForRequest(r && r.requestId);
      const bits = [];
      if (d && d.extraMinutes != null) bits.push(`+${Number(d.extraMinutes)}m`);
      if (d && d.durationMinutes != null) bits.push(`${Number(d.durationMinutes)}m`);
      if (g && (g.expiresAtUtc || g.expiresUtc || g.expiresAt)){
        const exp = fmtUtcMaybe(g.expiresAtUtc || g.expiresUtc || g.expiresAt);
        bits.push(`until ${exp}`);
      }
      return bits.length ? (" · " + bits.join(" · ")) : "";
    }
    function fmtUtcMaybe(s){
      try{
        if (!s) return "—";
        const d = new Date(s);
        if (String(d) === "Invalid Date") return String(s);
        return d.toLocaleString();
      }catch{
        return String(s || "—");
      }
    }
    function grantLabel(g){
      const type = (g && (g.type || g.grantType || g.kind || g.scope || g.requestType)) ? String(g.type || g.grantType || g.kind || g.scope || g.requestType) : "grant";
      const target = (g && g.target != null) ? String(g.target) : "";
      const extra = (g && (g.extraMinutes != null || g.minutes != null)) ? Number(g.extraMinutes ?? g.minutes) : null;
      const expires = fmtUtcMaybe(g && (g.expiresAtUtc || g.expiresUtc || g.expiresAt));
      const bits = [];
      bits.push(type);
      if (target) bits.push(target);
      if (extra != null && Number.isFinite(extra)) bits.push(`+${extra}m`);
      bits.push(`until ${expires}`);
      return bits.join(" · ");
    }
    const status = statusRes && statusRes.ok ? statusRes.data : null;
    const lastSeen = status && status.lastSeenUtc ? new Date(status.lastSeenUtc).toLocaleString() : "—";
    const agentVer = status && status.agentVersion ? status.agentVersion : "—";
    const deviceName = status && status.deviceName ? status.deviceName : "—";
    const online = status && status.lastSeenUtc ? ((Date.now() - new Date(status.lastSeenUtc).getTime()) <= 20000) : false;

    // K4/P6: screen time + schedules
    const dailyLimitMins = (policy.dailyScreenTimeLimitMinutes != null && Number.isFinite(Number(policy.dailyScreenTimeLimitMinutes)))
      ? String(policy.dailyScreenTimeLimitMinutes)
      : "";

    function normWindow(w, defStart, defEnd){
      if (!w) return { enabled: false, startLocal: defStart, endLocal: defEnd };
      return {
        enabled: !!w.enabled,
        startLocal: w.startLocal || defStart,
        endLocal: w.endLocal || defEnd
      };
    }

    const bedtimeW = normWindow(policy.bedtimeWindow, "22:00", "07:00");
    const schoolW = normWindow(policy.schoolWindow, "09:00", "15:00");
    const homeworkW = normWindow(policy.homeworkWindow, "16:00", "18:00");

    function renderWindowRow(key, label, w){
      const enabledId = `win-${key}-enabled`;
      const startId = `win-${key}-start`;
      const endId = `win-${key}-end`;
      return `
        <div style="display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin-top:10px;">
          <label class="check" style="min-width:140px;">
            <input id="${enabledId}" type="checkbox" ${w.enabled ? "checked" : ""}/>
            <span><strong>${escapeHtml(label)}</strong></span>
          </label>
          <div style="display:flex;gap:8px;align-items:center;">
            <span class="hint">Start</span>
            <input id="${startId}" class="input" type="time" value="${escapeHtml(w.startLocal)}" />
          </div>
          <div style="display:flex;gap:8px;align-items:center;">
            <span class="hint">End</span>
            <input id="${endId}" class="input" type="time" value="${escapeHtml(w.endLocal)}" />
          </div>
        </div>
      `;
    }

    root.innerHTML = `
      <div class="card">
        <h2>${escapeHtml(childName)}</h2>
        <p class="lead">Effective state is computed by precedence: <span class="mono">always_allowed</span> → <span class="mono">grant</span> → <span class="mono">mode</span> → <span class="mono">schedules/budgets</span>.</p>
        <div class="notice ${online ? "notice--success" : "notice--warning"}">
          <strong>Child device</strong><br/>
          Status: ${online ? "Online" : "Offline"}<br/>
          Last seen: ${escapeHtml(lastSeen)}<br/>
          Device: ${escapeHtml(deviceName)}<br/>
          Agent: ${escapeHtml(agentVer)}
        </div>

        <div class="notice">
          <strong>Screen time (today)</strong><br/>
          Used: ${escapeHtml(status && status.screenTimeUsedMinutes != null ? String(status.screenTimeUsedMinutes) + " min" : "—")}<br/>
          Remaining: ${escapeHtml(status && status.screenTimeRemainingMinutes != null ? String(status.screenTimeRemainingMinutes) + " min" : "—")}<br/>
          ${status && status.screenTimeBudgetDepleted ? "<span class=\"mono\">Time's up</span><br/>" : ""}
          ${status && status.activeSchedule ? "Active schedule: <span class=\"mono\">" + escapeHtml(String(status.activeSchedule)) + "</span>" : ""}
        </div>



        <div class="card" style="margin-top:14px;">
          <div style="font-weight:700;margin-bottom:6px;">Pair this child device</div>
          <div class="hint">Generate a one-time code and set <span class="mono">SAFEONE_PAIR_CODE</span> on the child device to claim it.</div>
          <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:center;margin-top:10px;">
            <button class="btn" type="button" id="pair-start">Generate pairing code</button>
            <span id="pair-code" class="mono"></span>
            <span id="pair-expiry" class="hint"></span>
          </div>
          <div id="paired-devices" style="margin-top:12px;"></div>
        </div>


        <div class="card" style="margin-top:14px;">
          <div style="font-weight:700;margin-bottom:6px;">Commands (agent channel)</div>
          <div class="hint">Send a command to the child agent. Today it only logs + acknowledges (enforcement UI later).</div>
          <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:center;margin-top:10px;">
            <button class="btn" type="button" id="cmd-notice">Send notice</button>
            <button class="btn" type="button" id="cmd-sync">Force sync</button>
            <button class="btn" type="button" id="cmd-ping">Ping</button>
          </div>
          <div id="cmd-result" style="margin-top:12px;"></div>
          <div id="cmd-list" style="margin-top:12px;"></div>
        </div>

        <div class="notice">
          <div class="kv">
            <span>Effective mode</span><span><strong>${escapeHtml(String(effMode))}</strong></span>
          </div>
          <div class="kv">
            <span>Reason</span><span class="mono">${escapeHtml(String(effReason))}</span>
          </div>
          <div class="kv">
            <span>Evaluated</span><span>${escapeHtml(String(effAt))}</span>
          </div>
          <div style="margin-top:10px;">
            <div class="hint" style="margin-bottom:6px;">Active grants</div>
            ${
              (activeGrants && activeGrants.length)
                ? `<ul class="list">` + activeGrants.slice(0,8).map(g => `<li>${escapeHtml(grantLabel(g))}</li>`).join("") + `</ul>`
                : `<div class="muted">No active grants.</div>`
            }

            <div style="margin-top:10px;">
              <div class="hint" style="margin-bottom:6px;">Recent decisions (last 24h)</div>
              ${
                (recentDecisions && recentDecisions.length)
                  ? `<ul class="list">` + recentDecisions.map(r => {
                      const when = fmtUtcMaybe(r.decidedAtUtc);
                      const type = reqTypeLabel(r.type);
                      const target = (r.target || "");
                      const st = reqStatusLabel(r.status);
                      const sum = decisionSummary(r);
                      return `<li>${escapeHtml(when)} · <span class="mono">${escapeHtml(type)}</span>${target ? " · " + escapeHtml(target) : ""} · <strong>${escapeHtml(st)}</strong>${escapeHtml(sum)}</li>`;
                    }).join("") + `</ul>`
                  : `<div class="muted">No decisions in the last 24 hours.</div>`
              }
            </div>
          </div>
        </div>

        <div class="form" style="margin-top:14px;">
          <label class="label" for="mode-select">Safety mode</label>
          <select id="mode-select" class="select">
            ${["Open","Homework","Bedtime","Lockdown"].map(m => `<option value="${m}" ${m===mode ? "selected":""}>${m}</option>`).join("")}
          </select>

          <div style="margin-top:14px;">
            <label class="check">
              <input id="always-allowed" type="checkbox" ${alwaysAllowed ? "checked" : ""}/>
              <span><strong>Always Allowed</strong> (override)</span>
            </label>
            <div class="hint">When enabled, effective mode is forced to <span class="mono">Open</span> regardless of other settings.</div>
          </div>

          <div style="margin-top:14px;">
            <div class="kv">
              <span>Grant until</span><span>${escapeHtml(grantUntilUtc ? grantUntilUtc.toLocaleString() : "—")}</span>
            </div>
            <div style="margin-top:10px;display:flex;gap:10px;flex-wrap:wrap;">
              <button class="btn" type="button" id="grant-15">Grant 15 min</button>
              <button class="btn" type="button" id="clear-grant">Clear grant</button>
            </div>
            <div class="hint">A grant temporarily overrides the configured mode (useful for requests / exceptions).</div>
          </div>

          
          <div style="margin-top:14px;">
            <div style="font-weight:700;margin-bottom:6px;">Blocked apps (Lockdown)</div>
            <div class="hint">One process name per line. Example: <span class="mono">notepad</span> (no <span class="mono">.exe</span>).</div>
            <textarea id="blocked-procs" class="textarea" rows="5" spellcheck="false" placeholder="notepad\nchrome">${escapeHtml(blockedText)}</textarea>
          </div>

          <div style="margin-top:14px;">
            <div style="font-weight:700;margin-bottom:6px;">Apps & games</div>
            <div class="hint">Windows-first (best effort). Use executable names like <span class="mono">chrome.exe</span>.</div>
            <label class="check" style="margin-top:8px;">
              <input id="apps-allowlist" type="checkbox" ${appsAllowListEnabled ? "checked" : ""}/>
              <span><strong>Only allow listed apps</strong> (advanced)</span>
            </label>
            <div style="margin-top:10px;display:grid;grid-template-columns:1fr 1fr;gap:12px;">
              <div>
                <div class="hint" style="margin-bottom:6px;">Allowed apps (one per line)</div>
                <textarea id="allowed-procs" class="textarea" rows="5" spellcheck="false" placeholder="chrome.exe">${escapeHtml(allowedText)}</textarea>
              </div>
              <div>
                <div class="hint" style="margin-bottom:6px;">Per-app daily limits (process.exe=minutes)</div>
                <textarea id="perapp-limits" class="textarea" rows="5" spellcheck="false" placeholder="chrome.exe=30">${escapeHtml(perAppText)}</textarea>
              </div>
            </div>
            <div class="hint" style="margin-top:8px;">Allow-list is enforced only for the foreground app to reduce OS risk. Per-app limits count foreground time and ignore idle.</div>
          </div>

          <div style="margin-top:14px;">
            <div style="font-weight:700;margin-bottom:6px;">Screen time</div>
            <div class="hint">Daily limit in minutes. Leave blank for no limit.</div>
            <input id="daily-limit-mins" class="input" type="number" min="0" step="1" placeholder="e.g. 120" value="${escapeHtml(dailyLimitMins)}" />
          </div>

          <div style="margin-top:14px;">
            <div style="font-weight:700;margin-bottom:6px;">Schedules</div>
            <div class="hint">Times are local to this computer (prototype). Cross-midnight windows are supported.</div>

            ${renderWindowRow("bedtime", "Bedtime", bedtimeW)}
            ${renderWindowRow("school", "School", schoolW)}
            ${renderWindowRow("homework", "Homework", homeworkW)}
          </div>


<div style="margin-top:14px;">
  <div style="font-weight:700;margin-bottom:6px;">Web & content filtering</div>
  <div class="hint">Windows-first (best effort). Explicit domains are blocked via hosts-file entries. Category rules apply to a small built-in domain list (prototype).</div>

  <label class="check" style="margin-top:8px;">
    <input id="web-adult" type="checkbox" ${webAdultBlockEnabled ? "checked" : ""}/>
    <span><strong>Block adult/porn</strong> (best effort)</span>
  </label>

  <label class="check" style="margin-top:6px;">
    <input id="web-safesearch" type="checkbox" ${webSafeSearchEnabled ? "checked" : ""}/>
    <span>Try to enforce SafeSearch / Restricted Mode (best effort)</span>
  </label>

  <label class="check" style="margin-top:6px;">
    <input id="web-youtube-restricted" type="checkbox" ${webYouTubeRestrictedModeEnabled ? "checked" : ""}/>
    <span>Enforce <strong>YouTube Restricted Mode</strong> (best effort)</span>
  </label>

  <label class="check" style="margin-top:6px;">
    <input id="web-circ" type="checkbox" ${webCircumventionDetectionEnabled ? "checked" : ""}/>
    <span>Detect VPN/proxy/private DNS circumvention (best effort)</span>
  </label>

  <div style="margin-top:10px;display:grid;grid-template-columns:1fr 1fr;gap:12px;">
    <div>
      <div class="hint" style="margin-bottom:6px;">Allowed domains (one per line)</div>
      <textarea id="web-allow-domains" class="textarea" rows="5" spellcheck="false" placeholder="khanacademy.org\nbbc.co.uk">${escapeHtml(webAllowedText)}</textarea>
    </div>
    <div>
      <div class="hint" style="margin-bottom:6px;">Blocked domains (one per line)</div>
      <textarea id="web-block-domains" class="textarea" rows="5" spellcheck="false" placeholder="example.com">${escapeHtml(webBlockedText)}</textarea>
    </div>
  </div>

  <div style="margin-top:10px;display:grid;grid-template-columns:1fr 1fr;gap:12px;">
    <div>
      <div class="hint" style="margin-bottom:6px;">Category rules (Allow / Alert / Block)</div>
      <div style="display:grid;grid-template-columns:1fr 140px;gap:8px;align-items:center;">
        <div>Adult</div>
        <select id="webcat-1" class="select">
          <option value="0" ${(webRuleMap["1"]||"0")==="0" ? "selected" : ""}>Allow</option>
          <option value="1" ${(webRuleMap["1"]||"0")==="1" ? "selected" : ""}>Alert</option>
          <option value="2" ${(webRuleMap["1"]||"0")==="2" ? "selected" : ""}>Block</option>
        </select>

        <div>Social</div>
        <select id="webcat-2" class="select">
          <option value="0" ${(webRuleMap["2"]||"0")==="0" ? "selected" : ""}>Allow</option>
          <option value="1" ${(webRuleMap["2"]||"0")==="1" ? "selected" : ""}>Alert</option>
          <option value="2" ${(webRuleMap["2"]||"0")==="2" ? "selected" : ""}>Block</option>
        </select>

        <div>Games</div>
        <select id="webcat-3" class="select">
          <option value="0" ${(webRuleMap["3"]||"0")==="0" ? "selected" : ""}>Allow</option>
          <option value="1" ${(webRuleMap["3"]||"0")==="1" ? "selected" : ""}>Alert</option>
          <option value="2" ${(webRuleMap["3"]||"0")==="2" ? "selected" : ""}>Block</option>
        </select>

        <div>Streaming</div>
        <select id="webcat-4" class="select">
          <option value="0" ${(webRuleMap["4"]||"0")==="0" ? "selected" : ""}>Allow</option>
          <option value="1" ${(webRuleMap["4"]||"0")==="1" ? "selected" : ""}>Alert</option>
          <option value="2" ${(webRuleMap["4"]||"0")==="2" ? "selected" : ""}>Block</option>
        </select>

        <div>Shopping</div>
        <select id="webcat-5" class="select">
          <option value="0" ${(webRuleMap["5"]||"0")==="0" ? "selected" : ""}>Allow</option>
          <option value="1" ${(webRuleMap["5"]||"0")==="1" ? "selected" : ""}>Alert</option>
          <option value="2" ${(webRuleMap["5"]||"0")==="2" ? "selected" : ""}>Block</option>
        </select>
      </div>
    </div>
    <div>
      <div class="hint" style="margin-bottom:6px;">Device signals (last heartbeat)</div>
      <div class="card" style="padding:10px;">
        <div>VPN suspected: <strong>${status && status.circumvention ? (status.circumvention.vpnSuspected ? "Yes" : "No") : "—"}</strong></div>
        <div>Proxy enabled: <strong>${status && status.circumvention ? (status.circumvention.proxyEnabled ? "Yes" : "No") : "—"}</strong></div>
        <div>Public DNS detected: <strong>${status && status.circumvention ? (status.circumvention.publicDnsDetected ? "Yes" : "No") : "—"}</strong></div>
        <div>Hosts write failed: <strong>${status && status.circumvention ? (status.circumvention.hostsWriteFailed ? "Yes" : "No") : "—"}</strong></div>
      </div>
    </div>
  </div>
</div>

<div class="kv" style="margin-top:14px;">
            <span>Policy version</span><span class="mono" id="policy-version">${escapeHtml(String(version))}</span>
          </div>
          <div class="kv">
            <span>Last updated</span><span id="policy-updated-at">${escapeHtml(String(updatedAt))}</span>
          </div>
          <div class="kv">
            <span>Updated by</span><span id="policy-updated-by">${escapeHtml(String(updatedBy))}</span>
          </div>

          <div style="margin-top:16px;display:flex;gap:10px;flex-wrap:wrap;">
            <a class="btn" href="#/children">Back</a>
            <button class="btn btn--primary" type="button" id="save-mode">Save</button>
          </div>

          <div id="policy-validate" style="margin-top:10px;"></div>

          <div id="save-result" style="margin-top:12px;"></div>
        </div>
      </div>
    `;

    const saveBtn = document.getElementById("save-mode");
    const modeSelect = document.getElementById("mode-select");
    const alwaysAllowedEl = document.getElementById("always-allowed");
    const grant15 = document.getElementById("grant-15");
    const clearGrant = document.getElementById("clear-grant");
    const validateBox = document.getElementById("policy-validate");
    const result = document.getElementById("save-result");
    const blockedEl = document.getElementById("blocked-procs");
    const appsAllowListEl = document.getElementById("apps-allowlist");
    const allowedEl = document.getElementById("allowed-procs");
    const perAppEl = document.getElementById("perapp-limits");
    const dailyLimitEl = document.getElementById("daily-limit-mins");

    const wBedEnabled = document.getElementById("win-bedtime-enabled");
    const wBedStart = document.getElementById("win-bedtime-start");
    const wBedEnd = document.getElementById("win-bedtime-end");

    const wSchEnabled = document.getElementById("win-school-enabled");
    const wSchStart = document.getElementById("win-school-start");
    const wSchEnd = document.getElementById("win-school-end");

    const wHwEnabled = document.getElementById("win-homework-enabled");
    const wHwStart = document.getElementById("win-homework-start");
    const wHwEnd = document.getElementById("win-homework-end");

    const webAdultEl = document.getElementById("web-adult");
    const webSafeEl = document.getElementById("web-safesearch");
    const webYouTubeEl = document.getElementById("web-youtube-restricted");
    const webCircEl = document.getElementById("web-circ");
    const webAllowEl = document.getElementById("web-allow-domains");
    const webBlockEl = document.getElementById("web-block-domains");


    const pairBtn = document.getElementById("pair-start");
    const pairCodeEl = document.getElementById("pair-code");
    const pairExpiryEl = document.getElementById("pair-expiry");
    const devicesEl = document.getElementById("paired-devices");

    const cmdNotice = document.getElementById("cmd-notice");
    const cmdSync = document.getElementById("cmd-sync");
    const cmdPing = document.getElementById("cmd-ping");
    const cmdResult = document.getElementById("cmd-result");
    const cmdList = document.getElementById("cmd-list");

    function normDomain(line){
      let s = String(line || "").trim();
      if (!s) return null;
      // Allow user to paste full URLs; store only host-like tokens.
      try{
        if (s.includes("://")){
          const u = new URL(s);
          s = u.hostname || "";
        }
      }catch{
        // fall through
      }
      s = s.toLowerCase();
      s = s.replace(/^\.+/, "").replace(/\.+$/, "");
      // Basic host validation (intentionally permissive; no wildcards for v1).
      if (!/^[a-z0-9.-]+$/.test(s)) return null;
      if (!s.includes(".")) return null;
      return s;
    }

    function isTimeHHMM(v){
      const m = /^([0-1]\d|2[0-3]):([0-5]\d)$/.exec(String(v||""));
      return !!m;
    }

    function collectValidation(){
      const errors = [];
      const warnings = [];

      // Daily limit guardrails.
      const rawLimit = String(dailyLimitEl ? dailyLimitEl.value : "").trim();
      if (rawLimit){
        const n = Number(rawLimit);
        if (!Number.isFinite(n) || !Number.isInteger(n)){
          errors.push("Daily limit must be a whole number (minutes).");
        }else if (n < 0 || n > 1440){
          errors.push("Daily limit must be between 0 and 1440 minutes.");
        }
      }

      // Per-app limits guardrails.
      if (perAppEl){
        const lines = String(perAppEl.value || "").split(/\r?\n/).map(x => x.trim()).filter(x => x.length>0);
        let badFmt = 0;
        for (const line of lines){
          const parts = line.split('=');
          if (parts.length !== 2){ badFmt++; continue; }
          const proc = parts[0].trim();
          const minsRaw = parts[1].trim();
          const mins = Number(minsRaw);
          if (!proc || !Number.isFinite(mins) || !Number.isInteger(mins)) { badFmt++; continue; }
          if (mins < 0) errors.push("Per-app limits cannot be negative.");
          if (mins > 1440) warnings.push("Per-app limit over 1440 minutes is unusual.");
        }
        if (badFmt) warnings.push(`Per-app daily limits: ${badFmt} invalid entr${badFmt===1?"y":"ies"} will be ignored.`);
      }

      function validateWindow(label, enEl, stEl, edEl){
        const enabled = !!(enEl && enEl.checked);
        const st = (stEl && stEl.value) ? stEl.value : "";
        const ed = (edEl && edEl.value) ? edEl.value : "";
        if (!enabled) return;
        if (!isTimeHHMM(st) || !isTimeHHMM(ed)){
          errors.push(`${label} window must use HH:MM times.`);
          return;
        }
        if (st === ed){
          errors.push(`${label} window start and end cannot be the same.`);
        }
      }

      validateWindow("Bedtime", wBedEnabled, wBedStart, wBedEnd);
      validateWindow("School", wSchEnabled, wSchStart, wSchEnd);
      validateWindow("Homework", wHwEnabled, wHwStart, wHwEnd);

      // Domains: warn (don’t block) if user has invalid lines; they will be dropped.
      function domainWarnings(textareaEl, label){
        if (!textareaEl) return;
        const lines = String(textareaEl.value || "").split(/\r?\n/).map(x => x.trim()).filter(x => x.length>0);
        const bad = lines.filter(x => normDomain(x) == null);
        if (bad.length){
          warnings.push(`${label}: ${bad.length} invalid entr${bad.length===1?"y":"ies"} will be ignored.`);
        }
      }
      domainWarnings(webAllowEl, "Allowed domains");
      domainWarnings(webBlockEl, "Blocked domains");

      return { errors, warnings };
    }

    function renderValidation(){
      if (!validateBox) return true;
      const v = collectValidation();
      const errs = v.errors || [];
      const warns = v.warnings || [];
      const ok = errs.length === 0;
      if (errs.length === 0 && warns.length === 0){
        validateBox.innerHTML = "";
        return ok;
      }
      let html = "";
      if (errs.length){
        html += `<div class="notice notice--danger"><strong>Fix before saving</strong><ul class="list">${errs.map(x=>`<li>${escapeHtml(x)}</li>`).join("")}</ul></div>`;
      }
      if (warns.length){
        html += `<div class="notice notice--warning"><strong>Heads up</strong><ul class="list">${warns.map(x=>`<li>${escapeHtml(x)}</li>`).join("")}</ul></div>`;
      }
      validateBox.innerHTML = html;
      return ok;
    }

    function wireValidation(){
      const inputs = [
        modeSelect, alwaysAllowedEl, blockedEl, appsAllowListEl, allowedEl, perAppEl, dailyLimitEl,
        wBedEnabled, wBedStart, wBedEnd,
        wSchEnabled, wSchStart, wSchEnd,
        wHwEnabled, wHwStart, wHwEnd,
        webAdultEl, webSafeEl, webYouTubeEl, webCircEl, webAllowEl, webBlockEl,
        document.getElementById("webcat-1"), document.getElementById("webcat-2"), document.getElementById("webcat-3"), document.getElementById("webcat-4"), document.getElementById("webcat-5"),
      ].filter(Boolean);
      const onChange = () => {
        const ok = renderValidation();
        if (saveBtn) saveBtn.disabled = !ok;
      };
      inputs.forEach(el => el.addEventListener("input", onChange));
      inputs.forEach(el => el.addEventListener("change", onChange));
      // Initial state
      onChange();
    }

    async function refreshCommands(){
      if (!cmdList) return;
      const res = await Safe0neApi.getChildCommands(childId, 10);
      if (!res.ok){
        cmdList.innerHTML = '<div class="hint">Could not load commands.</div>';
        return;
      }
      const list = res.data || [];
      if (list.length === 0){
        cmdList.innerHTML = '<div class="hint">No commands yet.</div>';
        return;
      }
      let rows = '<div class="table">';
      rows += '<div class="tr tr--head"><div>Time</div><div>Type</div><div>Status</div></div>';
      for (const c of list){
        const t = c.createdAtUtc ? new Date(c.createdAtUtc).toLocaleString() : '—';
        const st = c.acked ? 'Acked' : 'Pending';
        rows += `<div class="tr"><div>${escapeHtml(t)}</div><div class="mono">${escapeHtml(c.type)}</div><div>${escapeHtml(st)}</div></div>`;
      }
      rows += '</div>';
      cmdList.innerHTML = rows;
    }

    async function sendCommand(type, payloadJson){
      if (!cmdResult) return;
      cmdResult.innerHTML = '';
      const res = await Safe0neApi.sendChildCommand(childId, { type: type, payloadJson: payloadJson, expiresInMinutes: 30 });
      if (!res.ok){
        cmdResult.innerHTML = `<div class="notice notice--danger">Command failed: ${escapeHtml(res.error || "unknown error")}</div>`;
        return;
      }
      cmdResult.innerHTML = `<div class="notice notice--success">Sent: <span class="mono">${escapeHtml(type)}</span></div>`;
      await refreshCommands();
    }



    async function refreshDevices(){
      if (!devicesEl) return;
      const devRes = await Safe0neApi.getChildDevices(childId);
      if (!devRes.ok){
        devicesEl.innerHTML = '<div class="hint">Could not load devices.</div>';
        return;
      }
      const list = devRes.data || [];
      if (list.length === 0){
        devicesEl.innerHTML = '<div class="hint">No paired devices yet.</div>';
        return;
      }
      let rows = '';
      for (const d of list){
        rows += '<div class="kv">' +
          '<span>' + escapeHtml(d.deviceName || 'Device') + '</span>' +
          '<span class="mono">' + escapeHtml(d.deviceId || '') + '</span>' +
        '</div>';
      }
      devicesEl.innerHTML = '<div class="notice"><strong>Paired devices</strong>' + rows + '</div>';
    }

    if (pairBtn){
      pairBtn.addEventListener('click', async () => {
        pairBtn.disabled = true;
        if (pairCodeEl) pairCodeEl.textContent = '';
        if (pairExpiryEl) pairExpiryEl.textContent = '';
        const res = await Safe0neApi.startPairing(childId);
        pairBtn.disabled = false;
        if (!res.ok){
          if (devicesEl) devicesEl.innerHTML = '<div class="notice notice--danger">Pairing failed: ' + escapeHtml(res.error || 'unknown error') + '</div>';
          return;
        }
        if (pairCodeEl) pairCodeEl.textContent = String(res.data.pairingCode || '');
        if (pairExpiryEl && res.data.expiresAtUtc) pairExpiryEl.textContent = 'expires ' + new Date(res.data.expiresAtUtc).toLocaleTimeString();
        await refreshDevices();
      });
    }

    // Initial render: show current paired devices + latest commands.
    await refreshDevices();
    await refreshCommands();

    if (cmdNotice){
      cmdNotice.addEventListener("click", async () => {
        const payload = JSON.stringify({ title: "Safe0ne", message: "This is a test notice from Parent.", severity: "info" });
        await sendCommand("notice", payload);
      });
    }
    if (cmdSync){
      cmdSync.addEventListener("click", async () => {
        await sendCommand("sync", null);
      });
    }
    if (cmdPing){
      cmdPing.addEventListener("click", async () => {
        await sendCommand("ping", null);
      });
    }

    async function refreshEffective(){
      const eff2 = await Safe0neApi.getEffectiveChildState(childId);
      if (!eff2.ok) return;
      const e = eff2.data;
      const modeEl = root.querySelector(".notice .kv:nth-child(1) span:nth-child(2) strong");
      const reasonEl = root.querySelector(".notice .kv:nth-child(2) span:nth-child(2)");
      const atEl = root.querySelector(".notice .kv:nth-child(3) span:nth-child(2)");
      if (modeEl) modeEl.textContent = String(e.effectiveMode);
      if (reasonEl) reasonEl.textContent = String(e.reasonCode);
      if (atEl) atEl.textContent = new Date(e.evaluatedAtUtc).toLocaleString();
    }

    function applyPolicyMeta(p){
      document.getElementById("policy-version").textContent = String((p.version && p.version.value) ? p.version.value : "?");
      document.getElementById("policy-updated-at").textContent = p.updatedAtUtc ? new Date(p.updatedAtUtc).toLocaleString() : "—";
      document.getElementById("policy-updated-by").textContent = p.updatedBy || "—";

      // Grant until line (by label)
      const kvs = Array.from(root.querySelectorAll(".kv"));
      const grantKv = kvs.find(k => (k.textContent || "").toLowerCase().includes("grant until"));
      if (grantKv){
        const v = grantKv.querySelector("span:nth-child(2)");
        if (v) v.textContent = p.grantUntilUtc ? new Date(p.grantUntilUtc).toLocaleString() : "—";
      }

      if (blockedEl && Array.isArray(p.blockedProcessNames)){
        blockedEl.value = p.blockedProcessNames.join("\n");
      }

      if (appsAllowListEl) appsAllowListEl.checked = !!p.appsAllowListEnabled;
      if (allowedEl && Array.isArray(p.allowedProcessNames)) allowedEl.value = p.allowedProcessNames.join('\n');
      if (perAppEl && Array.isArray(p.perAppDailyLimits)) perAppEl.value = p.perAppDailyLimits.map(x => `${x.processName}=${x.limitMinutes}`).join('\n');

      if (dailyLimitEl){
        dailyLimitEl.value = (p.dailyScreenTimeLimitMinutes != null) ? String(p.dailyScreenTimeLimitMinutes) : "";
      }

      function applyWin(prefix, w, defStart, defEnd){
        const en = document.getElementById(`win-${prefix}-enabled`);
        const st = document.getElementById(`win-${prefix}-start`);
        const ed = document.getElementById(`win-${prefix}-end`);
        const enabled = !!(w && w.enabled);
        if (en) en.checked = enabled;
        if (st) st.value = (w && w.startLocal) ? w.startLocal : defStart;
        if (ed) ed.value = (w && w.endLocal) ? w.endLocal : defEnd;
      }

      applyWin("bedtime", p.bedtimeWindow, "22:00", "07:00");
      applyWin("school", p.schoolWindow, "09:00", "15:00");
      applyWin("homework", p.homeworkWindow, "16:00", "18:00");
    }

    async function submitPolicy(extra){
      result.innerHTML = "";

      // Guardrails: block save only on hard validation errors.
      if (!renderValidation()){
        result.innerHTML = `<div class="notice notice--danger">Not saved. Fix the highlighted issues and try again.</div>`;
        return;
      }

      saveBtn.disabled = true;
      grant15.disabled = true;
      clearGrant.disabled = true;

      function parseIntOrNull(x){
        const t = String(x || "").trim();
        if (!t) return null;
        const n = parseInt(t, 10);
        return Number.isFinite(n) ? n : null;
      }


      function parsePerAppLimits(raw){
        const out=[];
        String(raw||'').split(/\r?\n/).map(x=>x.trim()).filter(x=>x.length>0).forEach(line=>{
          const parts=line.split('=');
          if (parts.length!==2) return;
          const processName=parts[0].trim();
          const limitMinutes=parseInt(parts[1].trim(),10);
          if (!processName || !Number.isFinite(limitMinutes)) return;
          out.push({processName, limitMinutes});
        });
        return out;
      }
      function windowPayload(enEl, stEl, edEl){
        return {
          enabled: !!(enEl && enEl.checked),
          startLocal: (stEl && stEl.value) ? stEl.value : "00:00",
          endLocal: (edEl && edEl.value) ? edEl.value : "00:00"
        };
      

function buildWebCategoryRules(){
  const cats=[1,2,3,4,5];
  const out=[];
  cats.forEach(c=>{
    const el=document.getElementById(`webcat-${c}`);
    if (!el) return;
    const act=parseInt(el.value,10);
    if (!Number.isFinite(act)) return;
    out.push({category:c, action:act});
  });
  return out;
}
}

      const payload = Object.assign({
        mode: modeSelect.value,
        updatedBy: "parent",
        alwaysAllowed: !!alwaysAllowedEl.checked,
        blockedProcessNames: blockedEl ? blockedEl.value.split(/\r?\n/).map(x => x.trim()).filter(x => x.length>0) : [],
        appsAllowListEnabled: appsAllowListEl ? !!appsAllowListEl.checked : false,
        allowedProcessNames: allowedEl ? allowedEl.value.split(/\r?\n/).map(x => x.trim()).filter(x => x.length>0) : [],
        perAppDailyLimits: parsePerAppLimits(perAppEl ? perAppEl.value : ""),
        dailyScreenTimeLimitMinutes: parseIntOrNull(dailyLimitEl ? dailyLimitEl.value : ""),
        bedtimeWindow: windowPayload(wBedEnabled, wBedStart, wBedEnd),
        schoolWindow: windowPayload(wSchEnabled, wSchStart, wSchEnd),
        homeworkWindow: windowPayload(wHwEnabled, wHwStart, wHwEnd),
        webAdultBlockEnabled: webAdultEl ? !!webAdultEl.checked : false,
        webSafeSearchEnabled: webSafeEl ? !!webSafeEl.checked : false,
        webYouTubeRestrictedModeEnabled: webYouTubeEl ? !!webYouTubeEl.checked : false,
        webCircumventionDetectionEnabled: webCircEl ? !!webCircEl.checked : true,
        webAllowedDomains: webAllowEl ? webAllowEl.value.split(/\r?\n/).map(x => normDomain(x)).filter(x => x) : [],
        webBlockedDomains: webBlockEl ? webBlockEl.value.split(/\r?\n/).map(x => normDomain(x)).filter(x => x) : [],
        webCategoryRules: buildWebCategoryRules()
      }, extra || {});

      const putRes = await Safe0neApi.updateChildPolicy(childId, payload);

      saveBtn.disabled = false;
      grant15.disabled = false;
      clearGrant.disabled = false;

      if (!putRes.ok){
        result.innerHTML = `<div class="notice notice--danger">Save failed: ${escapeHtml(putRes.error || "unknown error")}</div>`;
        return;
      }

      applyPolicyMeta(putRes.data);
      result.innerHTML = `<div class="notice notice--success">Saved.</div>`;
      await refreshEffective();
    }

    saveBtn.addEventListener("click", async () => submitPolicy(null));
    grant15.addEventListener("click", async () => submitPolicy({ grantMinutes: 15 }));
    clearGrant.addEventListener("click", async () => submitPolicy({ grantMinutes: 0 }));

    // Live guardrails (don’t allow invalid saves; warn on drop-on-save inputs).
    wireValidation();
  });

  return `
    ${pageHeader("Child Profile",
      "Manage Devices, Policies, and Activity for this child. This page now shows an Effective preview from the policy engine.",
      null
    )}
    <div id="${containerId}"></div>
  `;
}



function renderReports(){
  /* ADR-0003: delegate Reports/Alerts-Inbox route rendering to feature module when available. */
  try{
    if (window.Safe0neReports && typeof window.Safe0neReports.renderReports === "function"){
      const html = window.Safe0neReports.renderReports();
      if (typeof html === "string") return html;
    }
  }catch(err){
    console.warn("Safe0neReports module render failed; using fallback.", err);
  }
  return _renderReportsFallback();
}

function renderAlerts(){
  /*
    Contract: router delegates Alerts rendering to Safe0neAlerts module.
    Marker: Show acknowledged
    The unit test DashboardUiMarkerTests.RouterJs_Delegates_Alerts_To_Safe0neAlerts_Module
    asserts that router.js references Safe0neAlerts.buildAlerts and Safe0neAlerts.isAcked.

    Router rule: never render Promises. We render the shell synchronously, then hydrate.
  */

  const containerId = "alerts-root";
  // Legacy fallback only; SSOT-backed prefs live in Local Settings Profiles.
  const legacyShowAckedKey = "safe0ne_alerts_show_acknowledged_v1";
  let showAcked = false;
  let sevFilter = "all";
  let qFilter = "";
  try{ showAcked = localStorage.getItem(legacyShowAckedKey) === "true"; }catch{}

  setTimeout(async () => {
    // Best-effort: hydrate SSOT-backed prefs + ack state and mirror into localStorage.
    let children = [];
    try{
      const cr = (window.Safe0neApi && typeof Safe0neApi.getChildren === "function") ? await Safe0neApi.getChildren() : null;
      if (cr && cr.ok) children = cr.data || [];
    }catch{}

    try{
      if (window.Safe0neApi && typeof Safe0neApi.getChildAlertsStateLocal === "function" && children.length){
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
          // Use first available prefs as the "global" default; we persist updates to all children.
          if (st.data && st.data.ui && typeof st.data.ui === "object"){
            showAcked = !!st.data.ui.showAcknowledged;
            sevFilter = String(st.data.ui.severity || "all");
            qFilter = String(st.data.ui.search || "");
          }
        }
      }
    }catch{}

    // Wire up toolbar controls (marker contract + compact UX).
    function persistPrefs(next){
      try{
        if (children.length && window.Safe0neApi && typeof Safe0neApi.setChildAlertsUiPrefsLocal === "function"){
          for (const c of children){
            const sid = c && c.id ? String(c.id) : "";
            if (!sid) continue;
            Safe0neApi.setChildAlertsUiPrefsLocal(sid, next);
          }
        } else {
          // legacy fallback
          if (typeof next.showAcknowledged === "boolean"){
            try{ localStorage.setItem(legacyShowAckedKey, next.showAcknowledged ? "true" : "false"); }catch{}
          }
        }
      }catch{}
    }

    try{
      const cb = document.getElementById("alerts-show-acked");
      const sev = document.getElementById("alerts-severity");
      const q = document.getElementById("alerts-search");
      if (cb){
        cb.checked = !!showAcked;
        cb.onchange = () => { persistPrefs({ showAcknowledged: cb.checked }); void render(); };
      }
      if (sev){
        sev.value = String(sevFilter || "all");
        sev.onchange = () => { persistPrefs({ severity: sev.value }); void render(); };
      }
      if (q){
        q.value = String(qFilter || "");
        let t;
        q.oninput = () => {
          clearTimeout(t);
          t = setTimeout(() => { persistPrefs({ search: q.value || "" }); void render(); }, 200);
        };
      }
    }catch{}

    const root = document.getElementById(containerId);
    if (!root) return;

    root.innerHTML = `<div class="skeleton">Loading alerts…</div>`;

    // Derive alerts from per-child status (no bulk endpoint required).
    let statuses = [];
    try{
      if (!children.length){
        const cr = (window.Safe0neApi && typeof Safe0neApi.getChildren === "function") ? await Safe0neApi.getChildren() : null;
        if (cr && cr.ok) children = cr.data || [];
      }
      for (const c of children){
        const st = (window.Safe0neApi && typeof Safe0neApi.getChildStatus === "function") ? await Safe0neApi.getChildStatus(c.id) : null;
        if (st && st.ok) statuses.push({ ok:true, child: c, status: st.data || {} });
      }
    }catch{}

    // Delegate alert building to module.
    let alerts = [];
    try{
      if (window.Safe0neAlerts && typeof Safe0neAlerts.buildAlerts === "function"){
        alerts = Safe0neAlerts.buildAlerts(statuses, Date.now()) || [];
      }
    }catch{}

    // Filter: routing (per child), ack, severity, search
    try{
      // Alerts routing: if child policy disables inbox, hide here.
      const routing = {};
      if (children.length && window.Safe0neApi && typeof Safe0neApi.getChildProfileLocal === "function"){
        for (const c of children){
          const sid = c && c.id ? String(c.id) : "";
          if (!sid) continue;
          try{
            const pr = await Safe0neApi.getChildProfileLocal(sid);
            const prof = (pr && pr.ok) ? (pr.data || {}) : {};
            routing[sid] = !(prof && prof.policy && prof.policy.alerts && prof.policy.alerts.routing && prof.policy.alerts.routing.inboxEnabled === false);
          }catch{ routing[sid] = true; }
        }
      }

      const q = String(qFilter || "").trim().toLowerCase();
      alerts = (alerts || []).filter(a => {
        const cid = a && a.childId ? String(a.childId) : "";
        if (cid && routing[cid] === false) return false;
        if (!showAcked && window.Safe0neAlerts && typeof Safe0neAlerts.isAcked === "function" && Safe0neAlerts.isAcked(a)) return false;
        if (sevFilter && sevFilter !== "all" && String(a.sev || "info") !== sevFilter) return false;
        if (q){
          const blob = `${a.title||""} ${a.detail||""} ${a.childName||""}`.toLowerCase();
          if (!blob.includes(q)) return false;
        }
        return true;
      });
    }catch{}

    if (!alerts || alerts.length === 0){
      root.innerHTML = `<div class="card"><h2>No alerts</h2><p class="muted">Everything looks calm right now.</p></div>`;
      return;
    }

    const rows = alerts.map(a => {
      const sev = (a && a.sev) ? String(a.sev) : "info";
      const title = (a && a.title) ? escapeHtml(a.title) : "Alert";
      const detail = (a && a.detail) ? escapeHtml(a.detail) : "";
      const who = (a && a.childName) ? escapeHtml(a.childName) : "";
      const actions = (window.Safe0neAlerts && typeof Safe0neAlerts.renderAlertActions === "function")
        ? Safe0neAlerts.renderAlertActions(a)
        : "";
      return `
        <div class="card" data-sev="${escapeHtml(sev)}">
          <h2 style="margin:0 0 6px;">${title}</h2>
          <div class="muted" style="margin-bottom:8px;">${who}</div>
          <div style="margin-bottom:12px;">${detail}</div>
          <div class="kv" style="gap:8px;flex-wrap:wrap;">${actions}</div>
        </div>
      `;
    }).join("");

    root.innerHTML = `<div id="alerts-list">${rows}</div>`;

    // Bind module interactions (ack/unack).
    try{
      if (window.Safe0neAlerts && typeof Safe0neAlerts.bindAlertsList === "function"){
        Safe0neAlerts.bindAlertsList(document.getElementById("alerts-list"), () => { void render(); });
      }
    }catch{}
  }, 0);

  return `
    ${pageHeader("Alerts", "Review items that need attention.", null)}
    <div class="card" style="margin-bottom:12px;">
      <div class="card__body" style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;">
        <select id="alerts-severity" class="so-input" title="Filter by severity" style="min-width:160px;">
          <option value="all">All severities</option>
          <option value="danger">High</option>
          <option value="warning">Warning</option>
          <option value="info">Info</option>
        </select>
        <input id="alerts-search" class="so-input" placeholder="Search" title="Search alerts" style="min-width:180px;flex:1;" />
        <label class="muted" style="display:flex;align-items:center;gap:8px;user-select:none;">
          <input id="alerts-show-acked" type="checkbox" />
          <span>Show acknowledged</span>
        </label>
      </div>
    </div>
    <div id="${containerId}"></div>
  `;
}

function _renderReportsFallback(){
  // Minimal, syntax-safe fallback. The full alerts/reports UI lives in reports.js.
  return `
    ${pageHeader(
      "Family Alerts & Reports",
      "View alerts and reports for the selected child.",
      null
    )}
    <div class="page">
      <div class="card">
        <div class="card__body">
          <p>Loading reports module…</p>
          <p class="muted">If this message persists, open Dev Tools and check console/network.</p>
        </div>
      </div>
    </div>
  `;
}

function renderSupport(route) {
  if (window.Safe0neSupport && typeof window.Safe0neSupport.renderSupport === 'function') {
    return window.Safe0neSupport.renderSupport(route);
  }
  return _renderSupportFallback(route);
}


function _renderSupportFallback(){
  const containerId = "support-diag";
  setTimeout(async () => {
    const root = document.getElementById(containerId);
    if (!root) return;

    root.innerHTML = `<div class="skeleton">Loading devices…</div>`;

    // Delegate actions (no inline handlers).
    root.addEventListener('click', (ev) => {
      try{
        const btn = ev.target && ev.target.closest ? ev.target.closest('button[data-action="request-bundle"]') : null;
        if (!btn) return;
        const childEnc = btn.getAttribute('data-child') || '';
        const childId = decodeURIComponent(childEnc);
        if (window.Safe0neSupport && typeof window.Safe0neSupport.requestBundle === 'function'){
          window.Safe0neSupport.requestBundle(childId);
        }
      }catch{}
    });

    // Delegate actions (no inline handlers).
    root.addEventListener('click', (ev) => {
      try{
        const btn = ev.target && ev.target.closest ? ev.target.closest('button[data-action=\"request-bundle\"]') : null;
        if (!btn) return;
        const childEnc = btn.getAttribute('data-child') || '';
        const childId = decodeURIComponent(childEnc);
        if (window.Safe0neSupport && typeof window.Safe0neSupport.requestBundle === 'function'){
          window.Safe0neSupport.requestBundle(childId);
        }else{
          alert('Support module unavailable');
        }
      }catch{}
    });
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

      return `
        <div class="card">
          <h2>${escapeHtml(name)}</h2>
          <p class="muted">Child ID: ${escapeHtml(id)}</p>
          <div class="kv">
            <span>Request new bundle</span>
            <button class="btn" type="button" data-action="request-bundle" data-child="${encodeURIComponent(id)}">Request</button>
          </div>
          ${infoHtml}
        </div>
      `;
    }));

    root.innerHTML = cards.join("");
  });

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

function renderAdmin(){
  // ADR-0003: delegate Admin view rendering to feature module if present.
  try{
    if (window.Safe0neAdmin && typeof window.Safe0neAdmin.renderAdmin === "function"){
      const html = window.Safe0neAdmin.renderAdmin();
      if (typeof html === "string") return html;
    }
  }catch{}

  // Fallback: keep a minimal built-in template to avoid rendering literal "undefined".
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

function renderDevTools(route) {
  // Canonical DevTools renderer lives in /app/features/devtools.js
  // Keep router slim: delegate to the feature module so the Modules card + toggles always render.
  try {
    const DT = window.Safe0neDevTools;
    if (DT && typeof DT.renderDevTools === "function") {
      return DT.renderDevTools(route);
    }
  } catch (e) {
    console.warn("Router: failed to delegate DevTools renderer", e);
  }

  // Fallback (should rarely be hit)
  const enabled = isDevToolsUnlocked();
  return `
    <div class="page">
      <div class="dtHeader">
        <h1 style="margin:0;">Dev Tools</h1>
      </div>
      <div class="card" style="margin-top:16px;padding:18px;">
        <div class="muted">DevTools UI module is not available. (Unlocked: ${enabled ? "yes" : "no"})</div>
      </div>
    </div>
  `;
}

function buildWebCategoryRules(stateMap) {
  const out = [];
  Object.keys(stateMap || {}).forEach(k => {
    const cat = parseInt(k, 10);
    const act = parseInt(stateMap[k], 10);
    if (Number.isFinite(cat) && Number.isFinite(act)) {
      out.push({ category: cat, action: act });
    }
  });

function collectWebCategorySelections() {
  const map = {};
  document.querySelectorAll("select[data-web-cat]").forEach(sel => {
    const id = sel.getAttribute("data-web-cat");
    if (!id) return;
    map[id] = sel.value;
  });
  return map;
}


  return out;
}


const Safe0neSupport = {
  async requestBundle(childId){
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
};

function escapeHtml(s){
  return String(s)
    .replaceAll("&","&amp;")
    .replaceAll("<","&lt;")
    .replaceAll(">","&gt;")
    .replaceAll('"',"&quot;")
    .replaceAll("'","&#039;");
}

async function render(){
  const r = parseRoute();
  setActiveNav(r.key);
  const route = routes.find(x => x.key === r.key) || routes[0];

  const modId = (route && route.moduleId) ? route.moduleId : (route ? route.key : "unknown");
  const modTitle = route.title || route.key || "Module";

  // Disabled module placeholder (registry missing => treat as enabled)
  try{
    const M = window.Safe0neModules;
    const enabled = (M && typeof M.isEnabled === "function") ? M.isEnabled(modId) : true;
    if (!enabled){
      mount(`
        <div class="card" style="padding:24px;">
          <h2 style="margin:0 0 6px;">${escapeHtml(modTitle)}</h2>
          <div class="muted">This module is currently <b>disabled</b> in Dev Tools.</div>
        </div>
      `);
      return;
    }
  }catch{}

  const M = window.Safe0neModules;

  // Dynamic module loading: load only when needed/enabled.
  if (M && typeof M.ensureLoaded === "function"){
    try{
      const p = M.ensureLoaded(modId);
      if (p && typeof p.then === "function"){
        mount(`
          <div class="card" style="padding:24px;">
            <h2 style="margin:0 0 6px;">${escapeHtml(modTitle)}</h2>
            <div class="muted">Loading…</div>
          </div>
        `);
        await p;
      }
    }catch(e){
      if (M && typeof M.markFail === "function") M.markFail(modId, e);
      mount(`
        <div class="card" style="padding:24px;">
          <h2 style="margin:0 0 6px;">${escapeHtml(modTitle)}</h2>
          <div class="muted">Failed to load module script.</div>
          <pre class="mono" style="white-space:pre-wrap;margin-top:12px;">${escapeHtml(String(e && (e.stack || e.message) || e))}</pre>
        </div>
      `);
      return;
    }
  }

  // Render (async-safe)
  try{
    let out = route.render(r);

    // Contract guard for Children module regressions (module renderer returns undefined).
    if (route.key === "children"){
      const mod = window.Safe0neChildren;
      if (mod && typeof mod.renderChildren === "function"){
        const maybe = mod.renderChildren(r);
        if (maybe != null && (typeof maybe === "string" || (maybe && typeof maybe.then === "function"))){
          out = maybe;
        } else {
          // fall back to router implementation
          out = renderChildren(r);
        }
      }
    }
    if (route.key === "child"){
      const mod = window.Safe0neChildren;
      if (mod && typeof mod.renderChildProfile === "function"){
        const maybe = mod.renderChildProfile(r);
        if (maybe != null && (typeof maybe === "string" || (maybe && typeof maybe.then === "function"))){
          out = maybe;
        } else {
          out = renderChildProfile(r);
        }
      }
    }

    const html = await resolveHtml(out);
    mount(html);
    if (M && typeof M.markOk === "function") M.markOk(modId);
  }catch(e){
    if (M && typeof M.markFail === "function") M.markFail(modId, e);
    mount(`
      <div class="card" style="padding:24px;">
        <h2 style="margin:0 0 6px;">${escapeHtml(modTitle)}</h2>
        <div class="muted">This module failed to render.</div>
        <pre class="mono" style="white-space:pre-wrap;margin-top:12px;">${escapeHtml(String(e && (e.stack || e.message) || e))}</pre>
      </div>
    `);
  }
}


routes = [
  { key: "dashboard", title: "Dashboard", render: renderDashboard },
  { key: "parent", title: "Parent Profile", render: renderParent },
  { key: "children", title: "Children", render: renderChildren },
  // P11: Requests inbox (approve/deny).
  { key: "requests", title: "Requests", render: renderRequests },
  { key: "child", title: "Child Profile", render: renderChildProfile, hidden: true, moduleId: "children" },
	  // Hidden route retained for contract tests: router delegates to Safe0neAlerts module.
	  { key: "alerts", title: "Alerts", render: renderAlerts, hidden: true },
  { key: "reports", title: "Family Alerts & Reports", render: renderReports },
  { key: "support", title: "Support & Safety", render: renderSupport },
  { key: "admin", title: "Admin / Advanced", render: renderAdmin },
  { key: "devtools", title: "Dev Tools", render: renderDevTools },
];

// Router contract (used by self-test)
// NOTE: routeKeys is part of the boot self-test contract (modules.js runBootSelfTest).
// It must include the canonical keys so hidden-nav gating remains UI-only.
window.Safe0neRouter = {
  render,
  asyncSafe: true,
  exposed: true,
  routeKeys: Array.isArray(routes) ? routes.map(r => r && r.key).filter(Boolean) : [],
};


window.addEventListener("hashchange", () => { void render(); });
window.addEventListener("DOMContentLoaded", () => {
  if (!location.hash) location.hash = "#/dashboard";

  // Dev Tools menu is hidden by default; only show when unlocked.
  syncDevToolsNavVisibility();
  attachDevToolsUnlockGesture();

  void render();

  // Theme toggle (persist in localStorage)
  const saved = localStorage.getItem("theme") || "light";
  if (saved === "dark") document.documentElement.setAttribute("data-theme","dark");
  document.getElementById("theme-toggle").addEventListener("click", () => {
    const isDark = document.documentElement.getAttribute("data-theme") === "dark";
    if (isDark){
      document.documentElement.removeAttribute("data-theme");
      localStorage.setItem("theme","light");
    } else {
      document.documentElement.setAttribute("data-theme","dark");
      localStorage.setItem("theme","dark");
    }
  });
});


