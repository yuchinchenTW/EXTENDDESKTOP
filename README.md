# ExtentDesktop

`ExtentDesktop` is a small Windows pair of apps that stream one chosen desktop surface from a desktop PC to a laptop.

## What It Can Do

- Mirror the combined desktop or one chosen display to a laptop receiver.
- Help with the "third monitor" workflow if the desktop PC already has a virtual display driver installed.
- Run as two simple WinForms executables with no extra framework install required on normal Windows machines.

## What It Cannot Do By Itself

This project does **not** create a brand-new Windows monitor on its own.

For a laptop to behave like a true extra extended desktop target, the desktop PC must first expose an additional monitor through a virtual or indirect display driver. After that, this project can stream that new virtual monitor to the laptop.

Without that driver layer:

- selecting `Screen 1` or `Screen 2` only mirrors an existing monitor
- selecting `All Displays` mirrors the combined desktop
- Windows still sees only the monitors it already had

## Structure

- `Host`: runs on the desktop PC and streams the selected display area
- `Receiver`: runs on the laptop and displays the stream
- `Shared`: tiny TCP auth/frame protocol

## Build

```powershell
.\build.ps1
```

Outputs go to `dist\`.

## Usage

1. Run `ExtentDesktopHost.exe` on the desktop PC.
2. Pick a port, password, and display source.
3. Run `ExtentDesktopReceiver.exe` on the laptop.
4. Enter the desktop PC's IP, port, and password.
5. Click `Connect`.
6. Use `Fullscreen` or `F11` on the receiver.

## Practical Reality

If your real goal is "make the laptop become a third Windows monitor", the missing piece is the desktop-side display driver, not the receiver UI.

This project is the transport/display half of that setup.
