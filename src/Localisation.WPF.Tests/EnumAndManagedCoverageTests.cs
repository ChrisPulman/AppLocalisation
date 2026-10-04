// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Threading.Tasks;
using System.Windows.Data;
using Lean = CP.Localisation;
using Reactive = CP.Localisation.Reactive;

namespace Localisation.WPF.Tests;

/// <summary>Verifies conversion fallbacks in both package variants.</summary>
internal sealed class EnumAndManagedCoverageTests
{
    private const int UnknownFlagBits = 8;

    private const int EnumValueCount = 2;

    /// <summary>Verifies null, standard flags, conversion fallbacks and WPF converter adapters.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task EnumConvertersHandleNullAndFallbackConversions()
    {
        var manager = new ResourceManager("Localisation.WPF.Tests.TestResources", typeof(EnumAndManagedCoverageTests).Assembly);
        TypeConverter[] simpleConverters =
        [
            new Lean.ResourceEnumConverter(typeof(SampleValue), manager),
            new Reactive.ResourceEnumConverter(typeof(SampleValue), manager),
        ];
        TypeConverter[] flagConverters =
        [
            new Lean.ResourceEnumConverter(typeof(SampleFlags), manager),
            new Reactive.ResourceEnumConverter(typeof(SampleFlags), manager),
        ];
        foreach (var converter in simpleConverters)
        {
            await Assert.That(converter.ConvertTo(null, null, null, typeof(string))).IsNull();
            await Assert.That(converter.ConvertFrom(null, null, nameof(SampleValue.First))).IsEqualTo(SampleValue.First);
            await Assert.That(converter.ConvertFrom(null, null, new Enum[] { SampleValue.Second })).IsEqualTo(SampleValue.Second);
            await Assert.That(converter.ConvertTo(null, null, SampleValue.Second, typeof(Enum[]))).IsEquivalentTo(new Enum[] { SampleValue.Second });
            await Assert.That(converter.ConvertTo(null, null, SampleValue.First, typeof(object))).IsEqualTo("First localized");
            await Assert.That(((IValueConverter)converter).Convert(
                SampleValue.Second,
                typeof(string),
                null!,
                CultureInfo.InvariantCulture)).IsEqualTo("Second localized");
            await Assert.That(((IValueConverter)converter).ConvertBack(
                "First localized",
                typeof(SampleValue),
                null!,
                CultureInfo.InvariantCulture)).IsEqualTo(SampleValue.First);
            var debuggerDisplay = converter.GetType().GetProperty("DebuggerDisplay", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await Assert.That(debuggerDisplay.GetValue(converter)).IsNotNull();
        }

        foreach (var converter in flagConverters)
        {
            await Assert.That(converter.ConvertTo(null, null, SampleFlags.Read, typeof(string))).IsEqualTo("Read");
            await Assert.That(converter.ConvertTo(null, null, SampleFlags.Read | SampleFlags.Write, typeof(string))).IsEqualTo("Read, Write");
            await Assert.That(converter.ConvertFrom(null, null, "Read, Write")).IsEqualTo(SampleFlags.Read | SampleFlags.Write);
            await Assert.That(converter.ConvertFrom(null, null, "Unknown")).IsNull();
            await Assert.That(converter.ConvertTo(null, null, (SampleFlags)UnknownFlagBits, typeof(string))).IsNull();
        }

        await Assert.That(Lean.ResourceEnumConverter.GetValues(typeof(SampleValue), CultureInfo.InvariantCulture)).Count().IsEqualTo(EnumValueCount);
        await Assert.That(Reactive.ResourceEnumConverter.GetValues(typeof(SampleValue))).Count().IsEqualTo(EnumValueCount);
        await Assert.That(Lean.ResourceEnumConverter.ConvertToString(SampleValue.First)).IsEqualTo("First");
        await Assert.That(Reactive.ResourceEnumConverter.ConvertToString(SampleValue.Second)).IsEqualTo("Second");
    }
}
