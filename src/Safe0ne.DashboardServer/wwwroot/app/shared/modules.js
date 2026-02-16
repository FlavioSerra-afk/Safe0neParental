// Safe0ne module registry + toggles (idempotent)
(function(){
  'use strict';
  if (window.Safe0neModules) return;

  var STORAGE_PREFIX = 'safe0ne_module_enabled_v1:';
  var EVENT_NAME = 'safe0ne:moduleChanged';

  // Map hash route segment -> module id
  var ROUTE_TO_MODULE = {
    dashboard:'dashboard',
    parent:'parent',
    children:'children',
    child:'children',
    requests:'requests',
    reports:'reports',
    support:'support',
    admin:'admin',
    devtools:'devtools'
  };

  // Canonical module catalog
  var CATALOG = {
    dashboard:{ id:'dashboard', title:'Dashboard', global:'Safe0neDashboard', canDisable:false, defaultEnabled:true },
    parent:{ id:'parent', title:'Parent', global:'Safe0neParent', canDisable:true, defaultEnabled:true },
    children:{ id:'children', title:'Children', global:'Safe0neChildren', canDisable:true, defaultEnabled:true },
    requests:{ id:'requests', title:'Requests', global:'Safe0neRequests', canDisable:true, defaultEnabled:true },
    reports:{ id:'reports', title:'Alerts & Reports', global:'Safe0neReports', canDisable:true, defaultEnabled:true },
    support:{ id:'support', title:'Support', global:'Safe0neSupport', canDisable:true, defaultEnabled:true },
    admin:{ id:'admin', title:'Admin', global:'Safe0neAdmin', canDisable:true, defaultEnabled:true },
    devtools:{ id:'devtools', title:'Dev Tools', global:'Safe0neDevTools', canDisable:false, defaultEnabled:true }
  };

  function lsGet(k){ try{ return localStorage.getItem(k); }catch(e){ return null; } }
  function lsSet(k,v){ try{ localStorage.setItem(k,v); }catch(e){} }
  function storageKey(id){ return STORAGE_PREFIX + id; }
  function asBool(v){
    var s = String(v==null?'':v).toLowerCase().trim();
    return s==='true' || s==='1' || s==='yes' || s==='on';
  }

  // enabled state
  var state = {};
  Object.keys(CATALOG).forEach(function(id){
    var stored = lsGet(storageKey(id));
    state[id] = (stored==null || stored==='') ? !!CATALOG[id].defaultEnabled : asBool(stored);
  });

  // health state
  var health = {};
  Object.keys(CATALOG).forEach(function(id){
    health[id] = { status:'unknown', lastOkAt:null, lastFailAt:null, lastError:null };
  });

  function getModuleIdForRoute(routeKey){
    return ROUTE_TO_MODULE[String(routeKey||'').toLowerCase()] || null;
  }

  // Returns the canonical module meta object for a given route key.
  // NOTE: this intentionally returns an object (not just an id string) so
  // router-level boundaries can attribute failures to the correct module.
  function getModuleForRoute(routeKey){
    var id = getModuleIdForRoute(routeKey);
    return id ? (CATALOG[id] || null) : null;
  }

  function isEnabled(id){
    id = String(id||'');
    if(!id) return true;
    if(state[id]===undefined) return true;
    return !!state[id];
  }

  function emitChange(id){
    try{ window.dispatchEvent(new CustomEvent(EVENT_NAME,{ detail:{ id:id, enabled:isEnabled(id) } }));
      // Back-compat event name used by some older code paths
      window.dispatchEvent(new CustomEvent('safeone:modules-changed',{ detail:{ id:id, enabled:isEnabled(id) } })); }
    catch(_){ }
  }

  function syncNavVisibility(){
    try{
      document.querySelectorAll('.rail__item[data-route]').forEach(function(a){
        var routeKey = a.getAttribute('data-route');
        if(routeKey==='devtools') return; // DevTools gating is separate
        var modId = getModuleIdForRoute(routeKey);
        if(!modId) return;
        a.style.display = isEnabled(modId) ? '' : 'none';
      });
    }catch(e){}
  }

  function setEnabled(id, enabled){
    id = String(id||'');
    if(!id) return;
    var meta = CATALOG[id];
    if(meta && meta.canDisable===false){
      state[id] = true;
      lsSet(storageKey(id),'true');
      syncNavVisibility();
      emitChange(id);
      return;
    }
    state[id] = !!enabled;
    lsSet(storageKey(id), state[id] ? 'true' : 'false');
    syncNavVisibility();
    emitChange(id);
  }

  function list(){
    return Object.keys(CATALOG).map(function(id){
      var m = CATALOG[id];
      var h = health[id] || { status:'unknown' };
      return {
        id: m.id,
        title: m.title,
        global: m.global,
        canDisable: !!m.canDisable,
        enabled: isEnabled(id),
        status: h.status,
        lastError: h.lastError
      };
    });
  }

  function reportOk(id){
    id = String(id||'');
    if(!health[id]) return;
    health[id].status = 'ok';
    health[id].lastOkAt = Date.now();
    health[id].lastError = null;
  }

  function reportFail(id, err){
    id = String(id||'');
    if(!health[id]) return;
    health[id].status = 'failed';
    health[id].lastFailAt = Date.now();
    try{
      health[id].lastError = err ? (err.message || String(err)) : 'unknown error';
    }catch(_){
      health[id].lastError = 'unknown error';
    }
  }

  function getStatus(id){
    id = String(id||'');
    return health[id] || { status:'unknown', lastError:null };
  }

  window.Safe0neModules = {
    EVENT_NAME: EVENT_NAME,
    getModuleForRoute: getModuleForRoute,
    getModuleIdForRoute: getModuleIdForRoute,
    isEnabled: isEnabled,
    setEnabled: setEnabled,
    list: list,
    syncNavVisibility: syncNavVisibility,
    reportOk: reportOk,
    reportFail: reportFail,
    getStatus: getStatus
  };
})();
