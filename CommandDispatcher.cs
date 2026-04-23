namespace XboxLedControl;

/// <summary>
/// Routes a populated <see cref="AppOptions"/> to the appropriate command handler.
/// To support a new subcommand, add a case to the switch expression and a new handler class.
/// </summary>
internal static class CommandDispatcher
{
    internal static int Dispatch(AppOptions options) => options switch
    {
        LedOptions      led      => LedCommandHandler.Execute(led),
        ListOptions     list     => ListCommandHandler.Execute(list),
        MetadataOptions metadata => MetadataCommandHandler.Execute(metadata),
        _ => throw new InvalidOperationException($"No handler registered for options type '{options.GetType().Name}'.")
    };
}
