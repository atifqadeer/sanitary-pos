# Sanitary POS — Bilingual (Urdu/English) Desktop POS for Pakistan

Status: Phase 2 code complete — WPF app in `src/` (login, dashboard, products, billing,
quotations, customers/khata, settings, bilingual EN/UR engine, receipt + A4 printing,
plus purchase/GRN, suppliers & payables, returns & exchange, reports, bulk price update).
See `docs/PHASE1.md` and `docs/PHASE2.md` for what was built, Windows run steps and the
counter test checklists.
Not yet compiled — first build happens on a Windows PC with Visual Studio 2022 (F5).
First login: admin / admin123 (must be changed before live use).
Next: Phase 3 (Users & Roles, scheduled backup, day-end, installer, FBR readiness).

## Folders
- `docs/PLAN.md` — full plan: architecture, 14 screens, roles, build order, assumptions
- `database/schema.sql` — complete SQLite schema (19 tables) + seed data (units, categories, settings)
- `src/SanitaryPOS/` — WPF app will live here (Models, Data, Services, Localization, ViewModels, Views, Printing, Helpers)
- `installer/` — Inno Setup script will go here in Phase 3

## To open/build (on Windows, when code lands)
1. Install Visual Studio 2022 Community (free) with ".NET desktop development".
2. Open the .sln in `src/`, press F5.
3. Database is created automatically from `database/schema.sql` on first run.

## Language files
All English text in `Localization/Strings.en.xaml`, all Urdu in `Localization/Strings.ur.xaml`.
To change any Urdu word, edit only the Urdu file — no code change needed.
