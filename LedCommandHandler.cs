namespace XboxLedControl;

/// <summary>
/// Executes the <c>led</c> command given already-resolved <see cref="LedOptions"/>.
/// Owns the mapping from <see cref="LedCommand"/> to <see cref="GipLedPattern"/>
/// and the interaction with <see cref="GipDirectSender"/>.
/// </summary>
internal static class LedCommandHandler
{
    internal static int Execute(LedOptions options)
    {
        GipLedPattern gipPattern = options.Pattern switch
        {
            LedCommand.Off       => GipLedPattern.Off,
            LedCommand.On        => GipLedPattern.On,
            LedCommand.Ramp      => GipLedPattern.RampToLevel,
            LedCommand.FastBlink => GipLedPattern.FastBlink,
            LedCommand.SlowBlink => GipLedPattern.SlowBlink,
            LedCommand.Charging  => GipLedPattern.ChargingBlink,
            _ => throw new InvalidOperationException($"Unexpected LED pattern: {options.Pattern}")
        };

        byte[] frame = GipLedCommand.BuildRaw(gipPattern, options.Intensity);

        if (options.Verbose)
        {
            Console.WriteLine($"Pattern:   {gipPattern}");
            Console.WriteLine($"Intensity: {options.Intensity}/47");
            Console.WriteLine($"GIP frame: {BitConverter.ToString(frame)}");
            Console.WriteLine();
        }

        bool ok = GipDirectSender.TrySend(frame, options.Verbose);
        if (!ok)
            Console.Error.WriteLine("Failed: no USB Xbox controller found via \\\\.\\XboxGIP.");

        return ok ? 0 : 1;
    }
}
