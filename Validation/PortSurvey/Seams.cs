using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json.Linq;

// PortSurvey seams <sources> <seams.json>
//
// A few members of the mod mean something else in this game (making a person out of
// the counted population, for one). Those are written by hand in Assets/EmpireCraft/Core/Seams
// as the other half of a partial class. This switches the mod's own version off
// (#if EC_REPLACED, a symbol nothing defines) and marks the class partial, so the
// rest of the ported code calls the hand-written one.
//
// seams.json: { "CityPopulationSystem": ["SpawnFromGroup"], ... } - class name to member names
// (every overload of a name); "*" switches the whole type off, for a class written anew by hand.
// An empty list only makes the class partial, so a hand-written half can add members beside the mod's.
// A name that is not found is an error: the mod has moved on.
public static class Seams
{
    public static int Run(string sources, string table)
    {
        var wanted = JObject.Parse(File.ReadAllText(table)).Properties()
            .ToDictionary(p => p.Name, p => new HashSet<string>(p.Value.Select(v => (string)v), StringComparer.Ordinal), StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        int files = 0, members = 0;
        foreach (string file in Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file).Replace("\r\n", "\n");
            var root = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest)).GetRoot();
            var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(t => wanted.ContainsKey(t.Identifier.Text)).ToList();
            if (types.Count == 0) continue;
            var lines = text.Split('\n').ToList();
            var off = new List<(int first, int last)>(); var partial = new List<int>();
            foreach (var type in types)
            {
                var names = wanted[type.Identifier.Text];
                if (names.Contains("*"))
                {
                    // The whole type goes; the hand-written one replaces it, so it is not made partial.
                    var whole = type.GetLocation().GetLineSpan();
                    off.Add((whole.StartLinePosition.Line, whole.EndLinePosition.Line));
                    found.Add(type.Identifier.Text + ".*"); members++;
                    continue;
                }
                foreach (var member in type.Members)
                {
                    string name = member is MethodDeclarationSyntax m ? m.Identifier.Text : member is PropertyDeclarationSyntax p ? p.Identifier.Text :
                        member is BaseFieldDeclarationSyntax f ? f.Declaration.Variables.First().Identifier.Text : null;
                    if (name == null || !names.Contains(name)) continue;
                    var span = member.GetLocation().GetLineSpan();
                    off.Add((span.StartLinePosition.Line, span.EndLinePosition.Line));
                    found.Add(type.Identifier.Text + "." + name); members++;
                }
                if (!type.Modifiers.Any(SyntaxKind.PartialKeyword)) partial.Add(type.Keyword.GetLocation().GetLineSpan().StartLinePosition.Line * 100000 + type.Keyword.GetLocation().GetLineSpan().StartLinePosition.Character);
            }
            // An empty list switches nothing off and only makes the class partial, for a hand-written half that adds members.
            if (off.Count == 0 && partial.Count == 0) continue;
            // `partial` goes right before the class keyword; then the directives, from the bottom up so line numbers hold.
            foreach (int at in partial.OrderByDescending(v => v))
            {
                int line = at / 100000, column = at % 100000;
                lines[line] = lines[line].Substring(0, column) + "partial " + lines[line].Substring(column);
            }
            foreach (var (first, last) in off.OrderByDescending(v => v.first))
            {
                lines.Insert(last + 1, "#endif");
                lines.Insert(first, "#if EC_REPLACED // written by hand for this game: see Assets/EmpireCraft/Core/Seams");
            }
            File.WriteAllText(file, string.Join("\n", lines)); files++;
        }
        var missing = wanted.SelectMany(p => p.Value.Select(name => p.Key + "." + name)).Where(name => !found.Contains(name)).ToList();
        Console.WriteLine(members + " members replaced in " + files + " files" + (missing.Count > 0 ? "; NOT FOUND: " + string.Join(", ", missing) : ""));
        return missing.Count > 0 ? 3 : 0;
    }
}
