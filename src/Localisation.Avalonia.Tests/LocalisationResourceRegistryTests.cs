// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CP.Localisation;

namespace Localisation.Avalonia.Tests;

/// <summary>Verifies generated resources retain RESX culture fallback and registration.</summary>
internal sealed class LocalisationResourceRegistryTests
{
    private const string ReplacementValue = "Replacement";

    private const string NeutralCulture = "en-US";

    private const string OriginalValue = "Original";

    private const string RegionalCulture = "fr-CA";

    private const string GreetingKey = "Greeting";

    private const string ResourceName = "Localisation.Avalonia.Tests.GeneratedResources.SmokeResources";

    /// <summary>Verifies lookup order, null fallback, and replacement in the published provider snapshot.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task TryGetResource_SharedDictionary_UsesOrdinalAssemblyOrderAndReplacement()
    {
        const string dictionary = "RegistryOrderTests.Resources";
        LocalisationResourceRegistry.Register("RegistryOrder.Z", dictionary, static (_, _) => "last");
        LocalisationResourceRegistry.Register("RegistryOrder.a", dictionary, static (_, _) => "lowercase");
        LocalisationResourceRegistry.Register("RegistryOrder.A", dictionary, static (_, _) => null);
        await Assert.That(LocalisationResourceRegistry.TryGetResource(dictionary, GreetingKey, CultureInfo.InvariantCulture, out var value)).IsTrue();
        await Assert.That(value).IsEqualTo("last");
        LocalisationResourceRegistry.Register("RegistryOrder.A", dictionary, static (_, _) => ReplacementValue);
        await Assert.That(LocalisationResourceRegistry.TryGetResource(dictionary, GreetingKey, CultureInfo.InvariantCulture, out value)).IsTrue();
        await Assert.That(value).IsEqualTo(ReplacementValue);
    }

    /// <summary>Verifies concurrent registration publishes a complete snapshot without losing other providers.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Register_ConcurrentProviders_RetainsEveryDictionary()
    {
        const int providerCount = 20;
        var registrations = Enumerable.Range(0, providerCount).Select(index => Task.Run(() =>
            LocalisationResourceRegistry.Register("RegistryConcurrentTests", $"RegistryConcurrentTests.Resources{index}", static (_, _) => OriginalValue)));
        await Task.WhenAll(registrations);
        foreach (var index in Enumerable.Range(0, providerCount))
        {
            var dictionary = $"RegistryConcurrentTests.Resources{index}";
            var found = LocalisationResourceRegistry.TryGetResource(dictionary, GreetingKey, CultureInfo.InvariantCulture, out var value);
            await Assert.That(found).IsTrue();
            await Assert.That(value).IsEqualTo(OriginalValue);
        }
    }

    /// <summary>Verifies a regional culture falls back through its parent and then neutral resources.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task TryGetResource_RegionalCulture_UsesParentAndNeutralResources()
    {
        var culture = CultureInfo.GetCultureInfo(RegionalCulture);
        await Assert.That(
            LocalisationResourceRegistry.TryGetResource(ResourceName, "Regional", culture, out var regional)).IsTrue();
        await Assert.That(regional).IsEqualTo("Allô");
        await Assert.That(
            LocalisationResourceRegistry.TryGetResource(ResourceName, GreetingKey, culture, out var parent)).IsTrue();
        await Assert.That(parent).IsEqualTo("Bonjour");
        await Assert.That(
            LocalisationResourceRegistry.TryGetResource(ResourceName, "SmokeValue_First", culture, out var neutral)).IsTrue();
        await Assert.That(neutral).IsEqualTo("First localized");
    }

    /// <summary>Verifies unrelated cultures fall back to the neutral resources.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task TryGetResource_UntranslatedCulture_ReturnsNeutralResource()
    {
        await Assert.That(
            LocalisationResourceRegistry.TryGetResource(ResourceName, GreetingKey, CultureInfo.GetCultureInfo("de-DE"), out var resource)).IsTrue();
        await Assert.That(resource).IsEqualTo("Hello");
    }

    /// <summary>Verifies absent keys remain absent after traversing the culture fallback chain.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task TryGetResource_MissingKey_ReturnsFalseAndNull()
    {
        await Assert.That(
            LocalisationResourceRegistry.TryGetResource(ResourceName, "Missing", CultureInfo.GetCultureInfo(RegionalCulture), out var resource)).IsFalse();
        await Assert.That(resource).IsNull();
    }

    /// <summary>Verifies generated registration includes resource culture names.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task GetAvailableCultures_GeneratedResources_ContainsParentAndRegionalCultures()
    {
        var cultures = LocalisationResourceRegistry.GetAvailableCultures("Localisation.Avalonia.Tests");
        await Assert.That(cultures).Contains(CultureInfo.GetCultureInfo("fr"));
        await Assert.That(cultures).Contains(CultureInfo.GetCultureInfo(RegionalCulture));
    }

    /// <summary>Verifies replacement registration removes cultures belonging to the previous provider.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Register_ReplacementProvider_ReplacesResourcesAndCultures()
    {
        const string assemblyName = "RegistryReplacementTests";
        const string dictionary = "RegistryReplacementTests.Resources";
        LocalisationResourceRegistry.Register(assemblyName, dictionary, static (_, _) => OriginalValue, [NeutralCulture]);
        LocalisationResourceRegistry.Register(assemblyName, dictionary, static (_, _) => ReplacementValue, ["fr"]);
        await Assert.That(
            LocalisationResourceRegistry.TryGetResource(assemblyName, dictionary, GreetingKey, CultureInfo.InvariantCulture, out var resource)).IsTrue();
        await Assert.That(resource).IsEqualTo(ReplacementValue);
        var cultures = LocalisationResourceRegistry.GetAvailableCultures(assemblyName);
        await Assert.That(cultures).Count().IsEqualTo(1);
        await Assert.That(cultures).Contains(CultureInfo.GetCultureInfo("fr"));
    }

    /// <summary>Verifies invalid replacement metadata cannot partially replace a working provider.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Register_InvalidReplacementCulture_PreservesOriginalRegistration()
    {
        const string assemblyName = "RegistryAtomicityTests";
        const string dictionary = "RegistryAtomicityTests.Resources";
        LocalisationResourceRegistry.Register(assemblyName, dictionary, static (_, _) => OriginalValue, [NeutralCulture]);
        await Assert.That(() => LocalisationResourceRegistry.Register(
            assemblyName,
            dictionary,
            static (_, _) => ReplacementValue,
            ["fr", "!"])).Throws<CultureNotFoundException>();
        await Assert.That(
            LocalisationResourceRegistry.TryGetResource(assemblyName, dictionary, GreetingKey, CultureInfo.InvariantCulture, out var resource)).IsTrue();
        await Assert.That(resource).IsEqualTo(OriginalValue);
        var cultures = LocalisationResourceRegistry.GetAvailableCultures(assemblyName);
        await Assert.That(cultures).Count().IsEqualTo(1);
        await Assert.That(cultures).Contains(CultureInfo.GetCultureInfo(NeutralCulture));
    }
}
