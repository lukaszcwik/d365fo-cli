using D365FO.Cli.Commands.Agent;
using Spectre.Console;

namespace D365FO.Cli.Tests;

public class ModifyHelpRenderTests
{
    public static IEnumerable<object[]> ModifyCommands => SchemaCommand.Commands()
        .Where(command => command.Command.StartsWith("modify ", StringComparison.Ordinal))
        .Select(command => new object[] { command.Command });

    [Theory]
    [MemberData(nameof(ModifyCommands))]
    public void Help_renders_without_treating_json_arrays_as_markup(string command)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = 500;

        Assert.Equal(0, CliApp.Build(console).Run(command.Split(' ').Append("--help").ToArray()));
        if (command == "modify batch")
        {
            var help = writer.ToString();
            Assert.Contains("[{\"operation\":\"add-field\"", help);
            Assert.Contains("\"fields\":[\"Note\"]}]", help);
            Assert.Contains("--operations-file", help);
        }
    }
}
