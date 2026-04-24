# ExtentDesktop

`ExtentDesktop` is a small Windows host/receiver pair for showing one chosen desktop surface from a desktop PC on a laptop over the LAN.

## What It Does

- Streams `All Displays` or one selected display from the desktop PC to the laptop.
- Works well with a virtual display driver, so the laptop can show that virtual monitor.
- Auto-discovers available hosts on the same LAN inside the receiver UI.
- Builds into two simple WinForms executables with the built-in .NET Framework compiler already present on most Windows machines.

## What It Does Not Do By Itself

This project does **not** create a new Windows monitor on its own.

For the laptop to behave like a true extra extended desktop target, the desktop PC must first expose an additional monitor through a virtual or indirect display driver. After that, `ExtentDesktop` can stream that extra virtual monitor to the laptop.

Without that driver layer:

- selecting `Screen 1` or `Screen 2` only mirrors an existing monitor
- selecting `All Displays` mirrors the combined desktop
- Windows still sees only the monitors it already had

## Structure

- `Host`: runs on the desktop PC and streams the selected display area
- `Receiver`: runs on the laptop and shows the stream
- `Shared`: small TCP auth/frame protocol plus LAN discovery protocol

## Build

```powershell
.\build.ps1
```

Outputs go to `dist\`.

## Normal Setup Flow

### Desktop PC

1. Install and enable your virtual display driver.
2. In the virtual display driver control app, create at least one virtual monitor.
3. Open Windows `Settings > System > Display` and confirm the new display exists.
4. Set Windows to `Extend these displays`.
5. Arrange the monitors so the virtual display is where you want it.
6. Run `ExtentDesktopHost.exe`.
7. Choose the virtual display in the `Display` dropdown.
8. Set a port and password, then click `Start Host`.

### Laptop

1. Run `ExtentDesktopReceiver.exe`.
2. Wait for the desktop PC to appear in the host list on the right.
3. Click the discovered host to auto-fill IP and port.
4. Enter the same password.
5. Click `Connect`.
6. Use `Fullscreen` or `F11` if you want the laptop screen dedicated to that display.

## When Host Discovery Does Not Show Up

- Make sure both machines are on the same LAN.
- Make sure the desktop host is already running.
- Allow the app through Windows Firewall when prompted.
- If needed, connect manually by entering the desktop PC's IP and port.

## Practical Reality

If your real goal is "make the laptop become a third Windows monitor", the missing piece is the desktop-side display driver, not the receiver UI.

This project is the transport/display half of that setup.
