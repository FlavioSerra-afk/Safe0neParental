// Parent feature module
// ADR-0003: Modular DashboardServer UI feature modules.
// Exposes: window.Safe0neParent.renderParent(ctx)
//
// This module must preserve the existing Parent Profile UX previously rendered inline in router.js.
(function () {
  'use strict';

  function renderParent(_ctx) {
    // pageHeader and card are shared helpers defined in router.js and used across feature modules.
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

  window.Safe0neParent = {
    // Standard module contract
    render: renderParent,
    renderParent,
  };
})();
