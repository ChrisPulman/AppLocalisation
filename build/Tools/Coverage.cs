using System.Globalization;
using System.Xml.Linq;

namespace Localisation.BuildTools;

internal static class Coverage
{
    internal static readonly string[] Assemblies =
    [
        "Localisation.WPF", "Localisation.WPF.Reactive", "Localisation.Avalonia",
        "Localisation.Avalonia.Reactive", "Localisation.Core", "Localisation.Generators",
    ];

    public static void Verify(Options options)
    {
        var directory = options.Required("ResultsDirectory");
        if (!double.TryParse(options.Get("MinimumLineCoverage", "98"), NumberStyles.Float, CultureInfo.InvariantCulture, out var minimum)
            || !double.IsFinite(minimum) || minimum is < 0 or > 100)
        {
            throw new ArgumentException("MinimumLineCoverage must be between 0 and 100.");
        }

        var reports = Directory.GetFiles(directory, "*.cobertura.xml", SearchOption.AllDirectories);
        if (reports.Length == 0)
        {
            throw new InvalidDataException($"No Cobertura reports were found under '{directory}'.");
        }

        var coverage = Assemblies.ToDictionary(name => name,
            _ => new Dictionary<(string File, int Line), bool>(SourceLineComparer.Instance), StringComparer.OrdinalIgnoreCase);
        foreach (var report in reports)
        {
            var document = XDocument.Load(report);
            foreach (var package in document.Element("coverage")?.Element("packages")?.Elements("package") ?? [])
            {
                if (!coverage.TryGetValue((string?)package.Attribute("name") ?? "", out var lines))
                {
                    continue;
                }

                foreach (var type in package.Element("classes")?.Elements("class") ?? [])
                {
                    var file = (string?)type.Attribute("filename");
                    if (string.IsNullOrWhiteSpace(file))
                    {
                        throw new InvalidDataException($"Class in '{report}' has no source filename.");
                    }

                    file = file.Replace('\\', '/');
                    if (file.StartsWith("./", StringComparison.Ordinal))
                    {
                        file = file[2..];
                    }

                    foreach (var line in type.Element("lines")?.Elements("line") ?? [])
                    {
                        if (!int.TryParse((string?)line.Attribute("number"), out var number) || number <= 0
                            || !int.TryParse((string?)line.Attribute("hits"), out var hits) || hits < 0)
                        {
                            throw new InvalidDataException($"Invalid line number or hit count for '{file}' in '{report}'.");
                        }

                        var key = (file, number);
                        lines[key] = lines.GetValueOrDefault(key) || hits > 0;
                    }
                }
            }
        }

        var failed = false;
        foreach (var (assembly, lines) in coverage)
        {
            if (lines.Count == 0)
            {
                Console.Error.WriteLine($"No executable Cobertura lines were found for assembly '{assembly}'.");
                failed = true;
                continue;
            }

            var covered = lines.Values.Count(value => value);
            var percentage = 100.0 * covered / lines.Count;
            Console.WriteLine(FormattableString.Invariant($"{assembly}: {percentage:N2}% line coverage ({covered}/{lines.Count})"));
            if (percentage < minimum)
            {
                Console.Error.WriteLine(FormattableString.Invariant($"{assembly} line coverage {percentage:N2}% is below the required {minimum:N2}%."));
                failed = true;
            }
        }

        if (failed)
        {
            throw new InvalidDataException("Package line coverage validation failed.");
        }
    }

    private sealed class SourceLineComparer : IEqualityComparer<(string File, int Line)>
    {
        public static readonly SourceLineComparer Instance = new();

        public bool Equals((string File, int Line) x, (string File, int Line) y) =>
            x.Line == y.Line && StringComparer.OrdinalIgnoreCase.Equals(x.File, y.File);

        public int GetHashCode((string File, int Line) value) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.File), value.Line);
    }
}
