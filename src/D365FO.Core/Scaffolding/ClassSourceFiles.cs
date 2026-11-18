using System.Text.RegularExpressions;
using System.Xml.Linq;
using D365FO.Core.Validation;

namespace D365FO.Core.Scaffolding;

/// <summary>Turns X++ source files into structured AxClass metadata, without caller-authored XML.</summary>
public static class ClassSourceFiles
{
    public static XDocument Create(string name, string? extends, bool nonFinal,
        string? declarationFile = null, IEnumerable<string>? methodFiles = null)
    {
        if (!Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_]*\z"))
            throw new ArgumentException("Class name must be an X++ identifier.");
        var doc = XppScaffolder.Class(name, extends, !nonFinal);
        var sourceCode = doc.Root!.Element("SourceCode")!;
        if (declarationFile is not null)
        {
            if (!string.IsNullOrWhiteSpace(extends) || nonFinal)
                throw new ArgumentException("--declaration-file supplies the complete declaration; do not combine it with --extends or --non-final.");
            var declaration = File.ReadAllText(declarationFile);
            CheckDeclaration(name, declaration);
            sourceCode.Element("Declaration")!.ReplaceNodes(new XCData(declaration));
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var methods = new XElement("Methods");
        foreach (var file in methodFiles ?? [])
        {
            var source = File.ReadAllText(file);
            var masked = AotMethodSourceRules.BlankComments(source);
            CheckBraces(masked);
            var bodyStart = masked.IndexOf('{');
            var bodyEnd = masked.LastIndexOf('}');
            if (bodyStart < 0 || masked[..bodyStart].Contains(';') || !string.IsNullOrWhiteSpace(masked[(bodyEnd + 1)..]))
                throw new ArgumentException($"Method file '{file}' must contain only one complete method.");
            var declared = AotMethodSourceRules.DeclaredMethodNames(masked);
            if (declared is null || declared.Count != 1 || !Regex.IsMatch(declared[0], @"\A[A-Za-z_][A-Za-z0-9_]*\z"))
                throw new ArgumentException($"Method file '{file}' must declare exactly one complete X++ method with a body.");
            if (!names.Add(declared[0])) throw new ArgumentException($"Duplicate method '{declared[0]}'.");
            methods.Add(new XElement("Method", new XElement("Name", declared[0]), new XElement("Source", new XCData(source))));
        }
        if (methods.HasElements) sourceCode.Add(methods);
        return doc;
    }

    private static void CheckDeclaration(string name, string source)
    {
        var masked = AotMethodSourceRules.BlankComments(source);
        var open = masked.IndexOf('{');
        var close = masked.LastIndexOf('}');
        if (open < 0 || close < open) throw new ArgumentException("The class declaration must include its outer braces and member variables; supply methods in --method-file inputs.");
        var header = masked[..open];
        var match = Regex.Match(header,
            @"\A\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|final|abstract|static)\s+)*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s+extends\s+[A-Za-z_][A-Za-z0-9_.]*)?(?:\s+implements\s+[A-Za-z_][A-Za-z0-9_.]*(?:\s*,\s*[A-Za-z_][A-Za-z0-9_.]*)*)?\s*\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !string.Equals(match.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"The declaration must declare class '{name}', with optional attributes, modifiers, base class and interfaces.");
        var body = masked[(open + 1)..close];
        if (!string.IsNullOrWhiteSpace(masked[(close + 1)..]) || body.Contains('{') || body.Contains('}') || body.Contains('#'))
            throw new ArgumentException("The declaration may contain member variables only; put methods in separate --method-file inputs. Macro-expanded declarations are not supported.");
        foreach (var statement in body.Split(';'))
            if (!statement.Contains('=') && Regex.IsMatch(statement, @"\b\w+\s+\w+\s*\("))
                throw new ArgumentException("Method declarations belong in --method-file inputs, not the class declaration.");
    }

    private static void CheckBraces(string masked)
    {
        var depth = 0;
        foreach (var c in masked)
        {
            if (c == '{') depth++;
            if (c == '}' && --depth < 0) throw new ArgumentException("Method source has unbalanced braces.");
        }
        if (depth != 0) throw new ArgumentException("Method source has unbalanced braces.");
    }
}
