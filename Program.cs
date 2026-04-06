using XboxLedControl;

/*
 * Xbox Controller LED Control
 * ===========================
 * Controls the Guide-button LED on a USB-connected Xbox One / Series controller.
 * Sends a GIP LED command directly via \\.\XboxGIP (xboxgip.sys).
 * No admin rights required.
 *
 * Usage:  XboxLedControl [--debug] <brightness|pattern>
 *
 * Protocol: MS-GIPUSB §3.1.5.5.7 (LED eButton Command)
 */

bool debug     = args.Any(a => a.Equals("--debug", StringComparison.OrdinalIgnoreCase));
string[] valueArgs = args.Where(a => !a.StartsWith("--")).ToArray();

if (valueArgs.Length == 0)
{
    Console.Error.WriteLine("Usage: XboxLedControl [--debug] <brightness|pattern>");
    Console.Error.WriteLine("  brightness:  0-100");
    Console.Error.WriteLine("  pattern:     off | on | ramp | fastblink | slowblink | charging");
    return 1;
}

string cmdArg = valueArgs[0].Trim().ToLowerInvariant();
var (pattern, intensity) = ParseLedArg(cmdArg);
byte[] frame = GipLedCommand.BuildRaw(pattern, intensity);

if (debug)
{
    Console.WriteLine($"Pattern:   {pattern}");
    Console.WriteLine($"Intensity: {intensity}/47");
    Console.WriteLine($"GIP frame: {BitConverter.ToString(frame)}");
    Console.WriteLine();
}

bool ok = GipDirectSender.TrySend(frame, debug);
if (!ok)
    Console.Error.WriteLine("Failed: no USB Xbox controller found via \\\\.\\XboxGIP.");
return ok ? 0 : 1;

static (GipLedPattern pattern, byte intensity) ParseLedArg(string arg) =>
    arg switch
    {
        "off"  or "0"                            => (GipLedPattern.Off,            0),
        "on"   or "solid"                        => (GipLedPattern.On,            47),
        "ramp" or "breathe" or "breath" or "fade"
                                                 => (GipLedPattern.RampToLevel,   47),
        "fastblink" or "blink" or "blink1"       => (GipLedPattern.FastBlink,     47),
        "slowblink" or "slow"  or "blink2"       => (GipLedPattern.SlowBlink,     47),
        "charging"  or "charge"                  => (GipLedPattern.ChargingBlink, 47),
        "full" or "max" or "100"                 => (GipLedPattern.On,            47),
        _ when byte.TryParse(arg, out byte b)    => b == 0
                                                     ? (GipLedPattern.Off, (byte)0)
                                                     : (GipLedPattern.On, GipLedCommand.ScaleIntensity(b)),
        _                                        => FallbackOff(arg),
    };

static (GipLedPattern, byte) FallbackOff(string a)
{
    Console.Error.WriteLine($"Unknown argument '{a}', defaulting to off.");
    return (GipLedPattern.Off, 0);
}
