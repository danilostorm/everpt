using System;
using PristonTale;
using PristonTale.Display;
using PristonTale.Engine3D.Diagnostics;

if (args.Length == 2 && args[0] == "--validate-display")
{
    // The game is disposed on the thread that ran it (SDL and Vulkan objects belong to it) before the report is awaited.
    var probe = new DisplayValidationProbe(args[1]);
    using (probe)
    {
        probe.Run();
    }

    await probe.WriteReportAsync();
    return 0;
}

string settingsPath = null;
if (args.Length == 2 && args[0] == "--settings")
{
    settingsPath = args[1];
}
else if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: PristonTale [--settings PATH | --validate-display REPORT.json]");
    return 1;
}

using var log = ClientLogFile.Open(AppContext.BaseDirectory);
using var game = new Game1(settingsPath);
game.Run();
return 0;
