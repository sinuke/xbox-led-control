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
dotnet build XboxLedControl.csproj -c Release -r win-x64
```

### Publish as a single self-contained `.exe` (no .NET install needed to run)

```bat
dotnet publish XboxLedControl.csproj -c Release -r win-x64
```

Output: `bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\xbox-led-control.exe`

> `PublishSingleFile`, `PublishTrimmed`, and `EnableCompressionInSingleFile` are already set in the project file — no extra flags needed.

---

## Usage

```
xbox-led-control [global options] <command> [command options] [arguments]

Global options (available to all commands):
  -v, --verbose    Print verbose output (device ID, frame bytes, send result)
  -?, -h, --help   Show help
      --version    Show version

Commands:
  led              Set the Guide button LED intensity or pattern
  list             List all connected Xbox controllers
  metadata         Read and display GIP metadata from a controller
```

### `led` command

```
xbox-led-control led [-v] [-d <device-id>] <intensity|pattern>

Arguments:
  intensity        0–100  (0 = off, 100 = maximum; out-of-range is an error)
  pattern          off | on | ramp | fastblink | slowblink | charging

Options:
  -d, --device     Controller device ID (6 colon-separated hex bytes).
                   Skips auto-discovery. Case-insensitive.
                   Example: AA:BB:CC:DD:EE:FF
  -v, --verbose    Print device ID, frame bytes, and send result
  -?, -h, --help   Show help for this command
```

### `list` command

```
xbox-led-control list [-v]

Options:
  -v, --verbose    Print verbose output (driver handle, IOCTL result, discovered MACs)
  -?, -h, --help   Show help for this command
```

Output example:

```
#    Controller                     Device ID
---  ----------------------------   -----------------
1    Xbox Series X|S Controller     AA:BB:CC:DD:EE:FF
2    Xbox One Controller            11:22:33:44:55:66
```

If no controllers are connected:

```
No Xbox controllers found.
```

### `metadata` command

```
xbox-led-control metadata [-v] [-d <device-id>]

Options:
  -d, --device     Controller device ID (6 colon-separated hex bytes).
                   Skips auto-discovery. Case-insensitive.
                   Example: AA:BB:CC:DD:EE:FF
  -v, --verbose    Also print blob size and hex dump before the decoded output
  -?, -h, --help   Show help for this command
```

Reads the GIP metadata blob the controller broadcasts during USB enumeration and prints a human-readable summary (VendorID, ProductID, firmware versions, supported commands, interfaces, messages). No request is sent to the device — the blob arrives automatically after re-enumeration.

Output example:

```
VendorID:   0x045E (Microsoft)
ProductID:  0x0B12 (Xbox Series X|S Controller)
Version:    5.0

SupportedInSystemCommands:
    1 - Protocol Control
    2 - Hello Device
  ...

SupportedInterfaces:
  {082E402C-07DF-45E1-A5AB-A3127AF197B5} Microsoft.Xbox.Input.IGamepad
  ...
```

### Examples

```bat
xbox-led-control led 0                           # turn LED off
xbox-led-control led 50                          # 50% intensity
xbox-led-control led 100                         # maximum intensity
xbox-led-control led on                          # solid on (max)
xbox-led-control led ramp                        # animate (ramp) to full intensity
xbox-led-control led fastblink                   # fast blink (200 ms on / 400 ms cycle)
xbox-led-control led slowblink                   # slow blink (600 ms on / 1200 ms cycle)
xbox-led-control led charging                    # charging pulse (3 s on / 6 s cycle)
xbox-led-control led 50 --verbose                # verbose output
xbox-led-control led 50 -v                       # same, short form

xbox-led-control led on --device AA:BB:CC:DD:EE:FF   # target specific controller
xbox-led-control led 75 -d aa:bb:cc:dd:ee:ff         # same, lowercase hex

xbox-led-control list                            # list all connected controllers
xbox-led-control list -v                         # list with verbose driver output

xbox-led-control metadata                        # read and decode GIP metadata (first controller)
xbox-led-control metadata -v                     # also print blob size and hex dump
xbox-led-control metadata -d AA:BB:CC:DD:EE:FF  # target specific controller

xbox-led-control --help                          # show global help
xbox-led-control led --help                      # show led command help
xbox-led-control list --help                     # show list command help
xbox-led-control metadata --help                 # show metadata command help
xbox-led-control --version                       # show version
```

### Exit codes

| Code | Meaning              |
|------|----------------------|
| `0`  | Success              |
| `1`  | Controller not found |

---

## Run with `dotnet run`

```bat
dotnet run --project XboxLedControl.csproj -- led 50
dotnet run --project XboxLedControl.csproj -- led ramp
dotnet run --project XboxLedControl.csproj -- led 75 --verbose
dotnet run --project XboxLedControl.csproj -- led on --device AA:BB:CC:DD:EE:FF
dotnet run --project XboxLedControl.csproj -- list
dotnet run --project XboxLedControl.csproj -- metadata -v
```

---

## How it works

All three commands open `\\.\XboxGIP` (driver `xboxgip.sys`) and send `IOCTL_GIP_REENUMERATE` to trigger controller announcements.

- **`led`** — reads the first inbound frame to discover the device ID, then writes a 23-byte GIP frame per [MS-GIPUSB §3.1.5.5.7](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gipusb/ec312389-2e05-4915-85ed-0e8fe9c3d33b). When `--device` is provided, the discovery read is skipped entirely.
- **`list`** — reads all inbound frames for up to 1 second, collecting device MACs and opportunistically extracting VendorID/ProductID from metadata fragments to resolve product names.
- **`metadata`** — passively reads the fragmented GIP metadata blob the controller broadcasts during enumeration (no write is sent; the driver only forwards host writes for LED commands). Reassembles fragments per [MS-GIPUSB §3.1.5.2](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gipusb/), then decodes the binary blob.
