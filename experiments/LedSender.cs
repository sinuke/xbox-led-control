using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static XboxLedControl.NativeMethods;

namespace XboxLedControl;

public static class LedSender
{
    public static bool Send(XboxDevice device, byte[] gipCommand)
    {
        var caps = ReadCaps(device.Path);
        PrintCaps(caps);

        bool ok = false;

        // ── Output report ────────────────────────────────────────────────────
        if (caps.output > 0)
        {
            Console.WriteLine($"  Trying output report (size {caps.output})...");
            ok = TrySend(device.Path, gipCommand, caps.output, usFeature: false);
        }
        else
        {
            Console.WriteLine("  No output reports in HID descriptor — skipping.");
        }

        // ── Feature report (fallback) ─────────────────────────────────────────
        if (!ok && caps.feature > 0)
        {
            Console.WriteLine($"  Trying feature report (size {caps.feature})...");
            ok = TrySend(device.Path, gipCommand, caps.feature, usFeature: true);
        }

        // ── L2CAP raw socket ─────────────────────────────────────────────────
        if (!ok && caps.output == 0 && caps.feature == 0)
        {
            Console.WriteLine("  ig_00 is input-only — trying raw Bluetooth L2CAP socket...");
            ok = BtSocketSender.TrySend(device, gipCommand);
        }

        // ── GIP direct write via \\.\XboxGIP (USB only) ──────────────────────
        // Confirmed working: read-symmetric 23-byte frame with seq=0x00.
        // gipCommand[1..] strips the HID report-ID prefix → raw 7-byte GIP frame.
        if (!ok && device.Connection == ConnectionType.Usb)
        {
            Console.WriteLine("  Trying GIP direct write (\\\\.\\XboxGIP)...");
            ok = GipDirectSender.TrySend(gipCommand[1..]);
            Console.WriteLine(ok ? "  [OK]  GipDirect" : "  [FAIL] GipDirect");
        }

        // ── Windows.Gaming.Input.Custom GIP factory ───────────────────────────
        if (!ok)
        {
            Console.WriteLine("  Trying GIP via GameControllerFactoryManager...");
            // LED payload = bytes [5..12] of the 13-byte gipCommand buffer
            // (skip the 0x00 report-ID prefix and the 4-byte GIP header)
            byte[] ledPayload = gipCommand[5..];
            ok = GipMessageSender.TrySendAsync(device.VendorId, device.ProductId, ledPayload)
                                 .GetAwaiter().GetResult();
        }

        // ── BLE GATT (async, called synchronously via .GetAwaiter().GetResult()) ──
        // NOTE: BLE GATT writes return GattCommunicationStatus.Success but the LED
        //       does NOT change — keeping this as last resort for discovery purposes.
        if (!ok && device.Connection == ConnectionType.Bluetooth)
        {
            Console.WriteLine("  Trying BLE GATT vendor service write...");
            ok = BleGattSender.TrySendAsync(device, gipCommand).GetAwaiter().GetResult();
        }

        return ok;
    }

    public static void Probe(XboxDevice device, GipLedPattern pattern, byte intensity)
    {
        Console.WriteLine($"\n  [Spec-correct: MessageType=0x0A, payload=3B, intensity={intensity}/47]");
        Send(device, GipLedCommand.Build(pattern, intensity));
    }

    // ── internals ─────────────────────────────────────────────────────────────

    private static bool TrySend(string path, byte[] gipCommand, ushort reportLen, bool usFeature)
    {
        SafeFileHandle h = OpenWrite(path);
        if (h.IsInvalid)
        {
            Console.WriteLine($"    Cannot open for writing (Win32 error {Marshal.GetLastWin32Error()})");
            return false;
        }

        using (h)
        {
            byte[] buf = new byte[reportLen];
            int copy = Math.Min(gipCommand.Length, reportLen);
            Array.Copy(gipCommand, buf, copy);
            Console.WriteLine($"    Payload: {BitConverter.ToString(buf)}");

            if (usFeature)
            {
                if (HidD_SetFeature(h, buf, (uint)buf.Length))
                {
                    Console.WriteLine("    [OK]  HidD_SetFeature");
                    return true;
                }
                Console.WriteLine($"    [FAIL] HidD_SetFeature error {Marshal.GetLastWin32Error()}");
                return false;
            }
            else
            {
                if (HidD_SetOutputReport(h, buf, (uint)buf.Length))
                {
                    Console.WriteLine("    [OK]  HidD_SetOutputReport");
                    return true;
                }
                int e1 = Marshal.GetLastWin32Error();
                Console.WriteLine($"    HidD_SetOutputReport failed ({e1}) – trying WriteFile...");
                if (WriteFile(h, buf, (uint)buf.Length, out uint written, IntPtr.Zero))
                {
                    Console.WriteLine($"    [OK]  WriteFile ({written} bytes)");
                    return true;
                }
                Console.WriteLine($"    [FAIL] WriteFile error {Marshal.GetLastWin32Error()}");
                return false;
            }
        }
    }

    /// <summary>
    /// Read HID capabilities using a read-access handle.
    /// A write-only handle is often refused by HidD_GetPreparsedData on BT devices.
    /// </summary>
    private static (ushort input, ushort output, ushort feature) ReadCaps(string path)
    {
        // Try GENERIC_READ first; fall back to query-only (0)
        SafeFileHandle h = CreateFile(path, GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

        if (h.IsInvalid)
            h = CreateFile(path, 0,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

        if (h.IsInvalid) return (0, 0, 0);

        using (h)
        {
            if (!HidD_GetPreparsedData(h, out IntPtr preparsed)) return (0, 0, 0);
            try
            {
                int status = HidP_GetCaps(preparsed, out HIDP_CAPS caps);
                if (status != unchecked((int)0x00110000)) return (0, 0, 0);
                return (caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength);
            }
            finally { HidD_FreePreparsedData(preparsed); }
        }
    }

    private static void PrintCaps((ushort input, ushort output, ushort feature) caps)
    {
        Console.WriteLine($"  HID caps: input={caps.input}  output={caps.output}  feature={caps.feature}");
    }

    private static SafeFileHandle OpenWrite(string path)
    {
        var h = CreateFile(path, GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (!h.IsInvalid) return h;

        return CreateFile(path, GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
    }
}
