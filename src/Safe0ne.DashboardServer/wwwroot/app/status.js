async function refreshStatus(){
  // Local service chip.
  const chipService = document.getElementById("chip-service");
  try{
    const res = await fetch("/api/health", { cache: "no-store" });
    if (!res.ok) throw new Error("bad status");
    chipService.textContent = "Local service: Running";
    chipService.className = "chip chip--success";
  }catch{
    chipService.textContent = "Local service: Not running";
    chipService.className = "chip chip--danger";
  }

  // Protection chip: show whether the Child Agent has checked in recently.
  // Privacy-first: coarse heartbeat age only.
  const chipProtection = document.getElementById("chip-protection");
  const OFFLINE_AFTER_MS = 3 * 60 * 1000; // 3 minutes (keep aligned with Alerts inbox)
  const parseUtcMs = (s) => {
    if (!s || typeof s !== "string") return NaN;
    const ms = Date.parse(s);
    return Number.isFinite(ms) ? ms : NaN;
  };
  const formatAge = (ageMs) => {
    const s = Math.max(0, Math.floor(ageMs / 1000));
    const m = Math.floor(s / 60);
    const r = s % 60;
    if (m <= 0) return `${r}s`;
    return `${m}m ${r}s`;
  };

  try{
    // Prefer the first Local Mode child (SSOT) for the protection chip.
    // Fallback to the legacy seeded GUID if Local Mode children cannot be fetched.
    let childGuid = "11111111-1111-1111-1111-111111111111";
    try{
      const cres = await fetch(`/api/local/children?includeArchived=false`, { cache: "no-store" });
      if (cres.ok){
        const cbody = await cres.json();
        const list = cbody && cbody.data && Array.isArray(cbody.data) ? cbody.data : [];
        const first = list.find(x => x && typeof x.id === 'string' && x.id.length >= 32);
        if (first && first.id) childGuid = first.id;
      }
    }catch{}

    const res = await fetch(`/api/v1/children/${encodeURIComponent(childGuid)}/status`, { cache: "no-store" });
    if (!res.ok) throw new Error("no status");
    const body = await res.json();
    const status = body && body.data ? body.data : null;

    // Prefer heartbeat if present, else fall back to lastSeen.
    const last = status ? (parseUtcMs(status.lastHeartbeatUtc) || parseUtcMs(status.lastSeenUtc)) : NaN;
    if (!Number.isFinite(last)) throw new Error("bad status");

    const ageMs = Date.now() - last;

    if (ageMs <= OFFLINE_AFTER_MS){
      chipProtection.textContent = "Protection: Online";
      chipProtection.className = "chip chip--success";
    } else {
      chipProtection.textContent = `Protection: Offline (${formatAge(ageMs)} since heartbeat)`;
      chipProtection.className = "chip chip--warning";
    }
  }catch{
    chipProtection.textContent = "Protection: Offline";
    chipProtection.className = "chip chip--warning";
  }
}

window.addEventListener("DOMContentLoaded", () => {
  // Self-test badge (PASS/FAIL) — driven by Safe0neModules.runBootSelfTest()
  try{
    const Ui = window.Safe0neUi;
    if (Ui && typeof Ui.setSelfTestBadge === 'function'){
      // Render current snapshot if it already exists.
      if (window.Safe0neSelfTest) Ui.setSelfTestBadge(window.Safe0neSelfTest);
      // Subscribe to updates.
      window.addEventListener('safe0ne:selftest', (ev) => {
        try{ Ui.setSelfTestBadge(ev?.detail || window.Safe0neSelfTest); }catch{}
      });
      // Ensure the chip exists even before the first event.
      Ui.ensureChip?.('chip-selftest', 'Self-test: …', 'chip chip--info');
    }
  }catch{}


// Anti-regression: periodic contract check (only when DevTools is unlocked).
try{
  const tick = () => {
    try{
      const unlocked = localStorage.getItem("safe0ne_devtools_unlocked_v1") === "true";
      if (!unlocked) return;
      window.Safe0neModules?.assertContracts?.({ log: true });
    }catch{}
  };
  tick();
  setInterval(tick, 30000);
}catch{}

  refreshStatus();
  setInterval(refreshStatus, 5000);
});
