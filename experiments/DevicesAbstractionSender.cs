using System.Runtime.InteropServices;

namespace XboxLedControl;

/// <summary>
/// Calls INexusApi / IMessageApi from Devices.Abstraction.dll via raw COM vtable P/Invoke.
///
/// COM vtable layout (x64, after IInspectable vtable[0..5]):
///   IControllerFactoryStatics  vtable[6]  = get_Instance
///   IControllerFactory         vtable[6]  = Initialize
///                              vtable[11] = get_Controllers
///                              vtable[14] = GetReadyControllersAsync
///   IVectorView<IController>   vtable[6]  = GetAt(uint)
///                              vtable[7]  = get_Size
///   IController                vtable[6]  = get_AccessoryId
///                              vtable[34] = get_MessageApi
///                              vtable[35] = get_SupportsMessageApi
///                              vtable[40] = get_NexusApi
///                              vtable[41] = get_SupportsNexusApi
///   INexusApi                  vtable[6]  = SetTempNexusBrightness(byte)
///   IMessageApi                vtable[6]  = get_MaxMessageByteCount
///                              vtable[7]  = SendMessageAsync(out asyncOp, request)
///   IMessageSenderStatics      vtable[6]  = Create(out sender)
///                              vtable[7]  = CreateForAccessoryId(out sender, accessoryId)
///   IMessageSender             vtable[8]  = SendAsync(out asyncOp, request)
///   IMessageRequest            vtable[7]  = put_MessageId(uint)
///                              vtable[9]  = put_Data(IVector<byte>)
///                              vtable[11] = put_Class(int)
///                              vtable[13] = put_Transport(int)
/// </summary>
public static class DevicesAbstractionSender
{
    // ── Interface GUIDs (from Devices.Abstraction.winmd) ───────────────────

    static readonly Guid IID_IControllerFactoryStatics =
        new("322929BF-84B7-50DF-9682-31B7EA0DE1BA");

    static readonly Guid IID_IControllerFactory =
        new("AFAF416F-AF0A-5B85-9184-4176032F5CFB");

    static readonly Guid IID_IController =
        new("2E157525-AFBB-412E-9573-3A32D04CDCEB");

    static readonly Guid IID_INexusApi =
        new("D8F5361E-4057-4DC7-AD81-8439B650C2CF");

    static readonly Guid IID_IGipControllerFactoryStatics =
        new("05BEC42E-5ACD-5F6B-BE4B-49DB159C4A47");

    static readonly Guid IID_IMessageSenderStatics =
        new("DABE680E-B9CF-5453-A6A8-00F9DBB02103");

    static readonly Guid IID_IMessageSender =
        new("DC0CD4FE-C4D3-5E21-A099-B2C68C0B287D");

    static readonly Guid IID_IMessageRequest =
        new("2AEFFC83-890D-5597-A474-752FCBA99B19");

    // ── WinRT / COM P/Invoke helpers ────────────────────────────────────────

    [DllImport("combase.dll", PreserveSig = true)]
    static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string str, int len, out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", PreserveSig = true)]
    static extern int RoActivateInstance(IntPtr classId, out IntPtr instance);

    // ── Raw vtable dispatch ─────────────────────────────────────────────────

    // QueryInterface — vtable[0]
    static unsafe int QI(IntPtr obj, ref Guid iid, out IntPtr result)
    {
        var fn = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>*)*(void**)obj)[0];
        fixed (Guid* pG = &iid) fixed (IntPtr* pR = &result) return fn(obj, pG, pR);
    }

    // Release — vtable[2]
    static unsafe uint Release(IntPtr obj)
    {
        if (obj == IntPtr.Zero) return 0;
        return ((delegate* unmanaged[Stdcall]<IntPtr, uint>*)*(void**)obj)[2](obj);
    }

    // vtable[n](this) → HRESULT
    static unsafe int V0(IntPtr obj, int n)
        => ((delegate* unmanaged[Stdcall]<IntPtr, int>*)*(void**)obj)[n](obj);

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

    // vtable[n](this, uint) → HRESULT + out IntPtr  (e.g. GetAt)
    static unsafe int VGetAt(IntPtr obj, int n, uint idx, out IntPtr r)
    {
        var fn = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>*)*(void**)obj)[n];
        fixed (IntPtr* p = &r) return fn(obj, idx, p);
    }

    // vtable[n](this) → HRESULT + out byte/bool
    static unsafe int VBool(IntPtr obj, int n, out byte r)
    {
        var fn = ((delegate* unmanaged[Stdcall]<IntPtr, byte*, int>*)*(void**)obj)[n];
        fixed (byte* p = &r) return fn(obj, p);
    }

    // vtable[n](this) → HRESULT + out int  (e.g. IAsyncInfo.get_Status)
    static unsafe int VInt(IntPtr obj, int n, out int r)
    {
        var fn = ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>*)*(void**)obj)[n];
        fixed (int* p = &r) return fn(obj, p);
    }

    // vtable[n](this, byte) → HRESULT
    static unsafe int VByte(IntPtr obj, int n, byte arg)
        => ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>*)*(void**)obj)[n](obj, arg);

    // vtable[n](this) → HRESULT + out IntPtr, taking IntPtr input arg
    // Used for: CreateForAccessoryId(out sender, accessoryId)
    //           SendMessageAsync(out asyncOp, request)
    //           SendAsync(out asyncOp, request)
    static unsafe int VOutIn(IntPtr obj, int n, out IntPtr r, IntPtr arg)
    {
        var fn = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, IntPtr, int>*)*(void**)obj)[n];
        fixed (IntPtr* p = &r) return fn(obj, p, arg);
    }

    // vtable[n](this, uint) → HRESULT  (e.g. put_MessageId)
    static unsafe int VUIntArg(IntPtr obj, int n, uint arg)
        => ((delegate* unmanaged[Stdcall]<IntPtr, uint, int>*)*(void**)obj)[n](obj, arg);

    // vtable[n](this, int) → HRESULT  (e.g. put_Transport, put_Class)
    static unsafe int VIntArg(IntPtr obj, int n, int arg)
        => ((delegate* unmanaged[Stdcall]<IntPtr, int, int>*)*(void**)obj)[n](obj, arg);

    // vtable[n](this, IntPtr) → HRESULT  (e.g. put_Data(null))
    static unsafe int VPtrArg(IntPtr obj, int n, IntPtr arg)
        => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>*)*(void**)obj)[n](obj, arg);

    // ── Public entry point ──────────────────────────────────────────────────

    public static async Task<bool> TrySendAsync(byte brightness)
    {
        // 1. ControllerFactory (HID + GIP) — finds BT HID controller
        bool ok = await TryFactoryAsync("Devices.Abstraction.ControllerFactory",
            IID_IControllerFactoryStatics, brightness);
        if (ok) return true;

        Console.WriteLine();

        // 2. GipControllerFactory (GIP only) — finds USB GIP controllers
        ok = await TryFactoryAsync("Devices.Abstraction.GipControllerFactory",
            IID_IGipControllerFactoryStatics, brightness);
        return ok;
    }

    static async Task<bool> TryFactoryAsync(string className, Guid staticsIid, byte brightness)
    {
        Console.WriteLine($"  [Nexus] Activating {className}...");

        int hr = WindowsCreateString(className, className.Length, out var classId);
        if (hr != 0) { Console.WriteLine($"  WindowsCreateString: 0x{hr:X8}"); return false; }

        IntPtr statics    = default;
        IntPtr factory    = default;
        IntPtr vecView    = default;
        IntPtr controller = default;
        IntPtr icontrol   = default;
        IntPtr nexus      = default;

        try
        {
            // 1. Get statics interface
            var iid = staticsIid;
            hr = RoGetActivationFactory(classId, ref iid, out statics);
            if (hr != 0) { Console.WriteLine($"  RoGetActivationFactory: 0x{hr:X8}"); return false; }
            Console.WriteLine("  RoGetActivationFactory: OK");

            // 2. get_Instance() → IControllerFactory  [statics vtable[6]]
            hr = VOut(statics, 6, out factory);
            if (hr != 0 || factory == default) { Console.WriteLine($"  get_Instance: 0x{hr:X8}"); return false; }
            Console.WriteLine("  get_Instance: OK");

            // 3. Initialize()  [factory vtable[6]]
            hr = V0(factory, 6);
            Console.WriteLine($"  Initialize: 0x{hr:X8}");
            if (hr < 0) return false;

            // 4a. Sync: wait and get_Controllers  [factory vtable[11]]
            await Task.Delay(2000);
            hr = VOut(factory, 11, out vecView);
            if (hr == 0 && vecView != default)
            {
                VUInt(vecView, 7, out uint cnt);
                if (cnt > 0) { Console.WriteLine($"  get_Controllers: {cnt} controller(s)"); goto gotVecView; }
                Release(vecView); vecView = default;
            }

            // 4b. GetReadyControllersAsync() [factory vtable[14]] → IAsyncOperation
            Console.Write("  GetReadyControllersAsync: waiting");
            hr = VOut(factory, 14, out var asyncOp);
            if (hr != 0 || asyncOp == default) { Console.WriteLine($"  GetReadyControllersAsync: 0x{hr:X8}"); return false; }

            var asyncInfoIid = new Guid("00000036-0000-0000-C000-000000000046");
            QI(asyncOp, ref asyncInfoIid, out var asyncInfo);
            for (int t = 0; t < 50; t++)
            {
                int status = -1;
                if (asyncInfo != default) VInt(asyncInfo, 7, out status);
                Console.Write(".");
                if (status == 1) break;
                await Task.Delay(100);
            }
            Console.WriteLine();
            if (asyncInfo != default) Release(asyncInfo);

            hr = VOut(asyncOp, 8, out vecView);
            Release(asyncOp);
            if (hr != 0 || vecView == default) { Console.WriteLine($"  GetResults: 0x{hr:X8}"); return false; }

            gotVecView:
            VUInt(vecView, 7, out uint count);
            Console.WriteLine($"  Controllers found: {count}");
            if (count == 0) { Console.WriteLine("  No controllers."); return false; }

            for (uint i = 0; i < count; i++)
            {
                hr = VGetAt(vecView, 6, i, out controller);
                if (hr != 0 || controller == default) { Console.WriteLine($"  GetAt({i}): 0x{hr:X8}"); continue; }

                var icIid = IID_IController;
                hr = QI(controller, ref icIid, out icontrol);
                if (hr != 0 || icontrol == default) { Console.WriteLine($"  QI IController[{i}]: 0x{hr:X8}"); continue; }

                // Dump supports flags
                string[] apiNames = { "WGI","Base","GIP","GipRemap","XboxGIP","GipFW",
                    "Delphi","Cal","Pendragon","GameInput","CoAssist","Message",
                    "Norland","Power","Nexus","AttachedUsb","GamepadAudio","HidCtrl" };
                var sb = new System.Text.StringBuilder();
                for (int ai = 0; ai < apiNames.Length; ai++)
                {
                    VBool(icontrol, 13 + ai * 2, out byte sv);
                    if (sv != 0) sb.Append(apiNames[ai] + " ");
                }
                Console.WriteLine($"  Controller[{i}]: supports=[{sb.ToString().Trim()}]");

                // ── Path A: NexusApi ────────────────────────────────────────
                VBool(icontrol, 41, out byte supportsNexus);
                Console.WriteLine($"  Controller[{i}]: supportsNexus={supportsNexus != 0} (trying anyway)");

                hr = VOut(icontrol, 40, out nexus);
                if (hr == 0 && nexus != default)
                {
                    hr = VByte(nexus, 6, brightness);
                    Console.WriteLine($"  SetTempNexusBrightness({brightness}): 0x{hr:X8}");
                    if (hr == 0)
                    {
                        Console.WriteLine($"  [OK] INexusApi.SetTempNexusBrightness({brightness})!");
                        return true;
                    }
                    Release(nexus); nexus = default;
                }
                else Console.WriteLine($"  get_NexusApi({i}): 0x{hr:X8}");

                // ── Path B: IMessageApi (direct from controller vtable[34]) ─
                Console.WriteLine($"  Controller[{i}]: trying IMessageApi...");
                hr = VOut(icontrol, 34, out IntPtr msgApi);
                Console.WriteLine($"    get_MessageApi: 0x{hr:X8}");
                if (hr == 0 && msgApi != default)
                {
                    bool msgOk = await TrySendViaMessageApiAsync(msgApi, brightness);
                    Release(msgApi);
                    if (msgOk) return true;
                }

                // ── Path C: IMessageSender via AccessoryId ──────────────────
                Console.WriteLine($"  Controller[{i}]: trying IMessageSender.CreateForAccessoryId...");
                hr = VOut(icontrol, 6, out IntPtr accessoryId);  // get_AccessoryId → HSTRING
                Console.WriteLine($"    get_AccessoryId: 0x{hr:X8}");
                if (hr == 0 && accessoryId != default)
                {
                    bool sndOk = await TrySendViaMessageSenderAsync(accessoryId, brightness);
                    WindowsDeleteString(accessoryId);
                    if (sndOk) return true;
                }

                Release(icontrol); icontrol = default;
                Release(controller); controller = default;
            }
        }
        catch (Exception ex) { Console.WriteLine($"  Exception: {ex}"); }
        finally
        {
            WindowsDeleteString(classId);
            if (nexus      != default) Release(nexus);
            if (icontrol   != default) Release(icontrol);
            if (controller != default) Release(controller);
            if (vecView    != default) Release(vecView);
            if (factory    != default) Release(factory);
            if (statics    != default) Release(statics);
        }
        return false;
    }

    // ── Send via IMessageApi (obtained directly from IController.get_MessageApi) ─

    static async Task<bool> TrySendViaMessageApiAsync(IntPtr msgApi, byte brightness)
    {
        Console.WriteLine($"    IMessageApi.get_MaxMessageByteCount:");
        VUInt(msgApi, 6, out uint maxBytes);
        Console.WriteLine($"    maxMessageBytes={maxBytes}");

        IntPtr msgReqInsp = default, msgReq = default;
        try
        {
            if (!TryCreateMessageRequest(out msgReqInsp, out msgReq)) return false;

            // Try GIP (2) then IOCTL (1) transport
            foreach (int transport in new[] { 2, 1 })
            {
                string tname = transport == 2 ? "GIP" : "IOCTL";
                SetupMessageRequest(msgReq, brightness, transport);

                // IMessageApi.SendMessageAsync vtable[7]: fn(this, &asyncOp, request)
                int hr = VOutIn(msgApi, 7, out IntPtr asyncOp, msgReq);
                Console.WriteLine($"    SendMessageAsync({tname}): 0x{hr:X8}");
                if (hr < 0) continue;

                (hr, IntPtr resp) = await WaitAsyncAndGetResult(asyncOp);
                Console.WriteLine($"    GetResults({tname}): 0x{hr:X8}");
                if (resp != default) Release(resp);
                if (hr == 0) { Console.WriteLine("    [OK] IMessageApi succeeded!"); return true; }
            }
        }
        catch (Exception ex) { Console.WriteLine($"    IMessageApi exception: {ex.Message}"); }
        finally
        {
            if (msgReq  != default) Release(msgReq);
            if (msgReqInsp != default) Release(msgReqInsp);
        }
        return false;
    }

    // ── Send via IMessageSenderStatics.CreateForAccessoryId ────────────────

    static async Task<bool> TrySendViaMessageSenderAsync(IntPtr accessoryIdHstr, byte brightness)
    {
        IntPtr senderStatics = default, sender = default, iMsgSender = default;
        IntPtr msgReqInsp = default, msgReq = default;
        try
        {
            int hr = WindowsCreateString("Devices.Abstraction.MessageSender",
                "Devices.Abstraction.MessageSender".Length, out var senderClassId);
            if (hr != 0) return false;

            var ssIid = IID_IMessageSenderStatics;
            hr = RoGetActivationFactory(senderClassId, ref ssIid, out senderStatics);
            WindowsDeleteString(senderClassId);
            Console.WriteLine($"    RoGetActivationFactory(MessageSender): 0x{hr:X8}");
            if (hr != 0 || senderStatics == default) return false;

            // CreateForAccessoryId(out sender, accessoryId) — vtable[7]
            Console.WriteLine($"    accessoryIdHstr=0x{accessoryIdHstr:X16}");
            hr = VOutIn(senderStatics, 7, out sender, accessoryIdHstr);
            Console.WriteLine($"    CreateForAccessoryId: 0x{hr:X8} sender=0x{sender:X16}");

            if (hr != 0 || sender == default)
            {
                // CreateForAccessoryId returned null = BT HID controller not a GIP accessory
                // MessageSender only works for GIP controllers. Dead end for BT.
                Console.WriteLine("    CreateForAccessoryId: null sender — BT HID not a GIP target");
                return false;
            }

            // sender is already typed as IMessageSender — use directly
            iMsgSender = sender;

            if (!TryCreateMessageRequest(out msgReqInsp, out msgReq)) return false;

            foreach (int transport in new[] { 2, 1 })
            {
                string tname = transport == 2 ? "GIP" : "IOCTL";
                SetupMessageRequest(msgReq, brightness, transport);

                // IMessageSender.SendAsync vtable[8]: fn(this, &asyncOp, request)
                hr = VOutIn(iMsgSender, 8, out IntPtr asyncOp, msgReq);
                Console.WriteLine($"    MessageSender.SendAsync({tname}): 0x{hr:X8}");
                if (hr < 0) continue;

                (hr, IntPtr resp) = await WaitAsyncAndGetResult(asyncOp);
                Console.WriteLine($"    MessageSender.GetResults({tname}): 0x{hr:X8}");
                if (resp != default) Release(resp);
                if (hr == 0) { Console.WriteLine("    [OK] IMessageSender succeeded!"); return true; }
            }
        }
        catch (Exception ex) { Console.WriteLine($"    IMessageSender exception: {ex.Message}"); }
        finally
        {
            if (msgReq      != default) Release(msgReq);
            if (msgReqInsp  != default) Release(msgReqInsp);
            if (iMsgSender  != default) Release(iMsgSender);
            if (sender      != default) Release(sender);
            if (senderStatics != default) Release(senderStatics);
        }
        return false;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    static bool TryCreateMessageRequest(out IntPtr inspectable, out IntPtr msgReq)
    {
        inspectable = default; msgReq = default;
        const string cn = "Devices.Abstraction.MessageRequest";
        int hr = WindowsCreateString(cn, cn.Length, out var classId);
        if (hr != 0) return false;
        hr = RoActivateInstance(classId, out inspectable);
        WindowsDeleteString(classId);
        Console.WriteLine($"    RoActivateInstance(MessageRequest): 0x{hr:X8}");
        if (hr != 0 || inspectable == default) return false;

        var mrIid = IID_IMessageRequest;
        hr = QI(inspectable, ref mrIid, out msgReq);
        Console.WriteLine($"    QI IMessageRequest: 0x{hr:X8}");
        return hr == 0 && msgReq != default;
    }

    static void SetupMessageRequest(IntPtr msgReq, byte brightness, int transport)
    {
        // put_MessageId(0x0A = GipSetLed)   — vtable[7]
        VUIntArg(msgReq, 7, 0x0A);
        // put_Class(GipCommand = 4096)       — vtable[11]
        VIntArg(msgReq, 11, 4096);
        // put_Transport(GIP=2 or IOCTL=1)   — vtable[13]
        VIntArg(msgReq, 13, transport);
        // put_Data(null) — no payload for now, vtable[9]
        VPtrArg(msgReq, 9, IntPtr.Zero);
    }

    static async Task<(int hr, IntPtr result)> WaitAsyncAndGetResult(IntPtr asyncOp)
    {
        var aiIid = new Guid("00000036-0000-0000-C000-000000000046");
        QI(asyncOp, ref aiIid, out var asyncInfo);
        for (int t = 0; t < 30; t++)
        {
            int status = -1;
            if (asyncInfo != default) VInt(asyncInfo, 7, out status);
            if (status >= 1) break;
            await Task.Delay(100);
        }
        if (asyncInfo != default) Release(asyncInfo);

        // GetResults — vtable[8]
        int hr = VOut(asyncOp, 8, out IntPtr result);
        Release(asyncOp);
        return (hr, result);
    }
}
