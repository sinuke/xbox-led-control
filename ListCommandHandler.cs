namespace XboxLedControl;

internal static class ListCommandHandler
{
    internal static int Execute(ListOptions options)
    {
        var deviceIds = GipEnumerator.ReadAllDeviceIds(options.Verbose);

        if (deviceIds.Count == 0)
        {
            Console.WriteLine("No Xbox controllers found.");
            return 0;
        }

        Console.WriteLine($"{"#",-3}  {"Controller",-20}  {"Device ID",-17}");
        Console.WriteLine($"{"---",-3}  {"--------------------",-20}  {"-----------------",-17}");

        for (int i = 0; i < deviceIds.Count; i++)
        {
            string mac = BitConverter.ToString(deviceIds[i]).Replace('-', ':');
            Console.WriteLine($"{i + 1,-3}  {"Xbox Controller",-20}  {mac,-17}");
        }

        return 0;
    }
}
