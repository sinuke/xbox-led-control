# Xbox Controller LED Control Project

## Project Location
`D:/_xbox_controller_led/`

## Goal
Control brightness/state of Xbox controller Guide button LED on Windows via GIP protocol.
Target: Xbox Series X/S PID 0x0B13, fw 0x0522, connected via Bluetooth.

## Stack
- .NET 10 (`net10.0-windows10.0.19041.0`)
- Windows HID API (P/Invoke — no NuGet deps)
- MSIX packaged app (required for `xboxAccessoryManagement` capability)
- Build: `"C:\Program Files\dotnet\dotnet.exe" build XboxLedControl.csproj -c Release`
- Pack+Install: `pwsh -NoProfile -ExecutionPolicy Bypass -File pack-and-install.ps1` (admin)
- Run: `XboxLedControl.exe [brightness|mode|--nexus|--list|...]`

## Protocol: GIP LED Command

### Windows HID tunnel format (MS-GIPUSB, current GipLedCommand.cs)
Reference: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gipusb

0x0E here is likely the **HID Report ID** for GIP tunnel (not the GIP command itself).
13-byte HID buffer (byte 0 = report-ID prefix stripped by driver):
```
[0]  0x00        HID report-ID prefix (Win32 only)
[1]  0x0E        HID Report ID = GIP tunnel (NOT raw GIP command)
[2]  seqNum
[3]  0x08  [4] 0x00  Payload length (2-byte LE)
[5]  mode        LedMode enum (0=Off, 3=Solid, 4=Breathe, 5=SlowBreathe, 6=BlinkSolid, 7=Blink1, 8=Blink2)
[6]  brightness  0–100
[7]  0xFF        Period
[8-10] R G B
[11] 0xFF  [12] 0x00  OnPeriod / OffPeriod
```

### Raw GIP wire format (xone/xow Linux drivers — confirmed working over dongle/USB)
Sources: xone `bus/protocol.h` + `bus/protocol.c`, xow `controller/gip.h`
```
[0]  0x0A        GIP_CMD_LED (raw GIP command — NOT 0x0E!)
[1]  options     clientId | 0x20
[2]  sequence
[3]  0x03        payload length (3 bytes)
[4]  0x00        unknown, always 0
[5]  mode        LedMode enum (see below)
[6]  brightness  0–50 (GIP_LED_BRIGHTNESS_MAX=50, default=20)
```
LedMode: OFF=0x00, ON=0x01, BLINK_FAST=0x02, BLINK_NORMAL=0x03, BLINK_SLOW=0x04, FADE_SLOW=0x08, FADE_FAST=0x09

**Key discrepancy:** current GipLedCommand.cs uses cmd=0x0E, payload=8 bytes, brightness 0–100.
xone/xow use cmd=0x0A, payload=3 bytes, brightness 0–50.
When testing with dongle+NexusApi, if SendGipCommand takes raw GIP frames → use xone format (0x0A).

## Confirmed Dead Ends
- USB (xusb22.sys): LED reports succeed but no effect. Requires WinUSB/private IOCTL.
- BT HID ig_00: input-only (caps output=0). No output path.
- Raw L2CAP (AF_BTH PSM 0x0013): WSA 10050, Windows blocks user-mode access to connected HID PSMs.
- BLE GATT char 0x0004 writes: All 10 GIP payload variants return Success but LED doesn't change.
- BLE GATT attribute-write theory: char 0x0002 values (brightness/mode) don't change after 0x0004 writes.
- Windows.Gaming.Input.Custom: BT → XusbGameControllerProvider only. GipGameControllerProvider requires capability.
- Devices.Abstraction NexusApi (--nexus): BT HID → get_NexusApi returns 0x80070490 (GIP-only).
- Devices.Abstraction MessageApi: BT HID → get_MessageApi returns 0x80070490 (GIP-only).
- IMessageSenderStatics.CreateForAccessoryId: returns hr=0, sender=NULL for BT HID (not a GIP target).
- Xbox Accessories App: confirmed cannot change LED brightness on Windows (BT or USB).

## Root Cause (Definitive)
BT Xbox controllers on Windows use BTHXHID.sys → xusb22.sys bridge = XUSB/HID.
They NEVER present as GIP regardless of capability. GIP requires USB (xboxgip.sys).
All LED-control APIs (INexusApi, IMessageApi, IMessageSender) are GIP-only.

## Devices.Abstraction.dll MSIX Setup
- Requires MSIX with xboxAccessoryManagement + runFullTrust capabilities
- AppxManifest registers all activatable classes as inProcessServer (see Package/AppxManifest.xml)
- pack-and-install.ps1 copies Devices.Abstraction.dll from Microsoft.XboxDevices AppX
- pack-and-install.ps1 copies msvcp140_app.dll, vcruntime140_app.dll, vcruntime140_1_app.dll from Microsoft.VCLibs.140.00 x64
- PackageDependency: Microsoft.VCLibs.140.00 (NOT .UWPDesktop)

## Devices.Abstraction IController vtable (confirmed from WinMD)
- [6]  get_AccessoryId → HSTRING
- [34] get_MessageApi → 0x80070490 for HID (GIP-only)
- [40] get_NexusApi   → 0x80070490 for HID (GIP-only)
- [46] get_HidControllerApi → get_HidProvider only (no LED methods)
- MessageClass.GipCommand=4096, MessageTransport.GIP=2, MessageTransport.IOCTL=1

## BLE GATT Vendor Service (confirmed fw 0x0522)
```
Service: 00000001-5f60-4c4f-9c83-a7953298d40d
  0x0002  Read  — 6-byte TLV: key 0x0010=brightness(98), 0x0011=LedMode(3), 0x0038=max_brt(100)
  0x0003  Read  — device info
  0x0004  Write — accepts silently, no LED effect
```

## USB Path Result (DEAD END — Xbox One only)
USB Xbox One 0x02FF: ControllerFactory.GetReadyControllersAsync → 0 controllers.
NOTE: tested only on Xbox One (0x02FF), NOT on target Series X/S (0x0B13)!

## USB cable with Series X/S (0x0B12) — DEAD END (tested)
HID caps: input=16, output=0, feature=0 — same as Xbox One, no output path.
USB PID = 0x0B12 (hardware), exposed as 0x02FF by xusb22.sys (HID child).
CompatibleIds include USB\MS_COMP_XGIP10 — device DECLARES GIP 1.0 support!
BUT: XboxCompositeDevice creates only one child (IG_00/HID). No GIP device node created.
XboxGipSvc is Running but does NOT force-create GIP interface for direct USB cable.
→ "по проводу USB команды срабатывают" (Habr author) likely means via USB Wireless Adapter dongle, NOT cable.

## Promising Untried: Xbox Wireless Adapter (2.4GHz dongle)
Habr article (https://habr.com/p/1018726/) confirms: wireless dongle uses xboxgip.sys → ControllerType.GIP.
Habr author is currently investigating this path.
`ControllerFactory` WILL find the controller, `get_NexusApi` should NOT return 0x80070490.
→ Test `XboxLedControl.exe --nexus 50` with controller paired via Xbox Wireless Adapter dongle.

## GipGameControllerProvider.SendMessage — timing issue (from Habr author)
Controller appears AFTER app starts even if already connected. Must subscribe to
`GameController.GameControllerAdded` event — do NOT try to get controller synchronously at startup.
Windows.Gaming.Input.Custom is designed for custom/accessory gamepads, may have limited support for standard controllers.

## BT LED — confirmed different transport (Habr author)
BT controller data "either encrypted or transmitted differently" vs USB/dongle GIP.
This explains why GATT char 0x0004 accepts all writes silently — BT LED control
is not routed through the same GIP mechanism at all.

## IGameControllerProviderPrivate (from Habr/Microsoft Support)
Private interface QI-able from public IGameControllerProvider.
Has PowerOff() and possibly LED methods. IID not published — needs WinMD/binary reverse.

## GameInput API — IGameInputDevice::SendRawDeviceOutput (v3)
NuGet: Microsoft.GameInput (C++ native only, no .NET projection — needs COM P/Invoke)
Runtime: GameInputRedist.msi / `winget install Microsoft.GameInput`
IGameInputDevice IID:          63E2F38B-A399-4275-8AE7-D4C6E524D12A
IGameInputRawDeviceReport IID: 05A42D89-2CB6-45A3-874D-E635723587AB
- BT controller: DEAD END — same output=0 wall, GameInput is a wrapper over same HID stack
- USB cable: likely same dead end (xusb22.sys, output=0)
- Xbox Wireless Adapter dongle: PROMISING — xboxgip.sys → GIP device → outputReportCount > 0
  Advantage over NexusApi: NO xboxAccessoryManagement capability, NO MSIX required!
  Flow: CreateRawDeviceReport(reportId, GameInputRawOutputReport) → SetRawData → SendRawDeviceOutput
  v3 changelog: "Added support for GIP raw device reports" — explicitly supports this path

## Other Next Steps
- !! Xbox Accessories App CANNOT change LED brightness on Windows — no traffic to capture, Wireshark useless !!
- ETW/kernel trace on xboxaccessories.sys IRP stack (only if some GIP app triggers LED, e.g. on Xbox console)
- Reverse IGameControllerProviderPrivate IID from xboxgip.sys or XboxGipSvc.dll

## Key Files
- `DevicesAbstractionSender.cs` — COM vtable P/Invoke for Devices.Abstraction.dll (--nexus path)
- `NativeMethods.cs`            — P/Invoke (HID, SetupAPI, Kernel32)
- `GipLedCommand.cs`            — 13-byte GIP command, LedMode enum
- `XboxControllerFinder.cs`     — HID device enumeration (IntPtr not StringBuilder for BT product name!)
- `LedSender.cs`                — send pipeline (HID output → HID feature → L2CAP → GipFactory → BLE GATT)
- `BleGattSender.cs`            — BLE GATT send + ProbeVendorServiceAsync + MonitorAsync
- `Program.cs`                  — CLI: --list --probe --ble-read --ble-attr --ble-monitor --nexus + args
- `Package/AppxManifest.xml`    — MSIX manifest with capabilities and activatable class registrations
- `pack-and-install.ps1`        — builds, packs, signs, installs MSIX

## Critical Implementation Notes
- GipMessageSender.CreateGameController must return Windows.Foundation.PropertyValue.CreateInt32(0), NOT null
- async methods cannot have out parameters in C# — use tuple return instead
- BT HID paths contain &ig_00 but DO NOT use ig_ as USB indicator
- PID 0x02FF = Xbox One For Windows (unlisted PID, matched by product name)
