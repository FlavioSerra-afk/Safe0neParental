// Thin API client for the local DashboardServer control plane.
const Safe0neApi = {
  async getChildren(opts){
    const includeArchived = !!(opts && opts.includeArchived);
    const qs = includeArchived ? '?includeArchived=true' : '';
    // Local Mode (preferred): Control Plane local API.
    // Fallback: legacy v1 API.
    const local = await _get(`/api/local/children${qs}`);
    if (local && local.ok) return local;
    return await _get(`/api/v1/children`);
  },

  // Local Mode: Children CRUD
  async createChildLocal(payload){
    return await _post(`/api/local/children`, payload);
  },
  async patchChildLocal(childId, payload){
    return await _patch(`/api/local/children/${encodeURIComponent(childId)}`, payload);
  },
  async getChildProfileLocal(childId){
    return await _get(`/api/local/children/${encodeURIComponent(childId)}/profile`);
  },
  async putChildProfileLocal(childId, payload){
    return await _put(`/api/local/children/${encodeURIComponent(childId)}/profile`, payload);
  },
  async getChildPolicyLocal(childId){
    return await _get(`/api/local/children/${encodeURIComponent(childId)}/policy`);
  },
  async putChildPolicyLocal(childId, payload){
    return await _put(`/api/local/children/${encodeURIComponent(childId)}/policy`, payload);
  },
  async patchChildPolicyLocal(childId, payload){
    return await _patch(`/api/local/children/${encodeURIComponent(childId)}/policy`, payload);
  },


  // Local Mode: Devices + pairing
  async getChildDevicesLocal(childId){
    return await _get(`/api/local/children/${encodeURIComponent(childId)}/devices`);
  },
  async startPairingLocal(childId){
    return await _post(`/api/local/children/${encodeURIComponent(childId)}/devices/pair`, null);
  },
  async getChildPairingLocal(childId){
    return await _get(`/api/local/children/${encodeURIComponent(childId)}/devices/pairing`);
  },
  async clearChildPairingLocal(childId){
    return await _del(`/api/local/children/${encodeURIComponent(childId)}/devices/pairing`);
  },

  async unpairDeviceLocal(deviceId){
    return await _del(`/api/local/devices/${encodeURIComponent(deviceId)}`);
  },

  async revokeDeviceTokenLocal(deviceId, payload){
    return await _post(`/api/local/devices/${encodeURIComponent(deviceId)}/revoke`, payload || {});
  },

  // Local Mode: Activity
  async getChildActivityLocal(childId, opts){
    const params = [];
    if (opts && opts.from) params.push(`from=${encodeURIComponent(opts.from)}`);
    if (opts && opts.to) params.push(`to=${encodeURIComponent(opts.to)}`);
    if (opts && opts.take) params.push(`take=${encodeURIComponent(opts.take)}`);
    const qs = params.length ? `?${params.join("&")}` : "";
    return await _get(`/api/local/children/${encodeURIComponent(childId)}/activity${qs}`);
  },

  async exportChildActivityLocal(childId){
    return await _get(`/api/local/children/${encodeURIComponent(childId)}/activity/export`);
  },

  // Local Mode: Audit (append-only)
  async getChildAuditLocal(childId, opts){
    const params = [];
    if (opts && opts.take) params.push(`take=${encodeURIComponent(opts.take)}`);
    const qs = params.length ? `?${params.join("&")}` : "";
    return await _get(`/api/local/children/${encodeURIComponent(childId)}/audit${qs}`);
  },

  // Local Mode: Location (last known)
  async getChildLocationLocal(childId){
    return await _get(`/api/local/children/${encodeURIComponent(childId)}/location`);
  },

  // Local Mode: Alerts state (SSOT-backed). Best-effort; UI falls back to localStorage.
  async getChildAlertsStateLocal(childId){
    return await _get(`/api/local/children/${encodeURIComponent(childId)}/alerts/state`);
  },
  async setChildAlertAckLocal(childId, key, acked){
    return await _post(`/api/local/children/${encodeURIComponent(childId)}/alerts/ack`, {
      key: String(key || ""),
      acked: !!acked
    });
  },

  async getChildPolicy(childId){
    return await _get(`/api/v1/children/${encodeURIComponent(childId)}/policy`);
  },
  async getChildStatus(childId){
    return await _get(`/api/v1/children/${encodeURIComponent(childId)}/status`);
  },
  async getEffectiveChildState(childId){
    return await _get(`/api/v1/children/${encodeURIComponent(childId)}/effective`);
  },
  async getChildDevices(childId){
    return await _get(`/api/v1/children/${encodeURIComponent(childId)}/devices`);
  },
  async startPairing(childId){
    return await _post(`/api/v1/children/${encodeURIComponent(childId)}/pair/start`, null);
  },
  async getChildCommands(childId, take){
    const t = take ? `?take=${encodeURIComponent(take)}` : "";
    return await _get(`/api/v1/children/${encodeURIComponent(childId)}/commands${t}`);
  },
  async sendChildCommand(childId, payload){
    return await _post(`/api/v1/children/${encodeURIComponent(childId)}/commands`, payload);
  },
  async updateChildPolicy(childId, payload){
    // payload supports: mode, updatedBy, alwaysAllowed, grantMinutes
    return await _put(`/api/v1/children/${encodeURIComponent(childId)}/policy`, payload);
  },

  // 16W23: rollback to last known good policy snapshot (server-side).
  async rollbackPolicyToLastKnownGood(childId, payload){
    return await _post(`/api/v1/children/${encodeURIComponent(childId)}/policy/rollback-last-known-good`, payload || {});
  },
  // P11: Requests inbox + decision loop.
  async getRequests(childId, status, take){
    const params = [];
    if (childId) params.push(`childId=${encodeURIComponent(childId)}`);
    if (status) params.push(`status=${encodeURIComponent(status)}`);
    if (take) params.push(`take=${encodeURIComponent(take)}`);
    const qs = params.length ? `?${params.join("&")}` : "";
    return await _get(`/api/v1/requests${qs}`);
  },
  async decideRequest(requestId, payload){
    return await _post(`/api/v1/requests/${encodeURIComponent(requestId)}/decide`, payload);
  },
  // K8: Child request status list (parent can also use this for per-child history).
  async getChildRequests(childId){
    return await _get(`/api/v1/children/${encodeURIComponent(childId)}/requests`);
  },
  // K9: diagnostics bundle metadata + download
  async getLatestDiagnosticsInfo(childId){
    return await _get(`/api/v1/children/${encodeURIComponent(childId)}/diagnostics/bundles/latest/info`);
  },

  async getDiagnosticsBundles(childId, max){
    const m = (max && max > 0) ? max : 25;
    return await _get(`/api/v1/children/${encodeURIComponent(childId)}/diagnostics/bundles?max=${encodeURIComponent(String(m))}`);
  },

  // Local Mode: Web telemetry/summary (Phase 15D)
  async getChildWebRecentLocal(childId, take){
    const t = (take && take > 0) ? take : 100;
    return await _get(`/api/local/children/${encodeURIComponent(childId)}/web/recent?take=${encodeURIComponent(String(t))}`);
  },
  async getChildWebCategoriesSummaryLocal(childId, days){
    const d = (days && days > 0) ? days : 7;
    return await _get(`/api/local/children/${encodeURIComponent(childId)}/web/categories/summary?days=${encodeURIComponent(String(d))}`);
  },

};

async function _get(url){
  try{
    const res = await fetch(url, { cache: "no-store" });
    const body = await res.json().catch(() => null);
    if (!res.ok){
      return { ok:false, error: (body && body.error && body.error.message) ? body.error.message : `HTTP ${res.status}` };
    }
    return { ok:true, data: body ? body.data : null };
  }catch{
    return { ok:false, error: "Network error" };
  }
}

async function _put(url, payload){
  try{
    const res = await fetch(url, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });
    const body = await res.json().catch(() => null);
    if (!res.ok){
      return { ok:false, error: (body && body.error && body.error.message) ? body.error.message : `HTTP ${res.status}` };
    }
    return { ok:true, data: body ? body.data : null };
  }catch{
    return { ok:false, error: "Network error" };
  }
}

async function _patch(url, payload){
  try{
    const res = await fetch(url, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });
    const body = await res.json().catch(() => null);
    if (!res.ok){
      return { ok:false, error: (body && body.error && body.error.message) ? body.error.message : `HTTP ${res.status}` };
    }
    return { ok:true, data: body ? body.data : null };
  }catch{
    return { ok:false, error: "Network error" };
  }
}

async function _post(url, payload){
  try{
    const opts = { method: "POST" };
    if (payload !== null && payload !== undefined){
      opts.headers = { "Content-Type": "application/json" };
      opts.body = JSON.stringify(payload);
    }
    const res = await fetch(url, opts);
    const body = await res.json().catch(() => null);
    if (!res.ok){
      return { ok:false, error: (body && body.error && body.error.message) ? body.error.message : `HTTP ${res.status}` };
    }
    return { ok:true, data: body ? body.data : null };
  }catch{
    return { ok:false, error: "Network error" };
  }
}


async function _del(url){
  try{
    const res = await fetch(url, { method: 'DELETE' });
    const body = await res.json().catch(() => null);
    if (!res.ok){
      return { ok:false, error: (body && body.error && body.error.message) ? body.error.message : `HTTP ${res.status}` };
    }
    return { ok:true, data: body ? body.data : null };
  }catch{
    return { ok:false, error: 'Network error' };
  }
}
