using Windows.Gaming.Input;
using Windows.Gaming.Input.Custom;

namespace XboxLedControl;

/// <summary>
/// Sends GIP LED commands via Windows.Gaming.Input.Custom GIP factory.
///
/// Two registration strategies:
///
/// 1. RegisterCustomFactoryForHardwareId (VID/PID)
///    → For BT Xbox controllers Windows bridges BTHXHID→XUSB, so we receive an
///      XusbGameControllerProvider (SetVibration only — no LED method).
///
/// 2. RegisterCustomFactoryForGipInterface (GIP interface GUID)
///    → If the controller exposes a GIP interface (typical over USB, possibly over
///      BT too) we receive a GipGameControllerProvider, which has SendMessage() for
///      arbitrary GIP commands including GipSetLed (0x0A).
///
/// Known Xbox Series X/S GIP interface GUIDs (from driver INF / reverse-engineering):
///   {5eac7de0-2d8d-42e1-b64c-ea3b46c5df35}  — Series X/S (fw 0x05xx)
///   {A9FEE80F-CA9F-4F62-8AC8-78E48E3EEF57}  — Xbox One (tested)
/// </summary>
public static class GipMessageSender
{
    // GIP interface GUIDs to try for Xbox Series X/S controllers
    private static readonly Guid[] GipInterfaceGuids =
    [
        new("5eac7de0-2d8d-42e1-b64c-ea3b46c5df35"),  // Series X/S observed
        new("A9FEE80F-CA9F-4F62-8AC8-78E48E3EEF57"),  // Xbox One
        new("6dbb7a25-2484-4fd8-ab89-4c4dbd85a753"),  // alt Xbox One
        new("00000001-0000-0000-c000-000000000046"),  // IUnknown (wildcard test)
    ];

    private static TaskCompletionSource<GipGameControllerProvider?> _gipTcs  = new();
    private static TaskCompletionSource<XusbGameControllerProvider?> _xusbTcs = new();

    /// <summary>
    /// LED payload = 8 bytes: [mode][brightness][period][R][G][B][on_period][off_period]
    /// </summary>
    public static async Task<bool> TrySendAsync(ushort vid, ushort pid,
                                                byte[] ledPayload,
                                                int timeoutMs = 5000)
    {
        if (ledPayload.Length != 8)
            throw new ArgumentException("LED payload must be exactly 8 bytes.", nameof(ledPayload));

        // ── Strategy 1: GIP interface GUID ────────────────────────────────────
        // Might give GipGameControllerProvider (needed for SendMessage).
        Console.WriteLine("  Trying RegisterCustomFactoryForGipInterface...");
        foreach (var guid in GipInterfaceGuids)
        {
            _gipTcs = new TaskCompletionSource<GipGameControllerProvider?>();
            var gipFactory = new GipFactory(p => _gipTcs.TrySetResult(p));
            try
            {
                GameControllerFactoryManager.RegisterCustomFactoryForGipInterface(gipFactory, guid);
                Console.Write($"    Registered GUID {guid} — waiting... ");
                var p = await Task.WhenAny(_gipTcs.Task,
                    Task.Delay(1500).ContinueWith(_ => (GipGameControllerProvider?)null))
                    .Unwrap();
                if (p != null)
                {
                    Console.WriteLine("found!");
                    return await SendViaGipProvider(p, ledPayload);
                }
                Console.WriteLine("no callback.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"failed ({ex.Message})");
            }
        }

        // ── Strategy 2: Hardware ID (gives XusbGameControllerProvider for BT) ─
        Console.WriteLine("  Trying RegisterCustomFactoryForHardwareId (HW ID)...");
        _xusbTcs = new TaskCompletionSource<XusbGameControllerProvider?>();
        var hwFactory = new HwIdFactory(
            onXusb: p => _xusbTcs.TrySetResult(p),
            onGip:  p => _gipTcs.TrySetResult(p));
        try
        {
            GameControllerFactoryManager.RegisterCustomFactoryForHardwareId(hwFactory, vid, pid);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  RegisterCustomFactoryForHardwareId failed: {ex.Message}");
            return false;
        }

        Console.Write("  Waiting for provider (HW ID)... ");
        var xp = await Task.WhenAny(_xusbTcs.Task,
                    Task.Delay(timeoutMs).ContinueWith(_ => (XusbGameControllerProvider?)null))
                    .Unwrap();
        var gp = _gipTcs.Task.IsCompleted ? _gipTcs.Task.Result : null;

        if (gp != null)
        {
            Console.WriteLine("got GipGameControllerProvider!");
            return await SendViaGipProvider(gp, ledPayload);
        }

        if (xp != null)
        {
            Console.WriteLine("got XusbGameControllerProvider (BT bridge mode).");
            Console.WriteLine("  XusbGameControllerProvider has no LED method — cannot send.");
            return false;
        }

        Console.WriteLine("timeout.");
        return false;
    }

    private static Task<bool> SendViaGipProvider(GipGameControllerProvider provider, byte[] ledPayload)
    {
        Console.WriteLine($"  Payload (LED data): {BitConverter.ToString(ledPayload)}");
        try
        {
            // CsWinRT projects IBuffer params as byte[] in .NET
            provider.SendMessage(GipMessageClass.Command, 0x0A, ledPayload);
            Console.WriteLine("  [OK] GipGameControllerProvider.SendMessage(Command, 0x0A, ...)");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [FAIL] SendMessage: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    // ── Inner factory classes ─────────────────────────────────────────────────

    // Placeholder WinRT object returned from CreateGameController.
    // Must not be null — native WinRT code dereferences it immediately.
    // Any boxed WinRT value works; we use an integer via PropertyValue.
    private static object DummyInspectable() => Windows.Foundation.PropertyValue.CreateInt32(0);

    private sealed class GipFactory : ICustomGameControllerFactory
    {
        private readonly Action<GipGameControllerProvider> _onGip;
        public GipFactory(Action<GipGameControllerProvider> onGip) => _onGip = onGip;

        public object CreateGameController(IGameControllerProvider provider)
        {
            Console.WriteLine($"    [GipFactory] CreateGameController: {provider.GetType().Name}");
            if (provider is GipGameControllerProvider gip) _onGip(gip);
            return DummyInspectable();
        }
        public void OnGameControllerAdded(IGameController value) { }
        public void OnGameControllerRemoved(IGameController value) { }
    }

    private sealed class HwIdFactory : ICustomGameControllerFactory
    {
        private readonly Action<XusbGameControllerProvider> _onXusb;
        private readonly Action<GipGameControllerProvider>  _onGip;

        public HwIdFactory(Action<XusbGameControllerProvider> onXusb,
                           Action<GipGameControllerProvider>  onGip)
        { _onXusb = onXusb; _onGip = onGip; }

        public object CreateGameController(IGameControllerProvider provider)
        {
            Console.WriteLine($"  [HwIdFactory] CreateGameController: {provider.GetType().Name}");
            if (provider is GipGameControllerProvider  gip)  _onGip(gip);
            if (provider is XusbGameControllerProvider xusb) _onXusb(xusb);
            return DummyInspectable();
        }
        public void OnGameControllerAdded(IGameController value) { }
        public void OnGameControllerRemoved(IGameController value) { }
    }
}
