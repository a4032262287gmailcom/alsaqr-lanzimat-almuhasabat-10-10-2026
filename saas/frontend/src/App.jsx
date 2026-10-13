import { useMemo, useState } from "react";
import { Building2, Cloud, LogOut, ShieldCheck } from "lucide-react";
import { api, clearAuth, readAuth, saveAuth } from "./api.js";
import POS from "./POS.jsx";

export default function App() {
  const [auth, setAuth] = useState(() => readAuth());
  const [mode, setMode] = useState("login");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [form, setForm] = useState({
    email: "",
    password: "",
    full_name: "",
    company_name: "",
    company_slug: "",
  });

  const selectedMembership = useMemo(
    () => auth?.memberships.find(item => item.tenant_id === auth.companyId) || null,
    [auth]
  );

  function updateField(event) {
    setForm(current => ({ ...current, [event.target.name]: event.target.value }));
  }

  async function submit(event) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      const payload = mode === "login"
        ? { email: form.email, password: form.password }
        : {
            email: form.email,
            password: form.password,
            full_name: form.full_name,
            company_name: form.company_name,
            ...(form.company_slug.trim() ? { company_slug: form.company_slug.trim().toLowerCase() } : {}),
          };
      const response = await api(mode === "login" ? "/auth/login" : "/auth/register", {
        method: "POST",
        body: payload,
      });
      saveAuth(response);
      setAuth(readAuth());
    } catch (problem) {
      setError(problem.message || "تعذر تسجيل الدخول.");
    } finally {
      setBusy(false);
    }
  }

  function logout() {
    clearAuth();
    setAuth(null);
    setForm(current => ({ ...current, password: "" }));
  }

  function chooseCompany(event) {
    const companyId = event.target.value;
    localStorage.setItem("alsaqr.companyId", companyId);
    setAuth(readAuth());
  }

  if (!auth) {
    return (
      <main className="auth-shell">
        <section className="auth-brand">
          <div className="brand-mark"><Building2 size={28} /></div>
          <span className="eyebrow">ALSAQR CLOUD ERP</span>
          <h1>إدارة شركتك، من مكان واحد.</h1>
          <p>حسابات عامة، مبيعات، مشتريات ومستودعات في مساحة عمل واحدة لكل شركة.</p>
          <div className="auth-points">
            <span><ShieldCheck size={17} /> عزل بيانات الشركات</span>
            <span><Cloud size={17} /> واجهة سحابية عربية</span>
          </div>
        </section>
        <section className="auth-card">
          <div className="auth-tabs">
            <button className={mode === "login" ? "selected" : ""} onClick={() => { setMode("login"); setError(""); }}>تسجيل الدخول</button>
            <button className={mode === "register" ? "selected" : ""} onClick={() => { setMode("register"); setError(""); }}>إنشاء شركة</button>
          </div>
          <h2>{mode === "login" ? "مرحبًا بعودتك" : "ابدأ مساحة شركتك"}</h2>
          <p className="muted">{mode === "login" ? "أدخل بيانات حسابك للمتابعة." : "سيتم إنشاء الشركة ومديرها وشجرة الحسابات الأساسية."}</p>
          <form onSubmit={submit} className="form-stack">
            {mode === "register" && (
              <>
                <label>الاسم الكامل<input name="full_name" value={form.full_name} onChange={updateField} minLength="2" maxLength="180" autoComplete="name" required /></label>
                <label>اسم الشركة<input name="company_name" value={form.company_name} onChange={updateField} minLength="2" maxLength="180" required /></label>
                <label>معرّف الشركة (اختياري)<input name="company_slug" value={form.company_slug} onChange={updateField} placeholder="company-name" pattern="[a-zA-Z0-9]+(-[a-zA-Z0-9]+)*" /></label>
              </>
            )}
            <label>البريد الإلكتروني<input name="email" type="email" value={form.email} onChange={updateField} autoComplete="email" required /></label>
            <label>كلمة المرور<input name="password" type="password" value={form.password} onChange={updateField} minLength={mode === "register" ? 12 : 1} maxLength="128" autoComplete={mode === "login" ? "current-password" : "new-password"} required /></label>
            {mode === "register" && <small className="muted">12 حرفًا على الأقل. احتفظ بكلمة المرور؛ لا توجد آلية استعادة حساب مفعّلة في هذا الإصدار الأولي.</small>}
            {error && <div className="alert error">{error}</div>}
            <button className="primary-button" disabled={busy}>{busy ? "جارٍ المعالجة…" : mode === "login" ? "دخول آمن" : "إنشاء الشركة والمتابعة"}</button>
          </form>
          <p className="tiny-note">بيئة تأسيسية للتطوير. قبل الاستخدام التجاري يجب إضافة استعادة الحساب، تحقق البريد، حدود المحاولات، إدارة الاشتراكات وسياسات الخصوصية.</p>
        </section>
      </main>
    );
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="topbar-brand">
          <span className="small-mark"><Building2 size={20} /></span>
          <div><strong>الصقر</strong><span>Cloud ERP</span></div>
        </div>
        <div className="company-switcher">
          <Building2 size={16} />
          <select value={auth.companyId} onChange={chooseCompany} aria-label="الشركة الحالية">
            {auth.memberships.map(member => (
              <option key={member.tenant_id} value={member.tenant_id}>{member.company_name} — {member.role}</option>
            ))}
          </select>
        </div>
        <div className="user-actions">
          <span className="user-chip">{auth.user.full_name}</span>
          <button className="icon-button" onClick={logout} title="تسجيل الخروج" aria-label="تسجيل الخروج"><LogOut size={17} /></button>
        </div>
      </header>
      <main className="workspace">
        {selectedMembership && <POS membership={selectedMembership} />}
      </main>
    </div>
  );
}
