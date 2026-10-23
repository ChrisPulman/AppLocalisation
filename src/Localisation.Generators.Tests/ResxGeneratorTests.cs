// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CP.Localisation.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Localisation.Generators.Tests;

/// <summary>Verifies RESX diagnostics and metadata-based registration.</summary>
internal sealed class ResxGeneratorTests
{
    /// <summary>Verifies invalid input produces a user-facing diagnostic instead of crashing generation.</summary>
    /// <param name="xml">The invalid RESX content.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("<root>")]
    [Arguments("<root><data name='A'><value>one</value></data><data name='A'><value>two</value></data></root>")]
    [Arguments("<root><data><value>unnamed</value></data></root>")]
    internal async Task Generate_InvalidResources_ReportsDiagnostic(string xml)
    {
        var result = Run(xml);
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Id).IsEqualTo("LOCGEN001");
        await Assert.That(result.Diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
    }

    /// <summary>Verifies object values require explicit providers instead of unsafe generated deserialization.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Generate_ObjectResource_ReportsProviderDiagnostic()
    {
        var result = Run("<root><data name='Number' type='System.Int32, System.Private.CoreLib'><value>42</value></data></root>");
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Id).IsEqualTo("LOCGEN002");
        await Assert.That(result.Diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
    }

    /// <summary>Verifies missing MSBuild metadata produces an actionable diagnostic.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Generate_MissingResourceMetadata_ReportsImportDiagnostic()
    {
        var result = Run("<root/>", resourceName: string.Empty);
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].GetMessage()).Contains("import Localisation.Generators.targets");
    }

    /// <summary>Verifies linked resources use their declared logical name and retain culture fallback.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Generate_LinkedCultureResource_UsesMetadataInsteadOfPhysicalPath()
    {
        var result = Run("<root><data name='Greeting'><value>Bonjour</value></data></root>", "Consumer.Linked.Messages.fr", "fr");
        await Assert.That(result.Diagnostics).IsEmpty();
        var source = result.GeneratedTrees
            .Single(static tree => tree.FilePath.EndsWith("Localisation.ResourceModule.g.cs", System.StringComparison.Ordinal))
            .ToString();
        await Assert.That(source).Contains("\"Consumer.Linked.Messages\"");
        await Assert.That(source).Contains("\"Consumer\"");
        await Assert.That(source).Contains("culture = culture.Parent");
        await Assert.That(source).Contains("\"en-US\"");
        await Assert.That(source).Contains("\"Bonjour\"");
        await Assert.That(source).Contains("global::System.StringComparison.OrdinalIgnoreCase");
        await Assert.That(source).Contains("private static object? Get0Culture0(string key)");
        await Assert.That(source).Contains("private static object? Get0Neutral(string key)");
        await Assert.That(source.Contains("ToUpperInvariant", System.StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>Verifies neutral-only and unsupported-only resources generate valid switch statements.</summary>
    /// <param name="xml">The resource file without any satellite string values.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("<root><data name='Greeting'><value>Hello</value></data></root>")]
    [Arguments("<root><data name='Number' type='System.Int32'><value>42</value></data></root>")]
    internal async Task Generate_ResourcesWithoutSatelliteStrings_ProducesValidSyntax(string xml)
    {
        var result = Run(xml);
        var diagnostics = result.GeneratedTrees.SelectMany(static tree => tree.GetDiagnostics()).ToArray();
        await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>Verifies invalid neutral culture metadata fails generation with an actionable diagnostic.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Generate_InvalidNeutralCulture_ReportsDiagnostic()
    {
        var result = Run("<root/>", neutral: "!");
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Id).IsEqualTo("LOCGEN001");
    }

    /// <summary>Verifies a project without RESX inputs does not emit an unused resource registration.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Generate_NoResources_DoesNotEmitResourceModule()
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ResxGenerator());
        var syntax = CSharpSyntaxTree.ParseText(
            "namespace System.Runtime.CompilerServices { internal sealed class ModuleInitializerAttribute { } }");
        var compilation = CSharpCompilation.Create("EmptyConsumer", syntaxTrees: [syntax]);
        var result = driver.RunGenerators(compilation).GetRunResult();
        await Assert.That(result.Diagnostics).IsEmpty();
        var containsResources = result.GeneratedTrees.Any(
            static tree => tree.FilePath.EndsWith("Localisation.ResourceModule.g.cs", System.StringComparison.Ordinal));
        await Assert.That(containsResources).IsFalse();
    }

    private static GeneratorDriverRunResult Run(string xml, string resourceName = "Consumer.Messages", string culture = "", string neutral = "en-US")
    {
        var additional = new ResourceFile("/external/PhysicalName.resx", xml);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new ResxGenerator().AsSourceGenerator()],
            additionalTexts: [additional],
            optionsProvider: new ResourceOptions(resourceName, culture, neutral));
        return driver.RunGenerators(CSharpCompilation.Create("Consumer")).GetRunResult();
    }

    private sealed class ResourceFile(string path, string xml) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(xml);
    }

    private sealed class ResourceOptions(string resourceName, string culture, string neutral) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(new Dictionary<string, string>
        {
            ["build_property.AssemblyName"] = "Consumer",
            ["build_property.NeutralLanguage"] = neutral,
        });

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.LocalisationResourceName"] = resourceName,
            ["build_metadata.AdditionalFiles.CultureName"] = culture,
        });
    }

    private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
}
