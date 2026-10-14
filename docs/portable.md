# GTK3 Portable Edition (Linux, macOS, BSD)

The portable edition of Tiny11 Builder is a native desktop application implemented with GTK3 and C# (.NET 8). It allows you to build debloated, portable Windows 11 installation images directly on Linux, macOS, and BSD systems without requiring Windows or virtual machines.

## Architecture and Native POSIX Tools

Unlike the Windows edition which relies on DISM and Win32 registry APIs, the portable edition runs entirely using native, cross-platform POSIX utilities:

- **`wimlib-imagex`** ([wimlib.net](https://wimlib.net/)):
  - Reads image metadata, editions, architectures, and sizes (`wimlib-imagex info`).
  - Exports and recompresses selected client editions (`wimlib-imagex export --compress=LZX --recompress --check`).
  - Discovers installed WindowsApps packages (`wimlib-imagex dir --path-only`).
  - Deletes chosen bundled apps, Edge/WebView2, OneDrive, and scheduled task definitions (`wimlib-imagex update`).
  - Injects unattended setup files and post-install configuration scripts.
  - Splits oversized images into FAT32-compatible SWM chunks (`wimlib-imagex split`).
  - Optimizes and verifies WIM archive integrity (`wimlib-imagex optimize --check`).
- **`xorriso`** ([manpages.debian.org/xorriso](https://manpages.debian.org/trixie/xorriso/xorrisofs.1.en.html)):
  - Extracts source Windows 11 ISOs cleanly preserving directory hierarchies.
  - Creates dual-mode bootable ISOs supporting both legacy BIOS (El Torito) and UEFI (`efisys.bin`).
- **`GtkSharp`** ([GtkSharp](https://github.com/GtkSharp/GtkSharp)):
  - Native GTK 3 desktop GUI with responsive navigation rail, card layouts, dark styling, live progress reporting, and modal dialogs.

## Features

- **Full GTK3 Desktop Interface**:
  - Step 1: Welcome & Mode Selection (Tiny11 Original, Tiny11 Minimal, Custom Configuration).
  - Step 2: Windows Image Selection (Pick local ISO, browse folder, inspect editions, or download official ISO from Microsoft).
  - Step 3: Components & Apps (Presets: Tiny11, Minimal, Balanced, Select All; individual toggles for Copilot, Teams, Outlook, Xbox, News, Solitaire, Edge, OneDrive, etc.).
  - Step 4: Settings & Tweaks (Hardware bypasses, Local Account BypassNRO, Compact OS, Telemetry, Sponsored apps, Copilot, BitLocker, Reserved storage, workspace and output destinations).
  - Step 5: Review & Recipes (Configuration summary card, JSON recipe export and import).
  - Step 6: Live Build Progress (Animated progress bar, terminal-style log output, cancellation, SHA-256 digest calculation, and one-click copy).
- **Dual-Layer Hardware Bypass**:
  - Injects bypass values into Windows Setup WinPE boot image (`sources/boot.wim` Index 2) so hardware requirements (TPM 2.0, Secure Boot, RAM, CPU, Storage) are bypassed before setup displays.
  - Injects synchronous bypass commands in the `windowsPE` pass of `autounattend.xml`.
- **Unattended Local Account & OOBE Debloat**:
  - Injects `autounattend.xml` in the media root and in `/Windows/System32/Sysprep/autounattend.xml`.
  - Bypasses mandatory Microsoft Account sign-in (`BypassNRO = 1`, `HideOnlineAccountScreens = true`).
  - Configures Compact OS installation to minimize disk footprint.
- **Post-Install Servicing Script (`SetupComplete.cmd`)**:
  - Automatically executed by Windows Setup as `NT AUTHORITY\SYSTEM` at the end of installation.
  - Applies registry tweaks to the live system and mounts the Default User profile hive (`NTUSER.DAT`) so all future user accounts inherit privacy and debloat settings.
  - Disables telemetry services (`dmwappushservice`), sponsored cloud content, advertising IDs, and telemetry tasks.
- **Official Microsoft Windows 11 Downloader**:
  - Queries Microsoft's software download catalog for the latest official ISOs (x64 and ARM64).
  - Automatic language matching.
  - Resumable downloads via HTTP Range headers with SHA-256 digest verification.

## Supported Operating Systems

- **Linux**: Debian, Ubuntu, Fedora, Arch Linux, Alpine, openSUSE, etc.
- **macOS**: macOS Monterey, Ventura, Sonoma, Sequoia (Intel & Apple Silicon).
- **BSD**: FreeBSD, OpenBSD, NetBSD.

## Installation & Prerequisites

Install .NET 8 SDK, GTK 3 development/runtime packages, `wimlib-imagex`, and `xorriso`:

### Ubuntu / Debian:
```sh
sudo apt update
sudo apt install dotnet-sdk-8.0 libgtk-3-0 wimtools xorriso
```

### Fedora / RHEL:
```sh
sudo dnf install dotnet-sdk-8.0 gtk3 wimtools xorriso
```

### Arch Linux:
```sh
sudo pacman -S dotnet-sdk gtk3 wimlib xorriso
```

### macOS (Homebrew):
```sh
brew install dotnet gtk+3 wimlib xorriso
```

### FreeBSD:
```sh
pkg install dotnet gtk3 wimlib xorriso
```

## Running the Application

From the repository root:

```sh
# Run the GTK3 application
dotnet run --project src/Tiny11Builder.Gtk/Tiny11Builder.Gtk.csproj

# Or build the portable solution
dotnet build Tiny11Builder.Portable.sln

# Run portable unit tests
dotnet test tests/Tiny11Builder.Portable.Tests/Tiny11Builder.Portable.Tests.csproj
```
