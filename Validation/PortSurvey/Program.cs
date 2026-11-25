using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Newtonsoft.Json.Linq;

// Semantic helper for Tools/PortTool. Two modes over the rewritten mod sources:
//
//   PortSurvey survey <sources> <report.json> <core folder> [<more source folders>...]
//     Binds every member access against the ported core and counts the ones that
//     do not resolve, grouped by the type they were asked of.
//
//   PortSurvey fix <sources> <members.json> <core folder> [<more source folders>...]
//     Renames members by the type of their receiver, in place. members.json maps
//     "Realm.id" -> "Id" (one member) or "Realm.data.color_id" -> "ColorId" (a chain
//     of two collapsed into one). Only accesses that do not resolve are touched, so
//     a mod class with a member of the same name is left alone. Repeats until
//     nothing changes, since one rename can give the next receiver its type.
public static class Program
{
    static readonly CSharpParseOptions Options = new CSharpParseOptions(LanguageVersion.Latest);

    public static int Main(string[] args)
    {
        if (args.Length < 4 || (args[0] != "survey" && args[0] != "fix"))
        { Console.Error.WriteLine("PortSurvey survey|fix <sources> <report.json|members.json> <core folder> [more folders]"); return 2; }
        string sources = Path.GetFullPath(args[1]);
        var others = new List<SyntaxTree>();
        for (int i = 3; i < args.Length; i++)
            foreach (string file in Directory.EnumerateFiles(args[i], "*.cs", SearchOption.AllDirectories))
                if (!file.Contains(Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar))
                    others.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), Options, file));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Where(p => !Path.GetFileName(p).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
        var texts = new Dictionary<string, string>();
        foreach (string file in Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories)) texts[file] = File.ReadAllText(file);

        if (args[0] == "survey") { Survey(sources, texts, others, references, args[2]); return 0; }

        var map = JObject.Parse(File.ReadAllText(args[2])).Properties().ToDictionary(p => p.Name, p => (string)p.Value);
        int total = 0;
        for (int pass = 1; pass <= 6; pass++)
        {
            int changed = Fix(texts, others, references, map);
            Console.WriteLine("pass " + pass + ": " + changed + " renames");
            total += changed;
            if (changed == 0) break;
        }
        foreach (var pair in texts) File.WriteAllText(pair.Key, pair.Value);
        Console.WriteLine(total + " renames in all");
        return 0;
    }

    static CSharpCompilation Compile(Dictionary<string, string> texts, List<SyntaxTree> others, List<MetadataReference> references, out List<SyntaxTree> surveyed)
    {
        surveyed = texts.Select(p => CSharpSyntaxTree.ParseText(p.Value, Options, p.Key)).ToList();
        return CSharpCompilation.Create("survey", surveyed.Concat(others), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    }

    // The receiver type's name and the member asked of it, when the access does not resolve.
    static bool Unresolved(SemanticModel model, SyntaxNode node, out string owner, out SimpleNameSyntax member)
    {
        owner = null; member = null;
        ExpressionSyntax receiver;
        if (node is MemberAccessExpressionSyntax access) { member = access.Name; receiver = access.Expression; }
        else if (node is MemberBindingExpressionSyntax binding)
        {
            member = binding.Name;
            receiver = binding.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault()?.Expression;
            if (receiver == null) return false;
        }
        else return false;
        var info = model.GetSymbolInfo(node);
        if (info.Symbol != null || info.CandidateSymbols.Length > 0) return false;
        var type = model.GetTypeInfo(receiver).Type;
        if (type == null || string.IsNullOrEmpty(type.Name)) return false;
        owner = type.Name;
        return true;
    }

    static int Fix(Dictionary<string, string> texts, List<SyntaxTree> others, List<MetadataReference> references, Dictionary<string, string> map)
    {
        var compilation = Compile(texts, others, references, out var surveyed);
        int changed = 0;
        foreach (var tree in surveyed)
        {
            var model = compilation.GetSemanticModel(tree);
            var edits = new List<TextChange>();
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (!Unresolved(model, node, out string owner, out var member)) continue;
                string name = member.Identifier.Text;
                // A chain of two collapsed into one member: x.data.color_id -> x.ColorId.
                if (node.Parent is MemberAccessExpressionSyntax outer && outer.Expression == node &&
                    map.TryGetValue(owner + "." + name + "." + outer.Name.Identifier.Text, out string collapsed))
                { edits.Add(new TextChange(TextSpan.FromBounds(member.SpanStart, outer.Name.Span.End), collapsed)); continue; }
                if (!map.TryGetValue(owner + "." + name, out string renamed)) continue;
                if (renamed.Length > 0) { edits.Add(new TextChange(member.Identifier.Span, renamed)); continue; }
                // An empty target drops the access, dot included: cities.list -> cities.
                if (node is MemberAccessExpressionSyntax plain)
                    edits.Add(new TextChange(TextSpan.FromBounds(plain.OperatorToken.SpanStart, member.Span.End), ""));
                // `a?.list` loses the question mark too; a longer chain after `?.` is left for a hand edit.
                else if (node.Parent is ConditionalAccessExpressionSyntax conditional && conditional.WhenNotNull == node)
                    edits.Add(new TextChange(TextSpan.FromBounds(conditional.OperatorToken.SpanStart, member.Span.End), ""));
            }
            if (edits.Count == 0) continue;
            // Chain edits may contain a single edit of the inner member; keep the widest, drop overlaps.
            edits.Sort((a, b) => a.Span.Start != b.Span.Start ? a.Span.Start.CompareTo(b.Span.Start) : b.Span.Length.CompareTo(a.Span.Length));
            var kept = new List<TextChange>(); int end = -1;
            foreach (var edit in edits) { if (edit.Span.Start < end) continue; kept.Add(edit); end = edit.Span.End; }
            texts[tree.FilePath] = tree.GetText().WithChanges(kept).ToString();
            changed += kept.Count;
        }
        return changed;
    }

    static void Survey(string sources, Dictionary<string, string> texts, List<SyntaxTree> others, List<MetadataReference> references, string output)
    {
        var compilation = Compile(texts, others, references, out var surveyed);
        var members = new Dictionary<string, int>(); var chains = new Dictionary<string, int>(); var names = new Dictionary<string, int>();
        var perFile = new Dictionary<string, int>(); var files = new Dictionary<string, int>();
        void Count(Dictionary<string, int> table, string key) { table.TryGetValue(key, out int n); table[key] = n + 1; }
        foreach (var tree in surveyed)
        {
            string relative = tree.FilePath.Substring(sources.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
            files[relative] = tree.GetText().Lines.Count; perFile[relative] = 0;
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (Unresolved(model, node, out string owner, out var member))
                {
                    Count(members, owner + "." + member.Identifier.Text); Count(perFile, relative);
                    if (node.Parent is MemberAccessExpressionSyntax outer && outer.Expression == node)
                        Count(chains, owner + "." + member.Identifier.Text + "." + outer.Name.Identifier.Text);
                }
                else if (node is IdentifierNameSyntax name && !(node.Parent is MemberAccessExpressionSyntax parent && parent.Name == node) && !(node.Parent is MemberBindingExpressionSyntax))
                {
                    var info = model.GetSymbolInfo(name);
                    if (info.Symbol != null || info.CandidateSymbols.Length > 0) continue;
                    if (name.IsVar || name.Identifier.Text == "nameof" || name.Identifier.Text == "_" || name.Identifier.Text == "global") continue;
                    if (model.GetTypeInfo(name).Type is ITypeSymbol resolved && resolved.TypeKind != TypeKind.Error) continue;
                    Count(names, name.Identifier.Text); Count(perFile, relative);
                }
            }
        }
        JObject Sorted(Dictionary<string, int> table) => JObject.FromObject(table.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value));
        var report = new JObject
        {
            ["files"] = files.Count, ["lines"] = files.Values.Sum(),
            ["members"] = Sorted(members), ["chains"] = Sorted(chains), ["names"] = Sorted(names),
            ["unresolved_per_file"] = Sorted(perFile), ["file_lines"] = JObject.FromObject(files)
        };
        File.WriteAllText(output, report.ToString());
        Console.WriteLine(files.Count + " files, " + files.Values.Sum() + " lines; " + members.Values.Sum() + " member accesses and " + names.Values.Sum() + " names do not resolve");
    }
}
