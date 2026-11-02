# Sanitary POS — Full Plan (Step 1, as requested)
Bilingual Urdu + English | Windows Desktop | Offline | Pakistan Market

## A. Architecture — simple, as requested
- App: C# .NET 8, WPF, MVVM (CommunityToolkit.Mvvm — free, standard)
- Database: SQLite, ONE file: `sanitarypos.db` next to the app / in AppData. No server needed.
- Data access: Dapper (simple, fast) + raw SQL. NO heavy ORM.
- Language system: Two ResourceDictionaries — `Strings.en.xaml` and `Strings.ur.xaml`.
  Toggle swaps the dictionary + sets FlowDirection (LTR for English, RTL for Urdu) on the main window. No restart needed.
  Urdu font: Noto Nastaliq Urdu (bundled with app, free license). English font: Segoe UI.
- Money: stored as INTEGER paisa? No — use DECIMAL in DB, display via a MoneyFormatter:
  `Rs 1,25,000` (en-PK grouping: lakh/crore style), 0 decimal places by default.
- Printing:
  - Receipt (bill): 80mm thermal — WPF FlowDocument / raw ESC-POS for cash drawer kick.
  - Quotation + detailed invoice: A4 FlowDocument → Print / Save as PDF (for WhatsApp sharing).
  - All print templates are bilingual: shop name EN + UR on top, column headings EN/اردو.
- Barcode scanner: works as keyboard (HID) — billing screen keeps focus in the barcode box, Enter adds the item. No special driver code.
- Backup: on app close + daily at a set time, copy `sanitarypos.db` to `Backup/yyyy-MM-dd.db` and optionally a USB/Google Drive folder. One-click Backup / Restore in Settings.
- Installer (Phase 3): Inno Setup (free) → single Setup.exe, creates desktop shortcut, bundles the Urdu font.

## B. Screens list (14 screens)
Login (PIN/Password)
1. Dashboard — today's sale/profit/cash, udhaar receivable, low-stock count
2. POS Billing — THE main screen. Left: search/barcode + product grid. Right: bill items, totals, payment. Fits 1366x768, no scroll.
3. Held Bills popup — hold / recall
4. Quotation — list + editor (same editor as billing, with "Save as Quotation / کوٹیشن")
5. Products / Inventory — list, add/edit form, quick price update, bulk brand-wise price update
6. Stock Report — current stock, low stock, stock value
7. Purchase (GRN) — supplier bill entry [Phase 2]
8. Customers & Khata — customer list, ledger detail, receive payment (وصولی), statement print/WhatsApp
9. Suppliers — list + payable ledger [Phase 2]
10. Returns & Exchange — by bill number [Phase 2]
11. Reports — sales, profit, brand/category, udhaar, payable [Phase 2, basic sales report in Phase 1]
12. Users — Admin/Cashier management [Phase 3]
13. Settings — shop info EN/UR, printers, tax, language, backup
14. Day-End / Cash Closing — cash count vs system cash, Z-report

## C. Database tables (18) — full SQL is in /database/schema.sql
users, settings,
brands, categories, units,
products, stock (per product + location), stock_movements (every in/out with reason),
customers, suppliers,
sales (bill head), sale_items,
quotations, quotation_items,
purchases, purchase_items,           [Phase 2]
payments (customer receipts + supplier payments — one table, direction flag),
returns, return_items,                [Phase 2]
cancelled_bills (audit log with reason)
Key rules in the schema:
- Every sale records: price level used (Retail/Trade/Wholesale), payment type, tax amount (FBR-ready field), sold-by user.
- Stock is NEVER edited directly — it only changes through stock_movements (sale, purchase, return, damage, adjustment). This keeps stock trustworthy.
- Soft-delete only: products/bills are marked inactive/cancelled, never physically deleted.

## D. Roles
- Admin: everything, including purchase rates, profit, price changes, cancellations, backup/restore, users.
- Cashier: billing, quotation (no profit shown), returns request, customer khata VIEW + receive payment.
  Cashier CANNOT see: purchase rate, profit reports, delete/cancel (Admin PIN required for cancel).

## E. Build order (phases, exactly as you set)
Phase 1 — MVP: Login (simple), Dashboard (basic), Products/Inventory, POS Billing + thermal print,
  Quotation + A4 print/PDF, Customers & Khata + receive payment, Settings (shop + printer + language),
  EN/UR language engine working on ALL Phase 1 screens.
  Test at the counter for 1 week before Phase 2.
Phase 2: Purchase/GRN, Suppliers + payables, Returns & Exchange (incl. damaged stock),
  Full Reports, Excel/PDF export, bulk price update.
Phase 3: Users & Roles hardening, Auto-backup, Day-End closing, Installer (Setup.exe),
  Optional: FBR POS integration (tax field already in schema).

## F. Assumptions I made (tell me if any is wrong — I did not remove any feature)
1. Single shop, single counter PC to start (database is local). Multi-branch/cloud is NOT in this plan.
2. One currency only: PKR. One tax rate setting (on/off + %), not per-product tax.
3. Shop name/address for printouts will be entered in Settings by you — I used placeholders.
4. Urdu translations use your glossary; any word you want changed can be edited in ONE file: Strings.ur.xaml.

## G. Honest limitation from my side
I work on a Linux machine and WPF only builds/runs on Windows, so I can write ALL the code, schema and files here, but the final compile + installer must be run once on a Windows PC with Visual Studio 2022 (free Community edition) — `dotnet build` / open the .sln and press F5. I will give exact steps for that after each phase.
