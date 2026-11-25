using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Newtonsoft.Json.Linq;

// PortSurvey suspend <sources> <report.json> <core folder> [<more source folders>...]
//
// Finds, member by member, what in the rewritten mod sources cannot compile yet and
// wraps it in #if EC_PENDING ... #endif, so that everything else does. A member that
// names something missing is suspended; so is, next round, whatever used that member;
// a type whose header is at fault (a missing base class) goes as a whole. Repeats
// until the whole set compiles under C# 9. Lines are blanked while searching so line
// numbers stay put; the directives are written once at the end.
public static class Suspend
{
    static readonly CSharpParseOptions Unity = new CSharpParseOptions(LanguageVersion.CSharp9);

    sealed class Source
    {
        public string Path, Relative; public string[] Lines; public bool[] Off; public bool Dirty = true; public SyntaxTree Tree;
        public int Members; public bool Whole;
        public string Text() { var copy = new string[Lines.Length]; for (int i = 0; i < copy.Length; i++) copy[i] = Off[i] ? "" : Lines[i]; return string.Join("\n", copy); }
    }

    public static int Run(string sources, string report, IEnumerable<string> folders)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Where(p => !Path.GetFileName(p).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
        var others = new List<SyntaxTree>();
        foreach (string folder in folders)
            foreach (string file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
                if (!file.Contains(Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar))
                    others.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), Unity, file));
        var files = new List<Source>();
        foreach (string file in Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');
            files.Add(new Source { Path = file, Relative = file.Substring(sources.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/'), Lines = lines, Off = new bool[lines.Length] });
        }
        var byPath = files.ToDictionary(f => f.Path);
        CSharpCompilation compilation = null;
        for (int round = 1; round <= 60; round++)
        {
            foreach (var f in files)
            {
                if (!f.Dirty) continue;
                var tree = CSharpSyntaxTree.ParseText(f.Text(), Unity, f.Path);
                compilation = compilation == null ? null : (f.Tree == null ? compilation.AddSyntaxTrees(tree) : compilation.ReplaceSyntaxTree(f.Tree, tree));
                f.Tree = tree; f.Dirty = false;
            }
            if (compilation == null)
                compilation = CSharpCompilation.Create("suspend", files.Select(f => f.Tree).Concat(others), references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource).ToList();
            int foreign = errors.Count(d => !byPath.ContainsKey(d.Location.SourceTree.FilePath));
            int suspended = 0;
            foreach (var group in errors.Where(d => byPath.ContainsKey(d.Location.SourceTree.FilePath)).GroupBy(d => d.Location.SourceTree.FilePath))
            {
                var f = byPath[group.Key]; var root = f.Tree.GetRoot(); var text = f.Tree.GetText();
                foreach (var d in group)
                {
                    var span = d.Location.SourceSpan;
                    var node = root.FindNode(span.Start < root.FullSpan.End ? span : new TextSpan(Math.Max(0, root.FullSpan.End - 1), 0), getInnermostNodeForTie: true);
                    if (Take(f, text, node)) suspended++;
                }
            }
            Console.WriteLine("round " + round + ": " + errors.Count + " errors" + (foreign > 0 ? " (" + foreign + " outside the sources)" : "") + ", " + suspended + " more members suspended");
            if (errors.Count == 0) break;
            if (suspended == 0)
            {
                // Nothing left to narrow down: the files still at fault go as a whole.
                int whole = 0;
                foreach (string path in errors.Select(d => d.Location.SourceTree.FilePath).Distinct())
                    if (byPath.TryGetValue(path, out var f) && !f.Whole) { All(f); whole++; }
                Console.WriteLine("   " + whole + " files suspended whole");
                if (whole == 0) { Console.WriteLine("   stuck with " + errors.Count + " errors: " + errors[0]); break; }
            }
        }

        var summary = new JObject(); int liveFiles = 0, liveLines = 0, offLines = 0, total = 0;
        foreach (var f in files)
        {
            int off = 0, code = 0, liveCode = 0;
            for (int i = 0; i < f.Lines.Length; i++)
            {
                bool blank = f.Lines[i].Trim().Length == 0;
                if (!blank) code++;
                if (f.Off[i]) off++; else if (!blank) liveCode++;
            }
            bool live = !f.Whole && HasLiveDeclaration(f);
            total += f.Lines.Length; offLines += off;
            if (live) { liveFiles++; liveLines += f.Lines.Length - off; }
            summary[f.Relative] = new JObject { ["lines"] = f.Lines.Length, ["suspended_lines"] = off, ["suspended_members"] = f.Members, ["live"] = live };
            File.WriteAllText(f.Path, Wrap(f));
        }
        File.WriteAllText(report, new JObject { ["files"] = files.Count, ["lines"] = total, ["live_files"] = liveFiles, ["live_lines"] = liveLines, ["suspended_lines"] = offLines, ["per_file"] = summary }.ToString());
        Console.WriteLine(files.Count + " files, " + total + " lines: " + liveFiles + " files keep live code, " + liveLines + " lines live, " + offLines + " lines suspended");
        return 0;
    }

    static bool HasLiveDeclaration(Source f)
    {
        var root = CSharpSyntaxTree.ParseText(f.Text(), Unity).GetRoot();
        return root.DescendantNodes().Any(n => n is BaseTypeDeclarationSyntax || n is DelegateDeclarationSyntax);
    }

    static void All(Source f) { for (int i = 0; i < f.Off.Length; i++) f.Off[i] = true; f.Whole = true; f.Dirty = true; }

    // Suspend the smallest whole-line unit around the node: its member, else its type, else the file.
    static bool Take(Source f, SourceText text, SyntaxNode node)
    {
        if (f.Whole) return false;
        for (var current = node; current != null; current = current.Parent)
        {
            bool unit = current is BaseMethodDeclarationSyntax || current is BasePropertyDeclarationSyntax || current is BaseFieldDeclarationSyntax ||
                        current is BaseTypeDeclarationSyntax || current is DelegateDeclarationSyntax || current is UsingDirectiveSyntax ||
                        current is AttributeListSyntax && current.Parent is CompilationUnitSyntax;
            if (!unit) continue;
            // An enum member, an accessor or a parameter is not a unit of its own; keep climbing for those.
            var lines = text.Lines.GetLinePositionSpan(current.Span);
            int first = lines.Start.Line, last = lines.End.Line;
            string before = text.Lines[first].ToString().Substring(0, lines.Start.Character);
            string lastLine = text.Lines[last].ToString();
            string after = lines.End.Character <= lastLine.Length ? lastLine.Substring(lines.End.Character) : "";
            int comment = after.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) after = after.Substring(0, comment);
            if (before.Trim().Length > 0 || after.Trim().Length > 0) continue;   // shares a line with something else: take the enclosing unit
            // Preprocessor lines inside must pair up, or the wrapper would cut a region in half.
            int depth = 0; bool balanced = true;
            for (int i = first; i <= last && balanced; i++)
            {
                string t = f.Lines[i].TrimStart();
                if (t.StartsWith("#if", StringComparison.Ordinal) || t.StartsWith("#region", StringComparison.Ordinal)) depth++;
                else if (t.StartsWith("#endif", StringComparison.Ordinal) || t.StartsWith("#endregion", StringComparison.Ordinal)) { depth--; if (depth < 0) balanced = false; }
                else if ((t.StartsWith("#else", StringComparison.Ordinal) || t.StartsWith("#elif", StringComparison.Ordinal)) && depth == 0) balanced = false;
            }
            if (!balanced || depth != 0) continue;
            bool changed = false;
            for (int i = first; i <= last; i++) if (!f.Off[i]) { f.Off[i] = true; changed = true; }
            if (changed) { f.Members++; f.Dirty = true; }
            return changed;
        }
        All(f);
        return true;
    }

    static string Wrap(Source f)
    {
        var output = new List<string>(f.Lines.Length + 16);
        if (f.Whole)
        {
            // Using aliases and the header stay outside so the file still reads as what it is.
            output.Add("#if EC_PENDING // the whole file waits for types this repository does not have yet");
            output.AddRange(f.Lines); output.Add("#endif");
            return string.Join("\n", output);
        }
        for (int i = 0; i < f.Lines.Length; i++)
        {
            if (!f.Off[i]) { output.Add(f.Lines[i]); continue; }
            int end = i;
            // Run to the end of the suspended stretch, joining stretches separated only by blank lines.
            while (true)
            {
                while (end + 1 < f.Lines.Length && f.Off[end + 1]) end++;
                int next = end + 1;
                while (next < f.Lines.Length && !f.Off[next] && f.Lines[next].Trim().Length == 0) next++;
                if (next < f.Lines.Length && f.Off[next] && next > end + 1) { end = next; continue; }
                break;
            }
            output.Add("#if EC_PENDING");
            for (int k = i; k <= end; k++) output.Add(f.Lines[k]);
            output.Add("#endif");
            i = end;
        }
        return string.Join("\n", output);
    }
}
