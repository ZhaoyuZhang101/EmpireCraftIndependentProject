using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Usage: PortSurvey <rewritten mod sources> <report.json> <core source folder> [<more source folders>...]
// Binds every member access in the rewritten sources against the ported core and
// counts the ones that do not resolve, grouped by the type they were asked of.
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("PortSurvey <sources> <report.json> <core folder> [more folders]"); return 2; }
        string sources = Path.GetFullPath(args[0]);
        var options = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new List<SyntaxTree>(); var surveyed = new HashSet<SyntaxTree>();
        foreach (string file in Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories))
        { var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), options, file); trees.Add(tree); surveyed.Add(tree); }
        for (int i = 2; i < args.Length; i++)
            foreach (string file in Directory.EnumerateFiles(args[i], "*.cs", SearchOption.AllDirectories))
                if (!file.Contains(Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar))
                    trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), options, file));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Where(p => !Path.GetFileName(p).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
        var compilation = CSharpCompilation.Create("survey", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var members = new Dictionary<string, int>(); var types = new Dictionary<string, int>(); var perFile = new Dictionary<string, int>();
        var files = new Dictionary<string, int>(); // lines per surveyed file
        void Count(Dictionary<string, int> table, string key) { table.TryGetValue(key, out int n); table[key] = n + 1; }
        foreach (var tree in surveyed)
        {
            string relative = tree.FilePath.Substring(sources.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
            files[relative] = tree.GetText().Lines.Count; perFile[relative] = 0;
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is MemberAccessExpressionSyntax access)
                {
                    var info = model.GetSymbolInfo(access);
                    if (info.Symbol != null || info.CandidateSymbols.Length > 0) continue;
                    var receiver = model.GetTypeInfo(access.Expression).Type;
                    if (receiver == null) { var symbol = model.GetSymbolInfo(access.Expression).Symbol; if (symbol is INamespaceSymbol) continue; }
                    string owner = receiver == null || string.IsNullOrEmpty(receiver.Name) ? null : receiver.Name;
                    if (owner == null) continue;   // the receiver itself failed; it is counted where it failed
                    Count(members, owner + "." + access.Name.Identifier.Text); Count(perFile, relative);
                }
                else if (node is MemberBindingExpressionSyntax binding && node.Parent is ConditionalAccessExpressionSyntax || node is MemberBindingExpressionSyntax)
                {
                    var bound = (MemberBindingExpressionSyntax)node;
                    var info = model.GetSymbolInfo(bound);
                    if (info.Symbol != null || info.CandidateSymbols.Length > 0) continue;
                    var conditional = bound.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault();
                    var receiver = conditional == null ? null : model.GetTypeInfo(conditional.Expression).Type;
                    if (receiver == null || string.IsNullOrEmpty(receiver.Name)) continue;
                    Count(members, receiver.Name + "." + bound.Name.Identifier.Text); Count(perFile, relative);
                }
                else if (node is IdentifierNameSyntax name && !(node.Parent is MemberAccessExpressionSyntax parent && parent.Name == node) && !(node.Parent is MemberBindingExpressionSyntax))
                {
                    var info = model.GetSymbolInfo(name);
                    if (info.Symbol != null || info.CandidateSymbols.Length > 0) continue;
                    if (name.IsVar || name.Identifier.Text == "nameof" || name.Identifier.Text == "_" || name.Identifier.Text == "global") continue;
                    if (model.GetTypeInfo(name).Type is ITypeSymbol resolved && resolved.TypeKind != TypeKind.Error) continue;
                    Count(types, name.Identifier.Text); Count(perFile, relative);
                }
            }
        }
        var report = new Newtonsoft.Json.Linq.JObject
        {
            ["files"] = files.Count, ["lines"] = files.Values.Sum(),
            ["members"] = Newtonsoft.Json.Linq.JObject.FromObject(members.OrderByDescending(p => p.Value).ToDictionary(p => p.Key, p => p.Value)),
            ["names"] = Newtonsoft.Json.Linq.JObject.FromObject(types.OrderByDescending(p => p.Value).ToDictionary(p => p.Key, p => p.Value)),
            ["unresolved_per_file"] = Newtonsoft.Json.Linq.JObject.FromObject(perFile.OrderByDescending(p => p.Value).ToDictionary(p => p.Key, p => p.Value)),
            ["file_lines"] = Newtonsoft.Json.Linq.JObject.FromObject(files)
        };
        File.WriteAllText(args[1], report.ToString());
        Console.WriteLine(files.Count + " files, " + files.Values.Sum() + " lines; " + members.Values.Sum() + " member accesses and " + types.Values.Sum() + " names do not resolve");
        return 0;
    }
}
