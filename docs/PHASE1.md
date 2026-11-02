# Phase 1 — What Was Built (simple English)

Project: **SanitaryPOS** — bilingual (Urdu + English) desktop POS for a sanitary store in Pakistan.
Stack: C# .NET 8, WPF, MVVM (CommunityToolkit.Mvvm), SQLite (Microsoft.Data.Sqlite + Dapper).
Solution: `src/SanitaryPOS.sln` → project `src/SanitaryPOS/SanitaryPOS.csproj` (net8.0-windows, UseWPF).

Demo data decision: **NO demo products/customers are seeded.** The database starts
clean except: brands are created by you in the product form, units + categories +
default settings come from schema.sql, and ONE admin login (below). Add your real
products first — testing is easier with your own rates.

## First login — CHANGE THIS
- Username: **admin**  Password: **admin123**
- This is seeded only so you can log in the first time. It is a SHA-256 hash in the
  DB, but the password itself is public (it is written here) — **ask your developer
  to change it before live use.** The Users/change-password screen is Phase 3.

## Screen by screen

**Login** — username + password against the `users` table. Wrong login shows a
translated error. After login the sidebar appears.

**Shell / Main Window** — green header with shop name (English • Urdu, from
Settings), the logged-in user, an **EN | اردو toggle** and Logout. The toggle swaps
the language dictionary instantly on every screen and flips the whole layout
LTR ↔ RTL, Urdu uses the Noto Nastaliq Urdu font (install the free font on the PC;
Segoe UI is the fallback). Sidebar: Dashboard, Billing, Quotation, Products,
Customers, Settings — plus greyed Phase 2 (Purchase, Suppliers, Returns, Reports)
and Phase 3 (Users) entries that open a "coming in next phase" placeholder.

**Dashboard** — Today's Sales (Rs), Today's Bills count, Total Udhaar Receivable
(all customers), Low Stock item count, and Today's Profit (Admin only; labelled
approx. because profit uses each product's *current* purchase rate, and it is
calculated after line discounts, before bill discount).

**Products / Stock** — searchable list (name, brand, model, barcode), showing
stock qty, purchase rate (Admin only — column and form field are hidden for
Cashier) and retail rate, with a one-box Quick Price Update. Add/Edit form has
every field from the requirements: name (English), brand (select or type a new
brand name to create it), category, model/code, barcode, finish, size, unit,
purchase / retail / trade / wholesale rates (typed in Rupees, stored in paisa),
low-stock level, location (Shop/Godown), active flag, and Opening Stock qty —
which is only used when creating a product and is written as an "Opening" stock
movement, never as a silent stock edit.

**Billing (POS)** — built to fit 1366×768 without scrolling. Focus starts in the
search box: a barcode scanner (which types + sends Enter) adds the item
immediately; typing a name shows up to 8 matches (double-click or Enter when
only one matches adds it). Each line: qty, rate, discount as % or Rs, line total,
remove button. Changing the Price Level (Retail/Trade/Wholesale) re-rates all
lines. Customer selector (walk-in by default), payment method (Cash, Card, Bank,
JazzCash, EasyPaisa, Credit, Mixed). Paid box: blank = full payment (or 0 if the
method is Credit). Overpay shows Change; underpay shows Balance to Khata.
**Rules enforced:** a part-paid or Credit bill cannot be saved for a walk-in —
a saved customer is required, because the unpaid part becomes that customer's
khata balance automatically. Save, Save+Print (F9), New Bill (F2), Hold Bill
(parks it with NO stock cut; recalling a held bill deletes the parked record so
stock is cut exactly once, at the real sale).

**Quotation** — top: editor for a new quotation (same line editor, plus saved
customer OR guest name/mobile, and a bill discount). Bottom: saved quotations
list with Print (A4) and **Convert to Bill** (one click: creates a completed
Cash/paid-in-full sale, cuts stock, marks the quotation Converted). Saved
quotations are not edited in Phase 1 — make a fresh one.

**Customers / Khata** — customer list with live balance, add/edit form (type
WalkIn/Regular/Plumber/Contractor, mobile, address, city, credit limit, opening
balance for old udhaar), a red warning when a customer is over the credit limit,
the full khata ledger (opening balance, each unpaid bill share as Debit, each
payment as Credit, running balance) and **Receive Payment / وصولی** (amount,
method, note) which writes to the payments table and updates the balance
everywhere. Balance rule, one formula everywhere:
`opening + Σ(completed bills: total − paid at bill time) − Σ(payments received)`.

**Settings** — shop name EN + UR, address, phone, WhatsApp, tax on/off + tax %
(tax is calculated on the bill after discounts and stored separately on every
bill — FBR-ready field), default language on start, receipt printer name, A4
printer name, backup folder + **Backup Now** (copies sanitarypos.db with a dated
name). Leave printer names empty to get the Windows print dialog each time.

**Printing** — Receipt: 80mm bilingual FlowDocument (shop name EN+UR, address,
phone/WhatsApp, bill no, date, items with English names, totals, paid, balance /
change, thank-you in both languages). Quotation: A4 bilingual document with the
line **"Rates valid for 7 days / یہ نرخ 7 دن کے لیے ہیں"** (days come from the
`quotation_valid_days` setting). **PDF for WhatsApp in Phase 1 = in the print
dialog choose "Microsoft Print to PDF"** — no extra library is used.

## How the database works
- File: `%AppData%\SanitaryPOS\sanitarypos.db` (one file — copy it = full backup).
- It is auto-created on first run from `database/schema.sql`, embedded in the app
  as a resource, so there are no setup steps and the seeds never run twice.
- Money is integer **paisa** in the DB; the screen always shows Rupees with
  Pakistani grouping (Rs 1,25,000) via `Helpers/MoneyFormatter`.
- Stock only ever changes through `StockService.ApplyMovement`, which writes a
  `stock_movements` row and updates `stock` in the same transaction as the sale.

## Run it on Windows (exact steps)
1. Windows 10/11 PC. Install **Visual Studio 2022 Community** (free) from
   Microsoft, and in the installer tick the workload **".NET desktop development"**.
2. (Recommended) Install the free font **Noto Nastaliq Urdu** (Google Fonts) so
   Urdu looks right; without it the app still works with a fallback font.
3. Copy the whole `sanitary-pos` folder to the PC.
4. Open `src/SanitaryPOS.sln` in Visual Studio. Wait for NuGet restore
   (CommunityToolkit.Mvvm, Dapper, Microsoft.Data.Sqlite — internet needed once).
5. Press **F5**. The database is created automatically.
6. Login with **admin / admin123**. Go to Settings first: enter shop name
   (EN + UR), address, phone, WhatsApp; choose your default language; save.
7. To make the .exe later: Build → Publish, or ask your developer — the Phase 3
   installer (Inno Setup) is not part of Phase 1.

## Counter test checklist (do all of these before live use)
- [ ] Login as admin; wrong password shows the error in both languages.
- [ ] Toggle EN → اردو on **every** screen: all labels change, layout flips RTL,
      numbers and product names stay readable.
- [ ] Settings: save shop details, reopen Settings — values are still there.
      Close and restart the app — shop name shows in the header.
- [ ] Products: add a product in Urdu mode (e.g. "Master Basin Mixer, Chrome")
      with opening stock 10 and all four rates. It appears in the list with stock 10.
- [ ] Products: add a second product with a barcode you can scan/type.
- [ ] Billing: scan/type the barcode + Enter → item added. Add the second
      product by typing its name (double-click the match).
- [ ] Billing: change qty to 3, give one line a 10% discount and one line a
      Rs 200 discount — line totals and Grand Total must match your calculator.
- [ ] Billing: switch Price Level to Trade → line rates change to trade rates.
- [ ] Billing: Cash bill, Paid left blank → saved as fully paid. Check Products:
      stock went down by the sold qty. Check Dashboard: sales + bill count moved.
- [ ] Billing: Udhaar bill — pick a saved customer, method Credit → saved.
      Check Customers: that customer's balance increased by the bill total.
- [ ] Billing: try a Credit bill with Walk-in selected → it must refuse with a message.
- [ ] Billing: part payment — paid half in Cash → remaining half shows as
      Balance to Khata and appears in the customer's balance.
- [ ] Hold a bill, start another bill, then Held Bills → Recall the first one:
      lines come back; save it; stock is cut once only.
- [ ] F2 starts a fresh bill, F9 saves + prints.
- [ ] Print a receipt: shop name EN + UR, items, totals, paid/balance correct.
- [ ] Quotation: make one for a guest (name + mobile), Save, Print (A4):
      check the "Rates valid for 7 days / یہ نرخ 7 دن کے لیے ہیں" line.
- [ ] Quotation: Convert to Bill → quotation shows Converted, stock is cut,
      Dashboard sales increased. Converting again is impossible.
- [ ] Customers: Receive Payment (وصولی) Rs 1,000 from the udhaar customer →
      balance drops by Rs 1,000 and the khata shows the وصولی row.
- [ ] Customers: set a small credit limit, push the balance over it → red
      warning appears on the Customers screen.
- [ ] Settings: Backup Now to a USB/folder → a dated .db file appears there.
- [ ] Create a Cashier user later (Phase 3) — for now, note that purchase-rate
      hiding is wired to the Admin/Cashier role and takes effect when a Cashier
      login exists.
- [ ] Restart the app: all bills, stock and balances are still there (offline test:
      unplug the internet — everything above must still work).

## Deliberately NOT in Phase 1 (Phase 2/3, tables already exist)
Purchase/GRN + supplier payables, Returns & Exchange + damaged stock list,
full Reports + Excel/PDF export, bulk brand-wise price update, Users management
+ change password, auto-backup on a schedule, day-end closing, installer,
FBR integration, WhatsApp direct-send (Phase 1 uses Print to PDF + manual share),
customer statement print, per-bill reprint button (reprint exists only via
Save+Print at billing time — the data/API `SaleService.GetLastCompletedSale`
is ready for a button in Phase 2).
