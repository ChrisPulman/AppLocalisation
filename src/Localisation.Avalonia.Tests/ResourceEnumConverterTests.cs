// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Resources;
using System.Threading.Tasks;
using Lean = CP.Localisation.Avalonia;
using Reactive = CP.Localisation.Avalonia.Reactive;

namespace Localisation.Avalonia.Tests;

/// <summary>Verifies statically typed enumeration localization for trimmed applications.</summary>
internal sealed class ResourceEnumConverterTests
{
    private const string CombinedText = "Read, Write";

    private const int EnumCount = 2;

    private readonly ResourceManager _manager = new("Localisation.Avalonia.Tests.TestResources", typeof(ResourceEnumConverterTests).Assembly);

    /// <summary>Verifies both variants translate enumeration names and recover their typed values.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task ConvertToString_KnownValue_RoundTripsLocalizedText()
    {
        var lean = new Lean.ResourceEnumConverter<SampleValue>(_manager);
        var reactive = new Reactive.ResourceEnumConverter<SampleValue>(_manager);
        await Assert.That(lean.ConvertToString(SampleValue.First, CultureInfo.InvariantCulture)).IsEqualTo("First localized");
        await Assert.That(reactive.ConvertToString(SampleValue.Second, CultureInfo.InvariantCulture)).IsEqualTo("Second localized");
        await Assert.That(lean.ConvertFromString("Second localized", CultureInfo.InvariantCulture)).IsEqualTo(SampleValue.Second);
        await Assert.That(reactive.ConvertFromString("First localized", CultureInfo.InvariantCulture)).IsEqualTo(SampleValue.First);
        await Assert.That(lean.GetValues(CultureInfo.InvariantCulture)).Count().IsEqualTo(EnumCount);
        await Assert.That(reactive.GetValues(CultureInfo.InvariantCulture)).Count().IsEqualTo(EnumCount);
    }

    /// <summary>Verifies unknown localized text does not become a default enumeration value.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task ConvertFromString_UnknownText_ReturnsNull()
    {
        var lean = new Lean.ResourceEnumConverter<SampleValue>(_manager);
        var reactive = new Reactive.ResourceEnumConverter<SampleValue>(_manager);
        await Assert.That(lean.ConvertFromString("Missing", CultureInfo.InvariantCulture)).IsNull();
        await Assert.That(reactive.ConvertFromString("Missing", CultureInfo.InvariantCulture)).IsNull();
    }

    /// <summary>Verifies explicitly enabled flags preserve combinations without reflection.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task ConvertToString_CombinedFlags_RoundTripsLocalizedParts()
    {
        var lean = new Lean.ResourceEnumConverter<SampleFlags>(_manager, isFlags: true);
        var reactive = new Reactive.ResourceEnumConverter<SampleFlags>(_manager, isFlags: true);
        const SampleFlags combined = SampleFlags.Read | SampleFlags.Write;
        await Assert.That(lean.ConvertToString(combined, CultureInfo.InvariantCulture)).IsEqualTo(CombinedText);
        await Assert.That(reactive.ConvertToString(combined, CultureInfo.InvariantCulture)).IsEqualTo(CombinedText);
        await Assert.That(lean.ConvertFromString(CombinedText, CultureInfo.InvariantCulture)).IsEqualTo(combined);
        await Assert.That(reactive.ConvertFromString(CombinedText, CultureInfo.InvariantCulture)).IsEqualTo(combined);
    }
}
