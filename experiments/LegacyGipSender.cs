using System.Runtime.InteropServices;

namespace XboxLedControl;

/// <summary>
/// Tries to set the Guide button LED via the semi-public WinRT Preview API:
///   Windows.Gaming.Input.Preview.LegacyGipGameControllerProvider.SetHomeLedIntensity(byte)
///
/// All calls are raw COM vtable — no C# WinRT projection needed for Preview types.
///
/// Flow:
///   1. RoGetActivationFactory("Windows.Gaming.Input.RawGameController")
///      → IRawGameControllerStatics.get_RawGameControllers [vtable[10]]
///      → IVectorView.GetAt(0)  [vtable[6]]
///      → QI for IGameController [1baf6522-...]
///   2. RoGetActivationFactory("Windows.Gaming.Input.Preview.LegacyGipGameControllerProvider")
///      → ILegacyGipGameControllerProviderStatics.FromGameController [vtable[6]]
///   3. QI result for ILegacyGipGameControllerProvider [2da3ed52-...]
///      → SetHomeLedIntensity(byte) [vtable[15]]
/// </summary>
public static class LegacyGipSender
{
    // ── Interface GUIDs ──────────────────────────────────────────────────────

    // Windows.Gaming.Input.IRawGameControllerStatics
    static readonly Guid IID_IRawGameControllerStatics =
        new("eb8d0792-e95a-4b19-afc7-0a59f8bf759e");

    // Windows.Gaming.Input.IGameController
    static readonly Guid IID_IGameController =
        new("1baf6522-5f64-42c5-8267-b9fe2215bfbd");

    // Windows.Gaming.Input.Preview.ILegacyGipGameControllerProviderStatics
    static readonly Guid IID_ILegacyGipGameControllerProviderStatics =
        new("d40dda17-b1f4-499a-874c-7095aac15291");

    // Windows.Gaming.Input.Preview.ILegacyGipGameControllerProvider
    static readonly Guid IID_ILegacyGipGameControllerProvider =
        new("2da3ed52-ffd9-43e2-825c-1d2790e04d14");

    // ── P/Invoke ─────────────────────────────────────────────────────────────

    [DllImport("combase.dll", PreserveSig = true)]
    static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string str, int len, out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

    // ── Raw vtable helpers ───────────────────────────────────────────────────

    // vtable[0] = QueryInterface
    static unsafe int QI(IntPtr obj, ref Guid iid, out IntPtr result)
    {
        var fn = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>*)*(void**)obj)[0];
        fixed (Guid* pG = &iid) fixed (IntPtr* pR = &result) return fn(obj, pG, pR);
    }

    // vtable[2] = Release
    static unsafe uint Release(IntPtr obj)
    {
        if (obj == IntPtr.Zero) return 0;
        return ((delegate* unmanaged[Stdcall]<IntPtr, uint>*)*(void**)obj)[2](obj);
    }

    // vtable[n](this) → HRESULT + out IntPtr
    static unsafe int VOut(IntPtr obj, int n, out IntPtr r)
    {
        var fn = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>*)*(void**)obj)[n];
        fixed (IntPtr* p = &r) return fn(obj, p);
    }

    // vtable[n](this) → HRESULT + out uint
    static unsafe int VUInt(IntPtr obj, int n, out uint r)
    {
        var fn = ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>*)*(void**)obj)[n];
        fixed (uint* p = &r) return fn(obj, p);
    }

    // vtable[n](this, uint) → HRESULT + out IntPtr  (IVectorView.GetAt)
    static unsafe int VGetAt(IntPtr obj, int n, uint idx, out IntPtr r)
    {
        var fn = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>*)*(void**)obj)[n];
        fixed (IntPtr* p = &r) return fn(obj, idx, p);
    }

    // vtable[n](this, IntPtr in) → HRESULT + out IntPtr  (in before out, WinRT style)
    static unsafe int VInOut(IntPtr obj, int n, IntPtr arg, out IntPtr r)
    {
        var fn = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>*)*(void**)obj)[n];
        fixed (IntPtr* p = &r) return fn(obj, arg, p);
    }

    // vtable[n](this, byte) → HRESULT
    static unsafe int VByte(IntPtr obj, int n, byte arg)
        => ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>*)*(void**)obj)[n](obj, arg);

    // ── Public entry point ───────────────────────────────────────────────────

    /// <param name="intensity">0–47 (GIP scale; user 0–100 should be prescaled)</param>
    public static void TrySend(byte intensity)
    {
        IntPtr rawStatics    = default;
        IntPtr vecView       = default;
        IntPtr controllerObj = default;
        IntPtr iGameCtrl     = default;
        IntPtr legacyStatics = default;
        IntPtr legacyProv    = default;
        IntPtr iLegacy       = default;

        try
        {
            // ── Step 1: enumerate RawGameControllers via COM ──────────────

            int hr = WindowsCreateString("Windows.Gaming.Input.RawGameController",
                "Windows.Gaming.Input.RawGameController".Length, out var rawClassId);
            if (hr != 0) { Console.WriteLine($"  WindowsCreateString(RawGameController): 0x{hr:X8}"); return; }

            var rawIid = IID_IRawGameControllerStatics;
            hr = RoGetActivationFactory(rawClassId, ref rawIid, out rawStatics);
            WindowsDeleteString(rawClassId);
            Console.WriteLine($"  RoGetActivationFactory(RawGameController): 0x{hr:X8}");
            if (hr != 0 || rawStatics == default) return;

            // get_RawGameControllers → IVectorView  [vtable[10]]
            hr = VOut(rawStatics, 10, out vecView);
            Console.WriteLine($"  get_RawGameControllers: 0x{hr:X8}");
            if (hr != 0 || vecView == default) return;

            // WinRT gaming input enumerates asynchronously — poll up to 3 s
            uint count = 0;
            for (int t = 0; t < 30; t++)
            {
                VUInt(vecView, 7, out count);
                if (count > 0) break;
                Thread.Sleep(100);
                // Refresh the IVectorView snapshot each tick
                Release(vecView); vecView = default;
                hr = VOut(rawStatics, 10, out vecView);
                if (hr != 0 || vecView == default) { Console.WriteLine($"  get_RawGameControllers refresh: 0x{hr:X8}"); return; }
            }
            Console.WriteLine($"  Controller count: {count}");
            if (count == 0) { Console.WriteLine("  No controllers found after 3 s."); return; }

            for (uint i = 0; i < count; i++)
            {
                if (controllerObj != default) { Release(controllerObj); controllerObj = default; }
                if (iGameCtrl     != default) { Release(iGameCtrl);     iGameCtrl     = default; }
                if (legacyProv    != default) { Release(legacyProv);    legacyProv    = default; }
                if (iLegacy       != default) { Release(iLegacy);       iLegacy       = default; }

                // IVectorView.GetAt(i) [vtable[6]]
                hr = VGetAt(vecView, 6, i, out controllerObj);
                Console.WriteLine($"\n  Controller[{i}] GetAt: 0x{hr:X8} ptr=0x{controllerObj:X16}");
                if (hr != 0 || controllerObj == default) continue;

                // QI for IGameController
                var gcIid = IID_IGameController;
                hr = QI(controllerObj, ref gcIid, out iGameCtrl);
                Console.WriteLine($"  QI IGameController: 0x{hr:X8}");
                if (hr != 0 || iGameCtrl == default) continue;

                // ── Attempt A: QI directly on the controller object ───────
                Console.WriteLine("  [A] QI ILegacyGipGameControllerProvider directly on controller...");
                var legacyIid = IID_ILegacyGipGameControllerProvider;
                hr = QI(controllerObj, ref legacyIid, out iLegacy);
                Console.WriteLine($"  [A] QI direct: 0x{hr:X8}");

                if (hr == 0 && iLegacy != default)
                {
                    Console.WriteLine("  [A] Got ILegacyGipGameControllerProvider directly!");
                    goto callSetIntensity;
                }

                // ── Attempt B: FromGameController via statics [vtable[6]] ─

                if (legacyStatics == default)
                {
                    const string cn = "Windows.Gaming.Input.Preview.LegacyGipGameControllerProvider";
                    hr = WindowsCreateString(cn, cn.Length, out var legacyClassId);
                    if (hr != 0) { Console.WriteLine($"  WindowsCreateString(LegacyGip): 0x{hr:X8}"); return; }

                    var legacyIid2 = IID_ILegacyGipGameControllerProviderStatics;
                    hr = RoGetActivationFactory(legacyClassId, ref legacyIid2, out legacyStatics);
                    WindowsDeleteString(legacyClassId);
                    Console.WriteLine($"  RoGetActivationFactory(LegacyGipGCProvider statics): 0x{hr:X8}");
                    if (hr != 0 || legacyStatics == default) return;
                }

                Console.WriteLine("  [B] FromGameController [vtable[6]]...");
                hr = VInOut(legacyStatics, 6, iGameCtrl, out legacyProv);
                Console.WriteLine($"  [B] FromGameController: 0x{hr:X8} provider=0x{legacyProv:X16}");

                if (hr == 0 && legacyProv != default)
                {
                    legacyIid = IID_ILegacyGipGameControllerProvider;
                    hr = QI(legacyProv, ref legacyIid, out iLegacy);
                    Console.WriteLine($"  [B] QI ILegacyGipGameControllerProvider: 0x{hr:X8}");
                    if (hr == 0 && iLegacy != default) goto callSetIntensity;
                }

                // ── Attempt C: FromGameControllerProvider [vtable[7]] ─────
                // This takes IGameControllerProvider — try passing the controller object directly
                // (it might also implement IGameControllerProvider if it's a GIP device)
                Console.WriteLine("  [C] FromGameControllerProvider [vtable[7]] (passing controller as provider)...");
                hr = VInOut(legacyStatics, 7, iGameCtrl, out var legacyProv2);
                Console.WriteLine($"  [C] FromGameControllerProvider: 0x{hr:X8} provider=0x{legacyProv2:X16}");
                if (hr == 0 && legacyProv2 != default)
                {
                    legacyIid = IID_ILegacyGipGameControllerProvider;
                    hr = QI(legacyProv2, ref legacyIid, out iLegacy);
                    Console.WriteLine($"  [C] QI ILegacyGipGameControllerProvider: 0x{hr:X8}");
                    Release(legacyProv2);
                    if (hr == 0 && iLegacy != default) goto callSetIntensity;
                }

                Console.WriteLine("  All paths returned null/error.");
                continue;

                callSetIntensity:
                // ── SetHomeLedIntensity(byte) [vtable[15]] ────────────────
                Console.WriteLine($"  Calling SetHomeLedIntensity({intensity}) [vtable[15]]...");
                hr = VByte(iLegacy, 15, intensity);
                Console.WriteLine(hr == 0
                    ? $"  [OK] SetHomeLedIntensity({intensity}) succeeded!"
                    : $"  FAILED: 0x{hr:X8}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Exception: 0x{ex.HResult:X8} {ex.Message}");
        }
        finally
        {
            if (iLegacy       != default) Release(iLegacy);
            if (legacyProv    != default) Release(legacyProv);
            if (legacyStatics != default) Release(legacyStatics);
            if (iGameCtrl     != default) Release(iGameCtrl);
            if (controllerObj != default) Release(controllerObj);
            if (vecView       != default) Release(vecView);
            if (rawStatics    != default) Release(rawStatics);
        }
    }
}
