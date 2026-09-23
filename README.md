# Teclado Flotante — Floating On-Screen Keyboard for Windows

**English** · [Español](README.es.md)

> 🧪 **Open beta.** It is ready for daily use, but you may still find bugs. If you do, please [open an issue](https://github.com/Danii204/teclado-flotante/issues) describing what you were doing and, if possible, attach the log file `%APPDATA%\TecladoFlotante\log.txt`.

A touch-friendly on-screen keyboard for Windows 10 and 11, built as a replacement for the built-in ones: place it **anywhere on the screen** (including the very top), **resize it freely**, keep the frame to a minimum, and it **never steals focus** from the app you are typing in. Designed for tablets and for people who don't want to deal with anything technical: updates install with one tap from the keyboard itself.

![Teclado Flotante, dark theme](docs/captura.png)
![Teclado Flotante, light theme](docs/captura-claro.png)

## Download

**[⬇ Download the latest version](https://github.com/Danii204/teclado-flotante/releases)** — in the most recent release, download `TecladoFlotante-Setup.exe` (≈ 100 KB, no administrator rights needed).

1. Open `TecladoFlotante-Setup.exe` and tap **Instalar** (Install).
2. If Windows shows *"Windows protected your PC"*, tap **More info → Run anyway**. This appears because the app is not signed with a paid certificate; the source code is this repository and every release publishes its SHA-256 hash.

It installs for the current user only, adds a **Teclado Flotante** shortcut to the desktop, opens when setup finishes and from then on starts automatically with Windows. To install a newer version on top, there is no need to uninstall or close anything.

> The user interface is currently in **Spanish**. Keyboard layouts available: **Spanish (Spain)** and **English (US)**.

## Usage

| Action | How |
| --- | --- |
| Show / hide | Desktop shortcut, taskbar icon, blue floating button or **Ctrl+Alt+K** |
| Move | Drag the top bar or any gap between keys |
| Resize | Drag any edge or corner (saved automatically) |
| Always on top | 📌 button on the top bar |
| Menu | ⋯ button or right-click |

- **Dead keys** work like on a physical keyboard: `´` + `a` = á, `Shift` + `´` + `u` = ü, also `` ` `` and `^`.
- **AltGr** for @ # € [ ] { } \ | ~ ¬.
- **Shift, Ctrl, Alt, AltGr and Win** stay latched until the next key (Ctrl → C = Ctrl+C). Win twice opens the Start menu.
- **Numeric keypad** with Num Lock: when off, it works as arrows, Home, End, Page Up/Down, Insert and Delete.
- Holding a key repeats it.

### Settings (dropdown menu)

- **Layout:** Spanish (Spain) or English (US).
- **Theme:** automatic (follows Windows), light or dark.
- **Bold text** for easier reading.
- **Show when tapping a text field:** the keyboard appears by itself when you tap somewhere you can type.
- **Numeric keypad** and **function row** (F1–F12) can be hidden; the window adjusts and keys keep their size. Without the function row, Esc moves to the number row.
- **Options:** always on top, opacity (also with the mouse wheel over the top bar), floating button when minimized, start with Windows, and **what to show at startup** (nothing, the floating button or the keyboard).
- **Remember position after restart.** By default, every time the app starts the keyboard appears **centered** with the **size you saved**, so it is always visible whatever happened.

## Updates

No need to visit GitHub or know anything about computers: when a new version is available, a blue **Actualizar** (Update) button appears on the keyboard's top bar (and an orange dot on the floating button). Tap **Actualizar ahora** (Update now) and that's it.

The app checks the [Releases](https://github.com/Danii204/teclado-flotante/releases) page every 12 hours (during the open beta, *Pre-releases* are included). Updates are downloaded, verified (SHA-256), installed, and the keyboard reopens by itself. You can also check manually from *Ayuda y actualizaciones → Buscar actualizaciones ahora*, or turn the notification off.

## Privacy

- **No data is collected or sent.** No telemetry, analytics or accounts.
- The only Internet connection is an anonymous request to `api.github.com` to check for new versions (plus downloading the installer if you accept an update). It can be turned off in the menu.
- It never records what you type. *Show when tapping a text field* only detects that a click happened and whether the focused element is editable; it does not read its content. The error log (`%APPDATA%\TecladoFlotante\log.txt`) contains technical messages only, stays on your device and is never sent.

## Robustness

- Never steals focus (`WS_EX_NOACTIVATE`): keystrokes always go to the window you were using.
- Messages and dialogs always appear above the keyboard, where it does not cover them, with large buttons.
- Settings are saved atomically: a sudden power-off cannot corrupt them; a damaged file falls back to defaults.
- If the app crashes, the error is logged and it reopens by itself, centered.
- If a monitor is disconnected or the resolution changes, the keyboard moves back into view.
- Single instance: opening it again just shows the keyboard that is already running.

## Uninstall

*Settings → Apps → Installed apps → Teclado Flotante → Uninstall.* The app, its settings, the Start menu and desktop shortcuts and the autostart entry are removed.

## Known limitations

- It cannot type into apps running **as administrator** (e.g. Task Manager): Windows blocks this for any app without special signing (UIAccess).
- On Windows 11 the app asks for its icon to be shown on the taskbar; if someone previously moved it into the `^` overflow, that choice is respected (it can be dragged back).

## Building from source

Nothing to install: it uses the C# compiler of .NET Framework 4.8, which ships with Windows.

```powershell
.\build.ps1          # builds dist\TecladoFlotante-Setup.exe
.\test.ps1 unit      # tests without mouse (also run by GitHub Actions)
.\test.ps1           # full tests: moves the mouse and types into a test window
.\test.ps1 render    # keyboard screenshots in obj\test\img
```

Layout: `src/` application code, `tests/` test harness, `.github/workflows/` automated build and release.

### Publishing a release

1. Add a `## [X.Y.Z] - YYYY-MM-DD` section to `CHANGELOG.md`.
2. Run `.\release.ps1 X.Y.Z`. While the `CHANNEL` file says `beta`, it is published as a *Pre-release* tagged `vX.Y.Z-beta`; change it to `stable` for the first stable release.

GitHub Actions builds, tests and creates the release with the installer and its SHA-256; installed copies get the update notification.

## License

[MIT](LICENSE) © [Danii204](https://github.com/Danii204)
