// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Localisation.Avalonia.Tests;

/// <summary>Exercises the build tool's coverage union and failure boundaries through its CLI.</summary>
public sealed class BuildToolTests
{
    private const int SecondLine = 2;

    private const int Sha256HexLength = 64;

    private static readonly int[] ReportNumbers = [1, SecondLine];

    /// <summary>Verifies union coverage, thresholds and malformed or absent input.</summary>
    /// <param name="scenario">The fixture variation.</param>
    /// <param name="expectedExit">The expected exit code.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task representing the test.</returns>
    [Test]
    [Arguments("union", 0)]
    [Arguments("threshold", 0)]
    [Arguments("below", 1)]
    [Arguments("missing-assembly", 1)]
    [Arguments("invalid-hits", 1)]
    [Arguments("invalid-line", 1)]
    [Arguments("missing-file", 1)]
    [Arguments("malformed", 1)]
    [Arguments("no-reports", 1)]
    [Arguments("invalid-threshold", 1)]
    public async Task Coverage_Cli_ValidatesUnion(string scenario, int expectedExit, CancellationToken cancellationToken)
    {
        var directory = Directory.CreateTempSubdirectory("localisation-coverage-");
        try
        {
            if (scenario != "no-reports")
            {
                var names = new[]
                {
                    "Localisation.WPF", "Localisation.WPF.Reactive", "Localisation.Avalonia",
                    "Localisation.Avalonia.Reactive", "Localisation.Core", "Localisation.Generators",
                };
                if (scenario == "missing-assembly")
                {
                    names = names[..^1];
                }

                foreach (var report in ReportNumbers)
                {
                    var contents = scenario == "malformed" ? "<broken" : CreateReport(names, report, scenario);
                    await File.WriteAllTextAsync(Path.Combine(directory.FullName, $"{report}.cobertura.xml"), contents, cancellationToken);
                }
            }

            var minimum = scenario switch
            {
                "threshold" => "50",
                "invalid-threshold" => "NaN",
                _ => "100",
            };
            var result = await BuildToolTestHost.RunAsync(
                cancellationToken,
                "coverage",
                "--ResultsDirectory",
                directory.FullName,
                "--MinimumLineCoverage",
                minimum);
            await Assert.That(result.ExitCode).IsEqualTo(expectedExit).Because(result.Diagnostics);
            if (scenario == "union")
            {
                await Assert.That(result.Diagnostics.Contains("100.00% line coverage (2/2)", StringComparison.Ordinal)).IsTrue();
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }

    /// <summary>Verifies unsigned images fail the Windows trust gate.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task representing the test.</returns>
    [Test]
    public async Task Authenticode_UnsignedAssembly_IsRejected(CancellationToken cancellationToken)
    {
        var result = await BuildToolTestHost.RunAsync(
            cancellationToken,
            "authenticode",
            "--Path",
            typeof(BuildToolTests).Assembly.Location,
            "--CertificateFingerprint",
            new string('0', Sha256HexLength));
        await Assert.That(result.ExitCode).IsEqualTo(1).Because(result.Diagnostics);
    }

    private static string CreateReport(string[] names, int report, string scenario)
    {
        var xml = new StringBuilder("<coverage><packages>");
        foreach (var assembly in names)
        {
            var filename = report == 1 ? @".\Source.cs" : "source.cs";
            if (scenario == "missing-file")
            {
                filename = string.Empty;
            }

            _ = xml.Append($"<package name=\"{assembly}\"><classes><class filename=\"{filename}\"><lines>");
            foreach (var sourceLine in ReportNumbers)
            {
                _ = xml.Append(CreateLine(report, sourceLine, scenario));
            }

            _ = xml.Append("</lines></class></classes></package>");
        }

        _ = xml.Append("</packages></coverage>");
        return xml.ToString();
    }

    private static string CreateLine(int report, int sourceLine, string scenario)
    {
        var number = scenario == "invalid-line" ? "bad" : sourceLine.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var hits = sourceLine == report && !((scenario is "below" or "threshold") && sourceLine == SecondLine) ? "1" : "0";
        if (scenario == "invalid-hits")
        {
            hits = "bad";
        }

        return $"<line number=\"{number}\" hits=\"{hits}\"/>";
    }
}
