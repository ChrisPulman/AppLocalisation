namespace Localisation.BuildTools;

internal sealed class Options
{
    private static readonly HashSet<string> Switches = new(StringComparer.OrdinalIgnoreCase)
    {
        "ResolvePublishedPackages", "UseEnvironmentalTools",
    };

    private static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        "ResultsDirectory", "MinimumLineCoverage", "PackagesDirectory", "PublishedPackagesDirectory",
        "RuntimeIdentifier", "Configuration", "Path", "CertificateFingerprint",
    };

    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public Options(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            var name = argument.TrimStart('-');
            if (!argument.StartsWith('-') || (!Switches.Contains(name) && !Values.Contains(name)))
            {
                throw new ArgumentException($"Unknown option '{argument}'.");
            }

            var value = "true";
            if (!Switches.Contains(name))
            {
                if (++index == args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Missing value for '{argument}'.");
                }

                value = args[index];
            }

            if (!_values.TryAdd(name, value))
            {
                throw new ArgumentException($"Duplicate option '{argument}'.");
            }
        }
    }

    public string Required(string name) => _values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException($"--{name} is required.");

    public string Get(string name, string defaultValue) => _values.GetValueOrDefault(name, defaultValue);

    public bool Flag(string name) => _values.ContainsKey(name);
}
