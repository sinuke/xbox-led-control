# Xbox Controller Guide LED — Research Notes

Target: Xbox Series X/S controller (VID 0x045E, PID 0x0B13, fw 0x0522)
Goal: Control Guide button LED brightness from Windows user-mode software
Result: **SOLVED for USB via `\\.\XboxGIP`** (as of 2026-04)

---

## Solution

`WriteFile` to `\\.\XboxGIP` with a 23-byte read-symmetric GIP frame controls the LED
without admin rights. See `GipDirectSender.cs`.

Key insight: all previous attempts used `MessageType=0x0E` (does not exist in MS-GIPUSB spec).
The driver validates MessageType and returned `ERROR_INVALID_PARAMETER` for every variant.
Spec-correct `MessageType=0x0A` works immediately.

```
[0-5]   Controller device ID (from ReadFile frames)
[6-7]   00 00
[8]     0x0A  MessageType (Command class, message #10)
[9]     0x20  Flags (System, no ACK, primary device)
[10-11] 00 00 seq=0x00 (bypasses driver deduplication check)
[12-15] 03 00 00 00  PayloadLen (4-byte LE)
[16-19] 00 00 00 00
[20]    0x00  sub-command (Guide Button LED)
[21]    pattern  (GipLedPattern enum)
[22]    intensity (0–47)
```

---

## Hardware & Driver Stack

### Bluetooth connection
```
Xbox Series X/S (BT HID profile)
  └─ bthxhid.sys  (Bluetooth HID class driver)
       └─ xusb22.sys  (XUSB protocol, bridged from BT HID)
            └─ Windows.Gaming.Input → XusbGameControllerProvider (vibration only)
```
HID capabilities: `input=16  output=0  feature=0` — input-only, no output path.

### USB connection (Xbox Series X/S, PID 0x0B13)
```
Xbox Series X/S (USB)
  └─ xusb22.sys  (XUSB protocol) ← HID interface
  └─ xboxgip.sys (GIP protocol)  ← \\.\XboxGIP interface  ← LED CONTROL WORKS HERE
```
HID output caps = 0. GIP path via `\\.\XboxGIP` is the working solution.

### Xbox Wireless Adapter (2.4 GHz dongle)
```
Xbox Series X/S (wireless dongle)
  └─ xboxgip.sys  (GIP protocol)
       └─ XboxGipSvc
            └─ Devices.Abstraction.dll → ControllerType.GIP → INexusApi accessible
```
With the wireless adapter `--nexus` (NexusApi) and GameInput `SendRawDeviceOutput` should work.
Not tested (no dongle available).

---

## GIP LED Command Format (MS-GIPUSB §3.1.5.5.7)

### Correct 7-byte raw GIP frame

| Byte | Value      | Field        | Notes |
|------|------------|--------------|-------|
| 0    | `0x0A`     | MessageType  | Command class (bits7:5=000), message #10 (bits4:0=01010) |
| 1    | `0x20`     | Flags        | System=1, no ACK, not fragmented, primary device |
| 2    | `0x01–0xFF`| SequenceId   | Wrapping; 0x00 reserved |
| 3    | `0x03`     | PayloadLen   | 3 bytes |
| 4    | `0x00`     | Sub-command  | 0x00 = Guide Button LED |
| 5    | variable   | Pattern      | GipLedPattern enum |
| 6    | `0–47`     | Intensity    | 0–47% |

### GipLedPattern enum (Table 42)

| Value  | Name           | On     | Cycle |
|--------|----------------|--------|-------|
| `0x00` | Off            | —      | ∞     |
| `0x01` | On             | ∞      | ∞     |
| `0x02` | FastBlink      | 200 ms | 400 ms |
| `0x03` | SlowBlink      | 600 ms | 1.2 s |
| `0x04` | ChargingBlink  | 3 s    | 6 s   |
| `0x0D` | RampToLevel    | —      | —     |

### Old (wrong) format used before fix
```
MessageType = 0x0E  ← NOT IN SPEC → driver returns ERROR_INVALID_PARAMETER (87)
PayloadLen  = 0x08 0x00 (2-byte LE) ← wrong
Payload     = [mode, brightness, period, R, G, B, onPeriod, offPeriod] ← wrong
```

---

## `\\.\XboxGIP` WriteFile — Probing History

| Buffer | Result |
|--------|--------|
| < 20 bytes | `ERROR_BUFFER_TOO_SMALL (122)` |
| ≥ 20 bytes, no device ID | `ERROR_DEVICE_NOT_CONNECTED (1167)` |
| ≥ 20 bytes, real device ID, `MessageType=0x0E` | `ERROR_INVALID_PARAMETER (87)` — all variants |
| ≥ 20 bytes, real device ID, `MessageType=0x0A` | **OK** ← WORKS |

Why seq=0x00: `MAC + raw GIP` with seq=0x01 fails on second call with same seq (driver dedup).
Read-symmetric layout with seq=0x00 bypasses this check — works every call.

IOCTL access (non-admin):
- `0x40001CD0` re-enumerate → OK
- `0x40001CCC` → `ERROR_MORE_DATA`, bytesRet=0 (unknown)
- All others `0x40001C80–0x40001D00` → `ACCESS_DENIED`

---

## BLE GATT Vendor Service (Xbox Series X/S, fw 0x0522)

```
Service UUID:  00000001-5f60-4c4f-9c83-a7953298d40d
  Char 0x0002  Read   — 6-byte TLV config table (read-only telemetry)
  Char 0x0003  Read   — device info string
  Char 0x0004  Write  — accepts all payloads silently (no effect on LED)
```

### Char 0x0002 TLV format
```
Key 0x0010 = brightness      (current LED brightness)
Key 0x0011 = LedMode         (current mode)
Key 0x0038 = max_brightness  (ceiling, usually 100)
```
Read-only. Writing to 0x0004 does NOT update these values.

All 10 GIP payload variants tried — all returned `GattCommunicationStatus.Success`, LED unchanged.
`Success` = BLE packet accepted by radio. Write-without-response = no ACK from firmware.

---

## Dead Ends Summary

### BT paths (all blocked)

| Method | Result | Reason |
|--------|--------|--------|
| HID Output Report (`HidD_SetOutputReport`) | caps output=0 | No output interface on BT |
| HID Feature Report (`HidD_SetFeature`) | caps feature=0 | No feature interface |
| Raw L2CAP socket (`AF_BTH`, PSM 0x0013) | WSA 10050 | Windows blocks user-mode access to connected BT HID PSMs |
| BLE GATT write (char 0x0004) | Success, no effect | Write-without-response, firmware ignores it |
| `GameControllerFactoryManager` (BT) | `XusbGameControllerProvider` | Vibration only, no LED method |
| `Devices.Abstraction` NexusApi (BT) | `0x80070490` NOT_FOUND | GIP-only, BT controller is XUSB |

**Root cause:** BT Xbox controllers → `BTHXHID.sys → xusb22.sys` (XUSB). Never GIP. GIP is USB-only (`xboxgip.sys`).

### USB HID path (dead end)
- `HidD_SetOutputReport` on USB: returns OK, driver accepts silently, LED unchanged.
- HID output caps = 0 anyway.

### Windows.Gaming.Input.Custom (USB, no MSIX)
- `GipGameControllerProvider` received, `SendMessage(Command, 0x0E, ...)` — no LED change.
- Message uses old wrong MessageType=0x0E.

### Devices.Abstraction.dll (MSIX required)
- `NETSDK1130` blocks WinMD reference in .NET 5+, P/Invoke COM vtable used instead.
- BT controller: `ControllerType.HID`, `get_NexusApi()` → `0x80070490` NOT_FOUND.
- USB controller: `GetReadyControllersAsync()` → 0 controllers (uses xusb22.sys, not GIP).
- `CreateForAccessoryId()` → null sender (BT HID not a GIP target).

### Windows.Gaming.Input.Preview.LegacyGipGameControllerProvider (dead end)

Discovered via WinMD analysis of `Windows.Gaming.winmd`. Has two relevant methods:
- `SetHomeLedIntensity(byte intensity)` — direct LED brightness control
- `ExecuteCommand(DeviceCommand command)` — `DeviceCommand` enum has only `Reset=0`; no PowerOff value

`ILegacyGipGameControllerProviderStatics` (GUID `d40dda17-b1f4-499a-874c-7095aac15291`):
- `FromGameController(IGameController)` [vtable[6]]
- `FromGameControllerProvider(IGameControllerProvider)` [vtable[7]]

`ILegacyGipGameControllerProvider` (GUID `2da3ed52-ffd9-43e2-825c-1d2790e04d14`):
- `SetHomeLedIntensity(byte)` [vtable[15]]
- `ExecuteCommand(DeviceCommand)` [vtable[14]]

Tested via `experiments/LegacyGipSender.cs` using raw COM vtable P/Invoke
(same pattern as `DevicesAbstractionSender.cs`):

| Attempt | Method | Result |
|---------|--------|--------|
| [A] | QI `ILegacyGipGameControllerProvider` directly on `RawGameController` | `E_NOINTERFACE (0x80004002)` |
| [B] | `FromGameController(IGameController)` | `E_ACCESSDENIED (0x80070005)` |
| [C] | `FromGameControllerProvider(IGameController as provider)` | `E_ACCESSDENIED (0x80070005)` |

Tested from:
- Non-packaged `dotnet run` — `E_ACCESSDENIED`
- MSIX with `gamingInputPreview` + `xboxAccessoryManagement` + `runFullTrust` capabilities — still `E_ACCESSDENIED`
- MSIX run as Administrator — still `E_ACCESSDENIED`

**Root cause:** Windows checks package Publisher identity at the kernel/broker level, not just
capability strings. Only packages signed by `CN=Microsoft Corporation` can call these APIs.
The restriction is not bypassable via self-signed developer packages.

### `GipMessageSender.DummyInspectable` quirk
`CreateGameController` callback must return `Windows.Foundation.PropertyValue.CreateInt32(0)`,
NOT `null` — native WinRT dereferences the return value immediately → 0xC0000005 crash if null.

---

## Unexplored Approaches (driver-level or hardware)

1. **Xbox Wireless Adapter (dongle)** — uses `xboxgip.sys` → GIP path open.
   - `--nexus` (NexusApi via Devices.Abstraction, requires MSIX) — should work
   - GameInput v3 `SendRawDeviceOutput` (no MSIX, no capability) — should work
   - Neither tested (no dongle available)

2. **WinUSB on USB** — replace `xusb22.sys` with WinUSB (via Zadig), send raw GIP frames.
   Breaks controller as a standard game controller.

3. **IOCTL sniffing** — capture IOCTLs from XboxGipSvc to `xboxaccessories.sys` for a GIP
   accessory making LED changes, replicate from user mode.

4. **`IGameControllerProviderPrivate::PowerOff`** — private interface QI-able from public
   `IGameControllerProvider`. Confirmed by MS engineer (Habr article, Александр Прошанов,
   https://habr.com/p/1018726/). GUID not in any public WinMD or SDK header.
   Binary scan of `Windows.Gaming.Input.dll` found candidate GUID in the GIP interface GUID
   cluster: `7500a9a0-f41d-cb6b-eb71-296efb0eb448` (at 0xA4CC0, between
   `IGipGameControllerProvider` at 0xA4CA0 and `IXusbGameControllerProvider` at 0xA4CC8).
   Unverified — QI probe would require obtaining a `GipGameControllerProvider` first.

---

## Project File Map

```
D:\xbox.led.control\
├── XboxLedControl.csproj        Main app (clean)
├── Program.cs                   CLI: [--debug] <brightness|pattern>
├── GipLedCommand.cs             GIP frame builder, GipLedPattern enum
├── GipDirectSender.cs           TrySend() — the one working path
├── NativeMethods.cs             kernel32 P/Invoke only
├── CLAUDE.md                    Build/run instructions, architecture notes
├── RESEARCH.md                  This file
│
└── experiments/
    ├── experiments.csproj       Experiments project (AllowUnsafeBlocks, links ../GipLedCommand.cs)
    ├── Program.cs               Full CLI (--list --probe --ble-* --nexus --gip-list --gip-send)
    ├── GipDirectSender.cs       + ProbeFormats(), IOCTL sweep (unsafe)
    ├── NativeMethods.cs         Full P/Invoke: HID + SetupAPI + kernel32
    ├── XboxControllerFinder.cs  HID enumeration, XboxDevice, ConnectionType
    ├── LedSender.cs             Full send pipeline (all methods)
    ├── BtSocketSender.cs        Raw L2CAP socket (WSA 10050 — dead end)
    ├── BleGattSender.cs         BLE GATT probe (Success returned, no effect — dead end)
    ├── GipMessageSender.cs      Windows.Gaming.Input.Custom (dead end)
    ├── DevicesAbstractionSender.cs  COM vtable P/Invoke for Devices.Abstraction.dll (dead end)
    ├── LegacyGipSender.cs       Windows.Gaming.Input.Preview COM vtable probe (dead end)
    ├── GipDeviceLister.cs       \\.\XboxGIP frame reader diagnostic
    ├── Package/                 MSIX AppxManifest + assets (gamingInputPreview + xboxAccessoryManagement)
    ├── pack-and-install.ps1     Build → stage → sign → install MSIX (root app)
    ├── pack-experiments.ps1     Build → stage → sign → install MSIX (experiments)
    ├── publish/                 Self-contained publish output (used by MSIX)
    └── tmp/winmd_inspect/       WinMD inspector: dumps types, GUIDs, decoded method signatures
```

---

## CLI Reference

### Main app
```
XboxLedControl.exe [--debug] <brightness|pattern>

  brightness  0–100
  pattern     off | on | ramp | fastblink | slowblink | charging
  --debug     verbose output (device ID, frame bytes, send result)
```

### Experiments
```
XboxLedControlExperiments.exe --list           enumerate HID interfaces
XboxLedControlExperiments.exe --probe 50       try all send methods
XboxLedControlExperiments.exe 50               set brightness via full pipeline
XboxLedControlExperiments.exe --ble-read       probe BLE GATT vendor service
XboxLedControlExperiments.exe --ble-attr       test attribute-write theory on BLE
XboxLedControlExperiments.exe --ble-monitor    poll char 0x0002 every 1s
XboxLedControlExperiments.exe --nexus 50       try INexusApi via Devices.Abstraction
XboxLedControlExperiments.exe --gip-list       dump \\.\XboxGIP frames
XboxLedControlExperiments.exe --gip-send 50    probe all XboxGIP write formats
XboxLedControlExperiments.exe --legacy-led 23  probe LegacyGipGameControllerProvider (dead end)
```
