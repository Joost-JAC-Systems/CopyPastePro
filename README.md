# CopyPaste Pro

A modern clipboard manager for **Windows 10/11**. CopyPaste Pro keeps a searchable history of everything you copy—text, images, files, HTML, and more—while giving you quick paste, privacy controls, and an optional image library.

Built with **.NET 8** and **WPF**.

---

## Features

### Clipboard history
- Automatic capture of text, images, files, HTML, RTF, and other formats
- Search, filter by type/category, favorites, and pins
- Smart categories and optional auto-organization rules
- Duplicate detection and configurable history limits

### Quick access
- Floating popup near the cursor (default: **Ctrl+Shift+V**)
- Click an item to paste into the active application
- Search, pin, and favorite from the quick list

### Full manager
- Split view: history list + rich preview (text and zoomable images)
- Custom context menus for history, quick access, and image library
- **Ctrl+0** resets image preview zoom to 100%

### Image library
- Browse saved clipboard images (grid/list)
- Auto-save and sync-folder support
- Fullscreen preview, export, and Explorer integration

### Screenshot capture
- Region snip mode (default: **Ctrl+Shift+S**)
- Captured images can be saved to the image library automatically

### Privacy & security
- Sensitive-data detection (credit cards, API keys, JWTs, custom regex, and more)
- Private/incognito browser handling (Firefox, Chrome, Edge, etc.)
- Optional PIN lock for the manager
- **Panic wipe** hotkey and tray action (clears app history + Windows clipboard)
- Session lock options (pause capture, clear history/clipboard on lock)
- Optional DPAPI encryption for database text and payload files
- Secure delete passes for payload files on disk

### Clipboard actions
Separate controls for **CopyPaste Pro history** and the **Windows clipboard**:

| Action | App history | Windows clipboard (Ctrl+V) | Win+V history |
|--------|-------------|----------------------------|---------------|
| Clear unpinned | Unpinned only | — | — |
| Clear app history | All items | — | — |
| Empty database | All items | — | — |
| Clear app + Windows clipboard | All items | Cleared | Cleared |
| Clear Windows clipboard + Win+V | — | Cleared | Cleared |

### Database tools
- **Export database…** — save a copy of `history.db` anywhere
- **Empty database** — remove all history rows (with confirmation)
- Scheduled backups to `%LocalAppData%\CopyPastePro\backups\` (never auto-restored)

### System integration
- System tray icon with quick actions
- Global hotkeys
- Start minimized / run at Windows startup (settings)
- Dark and light themes

---

## Requirements

- **Windows 10** (version 1809+) or **Windows 11**
- For **Win+V history clearing**: Clipboard history must be enabled in  
  **Settings → System → Clipboard → Clipboard history**
- To **build from source**: [.NET 8 SDK](https://dotnet.microsoft.com/download)

---

## Download & run

### Pre-built executable

After building (see below), run:

```
dist\CopyPastePro.exe
```

Close any running instance before rebuilding, or the publish step may fail because the file is locked.

### Build from source

From the repository root in PowerShell:

```powershell
.\build.ps1
```

This publishes a **single-file, self-contained** `win-x64` executable to the `dist` folder.

Manual build:

```powershell
cd CopyPastePro
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o ..\dist
```

### Run during development

```powershell
cd CopyPastePro
dotnet run
```

---

## Default hotkeys

| Hotkey | Action |
|--------|--------|
| **Ctrl+Shift+V** | Quick access popup |
| **Ctrl+Shift+M** | Open full manager (optional; off by default) |
| **Ctrl+Shift+S** | Screenshot / snip capture |
| **Ctrl+Shift+Delete** | Panic privacy wipe (optional; off by default) |

All hotkeys can be changed in **Settings → Hotkeys**.

---

## Usage tips

1. **Tray icon** — Right-click for quick access, clipboard actions, settings, and exit. Use **Terminate** to quit without a confirmation dialog.
2. **Quick access** — Type to search; **Enter** pastes the selected item; **Esc** closes the window.
3. **Manager footer** — Export database, empty database, recategorize, clear unpinned, or clear app history.
4. **Right-click** — History items, quick access, and image library have full context menus (paste, pin, delete, storage paths, etc.).
5. **Backups** — Stored under `%LocalAppData%\CopyPastePro\backups\`. After a clear/wipe, a **new** timestamped backup is created; old backups are not reused automatically.

---

## Data locations

All application data is stored locally on your PC:

| Path | Contents |
|------|----------|
| `%LocalAppData%\CopyPastePro\history.db` | SQLite clipboard history |
| `%LocalAppData%\CopyPastePro\data\` | Image payloads and large binary data |
| `%LocalAppData%\CopyPastePro\settings.json` | User settings |
| `%LocalAppData%\CopyPastePro\backups\` | Timestamped database backups |

Nothing is sent to the cloud by default.

---

## Project structure

```
CopyPaste Pro/
├── build.ps1              # Publish script → dist/
├── dist/                  # Published CopyPastePro.exe (generated)
├── CopyPastePro/
│   ├── App.xaml.cs        # Startup, tray, hotkeys
│   ├── MainWindow.xaml    # Full manager UI
│   ├── QuickAccessWindow.xaml
│   ├── Services/          # Capture, paste, privacy, backup, rules, …
│   ├── Views/             # Settings, dialogs, image library
│   ├── Controls/          # Zoomable image preview, fullscreen
│   └── Themes/            # Dark/light styles and context menus
└── README.md
```

---

## Technology

- **.NET 8** — WPF + Windows Forms (tray)
- **Microsoft.Data.Sqlite** — local history database
- **WinRT** `Windows.ApplicationModel.DataTransfer.Clipboard` — Win+V history clear
- **Win32** — global hotkeys, clipboard monitor, focus helpers

---

## Privacy note

CopyPaste Pro is designed to help you manage clipboard history on your own machine. Use privacy settings (sensitive-data blocking, incognito rules, panic wipe, encryption) for content you do not want stored. Clipboard managers inherently retain copied data—review settings before use on shared or sensitive systems.

---

## Troubleshooting

| Issue | Suggestion |
|-------|------------|
| History reappears after clear | Fully quit the app (**Terminate** from tray) and restart. Another instance may still be running. |
| Windows clipboard not clearing | Enable Clipboard history in Windows Settings; close apps that lock the clipboard; try again. |
| Build fails — file in use | Exit CopyPaste Pro before running `build.ps1`. |
| Firefox InPrivate images missing | Enable **Save images from private windows** in Privacy settings (not “files”). |
| Pinned Win+V items remain | Windows may keep pinned items until removed manually; the app deletes history entries via the Windows API when possible. |

---

## Contributing

Contributions are welcome. Please open an issue or pull request with a clear description of the change.

1. Fork the repository  
2. Create a feature branch  
3. Make your changes and test on Windows  
4. Submit a pull request  

---

## License

Specify a license before publishing (for example MIT, Apache-2.0, or GPL-3.0). If no `LICENSE` file is present, all rights are reserved by the author.

---

## Author

**CopyPaste Pro** — Joost (JAC-Systems) — version 1.0.3
