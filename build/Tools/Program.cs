using Localisation.BuildTools;

try
{
    if (args.Length == 0 || args[0] is "--help" or "-h")
    {
        Console.WriteLine("Commands: coverage --ResultsDirectory PATH [--MinimumLineCoverage 98]; symbols --PackagesDirectory PATH [--ResolvePublishedPackages] [--PublishedPackagesDirectory PATH]; aot [--RuntimeIdentifier win-x64] [--Configuration Release] [--UseEnvironmentalTools]; authenticode --Path FILE --CertificateFingerprint SHA256; signatures --PackagesDirectory PATH --CertificateFingerprint SHA256");
        return args.Length == 0 ? 1 : 0;
    }

    var options = new Options(args.Skip(1).ToArray());
    switch (args[0])
    {
        case "coverage":
            Coverage.Verify(options);
            break;
        case "symbols":
            await SymbolPackages.RunAsync(options);
            break;
        case "aot":
            await AotVerification.RunAsync(options);
            break;
        case "authenticode":
            Authenticode.Verify(options.Required("Path"), options.Required("CertificateFingerprint"));
            break;
        case "signatures":
            await SignedPackages.RunAsync(options);
            break;
        default:
            throw new ArgumentException($"Unknown command '{args[0]}'. Use --help.");
    }

    return 0;
}
catch (Exception error)
{
    await Console.Error.WriteLineAsync(error.Message);
    return 1;
}
