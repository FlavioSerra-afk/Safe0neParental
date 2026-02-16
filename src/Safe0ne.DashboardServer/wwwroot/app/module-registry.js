// LEGACY SHIM (do not use directly)
// This file previously contained an alternate module registry implementation.
// Keeping it as a *non-overriding shim* prevents accidental regressions when older
// patches or stray script tags load it after the canonical registry (/app/modules.js).
//
// Canonical globals:
//   - window.Safe0neModules (registry + loader)
//   - window.Safe0neUi (UI helpers, loaded from /app/shared/ui.js)

(function () {
  'use strict';

  // Never override the canonical registry if it's already present.
  if (window.Safe0neModules && typeof window.Safe0neModules === 'object') return;

  const CORE_SRC = '/app/modules.js';
  let loadPromise = null;

  function ensureCoreLoaded() {
    if (window.Safe0neModules && typeof window.Safe0neModules.list === 'function') return Promise.resolve(window.Safe0neModules);
    if (loadPromise) return loadPromise;

    loadPromise = new Promise((resolve, reject) => {
      try {
        // If a script tag already exists, just wait a tick.
        const existing = Array.from(document.getElementsByTagName('script'))
          .some(s => (s.getAttribute('src') || '') === CORE_SRC);
        if (existing) {
          setTimeout(() => resolve(window.Safe0neModules), 0);
          return;
        }

        const s = document.createElement('script');
        s.src = CORE_SRC;
        s.async = false;
        s.onload = () => resolve(window.Safe0neModules);
        s.onerror = () => reject(new Error('Failed to load canonical module registry: ' + CORE_SRC));
        document.head.appendChild(s);
      } catch (e) {
        reject(e);
      }
    });

    return loadPromise;
  }

  // A tiny proxy so callers don't explode if they accidentally hit this legacy file first.
  const proxy = {
    __legacyShim: true,
    ensureLoaded: (id) => ensureCoreLoaded().then(M => (M && M.ensureLoaded ? M.ensureLoaded(id) : undefined)),
    list: () => (window.Safe0neModules && window.Safe0neModules.list ? window.Safe0neModules.list() : []),
    isEnabled: (id) => !!(window.Safe0neModules && window.Safe0neModules.isEnabled ? window.Safe0neModules.isEnabled(id) : true),
    setEnabled: (id, on) => { if (window.Safe0neModules && window.Safe0neModules.setEnabled) window.Safe0neModules.setEnabled(id, on); },
    getHealth: (id) => (window.Safe0neModules && window.Safe0neModules.getHealth ? window.Safe0neModules.getHealth(id) : { status: 'not_loaded', lastError: '' }),
    setHealth: (id, status, err) => { if (window.Safe0neModules && window.Safe0neModules.setHealth) window.Safe0neModules.setHealth(id, status, err); }
  };

  try { console.warn('[Safe0ne] Loaded legacy /app/module-registry.js shim. Using canonical registry from ' + CORE_SRC); } catch { }

  window.Safe0neModules = proxy;
})();
