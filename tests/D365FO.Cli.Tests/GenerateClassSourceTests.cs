using System.Xml.Linq;
using System.Text.Json;
using D365FO.Cli.Commands.Generate;
using D365FO.Core.Index;
using D365FO.Mcp;
using Xunit;

namespace D365FO.Cli.Tests;

[Collection("EnvIndexDb")]
public sealed class GenerateClassSourceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "d365-class-cli-" + Guid.NewGuid().ToString("N"));
    public GenerateClassSourceTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Cli_writes_declaration_and_multiple_methods_from_xpp_files()
    {
        var declaration = Path.Combine(root, "declaration.xpp");
        var method = Path.Combine(root, "run.xpp");
        var second = Path.Combine(root, "enabled.xpp");
        var output = Path.Combine(root, "Example.xml");
        File.WriteAllText(declaration, "internal final class Example { private int count; }");
        File.WriteAllText(method, "public void run() { count++; }");
        File.WriteAllText(second, "public static Example instance() { return new Example(); }");
        Assert.Equal(0, CliApp.Build().Run(["generate", "class", "Example", "--declaration-file", declaration,
            "--method-file", method, "--method-file", second, "--out", output, "--output", "json"]));
        var doc = XDocument.Load(output);
        Assert.Equal("internal final class Example { private int count; }", doc.Descendants("Declaration").Single().Value);
        Assert.Equal(2, doc.Descendants("Method").Count());
        Assert.Equal(1, CliApp.Build().Run(["generate", "class", "Example", "--declaration-file", declaration,
            "--out", output, "--output", "json"]));
        Assert.Equal(2, XDocument.Load(output).Descendants("Method").Count());
    }

    [Fact]
    public void Missing_source_and_invalid_declaration_produce_no_output()
    {
        var output = Path.Combine(root, "Example.xml");
        Assert.NotEqual(0, CliApp.Build().Run(["generate", "class", "Example", "--method-file", Path.Combine(root, "missing.xpp"), "--out", output]));
        Assert.False(File.Exists(output));
        var declaration = Path.Combine(root, "declaration.xpp");
        File.WriteAllText(declaration, "class WrongName { }");
        Assert.NotEqual(0, CliApp.Build().Run(["generate", "class", "Example", "--declaration-file", declaration, "--out", output]));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Source_paths_are_not_reported_as_ignored_metadata_properties()
    {
        var requested = GenerateInstaller.RequestedValues(new GenerateClassCommand.Settings
        { Name = "Example", DeclarationFile = "declaration.xpp", MethodFiles = ["run.xpp"] });
        Assert.Equal(new[] { "<NAME>" }, requested.Select(r => r.Option));
    }

    [Fact]
    public void Mcp_routes_the_same_source_files_into_the_generated_class()
    {
        var db = Path.Combine(root, "index.sqlite");
        var repo = new MetadataRepository(db);
        repo.EnsureSchema();
        try
        {
            var declaration = Path.Combine(root, "declaration.xpp");
            var method = Path.Combine(root, "instance.xpp");
            var output = Path.Combine(root, "Example.xml");
            File.WriteAllText(declaration, "internal final class Example { }");
            File.WriteAllText(method, "public static Example instance() { return new Example(); }");
            using var input = JsonDocument.Parse(JsonSerializer.Serialize(new
            { objectType = "class", name = "Example", declarationFile = declaration, methodFiles = new[] { method }, @out = output }));
            var result = Assert.IsType<D365FO.Core.ToolResult<object>>(
                ToolCatalog.All.Single(d => d.Name == "generate_object").Invoke(new ToolHandlers(repo), input.RootElement));
            Assert.True(result.Ok, result.Error?.Message);
            Assert.Equal("instance", XDocument.Load(output).Descendants("Method").Single().Element("Name")!.Value);
        }
        finally
        {
            foreach (var mode in new[] { Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate, Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly })
            {
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                { DataSource = db, Mode = mode, Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Private, Pooling = true }.ToString());
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
            }
        }
    }
}
