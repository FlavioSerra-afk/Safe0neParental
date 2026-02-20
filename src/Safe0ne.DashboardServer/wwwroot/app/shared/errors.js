// Safe0ne: JS runtime error capture (best-effort, in-memory ring buffer)
// SSOT rule: runtime-only; DO NOT persist to localStorage.
// Exposes: window.Safe0neErrors.{list(),clear(),exportJson(),max,enabled}
(function(){
  'use strict';
  const max = 200;
  const buf = [];
  function nowIso(){ try{ return new Date().toISOString(); }catch{ return ''; } }
  function push(entry){
    try{
      if (!entry) return;
      buf.push(entry);
      while (buf.length > max) buf.shift();
    }catch{}
  }
  function normError(err){
    try{
      if (!err) return { name:'Error', message:'(unknown)', stack:'' };
      if (typeof err === 'string') return { name:'Error', message: err, stack:'' };
      return {
        name: String(err.name || 'Error'),
        message: String(err.message || err.toString?.() || '(unknown)'),
        stack: String(err.stack || '')
      };
    }catch{
      return { name:'Error', message:'(unknown)', stack:'' };
    }
  }

  // window.onerror
  const prevOnError = window.onerror;
  window.onerror = function(message, source, lineno, colno, error){
    try{
      push({
        t: nowIso(),
        kind: 'error',
        message: String(message || ''),
        source: String(source || ''),
        line: Number(lineno || 0),
        col: Number(colno || 0),
        error: normError(error)
      });
    }catch{}
    try{ if (typeof prevOnError === 'function') return prevOnError.apply(this, arguments); }catch{}
    return false;
  };

  // unhandled rejections
  window.addEventListener('unhandledrejection', function(ev){
    try{
      const reason = ev && ev.reason;
      push({
        t: nowIso(),
        kind: 'unhandledrejection',
        message: (reason && reason.message) ? String(reason.message) : String(reason || ''),
        source: '',
        line: 0,
        col: 0,
        error: normError(reason)
      });
    }catch{}
  });

  // Expose API
  window.Safe0neErrors = {
    enabled: true,
    max,
    list: function(){ try{ return buf.slice(); }catch{ return []; } },
    clear: function(){ try{ buf.length = 0; }catch{} },
    exportJson: function(){
      try{ return JSON.stringify({ exportedAtUtc: nowIso(), entries: buf.slice() }, null, 2); }catch{ return '{"entries":[]}'; }
    }
  };

  try{ console.info('[Safe0ne] JS error capture enabled (ring buffer max='+max+')'); }catch{}
})();
