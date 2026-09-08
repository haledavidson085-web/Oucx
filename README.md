# Oucx Reader

A focused PDF reader for Windows, built with C#, WPF, .NET 10, and the Microsoft Edge WebView2 PDF engine.

The application includes an original Oucx Reader icon embedded in the executable and displayed in the window and taskbar.

## Features

- Open PDFs from the file picker or by drag-and-drop
- Recently opened document list
- Page navigation and zoom controls
- Search and print through the native Edge PDF toolbar/shortcuts
- Full-screen reading mode
- Dark, distraction-free interface

## Run

```powershell
dotnet restore
dotnet run --project Oucx.Reader.csproj
```

## Releases and updates

Push a semantic-version tag such as `v1.0.0` to build a self-contained Windows x64 package and publish it as a GitHub Release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

Packaged builds check the latest public GitHub Release at startup. When a newer stable version is available, the app offers to download it, verifies the release package's SHA-256 checksum, installs it after shutdown, and restarts automatically.

The [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) is included with current Windows 10 and Windows 11 installations. If the app reports that its PDF engine cannot start, install the Evergreen WebView2 Runtime from Microsoft.

## Shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+O` | Open a PDF |
| `Ctrl+F` | Search within the PDF |
| `Ctrl+P` | Print |
| `Ctrl++` / `Ctrl+-` | Zoom in / out |
| `Page Up` / `Page Down` | Previous / next page |
| `F11` | Toggle full screen |
| `Esc` | Leave full screen |
