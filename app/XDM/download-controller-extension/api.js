const BASE_URL = "http://127.0.0.1:8597/controller/v1";
const TOKEN_HEADER = "X-XDM-Controller-Token";
const CLIENT_HEADER = "X-XDM-Controller-Client";
const CLIENT_ID = "hmbfgklncdkaclckkibeflpmlaedhgck";
let token = null;

async function request(path, options = {}, retry = true) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 3500);
  try {
    const response = await fetch(`${BASE_URL}${path}`, {
      ...options,
      cache: "no-store",
      credentials: "omit",
      signal: controller.signal,
      headers: {
        ...(options.headers || {}),
        [CLIENT_HEADER]: CLIENT_ID,
        ...(token ? { [TOKEN_HEADER]: token } : {})
      }
    });
    if (response.status === 401 && retry) {
      token = null;
      await openSession();
      return request(path, options, false);
    }
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(payload.error || `XDM returned ${response.status}`);
    return payload;
  } finally {
    clearTimeout(timeout);
  }
}

export async function openSession() {
  const payload = await request("/session", {}, false);
  if (!payload.token) throw new Error("XDM did not provide a controller token");
  token = payload.token;
}

export async function getDownloads() {
  if (!token) await openSession();
  const payload = await request("/downloads");
  return Array.isArray(payload.downloads) ? payload.downloads : [];
}

export async function runAction(id, action) {
  if (!token) await openSession();
  return request(`/downloads/${encodeURIComponent(id)}/${action}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: "{}"
  });
}
