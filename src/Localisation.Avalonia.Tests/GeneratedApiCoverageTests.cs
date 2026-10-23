// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Resources;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Lean = CP.Localisation.Avalonia;
using Reactive = CP.Localisation.Avalonia.Reactive;

namespace Localisation.Avalonia.Tests;

internal sealed class GeneratedApiCoverageTests
{
    private const string DictionaryName = "Localisation.Avalonia.Tests.TestResources";

    private const string AssemblyName = "Api.Tests";

    private const string MissingName = "Missing.Dictionary";

    private const string MissingKey = "Missing";

    private const string RegisteredName = "Api.Generated";

    private const string CustomKey = "Custom";

    private const string UnknownText = "Unknown";

    private const string Format = "[{0}]";

    private const string TranslatedText = "Translated";

    [Test]
    internal async Task RegistryValidatesInputsAndListsAllCultures()
    {
        await Assert.That(CP.Localisation.LocalisationResourceRegistry.GetAvailableCultures()).IsNotEmpty();
        await Assert.That(() => CP.Localisation.LocalisationResourceRegistry.Register(
            string.Empty,
            DictionaryName,
            static (_, _) => null)).Throws<ArgumentException>();
        await Assert.That(() => CP.Localisation.LocalisationResourceRegistry.Register(
            AssemblyName,
            string.Empty,
            static (_, _) => null)).Throws<ArgumentException>();
        await Assert.That(() => CP.Localisation.LocalisationResourceRegistry.Register(
            AssemblyName,
            DictionaryName,
            null!)).Throws<ArgumentNullException>();
        await Assert.That(() => CP.Localisation.LocalisationResourceRegistry.TryGetResource(
            DictionaryName,
            MissingKey,
            null!,
            out _)).Throws<ArgumentNullException>();
        await Assert.That(CP.Localisation.LocalisationResourceRegistry.TryGetResource(
            "Unknown.Assembly",
            DictionaryName,
            MissingKey,
            CultureInfo.InvariantCulture,
            out var missing)).IsFalse();
        await Assert.That(missing).IsNull();
    }

    [Test]
    internal async Task GeneratedOptionsPreserveExplicitConversionAndInheritedNames()
    {
        var target = new Grid();
        var converter = new FuncValueConverter<object?, string>(static value => value?.ToString() ?? string.Empty);
        var parameter = new object();
        var lean = new Lean.GeneratedResxExtension
        {
            Key = MissingKey,
            BindingConverter = converter,
            BindingConverterParameter = parameter,
            BindingStringFormat = Format,
            DefaultValue = CustomKey,
        };
        var reactive = new Reactive.GeneratedResxExtension
        {
            Key = MissingKey,
            BindingConverter = converter,
            BindingConverterParameter = parameter,
            BindingStringFormat = Format,
            DefaultValue = CustomKey,
        };
        Lean.GeneratedResxExtension.SetDefaultResxName(target, DictionaryName);
        await Assert.That(Lean.GeneratedResxExtension.GetDefaultResxName(target)).IsEqualTo(DictionaryName);
        Reactive.GeneratedResxExtension.SetDefaultResxName(target, DictionaryName);
        await Assert.That(Reactive.GeneratedResxExtension.GetDefaultResxName(target)).IsEqualTo(DictionaryName);
        await Assert.That(lean.BindingConverter).IsSameReferenceAs(converter);
        await Assert.That(reactive.BindingConverter).IsSameReferenceAs(converter);
        await Assert.That(lean.BindingConverterParameter).IsSameReferenceAs(parameter);
        await Assert.That(reactive.BindingConverterParameter).IsSameReferenceAs(parameter);
        await Assert.That(lean.BindingStringFormat).IsEqualTo(Format);
        await Assert.That(reactive.BindingStringFormat).IsEqualTo(Format);
        await Assert.That(lean.DefaultValue).IsEqualTo(CustomKey);
        await Assert.That(reactive.DefaultValue).IsEqualTo(CustomKey);
        await Assert.That(lean.Key).IsEqualTo(MissingKey);
        await Assert.That(reactive.Key).IsEqualTo(MissingKey);
        lean.ResxName = MissingName;
        reactive.ResxName = MissingName;
        await Assert.That(lean.ResxName).IsEqualTo(MissingName);
        await Assert.That(reactive.ResxName).IsEqualTo(MissingName);
        await Assert.That(lean.ResolveValue(target, typeof(string))).IsEqualTo("[Custom]");
        await Assert.That(reactive.ResolveValue(target, typeof(string))).IsEqualTo("[Custom]");
        await Assert.That(lean.ProvideValue(null)).IsNotNull();
        await Assert.That(reactive.ProvideValue(null)).IsNotNull();
        Lean.GeneratedResxExtension.UpdateAllTargets();
        Reactive.GeneratedResxExtension.UpdateAllTargets();
        Lean.GeneratedResxExtension.UpdateTarget(MissingKey);
        Reactive.GeneratedResxExtension.UpdateTarget(MissingKey);
    }

    [Test]
    internal async Task GenericConvertersUseRegisteredProvidersAndCustomKeys()
    {
        CP.Localisation.LocalisationResourceRegistry.Register(
            AssemblyName,
            RegisteredName,
            static (key, _) => key == CustomKey ? TranslatedText : null);
        var lean = new Lean.ResourceEnumConverter<SampleValue>(RegisteredName, false, static _ => CustomKey);
        var reactive = new Reactive.ResourceEnumConverter<SampleValue>(RegisteredName, false, static _ => CustomKey);
        await Assert.That(lean.ConvertToString(SampleValue.First)).IsEqualTo(TranslatedText);
        await Assert.That(reactive.ConvertToString(SampleValue.First)).IsEqualTo(TranslatedText);
        await Assert.That(lean.ConvertFromString(TranslatedText)).IsEqualTo(SampleValue.First);
        await Assert.That(reactive.ConvertFromString(TranslatedText)).IsEqualTo(SampleValue.First);
        await Assert.That(lean.GetValues()).IsNotEmpty();
        await Assert.That(reactive.GetValues()).IsNotEmpty();
        var missingLean = new Lean.ResourceEnumConverter<SampleValue>(MissingName);
        var missingReactive = new Reactive.ResourceEnumConverter<SampleValue>(MissingName);
        await Assert.That(missingLean.ConvertToString(SampleValue.First)).IsEqualTo("SampleValue_First");
        await Assert.That(missingReactive.ConvertToString(SampleValue.First)).IsEqualTo("SampleValue_First");
        await Assert.That(missingLean.ConvertFromString(nameof(SampleValue.Second))).IsEqualTo(SampleValue.Second);
        await Assert.That(missingReactive.ConvertFromString(nameof(SampleValue.Second))).IsEqualTo(SampleValue.Second);
    }

    [Test]
    internal async Task GenericFlagsRejectUnknownTextAndBindingTypes()
    {
        var manager = new ResourceManager(DictionaryName, typeof(GeneratedApiCoverageTests).Assembly);
        var lean = new Lean.ResourceEnumConverter<SampleFlags>(manager, true, static value => value.ToString());
        var reactive = new Reactive.ResourceEnumConverter<SampleFlags>(manager, true, static value => value.ToString());
        await Assert.That(lean.ConvertFromString(UnknownText)).IsNull();
        await Assert.That(reactive.ConvertFromString(UnknownText)).IsNull();
        await Assert.That(lean.ConvertToString(SampleFlags.None)).IsEqualTo(nameof(SampleFlags.None));
        await Assert.That(reactive.ConvertToString(SampleFlags.None)).IsEqualTo(nameof(SampleFlags.None));
        IValueConverter leanBinding = lean;
        IValueConverter reactiveBinding = reactive;
        await Assert.That(leanBinding.Convert(new(), typeof(string), null, CultureInfo.InvariantCulture)).IsNull();
        await Assert.That(reactiveBinding.Convert(new(), typeof(string), null, CultureInfo.InvariantCulture)).IsNull();
        await Assert.That(leanBinding.ConvertBack(new(), typeof(SampleFlags), null, CultureInfo.InvariantCulture)).IsNull();
        await Assert.That(reactiveBinding.ConvertBack(new(), typeof(SampleFlags), null, CultureInfo.InvariantCulture)).IsNull();
        var leanRegisteredFlags = new Lean.ResourceEnumConverter<SampleFlags>(MissingName, true);
        var reactiveRegisteredFlags = new Reactive.ResourceEnumConverter<SampleFlags>(MissingName, true);
        await Assert.That(leanRegisteredFlags.ConvertFromString(UnknownText)).IsNull();
        await Assert.That(reactiveRegisteredFlags.ConvertFromString(UnknownText)).IsNull();
    }
}
