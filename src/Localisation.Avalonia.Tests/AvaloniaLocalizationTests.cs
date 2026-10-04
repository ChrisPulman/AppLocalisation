// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Lean = CP.Localisation.Avalonia;
using Reactive = CP.Localisation.Avalonia.Reactive;

[assembly: NotInParallel]

namespace Localisation.Avalonia.Tests;

internal sealed class AvaloniaLocalizationTests
{
    private const int ConvertedInteger = 42;

    private const int ExpectedNotificationCount = 2;

    private const int ExpectedRefreshCount = 4;

    private const int UnknownFlagBits = 8;

    private const string EnglishCultureName = "en-US";

    private const string FrenchCultureName = "fr-FR";

    private const string FirstLocalizedValue = "First localized";

    private const string FallbackValue = "Fallback";

    private const string GermanCultureName = "de-DE";

    private const string GreetingKey = "Greeting";

    private const string GreetingValue = "Hello";

    private const string InvalidIntegerText = "not-a-number";

    private const string MissingKey = "Missing";

    private const string ReadWriteText = "Read, Write";

    private const string TestResxName = "Localisation.Avalonia.Tests.TestResources";

    [Test]
    internal async Task CultureManagersPublishAndSynchronizeChanges()
    {
        var originalLeanCulture = Lean.CultureManager.UICulture;
        var originalLeanSynchronization = Lean.CultureManager.SynchronizeThreadCulture;
        var originalReactiveCulture = Reactive.CultureManager.UICulture;
        var originalReactiveSynchronization = Reactive.CultureManager.SynchronizeThreadCulture;
        var leanEvents = 0;
        var reactiveEvents = 0;
        var leanObserver = new RecordingObserver<ReactiveUI.Primitives.RxVoid>();
        var reactiveObserver = new RecordingObserver<System.Reactive.Unit>();
        EventHandler leanHandler = (_, _) => leanEvents++;
        EventHandler reactiveHandler = (_, _) => reactiveEvents++;
        Lean.CultureManager.UICultureChanged += leanHandler;
        Reactive.CultureManager.UICultureChanged += reactiveHandler;
        using var leanSubscription = Lean.CultureManager.UICultureChangedObserver.Subscribe(leanObserver);
        using var reactiveSubscription = Reactive.CultureManager.UICultureChangedObserver.Subscribe(reactiveObserver);

        try
        {
            var leanCulture = DifferentCulture(originalLeanCulture);
            var reactiveCulture = DifferentCulture(originalReactiveCulture, leanCulture);
            Lean.CultureManager.SynchronizeThreadCulture = false;
            Reactive.CultureManager.SynchronizeThreadCulture = false;
            Lean.CultureManager.UICulture = leanCulture;
            Reactive.CultureManager.UICulture = reactiveCulture;
            Lean.CultureManager.UICulture = leanCulture;
            Reactive.CultureManager.UICulture = reactiveCulture;
            Lean.CultureManager.Refresh();
            Reactive.CultureManager.Refresh();

            await Assert.That(Lean.CultureManager.UICulture).IsEqualTo(leanCulture);
            await Assert.That(Reactive.CultureManager.UICulture).IsEqualTo(reactiveCulture);
            await Assert.That(leanEvents).IsEqualTo(ExpectedNotificationCount);
            await Assert.That(reactiveEvents).IsEqualTo(ExpectedNotificationCount);
            await Assert.That(leanObserver.Values).Count().IsEqualTo(ExpectedNotificationCount);
            await Assert.That(reactiveObserver.Values).Count().IsEqualTo(ExpectedNotificationCount);

            Lean.CultureManager.SynchronizeThreadCulture = true;
            await Assert.That(CultureInfo.CurrentCulture).IsEqualTo(leanCulture);
            Reactive.CultureManager.SynchronizeThreadCulture = true;
            await Assert.That(CultureInfo.CurrentUICulture).IsEqualTo(reactiveCulture);
        }
        finally
        {
            Lean.CultureManager.UICultureChanged -= leanHandler;
            Reactive.CultureManager.UICultureChanged -= reactiveHandler;
            Lean.CultureManager.SynchronizeThreadCulture = originalLeanSynchronization;
            Reactive.CultureManager.SynchronizeThreadCulture = originalReactiveSynchronization;
            Lean.CultureManager.UICulture = originalLeanCulture;
            Reactive.CultureManager.UICulture = originalReactiveCulture;
        }
    }

    [Test]
    internal async Task ResourceExtensionsResolveEmbeddedOverridesDefaultsAndFormatting()
    {
        var target = new TextBlock();
        Lean.ResxExtension.SetDefaultResxName(target, TestResxName);
        Reactive.ResxExtension.SetDefaultResxName(target, TestResxName);
        var lean = new Lean.ResxExtension($"  {GreetingKey}  ");
        var reactive = new Reactive.ResxExtension(GreetingKey);

        await Assert.That(Lean.ResxExtension.GetDefaultResxName(target)).IsEqualTo(TestResxName);
        await Assert.That(Reactive.ResxExtension.GetDefaultResxName(target)).IsEqualTo(TestResxName);
        await Assert.That(lean.Key).IsEqualTo(GreetingKey);
        await Assert.That(lean.ResolveValue(target, typeof(string))).IsEqualTo(GreetingValue);
        await Assert.That(lean.ResolveValue(target, typeof(string))).IsEqualTo(GreetingValue);
        await Assert.That(reactive.ResolveValue(target, typeof(string))).IsEqualTo(GreetingValue);
        await Assert.That(reactive.ResolveValue(target, typeof(string))).IsEqualTo(GreetingValue);

        lean.Key = MissingKey;
        await Assert.That(lean.ResolveValue(target, typeof(string))).IsEqualTo($"#{MissingKey}");
        lean.DefaultValue = ConvertedInteger.ToString(CultureInfo.InvariantCulture);
        await Assert.That(lean.ResolveValue(target, typeof(int))).IsEqualTo(ConvertedInteger);
        lean.DefaultValue = InvalidIntegerText;
        await Assert.That(lean.ResolveValue(target, typeof(int))).IsEqualTo(InvalidIntegerText);
        lean.DefaultValue = null;
        await Assert.That(lean.ResolveValue(target, typeof(int))).IsNull();

        lean.Key = GreetingKey;
        lean.BindingStringFormat = "Value: {0}";
        await Assert.That(lean.ResolveValue(target, typeof(string))).IsEqualTo("Value: Hello");
        lean.BindingStringFormat = null;
        lean.BindingConverter = new PrefixConverter();
        lean.BindingConverterParameter = "Converted";
        await Assert.That(lean.ResolveValue(target, typeof(string))).IsEqualTo("Converted:Hello");

        EventHandler<Lean.GetResourceEventArgs> handler = (_, args) => args.Resource = "Override";
        Lean.ResxExtension.GetResource += handler;
        try
        {
            lean.BindingConverter = null;
            await Assert.That(lean.ResolveValue(target, typeof(string))).IsEqualTo("Override");
        }
        finally
        {
            Lean.ResxExtension.GetResource -= handler;
        }
    }

    [Test]
    internal async Task RefreshingObservablesRespectKeysAndDisposeSubscriptions()
    {
        var leanValue = 0;
        var reactiveValue = 0;
        var leanObserver = new RecordingObserver<int>();
        var reactiveObserver = new RecordingObserver<int>();
        var lean = new Lean.RefreshingObservable<int>(() => ++leanValue, "A", observeResourceChanges: true);
        var reactive = new Reactive.RefreshingObservable<int>(() => ++reactiveValue, "A", observeResourceChanges: true);
        var leanSubscription = lean.Subscribe(leanObserver);
        var reactiveSubscription = reactive.Subscribe(reactiveObserver);

        Lean.ResxExtension.UpdateTarget("B");
        Reactive.ResxExtension.UpdateTarget("B");
        Lean.ResxExtension.UpdateTarget("A");
        Reactive.ResxExtension.UpdateTarget("A");
        Lean.ResxExtension.UpdateAllTargets();
        Reactive.ResxExtension.UpdateAllTargets();
        Lean.CultureManager.Refresh();
        Reactive.CultureManager.Refresh();

        await Assert.That(leanObserver.Values).Count().IsEqualTo(ExpectedRefreshCount);
        await Assert.That(reactiveObserver.Values).Count().IsEqualTo(ExpectedRefreshCount);
        leanSubscription.Dispose();
        leanSubscription.Dispose();
        reactiveSubscription.Dispose();
        reactiveSubscription.Dispose();
        Lean.CultureManager.Refresh();
        Reactive.CultureManager.Refresh();
        await Assert.That(leanObserver.Values).Count().IsEqualTo(ExpectedRefreshCount);
        await Assert.That(reactiveObserver.Values).Count().IsEqualTo(ExpectedRefreshCount);
    }

    [Test]
    internal async Task MarkupExtensionsCreateAvaloniaBindings()
    {
        var leanTarget = new TextBlock();
        var reactiveTarget = new TextBlock();
        var leanCultureTarget = new TextBlock();
        var reactiveCultureTarget = new TextBlock();
        var provider = new TestServiceProvider(leanTarget, TextBlock.TextProperty);
        var lean = new Lean.ResxExtension(GreetingKey) { ResxName = TestResxName };
        var reactive = new Reactive.ResxExtension(GreetingKey) { ResxName = TestResxName };
        var leanBinding = (global::Avalonia.Data.BindingBase)lean.ProvideValue(provider);
        var reactiveBinding = (global::Avalonia.Data.BindingBase)reactive.ProvideValue(
            new TestServiceProvider(reactiveTarget, TextBlock.TextProperty));
        var leanCultureBinding = (global::Avalonia.Data.BindingBase)new Lean.UICultureExtension().ProvideValue(provider);
        var reactiveCultureBinding = (global::Avalonia.Data.BindingBase)new Reactive.UICultureExtension().ProvideValue(provider);
        using var leanSubscription = leanTarget.Bind(TextBlock.TextProperty, leanBinding);
        using var reactiveSubscription = reactiveTarget.Bind(TextBlock.TextProperty, reactiveBinding);
        using var leanCultureSubscription = leanCultureTarget.Bind(TextBlock.TextProperty, leanCultureBinding);
        using var reactiveCultureSubscription = reactiveCultureTarget.Bind(TextBlock.TextProperty, reactiveCultureBinding);

        await Assert.That(leanTarget.Text).IsEqualTo(GreetingValue);
        await Assert.That(reactiveTarget.Text).IsEqualTo(GreetingValue);
        await Assert.That(leanCultureTarget.Text).IsEqualTo(Lean.CultureManager.UICulture.IetfLanguageTag);
        await Assert.That(reactiveCultureTarget.Text).IsEqualTo(Reactive.CultureManager.UICulture.IetfLanguageTag);
        Lean.UICultureExtension.UpdateAllTargets();
        Reactive.UICultureExtension.UpdateAllTargets();
    }

    [Test]
    internal async Task EnumConvertersLocalizeSimpleAndFlagValues()
    {
        var manager = new ResourceManager(TestResxName, typeof(AvaloniaLocalizationTests).Assembly);
        var leanSimple = new Lean.ResourceEnumConverter(typeof(SampleValue), manager);
        var reactiveSimple = new Reactive.ResourceEnumConverter(typeof(SampleValue), manager);
        var leanFlags = new Lean.ResourceEnumConverter(typeof(SampleFlags), manager);
        var reactiveFlags = new Reactive.ResourceEnumConverter(typeof(SampleFlags), manager);

        await Assert.That(leanSimple.ConvertTo(null, CultureInfo.InvariantCulture, SampleValue.First, typeof(string))).IsEqualTo(FirstLocalizedValue);
        await Assert.That(reactiveSimple.ConvertFrom(null, CultureInfo.InvariantCulture, "Second localized")).IsEqualTo(SampleValue.Second);
        var enumPassThroughRejected = false;
        try
        {
            _ = leanSimple.ConvertFrom(null, CultureInfo.InvariantCulture, SampleValue.First);
        }
        catch (NotSupportedException)
        {
            enumPassThroughRejected = true;
        }

        await Assert.That(enumPassThroughRejected).IsTrue();
        await Assert.That(reactiveSimple.ConvertTo(null, CultureInfo.InvariantCulture, null, typeof(string))).IsNull();
        const SampleFlags combinedFlags = SampleFlags.Read | SampleFlags.Write;
        var leanFlagsText = leanFlags.ConvertTo(null, CultureInfo.InvariantCulture, combinedFlags, typeof(string));
        var reactiveFlagsValue = reactiveFlags.ConvertFrom(null, CultureInfo.InvariantCulture, "Read, Write");
        await Assert.That(leanFlagsText).IsEqualTo(ReadWriteText);
        await Assert.That(reactiveFlagsValue).IsEqualTo(combinedFlags);
        await Assert.That(leanFlags.ConvertFrom(null, CultureInfo.InvariantCulture, "Unknown")).IsNull();
        await Assert.That(
            reactiveFlags.ConvertTo(null, CultureInfo.InvariantCulture, (SampleFlags)UnknownFlagBits, typeof(string))).IsNull();
        var convertedValue = ((IValueConverter)leanSimple).Convert(
            SampleValue.First,
            typeof(string),
            null,
            CultureInfo.InvariantCulture);
        var convertedBackValue = ((IValueConverter)reactiveSimple).ConvertBack(
            "Second localized",
            typeof(SampleValue),
            null,
            CultureInfo.InvariantCulture);
        await Assert.That(convertedValue).IsEqualTo(FirstLocalizedValue);
        await Assert.That(convertedBackValue).IsEqualTo(SampleValue.Second);
        await Assert.That(
            Lean.ResourceEnumConverter.GetValues(typeof(SampleValue), CultureInfo.InvariantCulture)).Count().IsEqualTo(ExpectedNotificationCount);
        await Assert.That(
            Reactive.ResourceEnumConverter.GetValues(typeof(SampleValue))).Count().IsEqualTo(ExpectedNotificationCount);
        await Assert.That(Lean.ResourceEnumConverter.ConvertToString(SampleValue.First)).IsEqualTo(nameof(SampleValue.First));
        await Assert.That(((IValueConverter)reactiveSimple).Convert(
            SampleValue.First,
            typeof(string),
            null,
            CultureInfo.InvariantCulture)).IsEqualTo(FirstLocalizedValue);
        await Assert.That(reactiveFlags.ConvertFrom(null, CultureInfo.InvariantCulture, "Unknown")).IsNull();
        await Assert.That(reactiveFlags.ConvertTo(
            null,
            CultureInfo.InvariantCulture,
            combinedFlags,
            typeof(string))).IsEqualTo(ReadWriteText);
    }

    [Test]
    internal async Task ConverterGuardsCoverDefensivePaths()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var manager = new ResourceManager(TestResxName, typeof(AvaloniaLocalizationTests).Assembly);
        var leanSimple = new Lean.ResourceEnumConverter(typeof(SampleValue), manager);
        var reactiveSimple = new Reactive.ResourceEnumConverter(typeof(SampleValue), manager);
        var nullManagerRejected = false;
        var invalidDestinationRejected = false;
        var leanInvalidDestinationRejected = false;
        var reactiveEnumPassThroughRejected = false;
        try
        {
            Lean.Extensions.SyncCultureInfo(CultureInfo.InvariantCulture);
            Reactive.Extensions.SyncCultureInfo(originalCulture);
            _ = new Lean.ResourceEnumConverter(typeof(SampleValue), null!);
        }
        catch (ArgumentNullException)
        {
            nullManagerRejected = true;
        }

        try
        {
            _ = reactiveSimple.ConvertTo(null, null, SampleValue.First, typeof(int));
        }
        catch (NotSupportedException)
        {
            invalidDestinationRejected = true;
        }

        try
        {
            _ = leanSimple.ConvertTo(null, null, SampleValue.First, typeof(int));
        }
        catch (NotSupportedException)
        {
            leanInvalidDestinationRejected = true;
        }

        try
        {
            _ = reactiveSimple.ConvertFrom(null, null, SampleValue.First);
        }
        catch (NotSupportedException)
        {
            reactiveEnumPassThroughRejected = true;
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        await Assert.That(nullManagerRejected).IsTrue();
        await Assert.That(invalidDestinationRejected).IsTrue();
        await Assert.That(leanInvalidDestinationRejected).IsTrue();
        await Assert.That(reactiveEnumPassThroughRejected).IsTrue();
    }

    [Test]
    internal async Task ExtensionHelpersAndConverterFallbacksResolveValues()
    {
        var manager = new ResourceManager(TestResxName, typeof(AvaloniaLocalizationTests).Assembly);
        var leanSimple = new Lean.ResourceEnumConverter(typeof(SampleValue), manager);
        var reactiveSimple = new Reactive.ResourceEnumConverter(typeof(SampleValue), manager);
        var leanFlags = new Lean.ResourceEnumConverter(typeof(SampleFlags), manager);
        var reactiveFlags = new Reactive.ResourceEnumConverter(typeof(SampleFlags), manager);

        await Assert.That(Lean.Extensions.ConvertToString(SampleValue.First)).IsEqualTo(nameof(SampleValue.First));
        await Assert.That(Reactive.Extensions.ConvertToString(SampleValue.Second)).IsEqualTo(nameof(SampleValue.Second));
        await Assert.That(leanSimple.ConvertFrom(null, null, FirstLocalizedValue)).IsEqualTo(SampleValue.First);
        await Assert.That(leanSimple.ConvertFrom(null, null, FirstLocalizedValue)).IsEqualTo(SampleValue.First);
        await Assert.That(reactiveSimple.ConvertFrom(null, null, nameof(SampleValue.First))).IsEqualTo(SampleValue.First);
        await Assert.That(leanFlags.ConvertFrom(null, null, ReadWriteText)).IsEqualTo(SampleFlags.Read | SampleFlags.Write);
        await Assert.That(leanFlags.ConvertTo(null, null, SampleFlags.Read, typeof(object))).IsEqualTo("Read");
        await Assert.That(reactiveFlags.ConvertTo(null, null, SampleFlags.None, typeof(string))).IsEqualTo(
            $"{nameof(SampleFlags)}_{nameof(SampleFlags.None)}");
        await Assert.That(((IValueConverter)leanFlags).ConvertBack(
            "Read",
            typeof(SampleFlags),
            null,
            CultureInfo.InvariantCulture)).IsEqualTo(SampleFlags.Read);
        await Assert.That(leanSimple.ConvertTo(null, null, null, typeof(string))).IsNull();
        await Assert.That(Lean.ResourceEnumConverter.GetValues(typeof(SampleValue))).Count().IsEqualTo(ExpectedNotificationCount);
    }

    [Test]
    internal async Task AdditionalResxPathsHandleMissingResourcesAndClrProperties()
    {
        var lean = new Lean.ResxExtension
        {
            DefaultValue = FallbackValue,
            Key = MissingKey,
            ResxName = "Missing.Resource.Name",
        };
        var reactive = new Reactive.ResxExtension
        {
            DefaultValue = ConvertedInteger,
            Key = MissingKey,
            ResxName = "Missing.Resource.Name",
        };
        var clrTarget = new TestAvaloniaObject();
        var clrProperty = typeof(TestAvaloniaObject).GetProperty(nameof(TestAvaloniaObject.Value))!;

        await Assert.That(lean.ResolveValue(null, typeof(string))).IsEqualTo(FallbackValue);
        await Assert.That(lean.ResolveValue(null, typeof(string))).IsEqualTo(FallbackValue);
        await Assert.That(reactive.ResolveValue(null, typeof(int))).IsEqualTo(ConvertedInteger);
        reactive.DefaultValue = ConvertedInteger.ToString(CultureInfo.InvariantCulture);
        await Assert.That(reactive.ResolveValue(null, typeof(int))).IsEqualTo(ConvertedInteger);
        reactive.DefaultValue = InvalidIntegerText;
        await Assert.That(reactive.ResolveValue(null, typeof(int))).IsEqualTo(InvalidIntegerText);
        reactive.DefaultValue = null;
        await Assert.That(reactive.ResolveValue(null, typeof(object))).IsEqualTo($"#{MissingKey}");
        await Assert.That(lean.ProvideValue(new TestServiceProvider(clrTarget, clrProperty)) is global::Avalonia.Data.BindingBase).IsTrue();
        await Assert.That(reactive.ProvideValue(new TestServiceProvider(clrTarget, clrProperty)) is global::Avalonia.Data.BindingBase).IsTrue();
        await Assert.That(lean.ProvideValue(null) is global::Avalonia.Data.BindingBase).IsTrue();
        await Assert.That(reactive.ProvideValue(null) is global::Avalonia.Data.BindingBase).IsTrue();

        reactive.Key = GreetingKey;
        reactive.ResxName = TestResxName;
        reactive.DefaultValue = null;
        reactive.BindingConverter = new PrefixConverter();
        reactive.BindingConverterParameter = "Converted";
        await Assert.That(reactive.ResolveValue(null, typeof(string))).IsEqualTo("Converted:Hello");
        reactive.BindingConverter = null;
        reactive.BindingStringFormat = "Value: {0}";
        await Assert.That(reactive.ResolveValue(null, typeof(string))).IsEqualTo("Value: Hello");
    }

    [Test]
    internal async Task HeadlessAvaloniaSessionCreatesControls()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        var text = await session.Dispatch(
            () => new TextBlock { Text = "Headless" }.Text,
            CancellationToken.None);
        await Assert.That(text).IsEqualTo("Headless");
    }

    /// <summary>Verifies optional keys and invalid targets use the documented fallback and guards.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task ResourceExtensionsHandleNullKeysAndGuardAttachedTargets()
    {
        var lean = new Lean.ResxExtension { Key = null, ResxName = TestResxName };
        var reactive = new Reactive.ResxExtension { Key = null, ResxName = TestResxName };
        await Assert.That(lean.ResolveValue(null, typeof(object))).IsEqualTo("#");
        await Assert.That(reactive.ResolveValue(null, typeof(object))).IsEqualTo("#");
        await Assert.That(reactive.ResolveValue(null, typeof(int))).IsNull();
        await Assert.That(lean.ProvideValue(new TestServiceProvider(new object(), new object()))).IsNotNull();
        await Assert.That(reactive.ProvideValue(new TestServiceProvider(new object(), new object()))).IsNotNull();
        await Assert.That(() => Lean.ResxExtension.SetDefaultResxName(null!, null)).Throws<ArgumentNullException>();
        await Assert.That(() => Reactive.ResxExtension.SetDefaultResxName(null!, null)).Throws<ArgumentNullException>();
        await Assert.That(() => Reactive.ResxExtension.GetDefaultResxName(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Reactive.ResxExtension.UpdateTarget(" ")).Throws<ArgumentException>();
        await Assert.That(() => new Lean.GetResourceEventArgs(null, null, null!)).Throws<ArgumentNullException>();
        var manager = new ResourceManager(TestResxName, typeof(AvaloniaLocalizationTests).Assembly);
        await Assert.That(() => new Reactive.ResourceEnumConverter(typeof(SampleValue), null!)).Throws<ArgumentNullException>();
        var converter = new Lean.ResourceEnumConverter(typeof(SampleValue), manager);
        await Assert.That(((IValueConverter)converter).ConvertBack(
            FirstLocalizedValue,
            typeof(SampleValue),
            null,
            CultureInfo.InvariantCulture)).IsEqualTo(SampleValue.First);
    }

    [Test]
    internal async Task ValidationGuardsRejectInvalidArguments()
    {
        var nullAttachedTargetRejected = false;
        var whitespaceKeyRejected = false;
        var nullCultureRejected = false;
        try
        {
            _ = Lean.ResxExtension.GetDefaultResxName(null!);
        }
        catch (ArgumentNullException)
        {
            nullAttachedTargetRejected = true;
        }

        try
        {
            Lean.ResxExtension.UpdateTarget(" ");
        }
        catch (ArgumentException)
        {
            whitespaceKeyRejected = true;
        }

        try
        {
            _ = new Reactive.GetResourceEventArgs(null, null, null!);
        }
        catch (ArgumentNullException)
        {
            nullCultureRejected = true;
        }

        await Assert.That(nullAttachedTargetRejected).IsTrue();
        await Assert.That(whitespaceKeyRejected).IsTrue();
        await Assert.That(nullCultureRejected).IsTrue();
    }

    private static CultureInfo DifferentCulture(CultureInfo current, CultureInfo? excluded = null)
    {
        if (!CultureMatches(current, FrenchCultureName) && !CultureMatches(excluded, FrenchCultureName))
        {
            return new(FrenchCultureName);
        }

        var cultureName = !CultureMatches(current, EnglishCultureName) && !CultureMatches(excluded, EnglishCultureName)
            ? EnglishCultureName
            : GermanCultureName;
        return new(cultureName);
    }

    private static bool CultureMatches(CultureInfo? culture, string name) =>
        culture?.Name.Equals(name, StringComparison.OrdinalIgnoreCase) == true;

    private sealed class PrefixConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => $"{parameter}:{value}";

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
    }

    private sealed class RecordingObserver<T> : IObserver<T>
    {
        public List<T> Values { get; } = [];

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => Values.Add(value);
    }

    private sealed class TestServiceProvider(object target, object property) : IServiceProvider, IProvideValueTarget
    {
        public object TargetObject { get; set; } = target;

        public object TargetProperty { get; set; } = property;

        public object? GetService(Type serviceType) => serviceType == typeof(IProvideValueTarget) ? this : null;
    }

    private sealed class TestAvaloniaObject : global::Avalonia.AvaloniaObject
    {
        public string? Value { get; set; }
    }
}
