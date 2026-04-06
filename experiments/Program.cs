using XboxLedControl;

/*
 * Xbox Controller LED Control — Experiments
 * ==========================================
 * Sends GIP Guide Button LED command to an Xbox One / Series controller.
 * Command format: MS-GIPUSB §3.1.5.5.7 — MessageType=0x0A, 3-byte payload.
 *
 * ⚠  USB WARNING
 * When connected via USB, Windows loads xusb22.sys which intercepts GIP.
 * HID output reports appear to succeed but the LED is NOT changed.
 * USB LED control requires WinUSB / an admin IOCTL (see --gip-send).
 *
 * Usage:
 *   XboxLedControlExperiments [--list]
 *   XboxLedControlExperiments [--probe] <brightness|pattern>
 *
 *   --list         Show all detected Xbox HID interfaces and exit.
 *   --probe        Try all send methods on every interface.
 *   --ble-read     Read Xbox vendor GATT chars and try LED payload variants.
 *   --ble-tlv      Dump full TLV table from char 0x0002 with key labels.
 *   --ble-monitor  Poll char 0x0002 every 1 s (Ctrl+C to stop).
 *   --nexus        Call INexusApi.SetTempNexusBrightness via Devices.Abstraction.
 *   --gip-list     Open \\.\XboxGIP (USB only), dump raw GIP frames.
 *   --gip-send     Probe \\.\XboxGIP write/IOCTL paths with spec-correct LED frame.
 *                  Run as Administrator to unlock IOCTL 0x40001CD4.
 *   --gip-crc      Same flow as root TrySend but sends ONLY the 25B CRC-16-CCITT frame
 *                  (no prior 23B working frame). Tests BE and LE CRC on one handle.
 *
 *   brightness     0–100  (scaled to 0–47 per §3.1.5.5.7)
 *   pattern        off | on | ramp | fastblink | slowblink | charging
 */

bool listOnly   = args.Any(a => a.Equals("--list",        StringComparison.OrdinalIgnoreCase));
bool probe      = args.Any(a => a.Equals("--probe",       StringComparison.OrdinalIgnoreCase));
bool bleRead    = args.Any(a => a.Equals("--ble-read",    StringComparison.OrdinalIgnoreCase));
bool bleTlv     = args.Any(a => a.Equals("--ble-tlv",     StringComparison.OrdinalIgnoreCase));
bool bleAttr    = args.Any(a => a.Equals("--ble-attr",    StringComparison.OrdinalIgnoreCase));
bool bleMonitor = args.Any(a => a.Equals("--ble-monitor", StringComparison.OrdinalIgnoreCase));
bool nexusTest  = args.Any(a => a.Equals("--nexus",       StringComparison.OrdinalIgnoreCase));
bool gipList    = args.Any(a => a.Equals("--gip-list",    StringComparison.OrdinalIgnoreCase));
bool gipSend    = args.Any(a => a.Equals("--gip-send",    StringComparison.OrdinalIgnoreCase));
bool gipCrc     = args.Any(a => a.Equals("--gip-crc",     StringComparison.OrdinalIgnoreCase));
bool legacyLed  = args.Any(a => a.Equals("--legacy-led",  StringComparison.OrdinalIgnoreCase));
string[] valueArgs = args.Where(a => !a.StartsWith("--")).ToArray();

Console.WriteLine("Xbox Controller LED Control — Experiments");
Console.WriteLine("Protocol: MS-GIPUSB §3.1.5.5.7 — MessageType=0x0A, Guide Button LED");
Console.WriteLine();

// ── GIP device listing (USB-only path, no HID scan needed) ───────────────

if (gipList)
{
    GipDeviceLister.List();
    return 0;
}

if (gipSend)
{
    GipLedPattern p = GipLedPattern.On;
    byte          i = 47;
    if (valueArgs.Length >= 1) (p, i) = ParseArg(valueArgs[0]);
    GipDirectSender.ProbeFormats(p, i);
    return 0;
}

if (gipCrc)
{
    GipLedPattern p = GipLedPattern.On;
    byte          i = 47;
    if (valueArgs.Length >= 1) (p, i) = ParseArg(valueArgs[0]);
    Console.WriteLine($"CRC-16-CCITT experiment: pattern={p}, intensity={i}/47");
    Console.WriteLine("(No working 23B frame sent before — pure CRC path)");
    Console.WriteLine();
    GipDirectSender.TrySendCrc(p, i);
    return 0;
}

if (legacyLed)
{
    byte legacyIntensity = 23; // ~50% on 0-47 scale
    if (valueArgs.Length >= 1 && byte.TryParse(valueArgs[0], out byte v))
        legacyIntensity = v;
    Console.WriteLine($"LegacyGipGameControllerProvider.SetHomeLedIntensity({legacyIntensity})");
    Console.WriteLine("(Windows.Gaming.Input.Preview — Preview WinRT API)");
    Console.WriteLine();
    LegacyGipSender.TrySend(legacyIntensity);
    return 0;
}

// ── Find devices ─────────────────────────────────────────────────────────

Console.WriteLine("Scanning HID devices...");
var devices = XboxControllerFinder.Find();

if (devices.Count == 0)
{
    Console.WriteLine("No Xbox controller HID interfaces found.");
    Console.WriteLine("Make sure the controller is paired and connected.");
    return 1;
}

// ── Print device table ────────────────────────────────────────────────────

Console.WriteLine($"Found {devices.Count} Xbox HID interface(s):\n");
Console.WriteLine($"  {"#",-3} {"Conn",-4} {"PID",-8} {"Name",-36} Path");
Console.WriteLine($"  {new string('-', 90)}");

for (int i = 0; i < devices.Count; i++)
{
    var d = devices[i];
    string connStr = d.Connection switch
    {
        ConnectionType.Bluetooth => "BT",
        ConnectionType.Usb       => "USB",
        _                        => "?",
    };
    Console.WriteLine($"  {i,-3} {connStr,-4} 0x{d.ProductId:X4}  {d.ProductName,-36} {d.Path}");
}

if (listOnly) return 0;

// ── BLE / nexus probes ────────────────────────────────────────────────────

if (nexusTest)
{
    byte nexusBrt = valueArgs.Length > 0 && byte.TryParse(valueArgs[0], out byte nb) ? nb : (byte)50;
    Console.WriteLine($"Testing INexusApi.SetTempNexusBrightness({nexusBrt})...\n");
    bool ok = await DevicesAbstractionSender.TrySendAsync(nexusBrt);
    return ok ? 0 : 3;
}

if (bleRead || bleTlv || bleAttr || bleMonitor)
{
    var btDevices = devices.Where(d => d.Connection == ConnectionType.Bluetooth).ToList();
    if (btDevices.Count == 0)
    {
        Console.WriteLine("No Bluetooth-connected Xbox controller found.");
        return 1;
    }
    foreach (var dev in btDevices)
    {
        Console.WriteLine($"\nProbing vendor GATT service: {dev.ProductName}  PID=0x{dev.ProductId:X4}");
        if (bleTlv)
            await BleGattSender.DumpTlvAsync(dev);
        else if (bleAttr)
            await BleGattSender.ProbeAttrAsync(dev);
        else if (bleMonitor)
            await BleGattSender.MonitorAsync(dev);
        else
            await BleGattSender.ProbeVendorServiceAsync(dev);
    }
    return 0;
}

Console.WriteLine();

// ── Parse LED argument ────────────────────────────────────────────────────

GipLedPattern pattern;
byte          intensity;

if (valueArgs.Length >= 1)
{
    (pattern, intensity) = ParseArg(valueArgs[0]);
}
else
{
    Console.WriteLine("Options:  0–100 (brightness)  |  off  on  ramp  fastblink  slowblink  charging");
    Console.Write("Enter brightness or pattern: ");
    string? input = Console.ReadLine()?.Trim() ?? "0";
    (pattern, intensity) = ParseArg(input);
}

Console.WriteLine($"Setting: pattern={pattern}, intensity={intensity}/47\n");

// ── Send command ──────────────────────────────────────────────────────────

int sent = 0;

foreach (var dev in devices)
{
    string connLabel = dev.Connection switch
    {
        ConnectionType.Bluetooth => "[BT ]",
        ConnectionType.Usb       => "[USB]",
        _                        => "[?  ]",
    };

    Console.WriteLine($"{connLabel} {dev.ProductName}  PID=0x{dev.ProductId:X4}");
    Console.WriteLine($"       {dev.Path}");

    if (probe)
    {
        LedSender.Probe(dev, pattern, intensity);
    }
    else
    {
        byte[] cmd = GipLedCommand.Build(pattern, intensity);
        Console.WriteLine($"       GIP frame: {BitConverter.ToString(cmd)}");
        if (LedSender.Send(dev, cmd)) sent++;
    }

    Console.WriteLine();
}

if (!probe && sent == 0)
{
    Console.WriteLine("No commands delivered.");
    if (!devices.Any(d => d.Connection == ConnectionType.Bluetooth))
        Console.WriteLine("Connect the controller via Bluetooth and try again.");
    return 2;
}

if (!probe)
    Console.WriteLine($"Command sent to {sent} interface(s).");

return 0;

// ── helpers ───────────────────────────────────────────────────────────────

static (GipLedPattern pattern, byte intensity) ParseArg(string arg) =>
    arg.Trim().ToLowerInvariant() switch
    {
        "off"  or "0"                           => (GipLedPattern.Off,           0),
        "on"   or "solid"                       => (GipLedPattern.On,           47),
        "ramp" or "breathe" or "breath" or "fade"
                                                => (GipLedPattern.RampToLevel,  47),
        "fastblink" or "blink" or "blink1"      => (GipLedPattern.FastBlink,    47),
        "slowblink" or "slow"  or "blink2"      => (GipLedPattern.SlowBlink,    47),
        "charging"  or "charge"                 => (GipLedPattern.ChargingBlink,47),
        "full" or "max" or "100"                => (GipLedPattern.On,           47),
        _ when byte.TryParse(arg, out byte b)   => b == 0
                                                    ? (GipLedPattern.Off, (byte)0)
                                                    : (GipLedPattern.On, GipLedCommand.ScaleIntensity(b)),
        _                                       => FallbackOff(arg),
    };

static (GipLedPattern, byte) FallbackOff(string a)
{
    Console.Error.WriteLine($"Unknown argument '{a}', defaulting to off.");
    return (GipLedPattern.Off, 0);
}
