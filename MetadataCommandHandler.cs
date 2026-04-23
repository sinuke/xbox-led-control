namespace XboxLedControl;

/// <summary>
/// Executes the <c>metadata</c> command given already-resolved <see cref="MetadataOptions"/>.
/// Retrieves the GIP metadata blob via <see cref="GipMetadataReceiver"/> and decodes it
/// with <see cref="GipMetadataDecoder"/>.
/// </summary>
internal static class MetadataCommandHandler
{
    internal static int Execute(MetadataOptions options)
    {
        byte[]? blob = GipMetadataReceiver.TryReceive(options.Verbose, options.DeviceId);
        if (blob is null)
            return 1;

        if (options.Verbose)
        {
            Console.WriteLine($"Metadata blob: {blob.Length} bytes");
            Console.Write(GipMetadataDecoder.FormatHexDump(blob));
            Console.WriteLine();
        }

        string? decoded = GipMetadataDecoder.Decode(blob);
        if (decoded is null)
        {
            Console.Error.WriteLine($"Failed to decode metadata (blob too small: {blob.Length} bytes).");
            return 1;
        }

        Console.Write(decoded);
        return 0;
    }
}
