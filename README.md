# Xbox LED Control

Controls the Guide-button LED on an Xbox One / Series X|S controller connected via USB or the Xbox Wireless USB dongle.

Sends a GIP command directly to `\\.\XboxGIP` (driver `xboxgip.sys`). **No administrator rights required.**

---

## Requirements

- **Windows 10/11** (tested on Windows 11)
- **[.NET 10 SDK](https://dotnet.microsoft.com/download)** — required to build
- **Xbox controller** connected via USB cable or Xbox Wireless USB Adapter (dongle)

---

## Build

```bat
dotnet build XboxLedControl.csproj -c Release
```

### Publish as a single self-contained `.exe` (no .NET install needed to run)

```bat
dotnet publish XboxLedControl.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output: `bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\XboxLedControl.exe`

---

## Usage

```
XboxLedControl [--debug] <brightness|pattern>

  --debug      Print device ID, frame bytes, and send result.

  brightness   0–100  (0 = off, 100 = maximum)
  pattern      off | on | ramp | fastblink | slowblink | charging
```

### Examples

```bat
XboxLedControl 0             # turn LED off
XboxLedControl 50            # 50% brightness
XboxLedControl 100           # maximum brightness
XboxLedControl on            # solid on (max)
XboxLedControl ramp          # animate (ramp) to full brightness
XboxLedControl fastblink     # fast blink (200 ms on / 400 ms cycle)
XboxLedControl slowblink     # slow blink (600 ms on / 1200 ms cycle)
XboxLedControl charging      # charging pulse (3 s on / 6 s cycle)
XboxLedControl --debug 50    # verbose output
```

### Exit codes

| Code | Meaning              |
|------|----------------------|
| `0`  | Success              |
| `1`  | Controller not found |

---

## Run with `dotnet run`

```bat
dotnet run --project XboxLedControl.csproj -- 50
dotnet run --project XboxLedControl.csproj -- ramp
dotnet run --project XboxLedControl.csproj -- --debug 75
```

---

## How it works

The app opens `\\.\XboxGIP`, reads the controller's device ID from the first frame,
then writes a 23-byte GIP LED command frame per [MS-GIPUSB §3.1.5.5.7](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gipusb).
