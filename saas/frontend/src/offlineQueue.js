const DATABASE_NAME = "alsaqr-cloud-pos";
const DATABASE_VERSION = 1;

function openDatabase() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE_NAME, DATABASE_VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains("outbox")) db.createObjectStore("outbox", { keyPath: "id" });
      if (!db.objectStoreNames.contains("cache")) db.createObjectStore("cache", { keyPath: "key" });
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error || new Error("تعذر فتح مخزن العمل دون اتصال"));
  });
}

async function transact(storeName, mode, action) {
  const db = await openDatabase();
  try {
    return await new Promise((resolve, reject) => {
      const transaction = db.transaction(storeName, mode);
      const store = transaction.objectStore(storeName);
      let value;
      try { value = action(store); } catch (error) { reject(error); return; }
      transaction.oncomplete = () => resolve(value?.result);
      transaction.onerror = () => reject(transaction.error || new Error("تعذر إتمام عملية التخزين"));
      transaction.onabort = () => reject(transaction.error || new Error("تم إلغاء عملية التخزين"));
    });
  } finally {
    db.close();
  }
}

export async function queueSale(payload) {
  const record = {
    id: payload.client_reference,
    kind: "SALE",
    payload,
    createdAt: new Date().toISOString(),
  };
  await transact("outbox", "readwrite", store => store.put(record));
  return record;
}

export async function getQueuedSales() {
  const db = await openDatabase();
  try {
    return await new Promise((resolve, reject) => {
      const request = db.transaction("outbox", "readonly").objectStore("outbox").getAll();
      request.onsuccess = () => resolve((request.result || []).sort((a, b) => a.createdAt.localeCompare(b.createdAt)));
      request.onerror = () => reject(request.error || new Error("تعذر قراءة الفواتير المعلقة"));
    });
  } finally {
    db.close();
  }
}

export async function removeQueuedSale(id) {
  await transact("outbox", "readwrite", store => store.delete(id));
}

export async function saveOfflineCache(key, value) {
  const record = { key, value, savedAt: new Date().toISOString() };
  await transact("cache", "readwrite", store => store.put(record));
}

export async function readOfflineCache(key) {
  const db = await openDatabase();
  try {
    return await new Promise((resolve, reject) => {
      const request = db.transaction("cache", "readonly").objectStore("cache").get(key);
      request.onsuccess = () => resolve(request.result?.value ?? null);
      request.onerror = () => reject(request.error || new Error("تعذر قراءة البيانات المخزنة"));
    });
  } finally {
    db.close();
  }
}
