# Phase 2 — What Was Built

**Status: Phase 2 complete (code only — first compile happens on the Windows PC, see README).**

Phase 2 adds buying, suppliers, returns & exchange, reports and bulk price
updates on top of the Phase 1 app. Same rules as Phase 1: money is always
integer paisa, stock only changes through `StockService` / `stock_movements`,
every multi-table save runs in ONE SQLite transaction, and every new word on
screen exists in BOTH `Strings.en.xaml` and `Strings.ur.xaml` (252 keys each,
checked). **No schema change was needed** — Phase 1's schema already had
`suppliers`, `purchases`, `purchase_items`, `returns`, `return_items` and a
general `payments` table.

---

## 1. Purchase / GRN (خریداری)

**File map:** `Services/PurchaseService.cs`, `ViewModels/PurchaseViewModel.cs`, `Views/PurchaseView.xaml`.

- Pick a supplier from the list, or type a **new supplier name** and press
  *Add Supplier* — it is saved and selected immediately.
- Supplier bill number, purchase date (defaults to today).
- Product search / barcode box works like Billing: type, press Enter (or F2),
  the product is added. Each line: quantity + **purchase rate** (prefilled
  with the product's saved purchase rate, editable).
- Bill-level discount (Rs), **Paid Now (Rs)** amount + payment method
  (Cash / Card / Bank / JazzCash / EasyPaisa).
- Save runs in ONE transaction: `purchases` + `purchase_items` rows, stock
  in via movement `Purchase`, and a payment row for the paid-now amount.
- **Behaviour to know:** if a line's purchase rate is different from the
  product's saved purchase rate, the product's purchase rate is **updated to
  the latest GRN rate**. This keeps Reports / Dashboard cost figures current,
  but it also means old bills' profit is re-valued at today's rate (same
  "estimated" logic as Phase 1).
- Right side: purchase history (supplier, date) — select one to see its lines
  and the balance payable left on that bill.

## 2. Suppliers (سپلائر)

**File map:** `Services/SupplierService.cs`, `ViewModels/SuppliersViewModel.cs`, `Views/SuppliersView.xaml`.

- List of suppliers with their **payable balance**; a header shows the
  **Total Payable** across all suppliers.
- Add / edit: name, contact person, mobile, address, city, **opening
  balance** (old payable from before this software).
- One payable formula everywhere (in `SupplierService`):
  `opening balance + all purchase totals − all payments given`.
- Supplier ledger (khata): every purchase and every payment, oldest first,
  with a running payable balance.
- **Pay Supplier** writes a `payments` row with `party_type='Supplier'`,
  `direction='OUT'` and the chosen method (Cash/Card/Bank/JazzCash/EasyPaisa).
  The balance drops immediately in the list, the ledger and Reports.
- Note: a GRN's "paid now" amount is stored on the purchase AND written as a
  payment row in the same transaction (counted once — through payments).

## 3. Returns & Exchange (واپسی اور تبدیلی)

**File map:** `Services/ReturnService.cs`, `ViewModels/ReturnsViewModel.cs`, `Views/ReturnsView.xaml`.

- Type the **bill number** and press *Load Bill*: the original sale loads
  with Sold qty, **Already Returned** qty (from returns history) and customer.
- Per line: return qty (cannot be more than Sold − Already Returned — checked
  on screen AND again inside the save transaction) and condition:
  **Good / Damaged**.
- Refund rate per unit = what the customer actually paid for that line
  (line total ÷ qty, i.e. after line discount). A bill-level discount on the
  original bill is NOT split across lines — the refund total can differ
  slightly from a hand calculation; this is deliberate and simple.
- Settle by: **Cash** (money handed back), **Khata** (reduces the customer's
  balance — written as a normal payment-received row marked "Return
  adjustment", so the khata formula stays the single source of truth), or
  **Exchange**.
- Save runs in ONE transaction: `returns` + `return_items`, stock movement
  `SaleReturn` — **Good items go back to sellable stock; Damaged items go to
  damaged qty only** (they cannot be sold, same as Phase 1 damage handling).
- **Exchange, kept simple on purpose:** after saving, the app opens the
  Billing screen with the same customer selected and a green message showing
  the exchange credit, e.g. *"Exchange credit Rs 1,500 from the return of
  Bill #12…"*. Ring up the new items and settle the credit in Paid / Discount
  on the new bill. There is no separate exchange engine.
- Returns history list on the same screen.

## 4. Reports (رپورٹیں)

**File map:** `Services/ReportService.cs`, `ViewModels/ReportsViewModel.cs`, `Views/ReportsView.xaml`, `Helpers/CsvExport.cs`, `Printing/PrintServices.cs` (`ReportPrintService`).

- Report picker + From/To dates (default: today) with presets
  **Today / This Week / This Month**.
- Reports:
  1. **Sales** — date-wise bills + sales + discount + tax, plus a
     "by payment method" block (Cash/Card/Bank/JazzCash/EasyPaisa/Credit).
  2. **Profit (Estimated)** — sales minus cost, where cost = each sold line
     valued at the product's **current** purchase rate (same formula as the
     Phase 1 dashboard; line totals already include line discounts; the
     bill-level discount and tax are not in it). It is labelled
     تخمینی منافع / Estimated Profit everywhere. **Admin only:** the report
     is not even offered to a Cashier, and running it as Cashier is blocked
     in code too.
  3. **Brand-wise sales** — qty + amount per brand.
  4. **Category-wise sales** — qty + amount per category.
  5. **Udhaar Receivable list** — customers with balance > 0, their credit
     limit, and an **Over Limit** flag.
  6. **Supplier Payable list** — suppliers with balance > 0 (same formula as
     the Suppliers screen).
  7. **Stock Value** — per product: qty × purchase rate (stock purchase
     value) and qty × retail rate (stock sale value), with totals.
- Every report: on-screen grid, **Print** (A4, same bilingual shop header as
  quotations, printed via the existing FlowDocument approach — choosing a
  PDF printer gives a PDF), and **Export Excel (CSV)** — an honest CSV file
  (UTF-8 with BOM, so Excel shows the Urdu text correctly) saved through a
  Save File dialog. No heavy Excel library was added.

## 5. Bulk Price Update (ایک ساتھ قیمت تبدیلی)

On the **Products** screen, under the editor (Admin only, hidden for Cashier):

- Choose a **Brand**, choose the price field (Retail / Trade / Wholesale /
  Purchase), type a **% change** (e.g. `5` or `-5`).
- The screen shows how many products will be affected **before** you apply.
- *Apply* asks for confirmation in the current language
  ("Change Retail prices for brand X by 5%? This will update N products."),
  then updates all of them in ONE transaction, rounding to whole rupees.

## 6. Phase 1 gaps closed

- **Reprint Last Bill (آخری بل دوبارہ پرنٹ کریں)** button on Billing —
  reprints the most recent completed sale's thermal receipt using
  `SaleService.GetLastCompletedSale()`.
- **Edit an Open quotation:** select a saved quotation that is still *Open*
  and press *Edit Quotation* — it loads back into the editor (same number,
  same date) and Save **updates** it instead of creating a duplicate.
  Converted quotations cannot be edited (the button disables, like Convert).

---

## Counter Test Checklist — Phase 2

Do these on the shop PC after the first build:

**Purchase**
- [ ] Make a purchase of 10 pcs of a product → product stock goes up by 10.
- [ ] Enter a purchase rate different from the product's saved rate → the
      product's purchase rate now shows the new rate.
- [ ] Buy with Paid Now = 0 → supplier payable goes up by the full total.
- [ ] Buy with part payment → payable goes up by total − paid only.
- [ ] Purchase history shows the bill; selecting it shows its lines.

**Suppliers**
- [ ] Add a supplier with an opening balance → payable shows that amount.
- [ ] Pay Supplier Rs 1,000 → payable drops by 1,000 in the list, the ledger
      and the Supplier Payable report; ledger shows the payment row.

**Returns**
- [ ] Sell 3 pcs on a bill, return 1 **Good** → sellable stock back up by 1;
      returning more than the remaining 2 is refused.
- [ ] Return 1 **Damaged** → damaged qty up by 1, sellable stock unchanged.
- [ ] Return with **Khata** settle on a credit customer's bill → customer
      khata balance drops by the refund amount.
- [ ] Return with **Exchange** settle → Billing opens with the customer
      selected and the credit message showing the amount and bill number.

**Reports**
- [ ] Sales report for today matches the bills made today; the payment-method
      block adds up to the same total.
- [ ] Logged in as **Cashier**, the Profit report is NOT in the report list.
- [ ] Brand / Category reports: quantities match the bills of the range.
- [ ] Receivable list shows only customers who owe money; an over-limit
      customer is flagged.
- [ ] Stock Value totals = sum of qty × rates shown per product.
- [ ] Print a report → A4 page with shop name in English + Urdu.
- [ ] Export Excel (CSV) → file opens in Excel with Urdu readable.

**Bulk price update**
- [ ] As Admin: +10% on one brand's Retail → only that brand's retail prices
      change; the confirmation showed the right product count first.
- [ ] As Cashier: the bulk panel is not visible.

**Phase 1 gap fixes**
- [ ] Complete a bill, press **Reprint Last Bill** → same receipt prints again.
- [ ] Save a quotation, Edit it, change a qty, Save → same quotation number,
      updated amounts, no duplicate in the list.
- [ ] Convert a quotation → Edit button is disabled for it.

**Language**
- [ ] Switch to اردو on every new screen: Purchase, Suppliers, Returns,
      Reports — all labels change, layout flips to RTL, product/brand names
      stay in English.

---

## Deferred to Phase 3 (unchanged from the plan)

Users & Roles management screen and role hardening, scheduled auto-backup,
day-end closing report, installer, and FBR POS integration (the tax field is
already on every bill, ready for it).
