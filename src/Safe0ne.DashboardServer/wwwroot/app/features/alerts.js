/* Safe0ne DashboardServer UI Feature Module: Alerts
   ADR-0003: modularize high-churn UI logic to reduce router.js overwrite regressions.

   Exposes: window.Safe0neAlerts = {
     buildAlerts(statuses, nowMs),
     renderAlertActions(alert),
     bindAlertsList(listEl, rerender),
     isAcked(alert)
   }
*/
(function(){
  "use strict";

  const OFFLINE_AFTER_MS = 3 * 60 * 1000; // 3 minutes

  function escapeHtml(s){
    try{ if (window.Safe0neUi && typeof Safe0neUi.escapeHtml==='function') return Safe0neUi.escapeHtml(s);}catch(e){}
    // Avoid newer syntax here; this file must remain compatible with older WebView2 runtimes.
    const v = (s === null || s === undefined) ? '' : s;
    return String(v);
  }


  function ackKeyFor(alert){
    const childId = alert && alert.childId ? String(alert.childId) : "";
    const title = alert && alert.title ? String(alert.title) : "";
    return `${childId}|${title}`;
  }

  function safeGet(key){
    try{ return localStorage.getItem(key); }catch(e){ return null; }
  }

  function safeSet(key, value){
    try{ localStorage.setItem(key, value); }catch(e){}
  }

  function safeRemove(key){
    try{ localStorage.removeItem(key); }catch(e){}
  }

  function isAcked(alert){
    const key = ackKeyFor(alert);
    return safeGet(key) === "1";
  }

  function setAckedByKey(key, acked){
    if (!key) return;
    if (acked) safeSet(key, "1");
    else safeRemove(key);
  }

  function fmtAgeMs(ageMs){
    const totalSec = Math.max(0, Math.floor(ageMs / 1000));
    const mins = Math.floor(totalSec / 60);
    const secs = totalSec % 60;
    return `${mins}m ${secs}s`;
  }

  function buildAlerts(statuses, nowMs){
    const now = Number.isFinite(nowMs) ? nowMs : Date.now();
    const alerts = [];

    (statuses || []).forEach(x => {
      const childName = (x.child && x.child.displayName) ? x.child.displayName : "Child";
      const childId = x.child && x.child.id && x.child.id.value ? x.child.id.value : "";
      const st = x.status;

      if (!x.ok || !st){
        alerts.push({
          sev: "warning",
          childName,
          childId,
          title: "Device status unavailable",
          detail: `We could not read ${childName}’s device status. Check that DashboardServer is running and the child agent is online.`,
          when: ""
        });
        return;
      }

      // Offline heartbeat (derived)
      const hbMs = st.lastHeartbeatUtc ? Date.parse(st.lastHeartbeatUtc)
                 : (st.lastSeenUtc ? Date.parse(st.lastSeenUtc) : NaN);
      if (!Number.isNaN(hbMs)){
        const ageMs = now - hbMs;
        if (ageMs >= OFFLINE_AFTER_MS){
          alerts.push({
            sev: "danger",
            childName,
            childId,
            title: "Child device appears offline",
            detail: `No heartbeat for ${fmtAgeMs(ageMs)}. Controls may not be applying until the device is back online.`,
            when: st.lastHeartbeatUtc || st.lastSeenUtc || ""
          });
        }
      }

      // Screen time depleted
      if (st.screenTimeBudgetDepleted){
        alerts.push({
          sev: "info",
          childName,
          childId,
          title: "Screen time is used up for today",
          openRequests: true,
          detail: `${childName} has used today’s screen time budget. They can request more time, or you can adjust policy.`,
          when: ""
        });
      }

      // Circumvention signals
      const circ = st.circumvention;
      if (circ && (circ.vpnSuspected || circ.proxyEnabled || circ.publicDnsDetected || circ.hostsWriteFailed)){
        const notes = [];
        if (circ.vpnSuspected) notes.push("VPN suspected");
        if (circ.proxyEnabled) notes.push("Proxy enabled");
        if (circ.publicDnsDetected) notes.push("Public DNS detected");
        if (circ.hostsWriteFailed) notes.push("Hosts protection failed");
        alerts.push({
          sev: "warning",
          childName,
          childId,
          title: "Possible bypass attempt",
          detail: `${childName}’s device shows: ${notes.join(", ")}.`,
          when: ""
        });
      }

      // Agent health issue
      const t = st.tamper;
      if (t && (t.notRunningElevated || t.enforcementError)){
        const notes = [];
        if (t.notRunningElevated) notes.push("Not running elevated");
        if (t.enforcementError) notes.push("Enforcement errors");
        alerts.push({
          sev: "danger",
          childName,
          childId,
          title: "Agent health issue",
          openSupport: true,
          detail: `${childName}’s agent reported: ${notes.join(", ")}. Controls may not be fully enforced until this is fixed.`,
          when: t.lastErrorAtUtc ? new Date(t.lastErrorAtUtc).toLocaleString() : ""
        });
      }

      // Web alerts count
      if ((st.webAlertsToday || 0) > 0){
        alerts.push({
          sev: "info",
          childName,
          childId,
          title: "Web alerts today",
          detail: `${childName} has ${st.webAlertsToday} web alert(s) today. Review blocked domains and adjust categories if needed.`,
          when: ""
        });
      }

      // Blocked attempts spike
      if ((st.blockedAttemptsToday || 0) >= 10){
        alerts.push({
          sev: "warning",
          childName,
          childId,
          title: "Many blocked attempts today",
          detail: `${childName} has ${st.blockedAttemptsToday} blocked attempt(s) today. Consider whether a rule is too strict or explain the reason.`,
          when: ""
        });
      }
    });

    // Sort: danger > warning > info, then by child name.
    const rank = { danger: 0, warning: 1, info: 2 };
    alerts.sort((a,b) => (rank[a.sev] - rank[b.sev]) || String(a.childName).localeCompare(String(b.childName)));

    return alerts;
  }

  function renderAlertActions(alert){
    const childId = alert && alert.childId ? String(alert.childId) : "";
    const openChild = childId
      ? `<a class="btn" href="#/child/${encodeURIComponent(childId)}">Open child</a>`
      : "";

    const openRequests = alert && alert.openRequests && childId
      ? `<a class="btn" href="#/requests?child=${encodeURIComponent(childId)}&status=Pending">Open requests</a>`
      : "";

    const openSupport = alert && alert.openSupport && childId
      ? `<a class="btn" href="#/support?child=${encodeURIComponent(childId)}">Open support</a>`
      : "";

    const key = ackKeyFor(alert);
    const acked = isAcked(alert);
    const ackBtn = key
      ? `<button class="btn" type="button" data-alert-ack="1" data-ack-key="${escapeHtml(key)}">${acked ? "Unacknowledge" : "Acknowledge"}</button>`
      : "";

    return `${openChild}${openRequests}${openSupport}${ackBtn}`;
  }

  function bindAlertsList(listEl, rerender){
    if (!listEl) return;
    if (listEl.__safe0neAlertsBound) return;
    listEl.__safe0neAlertsBound = true;

    listEl.addEventListener("click", (ev) => {
      const btn = ev.target && ev.target.closest ? ev.target.closest("button[data-alert-ack]") : null;
      if (!btn) return;

      const key = btn.getAttribute("data-ack-key") || "";
      if (!key) return;

      const currentlyAcked = safeGet(key) === "1";
      setAckedByKey(key, !currentlyAcked);

      // Patch 15A: persist ack state to SSOT via the local API (best-effort).
      try{
        if (window.Safe0neApi && typeof window.Safe0neApi.setChildAlertAckLocal === "function"){
          const childId = String(key).split("|")[0] || "";
          if (childId) window.Safe0neApi.setChildAlertAckLocal(childId, key, !currentlyAcked);
        }
      }catch(e){}

      if (typeof rerender === "function") rerender();
    });
  }

  // Route-level renderer. Keep minimal and delegate to the Reports module when present.
  // This exists primarily to satisfy the router delegation contract and avoid regressions.
  function renderAlerts(){
    try{
      if (window.Safe0neReports && typeof window.Safe0neReports.renderReports === "function"){
        const html = window.Safe0neReports.renderReports();
        if (typeof html === "string") return html;
      }
    }catch(e){}
    return `
      <div class="page">
        <div class="card">
          <div class="card__body">
            <h2 style="margin:0 0 8px;">Family Alerts</h2>
            <div class="muted">Loading…</div>
          </div>
        </div>
      </div>
    `;
  }

  window.Safe0neAlerts = {
    buildAlerts,
    renderAlerts,
    renderAlertActions,
    bindAlertsList,
    isAcked
  };
})();