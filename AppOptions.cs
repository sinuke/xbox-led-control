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

/// <summary>
/// Base for commands that target a specific controller.
/// <see cref="DeviceId"/> overrides auto-discovery when provided.
/// Commands that don't need device targeting (e.g. future <c>list</c>) inherit
/// <see cref="AppOptions"/> directly and never receive this option.
/// </summary>
internal abstract record DeviceTargetedOptions(bool Verbose, byte[]? DeviceId)
    : AppOptions(Verbose);

/// <summary>Options for the <c>led</c> subcommand.</summary>
internal sealed record LedOptions(bool Verbose, byte[]? DeviceId, LedCommand Pattern, byte Intensity)
    : DeviceTargetedOptions(Verbose, DeviceId);

