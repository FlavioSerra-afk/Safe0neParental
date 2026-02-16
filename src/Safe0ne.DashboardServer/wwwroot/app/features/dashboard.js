/* Safe0ne Dashboard feature module (ADR-0003)
   Purpose: keep router.js thin by delegating Dashboard route rendering.
*/
(function(){
  'use strict';

  function renderDashboard(){
    // Keep this module UI-only and deterministic. Data binding comes later.
    return `
      ${window.pageHeader ? window.pageHeader("Dashboard",
        "A calm overview: what’s protected, what needs attention, and what you can do next.",
        "Quick actions (stub)"
      ) : ""}
      <div class="grid">
        ${window.card ? window.card("Protection status",
          "See whether protection is running and which devices are online. If something needs attention, you’ll see a clear next step.",
          "Shows status chips"
        ) : ""}
        ${window.card ? window.card("What’s next checklist",
          "No onboarding wizard. Just a short checklist that helps you set things up safely, one step at a time.",
          "Setup checklist (later)"
        ) : ""}
        ${window.card ? window.card("Weekly snapshot",
          "Screen time, top apps, top sites, blocked attempts, and requests — in plain language.",
          "Metrics (stub)"
        ) : ""}
      </div>
    `;
  }

  window.Safe0neDashboard = {
    renderDashboard,
    render: renderDashboard,
  };
})();
