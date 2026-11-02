# Building the Sanitary POS installer (Windows)

Everything here happens on a **Windows PC** — the app is WPF and cannot be
built or packaged on Linux. You end up with one file,
`SanitaryPOS-Setup-1.0.0.exe`, that you can copy to the shop PC on a USB stick.

## 1. Publish the app (framework-dependent, win-x64)

From the repo root, in a terminal:

```bat
dotnet publish src\SanitaryPOS\SanitaryPOS.csproj -c Release -r win-x64 --self-contained false -o installer\publish
```

- `--self-contained false` = framework-dependent: the PC must have the
  **.NET 8 Desktop Runtime** installed (the setup checks for it and shows
  the download link if missing). This keeps the setup file small.
- The output lands in `installer\publish\` — exactly where the Inno script
  (`SanitaryPOS.iss`, `#define PublishDir "publish"`) looks for it.
  Keep `SanitaryPOS.exe`, `SanitaryPOS.dll`, `SanitaryPOS.runtimeconfig.json`
  and the rest of that folder together; the installer copies the whole folder.

Quick local check before packaging: double-click `installer\publish\SanitaryPOS.exe`
on the build PC — it should open to the login screen (admin / admin123).

## 2. Get Inno Setup

Download **Inno Setup 6** from https://jrsoftware.org/isdl.php and install it
(free). You will use its compiler, `ISCC.exe`
(usually `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`), or just open the
`.iss` file in the Inno Setup Compiler GUI.

## 3. Get the Urdu font (one time)

The app's Urdu text uses **Noto Nastaliq Urdu**:

- Official source: Google Fonts — https://fonts.google.com/noto/specimen/Noto+Nastaliq+Urdu
  (Download family → you get the font files).
- Put `NotoNastaliqUrdu-Regular.ttf` into `installer\fonts\`.
- The font is under the **SIL Open Font License 1.1 (OFL)** — keep the
  license text (`OFL.txt`, included in the Google Fonts download) with the
  font files in your repo; the license allows bundling it with the app like
  this installer does.
- If you skip the font: delete the one `[Files]` font line in
  `SanitaryPOS.iss` before compiling, otherwise the compile fails with
  "file not found". The app still runs without it — Windows falls back to
  another Urdu-capable font — but Nastaliq looks right.

## 4. Compile the installer

GUI: open `installer\SanitaryPOS.iss` in Inno Setup Compiler → Build → Compile.

Or command line:

```bat
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\SanitaryPOS.iss
```

Result: `installer\Output\SanitaryPOS-Setup-1.0.0.exe`.

## 5. Test on a clean Windows account

Do this once before giving the file to the shop:

1. Create a fresh Windows user account (or use a spare PC / VM).
2. Copy `SanitaryPOS-Setup-1.0.0.exe` there and run it.
   - If .NET 8 Desktop Runtime is missing, setup must STOP with the clear
     message + Microsoft link. Install the runtime from that link, re-run setup.
3. Launch **Sanitary POS** from the Desktop shortcut: login screen appears.
4. Log in with `admin / admin123`, change the password, toggle EN/اردو.
5. Print a test bill (thermal) and a test quotation (A4) if printers are attached.
6. Uninstall from Settings → Apps: the program goes away, but
   `%AppData%\SanitaryPOS\sanitarypos.db` intentionally STAYS (shop data is
   never deleted by uninstall). Reinstall and confirm the data is still there.

## Notes

- Upgrades: bump `AppVersion` in the `.iss` (and the output name), rebuild,
  and run the new setup over the old install — files are replaced, the
  database in `%AppData%` is untouched, and the app's startup migration
  brings older databases forward.
- `PrivilegesRequired=admin` is set because installing the font and writing
  to Program Files need it.
