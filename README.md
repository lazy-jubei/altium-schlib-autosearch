# altium-schlib-autosearch

Automatically finds and adds `.SchLib` libraries from folders and subfolders, alongside `.PcbLib` files resolved through Altium’s project search paths.

## Install

1. Close Altium and run the installer from [Releases](https://github.com/lazy-jubei/altium-schlib-autosearch/releases/latest).
2. In **Project Options → Search Paths**, add your library folder (for example, `libs\*.*`) and enable **Recursive**.
3. Open **Panels → Library Search Paths** and enable discovery. Libraries appear in the normal **Components** panel and update automatically.

Follows the active project and preserves manually installed libraries. Tested with Altium Designer 26.11.1.

To uninstall, disable discovery in the panel, close Altium, and choose **Remove** in the installer.

## Build

Requires Windows, PowerShell 7, and an installed Altium Designer:

```powershell
.\Build.ps1 -AltiumSystem 'C:\Program Files\Altium\AD26\System'
.\installer\Build-Installer.ps1
```
