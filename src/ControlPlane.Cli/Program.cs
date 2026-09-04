using System.CommandLine;
using ControlPlane.Cli.Commands;

namespace ControlPlane.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("ControlPlane Standby Runner & Maintenance CLI");

        rootCommand.AddCommand(ServeCommand.Create());
        rootCommand.AddCommand(TakeoverCommand.Create());

        return await rootCommand.InvokeAsync(args);
    }
}
