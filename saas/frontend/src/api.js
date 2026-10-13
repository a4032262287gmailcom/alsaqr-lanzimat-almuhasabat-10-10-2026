const API_URL = (import.meta.env.VITE_API_URL || "http://localhost:8000/api/v1").replace(/\/$/, "");

export class ApiError extends Error {
  constructor(status, message, details) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.details = details;
  }
}

export async function api(path, options = {}) {
  const { method = "GET", body, companyId, headers: extraHeaders = {}, signal } = options;
  const headers = new Headers(extraHeaders);
  const token = localStorage.getItem("alsaqr.accessToken");
  const selectedCompany = companyId || localStorage.getItem("alsaqr.companyId");

  if (token) headers.set("Authorization", `Bearer ${token}`);
  if (selectedCompany && !path.startsWith("/auth/")) headers.set("X-Company-ID", selectedCompany);

  let requestBody = body;
  if (body !== undefined && !(body instanceof FormData) && typeof body !== "string") {
    headers.set("Content-Type", "application/json");
    requestBody = JSON.stringify(body);
  }

  const response = await fetch(`${API_URL}${path}`, {
    method,
    headers,
    body: requestBody,
    signal,
    credentials: "omit",
  });

  const contentType = response.headers.get("content-type") || "";
  let payload = null;
  if (contentType.includes("application/json")) {
    try { payload = await response.json(); } catch { payload = null; }
  } else if (response.status !== 204) {
    payload = await response.text().catch(() => "");
  }

  if (!response.ok) {
    const detail = payload && typeof payload === "object" ? payload.detail : null;
    const message = typeof detail === "string"
      ? detail
      : detail && typeof detail === "object"
        ? JSON.stringify(detail)
        : `تعذر إكمال الطلب (HTTP ${response.status})`;
    throw new ApiError(response.status, message, detail);
  }

  return payload;
}

export function saveAuth(response) {
  localStorage.setItem("alsaqr.accessToken", response.access_token);
  localStorage.setItem("alsaqr.user", JSON.stringify(response.user));
  localStorage.setItem("alsaqr.memberships", JSON.stringify(response.memberships || []));
  if (response.memberships?.length && !response.memberships.some(m => m.tenant_id === localStorage.getItem("alsaqr.companyId"))) {
    localStorage.setItem("alsaqr.companyId", response.memberships[0].tenant_id);
  }
}

export function clearAuth() {
  localStorage.removeItem("alsaqr.accessToken");
  localStorage.removeItem("alsaqr.user");
  localStorage.removeItem("alsaqr.memberships");
  localStorage.removeItem("alsaqr.companyId");
}

export function readAuth() {
  try {
    const accessToken = localStorage.getItem("alsaqr.accessToken");
    const user = JSON.parse(localStorage.getItem("alsaqr.user") || "null");
    const memberships = JSON.parse(localStorage.getItem("alsaqr.memberships") || "[]");
    const companyId = localStorage.getItem("alsaqr.companyId");
    if (!accessToken || !user || !Array.isArray(memberships) || !memberships.some(m => m.tenant_id === companyId)) return null;
    return { accessToken, user, memberships, companyId };
  } catch {
    return null;
  }
}
