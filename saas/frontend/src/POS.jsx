import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Barcode, CheckCircle2, CircleAlert, CloudOff, Minus, Plus, RefreshCw, Search, ShoppingBag, Trash2, Wifi, WifiOff } from "lucide-react";
import { api, ApiError } from "./api.js";
import { getQueuedSales, queueSale, readOfflineCache, removeQueuedSale, saveOfflineCache } from "./offlineQueue.js";

const currency = new Intl.NumberFormat("ar-SA", { style: "currency", currency: "SAR", maximumFractionDigits: 2 });
const roundMoney = amount => Math.round((Number(amount) + Number.EPSILON) * 100) / 100;

function buildTotals(items) {
  const subtotal = roundMoney(items.reduce((sum, item) => sum + Number(item.price) * item.quantity, 0));
  const vat = roundMoney(items.reduce((sum, item) => sum + roundMoney(Number(item.price) * item.quantity) * Number(item.vat_rate) / 100, 0));
  return { subtotal, vat, total: roundMoney(subtotal + vat) };
}

function saudiDate() {
  const parts = new Intl.DateTimeFormat("en-GB", {
    timeZone: "Asia/Riyadh",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(new Date());
  const values = Object.fromEntries(parts.map(part => [part.type, part.value]));
  return `${values.year}-${values.month}-${values.day}`;
}

function errorIsOffline(error) {
  return !navigator.onLine || (error instanceof TypeError && !(error instanceof ApiError));
}

export default function POS({ membership }) {
  const [products, setProducts] = useState([]);
  const [contacts, setContacts] = useState([]);
  const [warehouses, setWarehouses] = useState([]);
  const [cart, setCart] = useState([]);
  const [query, setQuery] = useState("");
  const [selectedContact, setSelectedContact] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [isCredit, setIsCredit] = useState(false);
  const [online, setOnline] = useState(navigator.onLine);
  const [pending, setPending] = useState([]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [notice, setNotice] = useState(null);
  const [lastInvoice, setLastInvoice] = useState(null);
  const searchRef = useRef(null);

  const totals = useMemo(() => buildTotals(cart), [cart]);
  const filteredProducts = useMemo(() => {
    const needle = query.trim().toLowerCase();
    if (!needle) return products;
    return products.filter(product =>
      [product.name, product.sku, product.barcode || ""].some(value => String(value).toLowerCase().includes(needle))
    );
  }, [products, query]);

  const refreshPending = useCallback(async () => {
    try { setPending(await getQueuedSales(membership.tenant_id)); } catch { setPending([]); }
  }, [membership.tenant_id]);

  const loadBaseData = useCallback(async () => {
    setLoading(true);
    setNotice(null);
    const tasks = [
      ["/setup/products", "products", setProducts],
      ["/setup/contacts?contact_type=CUSTOMER", "contacts", setContacts],
      ["/setup/warehouses", "warehouses", setWarehouses],
    ];
    const messages = [];
    await Promise.all(tasks.map(async ([endpoint, cacheKey, setter]) => {
      try {
        const data = await api(endpoint, { companyId: membership.tenant_id });
        setter(data || []);
        await saveOfflineCache(`${membership.tenant_id}:${cacheKey}`, data || []);
      } catch (error) {
        const cached = await readOfflineCache(`${membership.tenant_id}:${cacheKey}`).catch(() => null);
        if (cached) setter(cached);
        else if (!errorIsOffline(error)) messages.push(error.message);
      }
    }));
    await refreshPending();
    if (messages.length) setNotice({ type: "error", text: messages[0] });
    setLoading(false);
  }, [membership.tenant_id, refreshPending]);

  const syncPending = useCallback(async () => {
    if (!navigator.onLine) return;
    const queue = await getQueuedSales(membership.tenant_id).catch(() => []);
    let synced = 0;
    for (const record of queue) {
      try {
        await api("/commerce/sales", { method: "POST", body: record.payload, companyId: membership.tenant_id });
        await removeQueuedSale(record.id);
        synced += 1;
      } catch (error) {
        if (errorIsOffline(error)) break;
        setNotice({ type: "error", text: `تعذرت مزامنة فاتورة معلقة: ${error.message}` });
        break;
      }
    }
    await refreshPending();
    if (synced > 0) setNotice({ type: "success", text: `تمت مزامنة ${synced} فاتورة مع الخادم.` });
  }, [membership.tenant_id, refreshPending]);

  useEffect(() => {
    loadBaseData();
  }, [loadBaseData]);

  useEffect(() => {
    const becameOnline = () => { setOnline(true); syncPending(); loadBaseData(); };
    const becameOffline = () => { setOnline(false); };
    window.addEventListener("online", becameOnline);
    window.addEventListener("offline", becameOffline);
    const timer = window.setInterval(refreshPending, 5000);
    return () => {
      window.removeEventListener("online", becameOnline);
      window.removeEventListener("offline", becameOffline);
      window.clearInterval(timer);
    };
  }, [loadBaseData, refreshPending, syncPending]);

  function addProduct(product) {
    setCart(current => {
      const existing = current.find(item => item.id === product.id);
      if (existing) return current.map(item => item.id === product.id ? { ...item, quantity: item.quantity + 1 } : item);
      return [...current, {
        id: product.id,
        sku: product.sku,
        name: product.name,
        price: Number(product.unit_price),
        vat_rate: Number(product.vat_rate),
        item_type: product.item_type,
        quantity: 1,
      }];
    });
    setNotice(null);
  }

  function changeQuantity(productId, delta) {
    setCart(current => current
      .map(item => item.id === productId ? { ...item, quantity: Math.max(0, item.quantity + delta) } : item)
      .filter(item => item.quantity > 0));
  }

  function updateQuantity(productId, value) {
    const quantity = Math.max(0, Math.min(100000, Number(value) || 0));
    setCart(current => current.map(item => item.id === productId ? { ...item, quantity } : item).filter(item => item.quantity > 0));
  }

  async function completeSale() {
    if (!cart.length) {
      setNotice({ type: "error", text: "أضف صنفًا واحدًا على الأقل إلى الفاتورة." });
      return;
    }
    if (!selectedContact) {
      setNotice({ type: "error", text: "اختر العميل قبل إتمام البيع." });
      return;
    }
    if (cart.some(item => item.item_type !== "SERVICE") && !warehouseId) {
      setNotice({ type: "error", text: "اختر المستودع قبل بيع الأصناف المخزنية." });
      return;
    }
    const payload = {
      issue_date: saudiDate(),
      contact_id: selectedContact,
      warehouse_id: warehouseId || null,
      is_credit: isCredit,
      currency_code: "SAR",
      exchange_rate: "1",
      client_reference: crypto.randomUUID(),
      items: cart.map(item => ({
        product_id: item.id,
        description: item.name,
        quantity: String(item.quantity),
        unit_price: String(item.price),
        discount_rate: "0",
        vat_rate: String(item.vat_rate),
      })),
    };

    setSubmitting(true);
    setNotice(null);
    try {
      if (!navigator.onLine) throw new TypeError("offline");
      const invoice = await api("/commerce/sales", { method: "POST", body: payload, companyId: membership.tenant_id });
      setLastInvoice(invoice);
      setCart([]);
      setNotice({ type: "success", text: `تم ترحيل الفاتورة ${invoice.number} بإجمالي ${currency.format(Number(invoice.total))}.` });
      searchRef.current?.focus();
    } catch (error) {
      if (errorIsOffline(error)) {
        try {
          await queueSale(payload, membership.tenant_id);
          await refreshPending();
          setCart([]);
          setNotice({ type: "pending", text: "حُفظ الطلب محليًا بانتظار الاتصال. لم يُعتبر البيع مرحّلًا حتى يؤكده الخادم." });
        } catch (storageError) {
          setNotice({ type: "error", text: `تعذر الاتصال وحفظ الطلب محليًا: ${storageError.message}` });
        }
      } else {
        setNotice({ type: "error", text: error.message || "تعذر إتمام البيع." });
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="pos-page">
      <div className="page-heading">
        <div>
          <span className="eyebrow">SALES WORKSPACE</span>
          <h1>نقطة البيع</h1>
          <p>بيع سريع مع إعادة تحقق المخزون والحسابات في الخادم.</p>
        </div>
        <div className={online ? "connection-pill online" : "connection-pill offline"}>
          {online ? <Wifi size={16} /> : <WifiOff size={16} />}
          {online ? "متصل بالخادم" : "وضع عدم الاتصال"}
        </div>
      </div>

      {notice && (
        <div className={`notice ${notice.type}`}>
          {notice.type === "success" ? <CheckCircle2 size={18} /> : notice.type === "pending" ? <CloudOff size={18} /> : <CircleAlert size={18} />}
          <span>{notice.text}</span>
          <button onClick={() => setNotice(null)} aria-label="إغلاق">×</button>
        </div>
      )}

      <div className="pos-layout">
        <section className="catalog-panel">
          <div className="panel-heading">
            <div><h2>الأصناف والخدمات</h2><span className="muted">{products.length} صنف متاح</span></div>
            <button className="subtle-button" onClick={loadBaseData} disabled={loading}><RefreshCw size={15} /> تحديث</button>
          </div>
          <div className="search-input">
            <Search size={17} />
            <input ref={searchRef} value={query} onChange={event => setQuery(event.target.value)} placeholder="ابحث بالاسم أو الرمز أو الباركود" aria-label="البحث عن منتج" />
            <Barcode size={19} />
          </div>
          {loading && !products.length ? <div className="empty-state">جارٍ تحميل الأصناف…</div> : (
            <div className="product-grid">
              {filteredProducts.map(product => (
                <button className="product-card" key={product.id} onClick={() => addProduct(product)}>
                  <span className="product-icon"><ShoppingBag size={20} /></span>
                  <strong>{product.name}</strong>
                  <span className="product-code">{product.sku}</span>
                  <span className="product-price">{currency.format(Number(product.unit_price))}</span>
                  <span className="product-kind">{product.item_type === "SERVICE" ? "خدمة" : product.item_type === "MANUFACTURED" ? "مصنّع" : "مخزني"} · VAT {Number(product.vat_rate)}%</span>
                  <span className="add-product"><Plus size={15} /> إضافة</span>
                </button>
              ))}
              {!filteredProducts.length && <div className="empty-state">لا توجد أصناف مطابقة. أنشئ الأصناف من إدارة البيانات أولًا.</div>}
            </div>
          )}
          <div className="catalog-footnote">الحساب النهائي والضريبة وتوفر المخزون يعاد التحقق منها على الخادم قبل الترحيل.</div>
        </section>

        <section className="cart-panel">
          <div className="panel-heading cart-title">
            <div><h2>الفاتورة الحالية</h2><span className="muted">{cart.reduce((sum, item) => sum + item.quantity, 0)} وحدة</span></div>
            <button className="text-button danger-text" disabled={!cart.length} onClick={() => setCart([])}><Trash2 size={15} /> تفريغ</button>
          </div>

          <div className="cart-setup">
            <label>العميل
              <select value={selectedContact} onChange={event => setSelectedContact(event.target.value)}>
                <option value="">اختر العميل</option>
                {contacts.map(contact => <option value={contact.id} key={contact.id}>{contact.name}{contact.tax_number ? ` · ${contact.tax_number}` : ""}</option>)}
              </select>
            </label>
            <label>المستودع
              <select value={warehouseId} onChange={event => setWarehouseId(event.target.value)}>
                <option value="">اختر المستودع</option>
                {warehouses.map(warehouse => <option value={warehouse.id} key={warehouse.id}>{warehouse.name}</option>)}
              </select>
            </label>
            <label className="check-row">
              <input type="checkbox" checked={isCredit} onChange={event => setIsCredit(event.target.checked)} />
              <span>فاتورة آجلة (ذمم عميل)</span>
            </label>
          </div>

          <div className="cart-lines">
            {!cart.length ? (
              <div className="empty-cart"><ShoppingBag size={28} /><strong>الفاتورة فارغة</strong><span>اضغط على أي صنف لإضافته.</span></div>
            ) : cart.map(item => (
              <div className="cart-line" key={item.id}>
                <div className="line-main">
                  <strong>{item.name}</strong>
                  <span>{currency.format(item.price)} · ضريبة {item.vat_rate}%</span>
                </div>
                <div className="line-quantity">
                  <button onClick={() => changeQuantity(item.id, -1)} aria-label="تقليل الكمية"><Minus size={14} /></button>
                  <input type="number" min="0" step="1" value={item.quantity} onChange={event => updateQuantity(item.id, event.target.value)} aria-label={`كمية ${item.name}`} />
                  <button onClick={() => changeQuantity(item.id, 1)} aria-label="زيادة الكمية"><Plus size={14} /></button>
                </div>
                <strong className="line-total">{currency.format(roundMoney(item.price * item.quantity) * (1 + item.vat_rate / 100))}</strong>
              </div>
            ))}
          </div>

          <div className="cart-summary">
            <div><span>الإجمالي قبل الضريبة</span><strong>{currency.format(totals.subtotal)}</strong></div>
            <div><span>ضريبة القيمة المضافة</span><strong>{currency.format(totals.vat)}</strong></div>
            <div className="total-row"><span>الإجمالي شامل الضريبة</span><strong>{currency.format(totals.total)}</strong></div>
          </div>

          <button className="primary-button checkout-button" disabled={submitting || !cart.length} onClick={completeSale}>
            {submitting ? "جارٍ إرسال الفاتورة…" : online ? "إتمام البيع وترحيله" : "حفظ محليًا للمزامنة"}
          </button>
          <div className="offline-status">
            <span className={pending.length ? "pending-count active" : "pending-count"}>{pending.length}</span>
            <span>فواتير بانتظار المزامنة</span>
            {online && pending.length > 0 && <button className="text-button" onClick={syncPending}>مزامنة الآن</button>}
          </div>
          {lastInvoice?.qr_payload && <div className="qr-note">تم إنشاء حمولة QR الأساسية في الفاتورة. الاعتماد النهائي للفوترة الإلكترونية يتطلب تهيئة واعتماد زاتكا حسب المرحلة.</div>}
        </section>
      </div>
    </div>
  );
}
