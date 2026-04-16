namespace XboxLedControl;

/// <summary>
/// Application-level LED operations, independent of the GIP protocol enum.
/// </summary>
internal enum LedCommand
{
    Off,
    On,
    Ramp,
    FastBlink,
    SlowBlink,
    Charging,
}

/// <summary>
/// Base class for all command option records.
/// Contains options shared across every subcommand (e.g. <see cref="Verbose"/>).
/// Populated by <see cref="CliParser"/> and dispatched via <see cref="CommandDispatcher"/>.
/// </summary>
internal abstract record AppOptions(bool Verbose);

/// <summary>Options for the <c>led</c> subcommand.</summary>
internal sealed record LedOptions(bool Verbose, LedCommand Pattern, byte Intensity)
    : AppOptions(Verbose);

