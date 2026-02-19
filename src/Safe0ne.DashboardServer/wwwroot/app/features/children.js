(async () => {
  // LEGACY-COMPAT (UI): prior versions persisted domain state (children/profiles) in localStorage.
  // SSOT LAW: browser storage is preferences-only. These keys are now treated as legacy and ignored.
  // TODO(LEGACY-REMOVE): once all users are migrated and SSOT purity checks have been shipping, delete references.
  const LS_CHILDREN = "safe0ne.children.v1";
  const LS_PROFILES = "safe0ne.childProfiles.v1";
  const LS_SHOW_ARCHIVED = "safe0ne.children.showArchived.v1";
  const AGE = ["0–5", "6–9", "10–12", "13–15", "16–18"];

  const escapeHtml = (s) =>
    (window.Safe0neUi?.escapeHtml
      ? window.Safe0neUi.escapeHtml(String(s ?? ""))
      : String(s ?? "")
          .replace(/&/g, "&amp;")
          .replace(/</g, "&lt;")
          .replace(/>/g, "&gt;")
          .replace(/"/g, "&quot;")
          .replace(/'/g, "&#039;"));

  const clamp = (n, lo, hi) => Math.max(lo, Math.min(hi, n));


function dayOverrideField(day, profile) {
  const key = String(day || "").trim();
  const map = profile?.policy?.timeBudget?.perDayMinutes || {};
  const val = map && typeof map === "object" ? map[key] : null;
  return `<div class="so-field"><label>${escapeHtml(key)}</label>
    <input type="number" min="0" max="1440" step="5" placeholder="—" data-field="screenMinutes${escapeHtml(key)}" value="${val == null ? "" : escapeHtml(val)}"/>
  </div>`;
}


function ensurePerAppLimitIds(prof) {
  prof.policy = prof.policy || {};
  prof.policy.apps = prof.policy.apps || { allowList: [], denyList: [], perAppLimits: [], blockNewApps: false };
  if (!Array.isArray(prof.policy.apps.perAppLimits)) prof.policy.apps.perAppLimits = [];
  // Ensure stable ids for UI operations (additive).
  for (const lim of prof.policy.apps.perAppLimits) {
    if (lim && typeof lim === "object" && !lim.id) lim.id = `pal_${Math.random().toString(16).slice(2)}`;
  }
}

function renderPerAppLimitsCard(child, profile) {
  ensurePerAppLimitIds(profile);
  const limits = Array.isArray(profile?.policy?.apps?.perAppLimits) ? profile.policy.apps.perAppLimits : [];
  const days = ["Mon","Tue","Wed","Thu","Fri","Sat","Sun"];

  const allowTxt = Array.isArray(profile?.policy?.apps?.allowList) ? profile.policy.apps.allowList.join("\n") : "";
  const denyTxt = Array.isArray(profile?.policy?.apps?.denyList) ? profile.policy.apps.denyList.join("\n") : "";
  const blockNew = !!profile?.policy?.apps?.blockNewApps;

  const dayBox = (lid, d, checked) =>
    `<label class="pal-day" title="${escapeHtml(d)}">
      <input type="checkbox" data-pal="day" data-limit-id="${escapeHtml(lid)}" data-day="${escapeHtml(d)}" ${checked ? "checked" : ""}/>
      <span>${escapeHtml(d.slice(0,1))}</span>
    </label>`;

  const row = (lim) => {
    const lid = lim.id || `pal_${Math.random().toString(16).slice(2)}`;
    const limDays = Array.isArray(lim.days) && lim.days.length ? lim.days : days;
    const has = new Set(limDays);
    return `<div class="pal-row" data-pal-row="1" data-limit-id="${escapeHtml(lid)}">
      <div class="pal-main">
        <div class="pal-field">
          <label>App ID</label>
          <input type="text" placeholder="e.g. chrome.exe" data-pal="appId" data-limit-id="${escapeHtml(lid)}" value="${escapeHtml(lim.appId || "")}"/>
        </div>
        <div class="pal-field" style="max-width:120px;">
          <label>Min/day</label>
          <input type="number" min="0" max="1440" step="5" data-pal="minutes" data-limit-id="${escapeHtml(lid)}" value="${escapeHtml(Number.isFinite(Number(lim.minutesPerDay)) ? Number(lim.minutesPerDay) : 60)}"/>
        </div>
        <button class="so-btn pal-del" data-action="removePerAppLimit" data-childid="${escapeHtml(child.id)}" data-limit-id="${escapeHtml(lid)}" type="button" title="Remove limit">🗑</button>
      </div>
      <div class="pal-days" aria-label="Days">
        ${days.map((d) => dayBox(lid, d, has.has(d))).join("")}
      </div>
    </div>`;
  };

  return `<div class="card" style="margin-top:14px;">
    <div style="display:flex;justify-content:space-between;align-items:flex-end;gap:12px;">
      <div>
        <div style="font-weight:900;font-size:18px;">App controls</div>
        <div style="margin-top:6px;color:#64748b;font-size:12px;">Per‑app limits + allow/deny authoring (enforcement wired later).</div>
      </div>
      <button class="so-btn" data-action="addPerAppLimit" data-childid="${escapeHtml(child.id)}" type="button" title="Add per-app limit">➕ Add limit</button>
    </div>

    <div class="pal-list" style="margin-top:12px;">
      ${limits.length ? limits.map(row).join("") : `<div style="color:#64748b;font-size:12px;">No per‑app limits yet.</div>`}
    </div>

    <details style="margin-top:12px;">
      <summary style="cursor:pointer;font-weight:900;color:#334155;">Allow / Deny lists (optional)</summary>
      <div style="margin-top:10px;display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:12px;">
        <div class="so-field">
          <label>Allow list (one per line)</label>
          <textarea rows="6" data-field="appsAllowList" placeholder="e.g.\nchrome.exe\nnotepad.exe">${escapeHtml(allowTxt)}</textarea>
          <div style="margin-top:6px;color:#64748b;font-size:12px;">If set, these apps are explicitly allowed.</div>
        </div>
        <div class="so-field">
          <label>Deny list (one per line)</label>
          <textarea rows="6" data-field="appsDenyList" placeholder="e.g.\nsteam.exe">${escapeHtml(denyTxt)}</textarea>
          <div style="margin-top:6px;color:#64748b;font-size:12px;">If set, these apps are explicitly blocked (best-effort).</div>
        </div>
        <div style="grid-column:1/-1;">
          <label style="display:flex;gap:10px;align-items:center;font-weight:900;color:#475569;">
            <input type="checkbox" data-field="blockNewApps" ${blockNew ? "checked" : ""}/>
            Block newly installed apps (planned)
          </label>
        </div>
      </div>
    </details>

    <div style="margin-top:10px;color:#64748b;font-size:12px;">Tip: Use process names on Windows (e.g. <code>chrome.exe</code>). Keep IDs unique.</div>
  </div>`;
}

  function isGuid(s) {
    return /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(String(s || ""));
  }

  function defaultSettingsProfile(childId) {
    return {
      childId: String(childId || ""),
      // v1 enforcement contract (Kid polls /api/local/children/{id}/profile).
      // Additive-only: new fields MUST have defaults.
      policy: {
        mode: "Open",
        grantUntilUtc: null,
        alwaysAllowed: false,
        apps: {
          allowList: [],
          denyList: [],
          perAppLimits: [],
          blockNewApps: false,
        },
        timeBudget: {
          dailyMinutes: 120,
          perDayMinutes: {},
          // PATCH 16R (additive): grace period after budget ends + warning thresholds.
          graceMinutes: 0,
          warnAtMinutes: [5, 1],
        },
        alerts: {
          enabled: true,
          thresholds: {},
          routing: { inboxEnabled: true, notifyEnabled: false },
        },
      },
      permissions: {
        web: true,
        apps: true,
        bedtime: true,
        location: false,
        purchases: false,
      },
      limits: {
        screenMinutesPerDay: 120,
        bedtimeStart: "21:00",
        bedtimeEnd: "07:00",
      },
      devices: [],

      // Derived, additive state (server-updated). Safe defaults for older profiles.
      locationState: {
        geofences: [],
        lastEvaluatedAtUtc: null,
      },
    };
  }

  function mergeDefaults(def, obj) {
    // Shallow-safe, recursive merge. Arrays are treated as replace-by-source.
    if (!def || typeof def !== "object") return obj;
    if (!obj || typeof obj !== "object") {
      try {
        return JSON.parse(JSON.stringify(def));
      } catch {
        return def;
      }
    }
    if (Array.isArray(def)) {
      return Array.isArray(obj) ? obj : def;
    }
    const out = { ...def };
    for (const k of Object.keys(obj)) {
      const dv = def[k];
      const ov = obj[k];
      if (ov === undefined || ov === null) {
        // keep default
        continue;
      }
      if (dv && typeof dv === "object" && !Array.isArray(dv) && typeof ov === "object" && !Array.isArray(ov)) {
        out[k] = mergeDefaults(dv, ov);
      } else {
        out[k] = ov;
      }
    }
    return out;
  }


  function readJson(key, fallback) {
    try {
      const raw = localStorage.getItem(key);
      return raw ? JSON.parse(raw) : fallback;
    } catch {
      return fallback;
    }
  }
  function writeJson(key, v) {
    try {
      localStorage.setItem(key, JSON.stringify(v));
    } catch {}
  }

  function readBool(key, fallback) {
    try {
      const raw = localStorage.getItem(key);
      if (raw == null) return fallback;
      return raw === "1" || raw === "true";
    } catch {
      return fallback;
    }
  }
  function writeBool(key, v) {
    try {
      localStorage.setItem(key, v ? "1" : "0");
    } catch {}
  }


  function demoChild() {
    return {
      id: "demo-11111111",
      name: "Local Child",
      source: "Local",
      status: "Awaiting enrollment",
      gender: "unspecified",
      ageGroup: "10–12",
      avatar: { kind: "default", id: 0 },
    };
  }

  function getLocalChildren() {
    // SSOT purity: do NOT read children domain state from localStorage.
    // Keep a stable in-memory cache only (used for demo/offline rendering).
    if (!Array.isArray(state._localChildrenCache)) state._localChildrenCache = [];
    return state._localChildrenCache
      .filter((x) => x && x.id && x.id !== "demo-11111111");
  }
  function setLocalChildren(arr) {
    // SSOT purity: do NOT persist children domain state to localStorage.
    state._localChildrenCache = Array.isArray(arr)
      ? arr.filter((x) => x && x.id && x.id !== "demo-11111111")
      : [];
  }
  function normalizeApiChild(c) {
    const id = String(c?.id ?? c?.childId ?? "");
    const displayName = String(c?.displayName ?? c?.name ?? "");
    const isArchived = !!(c?.isArchived ?? c?.archived ?? false);
    return {
      id,
      name: displayName || "Unnamed",
      source: "Local",
      status: isArchived ? "Archived" : "Awaiting enrollment",
      gender: c?.gender ?? "unspecified",
      ageGroup: c?.ageGroup ?? "10–12",
      avatar: c?.avatar ?? { kind: "default", id: 0 },
      isArchived,
      archivedAtUtc: c?.archivedAtUtc ?? null,
    };
  }

  function getChildren() {
    const useApi = state?.api?.available && Array.isArray(state.api.children);
    const base = useApi ? state.api.children : [demoChild(), ...getLocalChildren()];
    const showArchived = !!state?.showArchived;
    return showArchived ? base : base.filter((c) => !c?.isArchived);
  }

  function getProfiles() {
    // IMPORTANT: must be a stable in-memory object.
    // Many features (e.g., geofence editor) mutate the returned object and then persist.
    // If we re-read from storage on each call, those mutations are lost.
    if (!state._profilesCache || typeof state._profilesCache !== "object") {
      // SSOT purity: do NOT hydrate domain state from localStorage.
      state._profilesCache = {};
    }
    return state._profilesCache;
  }
  function setProfiles(m) {
    // SSOT purity: do NOT persist domain state to localStorage.
    state._profilesCache = (m && typeof m === "object") ? m : {};
  }
  function ensureProfile(child) {
    // If the Local API is temporarily offline, we may keep an in-memory draft so the user
    // doesn't lose edits. Drafts are NOT SSOT and must never be persisted.
    try {
      const did = child?.id;
      const draft = did && state?.ui?.profileDraftByChildId && state.ui.profileDraftByChildId[did];
      if (draft && typeof draft === "object") return draft;
    } catch (_) {}

    const m = getProfiles();
    if (!m[child.id]) {
      m[child.id] = {
        childId: child.id,
        policy: {
          mode: 'Open',
          grantUntilUtc: null,
          alwaysAllowed: false,
          apps: { allowList: [], denyList: [], perAppLimits: [], blockNewApps: false },
          timeBudget: { dailyMinutes: 120, perDayMinutes: {} },
          location: { geofences: [] },
        },
        permissions: {
          web: true,
          apps: true,
          bedtime: true,
          location: false,
          purchases: false,
        },
        limits: {
          screenMinutesPerDay: 120,
          bedtimeStart: "21:00",
          bedtimeEnd: "07:00",
        },
	        // Local fallback profile (used only when Local API is unavailable).
	        // Keep it clearly non-demo: this represents "awaiting enrollment" until a device is paired.
	        devices: [
	          { id: "seed-device-1", name: "Awaiting enrollment", os: "—", lastSeen: "—" },
	        ],
      };
      setProfiles(m);
    }
    return m[child.id];
  }

  function genderMeta(g) {
    const gg = (g || "unspecified").toLowerCase();
    if (gg === "male") return { label: "Male", symbol: "♂", cls: "so-gender-male" };
    if (gg === "female") return { label: "Female", symbol: "♀", cls: "so-gender-female" };
    return { label: "Unspecified", symbol: "?", cls: "so-gender-unspec" };
  }

  function defaultAvatarSvg(i) {
    const palettes = [
      { bg: "#eaf2ff", fg: "#3b82f6" },
      { bg: "#e9fbf0", fg: "#22c55e" },
      { bg: "#fff2e5", fg: "#f97316" },
      { bg: "#f3e8ff", fg: "#8b5cf6" },
      { bg: "#eef2f7", fg: "#64748b" },
      { bg: "#ffe4ea", fg: "#fb7185" },
    ];
    const p = palettes[((i | 0) % palettes.length + palettes.length) % palettes.length];
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96" width="96" height="96">
      <rect width="96" height="96" rx="18" fill="${p.bg}"/>
      <circle cx="48" cy="38" r="16" fill="${p.fg}"/>
      <rect x="22" y="58" width="52" height="22" rx="11" fill="${p.fg}"/>
    </svg>`;
  }

  function avatarHtml(child, size = 96) {
    const a = child.avatar || { kind: "default", id: 0 };
    const g = genderMeta(child.gender);
    const crop = a.crop || { z: 1, x: 0, y: 0 };
    const z = clamp(Number(crop.z ?? 1), 1, 3);
    const x = clamp(Number(crop.x ?? 0), -40, 40);
    const y = clamp(Number(crop.y ?? 0), -40, 40);

    let inner = "";
    if (a.kind === "upload" && typeof a.dataUrl === "string" && a.dataUrl.startsWith("data:image")) {
      inner = `<div class="so-avatar-clip" style="width:${size}px;height:${size}px;">
        <img src="${a.dataUrl}" alt="" style="transform: translate(${x}px,${y}px) scale(${z});"/>
      </div>`;
    } else {
      inner = `<div class="so-avatar-clip" style="width:${size}px;height:${size}px;">
        ${defaultAvatarSvg(a.id | 0)}
      </div>`;
    }

    return `<button class="so-avatar-btn" type="button" data-action="avatarZoom" data-childid="${escapeHtml(
      child.id
    )}">
      ${inner}
      <span class="so-gender-badge ${g.cls}">
        <span class="so-gender-symbol">${g.symbol}</span>
      </span>
    </button>`;
  }

  function styles() {
    return `<style>
      .so-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(340px,1fr));gap:18px;margin-top:14px}
      .so-child-card{background:#fff;border-radius:18px;box-shadow:0 10px 24px rgba(0,0,0,.06);padding:16px}
      .so-card-top{display:flex;justify-content:space-between;gap:12px;align-items:flex-start}
      .so-card-name{font-size:22px;font-weight:900}
      .so-card-sub{margin-top:6px;color:#64748b;font-weight:700}
      .so-actions{display:flex;gap:10px;flex-wrap:wrap;justify-content:flex-end}
      .so-btn{border-radius:999px;padding:10px 14px;border:1px solid rgba(148,163,184,.45);background:#f8fafc;font-weight:800;cursor:pointer}
      .so-btn:hover{filter:brightness(.98)}
      .so-btn-danger{background:#fff;border-color:rgba(239,68,68,.35);color:#dc2626}
      .so-btn-danger:disabled{opacity:.5;cursor:not-allowed}
      .so-card-body{display:flex;gap:14px;align-items:center;margin-top:12px}
      .so-avatar-btn{border:none;background:transparent;padding:0;cursor:pointer;position:relative}
      .so-avatar-clip{border-radius:18px;overflow:hidden;background:#f1f5f9;display:flex;align-items:center;justify-content:center}
      .so-avatar-clip svg{width:100%;height:100%}
      .so-avatar-clip img{width:100%;height:100%;object-fit:cover;display:block;transform-origin:center center}
      .so-gender-badge{position:absolute;right:-6px;bottom:-6px;width:36px;height:36px;border-radius:999px;display:flex;align-items:center;justify-content:center;border:3px solid #fff;box-shadow:0 8px 18px rgba(0,0,0,.12)}
      .so-gender-symbol{font-size:20px;font-weight:900}
      .so-gender-male{background:#dbeafe;color:#1d4ed8}
      .so-gender-female{background:#ffe4ea;color:#be185d}
      .so-gender-unspec{background:#e2e8f0;color:#334155}
      .so-pill{border-radius:999px;padding:8px 12px;font-weight:900;border:1px solid rgba(148,163,184,.35);background:#fff}
      .so-pill--success{border-color:rgba(34,197,94,.35);background:rgba(34,197,94,.12);color:#14532d}
      .so-pill--warning{border-color:rgba(245,158,11,.35);background:rgba(245,158,11,.12);color:#92400e}
      .so-pill--muted{border-color:rgba(148,163,184,.35);background:rgba(148,163,184,.10);color:#334155}
      .so-badges{display:flex;gap:10px;flex-wrap:wrap}
      /* modal */
      .so-modal-backdrop{position:fixed;inset:0;background:rgba(15,23,42,.55);display:flex;align-items:center;justify-content:center;z-index:9999;padding:18px}
      .so-modal{width:min(920px,96vw);background:#fff;border-radius:20px;box-shadow:0 24px 64px rgba(0,0,0,.22);overflow:hidden}
      .so-hd{display:flex;justify-content:space-between;align-items:center;padding:18px;border-bottom:1px solid rgba(148,163,184,.25)}
      .so-title{font-size:22px;font-weight:900}
      .so-bd{padding:18px}
      .so-ft{display:flex;justify-content:flex-end;gap:12px;padding:16px 18px;border-top:1px solid rgba(148,163,184,.25)}
      .so-form{display:grid;grid-template-columns:1fr 1fr;gap:14px}
      .so-field label{display:block;font-weight:900;color:#475569;margin-bottom:6px}
      .so-field input,.so-field select,.so-field textarea{width:100%;border-radius:14px;border:1px solid rgba(148,163,184,.45);padding:12px;font-size:16px}
      .so-field textarea{resize:vertical;font-family:ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace}
      .so-one{grid-column:1/-1}
      .so-avatars{display:grid;grid-template-columns:repeat(6,minmax(0,1fr));gap:10px;margin-top:10px}
      .so-avatar-tile{border-radius:16px;border:2px solid rgba(148,163,184,.25);padding:10px;background:#fff;cursor:pointer}
      .so-avatar-tile[data-selected="1"]{border-color:#3b82f6;box-shadow:0 0 0 4px rgba(59,130,246,.18)}
      .so-avatar-tile svg{width:100%;height:68px}
      .so-slider{width:100%}
      .cp-tabs{display:flex;gap:10px;flex-wrap:wrap;margin-top:14px}
      .cp-tab{border:1px solid rgba(148,163,184,.45);background:#fff;border-radius:999px;padding:10px 14px;font-weight:900;cursor:pointer}
      .cp-tab.active{border-color:#2563eb;box-shadow:0 0 0 3px rgba(37,99,235,.15)}
      .cp-panel{display:none}
      .cp-panel.active{display:block}
      .table{display:flex;flex-direction:column;border:1px solid rgba(148,163,184,.25);border-radius:14px;overflow:hidden}
      .tr{display:grid;grid-template-columns:2fr 1fr 1fr auto;gap:10px;padding:12px 14px;align-items:center;background:#fff}
      .tr.th{background:#f1f5f9;font-weight:900;color:#334155}
      .tr + .tr{border-top:1px solid rgba(148,163,184,.18)}
      
      .so-banner-warn{margin-top:14px;background:rgba(245,158,11,.12);border:1px solid rgba(245,158,11,.35);color:#92400e;border-radius:16px;padding:10px 12px;font-weight:900}
      .pal-list{display:flex;flex-direction:column;gap:10px}
      .pal-row{border:1px solid rgba(148,163,184,.25);background:#fff;border-radius:14px;padding:10px 10px}
      .pal-main{display:flex;gap:10px;align-items:flex-end}
      .pal-field{flex:1}
      .pal-field label{display:block;font-weight:900;color:#475569;margin-bottom:6px;font-size:12px}
      .pal-field input{width:100%;border-radius:12px;border:1px solid rgba(148,163,184,.45);padding:10px;font-size:14px}
      .pal-del{padding:10px 12px;min-width:44px}
      .pal-days{display:flex;gap:6px;flex-wrap:wrap;margin-top:8px}
      .pal-day{display:flex;gap:6px;align-items:center;border:1px solid rgba(148,163,184,.25);background:#f8fafc;border-radius:999px;padding:6px 10px;font-weight:900;font-size:12px;cursor:pointer}
      .pal-day input{accent-color:#2563eb}
      @media (max-width:760px){.pal-main{flex-wrap:wrap}.pal-del{width:100%}}
@media (max-width:760px){.so-form{grid-template-columns:1fr}.so-avatars{grid-template-columns:repeat(3,minmax(0,1fr))}}
    </style>`;
  }

  function card(child) {
    const g = genderMeta(child.gender);
    const age = child.ageGroup || "—";
	    const status = child.isArchived ? "Archived" : (child.status || "Awaiting enrollment");
    const canDelete = child.id !== "demo-11111111";
    return `<div class="so-child-card" data-childid="${escapeHtml(child.id)}">
      <div class="so-card-top">
        <div>
          <div class="so-card-name">${escapeHtml(child.name || "Unnamed")}</div>
          <div class="so-card-sub">${escapeHtml(status)}</div>
        </div>
        <div class="so-actions">
          <button class="so-btn" data-action="open" data-childid="${escapeHtml(child.id)}">Open</button>
          <button class="so-btn" data-action="edit" data-childid="${escapeHtml(child.id)}" ${
      canDelete ? "" : "disabled"
    }>Edit</button>
          <button class="so-btn so-btn-danger" data-action="delete" data-childid="${escapeHtml(child.id)}" ${
      canDelete ? "" : "disabled"
    }>Delete</button>
        </div>
      </div>
      <div class="so-card-body">
        ${avatarHtml(child, 96)}
        <div class="so-badges">
          <span class="so-pill ${g.cls}"><span class="so-gender-symbol">${g.symbol}</span>&nbsp;${g.label}</span>
          <span class="so-pill">${escapeHtml(age)}</span>
          <span class="so-pill so-pill--muted" data-role="agent" data-childid="${escapeHtml(child.id)}">Protection: …</span>
        </div>
      </div>
    </div>`;
  }

  function renderChildren() {
    scheduleChildrenRefresh();
    scheduleStatusBadgesRefresh();
    const children = getChildren();
    return `<div class="page">
      <div class="page-title">Children</div>
      <div class="page-subtitle">Manage devices, policies and requests</div>
      ${styles()}
      <div class="card" style="display:flex;justify-content:space-between;align-items:center;gap:12px;flex-wrap:wrap;">
        <div style="font-weight:800;color:#334155;">Local mode: profiles are stored on this PC (not synced).</div>
        <div style="display:flex;gap:12px;align-items:center;flex-wrap:wrap;justify-content:flex-end;">
          <label style="display:flex;gap:10px;align-items:center;font-weight:800;color:#334155;">
            <input type="checkbox" data-action="toggleShowArchived" id="so-show-archived" style="width:18px;height:18px;" ${state.showArchived ? "checked" : ""}/>
            <span>Show archived</span>
          </label>
          <button class="so-btn" data-action="add">Add child</button>
        </div>
      </div>
      <div class="so-grid" id="children-grid">${children.map(card).join("")}</div>
    </div>`;
  }

  function permToggle(key, label, val) {
    return `<label style="display:flex;gap:10px;align-items:center;padding:10px;border:1px solid rgba(148,163,184,.35);border-radius:14px;background:#fff;cursor:pointer;">
      <input type="checkbox" data-perm="${escapeHtml(key)}" ${val ? "checked" : ""} style="width:18px;height:18px;"/>
      <span style="font-weight:900;">${escapeHtml(label)}</span>
    </label>`;
  }

  function alertRouteToggle(key, label, val) {
    return `<label style="display:flex;gap:10px;align-items:center;padding:10px;border:1px solid rgba(148,163,184,.35);border-radius:14px;background:#fff;cursor:pointer;">
      <input type="checkbox" data-alertroute="${escapeHtml(key)}" ${val ? "checked" : ""} style="width:18px;height:18px;"/>
      <span style="font-weight:900;">${escapeHtml(label)}</span>
      <span style="margin-left:auto;color:#64748b;font-weight:700;font-size:12px;">best-effort</span>
    </label>`;
  }

  // --- Child Profile tab state ---
  function getChildTabKey(childId) { return `s0_child_profile_tab:${childId}`; }
  function getChildTab(childId) {
    try { return localStorage.getItem(getChildTabKey(childId)) || "settings"; } catch { return "settings"; }
  }
  function setChildTab(childId, tab) {
    try { localStorage.setItem(getChildTabKey(childId), tab); } catch {}
  }

function renderDevicesTab(child) {
  const id = String(child?.id || "");
  const useApi = !!state?.api?.available && isGuid(id) && !!state.api.devicesByChildId;
  const apiDevices = useApi ? state.api.devicesByChildId[id] : null;
  const pairing = useApi ? state.api.pairingByChildId[id] : null;

  const fallbackProf = ensureProfile(child);
  const fallback = Array.isArray(fallbackProf.devices) ? fallbackProf.devices : [];
  const devices = Array.isArray(apiDevices) ? apiDevices : (fallback.length ? fallback : []);

  const rows = (devices.length ? devices : [{ deviceName: "No devices", status: "Not enrolled", lastSeenUtc: null }])
    .map((d) => {
      const devId = String(d?.deviceId ?? d?.id ?? "");
      const name = String(d?.deviceName ?? d?.name ?? "Device");
      const revoked = !!(d?.tokenRevoked || d?.tokenRevokedAtUtc);
      const expired = !!(d?.tokenExpired || (d?.tokenExpiresAtUtc && (new Date(d.tokenExpiresAtUtc)).getTime() <= Date.now()));
      const status = String(d?.status ?? (revoked ? "Revoked" : (expired ? "Expired" : (devId ? "Active" : "Not paired"))));
      const lastSeen = d?.lastSeenUtc ? new Date(d.lastSeenUtc).toLocaleString() : (d?.lastSeen || "—");
      const tokenExpires = d?.tokenExpiresAtUtc ? new Date(d.tokenExpiresAtUtc).toLocaleString() : null;
      const tokenMeta = (revoked && d?.tokenRevokedAtUtc)
        ? `Revoked: ${new Date(d.tokenRevokedAtUtc).toLocaleString()}${d?.tokenRevokedBy ? ` by ${String(d.tokenRevokedBy)}` : ""}`
        : (tokenExpires ? `Token expires: ${tokenExpires}` : "");
      const canUnpair = useApi && devId;
      const canRevoke = useApi && devId && !revoked;
      const canRepair = useApi && (revoked || expired);
      return `
    <div class="tr">
      <div><strong>${escapeHtml(name)}</strong></div>
      <div><span class="so-pill">${escapeHtml(status)}</span></div>
      <div class="so-card-sub">${escapeHtml(lastSeen)}${tokenMeta ? `<div class="so-card-sub" style="margin-top:6px;">${escapeHtml(tokenMeta)}</div>` : ""}</div>
      <div style="text-align:right;display:flex;gap:10px;justify-content:flex-end;flex-wrap:wrap;">
        ${canRepair ? `<button class="so-btn" data-action="pairDevice" data-childid="${escapeHtml(id)}" type="button">Re-pair</button>` : ""}
        ${canRevoke ? `<button class="so-btn" data-action="revokeDeviceToken" data-deviceid="${escapeHtml(devId)}" data-childid="${escapeHtml(id)}" type="button">Revoke token</button>` : ""}
        ${canUnpair ? `<button class="so-btn so-btn-danger" data-action="unpairDevice" data-deviceid="${escapeHtml(devId)}" data-childid="${escapeHtml(id)}" type="button">Unpair</button>` : `<button class="so-btn" data-action="noop" type="button">Details</button>`}
      </div>
    </div>`;
    })
    .join("");

  // Pairing deep link is optional polish. It does not replace code-based pairing.
  const pairingCode = (pairing && pairing.pairingCode) ? String(pairing.pairingCode) : "";
  const pairingDeepLink = (pairingCode && isGuid(id))
    ? `safe0ne://pair?childId=${encodeURIComponent(id)}&code=${encodeURIComponent(pairingCode)}`
    : "";

  const pairingBlock = (useApi
    ? (pairing && pairing.pairingCode
        ? `<div class="card" style="margin-top:12px;background:#f8fafc;border:1px solid rgba(148,163,184,.25);">
            <div style="display:flex;justify-content:space-between;gap:12px;align-items:center;flex-wrap:wrap;">
              <div style="font-weight:900;">Pairing code: <span class="so-pill">${escapeHtml(pairing.pairingCode)}</span></div>
              <div class="so-card-sub">Expires: ${escapeHtml(pairing.expiresAtUtc ? new Date(pairing.expiresAtUtc).toLocaleString() : "—")}</div>
            </div>
            ${pairingDeepLink ? `
              <div style="margin-top:10px;display:flex;gap:10px;flex-wrap:wrap;align-items:center;justify-content:space-between;">
                <div class="so-card-sub" style="flex:1;min-width:240px;">
                  Pairing link (optional):
                  <div style="margin-top:6px;display:flex;gap:10px;flex-wrap:wrap;align-items:center;">
                    <input class="so-input" style="flex:1;min-width:260px;" readonly value="${escapeHtml(pairingDeepLink)}" />
                    <button class="so-btn" data-action="copyPairLink" data-link="${escapeHtml(pairingDeepLink)}" type="button">Copy link</button>
                    <a class="so-btn" style="text-decoration:none;display:inline-flex;align-items:center;" href="${escapeHtml(pairingDeepLink)}">Open</a>
                  </div>
                  <div class="so-card-sub" style="margin-top:6px;">If the Kid device supports Safe0ne links, it can open the pairing screen pre-filled.</div>
                </div>
              </div>
            ` : ""}
            <div style="margin-top:10px;display:flex;gap:10px;flex-wrap:wrap;justify-content:flex-end;">
              <button class="so-btn" data-action="copyPairCode" data-code="${escapeHtml(pairing.pairingCode)}" type="button">Copy</button>
              <button class="so-btn" data-action="refreshDevices" data-childid="${escapeHtml(id)}" type="button">Refresh</button>
            </div>
          </div>`
        : `<div class="card" style="margin-top:12px;background:#f8fafc;border:1px solid rgba(148,163,184,.25);">
            <div style="font-weight:900;">No active pairing session</div>
            <div class="so-card-sub" style="margin-top:6px;">Generate a pairing code and enter it on the Kid device to enroll.</div>
          </div>`)
    : "");

  const pairActions = useApi
    ? `<div style="margin-top:12px;display:flex;gap:10px;flex-wrap:wrap;justify-content:flex-end;">
        <button class="so-btn" data-action="pairDevice" data-childid="${escapeHtml(id)}" type="button">Generate pairing code</button>
        <button class="so-btn" data-action="refreshDevices" data-childid="${escapeHtml(id)}" type="button">Refresh</button>
      </div>`
    : `<div style="margin-top:12px;" class="so-card-sub">Pair devices to this child profile (local mode requires SSOT-backed child).</div>`;

  return `
    <div class="card" style="margin-top:14px;">
      <div style="display:flex;justify-content:space-between;gap:12px;align-items:center;flex-wrap:wrap;">
        <div style="font-weight:900;font-size:18px;">Devices</div>
        <div class="so-card-sub">Local enrollment (Parent ↔ Kid)</div>
      </div>
      ${pairActions}
      ${pairingBlock}
      <div class="table" style="margin-top:12px;">
        <div class="tr th"><div>Device</div><div>Status</div><div>Last seen</div><div></div></div>
        ${rows}
      </div>
    </div>`;
}

  function renderRequestsTab(child) {
    const prof = ensureProfile(child);
    const reqs = Array.isArray(prof.requests) ? prof.requests : [];
    const items = (reqs.length ? reqs : [
      { type: "Extra screen time", detail: "Request +30 minutes", when: "Today" },
      { type: "Blocked website", detail: "example.com", when: "Yesterday" },
    ]).map((r) => `
      <div style="display:flex;justify-content:space-between;gap:12px;align-items:center;padding:12px;border:1px solid rgba(148,163,184,.25);border-radius:14px;background:#fff;margin-top:10px;">
        <div>
          <div style="font-weight:900;">${escapeHtml(r.type)}</div>
          <div class="so-card-sub">${escapeHtml(r.detail)}</div>
        </div>
        <div style="display:flex;gap:10px;align-items:center;">
          <div class="so-card-sub">${escapeHtml(r.when || "")}</div>
          <button class="so-btn" data-action="noop" type="button">Deny</button>
          <button class="so-btn" data-action="noop" type="button">Approve</button>
        </div>
      </div>`).join("");
    return `
      <div class="card" style="margin-top:14px;">
        <div style="font-weight:900;font-size:18px;">Requests</div>
	        <div style="margin-top:10px;" class="so-card-sub">Pending requests from the child device (Local mode, not synced).</div>
        ${items}
      </div>`;
  }

  function renderActivityTab(child) {
    const prof = ensureProfile(child);
    const acts = Array.isArray(prof.activity) ? prof.activity : [];

    // Local Mode: hydrate activity feed from SSOT (async-safe; never blocks render)
    if (state?.api?.available && isGuid(child.id) && !state.activityLoaded[child.id]) {
      state.activityLoaded[child.id] = true;
      setTimeout(() => {
        Promise.resolve(refreshActivityFromApi(child.id))
          .then((ok) => { if (ok) window.Safe0neRouter?.render?.(); })
          .catch(() => {});
      }, 0);
    }

    const rows = (acts.length ? acts : [
      { event: "Activity", item: "No events yet", time: "—", status: "Awaiting telemetry" },
    ]).map((a) => `
      <div class="tr">
        <div>${escapeHtml(a.event)}</div>
        <div><strong>${escapeHtml(a.item)}</strong></div>
        <div class="so-card-sub">${escapeHtml(a.time)}</div>
        <div><span class="so-pill">${escapeHtml(a.status)}</span></div>
      </div>`).join("");
    return `
      <div class="card" style="margin-top:14px;">
        <div style="font-weight:900;font-size:18px;">Activity</div>
        <div style="margin-top:10px;display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;">
          <div class="so-card-sub">Recent activity from the child device (Local mode, not synced).</div>
          <button class="so-btn" data-action="refreshActivity" data-childid="${escapeHtml(child.id)}" type="button">Refresh</button>
        </div>
        <div class="table" style="margin-top:12px;">
          <div class="tr th"><div>Event</div><div>App/Site</div><div>Time</div><div>Status</div></div>
          ${rows}
        </div>
      </div>`;
  }

  function ensureLocationPolicy(prof) {
  prof.policy = prof.policy || {};
  prof.policy.location = prof.policy.location || {};
  if (!Array.isArray(prof.policy.location.geofences)) prof.policy.location.geofences = [];
  // Normalize additive fields
  prof.policy.location.geofences = prof.policy.location.geofences.map((g) => ({
    id: g.id || `gf_${Math.random().toString(16).slice(2)}`,
    name: (g.name || "Geofence").slice(0, 64),
    latitude: typeof g.latitude === "number" ? g.latitude : (typeof g.lat === "number" ? g.lat : 0),
    longitude: typeof g.longitude === "number" ? g.longitude : (typeof g.lon === "number" ? g.lon : 0),
    radiusMeters: typeof g.radiusMeters === "number" ? g.radiusMeters : (typeof g.radius === "number" ? g.radius : 200),
    mode: (g.mode === "outside") ? "outside" : "inside",
  }));
}


function geoPanelHtml(childId) {
  const cid = String(childId || "");
  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);
  const geos = prof.policy.location.geofences;
  const ui = geoGetLeafletState();
  if (!ui.panelModeByChild) ui.panelModeByChild = {};
  if (!ui.pendingNameByChild) ui.pendingNameByChild = {};
  const selId = ui.selectedIdByChild?.[cid] || "";
  const selected = geos.find((g) => String(g.id) === String(selId)) || null;

  const mode = ui.panelModeByChild[cid] || (selected ? "edit" : "list");
  ui.panelModeByChild[cid] = mode;

  const pendingName = String(ui.pendingNameByChild[cid] || "");

  const listItems = geos.length
    ? geos
        .map((g) => {
          const isSel = selected && String(selected.id) === String(g.id);
          return `
          <button type="button" class="so-btn" style="width:100%;justify-content:space-between;gap:10px;${isSel ? "border:2px solid rgba(249,115,22,.55);background:rgba(249,115,22,.08);" : ""}"
            data-action="geoPanelSelect" data-childid="${escapeHtml(cid)}" data-gf-id="${escapeHtml(g.id)}">
            <span style="display:flex;flex-direction:column;align-items:flex-start;gap:2px;">
              <span style="font-weight:900;">${escapeHtml(g.name || "Geofence")}</span>
              <span class="so-card-sub">${escapeHtml((g.mode === "outside" ? "Outside" : "Inside"))} • ${escapeHtml(String(g.radiusMeters || 200))}m</span>
            </span>
            <span class="so-pill">${isSel ? "Editing" : "View"}</span>
          </button>`;
        })
        .join("")
    : `<div class="so-card-sub">No geofences yet.</div>`;

  const presetBtns = `
    <div style="display:flex;gap:8px;flex-wrap:wrap;">
      ${[
        ["Home", 150],
        ["School", 300],
        ["Park", 500],
      ]
        .map(
          ([n, r]) =>
            `<button class="so-btn" type="button" data-action="geoPresetName" data-childid="${escapeHtml(
              cid
            )}" data-name="${escapeHtml(n)}" data-radius="${r}">${escapeHtml(n)} ${r}m</button>`
        )
        .join("")}
    </div>`;

  if (mode === "edit" && selected) {
    return `
      <div style="display:flex;justify-content:space-between;align-items:center;gap:10px;">
        <div style="font-weight:900;font-size:16px;">Edit geofence</div>
        <button class="so-btn" type="button" data-action="geoPanelBack" data-childid="${escapeHtml(cid)}">Back</button>
      </div>

      <div class="card" style="margin-top:10px;background:#f8fafc;border:1px solid rgba(148,163,184,.25);">
        <div class="so-field" style="margin:0;">
          <label>Name</label>
          <input class="so-input" data-geoedit="name" data-gf-id="${escapeHtml(selected.id)}" value="${escapeHtml(selected.name || "")}" placeholder="e.g., Home, School" />
        </div>

        <div style="margin-top:10px;display:grid;grid-template-columns:1fr 1fr;gap:10px;align-items:end;">
          <div class="so-field" style="margin:0;">
            <label>Rule</label>
            <select class="so-input" data-geoedit="mode" data-gf-id="${escapeHtml(selected.id)}">
              <option value="inside" ${selected.mode !== "outside" ? "selected" : ""}>Inside</option>
              <option value="outside" ${selected.mode === "outside" ? "selected" : ""}>Outside</option>
            </select>
          </div>
          <div class="so-field" style="margin:0;">
            <label>Radius</label>
            <div style="display:flex;gap:8px;align-items:center;">
              <input class="so-input" style="width:92px;" data-gf-field="radiusMeters" data-gf-id="${escapeHtml(selected.id)}" value="${escapeHtml(String(selected.radiusMeters || 200))}" />
              <span class="so-card-sub">meters</span>
            </div>
          </div>
        </div>

        <div style="margin-top:10px;">
          <input type="range" min="25" max="2000" step="25" style="width:100%;" data-geoedit="radius" data-gf-id="${escapeHtml(selected.id)}" value="${escapeHtml(String(selected.radiusMeters || 200))}" />
          <div style="margin-top:10px;display:flex;gap:8px;flex-wrap:wrap;">
            <button class="so-btn" type="button" data-action="geoRadiusPreset" data-childid="${escapeHtml(cid)}" data-gf-id="${escapeHtml(selected.id)}" data-radius="150">Home 150m</button>
            <button class="so-btn" type="button" data-action="geoRadiusPreset" data-childid="${escapeHtml(cid)}" data-gf-id="${escapeHtml(selected.id)}" data-radius="300">School 300m</button>
            <button class="so-btn" type="button" data-action="geoRadiusPreset" data-childid="${escapeHtml(cid)}" data-gf-id="${escapeHtml(selected.id)}" data-radius="500">Park 500m</button>
          </div>
          <div class="so-card-sub" style="margin-top:10px;">Tip: drag the pin to move. Drag the small handle to resize.</div>
        </div>

        <div style="margin-top:12px;display:flex;gap:10px;flex-wrap:wrap;justify-content:space-between;align-items:center;">
          <div class="so-card-sub">Lat: ${escapeHtml(selected.latitude?.toFixed ? selected.latitude.toFixed(5) : String(selected.latitude))}, Lon: ${escapeHtml(selected.longitude?.toFixed ? selected.longitude.toFixed(5) : String(selected.longitude))}</div>
          <button class="so-btn so-btn-danger" type="button" data-action="deleteGeofence" data-childid="${escapeHtml(cid)}" data-gf-id="${escapeHtml(selected.id)}">Delete</button>
        </div>
      </div>
    `;
  }

  // list mode
  return `
    <div style="display:flex;justify-content:space-between;align-items:center;gap:10px;">
      <div style="font-weight:900;font-size:16px;">Geofences</div>
      <button class="so-btn" type="button" data-action="geoPanelNew" data-childid="${escapeHtml(cid)}">+ New</button>
    </div>

    <div class="so-card-sub" style="margin-top:6px;">Click a geofence to edit. Or create one by placing it on the map.</div>

    <div class="card" style="margin-top:10px;background:#f8fafc;border:1px solid rgba(148,163,184,.25);">
      <div style="font-weight:800;">Create</div>
      <div class="so-card-sub" style="margin-top:6px;">1) Choose a name, 2) click “Place on map”, 3) click the map where it should be.</div>

      <div style="margin-top:10px;display:flex;gap:10px;flex-wrap:wrap;align-items:center;">
        <input class="so-input" style="flex:1;min-width:200px;" placeholder="Name (Home, School…)" data-geo="pendingName" data-childid="${escapeHtml(cid)}" value="${escapeHtml(pendingName)}" />
        <button class="so-btn" type="button" data-action="geoPlaceMode" data-childid="${escapeHtml(cid)}">
          ${ui.placeByChild?.[cid] ? "Click map to place…" : "Place on map"}
        </button>
      </div>

      <div style="margin-top:10px;">${presetBtns}</div>
    </div>

    <div style="margin-top:10px;display:flex;flex-direction:column;gap:10px;">
      ${listItems}
    </div>
  `;
}


function geoSearchCardHtml(childId) {
  const cid = String(childId || "");
  const ui = geoGetLeafletState();
  const r = ui.searchResultByChild?.[cid];
  if (!r) return "";
  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);
  const geos = prof.policy.location.geofences;
  const selId = ui.selectedIdByChild?.[cid] || "";
  const selected = geos.find((g) => g.id === selId) || null;

  return `
    <div class="card" style="margin-top:10px;background:#f8fafc;border:1px solid rgba(148,163,184,.25);">
      <div style="font-weight:900;">Search result</div>
      <div class="so-card-sub" style="margin-top:6px;">${escapeHtml(r.label || "Result")}</div>
      <div style="margin-top:10px;display:flex;gap:10px;flex-wrap:wrap;">
        <button class="so-btn" data-action="geoCenterOnSearch" data-childid="${escapeHtml(cid)}" type="button">Center</button>
        <button class="so-btn" data-action="geoCreateAtSearch" data-childid="${escapeHtml(cid)}" type="button">Create here</button>
        ${selected ? `<button class="so-btn" data-action="geoMoveSelectedToSearch" data-childid="${escapeHtml(cid)}" type="button">Move selected</button>` : ""}
        <button class="so-btn so-btn-danger" data-action="geoClearSearch" data-childid="${escapeHtml(cid)}" type="button">Clear</button>
      </div>
    </div>
  `;
}

function geoUpdateSearchDom(childId) {
  const cid = String(childId || "");
  const el = document.querySelector(`[data-geo="searchcard"][data-childid="${cssEscape(cid)}"]`);
  if (!el) return;
  el.innerHTML = geoSearchCardHtml(cid);
}
function geoUpdatePanelDom(childId) {
  const cid = String(childId || "");
  const el = document.querySelector(`[data-geo="panel"][data-childid="${cssEscape(cid)}"]`);
  if (!el) return;
  el.innerHTML = geoPanelHtml(cid);
}

function renderLocationTab(child) {
  const prof = ensureProfile(child);
  ensureLocationPolicy(prof);

  const loc = prof.location || null;
  const sharing = !!(prof.permissions && prof.permissions.location);
  const geofences = prof.policy.location.geofences;

  // Local Mode: hydrate last known location from SSOT (async-safe; never blocks render)
  if (state?.api?.available && isGuid(child.id) && !state.locationLoaded[child.id]) {
    state.locationLoaded[child.id] = true;
    setTimeout(() => {
      Promise.resolve(refreshLocationFromApi(child.id))
        .then((ok) => { if (ok) window.Safe0neRouter?.render?.(); })
        .catch(() => {});
    }, 0);
  }

  const lastLine = (() => {
    if (!loc) return "—";
    if (loc.available === false) return `Unavailable${loc.capturedAtUtc ? ` (as of ${formatWhen(loc.capturedAtUtc)})` : ""}`;
    if (typeof loc.latitude === "number" && typeof loc.longitude === "number") {
      const coords = `${loc.latitude.toFixed(4)}, ${loc.longitude.toFixed(4)}`;
      const acc = (typeof loc.accuracyMeters === "number") ? ` (±${Math.round(loc.accuracyMeters)}m)` : "";
      const when = loc.capturedAtUtc ? ` — ${formatWhen(loc.capturedAtUtc)}` : "";
      return `${coords}${acc}${when}`;
    }
    return loc.capturedAtUtc ? formatWhen(loc.capturedAtUtc) : "—";
  })();

  // Choose map center: current location if available, else first geofence coords.
  const center = (() => {
    if (loc && typeof loc.latitude === "number" && typeof loc.longitude === "number") return { lat: loc.latitude, lon: loc.longitude, src: "device" };
    const g0 = geofences.find((g) => typeof g.latitude === "number" && typeof g.longitude === "number");
    if (g0) return { lat: g0.latitude, lon: g0.longitude, src: "geofence" };
    return { lat: 0, lon: 0, src: "default" };
  })();

  const ui = geoGetLeafletState();
  const selId = ui.selectedIdByChild?.[child.id] || "";
  const selected = geofences.find((g) => g.id === selId) || null;

  return `
    <div class="card" style="margin-top:14px;">
      <div style="font-weight:900;font-size:18px;">Location</div>
      <div style="margin-top:10px;" class="so-card-sub">Location data is shown when the child device is paired and sharing is enabled.</div>
      <div style="margin-top:10px;display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;">
        <div class="so-card-sub">Last known location from the local control plane (not synced).</div>
        <button class="so-btn" data-action="refreshLocation" data-childid="${escapeHtml(child.id)}" type="button">Refresh</button>
      </div>
      <div class="card" style="margin-top:12px;background:#f8fafc;border:1px solid rgba(148,163,184,.25);">
        <div style="font-weight:900;">Sharing: <span class="so-pill">${sharing ? "On" : "Off"}</span></div>
        <div class="so-card-sub" style="margin-top:6px;">Last known: ${escapeHtml(lastLine)}</div>
      </div>
    </div>

    <div class="card" style="margin-top:14px;">
      <div style="display:flex;justify-content:space-between;align-items:flex-start;gap:12px;flex-wrap:wrap;">
        <div>
          <div style="font-weight:900;font-size:18px;">Geofencing</div>
          <div class="so-card-sub" style="margin-top:6px;">Map center: ${center.lat.toFixed(5)}, ${center.lon.toFixed(5)} (${center.src})</div>
          <div class="so-card-sub" style="margin-top:6px;">Click a geofence to edit. Drag the <b>pin</b> to move, drag the <b>handle</b> to resize.</div>
        </div>
        <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;justify-content:flex-end;">
          <button class="so-btn" data-action="geoCenterOnChild" data-childid="${escapeHtml(child.id)}" type="button">Center on child</button>
          <button class="so-btn" data-action="geoMinMax" data-childid="${escapeHtml(child.id)}" type="button">${ui.minByChild?.[child.id] ? "Maximise map" : "Minimise map"}</button>
        </div>
      </div>

      <div style="margin-top:12px;display:flex;gap:12px;flex-wrap:wrap;align-items:flex-start;">
        <div style="flex:2;min-width:340px;${ui.minByChild?.[child.id] ? "display:none;" : ""}">
          <div style="margin-bottom:10px;display:flex;gap:10px;flex-wrap:wrap;align-items:center;">
            <div style="flex:1;min-width:220px;">
              <input class="so-input" style="width:100%;" placeholder="Search address / postcode (e.g., SW1A 2AA)" data-geo="searchQuery" data-childid="${escapeHtml(child.id)}" value="${escapeHtml(ui.searchQueryByChild?.[child.id]||"")}" />
            </div>
            <button class="so-btn" data-action="geoSearch" data-childid="${escapeHtml(child.id)}" type="button">Search</button>
            <select class="so-input" style="width:88px;" data-geo="zoom" data-childid="${escapeHtml(child.id)}">
              ${[12,13,14,15,16,17,18].map((z) => `<option value="${z}" ${(ui.zoomByChild?.[child.id]||15)===z?"selected":""}>${z}</option>`).join("")}
            </select>
          </div>

          <div data-geo="searchcard" data-childid="${escapeHtml(child.id)}">
            ${geoSearchCardHtml(child.id)}
          </div>


          <div style="margin-top:10px;position:relative;border-radius:14px;overflow:hidden;border:1px solid rgba(148,163,184,.25);background:#fff;">
            <div data-geo="leaflet" data-childid="${escapeHtml(child.id)}" style="width:100%;height:460px;display:block;background:#eef2f7;"></div>
            <div data-geo="status" data-childid="${escapeHtml(child.id)}" style="position:absolute;left:12px;top:10px;background:rgba(248,250,252,.92);border:1px solid rgba(148,163,184,.25);border-radius:12px;padding:8px 10px;font-size:12px;color:#334155;">
              ${ui.placeByChild?.[child.id] ? "Click on the map to place the new geofence." : (selected ? `Selected: <b>${escapeHtml(selected.name)}</b>` : "Click a geofence to select")}
            </div>
          </div>
        </div>

        <div style="flex:1;min-width:280px;max-width:420px;">
          <div class="card" style="background:#ffffff;border:1px solid rgba(148,163,184,.25);">
            <div data-geo="panel" data-childid="${escapeHtml(child.id)}">
              ${geoPanelHtml(child.id)}
            </div>
          </div>
        </div>
      </div>
    </div>
  `;
}

  function scheduleGeoCanvasInit(child) {
  const cid = String(child?.id || "");
  if (!cid) return;
  if (!state.geoUi) state.geoUi = { selectedIdByChild: {}, zoomByChild: {}, minByChild: {}, viewByChild: {}, placeByChild: {}, searchQueryByChild: {}, searchResultByChild: {} };
  if (!state.geoUi.zoomByChild[cid]) state.geoUi.zoomByChild[cid] = 15;

  // Defer until after DOM is painted.
  setTimeout(() => {
    // Prefer Leaflet when available
    try {
      if (geoLeafletSync(child)) { try { geoUpdatePanelDom(cid); } catch (_) {} return; }
    } catch (_) {}
    const canvas = document.querySelector(`canvas[data-geo="canvas"][data-childid="${cssEscape(cid)}"]`);
    if (!canvas) return;
    if (canvas.__geoBound) {
      geoRedraw(cid);
      return;
    }
    canvas.__geoBound = true;

    // Initialize view center from profile (current location or first geofence) if not set.
    const prof = ensureProfile({ id: cid });
    ensureLocationPolicy(prof);
    const geos = prof.policy.location.geofences;
    const loc = prof.location || null;

    const center = (() => {
      if (loc && typeof loc.latitude === "number" && typeof loc.longitude === "number") return { lat: loc.latitude, lon: loc.longitude };
      const g0 = geos.find((g) => typeof g.latitude === "number" && typeof g.longitude === "number");
      if (g0) return { lat: g0.latitude, lon: g0.longitude };
      return { lat: 0, lon: 0 };
    })();

    if (!state.geoUi.viewByChild[cid]) {
      state.geoUi.viewByChild[cid] = { centerLat: center.lat, centerLon: center.lon, panX: 0, panY: 0 };
    }

    const view = state.geoUi.viewByChild[cid];

    const hitTest = (mx, my) => {
      const r = geoProjectContext(cid, canvas);
      const prof2 = ensureProfile({ id: cid });
      ensureLocationPolicy(prof2);
      const geos2 = prof2.policy.location.geofences;
      const sel = state.geoUi.selectedIdByChild[cid] || "";
      let best = null;

      for (const g of geos2) {
        const p = r.project(g.latitude, g.longitude);
        const radiusPx = r.metersToPixels(g.radiusMeters);
        const dx = mx - p.x, dy = my - p.y;
        const d = Math.sqrt(dx*dx + dy*dy);

        // Center handle
        if (d <= 10) return { kind: "move", id: g.id, start: { mx, my, lat: g.latitude, lon: g.longitude } };

        // Radius handle (near circumference)
        if (Math.abs(d - radiusPx) <= 8) return { kind: "radius", id: g.id, start: { mx, my, radiusMeters: g.radiusMeters } };

        // Select inside circle
        if (d <= radiusPx) {
          best = { kind: "select", id: g.id };
        }
      }

      // Pan if nothing else
      return best || { kind: "pan", start: { mx, my, panX: view.panX, panY: view.panY } };
    };

    let drag = null;

    canvas.addEventListener("mousedown", (ev) => {
      const rect = canvas.getBoundingClientRect();
      const mx = ev.clientX - rect.left;
      const my = ev.clientY - rect.top;
      drag = hitTest(mx, my);

      if (drag.kind === "select") {
        state.geoUi.selectedIdByChild[cid] = drag.id;
        try { geoUpdateSearchDom(cid); } catch (_) {}
      try { geoUpdatePanelDom(cid); } catch (_) {}
        drag = null;
        return;
      }

      if (drag.kind === "move" || drag.kind === "radius") {
        state.geoUi.selectedIdByChild[cid] = drag.id;
        try { geoUpdateSearchDom(cid); } catch (_) {}
      try { geoUpdatePanelDom(cid); } catch (_) {} // update selected label
      }

      ev.preventDefault();
    });

    window.addEventListener("mousemove", (ev) => {
      if (!drag) return;
      const rect = canvas.getBoundingClientRect();
      const mx = ev.clientX - rect.left;
      const my = ev.clientY - rect.top;

      const prof3 = ensureProfile({ id: cid });
      ensureLocationPolicy(prof3);
      const geos3 = prof3.policy.location.geofences;

      if (drag.kind === "pan") {
        const dx = mx - drag.start.mx;
        const dy = my - drag.start.my;
        view.panX = drag.start.panX + dx;
        view.panY = drag.start.panY + dy;
        geoRedraw(cid);
        return;
      }

      const g = geos3.find((x) => x.id === drag.id);
      if (!g) return;

      const r = geoProjectContext(cid, canvas);

      if (drag.kind === "move") {
        const dpx = mx - drag.start.mx;
        const dpy = my - drag.start.my;
        const dMetersX = r.pixelsToMeters(dpx);
        const dMetersY = r.pixelsToMeters(dpy);
        const dLat = -dMetersY / 111320; // approx meters per degree latitude
        const dLon = dMetersX / (111320 * Math.cos((drag.start.lat || 0) * Math.PI / 180) || 1);
        g.latitude = clampNum(drag.start.lat + dLat, -85, 85);
        g.longitude = clampNum(drag.start.lon + dLon, -180, 180);
        setProfiles(getProfiles()); // persist in-memory
        geoRedraw(cid);
        return;
      }

      if (drag.kind === "radius") {
        const p = r.project(g.latitude, g.longitude);
        const dx = mx - p.x, dy = my - p.y;
        const distPx = Math.max(6, Math.sqrt(dx*dx + dy*dy));
        g.radiusMeters = clampNum(Math.round(r.pixelsToMeters(distPx)), 30, 20000);
        setProfiles(getProfiles());
        geoRedraw(cid);
        return;
      }
    });

    window.addEventListener("mouseup", () => { drag = null; });

    canvas.addEventListener("wheel", (ev) => {
      ev.preventDefault();
      const z = state.geoUi.zoomByChild[cid] || 15;
      const next = clampNum(z + (ev.deltaY < 0 ? 1 : -1), 12, 18);
      state.geoUi.zoomByChild[cid] = next;
      geoRedraw(cid);
    }, { passive: false });

    geoRedraw(cid);
  }, 0);
}

function geoProjectContext(childId, canvas) {
  const cid = String(childId || "");
  const z = state.geoUi?.zoomByChild?.[cid] || 15;
  const view = state.geoUi?.viewByChild?.[cid] || { centerLat: 0, centerLon: 0, panX: 0, panY: 0 };
  const w = canvas.width;
  const h = canvas.height;

  // Scale: meters per pixel (very rough, WebMercator-ish).
  const metersPerPx = 156543.03392 * Math.cos(view.centerLat * Math.PI / 180) / Math.pow(2, z);

  const project = (lat, lon) => {
    const dLat = (lat - view.centerLat);
    const dLon = (lon - view.centerLon);
    const metersY = dLat * 111320;
    const metersX = dLon * (111320 * Math.cos(view.centerLat * Math.PI / 180));
    const x = (w / 2) + (metersX / metersPerPx) + view.panX;
    const y = (h / 2) - (metersY / metersPerPx) + view.panY;
    return { x, y };
  };

  return {
    z,
    view,
    metersPerPx,
    project,
    metersToPixels: (m) => m / metersPerPx,
    pixelsToMeters: (px) => px * metersPerPx,
  };
}


function geoLeafletAvailable() {
  return !!(window.L && typeof window.L.map === "function");
}

function geoGetLeafletState() {
  if (!state.geoUi) state.geoUi = { selectedIdByChild: {}, zoomByChild: {}, minByChild: {}, viewByChild: {}, leafletByChild: {}, placeByChild: {}, searchQueryByChild: {}, searchResultByChild: {} };
  if (!state.geoUi.leafletByChild) state.geoUi.leafletByChild = {};
  return state.geoUi;
}

function geoSetStatus(cid, html) {
  const el = document.querySelector(`div[data-geo="status"][data-childid="${cssEscape(cid)}"]`);
  if (el) el.innerHTML = html;
}

// Destination point given start lat/lon, bearing degrees, distance meters
function geoDestPoint(lat, lon, bearingDeg, distM) {
  const R = 6378137;
  const brng = (bearingDeg * Math.PI) / 180;
  const d = distM / R;
  const lat1 = (lat * Math.PI) / 180;
  const lon1 = (lon * Math.PI) / 180;
  const lat2 = Math.asin(Math.sin(lat1) * Math.cos(d) + Math.cos(lat1) * Math.sin(d) * Math.cos(brng));
  const lon2 = lon1 + Math.atan2(Math.sin(brng) * Math.sin(d) * Math.cos(lat1), Math.cos(d) - Math.sin(lat1) * Math.sin(lat2));
  return { lat: (lat2 * 180) / Math.PI, lon: (((lon2 * 180) / Math.PI + 540) % 360) - 180 };
}

function geoHaversineMeters(lat1, lon1, lat2, lon2) {
  const R = 6378137;
  const toRad = (x) => (x * Math.PI) / 180;
  const dLat = toRad(lat2 - lat1);
  const dLon = toRad(lon2 - lon1);
  const a = Math.sin(dLat / 2) ** 2 + Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) ** 2;
  const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  return R * c;
}

function geoUpdateInputs(gid, patch) {
  const set = (field, val) => {
    const el = document.querySelector(`input[data-gf-field="${field}"][data-gf-id="${cssEscape(gid)}"]`);
    if (el) el.value = String(val);
  };
  if (patch.latitude != null) set("latitude", Number(patch.latitude).toFixed(6));
  if (patch.longitude != null) set("longitude", Number(patch.longitude).toFixed(6));
  if (patch.radiusMeters != null) set("radiusMeters", Math.max(1, Math.round(Number(patch.radiusMeters))));
}

function geoEnsureLeafletMap(child) {
  const cid = String(child?.id || "");
  if (!cid) return null;
  if (!geoLeafletAvailable()) return null;

  const div = document.querySelector(`div[data-geo="leaflet"][data-childid="${cssEscape(cid)}"]`);
  if (!div) return null;

  const ui = geoGetLeafletState();
  const existing = ui.leafletByChild[cid];
  if (existing && existing.div === div) return existing;

  // If DOM got re-rendered, destroy old map if needed
  if (existing && existing.map) {
    try { existing.map.remove(); } catch (_) {}
  }

  // Determine center
  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);
  const geos = prof.policy.location.geofences;
  const loc = prof.location || null;

  const center = (() => {
    if (loc && typeof loc.latitude === "number" && typeof loc.longitude === "number") return [loc.latitude, loc.longitude];
    const g0 = geos.find((g) => typeof g.latitude === "number" && typeof g.longitude === "number");
    if (g0) return [g0.latitude, g0.longitude];
    return [0, 0];
  })();

  const zoom = ui.zoomByChild[cid] || 15;

  const map = window.L.map(div, { zoomControl: true, attributionControl: true });
  map.setView(center, zoom);

  window.L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
    maxZoom: 19,
    attribution: "&copy; OpenStreetMap contributors"
  }).addTo(map);

  const st = { div, map, layersById: {}, childId: cid };
  ui.leafletByChild[cid] = st;

  // Keep UI zoom in sync
  map.on("zoomend", () => {
    ui.zoomByChild[cid] = map.getZoom();
    const zSel = document.querySelector(`select[data-geo="zoom"][data-childid="${cssEscape(cid)}"]`);
    if (zSel) zSel.value = String(ui.zoomByChild[cid]);
  });

  // One-time click handler for "add on map" placement mode.
  // IMPORTANT: the map DOM can be re-rendered, which recreates the Leaflet map.
  // We must (re)bind the click handler on every new map instance; otherwise
  // "Place on map" appears to do nothing.
  map.on("click", (ev) => {
    try {
      if (!ui.placeByChild?.[cid]) return;
      ui.placeByChild[cid] = false;

      if (!ui.pendingNameByChild) ui.pendingNameByChild = {};
      if (!ui.pendingRadiusByChild) ui.pendingRadiusByChild = {};
      const pendingName = String(ui.pendingNameByChild[cid] || "").trim();
      const pendingRadius = clampNum(parseFloat(String(ui.pendingRadiusByChild[cid] || "200")), 30, 20000);

      const p = ev?.latlng;
      if (!p) return;

      const created = geoCreateGeofenceAt(cid, p.lat, p.lng, {
        radiusMeters: pendingRadius || 200,
        mode: "inside",
        name: pendingName || undefined,
      });

      if (created?.id) ui.selectedIdByChild[cid] = created.id;
      if (!ui.panelModeByChild) ui.panelModeByChild = {};
      ui.panelModeByChild[cid] = "edit";

      geoSetStatus(cid, "Placed geofence.");
      try { geoUpdatePanelDom(cid); } catch (_) {}
      try { geoLeafletSync({ id: cid }); } catch (_) {}
    } catch (_) {}
  });

  return st;
}

function geoLeafletSync(child) {
  const cid = String(child?.id || "");
  const st = geoEnsureLeafletMap(child);
  if (!st) return false;

  const ui = geoGetLeafletState();
  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);
  const geos = prof.policy.location.geofences;
  const selId = ui.selectedIdByChild?.[cid] || "";

  // Marker icons (normal vs selected)
  st._icons = st._icons || {};
  if (!st._icons.normal) {
    st._icons.normal = window.L.divIcon({
      className: "so-geo-pin",
      html: '<div style="width:14px;height:14px;border-radius:999px;background:#3b82f6;border:2px solid #ffffff;box-shadow:0 6px 16px rgba(15,23,42,.25);"></div>',
      iconSize: [14, 14],
      iconAnchor: [7, 7],
    });
    st._icons.selected = window.L.divIcon({
      className: "so-geo-pin sel",
      html: '<div style="width:16px;height:16px;border-radius:999px;background:#f97316;border:3px solid #ffffff;box-shadow:0 8px 22px rgba(15,23,42,.28);"></div>',
      iconSize: [16, 16],
      iconAnchor: [8, 8],
    });
    st._icons.handle = window.L.divIcon({
      className: "so-geo-handle",
      html: '<div style="width:10px;height:10px;border-radius:999px;background:#94a3b8;border:2px solid #ffffff;box-shadow:0 6px 14px rgba(15,23,42,.18);"></div>',
      iconSize: [10, 10],
      iconAnchor: [5, 5],
    });
  }

  // Remove deleted
  Object.keys(st.layersById).forEach((gid) => {
    if (!geos.some((g) => String(g.id) === String(gid))) {
      const Ls = st.layersById[gid];
      try { Ls.marker?.remove(); } catch (_) {}
      try { Ls.circle?.remove(); } catch (_) {}
      try { Ls.handle?.remove(); } catch (_) {}
      delete st.layersById[gid];
    }
  });

  const onSelect = (gid) => {
    ui.selectedIdByChild[cid] = gid;
    if (!ui.panelModeByChild) ui.panelModeByChild = {};
    ui.panelModeByChild[cid] = "edit";
    const g = geos.find((x) => String(x.id) === String(gid));
    geoSetStatus(cid, g ? `Selected: <b>${escapeHtml(g.name || "Geofence")}</b> (${escapeHtml(g.mode || "inside")})` : "Click a geofence to select");
    try { geoUpdatePanelDom(cid); } catch (_) {}
    setTimeout(() => { try { geoLeafletSync({ id: cid }); } catch (_) {} }, 0);
  };

  geos.forEach((g) => {
    const gid = String(g.id || "");
    const lat = typeof g.latitude === "number" ? g.latitude : 0;
    const lon = typeof g.longitude === "number" ? g.longitude : 0;
    const radius = typeof g.radiusMeters === "number" ? g.radiusMeters : 200;

    let Ls = st.layersById[gid];
    if (!Ls) {
      const marker = window.L.marker([lat, lon], { draggable: true, icon: st._icons.normal }).addTo(st.map);
      const circle = window.L.circle([lat, lon], { radius }).addTo(st.map);

      // Radius handle at east bearing
      const hp = geoDestPoint(lat, lon, 90, radius);
      const handle = window.L.marker([hp.lat, hp.lon], { draggable: true, opacity: 0.95, icon: st._icons.handle }).addTo(st.map);

      marker.on("dragstart", () => onSelect(gid));
      circle.on("click", () => onSelect(gid));
      marker.on("click", () => onSelect(gid));
      handle.on("dragstart", () => onSelect(gid));

      marker.on("drag", (ev) => {
        const p = ev.target.getLatLng();
        g.latitude = p.lat;
        g.longitude = p.lng;
        circle.setLatLng(p);
        const hp2 = geoDestPoint(p.lat, p.lng, 90, g.radiusMeters ?? radius);
        handle.setLatLng([hp2.lat, hp2.lon]);
        geoUpdateInputs(gid, { latitude: p.lat, longitude: p.lng });
      });

      marker.on("dragend", () => {
        // keep status updated
        const gg = geos.find((x) => String(x.id) === gid);
        geoSetStatus(cid, gg ? `Selected: <b>${escapeHtml(gg.name || "Geofence")}</b> (${escapeHtml(gg.mode || "inside")})` : "Click a circle to select");
      });

      handle.on("drag", (ev) => {
        const p = ev.target.getLatLng();
        const c = marker.getLatLng();
        const dist = geoHaversineMeters(c.lat, c.lng, p.lat, p.lng);
        const r = Math.max(10, Math.round(dist));
        g.radiusMeters = r;
        circle.setRadius(r);
        const hp2 = geoDestPoint(c.lat, c.lng, 90, r);
        handle.setLatLng([hp2.lat, hp2.lon]);
        geoUpdateInputs(gid, { radiusMeters: r });
      });

      handle.on("dragend", () => {
        const c = marker.getLatLng();
        const r = g.radiusMeters ?? radius;
        const hp2 = geoDestPoint(c.lat, c.lng, 90, r);
        handle.setLatLng([hp2.lat, hp2.lon]);
      });

      Ls = { marker, circle, handle };
      st.layersById[gid] = Ls;
    } else {
      // update existing
      Ls.marker.setLatLng([lat, lon]);
      Ls.circle.setLatLng([lat, lon]);
      Ls.circle.setRadius(radius);
      const hp = geoDestPoint(lat, lon, 90, radius);
      Ls.handle.setLatLng([hp.lat, hp.lon]);
    }

    // Apply selection visuals (circle + marker)
    const isSel = selId && String(selId) === gid;
    try {
      Ls.circle.setStyle(isSel
        ? { color: "#f97316", weight: 4, fillColor: "#fb923c", fillOpacity: 0.20 }
        : { color: "#2563eb", weight: 3, fillColor: "#3b82f6", fillOpacity: 0.10 });
    } catch (_) {}
    try { Ls.marker.setIcon(isSel ? st._icons.selected : st._icons.normal); } catch (_) {}
    try { if (isSel) { Ls.circle.bringToFront(); Ls.marker.bringToFront(); Ls.handle.bringToFront(); } } catch (_) {}
  });

  // status default
  if (selId) {
    const g = geos.find((x) => String(x.id) === String(selId));
    geoSetStatus(cid, g ? `Selected: <b>${escapeHtml(g.name || "Geofence")}</b> (${escapeHtml(g.mode || "inside")})` : "Click a circle to select");
  } else {
    geoSetStatus(cid, "Click a circle to select");
  }

  // Ensure map zoom matches UI state
  const z = ui.zoomByChild[cid] || 15;
  if (st.map.getZoom() !== z) st.map.setZoom(z);

  return true;
}

function geoLeafletPanTo(childId, lat, lon, zoomOpt) {
  const cid = String(childId || "");
  const ui = geoGetLeafletState();
  const st = ui.leafletByChild?.[cid];
  if (!st?.map) return false;
  try {
    const z = (typeof zoomOpt === "number") ? zoomOpt : st.map.getZoom();
    st.map.setView([lat, lon], z, { animate: true });
    return true;
  } catch {
    return false;
  }
}

function geoCreateGeofenceAt(childId, lat, lon, opts) {
  const cid = String(childId || "");
  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);
  const geos = prof.policy.location.geofences;
  const name = String(opts?.name || `Geofence ${geos.length + 1}`);
  const radius = clampNum(parseFloat(opts?.radiusMeters ?? 200), 30, 20000);
  const mode = (String(opts?.mode) === "outside") ? "outside" : "inside";
  const g = {
    id: `gf_${Math.random().toString(16).slice(2)}`,
    name: name.slice(0, 64),
    latitude: clampNum(parseFloat(lat), -85, 85),
    longitude: clampNum(parseFloat(lon), -180, 180),
    radiusMeters: radius,
    mode,
  };
  geos.push(g);
  state.geoUi.selectedIdByChild[cid] = g.id;
  setProfiles(getProfiles());
  geoRedraw(cid);
  return g;
}

async function geoNominatimSearch(query) {
  const q = String(query || "").trim();
  if (!q) return null;
  const url = `https://nominatim.openstreetmap.org/search?format=json&limit=1&q=${encodeURIComponent(q)}`;
  const res = await fetch(url, { headers: { "Accept": "application/json" } });
  if (!res.ok) return null;
  const data = await res.json();
  const row = Array.isArray(data) ? data[0] : null;
  if (!row) return null;
  const lat = parseFloat(row.lat);
  const lon = parseFloat(row.lon);
  if (!isFinite(lat) || !isFinite(lon)) return null;
  const label = String(row.display_name || q);
  return { lat, lon, label };
}


function geoRedraw(childId) {
  const cid = String(childId || "");
  // Prefer Leaflet map when available.
  try {
    const child = { id: cid };
    if (geoLeafletSync(child)) return;
  } catch (_) {}

  const canvas = document.querySelector(`canvas[data-geo="canvas"][data-childid="${cssEscape(cid)}"]`);
  if (!canvas) return;

  const ctx = canvas.getContext("2d");
  if (!ctx) return;

  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);

  const geos = prof.policy.location.geofences;
  const loc = prof.location || null;

  const r = geoProjectContext(cid, canvas);

  // Background
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = "#ffffff";
  ctx.fillRect(0, 0, canvas.width, canvas.height);

  // Grid (acts as our offline \"map\" surface)
  ctx.save();
  ctx.strokeStyle = "rgba(148,163,184,.35)";
  ctx.lineWidth = 1;
  const step = 80;
  for (let x = (r.view.panX % step + step) % step; x < canvas.width; x += step) {
    ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, canvas.height); ctx.stroke();
  }
  for (let y = (r.view.panY % step + step) % step; y < canvas.height; y += step) {
    ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(canvas.width, y); ctx.stroke();
  }
  // Crosshair center
  ctx.strokeStyle = "rgba(15,23,42,.35)";
  ctx.lineWidth = 2;
  ctx.beginPath(); ctx.moveTo(canvas.width/2 + r.view.panX, 0); ctx.lineTo(canvas.width/2 + r.view.panX, canvas.height); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(0, canvas.height/2 + r.view.panY); ctx.lineTo(canvas.width, canvas.height/2 + r.view.panY); ctx.stroke();
  ctx.restore();

  // Device current location pin
  if (loc && typeof loc.latitude === "number" && typeof loc.longitude === "number") {
    const p = r.project(loc.latitude, loc.longitude);
    ctx.fillStyle = "#16a34a";
    ctx.beginPath(); ctx.arc(p.x, p.y, 6, 0, Math.PI*2); ctx.fill();
    ctx.strokeStyle = "#065f46";
    ctx.lineWidth = 2;
    ctx.beginPath(); ctx.arc(p.x, p.y, 10, 0, Math.PI*2); ctx.stroke();
  }

  const sel = state.geoUi?.selectedIdByChild?.[cid] || "";

  // Geofences
  for (const g of geos) {
    const p = r.project(g.latitude, g.longitude);
    const radiusPx = r.metersToPixels(g.radiusMeters);

    // Circle
    ctx.strokeStyle = (g.id === sel) ? "#2563eb" : "rgba(37,99,235,.7)";
    ctx.lineWidth = (g.id === sel) ? 4 : 3;
    ctx.beginPath();
    ctx.arc(p.x, p.y, Math.max(8, radiusPx), 0, Math.PI * 2);
    ctx.stroke();

    // Pin
    ctx.fillStyle = (g.id === sel) ? "#1d4ed8" : "#3b82f6";
    ctx.beginPath(); ctx.arc(p.x, p.y, 6, 0, Math.PI*2); ctx.fill();

    // Label
    ctx.fillStyle = "rgba(15,23,42,.85)";
    ctx.font = "14px system-ui, -apple-system, Segoe UI, Roboto, Arial";
    ctx.fillText(g.name || "Geofence", p.x + 10, p.y - 10);

    // Mode tag
    ctx.fillStyle = (g.mode === "outside") ? "rgba(239,68,68,.9)" : "rgba(16,185,129,.9)";
    ctx.font = "12px system-ui, -apple-system, Segoe UI, Roboto, Arial";
    ctx.fillText(g.mode === "outside" ? "Outside" : "Inside", p.x + 10, p.y + 10);
  }
}

function clampNum(n, lo, hi) {
  const x = Number(n);
  if (!Number.isFinite(x)) return lo;
  return Math.max(lo, Math.min(hi, x));
}

function cssEscape(s) {
  return String(s).replace(/[^a-zA-Z0-9_\\-]/g, (c) => `\\${c.codePointAt(0).toString(16)} `);
}


function renderChildProfile(route) {
    const id = String(route?.params?.id || "");
    const child = getChildren().find((c) => c.id === id) || demoChild();
    const profile = ensureProfile(child);

    // Local Mode: hydrate Settings profile from SSOT (async-safe; never blocks render)
    if (state?.api?.available && isGuid(id) && !state.profilesLoaded[id]) {
      state.profilesLoaded[id] = true;
      setTimeout(() => {
        Promise.resolve(refreshProfileFromApi(id))
          .then((ok) => { if (ok) window.Safe0neRouter?.render?.(); })
          .catch(() => {});
      }, 0);
    }

// Local Mode: hydrate Devices + active pairing status from SSOT (async-safe)
if (state?.api?.available && isGuid(id) && !state.devicesLoaded[id]) {
  state.devicesLoaded[id] = true;
  setTimeout(() => {
    Promise.resolve(refreshDevicesFromApi(id))
      .then(() => Promise.resolve(refreshPairingFromApi(id)))
      .then(() => window.Safe0neRouter?.render?.())
      .catch(() => {});
  }, 0);
}

    const g = genderMeta(child.gender);
    const tab = getChildTab(child.id);

    scheduleGeoCanvasInit(child);

    return `<div class="page">
      ${styles()}
      <div style="display:flex;justify-content:space-between;align-items:center;gap:12px;">
        <div>
          <div class="page-title">Child Profile</div>
          <div class="page-subtitle">Manage devices, policies, and activity for this child.</div>
        </div>
        <button class="so-btn" data-action="backToChildren">Back</button>
      </div>

      <div class="card" style="margin-top:14px;">
        <div style="display:flex;gap:16px;align-items:center;">
          ${avatarHtml(child, 86)}
          <div>
            <div style="font-size:22px;font-weight:900;">${escapeHtml(child.name)}</div>
            <div style="margin-top:6px;color:#64748b;font-weight:800;">Child ID: ${escapeHtml(child.id)}</div>
            <div style="margin-top:10px;display:flex;gap:10px;flex-wrap:wrap;">
              <span class="so-pill ${g.cls}"><span class="so-gender-symbol">${g.symbol}</span>&nbsp;${g.label}</span>
              <span class="so-pill">${escapeHtml(child.ageGroup || "—")}</span>
            </div>
          </div>
        </div>
      </div>


      <div class="cp-tabs" role="tablist" aria-label="Child profile tabs" style="margin-top:14px;">
        <button class="cp-tab ${tab === 'settings' ? 'active' : ''}" data-action="setChildTab" data-childid="${escapeHtml(child.id)}" data-tab="settings" type="button">Settings</button>
        <button class="cp-tab ${tab === 'devices' ? 'active' : ''}" data-action="setChildTab" data-childid="${escapeHtml(child.id)}" data-tab="devices" type="button">Devices</button>
        <button class="cp-tab ${tab === 'requests' ? 'active' : ''}" data-action="setChildTab" data-childid="${escapeHtml(child.id)}" data-tab="requests" type="button">Requests</button>
        <button class="cp-tab ${tab === 'activity' ? 'active' : ''}" data-action="setChildTab" data-childid="${escapeHtml(child.id)}" data-tab="activity" type="button">Activity</button>
        <button class="cp-tab ${tab === 'location' ? 'active' : ''}" data-action="setChildTab" data-childid="${escapeHtml(child.id)}" data-tab="location" type="button">Location</button>
      </div>

      <div class="cp-panel ${tab === 'settings' ? 'active' : ''}" data-tab="settings">

      ${state?.api?.saveFailedByChildId?.[id] ? `<div class="so-banner-warn">Not saved to SSOT (server unavailable). Your changes are local only.</div>` : ``}


      <div class="card" style="margin-top:14px;">
        <div style="font-weight:900;font-size:18px;">Permissions</div>
        <div style="margin-top:10px;display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px;">
          ${permToggle("web", "Web filtering", profile.permissions.web)}
          ${permToggle("apps", "App controls", profile.permissions.apps)}
          ${permToggle("bedtime", "Bedtime enforcement", profile.permissions.bedtime)}
          ${permToggle("location", "Location sharing", profile.permissions.location)}
          ${permToggle("purchases", "Purchases", profile.permissions.purchases)}
        </div>
      </div>

      <div class="card" style="margin-top:14px;">
        <div style="font-weight:900;font-size:18px;">Alerts routing</div>
        <div style="margin-top:8px;color:#64748b;font-size:12px;">Choose where alerts for this child should surface. This is additive and best-effort; enforcement is wired later.</div>
        <div style="margin-top:10px;display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:10px;">
          ${alertRouteToggle("inboxEnabled", "Show in Alerts inbox", profile?.policy?.alerts?.routing?.inboxEnabled !== false)}
          ${alertRouteToggle("notifyEnabled", "Allow notifications (planned)", !!profile?.policy?.alerts?.routing?.notifyEnabled)}
        </div>
      </div>

      <div class="card" style="margin-top:14px;">
        <div style="font-weight:900;font-size:18px;">Limits</div>
        <div style="margin-top:12px;display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:12px;">
          <div class="so-field"><label>Screen minutes / day</label>
            <input type="number" min="0" max="1440" step="5" data-field="screenMinutesPerDay" value="${escapeHtml(
              profile.limits.screenMinutesPerDay
            )}"/>
          </div>

          <div class="so-field"><label>Grace minutes (after limit)</label>
            <input type="number" min="0" max="120" step="1" data-field="graceMinutes" value="${escapeHtml(
              Number(profile?.policy?.timeBudget?.graceMinutes ?? 0)
            )}"/>
            <div style="margin-top:6px;color:#64748b;font-size:12px;">Optional buffer before Lockdown (PATCH 16R). 0 = no grace.</div>
          </div>

          <div class="so-field"><label>Warnings at minutes remaining</label>
            <input type="text" placeholder="5,1" data-field="warnAtMinutes" value="${escapeHtml(
              Array.isArray(profile?.policy?.timeBudget?.warnAtMinutes)
                ? profile.policy.timeBudget.warnAtMinutes.join(",")
                : (Array.isArray(profile?.policy?.timeBudget?.warnMinutesRemaining)
                  ? profile.policy.timeBudget.warnMinutesRemaining.join(",")
                  : "5,1")
            )}"/>
            <div style="margin-top:6px;color:#64748b;font-size:12px;">Comma-separated minutes before limit (e.g., 10,5,1). Defaults to 5,1.</div>
          </div>

<div style="grid-column:1/-1;">
  <details>
    <summary style="cursor:pointer;font-weight:800;color:#334155;">Per-day overrides (optional)</summary>
    <div style="margin-top:10px;display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px;">
      ${dayOverrideField("Mon", profile)}
      ${dayOverrideField("Tue", profile)}
      ${dayOverrideField("Wed", profile)}
      ${dayOverrideField("Thu", profile)}
      ${dayOverrideField("Fri", profile)}
      ${dayOverrideField("Sat", profile)}
      ${dayOverrideField("Sun", profile)}
    </div>
    <div style="margin-top:8px;color:#64748b;font-size:12px;">Leave blank to use the daily value.</div>
  </details>
</div>
          <div class="so-field"><label>Bedtime start</label>
            <input type="time" data-field="bedtimeStart" value="${escapeHtml(profile.limits.bedtimeStart)}"/>
          </div>
          <div class="so-field"><label>Bedtime end</label>
            <input type="time" data-field="bedtimeEnd" value="${escapeHtml(profile.limits.bedtimeEnd)}"/>
          </div>
        </div>
      </div>

      
      ${renderPerAppLimitsCard(child, profile)}

<div class="card" style="margin-top:14px;display:flex;justify-content:flex-end;gap:12px;">
        <button class="so-btn" data-action="resetProfile" data-childid="${escapeHtml(child.id)}">Reset</button>
        <button class="so-btn" data-action="saveProfile" data-childid="${escapeHtml(child.id)}">Save</button>
      </div>
      </div>

      <div class="cp-panel ${tab === 'devices' ? 'active' : ''}" data-tab="devices">
        ${renderDevicesTab(child)}
      </div>

      <div class="cp-panel ${tab === 'requests' ? 'active' : ''}" data-tab="requests">
        ${renderRequestsTab(child)}
      </div>

      <div class="cp-panel ${tab === 'activity' ? 'active' : ''}" data-tab="activity">
        ${renderActivityTab(child)}
      </div>

      <div class="cp-panel ${tab === 'location' ? 'active' : ''}" data-tab="location">
        ${renderLocationTab(child)}
      </div>
    </div>`;
  }

  // --- Modal / actions (delegated, robust) ---
  const state = {
    draft: null,
    showArchived: readBool(LS_SHOW_ARCHIVED, false),
    api: { available: false, children: null, inFlight: false, saveFailedByChildId: {}, devicesByChildId: {}, pairingByChildId: {}, statusByChildId: {}, statusInFlight: {}, devicesInFlight: {}, pairingInFlight: {}, activityInFlight: {}, locationInFlight: {} },
    profilesLoaded: {},
    devicesLoaded: {},
    activityLoaded: {},
    locationLoaded: {},
    geoUi: { selectedIdByChild: {}, zoomByChild: {}, minByChild: {}, viewByChild: {} },
    refreshScheduled: false,
  };
  function closeModal() {
    document.getElementById("so-modal-backdrop")?.remove();
    state.draft = null;
  }
  function navigate(hash) {
    if (!hash.startsWith("#/")) hash = "#/" + hash.replace(/^#*/, "");
    location.hash = hash;
  }

  function rerenderGrid() {
    const grid = document.getElementById("children-grid");
    if (!grid) return;
    grid.innerHTML = getChildren().map(card).join("");
    scheduleStatusBadgesRefresh();
  }

  
  function scheduleStatusBadgesRefresh(){
    // Best-effort, silent.
    setTimeout(() => {
      try{ refreshAllStatusBadges(); }catch(_){ }
    }, 0);
  }

  function setStatusPill(childId, text, cls){
    const id = String(childId || '');
    const el = document.querySelector(`span[data-role="agent"][data-childid="${cssEscape(id)}"]`);
    if (!el) return;
    el.textContent = text;
    el.className = `so-pill ${cls || 'so-pill--muted'}`;
  }

  function parseUtcMs(v){
    if (!v || typeof v !== 'string') return NaN;
    const ms = Date.parse(v);
    return Number.isFinite(ms) ? ms : NaN;
  }

  async function refreshStatusFromApi(childId){
    const id = String(childId || '');
    if (!isGuid(id)) return false;
    const api = window.Safe0neApi;
    if (!api || typeof api.getChildStatus !== 'function') return false;
    if (state.api.statusInFlight[id]) return false;
    state.api.statusInFlight[id] = true;
    try{
      const res = await api.getChildStatus(id);
      if (res && res.ok && res.data){
        state.api.statusByChildId[id] = res.data;
        return true;
      }
      // Treat missing/unknown status as "never seen".
      state.api.statusByChildId[id] = null;
      return false;
    }catch{
      return false;
    }finally{
      state.api.statusInFlight[id] = false;
    }
  }

  async function refreshAllStatusBadges(){
    const cards = Array.from(document.querySelectorAll('.so-child-card[data-childid]'));
    if (!cards.length) return;
    const OFFLINE_AFTER_MS = 3 * 60 * 1000;

    for (const c of cards){
      const id = String(c.getAttribute('data-childid') || '');
      if (!isGuid(id)) continue;

      // Cached status is either object, null (never seen), or undefined (not fetched yet).
      let st = state.api.statusByChildId[id];
      if (st === undefined){
        setStatusPill(id, 'Protection: …', 'so-pill--muted');
        await refreshStatusFromApi(id);
        st = state.api.statusByChildId[id];
      }

      if (!st){
        setStatusPill(id, 'Protection: Never seen', 'so-pill--muted');
        continue;
      }

      const last = parseUtcMs(st.lastHeartbeatUtc) || parseUtcMs(st.lastSeenUtc);
      if (!Number.isFinite(last)){
        setStatusPill(id, 'Protection: Unknown', 'so-pill--muted');
        continue;
      }

      const ageMs = Date.now() - last;
      if (ageMs <= OFFLINE_AFTER_MS){
        setStatusPill(id, 'Protection: Online', 'so-pill--success');
      } else {
        setStatusPill(id, 'Protection: Offline', 'so-pill--warning');
      }
    }
  }

function scheduleChildrenRefresh() {
    if (state.refreshScheduled) return;
    state.refreshScheduled = true;
    // Router safety: schedule async work after synchronous render
    setTimeout(refreshChildrenFromApi, 0);
  }

  async function refreshChildrenFromApi() {
    if (state.api.inFlight) return;
    const api = window.Safe0neApi;
    if (!api || typeof api.getChildren !== "function") return;
    state.api.inFlight = true;
    try {
      const list = await api.getChildren({ includeArchived: true });
      if (Array.isArray(list)) {
        state.api.children = list.map(normalizeApiChild).filter((c) => c.id);
        state.api.available = true;
        rerenderGrid();
      }
    } catch {
      // keep silent; we intentionally fall back to localStorage/demo mode
    } finally {
      state.api.inFlight = false;
    }
  }


  async function refreshProfileFromApi(childId) {
    const id = String(childId || "");
    if (!isGuid(id)) return false;
    const api = window.Safe0neApi;
    if (!api || typeof api.getChildProfileLocal !== "function") return false;
    try {
      const prof = await api.getChildProfileLocal(id);
      if (!prof || typeof prof !== "object") return false;
      const m = getProfiles();
      // Merge with defaults to prevent regressions when new fields are added.
      const merged = mergeDefaults(defaultSettingsProfile(id), prof);
      merged.childId = id;
      m[id] = merged;
      setProfiles(m);
      return true;
    } catch {
      return false;
    }
  }




async function refreshDevicesFromApi(childId) {
  const id = String(childId || "");
  if (!isGuid(id)) return false;
  const api = window.Safe0neApi;
  if (!api || typeof api.getChildDevicesLocal !== "function") return false;
  if (state.api.devicesInFlight[id]) return false;
  state.api.devicesInFlight[id] = true;
  try {
    const res = await api.getChildDevicesLocal(id);
    if (res && res.ok && Array.isArray(res.data)) {
      state.api.devicesByChildId[id] = res.data;
      return true;
    }
    return false;
  } finally {
    state.api.devicesInFlight[id] = false;
  }
}

  function fmtTime(iso) {
    try {
      const d = new Date(iso);
      if (Number.isNaN(d.getTime())) return "";
      return d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    } catch {
      return "";
    }
  }

  function normalizeActivityEvent(e) {
    const kind = String(e?.kind || e?.event || "activity");
    const app = String(e?.app || e?.item || e?.target || "");
    const details = String(e?.details || "");
    const occurredAtUtc = String(e?.occurredAtUtc || e?.timeUtc || "");
    const status = String(e?.status || e?.severity || (kind.includes("block") ? "Blocked" : "Allowed") || "");
    return {
      event: kind.replace(/_/g, " "),
      item: app || details || "—",
      time: occurredAtUtc ? fmtTime(occurredAtUtc) : "—",
      status: status || "—",
      occurredAtUtc: occurredAtUtc || null,
    };
  }

  async function refreshActivityFromApi(childId) {
    const id = String(childId || "");
    if (!isGuid(id)) return false;
    const api = window.Safe0neApi;
    if (!api || typeof api.getChildActivityLocal !== "function") return false;
    if (state.api.activityInFlight[id]) return false;
    state.api.activityInFlight[id] = true;
    try {
      const res = await api.getChildActivityLocal(id, { take: 200 });
      const events = res && res.ok && Array.isArray(res.data) ? res.data : null;
      if (!events) return false;
      const profs = getProfiles();
      const childProf = profs[id] || defaultSettingsProfile(id);
      childProf.activity = events.map(normalizeActivityEvent);
      profs[id] = childProf;
      setProfiles(profs);
      return true;
    } catch {
      return false;
    } finally {
      state.api.activityInFlight[id] = false;
    }
  }

  async function refreshLocationFromApi(childId) {
    const id = String(childId || "");
    if (!isGuid(id)) return false;
    const api = window.Safe0neApi;
    if (!api || typeof api.getChildLocationLocal !== "function") return false;
    if (state.api.locationInFlight[id]) return false;
    state.api.locationInFlight[id] = true;
    try {
      const res = await api.getChildLocationLocal(id);
      const loc = res && res.ok && res.data && typeof res.data === "object" ? res.data : null;
      if (!loc) return false;
      const profs = getProfiles();
      const childProf = profs[id] || defaultSettingsProfile(id);
      childProf.location = normalizeLocation(loc);
      profs[id] = childProf;
      setProfiles(profs);
      return true;
    } catch {
      return false;
    } finally {
      state.api.locationInFlight[id] = false;
    }
  }

  function normalizeLocation(loc) {
    if (!loc || typeof loc !== "object") return null;
    return {
      available: ("available" in loc) ? !!loc.available : true,
      capturedAtUtc: loc.capturedAtUtc || loc.capturedAt || null,
      latitude: (typeof loc.latitude === "number") ? loc.latitude : null,
      longitude: (typeof loc.longitude === "number") ? loc.longitude : null,
      accuracyMeters: (typeof loc.accuracyMeters === "number") ? loc.accuracyMeters : null,
      source: loc.source || null,
      note: loc.note || null,
    };
  }

  function formatWhen(isoUtc) {
    try {
      const d = new Date(isoUtc);
      if (Number.isNaN(d.getTime())) return String(isoUtc);
      return d.toLocaleString();
    } catch {
      return String(isoUtc);
    }
  }

async function refreshPairingFromApi(childId) {
  const id = String(childId || "");
  if (!isGuid(id)) return false;
  const api = window.Safe0neApi;
  if (!api || typeof api.getChildPairingLocal !== "function") return false;
  if (state.api.pairingInFlight[id]) return false;
  state.api.pairingInFlight[id] = true;
  try {
    const res = await api.getChildPairingLocal(id);
    if (res && res.ok) {
      state.api.pairingByChildId[id] = res.data || null;
      return true;
    }
    return false;
  } finally {
    state.api.pairingInFlight[id] = false;
  }
}

  function updateCropFromInputs() {
    const img = document.getElementById("so-crop-img");
    if (!img || !state.draft?.avatar) return;
    const z = clamp(Number(document.getElementById("so-z")?.value ?? 1), 1, 3);
    const x = clamp(Number(document.getElementById("so-x")?.value ?? 0), -40, 40);
    const y = clamp(Number(document.getElementById("so-y")?.value ?? 0), -40, 40);
    img.style.transform = `translate(${x}px,${y}px) scale(${z})`;
    state.draft.avatar.crop = { z, x, y };
  }

  function showCrop() {
    const wrap = document.getElementById("so-crop");
    const prev = document.getElementById("so-prev");
    if (!wrap || !prev || !state.draft?.avatar?.dataUrl) return;
    wrap.style.display = "";
    prev.innerHTML = `<div class="so-avatar-clip" style="width:120px;height:120px;">
      <img id="so-crop-img" src="${state.draft.avatar.dataUrl}" alt="" style="transform-origin:center center;"/>
    </div>`;
    updateCropFromInputs();
  }

  function openAddEdit(mode, childId) {
    const isEdit = mode === "edit";
    if (isEdit && childId === "demo-11111111") return;

    const existing = isEdit ? getChildren().find((c) => c.id === childId) : null;
    const draft = existing
      ? JSON.parse(JSON.stringify(existing))
      : {
          id: "local-" + Math.random().toString(16).slice(2) + "-" + Date.now(),
          name: "",
          source: "Local",
          status: "Local",
          gender: "male",
          ageGroup: "10–12",
          avatar: { kind: "default", id: 0 },
        };

    state.draft = draft;

    const tiles = Array.from({ length: 6 })
      .map((_, i) => {
        const sel = draft.avatar?.kind === "default" && (draft.avatar.id | 0) === i ? 1 : 0;
        return `<button class="so-avatar-tile" type="button" data-action="pickDefaultAvatar" data-avid="${i}" data-selected="${sel}">${defaultAvatarSvg(
          i
        )}</button>`;
      })
      .join("");

    document.body.insertAdjacentHTML(
      "beforeend",
      `<div class="so-modal-backdrop" id="so-modal-backdrop">
        <div class="so-modal">
          <div class="so-hd">
            <div class="so-title">${isEdit ? "Edit child" : "Add child"}</div>
            <button class="so-btn" data-action="closeModal">Close</button>
          </div>
          <div class="so-bd">
            <div class="so-form">
              <div class="so-field"><label>Name</label><input id="so-name" type="text" value="${escapeHtml(
                draft.name
              )}" placeholder="Child name"/></div>
              <div class="so-field"><label>Gender</label>
                <select id="so-gender">
                  <option value="male" ${draft.gender === "male" ? "selected" : ""}>Male</option>
                  <option value="female" ${draft.gender === "female" ? "selected" : ""}>Female</option>
                  <option value="unspecified" ${
                    draft.gender === "unspecified" ? "selected" : ""
                  }>Unspecified</option>
                </select>
              </div>
              <div class="so-field"><label>Age group</label>
                <select id="so-age">${AGE.map(
                  (a) => `<option value="${a}" ${draft.ageGroup === a ? "selected" : ""}>${a}</option>`
                ).join("")}</select>
              </div>
              <div class="so-field"><label>Avatar upload</label><input id="so-upload" type="file" accept="image/*"/></div>

              <div class="so-one">
                <div style="font-weight:900;margin-top:6px;">Or pick a default avatar</div>
                <div class="so-avatars" id="so-tiles">${tiles}</div>
              </div>

              <div class="so-one" id="so-crop" style="display:none;">
                <div style="font-weight:900;margin-top:10px;">Adjust uploaded avatar</div>
                <div class="so-form" style="margin-top:10px;">
                  <div class="so-field"><label>Zoom</label><input class="so-slider" id="so-z" type="range" min="1" max="3" step="0.05" value="1"/></div>
                  <div class="so-field"><label>Move X</label><input class="so-slider" id="so-x" type="range" min="-40" max="40" step="1" value="0"/></div>
                  <div class="so-field"><label>Move Y</label><input class="so-slider" id="so-y" type="range" min="-40" max="40" step="1" value="0"/></div>
                  <div class="so-field"><label>Preview</label><div id="so-prev" style="width:120px;height:120px;border-radius:18px;overflow:hidden;background:#f1f5f9;display:flex;align-items:center;justify-content:center;"></div></div>
                </div>
              </div>

            </div>
          </div>
          <div class="so-ft">
            <button class="so-btn" data-action="cancelModal">Cancel</button>
            <button class="so-btn" data-action="${isEdit ? "saveEdit" : "createChild"}">${isEdit ? "Save" : "Create"}</button>
          </div>
        </div>
      </div>`
    );
  }

  // Single delegated handler (prevents “lost events”)
  let installed = false;
  function installOnce() {
    if (installed) return;
    installed = true;

    document.addEventListener("click", (ev) => {
      const btn = ev.target.closest("[data-action]");
      if (!btn) return;

      const action = btn.getAttribute("data-action");
      const childId = btn.getAttribute("data-childid");

      if (action === "add") return (ev.preventDefault(), closeModal(), openAddEdit("add"));
      if (action === "toggleShowArchived") {
        ev.preventDefault();
        state.showArchived = !state.showArchived;
        writeBool(LS_SHOW_ARCHIVED, state.showArchived);
        return rerenderGrid();
      }
      if (action === "open") return (ev.preventDefault(), navigate(`#/child/${childId}`));
      if (action === "edit") return (ev.preventDefault(), closeModal(), openAddEdit("edit", childId));
      if (action === "delete") {
        ev.preventDefault();
        if (!childId || childId === "demo-11111111") return;

        const useApi = !!state.api.available && !!window.Safe0neApi && typeof window.Safe0neApi.patchChildLocal === "function";
        if (useApi) {
          const all = Array.isArray(state.api.children) ? state.api.children : [];
          const target = all.find((c) => String(c.id) === String(childId));
          const isArchived = !!target?.isArchived;
          const msg = isArchived ? "Restore this child profile?" : "Archive this child profile?";
          if (!confirm(msg)) return;
          window.Safe0neApi
            .patchChildLocal(childId, { archived: !isArchived })
            .then(() => refreshChildrenFromApi())
            .catch(() => {
              // If local API fails, fall back to legacy behavior without crashing the router
              setLocalChildren(getLocalChildren().filter((c) => c.id !== childId));
              const p = getProfiles();
              delete p[childId];
              setProfiles(p);
              rerenderGrid();
            });
          return;
        }

        if (!confirm("Delete this child profile?")) return;
        setLocalChildren(getLocalChildren().filter((c) => c.id !== childId));
        const p = getProfiles();
        delete p[childId];
        setProfiles(p);
        return rerenderGrid();
      }
      if (action === "closeModal" || action === "cancelModal") return (ev.preventDefault(), closeModal());
      if (action === "pickDefaultAvatar") {
        ev.preventDefault();
        const avid = Number(btn.getAttribute("data-avid") ?? 0) | 0;
        document.querySelectorAll(".so-avatar-tile").forEach((t) => t.setAttribute("data-selected", "0"));
        btn.setAttribute("data-selected", "1");
        if (state.draft) state.draft.avatar = { kind: "default", id: avid };
        document.getElementById("so-crop") && (document.getElementById("so-crop").style.display = "none");
        return;
      }
      if (action === "createChild" || action === "saveEdit") {
        ev.preventDefault();
        if (!state.draft) return;

        const name = String(document.getElementById("so-name")?.value ?? "").trim();
        if (!name) return alert("Please enter a name.");

        const gender = String(document.getElementById("so-gender")?.value ?? "unspecified");
        const age = String(document.getElementById("so-age")?.value ?? "10–12");
        const ageGroup = AGE.includes(age) ? age : "10–12";

        state.draft.name = name;
        state.draft.gender = gender;
        state.draft.ageGroup = ageGroup;

        const api = window.Safe0neApi;
        const useApi =
          !!state.api.available &&
          !!api &&
          ((action === "createChild" && typeof api.createChildLocal === "function") ||
            (action === "saveEdit" && typeof api.patchChildLocal === "function"));

        if (useApi) {
          const payload = {
            displayName: name,
            gender,
            ageGroup,
            avatar: state.draft.avatar || { kind: "default", id: 0 },
          };

          const op =
            action === "createChild"
              ? api.createChildLocal(payload)
              : api.patchChildLocal(state.draft.id, payload);

          Promise.resolve(op)
            .then(() => {
              closeModal();
              return refreshChildrenFromApi();
            })
            .catch(() => {
              // Fall back to legacy localStorage mode if the local API is not reachable
              const locals = getLocalChildren();
              const idx = locals.findIndex((c) => c.id === state.draft.id);
              idx >= 0 ? (locals[idx] = state.draft) : locals.push(state.draft);
              setLocalChildren(locals);
              ensureProfile(state.draft);
              closeModal();
              rerenderGrid();
            });

          return;
        }

        const locals = getLocalChildren();
        const idx = locals.findIndex((c) => c.id === state.draft.id);
        idx >= 0 ? (locals[idx] = state.draft) : locals.push(state.draft);
        setLocalChildren(locals);
        ensureProfile(state.draft);

        closeModal();
        return rerenderGrid();
      }

      if (action === "backToChildren") return (ev.preventDefault(), navigate("#/children"));

      if (action === "setChildTab") {
        ev.preventDefault();
        const tab = btn.getAttribute("data-tab") || "settings";
        const cid = btn.getAttribute("data-childid") || "";
        if (cid) setChildTab(cid, tab);
        // toggle in-place
        const container = btn.closest('.page') || document;
        container.querySelectorAll('.cp-tab').forEach((b) => b.classList.toggle('active', b === btn));
        container.querySelectorAll('.cp-panel').forEach((p) => p.classList.toggle('active', p.getAttribute('data-tab') === tab));
        return;
      }
      if (action === "noop") { ev.preventDefault(); return; }


if (action === "copyPairCode") {
  ev.preventDefault();
  const code = btn.getAttribute("data-code") || "";
  if (!code) return;
  try { navigator.clipboard?.writeText?.(code); } catch {}
  window.Safe0neUi?.toast?.("Copied", "Pairing code copied.");
  return;
}

if (action === "copyPairLink") {
  ev.preventDefault();
  const link = btn.getAttribute("data-link") || "";
  if (!link) return;
  try { navigator.clipboard?.writeText?.(link); } catch {}
  window.Safe0neUi?.toast?.("Copied", "Pairing link copied.");
  return;
}

if (action === "refreshDevices") {
  ev.preventDefault();
  const cid = childId || "";
  if (!state?.api?.available || !isGuid(cid)) return;
  Promise.resolve(refreshDevicesFromApi(cid))
    .then(() => Promise.resolve(refreshPairingFromApi(cid)))
    .then(() => window.Safe0neRouter?.render?.())
    .catch(() => {});
  return;
}

if (action === "refreshActivity") {
  ev.preventDefault();
  const cid = childId || "";
  if (!state?.api?.available || !isGuid(cid)) return;
  Promise.resolve(refreshActivityFromApi(cid))
    .then((ok) => { if (ok) window.Safe0neRouter?.render?.(); })
    .catch(() => {});
  return;
}

if (action === "refreshLocation") {
  ev.preventDefault();
  const cid = childId || "";
  if (!state?.api?.available || !isGuid(cid)) return;
  Promise.resolve(refreshLocationFromApi(cid))
    .then((ok) => { if (ok) window.Safe0neRouter?.render?.(); })
    .catch(() => {});
  return;
}

if (action === "geoMinMax") {
  ev.preventDefault();
  const cid = childId || "";
  state.geoUi.minByChild[cid] = !state.geoUi.minByChild[cid];
  window.Safe0neRouter?.render?.();
  return;
}




if (action === "geoPanelBack") {
  ev.preventDefault();
  const cid = childId || "";
  if (!state.geoUi.panelModeByChild) state.geoUi.panelModeByChild = {};
  state.geoUi.panelModeByChild[cid] = "list";
  try { geoUpdatePanelDom(cid); } catch (_) {}
  return;
}

if (action === "geoPanelSelect") {
  ev.preventDefault();
  const cid = childId || "";
  const gid = btn.getAttribute("data-gf-id") || "";
  state.geoUi.selectedIdByChild[cid] = gid;
  if (!state.geoUi.panelModeByChild) state.geoUi.panelModeByChild = {};
  state.geoUi.panelModeByChild[cid] = "edit";
  try { geoUpdatePanelDom(cid); } catch (_) {}
  try { geoLeafletSync({ id: cid }); } catch (_) {}
  return;
}

if (action === "geoPanelNew") {
  ev.preventDefault();
  const cid = childId || "";
  // Focus the name box and turn on place mode
  if (!state.geoUi.placeByChild) state.geoUi.placeByChild = {};
  state.geoUi.placeByChild[cid] = true;
  if (!state.geoUi.pendingNameByChild) state.geoUi.pendingNameByChild = {};
  if (!state.geoUi.pendingRadiusByChild) state.geoUi.pendingRadiusByChild = {};
  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);
  const geos = prof.policy.location.geofences;
  if (!state.geoUi.pendingNameByChild[cid]) state.geoUi.pendingNameByChild[cid] = `Geofence ${geos.length + 1}`;
  if (!state.geoUi.pendingRadiusByChild[cid]) state.geoUi.pendingRadiusByChild[cid] = 200;
  geoSetStatus(cid, "Click on the map to place the new geofence.");
  try { geoUpdatePanelDom(cid); } catch (_) {}
  // focus after paint
  setTimeout(() => {
    const inp = document.querySelector(`[data-geo="pendingName"][data-childid="${cssEscape(cid)}"]`);
    try { inp?.focus?.(); inp?.select?.(); } catch (_) {}
  }, 0);
  return;
}

if (action === "geoPresetName") {
  ev.preventDefault();
  const cid = childId || "";
  const name = btn.getAttribute("data-name") || "";
  const radius = clampNum(parseFloat(btn.getAttribute("data-radius") || "200"), 30, 20000);
  if (!state.geoUi.pendingNameByChild) state.geoUi.pendingNameByChild = {};
  if (!state.geoUi.pendingRadiusByChild) state.geoUi.pendingRadiusByChild = {};
  state.geoUi.pendingNameByChild[cid] = String(name || "").slice(0, 64);
  state.geoUi.pendingRadiusByChild[cid] = radius;
  try { geoUpdatePanelDom(cid); } catch (_) {}
  return;
}

if (action === "geoPlaceMode") {
  ev.preventDefault();
  const cid = childId || "";
  if (!state.geoUi.placeByChild) state.geoUi.placeByChild = {};
  state.geoUi.placeByChild[cid] = !state.geoUi.placeByChild[cid];

  // Require a name for placement (defaults to "Geofence N" if empty).
  if (!state.geoUi.pendingNameByChild) state.geoUi.pendingNameByChild = {};
  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);
  const geos = prof.policy.location.geofences;
  if (!state.geoUi.pendingNameByChild[cid]) state.geoUi.pendingNameByChild[cid] = `Geofence ${geos.length + 1}`;

  geoSetStatus(cid, state.geoUi.placeByChild[cid] ? "Click on the map to place the new geofence." : "Placement cancelled.");
  try { geoUpdatePanelDom(cid); } catch (_) {}
  return;
}

if (action === "geoRadiusPreset") {
  ev.preventDefault();
  const cid = childId || "";
  const gid = btn.getAttribute("data-gf-id") || "";
  const r = clampNum(parseFloat(btn.getAttribute("data-radius") || ""), 30, 20000);
  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);
  const g = prof.policy.location.geofences.find((x) => x.id === gid);
  if (!g) return;
  g.radiusMeters = r;
  setProfiles(getProfiles());
  geoRedraw(cid);
  return;
}

if (action === "geoCenterOnChild") {
  ev.preventDefault();
  const cid = childId || "";
  const prof = ensureProfile({ id: cid });
  const loc = prof.location || null;
  if (loc && typeof loc.latitude === "number" && typeof loc.longitude === "number") {
    geoLeafletPanTo(cid, loc.latitude, loc.longitude);
  }
  return;
}

if (action === "geoClearSearch") {
  ev.preventDefault();
  const cid = childId || "";
  if (state.geoUi.searchResultByChild) delete state.geoUi.searchResultByChild[cid];
  try { geoUpdateSearchDom(cid); } catch (_) {}
  return;
}

if (action === "geoCenterOnSearch" || action === "geoCreateAtSearch" || action === "geoMoveSelectedToSearch") {
  ev.preventDefault();
  const cid = childId || "";
  const r = state.geoUi?.searchResultByChild?.[cid];
  if (!r) return;

  if (action === "geoCenterOnSearch") {
    geoLeafletPanTo(cid, r.lat, r.lon, Math.max(15, state.geoUi.zoomByChild?.[cid] || 15));
    return;
  }

  if (action === "geoCreateAtSearch") {
    const created = geoCreateGeofenceAt(cid, r.lat, r.lon, { radiusMeters: 200, mode: "inside", name: String(state.geoUi?.searchQueryByChild?.[cid] || "").slice(0,64) || undefined });
    if (!state.geoUi.panelModeByChild) state.geoUi.panelModeByChild = {};
    state.geoUi.panelModeByChild[cid] = "edit";
    try { geoLeafletSync({ id: cid }); } catch (_) {}
    try { geoUpdatePanelDom(cid); } catch (_) {}
    try { geoUpdateSearchDom(cid); } catch (_) {}
    return;
  }

  // Move selected
  const sel = state.geoUi?.selectedIdByChild?.[cid] || "";
  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);
  const g = prof.policy.location.geofences.find((x) => x.id === sel);
  if (!g) return;
  g.latitude = r.lat;
  g.longitude = r.lon;
  setProfiles(getProfiles());
  geoRedraw(cid);
  geoLeafletPanTo(cid, r.lat, r.lon);
  return;
}

if (action === "geoSearch") {
  ev.preventDefault();
  const cid = childId || "";
  const q = state.geoUi?.searchQueryByChild?.[cid] || "";
  geoSetStatus(cid, "Searching…");
  Promise.resolve(geoNominatimSearch(q))
    .then((r) => {
      if (!r) {
        geoSetStatus(cid, "No results.");
        return;
      }
      if (!state.geoUi.searchResultByChild) state.geoUi.searchResultByChild = {};
      state.geoUi.searchResultByChild[cid] = r;
      geoLeafletPanTo(cid, r.lat, r.lon, Math.max(15, state.geoUi.zoomByChild?.[cid] || 15));
      try { geoUpdateSearchDom(cid); } catch (_) {}
      try { geoUpdatePanelDom(cid); } catch (_) {}
    })
    .catch(() => geoSetStatus(cid, "Search failed (network)."));
  return;
}
if (action === "addGeofence") {
  ev.preventDefault();
  const cid = childId || "";
  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);
  const geos = prof.policy.location.geofences;

  const loc = prof.location || null;
  const baseLat = (loc && typeof loc.latitude === "number") ? loc.latitude : 0;
  const baseLon = (loc && typeof loc.longitude === "number") ? loc.longitude : 0;

  const g = {
    id: `gf_${Math.random().toString(16).slice(2)}`,
    name: `Geofence ${geos.length + 1}`,
    latitude: baseLat,
    longitude: baseLon,
    radiusMeters: 200,
    mode: "inside",
  };
  geos.push(g);
  state.geoUi.selectedIdByChild[cid] = g.id;
  if (!state.geoUi.panelModeByChild) state.geoUi.panelModeByChild = {};
  state.geoUi.panelModeByChild[cid] = "edit";
  setProfiles(getProfiles());
  geoRedraw(cid);
  try { geoLeafletSync({ id: cid }); } catch (_) {}
  try { geoUpdatePanelDom(cid); } catch (_) {}
  return;
}

if (action === "deleteGeofence") {
  ev.preventDefault();
  const cid = childId || "";
  const gid = btn.getAttribute("data-gf-id") || "";
  if (!gid) return;
  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);
  prof.policy.location.geofences = prof.policy.location.geofences.filter((g) => g.id !== gid);
  if (state.geoUi.selectedIdByChild[cid] === gid) state.geoUi.selectedIdByChild[cid] = "";
  if (!state.geoUi.panelModeByChild) state.geoUi.panelModeByChild = {};
  state.geoUi.panelModeByChild[cid] = "list";
  setProfiles(getProfiles());
  geoRedraw(cid);
  try { geoLeafletSync({ id: cid }); } catch (_) {}
  try { geoUpdatePanelDom(cid); } catch (_) {}
  return;
}

if (action === "selectGeofence") {
  ev.preventDefault();
  const cid = childId || "";
  const gid = btn.getAttribute("data-gf-id") || "";
  state.geoUi.selectedIdByChild[cid] = gid;
  if (!state.geoUi.panelModeByChild) state.geoUi.panelModeByChild = {};
  state.geoUi.panelModeByChild[cid] = "edit";
  try { geoUpdatePanelDom(cid); } catch (_) {}
  try { geoLeafletSync({ id: cid }); } catch (_) {}
  return;
}

if (action === "gfRadiusMinus" || action === "gfRadiusPlus") {
  ev.preventDefault();
  const cid = childId || "";
  const gid = btn.getAttribute("data-gf-id") || "";
  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);
  const g = prof.policy.location.geofences.find((x) => x.id === gid);
  if (!g) return;
  const delta = action === "gfRadiusPlus" ? 25 : -25;
  g.radiusMeters = clampNum((g.radiusMeters || 200) + delta, 30, 20000);
  setProfiles(getProfiles());
  geoRedraw(cid);
  window.Safe0neRouter?.render?.();
  return;
}



if (action === "addPerAppLimit") {
  ev.preventDefault();
  const cid = childId || "";
  const prof = ensureProfile({ id: cid });
  ensurePerAppLimitIds(prof);
  prof.policy.apps.perAppLimits.push({
    id: `pal_${Math.random().toString(16).slice(2)}`,
    appId: "",
    minutesPerDay: 60,
    days: ["Mon","Tue","Wed","Thu","Fri","Sat","Sun"],
  });
  setProfiles(getProfiles());
  window.Safe0neRouter?.render?.();
  return;
}

if (action === "removePerAppLimit") {
  ev.preventDefault();
  const cid = childId || "";
  const lid = btn.getAttribute("data-limit-id") || "";
  const prof = ensureProfile({ id: cid });
  ensurePerAppLimitIds(prof);
  prof.policy.apps.perAppLimits = prof.policy.apps.perAppLimits.filter((x) => (x && x.id) !== lid);
  setProfiles(getProfiles());
  window.Safe0neRouter?.render?.();
  return;
}

if (action === "pairDevice") {
  ev.preventDefault();
  const cid = childId || "";
  if (!state?.api?.available || !isGuid(cid) || !window.Safe0neApi?.startPairingLocal) return;
  Promise.resolve(window.Safe0neApi.startPairingLocal(cid))
    .then((res) => {
      if (res && res.ok) state.api.pairingByChildId[cid] = res.data || null;
      return Promise.resolve(refreshDevicesFromApi(cid));
    })
    .then(() => window.Safe0neRouter?.render?.())
    .catch(() => window.Safe0neUi?.toast?.("Pairing failed", "Could not start pairing."));
  return;
}

if (action === "unpairDevice") {
  ev.preventDefault();
  const cid = childId || "";
  const deviceId = btn.getAttribute("data-deviceid") || "";
  if (!deviceId) return;
  if (!confirm("Unpair this device? This will remove the device from this child.")) return;
  const phrase = (prompt("Type UNPAIR to confirm:") || "").trim().toUpperCase();
  if (phrase !== "UNPAIR") return;
  if (!state?.api?.available || !window.Safe0neApi?.unpairDeviceLocal) return;
  Promise.resolve(window.Safe0neApi.unpairDeviceLocal(deviceId))
    .then(() => Promise.resolve(refreshDevicesFromApi(cid)))
    .then(() => window.Safe0neRouter?.render?.())
    .catch(() => window.Safe0neUi?.toast?.("Unpair failed", "Could not unpair device."));
  return;
}

if (action === "revokeDeviceToken") {
  ev.preventDefault();
  const cid = childId || "";
  const deviceId = btn.getAttribute("data-deviceid") || "";
  if (!deviceId) return;
  if (!confirm("Revoke this device token? The Kid device will become Unauthorized until re-paired.")) return;
  if (!state?.api?.available || !window.Safe0neApi?.revokeDeviceTokenLocal) return;
  Promise.resolve(window.Safe0neApi.revokeDeviceTokenLocal(deviceId, { revokedBy: "parent" }))
    .then(() => Promise.resolve(refreshDevicesFromApi(cid)))
    .then(() => window.Safe0neRouter?.render?.())
    .catch(() => window.Safe0neUi?.toast?.("Revoke failed", "Could not revoke device token."));
  return;
}


      if (action === "saveProfile") {
        ev.preventDefault();
        const id = childId;
        const child = getChildren().find((c) => c.id === id) || demoChild();
        const prof = ensureProfile(child);

        document.querySelectorAll('input[type="checkbox"][data-perm]').forEach((cb) => {
          const k = cb.getAttribute("data-perm");
          if (k) prof.permissions[k] = !!cb.checked;
        });
        prof.limits.screenMinutesPerDay = clamp(
          Number(document.querySelector('input[data-field="screenMinutesPerDay"]')?.value ?? 120),
          0,
          1440
        );
        
        prof.limits.bedtimeStart = String(
          document.querySelector('input[data-field="bedtimeStart"]')?.value ?? "21:00"
        );
        prof.limits.bedtimeEnd = String(
          document.querySelector('input[data-field="bedtimeEnd"]')?.value ?? "07:00"
        );

        // Screen time per-day overrides (optional). Stored under policy.timeBudget.perDayMinutes (Mon..Sun).
        prof.policy = prof.policy || {};
        prof.policy.timeBudget = prof.policy.timeBudget || {};
        prof.policy.timeBudget.dailyMinutes = prof.limits.screenMinutesPerDay;

        const _readDay = (k) => {
          const raw = document.querySelector(`input[data-field="screenMinutes${k}"]`)?.value;
          if (raw == null || String(raw).trim() === "") return null;
          const n = clamp(Number(raw), 0, 1440);
          return Number.isFinite(n) ? n : null;
        };
        const perDay = {};
        ["Mon","Tue","Wed","Thu","Fri","Sat","Sun"].forEach((d) => {
          const n = _readDay(d);
          if (n != null) perDay[d] = n;
        });
        prof.policy.timeBudget.perDayMinutes = perDay;

        // PATCH 16R: grace + warning thresholds (additive)
        prof.policy.timeBudget.graceMinutes = clamp(
          Number(document.querySelector('input[data-field="graceMinutes"]')?.value ?? 0),
          0,
          120
        );

        const _parseWarnList = (raw) => {
          const s = String(raw || "").trim();
          if (!s) return [5, 1];
          const parts = s
            .split(/[,;\s]+/g)
            .map((x) => x.trim())
            .filter((x) => x.length);
          const nums = [];
          for (const p of parts) {
            const n = Number(p);
            if (!Number.isFinite(n)) continue;
            nums.push(clamp(Math.round(n), 0, 240));
          }
          // De-dupe + remove negatives; keep descending so the UX is predictable.
          const uniq = Array.from(new Set(nums)).filter((x) => x >= 0);
          uniq.sort((a, b) => b - a);
          return uniq.length ? uniq : [5, 1];
        };
        prof.policy.timeBudget.warnAtMinutes = _parseWarnList(
          document.querySelector('input[data-field="warnAtMinutes"]')?.value
        );
        // Legacy key (kept so older builds can still observe warnings)
        prof.policy.timeBudget.warnMinutesRemaining = prof.policy.timeBudget.warnAtMinutes.slice();

        // Apps allow/deny lists (one per line). Additive-only: keep existing keys.
        const _splitLines = (v) =>
          String(v || "")
            .split(/\r?\n/g)
            .map((s) => s.trim())
            .filter((s) => !!s);
        const allow = _splitLines(document.querySelector('textarea[data-field="appsAllowList"]')?.value || "");
        const deny = _splitLines(document.querySelector('textarea[data-field="appsDenyList"]')?.value || "");
        prof.policy.apps = prof.policy.apps || {};
        prof.policy.apps.allowList = allow;
        prof.policy.apps.denyList = deny;
        const bn = document.querySelector('input[type="checkbox"][data-field="blockNewApps"]');
        if (bn) prof.policy.apps.blockNewApps = !!bn.checked;

        
        // Per-app limits (PATCH 16S) — authoring + validation (additive)
        ensurePerAppLimitIds(prof);
        const palRows = Array.from(document.querySelectorAll('[data-pal-row="1"]'));
        const pal = [];
        const seen = new Set();
        const allDays = ["Mon","Tue","Wed","Thu","Fri","Sat","Sun"];
        for (const row of palRows) {
          const lid = row.getAttribute("data-limit-id") || `pal_${Math.random().toString(16).slice(2)}`;
          const appId = String(document.querySelector(`input[data-pal="appId"][data-limit-id="${cssEscape(lid)}"]`)?.value || "").trim();
          const minRaw = document.querySelector(`input[data-pal="minutes"][data-limit-id="${cssEscape(lid)}"]`)?.value;
          const minutes = clamp(Number(minRaw ?? 60), 0, 1440);

          const dayChecks = Array.from(document.querySelectorAll(`input[type="checkbox"][data-pal="day"][data-limit-id="${cssEscape(lid)}"]`))
            .filter((cb) => !!cb.checked)
            .map((cb) => String(cb.getAttribute("data-day") || "").trim())
            .filter((d) => allDays.includes(d));
          const days = dayChecks.length ? dayChecks : allDays;

          const isBlank = !appId && (minRaw == null || String(minRaw).trim() === "");
          if (isBlank) continue;

          if (!appId) {
            window.Safe0neUi?.toast?.("Fix per‑app limits", "Each limit needs an App ID (e.g. chrome.exe).");
            return;
          }
          const key = appId.toLowerCase();
          if (seen.has(key)) {
            window.Safe0neUi?.toast?.("Fix per‑app limits", "Duplicate App ID detected. Each App ID must be unique.");
            return;
          }
          seen.add(key);

          pal.push({ id: lid, appId, minutesPerDay: minutes, days });
        }
        prof.policy.apps.perAppLimits = pal;

// Alerts routing (16V)
        prof.policy.alerts = prof.policy.alerts || {};
        prof.policy.alerts.routing = prof.policy.alerts.routing || {};
        document.querySelectorAll('input[type=\"checkbox\"][data-alertroute]').forEach((cb) => {
          const k = cb.getAttribute('data-alertroute');
          if (k) prof.policy.alerts.routing[k] = !!cb.checked;
        });
// Prefer SSOT in Local Mode when we have a real child GUID.
        if (state?.api?.available && isGuid(id) && window.Safe0neApi?.putChildProfileLocal) {
          prof.childId = id;
          Promise.resolve(window.Safe0neApi.putChildProfileLocal(id, prof))
            .then(() => {
              const p = getProfiles();
              p[id] = prof;
              setProfiles(p);
              // Clear any draft when SSOT save succeeds.
              try {
                state.ui = state.ui || {};
                if (state.ui.profileDraftByChildId) delete state.ui.profileDraftByChildId[id];
              } catch (_) {}
              state.api.saveFailedByChildId[id] = false;
              window.Safe0neUi?.toast?.("Saved", "Profile updated.");
            })
            .catch(() => {
              // SSOT purity: do NOT claim a save happened and do NOT persist domain state locally.
              // Keep an in-memory draft so the user doesn't lose edits during an offline window.
              state.ui = state.ui || {};
              state.ui.profileDraftByChildId = state.ui.profileDraftByChildId || {};
              state.ui.profileDraftByChildId[id] = prof;
              state.api.saveFailedByChildId[id] = true;
              window.Safe0neUi?.toast?.("Not saved", "Local service offline — changes are kept as a draft only.");
            });
          return;
        }

        const p = getProfiles();
        p[id] = prof;
        setProfiles(p);
        return state.api.saveFailedByChildId[id] = false;
              window.Safe0neUi?.toast?.("Saved", "Profile updated.");
      }


      if (action === "resetProfile") {
        ev.preventDefault();
        const id = childId;
        if (!confirm("Reset this child's settings to defaults?")) return;

        // Prefer SSOT reset in Local Mode when we have a real child GUID.
        if (state?.api?.available && isGuid(id) && window.Safe0neApi?.putChildProfileLocal) {
          const def = defaultSettingsProfile(id);
          Promise.resolve(window.Safe0neApi.putChildProfileLocal(id, def))
            .then(() => {
              const p = getProfiles();
              p[id] = def;
              setProfiles(p);
              // Clear any draft when SSOT reset succeeds.
              try {
                state.ui = state.ui || {};
                if (state.ui.profileDraftByChildId) delete state.ui.profileDraftByChildId[id];
              } catch (_) {}
              try { geoUpdateSearchDom(cid); } catch (_) {}
      try { geoUpdatePanelDom(cid); } catch (_) {}
            })
            .catch(() => {
              // SSOT purity: do NOT reset locally if SSOT is unavailable.
              state.api.saveFailedByChildId[id] = true;
              window.Safe0neUi?.toast?.("Not reset", "Local service offline — cannot reset policy/profile.");
              try { geoUpdateSearchDom(cid); } catch (_) {}
      try { geoUpdatePanelDom(cid); } catch (_) {}
            });
          return;
        }

        const p = getProfiles();
        delete p[id];
        setProfiles(p);
        return window.Safe0neRouter?.render?.();
      }

    });

    document.addEventListener("input", (ev) => {
      if (["so-z", "so-x", "so-y"].includes(ev.target?.id)) updateCropFromInputs();
    });

    // Geofence inputs (live edit; persists on Save)
document.addEventListener("input", (ev) => {
  const t = ev.target;

  // Map search query live binding
  if (t?.getAttribute?.("data-geo") === "searchQuery") {
    const cid = t.getAttribute("data-childid") || "";
    if (!state.geoUi.searchQueryByChild) state.geoUi.searchQueryByChild = {};
    state.geoUi.searchQueryByChild[cid] = String(t.value || "");
    return;
  }

  // Geofence "create" name input (panel list mode)
  if (t?.getAttribute?.("data-geo") === "pendingName") {
    const cid = t.getAttribute("data-childid") || "";
    if (!state.geoUi.pendingNameByChild) state.geoUi.pendingNameByChild = {};
    state.geoUi.pendingNameByChild[cid] = String(t.value || "").slice(0, 64);
    return;
  }

  // Floating map editor controls
  const geoEdit = t?.getAttribute?.("data-geoedit");
  const geoGid = t?.getAttribute?.("data-gf-id");
  if (geoEdit && geoGid) {
    const route = window.Safe0neRouter?.getCurrent?.();
    const cid = String(route?.params?.id || "");
    if (!cid) return;
    const prof = ensureProfile({ id: cid });
    ensureLocationPolicy(prof);
    const g = prof.policy.location.geofences.find((x) => x.id === geoGid);
    if (!g) return;

    if (geoEdit === "name") g.name = String(t.value || "").slice(0, 64);
    else if (geoEdit === "mode") g.mode = (String(t.value) === "outside") ? "outside" : "inside";
    else if (geoEdit === "radius") g.radiusMeters = clampNum(parseFloat(t.value), 30, 20000);

    setProfiles(getProfiles());
    geoRedraw(cid);
    try { geoLeafletSync({ id: cid }); } catch (_) {}
    try { geoUpdatePanelDom(cid); } catch (_) {}
    return;
  }

  const field = t?.getAttribute?.("data-gf-field");
  const gid = t?.getAttribute?.("data-gf-id");
  if (!field || !gid) return;

  const route = window.Safe0neRouter?.getCurrent?.();
  const cid = String(route?.params?.id || "");
  if (!cid) return;

  const prof = ensureProfile({ id: cid });
  ensureLocationPolicy(prof);
  const g = prof.policy.location.geofences.find((x) => x.id === gid);
  if (!g) return;

  if (field === "name") g.name = String(t.value || "").slice(0, 64);
  else if (field === "mode") g.mode = (String(t.value) === "outside") ? "outside" : "inside";
  else if (field === "latitude") g.latitude = clampNum(parseFloat(t.value), -85, 85);
  else if (field === "longitude") g.longitude = clampNum(parseFloat(t.value), -180, 180);
  else if (field === "radiusMeters") g.radiusMeters = clampNum(parseFloat(t.value), 30, 20000);

  setProfiles(getProfiles());
  geoRedraw(cid);
  try { geoLeafletSync({ id: cid }); } catch (_) {}
  try { geoUpdatePanelDom(cid); } catch (_) {}
});

document.addEventListener("change", (ev) => {
  const t = ev.target;
  // Zoom selector
  if (t?.getAttribute?.("data-geo") === "zoom") {
    const cid = t.getAttribute("data-childid") || "";
    state.geoUi.zoomByChild[cid] = clampNum(parseInt(t.value, 10), 12, 18);
    geoRedraw(cid);
    return;
  }

  const field = t?.getAttribute?.("data-gf-field");
  const gid = t?.getAttribute?.("data-gf-id");
  if (field && gid && field === "mode") {
    const route = window.Safe0neRouter?.getCurrent?.();
    const cid = String(route?.params?.id || "");
    if (!cid) return;

    const prof = ensureProfile({ id: cid });
    ensureLocationPolicy(prof);
    const g = prof.policy.location.geofences.find((x) => x.id === gid);
    if (!g) return;

    g.mode = (String(t.value) === "outside") ? "outside" : "inside";
    setProfiles(getProfiles());
    geoRedraw(cid);
    return;
  }
});

document.addEventListener("change", (ev) => {
      if (ev.target?.id !== "so-upload") return;
      const file = ev.target.files && ev.target.files[0];
      if (!file || !state.draft) return;
      const reader = new FileReader();
      reader.onload = () => {
        if (!state.draft) return;
        state.draft.avatar = { kind: "upload", dataUrl: String(reader.result), crop: { z: 1, x: 0, y: 0 } };
        document.querySelectorAll(".so-avatar-tile").forEach((t) => t.setAttribute("data-selected", "0"));
        showCrop();
      };
      reader.readAsDataURL(file);
    });
  }

  // Expose to router
  window.Safe0neChildren = { __canonical: true, renderChildren, renderChildProfile };
  installOnce();
})();
