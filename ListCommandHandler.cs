namespace XboxLedControl;

internal static class ListCommandHandler
{
    internal static int Execute(ListOptions options)
    {
        var controllers = GipEnumerator.ReadAllControllers(options.Verbose);

        if (controllers.Count == 0)
        {
            Console.WriteLine("No Xbox controllers found.");
            return 0;
        }

        Console.WriteLine($"{"#",-3}  {"Controller",-28}  {"Device ID",-17}");
        Console.WriteLine($"{"---",-3}  {"----------------------------",-28}  {"-----------------",-17}");

        for (int i = 0; i < controllers.Count; i++)
        {
            var info = controllers[i];
            string mac  = BitConverter.ToString(info.Mac).Replace('-', ':');
            string name = GipMetadataDecoder.ProductName(info.VendorId, info.ProductId);
            Console.WriteLine($"{i + 1,-3}  {name,-28}  {mac,-17}");
        }

        return 0;
    }
}
