using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace XboxLedControl;

/// <summary>
/// Sends GIP LED commands via the Xbox vendor-specific BLE GATT service.
///
/// Discovery summary (Xbox Series X/S, PID 0x0B13, fw 0x0522)
/// ────────────────────────────────────────────────────────────
/// The controller exposes 6 GATT services.  The HID service (0x1812) is
/// reserved by Windows (AccessDenied).  LED control goes through a
/// proprietary Microsoft/Xbox vendor service:
///
///   Service UUID : 00000001-5f60-4c4f-9c83-a7953298d40d
///
///   0x0002  Read   — unknown (possibly config/capabilities)
///   0x0003  Read   — unknown
///   0x0004  Write  ← LED / GIP command channel  ✓
///
/// Payload written to 0x0004: raw GIP frame WITHOUT the Windows 0x00
/// report-ID prefix byte:
///   0x0A  seq  0x03 0x00  sub=0x00  pattern  intensity
///
/// The BLE address equals the classic BT address embedded in the HID
/// device-instance path (segment matching /&[0-9a-f]{12}[&#]/).
/// </summary>
public static class BleGattSender
{
    // Xbox vendor service + LED write characteristic (confirmed on fw 0x0522)
    private static readonly Guid XBOX_SERVICE =
        new("00000001-5f60-4c4f-9c83-a7953298d40d");

    private static readonly Guid XBOX_LED_CHAR =
        new("00000004-5f60-4c4f-9c83-a7953298d40d");

    public static async Task<bool> TrySendAsync(XboxDevice device, byte[] gipCommand)
    {
        ulong btAddr = ExtractBtAddress(device.Path);
        if (btAddr == 0)
        {
            Console.WriteLine("  Cannot parse BT address from path.");
            return false;
        }

        Console.Write($"  BLE ({btAddr:X12})... ");
        BluetoothLEDevice? le = null;
        try { le = await BluetoothLEDevice.FromBluetoothAddressAsync(btAddr); }
        catch (Exception ex) { Console.WriteLine($"exception: {ex.Message}"); return false; }

        if (le == null)
        {
            Console.WriteLine("not found (controller may only be reachable via classic BT on this pairing).");
            return false;
        }
        Console.WriteLine($"connected  ({le.Name})");

        try { return await SendGattAsync(le, gipCommand); }
        finally { le.Dispose(); }
    }

    private static async Task<bool> SendGattAsync(BluetoothLEDevice le, byte[] gipCommand)
    {
        // Get Xbox vendor service
        var svcResult = await le.GetGattServicesForUuidAsync(XBOX_SERVICE, BluetoothCacheMode.Uncached);
        if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
        {
            Console.WriteLine($"  Xbox vendor GATT service not found ({svcResult.Status}). Trying full probe...");
            return await FallbackProbeAsync(le, gipCommand);
        }

        using var svc = svcResult.Services[0];

        // Get LED write characteristic
        var charResult = await svc.GetCharacteristicsForUuidAsync(XBOX_LED_CHAR, BluetoothCacheMode.Uncached);
        if (charResult.Status != GattCommunicationStatus.Success || charResult.Characteristics.Count == 0)
        {
            Console.WriteLine($"  LED characteristic not found ({charResult.Status}).");
            return false;
        }

        var ch = charResult.Characteristics[0];

        // Payload: raw GIP frame — strip the Windows 0x00 report-ID prefix byte
        byte[] payload = gipCommand[1..];
        Console.WriteLine($"  Payload : {BitConverter.ToString(payload)}");

        var writeOpt = ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
            ? GattWriteOption.WriteWithoutResponse
            : GattWriteOption.WriteWithResponse;

        var writer = new DataWriter();
        writer.WriteBytes(payload);
        var status = await ch.WriteValueAsync(writer.DetachBuffer(), writeOpt);

        if (status == GattCommunicationStatus.Success)
        {
            Console.WriteLine("  [OK]  Sent via BLE GATT (Xbox vendor service)");
            return true;
        }

        Console.WriteLine($"  [FAIL] WriteValueAsync: {status}");
        return false;
    }

    /// <summary>
    /// Fallback: enumerate all services and try every writable characteristic.
    /// Used when the well-known Xbox vendor service UUID is not found
    /// (e.g. different firmware version or controller model).
    /// </summary>
    private static async Task<bool> FallbackProbeAsync(BluetoothLEDevice le, byte[] gipCommand)
    {
        var svcResult = await le.GetGattServicesAsync(BluetoothCacheMode.Uncached);
        if (svcResult.Status != GattCommunicationStatus.Success) return false;

        Console.WriteLine($"  Probing {svcResult.Services.Count} services...");

        // Skip services reserved by Windows HID stack
        var hidSvcUuid = new Guid("00001812-0000-1000-8000-00805f9b34fb");

        foreach (var svc in svcResult.Services)
        {
            if (svc.Uuid == hidSvcUuid) { svc.Dispose(); continue; }

            var charResult = await svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
            if (charResult.Status != GattCommunicationStatus.Success) { svc.Dispose(); continue; }

            foreach (var ch in charResult.Characteristics)
            {
                bool canWrite = ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ||
                                ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse);
                if (!canWrite) continue;

                Console.WriteLine($"  Trying {ch.Uuid} (svc {svc.Uuid})...");

                var writer = new DataWriter();
                writer.WriteBytes(gipCommand[1..]);
                var writeOpt = ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                    ? GattWriteOption.WriteWithoutResponse : GattWriteOption.WriteWithResponse;

                var result = await ch.WriteValueAsync(writer.DetachBuffer(), writeOpt);
                Console.WriteLine($"    → {result}");

                if (result == GattCommunicationStatus.Success)
                {
                    Console.WriteLine($"  [OK]  Sent via {ch.Uuid}");
                    foreach (var s in svcResult.Services) s.Dispose();
                    return true;
                }
            }

            svc.Dispose();
        }

        foreach (var s in svcResult.Services) s.Dispose();
        return false;
    }

    // Known read-only characteristics in the Xbox vendor service
    private static readonly Guid XBOX_CHAR_0002 =
        new("00000002-5f60-4c4f-9c83-a7953298d40d");
    private static readonly Guid XBOX_CHAR_0003 =
        new("00000003-5f60-4c4f-9c83-a7953298d40d");

    // Attribute keys observed in characteristic 0x0002 that relate to LED state
    private const ushort ATTR_BRIGHTNESS     = 0x0010;   // current brightness 0-100
    private const ushort ATTR_LED_MODE       = 0x0011;   // LedMode enum value
    private const ushort ATTR_MAX_BRIGHTNESS = 0x0038;   // max brightness cap (usually 100)

    /// <summary>
    /// Tests the "attribute write" theory: characteristic 0x0002 stores device
    /// state as 6-byte records (2-byte key LE + 4-byte value LE). This method
    /// tries writing the same format to 0x0004 to directly set brightness/mode,
    /// then re-reads 0x0002 to confirm whether the values actually changed.
    /// </summary>
    public static async Task ProbeAttrAsync(XboxDevice device)
    {
        ulong btAddr = ExtractBtAddress(device.Path);
        if (btAddr == 0) { Console.WriteLine("  Cannot parse BT address."); return; }

        Console.Write($"  BLE ({btAddr:X12})... ");
        BluetoothLEDevice? le = null;
        try { le = await BluetoothLEDevice.FromBluetoothAddressAsync(btAddr); }
        catch (Exception ex) { Console.WriteLine($"exception: {ex.Message}"); return; }
        if (le == null) { Console.WriteLine("not found."); return; }
        Console.WriteLine($"connected  ({le.Name})");

        try
        {
            var svcResult = await le.GetGattServicesForUuidAsync(XBOX_SERVICE, BluetoothCacheMode.Uncached);
            if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
            {
                Console.WriteLine($"  Xbox vendor service not found ({svcResult.Status}).");
                return;
            }
            using var svc = svcResult.Services[0];

            // Get read characteristic (0x0002)
            var readCharResult = await svc.GetCharacteristicsForUuidAsync(XBOX_CHAR_0002, BluetoothCacheMode.Uncached);
            var readCh = (readCharResult.Status == GattCommunicationStatus.Success && readCharResult.Characteristics.Count > 0)
                ? readCharResult.Characteristics[0] : null;

            // Get write characteristic (0x0004)
            var writeCharResult = await svc.GetCharacteristicsForUuidAsync(XBOX_LED_CHAR, BluetoothCacheMode.Uncached);
            if (writeCharResult.Status != GattCommunicationStatus.Success || writeCharResult.Characteristics.Count == 0)
            {
                Console.WriteLine("  Write characteristic (0x0004) not found.");
                return;
            }
            var writeCh = writeCharResult.Characteristics[0];
            var writeOpt = writeCh.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                ? GattWriteOption.WriteWithoutResponse : GattWriteOption.WriteWithResponse;

            // ── Helper: read and print LED-relevant attributes ────────────────
            async Task PrintLedAttrs(string label)
            {
                if (readCh == null) return;
                var r = await readCh.ReadValueAsync(BluetoothCacheMode.Uncached);
                if (r.Status != GattCommunicationStatus.Success) { Console.WriteLine($"  [{label}] Read failed: {r.Status}"); return; }
                var reader = DataReader.FromBuffer(r.Value);
                byte[] blob = new byte[r.Value.Length];
                reader.ReadBytes(blob);
                var attrs = ParseAttrTable(blob);
                Console.Write($"  [{label}]");
                if (attrs.TryGetValue(ATTR_BRIGHTNESS, out uint brt))     Console.Write($"  brightness={brt}");
                if (attrs.TryGetValue(ATTR_LED_MODE, out uint mode))       Console.Write($"  mode={mode}");
                if (attrs.TryGetValue(ATTR_MAX_BRIGHTNESS, out uint maxb)) Console.Write($"  max_brightness={maxb}");
                Console.WriteLine();
            }

            // ── Helper: write single 6-byte attribute record ──────────────────
            async Task<GattCommunicationStatus> WriteAttr(ushort key, uint value)
            {
                byte[] rec = [
                    (byte)(key & 0xFF), (byte)(key >> 8),
                    (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF),
                    (byte)((value >> 16) & 0xFF), (byte)(value >> 24)
                ];
                Console.Write($"  WriteAttr key=0x{key:X4} val={value} ({BitConverter.ToString(rec)}) → ");
                var w = new DataWriter();
                w.WriteBytes(rec);
                var result = await writeCh.WriteValueAsync(w.DetachBuffer(), writeOpt);
                Console.WriteLine(result);
                await Task.Delay(600);
                return result;
            }

            await PrintLedAttrs("BEFORE");

            Console.WriteLine("\n  --- Trying attribute writes to turn LED off ---");

            // Try setting brightness to 0 via attribute key 0x0010
            await WriteAttr(ATTR_BRIGHTNESS, 0);
            await PrintLedAttrs("after brightness=0");

            // Try setting mode to Off (0) via attribute key 0x0011
            await WriteAttr(ATTR_LED_MODE, 0);
            await PrintLedAttrs("after mode=Off");

            // Try both together as a single 12-byte write
            Console.Write($"  WriteAttr combined (brightness=0 + mode=Off, 12 bytes) → ");
            byte[] combined = [
                (byte)(ATTR_BRIGHTNESS & 0xFF), (byte)(ATTR_BRIGHTNESS >> 8), 0x00, 0x00, 0x00, 0x00,
                (byte)(ATTR_LED_MODE & 0xFF),   (byte)(ATTR_LED_MODE >> 8),   0x00, 0x00, 0x00, 0x00,
            ];
            {
                var w = new DataWriter(); w.WriteBytes(combined);
                var result = await writeCh.WriteValueAsync(w.DetachBuffer(), writeOpt);
                Console.WriteLine(result);
                await Task.Delay(600);
            }
            await PrintLedAttrs("after combined");

            Console.WriteLine("\n  --- Restoring: brightness=100, mode=Solid(3) ---");
            await WriteAttr(ATTR_LED_MODE, 3);
            await WriteAttr(ATTR_BRIGHTNESS, 100);
            await PrintLedAttrs("RESTORED");
        }
        finally { le.Dispose(); }
    }

    // Parses characteristic 0x0002 blob as a table of 6-byte records (2-byte key LE + 4-byte value LE).
    private static Dictionary<ushort, uint> ParseAttrTable(byte[] blob)
    {
        var table = new Dictionary<ushort, uint>();
        for (int i = 0; i + 5 < blob.Length; i += 6)
        {
            ushort key = (ushort)(blob[i] | (blob[i + 1] << 8));
            uint   val = (uint)(blob[i + 2] | (blob[i + 3] << 8) | (blob[i + 4] << 16) | (blob[i + 5] << 24));
            table[key] = val;
        }
        return table;
    }

    /// <summary>
    /// Reads all characteristics in the Xbox vendor service, subscribes to
    /// notifications, and tries several payload variants on the write char
    /// to discover the correct LED command format.
    /// </summary>
    public static async Task ProbeVendorServiceAsync(XboxDevice device)
    {
        ulong btAddr = ExtractBtAddress(device.Path);
        if (btAddr == 0) { Console.WriteLine("  Cannot parse BT address."); return; }

        Console.Write($"  BLE ({btAddr:X12})... ");
        BluetoothLEDevice? le = null;
        try { le = await BluetoothLEDevice.FromBluetoothAddressAsync(btAddr); }
        catch (Exception ex) { Console.WriteLine($"exception: {ex.Message}"); return; }

        if (le == null) { Console.WriteLine("not found."); return; }
        Console.WriteLine($"connected  ({le.Name})");

        try
        {
            var svcResult = await le.GetGattServicesForUuidAsync(XBOX_SERVICE, BluetoothCacheMode.Uncached);
            if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
            {
                Console.WriteLine($"  Xbox vendor GATT service not found ({svcResult.Status}).");
                return;
            }

            using var svc = svcResult.Services[0];
            Console.WriteLine($"  Service: {svc.Uuid}");

            // ── Enumerate all characteristics ─────────────────────────────
            var charResult = await svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
            if (charResult.Status != GattCommunicationStatus.Success)
            {
                Console.WriteLine($"  GetCharacteristicsAsync failed: {charResult.Status}");
                return;
            }

            Console.WriteLine($"\n  Characteristics ({charResult.Characteristics.Count}):");

            GattCharacteristic? writeCh = null;

            foreach (var ch in charResult.Characteristics)
            {
                Console.WriteLine($"    {ch.Uuid}  [{ch.CharacteristicProperties}]");

                // Read readable characteristics
                if (ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
                {
                    var readResult = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
                    if (readResult.Status == GattCommunicationStatus.Success)
                    {
                        var reader = DataReader.FromBuffer(readResult.Value);
                        byte[] bytes = new byte[readResult.Value.Length];
                        reader.ReadBytes(bytes);
                        Console.WriteLine($"      Read → {BitConverter.ToString(bytes)}");
                    }
                    else
                    {
                        Console.WriteLine($"      Read → {readResult.Status}");
                    }
                }

                // Subscribe to notifications
                if (ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify) ||
                    ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
                {
                    var descVal = ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate)
                        ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
                        : GattClientCharacteristicConfigurationDescriptorValue.Notify;

                    var subStatus = await ch.WriteClientCharacteristicConfigurationDescriptorAsync(descVal);
                    Console.WriteLine($"      Subscribe → {subStatus}");

                    if (subStatus == GattCommunicationStatus.Success)
                    {
                        Guid capturedUuid = ch.Uuid;
                        ch.ValueChanged += (sender, args) =>
                        {
                            var r = DataReader.FromBuffer(args.CharacteristicValue);
                            byte[] b = new byte[args.CharacteristicValue.Length];
                            r.ReadBytes(b);
                            Console.WriteLine($"  [NOTIFY {capturedUuid}]: {BitConverter.ToString(b)}");
                        };
                    }
                }

                if (ch.Uuid == XBOX_LED_CHAR)
                    writeCh = ch;
            }

            if (writeCh == null)
            {
                Console.WriteLine("\n  Write characteristic (0x0004) not found.");
                return;
            }

            var writeOpt = writeCh.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                ? GattWriteOption.WriteWithoutResponse
                : GattWriteOption.WriteWithResponse;

            Console.WriteLine($"\n  Write option: {writeOpt}");
            Console.WriteLine("  Trying payload variants (LED off / mode=0, brightness=0):\n");

            // ── Payload variants to probe ─────────────────────────────────
            // Each row: human label + raw bytes written to characteristic 0x0004.
            // No 0x00 report-ID prefix — that prefix is only for Win32 HID APIs.
            var variants = new (string label, byte[] data)[]
            {
                // Current (confirmed 'Success' but no LED change)
                ("A  GIP 4-hdr 2-len off",   [0x0A, 0x01, 0x08, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00]),
                // Same but Solid 100% — to confirm any response differs from Off
                ("B  GIP 4-hdr 2-len solid",  [0x0A, 0x02, 0x08, 0x00, 0x03, 0x64, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00]),
                // GIP with 3-byte header (1-byte length, seen in Xbox One BT)
                ("C  GIP 3-hdr 1-len off",    [0x0A, 0x03, 0x08, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF]),
                // GIP without explicit length (payload only after cmd+seq)
                ("D  GIP no-len off",         [0x0A, 0x04, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00]),
                // Just mode + brightness bytes
                ("E  mode+brt only (off)",    [0x00, 0x00]),
                ("F  mode+brt only (solid)",  [0x03, 0x64]),
                // Single byte: mode
                ("G  1-byte mode=off",        [0x00]),
                ("H  1-byte mode=solid",      [0x03]),
                // Xbox One / Series S observed BT LED packet (from open-source captures):
                // opcode 0x0A, no length field, 8-byte payload starting at byte 2
                ("I  OXM-style off",          [0x0A, 0x05, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00]),
                ("J  OXM-style solid",        [0x0A, 0x06, 0x03, 0x64, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00]),
            };

            foreach (var (label, data) in variants)
            {
                Console.Write($"  [{label}] {BitConverter.ToString(data)} → ");
                var writer = new DataWriter();
                writer.WriteBytes(data);
                var result = await writeCh.WriteValueAsync(writer.DetachBuffer(), writeOpt);
                Console.WriteLine(result);
                await Task.Delay(600);
            }

            // Wait for any pending notifications
            await Task.Delay(1500);
            Console.WriteLine("\n  Probe complete.");
        }
        finally
        {
            le.Dispose();
        }
    }

    /// <summary>
    /// Continuously polls characteristic 0x0002 (attribute table) every second,
    /// printing LED-relevant keys and highlighting changes.
    /// Hold BLE connection open while running Xbox Accessories App to observe
    /// what—if anything—the app writes to the attribute table.
    /// Press Ctrl+C to stop.
    /// </summary>
    public static async Task MonitorAsync(XboxDevice device)
    {
        ulong btAddr = ExtractBtAddress(device.Path);
        if (btAddr == 0) { Console.WriteLine("  Cannot parse BT address."); return; }

        Console.Write($"  BLE ({btAddr:X12})... ");
        BluetoothLEDevice? le = null;
        try { le = await BluetoothLEDevice.FromBluetoothAddressAsync(btAddr); }
        catch (Exception ex) { Console.WriteLine($"exception: {ex.Message}"); return; }
        if (le == null) { Console.WriteLine("not found."); return; }
        Console.WriteLine($"connected  ({le.Name})");
        Console.WriteLine("  Monitoring 0x0002 — press Ctrl+C to stop.\n");

        try
        {
            var svcResult = await le.GetGattServicesForUuidAsync(XBOX_SERVICE, BluetoothCacheMode.Uncached);
            if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
            {
                Console.WriteLine($"  Xbox vendor service not found ({svcResult.Status}).");
                return;
            }
            using var svc = svcResult.Services[0];

            var charResult = await svc.GetCharacteristicsForUuidAsync(XBOX_CHAR_0002, BluetoothCacheMode.Uncached);
            if (charResult.Status != GattCommunicationStatus.Success || charResult.Characteristics.Count == 0)
            {
                Console.WriteLine($"  Characteristic 0x0002 not found ({charResult.Status}).");
                return;
            }
            var ch = charResult.Characteristics[0];

            Dictionary<ushort, uint>? prev = null;
            using var cts = new System.Threading.CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            while (!cts.Token.IsCancellationRequested)
            {
                var r = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
                if (r.Status == GattCommunicationStatus.Success)
                {
                    var reader = DataReader.FromBuffer(r.Value);
                    byte[] blob = new byte[r.Value.Length];
                    reader.ReadBytes(blob);
                    var cur = ParseAttrTable(blob);

                    string ts = DateTime.Now.ToString("HH:mm:ss");

                    // Collect changed keys only
                    var changed = cur
                        .Where(kv => prev == null || !prev.TryGetValue(kv.Key, out uint old) || old != kv.Value)
                        .ToList();

                    if (changed.Count == 0 && prev != null)
                    {
                        // silent — no output on steady state
                    }
                    else
                    {
                        Console.Write($"  [{ts}]");
                        foreach (var kv in changed)
                        {
                            string label = AttrLabels.TryGetValue(kv.Key, out string? l) ? l : $"0x{kv.Key:X4}";
                            uint? oldVal = (prev != null && prev.TryGetValue(kv.Key, out uint ov)) ? ov : null;
                            string delta = oldVal.HasValue ? $" ({(long)kv.Value - (long)oldVal.Value:+#;-#;0})" : "";
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.Write($"  {label}={kv.Value}{delta}");
                            Console.ResetColor();
                        }
                        Console.WriteLine();
                    }
                    prev = cur;
                }
                else
                {
                    Console.WriteLine($"  Read failed: {r.Status}");
                }

                try { await Task.Delay(1000, cts.Token); }
                catch (TaskCanceledException) { break; }
            }

            Console.WriteLine("\n  Monitor stopped.");
        }
        finally { le.Dispose(); }
    }

    // ── Key labels for char 0x0002 TLV dump ──────────────────────────────────

    private static readonly Dictionary<ushort, string> AttrLabels = new()
    {
        // Basic telemetry
        [0x0001] = "firmware_build",
        [0x0002] = "connection_count",
        [0x0003] = "unk_03",
        [0x0004] = "usage_time",
        [0x0005] = "unk_05",
        [0x0006] = "unk_06",
        [0x0007] = "unk_07",
        [0x0008] = "unk_08",
        // LED state
        [0x0010] = "led_brightness",
        [0x0011] = "led_mode",
        // Usage counters (suspected button presses)
        [0x0020] = "cnt_20",
        [0x0021] = "cnt_21",
        [0x0022] = "cnt_22",
        [0x0023] = "cnt_23",
        [0x0024] = "cnt_24",
        [0x0025] = "cnt_25",
        [0x0026] = "cnt_26",
        [0x0027] = "cnt_27",
        [0x0028] = "cnt_28",
        [0x0029] = "cnt_29",
        [0x002A] = "cnt_2A",
        [0x002B] = "cnt_2B",
        [0x002C] = "cnt_2C",
        [0x002D] = "cnt_2D",
        [0x002E] = "cnt_2E",
        [0x002F] = "cnt_2F",
        [0x0036] = "cnt_36",
        [0x0038] = "max_brightness",
        // Suspected input/calibration ranges
        [0x0040] = "cal_40",
        [0x0041] = "cal_41",
        [0x0042] = "cal_42",
        [0x0043] = "cal_43",
        // Larger counters
        [0x0050] = "cnt_50",
        [0x0051] = "cnt_51",
        [0x0052] = "cnt_52",
        [0x0053] = "cnt_53",
        [0x0054] = "cnt_54",
        [0x0055] = "cnt_55",
        [0x0056] = "cnt_56",
        [0x0057] = "cnt_57",
        [0x0058] = "cnt_58",
        [0x0059] = "cnt_59",
        [0x0060] = "cnt_60",
        [0x0061] = "cnt_61",
        [0x0062] = "cnt_62",
        [0x0063] = "cnt_63",
        [0x0064] = "cnt_64",
        [0x0065] = "cnt_65",
        [0x0066] = "cnt_66",
        [0x0067] = "cnt_67",
        // Suspected joystick / axis calibration pairs
        [0x0070] = "axis_70",
        [0x0071] = "axis_71",
        [0x0072] = "axis_72",
        [0x0074] = "axis_74",
        [0x0076] = "axis_76",
        [0x0078] = "axis_78",
        [0x007A] = "axis_7A",
        [0x007B] = "axis_7B",
        [0x007C] = "axis_7C",
        [0x007E] = "axis_7E",
        [0x0080] = "axis_80",
        [0x0082] = "axis_82",
        // Live joystick positions (two int16 per entry)
        [0x00A8] = "stick_pos_A8",
        [0x00A9] = "stick_pos_A9",
        [0x00AE] = "stick_pos_AE",
        [0x00AF] = "stick_pos_AF",
        // Misc
        [0x009C] = "unk_9C",
        [0x009D] = "unk_9D",
        [0x009E] = "unk_9E",
        [0x009F] = "unk_9F",
        [0x00A0] = "unk_A0",
        [0x00A1] = "unk_A1",
        [0x00A2] = "unk_A2",
        [0x00A3] = "unk_A3",
        [0x00C1] = "unk_C1",
        [0x00C2] = "unk_C2",
        [0x00C3] = "unk_C3",
        [0x00C4] = "unk_C4",
        [0x00C5] = "unk_C5",
        [0x00C6] = "unk_C6",
        [0x00C7] = "unk_C7",
        [0x00C8] = "unk_C8",
        [0x00D6] = "unk_D6",
        [0x00D9] = "unk_D9",
        [0x00DA] = "unk_DA",
        [0x00DD] = "unk_DD",
        [0x00DE] = "unk_DE",
        [0x00E1] = "unk_E1",
    };

    private static string LedModeName(uint mode) => mode switch
    {
        0 => "Off",
        1 => "On",
        2 => "FastBlink",
        3 => "SlowBlink",
        4 => "ChargingBlink",
        13 => "RampToLevel",
        _ => $"?({mode})",
    };

    /// <summary>
    /// Reads char 0x0002 and prints the full TLV attribute table with labels.
    /// </summary>
    public static async Task DumpTlvAsync(XboxDevice device)
    {
        ulong btAddr = ExtractBtAddress(device.Path);
        if (btAddr == 0) { Console.WriteLine("  Cannot parse BT address."); return; }

        Console.Write($"  BLE ({btAddr:X12})... ");
        BluetoothLEDevice? le = null;
        try { le = await BluetoothLEDevice.FromBluetoothAddressAsync(btAddr); }
        catch (Exception ex) { Console.WriteLine($"exception: {ex.Message}"); return; }
        if (le == null) { Console.WriteLine("not found."); return; }
        Console.WriteLine($"connected  ({le.Name})\n");

        try
        {
            var svcResult = await le.GetGattServicesForUuidAsync(XBOX_SERVICE, BluetoothCacheMode.Uncached);
            if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
            {
                Console.WriteLine($"  Xbox vendor service not found ({svcResult.Status}).");
                return;
            }
            using var svc = svcResult.Services[0];

            var charResult = await svc.GetCharacteristicsForUuidAsync(XBOX_CHAR_0002, BluetoothCacheMode.Uncached);
            if (charResult.Status != GattCommunicationStatus.Success || charResult.Characteristics.Count == 0)
            {
                Console.WriteLine($"  Char 0x0002 not found ({charResult.Status}).");
                return;
            }

            var r = await charResult.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached);
            if (r.Status != GattCommunicationStatus.Success)
            {
                Console.WriteLine($"  Read failed: {r.Status}");
                return;
            }

            var reader = DataReader.FromBuffer(r.Value);
            byte[] blob = new byte[r.Value.Length];
            reader.ReadBytes(blob);

            Console.WriteLine($"  Char 0x0002 — {blob.Length} bytes, {blob.Length / 6} entries\n");
            Console.WriteLine($"  {"Key",-8} {"Label",-20} {"Dec",12}  {"Hex",12}  Note");
            Console.WriteLine($"  {new string('-', 70)}");

            var table = ParseAttrTable(blob);
            foreach (var kv in table.OrderBy(x => x.Key))
            {
                string label = AttrLabels.TryGetValue(kv.Key, out string? l) ? l : $"unk_{kv.Key:X4}";
                string note = kv.Key switch
                {
                    ATTR_LED_MODE       => LedModeName(kv.Value),
                    ATTR_BRIGHTNESS     => $"(hw scale)",
                    ATTR_MAX_BRIGHTNESS => $"(%)",
                    _ => "",
                };

                // For live joystick positions: show as lo16/hi16 pair
                if (kv.Key is >= 0x00A8 and <= 0x00AF)
                {
                    ushort lo = (ushort)(kv.Value & 0xFFFF);
                    ushort hi = (ushort)(kv.Value >> 16);
                    note = $"lo={lo} hi={hi}";
                }

                Console.WriteLine($"  0x{kv.Key:X4}   {label,-20} {kv.Value,12}  0x{kv.Value:X8}  {note}");
            }
        }
        finally { le.Dispose(); }
    }

    private static ulong ExtractBtAddress(string path)
    {
        var m = Regex.Match(path, @"&([0-9a-f]{12})(?:[&#]|$)", RegexOptions.IgnoreCase);
        if (!m.Success) return 0;
        try { return Convert.ToUInt64(m.Groups[1].Value, 16); }
        catch { return 0; }
    }
}
