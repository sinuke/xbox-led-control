using System.CommandLine;

namespace XboxLedControl;

/*
 * Xbox Controller LED Control
 * ===========================
 * Controls the Guide-button LED on a USB-connected Xbox One / Series controller.
 * Sends a GIP LED command directly via \\.\XboxGIP (xboxgip.sys).
 * No admin rights required.
 *
 * Usage:  xbox-led-control led [-v|--verbose] <intensity|pattern>
 *
 * Protocol: MS-GIPUSB §3.1.5.5.7 (LED eButton Command)
 */

/// <summary>
/// Owns all System.CommandLine symbol definitions and parses <c>args</c> into a typed
/// <see cref="AppOptions"/> subclass. Does not execute any application logic.
/// To add a new flag or subcommand, start here — handler and dispatcher code stays untouched.
/// </summary>
internal static class CliParser
{
    /// <summary>
    /// Parses <paramref name="args"/> using System.CommandLine.
    /// Returns <see langword="true"/> and a populated <paramref name="options"/> when a
    /// user-defined subcommand was matched successfully.
    /// Returns <see langword="false"/> when System.CommandLine handled the invocation
    /// itself (e.g. <c>--help</c>, <c>--version</c>, parse error); the caller should
    /// exit with <paramref name="exitCode"/>.
    /// </summary>
    internal static bool TryParse(string[] args, out AppOptions? options, out int exitCode)
    {
        AppOptions? captured = null;

        // --- Shared options (recursive = available to all subcommands) ---

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Print verbose output (device ID, frame bytes, send result)",
            Recursive   = true,
        };
        verboseOption.Aliases.Add("-v");

        // --- 'led' subcommand ---

        Argument<string> valueArg = new("value")
        {
            Description = "Intensity 0-100, or pattern: off | on | ramp | fastblink | slowblink | charging"
        };
        valueArg.Validators.Add(result =>
        {
            string v = result.Tokens.Count > 0 ? result.Tokens[0].Value : string.Empty;
            if (!IsValidLedValue(v))
                result.AddError(
                    $"'{v}' is not a valid value. " +
                    "Use a number 0-100 or one of: off, on, ramp, fastblink, slowblink, charging.");
        });

        Command ledCommand = new("led", "Set the Guide button LED intensity or pattern.");
        ledCommand.Arguments.Add(valueArg);

        ledCommand.SetAction(parseResult =>
        {
            var (pattern, intensity) = ParseLedValue(parseResult.GetValue(valueArg)!);
            captured = new LedOptions(
                Verbose:   parseResult.GetValue(verboseOption),
                Pattern:   pattern,
                Intensity: intensity
            );
            return 0;
        });

        // --- Root command ---

        RootCommand rootCommand = new("Controls the Guide-button LED on a USB-connected Xbox One / Series controller.");
        rootCommand.Options.Add(verboseOption);
        rootCommand.Subcommands.Add(ledCommand);

        exitCode = rootCommand.Parse(args).Invoke();

        if (captured is not null)
        {
            options = captured;
            return true;
        }

        options = null;
        return false;
    }

    // -----------------------------------------------------------------
    // LED value helpers
    // -----------------------------------------------------------------

    private static bool IsValidLedValue(string v) =>
        v.ToLowerInvariant() switch
        {
            "off" or "on" or "ramp" or "fastblink" or "slowblink" or "charging" => true,
            _ => int.TryParse(v, out int n) && n >= 0 && n <= 100
        };

    private static (LedCommand pattern, byte intensity) ParseLedValue(string v) =>
        v.ToLowerInvariant() switch
        {
            "off"       => (LedCommand.Off,       0),
            "on"        => (LedCommand.On,        47),
            "ramp"      => (LedCommand.Ramp,      47),
            "fastblink" => (LedCommand.FastBlink, 47),
            "slowblink" => (LedCommand.SlowBlink, 47),
            "charging"  => (LedCommand.Charging,  47),
            _           => ParseNumericValue(int.Parse(v))
        };

    private static (LedCommand, byte) ParseNumericValue(int n) =>
        n == 0
            ? (LedCommand.Off, (byte)0)
            : (LedCommand.On, GipLedCommand.ScaleIntensity((byte)n));
}
