# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build
"C:\Program Files\dotnet\dotnet.exe" build XboxLedControl.csproj -c Release

# Run
dotnet run --project XboxLedControl.csproj -- <arg>

# Common invocations
dotnet run --project XboxLedControl.csproj -- 0                # turn LED off
dotnet run --project XboxLedControl.csproj -- 50               # 50% brightness
dotnet run --project XboxLedControl.csproj -- ramp             # ramp-to-level animation
dotnet run --project XboxLedControl.csproj -- --debug 50       # verbose output

# Publish single-file exe (self-contained, no .NET install required)
"C:\Program Files\dotnet\dotnet.exe" publish XboxLedControl.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# Output: bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\XboxLedControl.exe

# Experiments project (dead-end probes, diagnostics)
dotnet run --project experiments/experiments.csproj -- --list
dotnet run --project experiments/experiments.csproj -- --gip-send 50
dotnet run --project experiments/experiments.csproj -- --ble-read
```

Target framework is `net10.0-windows10.0.19041.0`. NuGet source must be configured: `dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org`.

The experiments project also requires `AllowUnsafeBlocks=true` (already in `experiments/experiments.csproj`). Root project does not use unsafe code.

## Goal & Status

Control the Guide-button LED brightness on an **Xbox Series X/S controller** (PID `0x0B13`, fw `0x0522`) connected via USB on Windows.

**STATUS: SOLVED for USB.** `WriteFile` to `\\.\XboxGIP` with the correct GIP format works without admin rights. BT LED control remains impossible via any known software path.

## Project Structure

```
XboxLedControl.csproj          Main app (clean, working only)
├── Program.cs                 CLI: parse [--debug] <brightness|pattern> → TrySend
├── GipLedCommand.cs           GIP frame builder, GipLedPattern enum, ScaleIntensity
├── GipDirectSender.cs         TrySend(rawGip, debug) — the one working path
└── NativeMethods.cs           kernel32 P/Invoke only (CreateFile, ReadFile, WriteFile, DeviceIoControl)

experiments/experiments.csproj  Dead-end probes and diagnostics
├── Program.cs                 Full CLI (--list, --probe, --ble-*, --nexus, --gip-list, --gip-send, --legacy-led)
├── GipDirectSender.cs         + ProbeFormats() with IOCTL sweep (unsafe)
├── NativeMethods.cs           Full P/Invoke: HID + SetupAPI + kernel32
├── XboxControllerFinder.cs    HID enumeration, XboxDevice record, ConnectionType enum
├── LedSender.cs               Send pipeline: HID → L2CAP → GipDirect → GipFactory → BLE GATT
├── BtSocketSender.cs          Raw L2CAP socket (dead end)
├── BleGattSender.cs           BLE GATT vendor service probe (dead end)
├── GipMessageSender.cs        Windows.Gaming.Input.Custom factory (dead end)
├── DevicesAbstractionSender.cs COM vtable P/Invoke for Devices.Abstraction.dll (dead end)
├── LegacyGipSender.cs         Windows.Gaming.Input.Preview COM vtable probe (dead end)
├── GipDeviceLister.cs         \\.\XboxGIP frame reader diagnostic
├── Package/                   MSIX AppxManifest + assets (gamingInputPreview + xboxAccessoryManagement)
├── pack-and-install.ps1       Build → stage → sign → install MSIX (root app)
├── pack-experiments.ps1       Build → stage → sign → install MSIX (experiments)
└── publish/                   Self-contained publish output (used by MSIX)

experiments/tmp/winmd_inspect/ WinMD inspector tool (dumps types, GUIDs, decoded method signatures)
```

## CLI

```
XboxLedControl [--debug] <brightness|pattern>

  --debug     Print device info, frame bytes, and send result to stdout.

  brightness  0–100  (scaled to 0–47 per §3.1.5.5.7)
  pattern     off | on | ramp | fastblink | slowblink | charging

Exit codes:  0 = success,  1 = failure
```

## Working Solution — USB via `\\.\XboxGIP`

`GipDirectSender.TrySend()` sends a 23-byte "read-symmetric" frame to `\\.\XboxGIP`:

```
[0-5]   Controller device ID (read from XboxGIP ReadFile frames)
[6-7]   00 00
[8]     0x0A  MessageType  (Command class bits7:5=000, message #10 bits4:0=01010)
[9]     0x20  Flags        (System=1, no ACK, not fragmented, primary device)
[10-11] 00 00 SequenceId=0 (seq=0x00 bypasses driver deduplication check)
[12-15] 03 00 00 00        PayloadLen = 3 (4-byte LE)
[16-19] 00 00 00 00
[20]    0x00  sub-command  (Guide Button LED)
[21]    pattern            (GipLedPattern enum, see below)
[22]    intensity          (0–47)
```

No administrator rights required. Works on first call and on every subsequent call.

**Why previous attempts failed:** All earlier probes used `MessageType=0x0E`, which does not exist in the MS-GIPUSB specification. The driver validates the GIP message type and returned `ERROR_INVALID_PARAMETER (87)` for every variant. Switching to the spec-correct `0x0A` immediately succeeded.

**Why seq=0x00:** The `MAC + raw GIP` format (seq=0x01) works on the first call per file handle but fails with `INVALID_PARAMETER` on a second call with the same seq. Using seq=0x00 in the read-symmetric layout bypasses the deduplication check entirely and works reliably every time.

## GIP LED Command Format — `GipLedCommand.cs`

Per [MS-GIPUSB] §3.1.5.5.7 (Table 41). The raw 7-byte GIP frame (`BuildRaw()`):

```
Byte 0: 0x0A           MessageType = Command class, message #10
Byte 1: 0x20           Flags: System=1, no ACK, not fragmented, primary device (expansion 0)
Byte 2: seq (1–255)    SequenceId, wrapping; 0x00 reserved
Byte 3: 0x03           PayloadLength = 3
Byte 4: 0x00           Sub-command = Guide Button LED
Byte 5: pattern        GipLedPattern enum value
Byte 6: intensity      0–47 (%)
```

`Build()` prepends a `0x00` HID report-ID byte → 8-byte buffer (used by experiments pipeline).

**GipLedPattern enum** (Table 42):

| Value  | Name           | On     | Cycle  | Notes                   |
|--------|----------------|--------|--------|-------------------------|
| `0x00` | Off            | —      | ∞      |                         |
| `0x01` | On             | ∞      | ∞      | Static solid            |
| `0x02` | FastBlink      | 200 ms | 400 ms |                         |
| `0x03` | SlowBlink      | 600 ms | 1.2 s  |                         |
| `0x04` | ChargingBlink  | 3 s    | 6 s    |                         |
| `0x0D` | RampToLevel    | —      | —      | Animate to intensity    |

**Intensity scaling:** user input 0–100 → `ScaleIntensity()` → 0–47.
`ScaleIntensity(b) = round(b × 47 / 100)`

**Flags = 0x20 decoded** (§2.2.10.2):
- bit 7 (Fragment) = 0 → single packet
- bit 6 (InitFrag) = 0 → N/A
- bit 5 (System)   = 1 → system message (no Metadata declaration needed)
- bit 4 (ACK)      = 0 → no acknowledgement required
- bits 2:0 (Exp)   = 000 → primary device

**MessageType encoding** (§2.2.10.1): bits 7:5 = Data Class, bits 4:0 = Message Number.
`0x0A = 0b00001010` → Command class (000) + message #10 (01010).

## Confirmed Dead Ends

**BT paths (all blocked):**
- **BT HID (`ig_00`):** `HID caps: input=16  output=0  feature=0` — input-only.
- **Raw L2CAP socket (`AF_BTH`, PSM `0x0013`):** WSA 10050 — Windows blocks user-mode access to already-connected BT HID PSMs.
- **BLE GATT writes to char `0x0004`:** All variants return `GattCommunicationStatus.Success` but LED does NOT change. Write-without-response; success = packet sent, not applied.
- **BLE GATT attribute-write theory:** `0x0002` is read-only telemetry; values don't change after writes to `0x0004`.
- **`Windows.Gaming.Input.Custom.GameControllerFactoryManager`:** BT controller → `XusbGameControllerProvider` (vibration only). `GipGameControllerProvider` requires USB (`xboxgip.sys`).
- **`Devices.Abstraction.winmd`:** `NETSDK1130` blocks WinMD reference in .NET 5+; classes unregistered outside Xbox Accessories App package context.

**Root cause (BT):** BT Xbox controllers on Windows are bridged via `BTHXHID.sys → xusb22.sys` as XUSB/HID — never present as GIP regardless of capabilities. GIP is USB-only (`xboxgip.sys`).

**`Windows.Gaming.Input.Preview.LegacyGipGameControllerProvider` (dead end):**
- Has `SetHomeLedIntensity(byte)` and `ExecuteCommand(DeviceCommand)` — but inaccessible to third-party apps.
- `DeviceCommand` enum has only `Reset=0`. No PowerOff value exists in the public API.
- `FromGameController` → `E_ACCESSDENIED (0x80070005)` even from MSIX with `gamingInputPreview` + `xboxAccessoryManagement` capabilities.
- QI for `ILegacyGipGameControllerProvider` directly on `RawGameController` → `E_NOINTERFACE (0x80004002)`.
- Root cause: Windows checks package Publisher identity (`CN=Microsoft Corporation` required), not just declared capabilities. Self-signed packages are rejected unconditionally.
- Probe code: `experiments/LegacyGipSender.cs`. MSIX script: `experiments/pack-experiments.ps1`.

**Key WinRT GUIDs discovered (recorded for reference):**
- `IRawGameControllerStatics`: `eb8d0792-e95a-4b19-afc7-0a59f8bf759e`
- `IGameController`: `1baf6522-5f64-42c5-8267-b9fe2215bfbd`
- `ILegacyGipGameControllerProviderStatics`: `d40dda17-b1f4-499a-874c-7095aac15291` (vtable[6]=FromGameController, vtable[7]=FromGameControllerProvider)
- `ILegacyGipGameControllerProvider`: `2da3ed52-ffd9-43e2-825c-1d2790e04d14` (vtable[15]=SetHomeLedIntensity, vtable[14]=ExecuteCommand)
- `IGipGameControllerProvider`: `dbcf1e19-1af5-45a8-bf02-a0ee50c823fc`
- `IGameControllerProvider`: `e6d73982-2996-4559-b16c-3e57d46e58d6`

**USB probing history (`\\.\XboxGIP` WriteFile):**
- Any buffer < 20 bytes → `ERROR_BUFFER_TOO_SMALL (122)`
- Buffer ≥ 20 bytes, no device ID prefix → `ERROR_DEVICE_NOT_CONNECTED (1167)`
- Buffer ≥ 20 bytes, real device ID, MessageType=0x0E (wrong) → `ERROR_INVALID_PARAMETER (87)` for every variant tried
- Buffer ≥ 20 bytes, real device ID, MessageType=**0x0A** (correct) → **OK** ← WORKS

**IOCTL access from user-mode (non-admin):**
- `0x40001CD0` (re-enumerate) → OK (no privileges needed)
- `0x40001CCC` → `ERROR_MORE_DATA`, bytesRet=0 (unknown semantics)
- All others `0x40001C80–0x40001D00` → `ACCESS_DENIED (5)`

## `\\.\XboxGIP` ReadFile Frame Format

```
[0-5]   Controller device ID (6 bytes; e.g. 7E ED 88 4A 67 9D)
[6-7]   00 00
[8]     MessageType (0x02=DeviceInfo, 0x03=Heartbeat, 0x04=Announce)
[9]     Flags = 0x20
[10-11] 00 00
[12-15] PayloadLen (4-byte LE)
[16-19] 00 00 00 00
[20+]   Payload
```

Announce frame (0x04): contains VID=0x045E + PID + "Windows.Xbox.Input.Gamepad" string.

## BLE GATT Vendor Service (Xbox Series X/S, fw 0x0522)

```
Service:  00000001-5f60-4c4f-9c83-a7953298d40d
  0x0002  Read   — 6-byte TLV: key 0x0010=brightness, 0x0011=LedMode, 0x0038=max_brightness
  0x0003  Read   — device info string
  0x0004  Write  — accepts all payloads silently (no effect on LED)
```

## Protocol Reference

[MS-GIPUSB] Game Input Protocol USB Transport:
https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gipusb

Key sections:
- §2.2.10   Message Header (4-byte GIP header structure)
- §2.2.10.1 MessageType encoding (bits 7:5 = Data Class, bits 4:0 = Message Number)
- §2.2.10.2 Flags field (Fragment, System, ACK, Expansion Index)
- §3.1.5.4  Message Summary — full table of all GIP command types
- §3.1.5.5.7 LED eButton Command (Table 41: frame format, Table 42: patterns)
- §3.1.5.5.8 LED IR Command (deprecated, SubCmd=0x01, PayloadLen=6)
