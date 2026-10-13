import React, { useState, useEffect } from 'react';
import { ShowDashboardView } from './components/screens/ShowDashboardView';
import { AccountingCenterScreen } from './components/screens/AccountingCenterScreen';
import { FinancialVouchersScreen } from './components/screens/FinancialVouchersScreen';
import { SalesEntryScreen } from './components/screens/SalesEntryScreen';
import { PurchaseEntryScreen } from './components/screens/PurchaseEntryScreen';
import { InventoryCenterScreen } from './components/screens/InventoryCenterScreen';
import { DomainCentersScreen } from './components/screens/DomainCentersScreen';
import { ReportsScreen } from './components/screens/ReportsScreen';
import { SecurityAdminScreen } from './components/screens/SecurityAdminScreen';
import { FullSystemLinkScreen } from './components/screens/FullSystemLinkScreen';
import { ScreenReplacementFormView } from './components/screens/ScreenReplacementFormView';
import { ConnectionSettingsFormView } from './components/screens/ConnectionSettingsFormView';
import { SchemaFormView } from './components/screens/SchemaFormView';
import { ExportProjectModal } from './components/common/ExportProjectModal';

import { OpenTab, ModuleCategory } from './types/erp';
import { ALL_SYSTEM_SCREENS } from './data/gts2026Metadata';
import { erpDb } from './services/erpDatabase';
import {
  Search,
  X,
  ExternalLink,
  RotateCcw,
  Minus,
  Square,
  ChevronDown
} from 'lucide-react';

export default function App() {
  const [, setDbVersion] = useState(0);

  // Subscribe to central ERP database updates
  useEffect(() => {
    return erpDb.subscribe(() => {
      setDbVersion((v) => v + 1);
    });
  }, []);

  const user = erpDb.getUser();
  const branches = erpDb.getBranches();

  // MDI Open Tabs
  const [openTabs, setOpenTabs] = useState<OpenTab[]>([
    {
      id: 'tab-0',
      screenId: 0,
      title: 'لوحة تشغيل الصقر ERP',
      category: 'الرئيسية',
    },
  ]);
  const [activeTabId, setActiveTabId] = useState<string>('tab-0');

  // Quick Launcher Dialog state
  const [showQuickLauncher, setShowQuickLauncher] = useState(false);
  const [showExportModal, setShowExportModal] = useState(false);
  const [launcherSearch, setLauncherSearch] = useState('');

  // Dropdown menu state
  const [openMenu, setOpenMenu] = useState<string | null>(null);

  // Open Screen Handler with deep parameter linking
  const handleOpenScreen = (screenId: number, params?: Record<string, any>) => {
    const screenDef = ALL_SYSTEM_SCREENS.find((s) => s.id === screenId);
    let title = screenDef ? screenDef.name : `شاشة #${screenId}`;
    if (screenId === 0) title = 'لوحة تشغيل الصقر ERP';
    const category = screenDef ? screenDef.category : 'الرئيسية';

    const existing = openTabs.find((t) => t.screenId === screenId);
    if (existing) {
      if (params) {
        existing.params = { ...existing.params, ...params };
      }
      setActiveTabId(existing.id);
    } else {
      const newTab: OpenTab = {
        id: `tab-${screenId}-${Date.now()}`,
        screenId,
        title,
        category,
        params,
      };
      setOpenTabs([...openTabs, newTab]);
      setActiveTabId(newTab.id);
    }

    setShowQuickLauncher(false);
    setOpenMenu(null);
  };

  const handleCloseTab = (tabId: string) => {
    if (openTabs.length <= 1) return;
    const remaining = openTabs.filter((t) => t.id !== tabId);
    setOpenTabs(remaining);
    if (activeTabId === tabId) {
      const nextActive = remaining[remaining.length - 1];
      setActiveTabId(nextActive.id);
    }
  };

  const activeTab = openTabs.find((t) => t.id === activeTabId) || openTabs[0];

  // Screen Router to render screen component based on screenId
  const renderActiveScreen = () => {
    if (!activeTab) return null;
    const sId = activeTab.screenId;
    const params = activeTab.params;

    switch (sId) {
      case 0:
        return <ShowDashboardView onOpenScreen={handleOpenScreen} />;

      // Accounting Center
      case 30: // دليل الحسابات
        return <AccountingCenterScreen initialTab="accounts" onOpenScreen={handleOpenScreen} />;
      case 36: // القيود اليومية
        return <AccountingCenterScreen initialTab="journal" onOpenScreen={handleOpenScreen} />;
      case 43: // دفتر الأستاذ
        return (
          <AccountingCenterScreen
            initialTab="ledger"
            selectedAccountId={params?.accountId}
            onOpenScreen={handleOpenScreen}
          />
        );
      case 45: // ميزان المراجعة
        return <AccountingCenterScreen initialTab="trial" onOpenScreen={handleOpenScreen} />;
      case 88: // الأرباح والخسائر
        return <AccountingCenterScreen initialTab="profit" onOpenScreen={handleOpenScreen} />;

      // Financial Vouchers
      case 31: // سند قبض
        return <FinancialVouchersScreen initialKind="receipt" onOpenScreen={handleOpenScreen} />;
      case 32: // سند صرف
        return <FinancialVouchersScreen initialKind="payment" onOpenScreen={handleOpenScreen} />;
      case 123: // الشيكات
        return <FinancialVouchersScreen initialKind="checks" onOpenScreen={handleOpenScreen} />;

      // Sales
      case 59: // فاتورة مبيعات
        return (
          <SalesEntryScreen
            initialInvoiceId={params?.invoiceId}
            onOpenScreen={handleOpenScreen}
          />
        );
      case 14: // مردود مبيعات
        return <SalesEntryScreen onOpenScreen={handleOpenScreen} />;
      case 23: // العملاء
        return <SalesEntryScreen onOpenScreen={handleOpenScreen} />;

      // Purchases
      case 15: // فاتورة مشتريات
        return <PurchaseEntryScreen onOpenScreen={handleOpenScreen} />;
      case 49: // مردود مشتريات
        return <PurchaseEntryScreen onOpenScreen={handleOpenScreen} />;
      case 24: // الموردين
        return <PurchaseEntryScreen onOpenScreen={handleOpenScreen} />;

      // Inventory
      case 8: // بطاقة الصنف
      case 5: // الوحدات
      case 7: // المجموعات
      case 18: // الرصيد الافتتاحي
      case 25: // التحويل
      case 83: // الجرد
        return (
          <InventoryCenterScreen
            initialItemId={params?.itemId}
            onOpenScreen={handleOpenScreen}
          />
        );

      // Centers & Operations
      case 2: // مراكز التكلفة
      case 98: // العقود
      case 76: // الموظفون
      case 58: // المطاعم
      case 118: // التصنيع
        return <DomainCentersScreen onOpenScreen={handleOpenScreen} />;

      // Reports
      case 50: // الضريبة VAT
        return <ReportsScreen initialReport="vat" onOpenScreen={handleOpenScreen} />;
      case 41: // الحركات اليومية
        return <ReportsScreen initialReport="daily" onOpenScreen={handleOpenScreen} />;
      case 60: // كرت حركة صنف
        return (
          <ReportsScreen
            initialReport="movement"
            selectedItemId={params?.itemId}
            onOpenScreen={handleOpenScreen}
          />
        );
      case 127: // مبيعات المناديب
        return <ReportsScreen initialReport="salesman" onOpenScreen={handleOpenScreen} />;

      // Security
      case 1:
      case 3:
      case 104:
        return <SecurityAdminScreen onOpenScreen={handleOpenScreen} />;

      // Full System Link
      case 999:
        return (
          <FullSystemLinkScreen
            initialTab={params?.tab || 'screens'}
            onOpenScreen={handleOpenScreen}
          />
        );

      // Screen Replacements
      case 998:
        return <ScreenReplacementFormView onOpenScreen={handleOpenScreen} onClose={() => handleCloseTab(activeTab.id)} />;

      // Connection Settings
      case 997:
        return <ConnectionSettingsFormView onClose={() => handleCloseTab(activeTab.id)} />;

      // Schema Form
      case 996:
        return <SchemaFormView onClose={() => handleCloseTab(activeTab.id)} />;

      default:
        return <ShowDashboardView onOpenScreen={handleOpenScreen} />;
    }
  };

  const filteredLauncherScreens = ALL_SYSTEM_SCREENS.filter(
    (s) =>
      s.name.toLowerCase().includes(launcherSearch.toLowerCase()) ||
      (s.legacyName && s.legacyName.toLowerCase().includes(launcherSearch.toLowerCase())) ||
      s.category.includes(launcherSearch) ||
      s.screenNum.toString().includes(launcherSearch)
  );

  // Group runtime screens by category for the sidebar menu (matching Forms.cs LoadRuntimeScreens)
  const menuCategories: { name: string; screens: { id: number; name: string }[] }[] = [
    {
      name: 'المحاسبة',
      screens: [
        { id: 30, name: 'دليل الحسابات' },
        { id: 36, name: 'القيود اليومية' },
        { id: 43, name: 'دفتر الأستاذ وكشف الحساب' },
        { id: 45, name: 'ميزان المراجعة' },
        { id: 88, name: 'الأرباح والخسائر' },
      ],
    },
    {
      name: 'السندات',
      screens: [
        { id: 31, name: 'سند قبض' },
        { id: 32, name: 'سند صرف' },
        { id: 123, name: 'إدارة الشيكات' },
      ],
    },
    {
      name: 'المبيعات',
      screens: [
        { id: 59, name: 'فاتورة مبيعات' },
        { id: 14, name: 'مردود مبيعات' },
        { id: 23, name: 'بطاقة العميل' },
      ],
    },
    {
      name: 'المشتريات',
      screens: [
        { id: 15, name: 'فاتورة مشتريات' },
        { id: 49, name: 'مردود مشتريات' },
        { id: 24, name: 'بطاقة المورد' },
      ],
    },
    {
      name: 'المخزون والأصناف',
      screens: [
        { id: 8, name: 'دليل وبطاقة الصنف' },
        { id: 18, name: 'الكميات الافتتاحية' },
        { id: 25, name: 'التحويل المخزني' },
        { id: 83, name: 'جرد وتسوية المخزون' },
        { id: 5, name: 'وحدات القياس' },
        { id: 7, name: 'مجموعات الأصناف' },
      ],
    },
    {
      name: 'التقارير',
      screens: [
        { id: 50, name: 'إقرار الضريبة VAT' },
        { id: 41, name: 'الحركات اليومية والصناديق' },
        { id: 60, name: 'كرت حركة صنف' },
        { id: 127, name: 'مبيعات المناديب' },
      ],
    },
    {
      name: 'المراكز والقطاعات',
      screens: [
        { id: 2, name: 'مراكز التكلفة' },
        { id: 98, name: 'العقود والتأجير' },
        { id: 76, name: 'شؤون الموظفين' },
        { id: 58, name: 'شاشة المطاعم' },
        { id: 118, name: 'أوامر التصنيع' },
      ],
    },
  ];

  return (
    <div
      className="flex flex-col h-screen w-screen overflow-hidden bg-[#eef2f6] select-none text-right font-sans"
      onClick={() => setOpenMenu(null)}
    >
      {/* 1. Classic Windows Window Title Bar */}
      <div className="bg-gradient-to-l from-[#005a9e] to-[#0078d7] text-white px-3 py-1.5 flex items-center justify-between text-xs font-semibold select-none shadow-xs border-b border-[#004b85]">
        <div className="flex items-center gap-2">
          <span className="text-sm">🦅</span>
          <span className="tracking-wide">
            الصقر ERP — {user.userName} | نظام الصقر المحاسبي المتكامل V4 (GTS 2026)
          </span>
        </div>

        <div className="flex items-center gap-1">
          <button
            title="تصغير"
            className="w-6 h-5 bg-white/10 hover:bg-white/20 flex items-center justify-center text-white rounded-2xs"
          >
            <Minus className="w-3 h-3" />
          </button>
          <button
            title="تكبير"
            className="w-6 h-5 bg-white/10 hover:bg-white/20 flex items-center justify-center text-white rounded-2xs"
          >
            <Square className="w-2.5 h-2.5" />
          </button>
          <button
            title="إغلاق"
            onClick={() => handleOpenScreen(0)}
            className="w-6 h-5 bg-white/10 hover:bg-red-600 flex items-center justify-center text-white rounded-2xs transition"
          >
            <X className="w-3 h-3" />
          </button>
        </div>
      </div>

      {/* 2. Classic WinForms Menu Strip */}
      <div className="bg-[#f8fafc] border-b border-[#cbd5e1] px-2 py-0.5 flex items-center gap-3 text-xs text-slate-800 relative select-none">
        {/* Menu: ملف */}
        <div className="relative">
          <button
            onClick={(e) => {
              e.stopPropagation();
              setOpenMenu(openMenu === 'file' ? null : 'file');
            }}
            className={`px-2 py-0.5 hover:bg-[#e5f1fb] hover:text-[#005a9e] border border-transparent rounded-xs cursor-pointer transition ${
              openMenu === 'file' ? 'bg-[#e5f1fb] text-[#005a9e] border-[#0078d7]' : ''
            }`}
          >
            ملف
          </button>
          {openMenu === 'file' && (
            <div className="absolute right-0 top-full mt-0.5 bg-white border border-[#cbd5e1] shadow-lg py-1 w-44 z-50 text-xs text-right rounded-xs">
              <button
                onClick={() => handleOpenScreen(0)}
                className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition"
              >
                لوحة التشغيل الرئيسية
              </button>
              <button
                onClick={() => handleOpenScreen(997)}
                className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition"
              >
                إعداد الاتصال
              </button>
              <button
                onClick={() => setShowExportModal(true)}
                className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition flex items-center justify-between font-bold text-[#005a9e]"
              >
                <span>تحميل الكود لرفعه إلى GitHub</span>
                <span>📦</span>
              </button>
              <div className="border-t border-[#cbd5e1] my-1"></div>
              <button
                onClick={() => {
                  erpDb.resetDatabase();
                  window.location.reload();
                }}
                className="w-full px-3 py-1 text-right hover:bg-rose-50 text-rose-600 cursor-pointer flex items-center gap-1 justify-end font-medium transition"
              >
                <span>إعادة ضبط البيانات الأولية</span>
                <RotateCcw className="w-3 h-3" />
              </button>
            </div>
          )}
        </div>

        {/* Menu: المحاسبة */}
        <div className="relative">
          <button
            onClick={(e) => {
              e.stopPropagation();
              setOpenMenu(openMenu === 'acc' ? null : 'acc');
            }}
            className={`px-2 py-0.5 hover:bg-[#e5f1fb] hover:text-[#005a9e] border border-transparent rounded-xs cursor-pointer transition ${
              openMenu === 'acc' ? 'bg-[#e5f1fb] text-[#005a9e] border-[#0078d7]' : ''
            }`}
          >
            المحاسبة
          </button>
          {openMenu === 'acc' && (
            <div className="absolute right-0 top-full mt-0.5 bg-white border border-[#cbd5e1] shadow-lg py-1 w-48 z-50 text-xs text-right rounded-xs">
              <button onClick={() => handleOpenScreen(30)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                دليل الحسابات (AccountTree)
              </button>
              <button onClick={() => handleOpenScreen(36)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                القيود اليومية (DailyRestrictions)
              </button>
              <button onClick={() => handleOpenScreen(43)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                دفتر الأستاذ وكشف الحساب
              </button>
              <button onClick={() => handleOpenScreen(45)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                ميزان المراجعة
              </button>
              <button onClick={() => handleOpenScreen(88)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                الأرباح والخسائر
              </button>
            </div>
          )}
        </div>

        {/* Menu: السندات */}
        <div className="relative">
          <button
            onClick={(e) => {
              e.stopPropagation();
              setOpenMenu(openMenu === 'vouch' ? null : 'vouch');
            }}
            className={`px-2 py-0.5 hover:bg-[#e5f1fb] hover:text-[#005a9e] border border-transparent rounded-xs cursor-pointer transition ${
              openMenu === 'vouch' ? 'bg-[#e5f1fb] text-[#005a9e] border-[#0078d7]' : ''
            }`}
          >
            السندات
          </button>
          {openMenu === 'vouch' && (
            <div className="absolute right-0 top-full mt-0.5 bg-white border border-[#cbd5e1] shadow-lg py-1 w-44 z-50 text-xs text-right rounded-xs">
              <button onClick={() => handleOpenScreen(31)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                سند قبض نقدي / بنكي
              </button>
              <button onClick={() => handleOpenScreen(32)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                سند صرف نقدي / بنكي
              </button>
              <button onClick={() => handleOpenScreen(123)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                إدارة الشيكات
              </button>
            </div>
          )}
        </div>

        {/* Menu: المبيعات */}
        <div className="relative">
          <button
            onClick={(e) => {
              e.stopPropagation();
              setOpenMenu(openMenu === 'sales' ? null : 'sales');
            }}
            className={`px-2 py-0.5 hover:bg-[#e5f1fb] hover:text-[#005a9e] border border-transparent rounded-xs cursor-pointer transition ${
              openMenu === 'sales' ? 'bg-[#e5f1fb] text-[#005a9e] border-[#0078d7]' : ''
            }`}
          >
            المبيعات
          </button>
          {openMenu === 'sales' && (
            <div className="absolute right-0 top-full mt-0.5 bg-white border border-[#cbd5e1] shadow-lg py-1 w-48 z-50 text-xs text-right rounded-xs">
              <button onClick={() => handleOpenScreen(59)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                فاتورة مبيعات وكاشير
              </button>
              <button onClick={() => handleOpenScreen(14)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                مردود مبيعات
              </button>
              <button onClick={() => handleOpenScreen(23)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                بطاقة العميل
              </button>
            </div>
          )}
        </div>

        {/* Menu: المشتريات */}
        <div className="relative">
          <button
            onClick={(e) => {
              e.stopPropagation();
              setOpenMenu(openMenu === 'purch' ? null : 'purch');
            }}
            className={`px-2 py-0.5 hover:bg-[#e5f1fb] hover:text-[#005a9e] border border-transparent rounded-xs cursor-pointer transition ${
              openMenu === 'purch' ? 'bg-[#e5f1fb] text-[#005a9e] border-[#0078d7]' : ''
            }`}
          >
            المشتريات
          </button>
          {openMenu === 'purch' && (
            <div className="absolute right-0 top-full mt-0.5 bg-white border border-[#cbd5e1] shadow-lg py-1 w-48 z-50 text-xs text-right rounded-xs">
              <button onClick={() => handleOpenScreen(15)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                فاتورة مشتريات
              </button>
              <button onClick={() => handleOpenScreen(49)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                مردود مشتريات
              </button>
              <button onClick={() => handleOpenScreen(24)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                بطاقة المورد
              </button>
            </div>
          )}
        </div>

        {/* Menu: المخزون */}
        <div className="relative">
          <button
            onClick={(e) => {
              e.stopPropagation();
              setOpenMenu(openMenu === 'inv' ? null : 'inv');
            }}
            className={`px-2 py-0.5 hover:bg-[#e5f1fb] hover:text-[#005a9e] border border-transparent rounded-xs cursor-pointer transition ${
              openMenu === 'inv' ? 'bg-[#e5f1fb] text-[#005a9e] border-[#0078d7]' : ''
            }`}
          >
            المخزون
          </button>
          {openMenu === 'inv' && (
            <div className="absolute right-0 top-full mt-0.5 bg-white border border-[#cbd5e1] shadow-lg py-1 w-48 z-50 text-xs text-right rounded-xs">
              <button onClick={() => handleOpenScreen(8)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                دليل وبطاقة الصنف
              </button>
              <button onClick={() => handleOpenScreen(18)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                الكميات الافتتاحية
              </button>
              <button onClick={() => handleOpenScreen(25)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                التحويل المخزني
              </button>
              <button onClick={() => handleOpenScreen(83)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                جرد وتسوية المخزون
              </button>
              <button onClick={() => handleOpenScreen(5)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                وحدات القياس
              </button>
              <button onClick={() => handleOpenScreen(7)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                مجموعات الأصناف
              </button>
            </div>
          )}
        </div>

        {/* Menu: التقارير */}
        <div className="relative">
          <button
            onClick={(e) => {
              e.stopPropagation();
              setOpenMenu(openMenu === 'rep' ? null : 'rep');
            }}
            className={`px-2 py-0.5 hover:bg-[#e5f1fb] hover:text-[#005a9e] border border-transparent rounded-xs cursor-pointer transition ${
              openMenu === 'rep' ? 'bg-[#e5f1fb] text-[#005a9e] border-[#0078d7]' : ''
            }`}
          >
            التقارير
          </button>
          {openMenu === 'rep' && (
            <div className="absolute right-0 top-full mt-0.5 bg-white border border-[#cbd5e1] shadow-lg py-1 w-48 z-50 text-xs text-right rounded-xs">
              <button onClick={() => handleOpenScreen(50)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                إقرار الضريبة (VAT)
              </button>
              <button onClick={() => handleOpenScreen(41)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                الحركات اليومية والصناديق
              </button>
              <button onClick={() => handleOpenScreen(60)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                كرت حركة صنف
              </button>
              <button onClick={() => handleOpenScreen(127)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                مبيعات المناديب
              </button>
            </div>
          )}
        </div>

        {/* Menu: الأمان والصلاحيات */}
        <div className="relative">
          <button
            onClick={(e) => {
              e.stopPropagation();
              setOpenMenu(openMenu === 'sec' ? null : 'sec');
            }}
            className={`px-2 py-0.5 hover:bg-[#e5f1fb] hover:text-[#005a9e] border border-transparent rounded-xs cursor-pointer transition ${
              openMenu === 'sec' ? 'bg-[#e5f1fb] text-[#005a9e] border-[#0078d7]' : ''
            }`}
          >
            الأمان والصلاحيات
          </button>
          {openMenu === 'sec' && (
            <div className="absolute right-0 top-full mt-0.5 bg-white border border-[#cbd5e1] shadow-lg py-1 w-52 z-50 text-xs text-right rounded-xs">
              <button onClick={() => handleOpenScreen(1)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                المستخدمون (User_Login)
              </button>
              <button onClick={() => handleOpenScreen(3)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                مجموعات الصلاحيات (User_Groups)
              </button>
              <button onClick={() => handleOpenScreen(104)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                مصفوفة الصلاحيات (Permissions)
              </button>
            </div>
          )}
        </div>

        {/* Menu: أدوات النظام */}
        <div className="relative">
          <button
            onClick={(e) => {
              e.stopPropagation();
              setOpenMenu(openMenu === 'tools' ? null : 'tools');
            }}
            className={`px-2 py-0.5 hover:bg-[#e5f1fb] hover:text-[#005a9e] border border-transparent rounded-xs cursor-pointer transition ${
              openMenu === 'tools' ? 'bg-[#e5f1fb] text-[#005a9e] border-[#0078d7]' : ''
            }`}
          >
            أدوات النظام
          </button>
          {openMenu === 'tools' && (
            <div className="absolute right-0 top-full mt-0.5 bg-white border border-[#cbd5e1] shadow-lg py-1 w-56 z-50 text-xs text-right rounded-xs">
              <button onClick={() => handleOpenScreen(999)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                ربط وتشغيل النظام بالكامل (Full Link)
              </button>
              <button onClick={() => handleOpenScreen(998)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                بدائل الشاشات الأصلية (Replacements)
              </button>
              <button onClick={() => handleOpenScreen(997)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                إعداد الاتصال (ConnectionSettings)
              </button>
              <button onClick={() => handleOpenScreen(996)} className="w-full px-3 py-1 text-right hover:bg-[#005a9e] hover:text-white cursor-pointer transition">
                فحص قاعدة البيانات (SchemaForm)
              </button>
            </div>
          )}
        </div>

        <div className="mr-auto flex items-center gap-2">
          <button
            onClick={() => setShowQuickLauncher(true)}
            className="px-2.5 py-1 bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] border border-[#cbd5e1] text-slate-800 text-[11px] font-semibold cursor-pointer flex items-center gap-1 shadow-2xs rounded-xs transition"
          >
            <Search className="w-3 h-3 text-[#005a9e]" />
            <span>بحث الشاشات (369 شاشة)</span>
          </button>
        </div>
      </div>

      {/* 3. Quick Toolbar Under Menu */}
      <div className="bg-[#f8fafc] border-b border-[#cbd5e1] px-2 py-1 flex items-center gap-1.5 overflow-x-auto text-xs text-slate-800 select-none">
        <button
          onClick={() => handleOpenScreen(0)}
          className="px-2.5 py-1 bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] border border-[#cbd5e1] text-xs font-semibold cursor-pointer shadow-2xs flex items-center gap-1 rounded-xs transition"
        >
          <span>🖥️</span>
          <span>لوحة التشغيل</span>
        </button>

        <button
          onClick={() => handleOpenScreen(59)}
          className="px-2.5 py-1 bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] border border-[#cbd5e1] text-xs font-semibold cursor-pointer shadow-2xs flex items-center gap-1 rounded-xs transition"
        >
          <span>🛒</span>
          <span>فاتورة مبيعات</span>
        </button>

        <button
          onClick={() => handleOpenScreen(15)}
          className="px-2.5 py-1 bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] border border-[#cbd5e1] text-xs font-semibold cursor-pointer shadow-2xs flex items-center gap-1 rounded-xs transition"
        >
          <span>📦</span>
          <span>فاتورة مشتريات</span>
        </button>

        <button
          onClick={() => handleOpenScreen(31)}
          className="px-2.5 py-1 bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] border border-[#cbd5e1] text-xs font-semibold cursor-pointer shadow-2xs flex items-center gap-1 rounded-xs transition"
        >
          <span>💵</span>
          <span>سند قبض</span>
        </button>

        <button
          onClick={() => handleOpenScreen(32)}
          className="px-2.5 py-1 bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] border border-[#cbd5e1] text-xs font-semibold cursor-pointer shadow-2xs flex items-center gap-1 rounded-xs transition"
        >
          <span>💳</span>
          <span>سند صرف</span>
        </button>

        <button
          onClick={() => handleOpenScreen(36)}
          className="px-2.5 py-1 bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] border border-[#cbd5e1] text-xs font-semibold cursor-pointer shadow-2xs flex items-center gap-1 rounded-xs transition"
        >
          <span>📝</span>
          <span>القيود اليومية</span>
        </button>

        <button
          onClick={() => handleOpenScreen(43)}
          className="px-2.5 py-1 bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] border border-[#cbd5e1] text-xs font-semibold cursor-pointer shadow-2xs flex items-center gap-1 rounded-xs transition"
        >
          <span>📖</span>
          <span>كشف الحساب</span>
        </button>

        <button
          onClick={() => handleOpenScreen(30)}
          className="px-2.5 py-1 bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] border border-[#cbd5e1] text-xs font-semibold cursor-pointer shadow-2xs flex items-center gap-1 rounded-xs transition"
        >
          <span>📂</span>
          <span>دليل الحسابات</span>
        </button>

        <button
          onClick={() => handleOpenScreen(8)}
          className="px-2.5 py-1 bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] border border-[#cbd5e1] text-xs font-semibold cursor-pointer shadow-2xs flex items-center gap-1 rounded-xs transition"
        >
          <span>🏷️</span>
          <span>الأصناف</span>
        </button>

        <button
          onClick={() => handleOpenScreen(999)}
          className="px-2.5 py-1 bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] border border-[#cbd5e1] text-xs font-semibold cursor-pointer shadow-2xs flex items-center gap-1 rounded-xs transition"
        >
          <span>⚡</span>
          <span>ربط النظام بالكامل</span>
        </button>

        <button
          onClick={() => setShowExportModal(true)}
          className="mr-auto px-3 py-1 bg-gradient-to-l from-[#005a9e] to-[#0078d7] text-white hover:brightness-110 border border-[#004b85] text-xs font-bold cursor-pointer shadow-xs flex items-center gap-1.5 rounded-xs transition"
        >
          <span>📥</span>
          <span>تحميل كود التصميم كاملاً (ZIP)</span>
        </button>
      </div>

      {/* 4. Main Body: SplitContainer Layout (Matching MainForm Forms.cs) */}
      <div className="flex-1 flex overflow-hidden">
        {/* Right Panel: WinForms Menu FlowLayoutPanel (250px) */}
        <aside className="w-[250px] bg-[#f8fafc] border-l border-[#cbd5e1] flex flex-col overflow-y-auto p-2.5 select-none">
          <div className="text-xs font-bold text-[#005a9e] pb-1.5 mb-2 border-b border-[#cbd5e1] flex items-center justify-between">
            <span>قائمة وحدات الصقر ERP</span>
            <span className="text-[10px] text-slate-500 font-mono">dboGTSdb2026</span>
          </div>

          <div className="flex-1 space-y-3">
            {menuCategories.map((group) => (
              <div key={group.name} className="space-y-1">
                <div
                  className="font-bold text-xs text-slate-800 px-1 pt-1"
                  style={{ fontFamily: 'Segoe UI, sans-serif' }}
                >
                  {group.name}
                </div>
                <div className="space-y-1 pr-1">
                  {group.screens.map((scr) => (
                    <button
                      key={scr.id}
                      onClick={() => handleOpenScreen(scr.id)}
                      className={`w-[225px] h-[34px] px-3 text-right text-xs border cursor-pointer font-medium transition shadow-2xs truncate block rounded-xs ${
                        activeTab.screenId === scr.id
                          ? 'bg-[#005a9e] text-white border-[#004b85] font-bold shadow-xs'
                          : 'bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] active:bg-[#cce4f7] border-[#cbd5e1] text-slate-700'
                      }`}
                      style={{ fontFamily: 'Segoe UI, sans-serif' }}
                      title={scr.name}
                    >
                      {scr.name}
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </div>

          {/* Action Buttons at bottom of menu (matching Forms.cs MainForm AddAction) */}
          <div className="pt-3 mt-3 border-t border-[#cbd5e1] space-y-1.5">
            <button
              onClick={() => handleOpenScreen(1)}
              className="w-[225px] h-[34px] px-3 text-right text-xs bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] active:bg-[#cce4f7] border border-[#cbd5e1] text-slate-800 font-semibold cursor-pointer shadow-2xs block truncate rounded-xs transition"
            >
              المستخدمون والمجموعات والصلاحيات
            </button>
            <button
              onClick={() => handleOpenScreen(997)}
              className="w-[225px] h-[34px] px-3 text-right text-xs bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] active:bg-[#cce4f7] border border-[#cbd5e1] text-slate-800 font-semibold cursor-pointer shadow-2xs block truncate rounded-xs transition"
            >
              إعداد الاتصال
            </button>
            <button
              onClick={() => handleOpenScreen(996)}
              className="w-[225px] h-[34px] px-3 text-right text-xs bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] active:bg-[#cce4f7] border border-[#cbd5e1] text-slate-800 font-semibold cursor-pointer shadow-2xs block truncate rounded-xs transition"
            >
              فحص قاعدة البيانات
            </button>
            <button
              onClick={() => handleOpenScreen(998)}
              className="w-[225px] h-[34px] px-3 text-right text-xs bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] active:bg-[#cce4f7] border border-[#cbd5e1] text-slate-800 font-semibold cursor-pointer shadow-2xs block truncate rounded-xs transition"
            >
              بدائل الشاشات الأصلية
            </button>
            <button
              onClick={() => handleOpenScreen(999)}
              className="w-[225px] h-[34px] px-3 text-right text-xs bg-white hover:bg-[#e5f1fb] hover:text-[#005a9e] hover:border-[#0078d7] active:bg-[#cce4f7] border border-[#cbd5e1] text-slate-800 font-semibold cursor-pointer shadow-2xs block truncate rounded-xs transition"
            >
              ربط وتشغيل النظام بالكامل
            </button>
          </div>
        </aside>

        {/* Left Panel: Content Area with MDI Tabs (Panel 2) */}
        <main className="flex-1 flex flex-col overflow-hidden bg-[#f1f5f9]">
          {/* Classic WinForms Tab Strip */}
          <div className="bg-[#e2e8f0] border-b border-[#cbd5e1] px-2 pt-1.5 flex items-center gap-1 overflow-x-auto select-none">
            {openTabs.map((tab) => {
              const isActive = tab.id === activeTabId;
              return (
                <div
                  key={tab.id}
                  onClick={() => setActiveTabId(tab.id)}
                  className={`flex items-center gap-2 px-3 py-1.5 border-t-2 border-x rounded-t-sm text-xs cursor-pointer select-none transition ${
                    isActive
                      ? 'bg-white border-t-[#005a9e] border-x-[#cbd5e1] font-bold text-[#005a9e] -mb-[1px] z-10 shadow-xs'
                      : 'bg-[#cbd5e1]/60 hover:bg-[#cbd5e1] border-t-transparent border-x-[#94a3b8]/50 text-slate-700'
                  }`}
                >
                  <span className="truncate max-w-[180px]">{tab.title}</span>
                  {openTabs.length > 1 && tab.screenId !== 0 && (
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        handleCloseTab(tab.id);
                      }}
                      className="hover:bg-red-500 hover:text-white rounded-2xs p-0.5 text-slate-500 transition cursor-pointer"
                    >
                      <X className="w-3 h-3" />
                    </button>
                  )}
                </div>
              );
            })}
          </div>

          {/* Form Workspace */}
          <div className="flex-1 overflow-auto p-3 bg-[#f8fafc]">
            {renderActiveScreen()}
          </div>
        </main>
      </div>

      {/* 5. Classic WinForms StatusStrip (Forms.cs StatusStrip) */}
      <footer className="bg-[#f8fafc] border-t border-[#cbd5e1] px-3 py-1 flex items-center justify-between text-xs text-slate-700 select-none">
        <div className="flex items-center gap-3">
          <div className="border border-[#cbd5e1] bg-white px-2.5 py-0.5 text-[11px] shadow-2xs rounded-xs">
            المستخدم: <strong className="text-slate-900">{user.userName}</strong> | المجموعة: <strong className="text-slate-900">{user.groupId}</strong> | الفرع: <strong className="text-slate-900">{user.branchId}</strong>
          </div>
          <div className="border border-[#cbd5e1] bg-white px-2.5 py-0.5 text-[11px] shadow-2xs rounded-xs">
            التبويبات المفتوحة: <strong className="text-[#005a9e]">{openTabs.length}</strong>
          </div>
        </div>

        <div className="border border-[#cbd5e1] bg-white px-2.5 py-0.5 text-[11px] flex items-center gap-1.5 shadow-2xs rounded-xs">
          <span className="w-2 h-2 rounded-full bg-emerald-500 inline-block"></span>
          <span className="font-semibold text-slate-900">dboGTSdb2026 / متصل</span>
        </div>
      </footer>

      {/* Quick Launcher Modal (Catalog of All 369 Screens) */}
      {showQuickLauncher && (
        <div className="fixed inset-0 bg-black/50 backdrop-blur-2xs flex items-center justify-center z-50 p-4">
          <div className="bg-white border border-[#cbd5e1] rounded-sm shadow-2xl max-w-2xl w-full max-h-[80vh] flex flex-col overflow-hidden text-right">
            <div className="px-3.5 py-2 bg-gradient-to-l from-[#005a9e] to-[#0078d7] text-white flex justify-between items-center text-xs font-bold border-b border-[#004b85]">
              <div className="flex items-center gap-2">
                <span>⚡</span>
                <span>فهرس الشاشات السريع (User_Screens) — فتح أي شاشة بالرقم أو الاسم</span>
              </div>
              <button
                onClick={() => setShowQuickLauncher(false)}
                className="hover:bg-red-600 px-2 py-0.5 text-white font-bold rounded-2xs cursor-pointer transition text-xs"
              >
                ✕
              </button>
            </div>

            <div className="p-3 border-b border-[#cbd5e1] bg-[#f8fafc]">
              <div className="relative">
                <input
                  type="text"
                  autoFocus
                  placeholder="ابحث باسم الشاشة أو رقمها أو القسم..."
                  value={launcherSearch}
                  onChange={(e) => setLauncherSearch(e.target.value)}
                  className="w-full bg-white border border-[#cbd5e1] rounded-xs pr-8 pl-3 py-1.5 text-xs focus:outline-none focus:border-[#0078d7] text-slate-900"
                />
                <Search className="w-4 h-4 text-slate-400 absolute right-2.5 top-2" />
              </div>
            </div>

            <div className="p-2 overflow-y-auto divide-y divide-[#cbd5e1]/40 text-xs bg-white flex-1">
              {filteredLauncherScreens.map((screen) => (
                <div
                  key={screen.id}
                  onClick={() => handleOpenScreen(screen.id)}
                  className="p-2.5 flex items-center justify-between hover:bg-[#e5f1fb] cursor-pointer transition rounded-xs"
                >
                  <div>
                    <div className="font-bold text-slate-900 flex items-center gap-2">
                      <span>{screen.name}</span>
                      <span className="font-mono text-[#005a9e] text-[11px] bg-[#e5f1fb] px-1.5 py-0.2 rounded-xs border border-[#cbd5e1]">#{screen.screenNum}</span>
                    </div>
                    <div className="text-[11px] text-slate-500 mt-0.5">
                      القسم: {screen.category} {screen.legacyName ? `| ${screen.legacyName}` : ''}
                    </div>
                  </div>
                  <button className="px-2.5 py-1 bg-white hover:bg-[#005a9e] hover:text-white border border-[#cbd5e1] text-slate-800 text-[11px] font-semibold flex items-center gap-1 cursor-pointer rounded-xs transition shadow-2xs">
                    <ExternalLink className="w-3 h-3" />
                    <span>فتح الشاشة</span>
                  </button>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}
      {/* Export / Download Project Modal */}
      {showExportModal && <ExportProjectModal onClose={() => setShowExportModal(false)} />}
    </div>
  );
}
