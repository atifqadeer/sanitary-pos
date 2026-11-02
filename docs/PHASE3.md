# Phase 3 — What Was Built (final planned phase)

**Status: Phase 3 complete (code only — still never compiled; first build is
on the Windows PC, see README).**

Phase 3 adds users & roles, the default-password safety banner, scheduled
auto-backup, day-end closing with Z-report, completed-bill cancellation, and
the Windows installer. Same rules as Phases 1–2: money is integer paisa,
stock only changes through `StockService`, multi-table saves run in ONE
transaction, and every new word exists in BOTH dictionaries (**310 keys
each**, parity-checked; 58 keys added in Phase 3).

---

## 0. Database change — one small migration, old databases keep working

Phase 3 is the first phase that needed a new table. Rather than breaking
existing Phase 1/2 databases, `DatabaseService.Initialize()` now runs
`ApplyMigrations()` on **every startup**:

- `CREATE TABLE IF NOT EXISTS day_closings` (id, closing_date TEXT UNIQUE in
  `'yyyy-MM-dd'`, expected_paisa, counted_paisa, difference_paisa, note,
  user_id, created_at). The same table was also appended to
  `database/schema.sql`, so fresh installs get it from the schema directly
  and the migration is a no-op for them.
- `INSERT OR IGNORE` defaults for the new settings keys:
  `admin_default_password`, `backup_auto_enabled` (0), `backup_time`
  ("21:00"), `backup_keep_count` (30), `last_backup_date`, `last_backup_at`.
- The `admin_default_password` flag is then **recomputed honestly** on every
  startup: it is `1` exactly while the seeded `admin` account still verifies
  against `admin123`. (A Phase 1/2 database where the owner already changed
  the password therefore gets flag `0` — no false warning.)

No data is ever altered by the migration, and running it twice is harmless.

---

## 1. Users & Roles (صارفین) — Admin only

**File map:** `Services/UserService.cs`, `ViewModels/UsersViewModel.cs`,
`Views/UsersView.xaml` (+ `.xaml.cs`).

- **List**: username, full name, role (Admin/Cashier), active checkbox.
- **Add user**: username + password + full name + role. Duplicate usernames
  are refused with a bilingual message.
- **Edit**: change full name / role of the selected user. Demoting the
  **last active Admin** is refused in code.
- **Activate / Deactivate**: you cannot deactivate **yourself**, and you
  cannot deactivate the **last active Admin** — both guards live in
  `UserService` and throw localization keys, so the message shows in the
  current language. Deactivated users can no longer log in (Phase 1's login
  already checked `is_active`).
- **Reset password** (Admin): type a new password for any user; it is hashed
  with the existing `PasswordHasher` (SHA-256), never stored in plain text.
- **Change MY password** (everyone): current password verified first, new
  password must be ≥ 4 characters and match its confirmation. This section
  exists here AND on the Settings screen, because a Cashier never sees the
  Users screen.

### Default-password warning banner

While the seeded admin still uses `admin123`, a red bilingual banner sits
under the top bar in `MainWindow` (all screens, both languages) telling the
user to change it. It disappears the moment any password change makes the
flag `0` — `UserService` refreshes the flag on every password change/reset
and `MainViewModel.RefreshPasswordWarningPublic()` re-reads it, so no
restart is needed. A brand-new user created as Admin with a weak password
does NOT trigger it — the flag is only about the seeded account.

## 2. Role-permission checklist (re-audited for Phase 3)

| Rule | Where enforced | Status |
|---|---|---|
| Users screen: Admin only | Sidebar button hidden for Cashier (`MainWindow.xaml`); `Navigate("Users")` returns an "Admin only" placeholder for Cashier; every Users command re-checks `IsAdmin` | ✅ |
| Change own password: everyone | Section on Users screen (Admin) + section on Settings screen (both roles) | ✅ |
| Profit on Dashboard: Admin only | `ShowProfit` card visibility (Phase 1) | ✅ unchanged |
| Profit report: Admin only | `ReportsViewModel` guard + nav-level role flag (Phase 2) | ✅ unchanged |
| Purchase rates in Products: Admin only | Grid column + form panel hidden for Cashier in `ProductsView` code-behind (Phase 1); bulk brand price update Admin-only (Phase 2) | ✅ unchanged |
| Settings backup/restore + auto-backup: Admin only | Whole backup panel collapsed for Cashier in `SettingsView` code-behind (`x:Name="BackupPanel"`); shop info stays visible | ✅ new |
| Cancel a completed bill: Admin only + reason | Cancel panel hidden for Cashier on Returns screen; `CancelBill` command re-checks `IsAdmin`; reason compulsory | ✅ new |
| Close the day: Admin only | Count/close panel hidden unless Admin; `CloseDay` command re-checks | ✅ new |
| Day-End viewing: both roles | Screen is in the sidebar for everyone (shared shop till — see §4) | ✅ by design |

This is a shop-counter rule set, not a security boundary — the same honesty
as Phase 1: the data sits in one local SQLite file.

## 3. Bill cancellation

Checked first: **no cancel existed in Phase 1/2** (billing could only hold
or complete a bill; `DeleteHeldSale` only accepts `status='Held'`).

**Decision: completed-bill cancellation IS implemented** — the existing
structure allowed it cleanly:

- **Where**: the Returns & Exchange screen (Admin-only red panel). It
  already loads a bill by number, and Billing stays dense and scroll-free
  as designed. Flow: load bill # → type the reason → *Cancel Bill* →
  bilingual Yes/No confirmation → done.
- **What happens** (`SaleService.CancelSale`, ONE transaction):
  - `sales.status` → `'Cancelled'`, `cancel_reason` saved. The bill is
    never deleted — full audit trail, as PLAN requires.
  - Every item's stock returns through `StockService` as a `SaleReturn`
    movement (+qty), note carries the bill number and reason.
  - The customer's khata fixes itself: balances only count
    `status='Completed'` bills, so the cancelled bill's unpaid share simply
    stops counting.
- **Refusals** (bilingual): bill not Completed; bill **already has returns**
  (cancelling would restore stock/refund a second time).
- **Documented edge**: payments already received against that bill (khata
  receipts naming the bill) are NOT reversed — they stay as credit on the
  customer's khata for the owner to settle by hand.
- Cancelled bills can no longer be loaded on the Returns screen (its loader
  only accepts Completed bills).
- **Held bills**: the Billing held-bills list gained a *Remove Held Bill*
  button — held bills never touched stock/khata, so `DeleteHeldSale` is the
  whole story, no cancel record needed.

## 4. Day-End Closing (دن کا اختتام)

**File map:** `Models/DayEndModels.cs`, `Services/DayEndService.cs`,
`ViewModels/DayEndViewModel.cs`, `Views/DayEndView.xaml` (code-behind stays
in `Views/SimpleViews.cs`).

**Role choice (documented per spec):** ANY logged-in user can VIEW the day
summary; only ADMIN can close the day. Reason: this app has one shared shop
till, not per-user tills, so a "view own day" split has nothing to attach
to — the useful rule is who may sign the closing off. The count/close panel
is hidden for Cashier and the command re-checks the role.

**Screen**: pick a date (default today) →

- total sales, bill count, **sales by payment method** (bills, total, paid),
- payments received from customers (IN), payments given to suppliers (OUT),
- returns total,
- the cash build-up rows and **Expected Cash**,
- counted cash entry + note → live **Difference** (counted − expected),
- **Close Day** button, **Print Z Report** (A4 via the Phase 2
  `ReportPrintService`; choose a PDF printer in the dialog for a PDF),
- closings history (date, expected, counted, difference, closed by, note).

Closing an already-closed date shows the saved record ("already closed")
instead of the form. Closing locks nothing — past bills stay viewable;
editing past bills was never a feature.

### The Expected Cash formula (read before trusting the number)

```
Expected Cash =
    paid-at-bill-time on COMPLETED bills with payment_method = 'Cash'
  + customer receipts: payments IN with method 'Cash'
        EXCLUDING rows whose note starts with 'Return adjustment'
        (Phase 2 bookkeeping for khata-settled returns — no cash moved)
  − supplier payments: payments OUT with method 'Cash'
        (includes purchase paid-now rows, which are real cash out)
  − cash refunds: returns with settle_method = 'Cash'
```

Deliberately NOT in Expected Cash, and labelled on screen:

- **Mixed-method bills' paid amount** — the sale records one paid total for
  a Mixed bill, not its cash/card split, so the cash part cannot be known.
  The screen shows that total on its own row ("NOT counted in Expected
  Cash") whenever it is non-zero.
- **Drawer float** — the app has no opening-float concept. If the shop
  keeps Rs 5,000 in the drawer overnight, the difference will show it every
  day. That is expected; write it in the closing note.
- Credit (udhaar) bills contribute only when money actually arrives — as
  customer receipts above. Card/Bank/JazzCash/EasyPaisa money never touches
  the drawer, so it never appears in Expected Cash.

## 5. Scheduled auto-backup

**File map:** `Services/AutoBackupService.cs`; configuration on the
Settings screen (Admin only, §2); status line on Dashboard + Settings.

Behaviour:

- A timer checks **every minute** while the app runs. If auto-backup is ON,
  a folder is set, the time is at/past `backup_time` (default 21:00) and no
  backup exists for today (`last_backup_date`), it copies the database via
  `DatabaseService.BackupCopyTo` as `sanitarypos-yyyy-MM-dd-HHmm.db`,
  stamps `last_backup_date` / `last_backup_at`, then **prunes**: only files
  matching that exact auto-backup name pattern, keeping the newest
  `backup_keep_count` (default 30). Manual backups
  (`sanitarypos-backup-*.db`) and anything else in the folder are never
  touched.
- The same check runs once at startup — if the PC was off at 21:00, the
  backup happens when the shop opens the app (catch-up).
- **On app exit**, one catch-up copy runs if the day has none. Phase 1 had
  NO on-close backup (only the manual button), so this replaces rather
  than duplicates anything — and because `last_backup_date` is the single
  gate, a day can never collect two scheduled copies.
- Every failure is swallowed on purpose: a full disk or an unplugged drive
  must never crash the billing counter. `last_backup_at` only moves on
  success, so the Dashboard line ("Last Backup: date • folder") tells the
  truth. A manual **Backup Now** also stamps `last_backup_at` now, so the
  status line covers manual backups too (and counts as today's backup for
  the timer).

## 6. Installer

**File map:** `installer/SanitaryPOS.iss`, `installer/BUILD-INSTALLER.md`.

- Inno Setup 6 script: "Sanitary POS" v1.0.0, installs the published files
  from a configurable `publish\` folder, Start Menu + Desktop shortcuts,
  full uninstall (the database in `%AppData%\SanitaryPOS` deliberately
  survives uninstall — shop data is never wiped by an upgrade/reinstall).
- **Noto Nastaliq Urdu** font: installed with the correct
  `fontinstall` / `onlyifdoesntexist` flags when `installer\fonts\
  NotoNastaliqUrdu-Regular.ttf` is present at compile time (delete that one
  `[Files]` line if you skip the font). Source: Google Fonts, SIL OFL 1.1 —
  ship the `OFL.txt` license with it (details in BUILD-INSTALLER.md).
- **.NET 8 Desktop Runtime**: NOT bundled. The script checks for the
  `Microsoft.WindowsDesktop.App 8.x` shared framework before copying
  anything and stops with a clear message + the Microsoft download link if
  it is missing.
- Build order (full steps in BUILD-INSTALLER.md):
  `dotnet publish ... -r win-x64 --self-contained false -o installer\publish`
  → put the font in `installer\fonts\` → compile the `.iss` →
  `installer\Output\SanitaryPOS-Setup-1.0.0.exe`.

## 7. Known gaps — what Phase 3 deliberately does NOT do

- **FBR live integration: NOT built.** There is still only the tax on/off +
  rate setting and the tax field on bills (Phase 1 placeholders). Connecting
  to FBR's POS invoicing API is a separate project with its own approval
  process.
- **Multi-branch / cloud sync: NOT built.** One shop, one PC, one SQLite
  file — as planned. Backups are the data-safety story.
- **No per-user tills** — day-end is one shared till (see §4).
- **Cancelled-bill payments are not auto-reversed** (see §3 edge case).
- Password hashing remains the Phase 1 **SHA-256** (fine for a shop
  counter; it is not a defence against someone who already has the PC).
- Everything from Phases 1–2 still applies: the app has **never been
  compiled** in the build environment (Linux); the first build is F5 in
  Visual Studio 2022 on the Windows PC.

## 8. Phase 3 counter test checklist (Windows PC)

Users & password:
- [ ] Login as admin → Users screen lists admin; sidebar shows Users +
      Day-End buttons.
- [ ] The red default-password banner is visible. Change admin's password
      (Users → Change MY Password, or Settings): banner disappears
      immediately, old password stops working, new one works.
- [ ] Add a Cashier user; log in as them: no Users button in the sidebar,
      no profit card, no purchase-rate column, Settings backup panel hidden,
      but Settings → Change Password works for their own account.
- [ ] Try to deactivate yourself → bilingual refusal. Deactivate the
      cashier → they can no longer log in. Reactivate → login works.
- [ ] With two Admins, demote one → OK. Try to demote/deactivate the last
      Admin → refusal.
- [ ] Admin resets the cashier's password → cashier logs in with the new one.
- [ ] Duplicate username on Add → refusal message.

Backup:
- [ ] Settings: set a backup folder, turn auto-backup ON, set the time a
      few minutes ahead, keep Settings open → at the time, a
      `sanitarypos-yyyy-MM-dd-HHmm.db` file appears in the folder and
      Dashboard shows the Last Backup line.
- [ ] Set keep-count to 2, fake 3 older auto-named files in the folder,
      trigger another backup → only the newest 2 auto files remain; a
      `sanitarypos-backup-...` manual file is untouched.
- [ ] Manual Backup Now still works and updates the Last Backup line.
- [ ] Close and reopen the app before the set time with no backup today →
      on close OR next open, today's copy is made (exactly one).

Day-End:
- [ ] Make 2 cash bills (one partly paid), 1 credit bill, receive one khata
      payment in cash, pay a supplier in cash, do one cash refund return.
      Open Day-End: check every row against the formula in §4 by hand.
- [ ] Enter counted cash = expected → difference Rs 0. Enter less →
      difference negative. Close Day (as Admin) → success message; reopen
      the date → saved record shown, form gone; history lists the closing.
- [ ] As Cashier: summary visible, count/close panel hidden.
- [ ] Print Z Report → check the A4 page has the shop header, the summary
      rows, expected/counted/difference. (Printing is allowed for both roles;
      only closing is Admin-only.)

Cancel bill:
- [ ] As Admin on Returns: load a completed bill, type a reason, Cancel →
      confirm Yes → stock of its items is back (check Products), the
      customer's khata no longer counts the bill, bill can't be loaded again.
- [ ] Cancel without a reason → refusal. Cancel a bill that has a return →
      refusal. As Cashier: no cancel panel at all.
- [ ] Billing: hold a bill, then Remove Held Bill → gone from the list.

Installer:
- [ ] Build per `installer/BUILD-INSTALLER.md`; on a clean Windows account:
      runtime-missing message appears if .NET 8 is absent; after installing
      the runtime, setup completes, Desktop shortcut opens the app, Urdu
      font renders Nastaliq, uninstall keeps `%AppData%\SanitaryPOS`.

## 9. Go-Live checklist (do these before the first real day)

- [ ] **Change the admin password** (banner must be gone) and create the
      real staff users with proper roles.
- [ ] **Set shop info** in Settings: shop names (EN + UR), address, phone,
      WhatsApp — these print on receipts and quotations.
- [ ] **Set printers**: thermal (receipts) + A4 (quotations/reports) names
      in Settings; print one test bill and one test quotation.
- [ ] **Set the backup folder** (ideally a USB drive or a synced folder),
      turn auto-backup ON, keep-count 30, time after closing.
- [ ] **Enter opening data**: products with correct purchase/retail/trade
      rates, opening stock (via Purchase or stock correction), customers
      with opening udhaar balances, suppliers with opening payables.
- [ ] **Test print + a full practice day**: a few bills at each price level,
      a quotation converted to a bill, one return, one day-end closing —
      then delete nothing; just start Day 1 knowing the flow works.
- [ ] Keep one printed copy of the first Z-report with the cash count —
      your paper trail starts there.

*All planned phases (1–3) are now complete.*
