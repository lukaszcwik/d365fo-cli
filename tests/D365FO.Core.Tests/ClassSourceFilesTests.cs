using System.Xml.Linq;
using D365FO.Core.Scaffolding;
using Xunit;

namespace D365FO.Core.Tests;

public sealed class ClassSourceFilesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "d365-class-source-" + Guid.NewGuid().ToString("N"));
    public ClassSourceFilesTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true);
    private string Source(string name, string content)
    {
        var file = Path.Combine(root, name + ".xpp");
        File.WriteAllText(file, content);
        return file;
    }

    [Fact]
    public void Feature_declaration_and_method_files_become_canonical_source_nodes()
    {
        const string declaration = "[ExportAttribute(identifierStr(Microsoft.Dynamics.ApplicationPlatform.FeatureExposure.IFeatureMetadata))]\ninternal final class ABCFeature implements IFeatureMetadata\n{\n    private static ABCFeature singleton;\n}";
        const string instance = "public static ABCFeature instance()\n{\n    if (!singleton) { singleton = new ABCFeature(); }\n    return singleton;\n}";
        const string label = "public str label() { return '@ABC:Feature'; }";
        var doc = ClassSourceFiles.Create("ABCFeature", null, false, Source("declaration", declaration),
            [Source("instance", instance), Source("label", label)]);
        var roundTrip = XDocument.Parse(doc.ToString());
        var code = roundTrip.Root!.Element("SourceCode")!;
        Assert.Equal(declaration, code.Element("Declaration")!.Value);
        Assert.Null(roundTrip.Root.Element("Methods"));
        Assert.Equal(new[] { "instance", "label" }, code.Element("Methods")!.Elements("Method").Select(m => m.Element("Name")!.Value));
        Assert.Equal(instance, code.Element("Methods")!.Elements("Method").First().Element("Source")!.Value);
        var violations = new List<D365FO.Core.Validation.XppViolation>();
        D365FO.Core.Validation.AotMethodSourceRules.Check(doc.ToString(), violations);
        Assert.Empty(violations);
    }

    [Fact]
    public void Method_literals_and_comments_do_not_affect_structure()
    {
        const string method = "public str text() { /* } ignored { */ return 'braces {} and markup <x> and ]]> are text'; }";
        var doc = ClassSourceFiles.Create("Example", "SysOperationServiceBase", false, null, [Source("text", method)]);
        Assert.Equal(method, XDocument.Parse(doc.ToString()).Descendants("Source").Single().Value);
        Assert.Contains("extends SysOperationServiceBase", doc.Descendants("Declaration").Single().Value);
    }

    [Theory]
    [InlineData("class Other { }")]
    [InlineData("<AxClass><Name>Example</Name></AxClass>")]
    [InlineData("class Example { public void run() { } }")]
    [InlineData("class Example { public abstract void run(); }")]
    [InlineData("class Example { } class Extra { }")]
    [InlineData("class Example { #Macro }")]
    public void Invalid_or_method_containing_declarations_are_refused(string declaration)
        => Assert.Throws<ArgumentException>(() => ClassSourceFiles.Create("Example", null, false, Source("decl", declaration)));

    [Theory]
    [InlineData("public void run() {")]
    [InlineData("public void run() { } public void other() { }")]
    [InlineData("public void run() { } trailing")]
    [InlineData("public void run(); public void other() { }")]
    [InlineData("class Example { public void run() { } }")]
    public void Method_files_must_contain_one_complete_method(string method)
        => Assert.Throws<ArgumentException>(() => ClassSourceFiles.Create("Example", null, false, null, [Source("method", method)]));

    [Fact]
    public void Duplicate_methods_and_conflicting_header_options_are_refused()
    {
        Assert.Throws<ArgumentException>(() => ClassSourceFiles.Create("Example", null, false, null,
            [Source("a", "public void run() { }"), Source("b", "public void RUN() { }")]));
        Assert.Throws<ArgumentException>(() => ClassSourceFiles.Create("Example", "Base", false, Source("decl", "class Example { }")));
        Assert.Throws<ArgumentException>(() => ClassSourceFiles.Create("Example", null, true, Source("decl", "class Example { }")));
    }
}
