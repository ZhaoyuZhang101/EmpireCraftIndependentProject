using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json.Linq;

// PortSurvey graph <sources> <graph.json> <core folder> [<more source folders>...]
//
// What every member of the rewritten mod sources lacks, and which other members it
// uses. `suspend` records only the first error that took a member out, and a member
// taken out early hides what else it leans on; this reads the sources with the
// suspended parts switched back on (EC_PENDING defined) and writes down, per member,
// all its own errors and all the members of the sources it refers to. From that
// Tools/PortTool/entry_needs.py works out everything a system's entry point still
// needs before it can compile.
public static class Graph
{
    sealed class Unit
    {
        public int Id; public string Name, File; public int Line;
        public readonly HashSet<string> Errors = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<int> Refs = new HashSet<int>();
    }

    public static int Run(string sources, string output, IEnumerable<string> folders)
    {
        var withPending = new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: new[] { "EC_PENDING" });
        var plain = new CSharpParseOptions(LanguageVersion.CSharp9);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Where(p => !Path.GetFileName(p).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
        var others = new List<SyntaxTree>();
        foreach (string folder in folders)
            foreach (string file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
                if (!file.Contains(Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar))
                    others.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), plain, file));
        var relative = new Dictionary<SyntaxTree, string>();
        foreach (string file in Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories))
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), withPending, file);
            relative[tree] = file.Substring(sources.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
        }
        var compilation = CSharpCompilation.Create("graph", relative.Keys.Concat(others), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var units = new ConcurrentDictionary<SyntaxNode, Unit>();
        int next = 0;
        Unit UnitOf(SyntaxNode node)
        {
            SyntaxNode found = null;
            for (var current = node; current != null && found == null; current = current.Parent)
                if (current is BaseMethodDeclarationSyntax || current is BasePropertyDeclarationSyntax || current is BaseFieldDeclarationSyntax ||
                    current is BaseTypeDeclarationSyntax || current is DelegateDeclarationSyntax)
                    found = current;
            if (found == null) return null;
            // An enum member belongs to its enum; an accessor or a local function to its member.
            return units.GetOrAdd(found, declaration =>
            {
                string owner = declaration.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text;
                return new Unit
                {
                    Id = System.Threading.Interlocked.Increment(ref next) - 1,
                    Name = (owner == null ? "" : owner + ".") + NameOf(declaration),
                    File = relative[declaration.SyntaxTree],
                    Line = declaration.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                };
            });
        }

        Parallel.ForEach(relative.Keys, tree =>
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var d in model.GetDiagnostics())
            {
                if (d.Severity != DiagnosticSeverity.Error || !d.Location.IsInSource) continue;
                var span = d.Location.SourceSpan;
                if (span.Start >= root.FullSpan.End) continue;
                var unit = UnitOf(root.FindNode(span, getInnermostNodeForTie: true));
                if (unit != null) lock (unit) unit.Errors.Add(d.Id + ": " + d.GetMessage());
            }
            foreach (var node in root.DescendantNodes())
            {
                if (!(node is SimpleNameSyntax)) continue;
                var symbol = model.GetSymbolInfo(node).Symbol;
                if (symbol == null) continue;
                if (symbol is IMethodSymbol method) symbol = (method.ReducedFrom ?? method).OriginalDefinition;
                else symbol = symbol.OriginalDefinition;
                if (!(symbol is IMethodSymbol || symbol is IPropertySymbol || symbol is IFieldSymbol || symbol is IEventSymbol || symbol is INamedTypeSymbol)) continue;
                Unit from = null;
                foreach (var declared in symbol.DeclaringSyntaxReferences)
                {
                    if (!relative.ContainsKey(declared.SyntaxTree)) continue;
                    var to = UnitOf(declared.GetSyntax());
                    if (to == null) continue;
                    from = from ?? UnitOf(node);
                    if (from == null) break;
                    if (from != to) lock (from) from.Refs.Add(to.Id);
                }
            }
            // A member cannot stand without the type it is declared in.
            foreach (var type in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                var holder = UnitOf(type);
                foreach (var member in type.ChildNodes())
                {
                    if (!(member is MemberDeclarationSyntax)) continue;
                    var unit = UnitOf(member);
                    if (unit != null && unit != holder) lock (unit) unit.Refs.Add(holder.Id);
                }
            }
        });

        // Numbers handed out in parallel can skip; references are written by position in the list.
        var ordered = units.Values.OrderBy(u => u.File, StringComparer.Ordinal).ThenBy(u => u.Line).ThenBy(u => u.Name, StringComparer.Ordinal).ToList();
        var position = new Dictionary<int, int>();
        for (int i = 0; i < ordered.Count; i++) position[ordered[i].Id] = i;
        var report = new JObject
        {
            ["units"] = new JArray(ordered.Select(u => new JObject
            {
                ["name"] = u.Name, ["file"] = u.File, ["line"] = u.Line,
                ["errors"] = new JArray(u.Errors.OrderBy(e => e, StringComparer.Ordinal)),
                ["refs"] = new JArray(u.Refs.Select(i => position[i]).OrderBy(i => i))
            }))
        };
        File.WriteAllText(output, report.ToString(Newtonsoft.Json.Formatting.None));
        Console.WriteLine(ordered.Count + " members, " + ordered.Count(u => u.Errors.Count > 0) + " with errors of their own, " + ordered.Sum(u => u.Refs.Count) + " references between them");
        return 0;
    }

    static string NameOf(SyntaxNode unit)
    {
        switch (unit)
        {
            case MethodDeclarationSyntax m: return m.Identifier.Text;
            case ConstructorDeclarationSyntax c: return c.Identifier.Text + "..ctor";
            case PropertyDeclarationSyntax p: return p.Identifier.Text;
            case BaseFieldDeclarationSyntax field: return string.Join(",", field.Declaration.Variables.Select(v => v.Identifier.Text));
            case BaseTypeDeclarationSyntax t: return "type " + t.Identifier.Text;
            case DelegateDeclarationSyntax d: return "delegate " + d.Identifier.Text;
            default: return unit.Kind().ToString();
        }
    }
}
