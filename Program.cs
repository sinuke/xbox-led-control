using XboxLedControl;

if (!CliParser.TryParse(args, out AppOptions? options, out int exitCode))
    return exitCode;

return CommandDispatcher.Dispatch(options!);
