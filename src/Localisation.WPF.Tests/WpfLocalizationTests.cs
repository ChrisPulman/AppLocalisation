// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using Lean = CP.Localisation;
using Reactive = CP.Localisation.Reactive;

[assembly: NotInParallel]

namespace Localisation.WPF.Tests;

internal sealed class WpfLocalizationTests
{
    private const string BindingElementName = "Element";

    private const string BindingGroupName = "Group";

    private const string BindingXPath = "Value";

    private const int ExpectedEnumValueCount = 2;

    private const int UnknownFlagBits = 8;

    private const string EnglishCultureName = "en-US";

    private const string FrenchCultureName = "fr-FR";

    private const string GermanCultureName = "de-DE";

    private const string GreetingKey = "Greeting";

    private const string InitialValue = "Initial";

    private const string TestResxName = "Localisation.WPF.Tests.TestResources";

    private const string UpdatedValue = "Updated";

    private const string DebuggerDisplayProperty = "DebuggerDisplay";

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
            Lean.CultureManager.UICulture = null!;
            Reactive.CultureManager.UICulture = null!;

            await Assert.That(Lean.CultureManager.UICulture).IsEqualTo(leanCulture);
            await Assert.That(Reactive.CultureManager.UICulture).IsEqualTo(reactiveCulture);
            await Assert.That(leanEvents).IsEqualTo(1);
            await Assert.That(reactiveEvents).IsEqualTo(1);
            await Assert.That(leanObserver.Values).Count().IsEqualTo(1);
            await Assert.That(reactiveObserver.Values).Count().IsEqualTo(1);

            Lean.CultureManager.SynchronizeThreadCulture = true;
            await Assert.That(CultureInfo.CurrentCulture.Name).IsEqualTo(leanCulture.Name);
            Reactive.CultureManager.SynchronizeThreadCulture = true;
            await Assert.That(CultureInfo.CurrentCulture.Name).IsEqualTo(reactiveCulture.Name);
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
    internal async Task ExtensionMethodsSynchronizeCulturesAndConvertEnums()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var culture = DifferentCulture(originalCulture);
        try
        {
            Lean.Extensions.SyncCultureInfo(culture);
            await Assert.That(CultureInfo.CurrentCulture).IsEqualTo(culture);
            await Assert.That(CultureInfo.CurrentUICulture).IsEqualTo(culture);
            Reactive.Extensions.SyncCultureInfo(originalCulture);
            await Assert.That(CultureInfo.CurrentCulture).IsEqualTo(originalCulture);
            await Assert.That(Lean.Extensions.ConvertToString(SampleValue.First)).IsEqualTo(nameof(SampleValue.First));
            await Assert.That(Reactive.Extensions.ConvertToString(SampleValue.Second)).IsEqualTo(nameof(SampleValue.Second));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    internal async Task ResourceEventArgumentsExposeValuesAndValidateCulture()
    {
        var lean = new Lean.GetResourceEventArgs(TestResxName, GreetingKey, CultureInfo.InvariantCulture)
        {
            Resource = "Lean",
        };
        var reactive = new Reactive.GetResourceEventArgs(null, null, CultureInfo.InvariantCulture)
        {
            Resource = "Reactive",
        };
        var nullCultureRejected = false;
        try
        {
            _ = new Lean.GetResourceEventArgs(null, null, null!);
        }
        catch (ArgumentNullException)
        {
            nullCultureRejected = true;
        }

        await Assert.That(lean.ResxName).IsEqualTo(TestResxName);
        await Assert.That(lean.Key).IsEqualTo(GreetingKey);
        await Assert.That(lean.Resource).IsEqualTo("Lean");
        await Assert.That(reactive.Resource).IsEqualTo("Reactive");
        await Assert.That(nullCultureRejected).IsTrue();
    }

    [Test]
    internal async Task EnumConvertersLocalizeSimpleAndFlagValues()
    {
        var manager = new ResourceManager(TestResxName, typeof(WpfLocalizationTests).Assembly);
        var leanSimple = new Lean.ResourceEnumConverter(typeof(SampleValue), manager);
        var reactiveSimple = new Reactive.ResourceEnumConverter(typeof(SampleValue), manager);
        var leanFlags = new Lean.ResourceEnumConverter(typeof(SampleFlags), manager);
        var reactiveFlags = new Reactive.ResourceEnumConverter(typeof(SampleFlags), manager);
        const SampleFlags combinedFlags = SampleFlags.Read | SampleFlags.Write;

        await Assert.That(leanSimple.ConvertTo(null, null, SampleValue.First, typeof(string))).IsEqualTo("First localized");
        await Assert.That(reactiveSimple.ConvertFrom(null, null, "Second localized")).IsEqualTo(SampleValue.Second);
        await Assert.That(leanFlags.ConvertTo(null, null, combinedFlags, typeof(string))).IsEqualTo("Read, Write");
        await Assert.That(reactiveFlags.ConvertFrom(null, null, "Read, Write")).IsEqualTo(combinedFlags);
        await Assert.That(leanFlags.ConvertFrom(null, null, "Unknown")).IsNull();
        await Assert.That(reactiveFlags.ConvertTo(null, null, (SampleFlags)UnknownFlagBits, typeof(string))).IsNull();
        await Assert.That(((IValueConverter)leanSimple).Convert(
            SampleValue.First,
            typeof(string),
            null!,
            CultureInfo.InvariantCulture)).IsEqualTo("First localized");
        await Assert.That(((IValueConverter)reactiveSimple).ConvertBack(
            "Second localized",
            typeof(SampleValue),
            null!,
            CultureInfo.InvariantCulture)).IsEqualTo(SampleValue.Second);
        await Assert.That(Lean.ResourceEnumConverter.GetValues(typeof(SampleValue))).Count().IsEqualTo(ExpectedEnumValueCount);
        await Assert.That(Reactive.ResourceEnumConverter.GetValues(
            typeof(SampleValue),
            CultureInfo.InvariantCulture)).Count().IsEqualTo(ExpectedEnumValueCount);
    }

    [Test]
    internal async Task ManagedExtensionsRegisterAndUpdateDependencyAndClrTargets()
    {
        var result = RunOnSta(
            static () =>
            {
                var manager = new Lean.MarkupExtensionManager(0);
                var dependencyExtension = new LeanTestExtension(manager) { Value = InitialValue };
                var dependencyTarget = new TextBlock();
                var dependencyProvider = new TestServiceProvider(dependencyTarget, TextBlock.TextProperty);
                var initialValue = dependencyExtension.ProvideValue(dependencyProvider);
                dependencyExtension.Value = UpdatedValue;
                manager.UpdateAllTargets();

                var clrExtension = new LeanTestExtension(manager) { Value = "CLR" };
                var clrTarget = new MutableTarget();
                var clrProperty = typeof(MutableTarget).GetProperty(nameof(MutableTarget.Value))!;
                _ = clrExtension.ProvideValue(new TestServiceProvider(clrTarget, clrProperty));
                manager.UpdateAllTargets();
                manager.CleanupInactiveExtensions();

                var templateExtension = new LeanTestExtension(manager);
                var templateValue = templateExtension.ProvideValue(new EmptyServiceProvider());
                var nullManagerRejected = false;
                try
                {
                    _ = new LeanTestExtension(null!);
                }
                catch (ArgumentNullException)
                {
                    nullManagerRejected = true;
                }

                return new ManagedExtensionResult(
                    initialValue,
                    dependencyTarget.Text,
                    dependencyExtension.IsTarget(dependencyTarget),
                    dependencyExtension.ExposedTargetPropertyType,
                    clrTarget.Value,
                    clrExtension.ExposedTargetPropertyType,
                    ReferenceEquals(templateValue, templateExtension),
                    templateExtension.IsTargetAlive,
                    nullManagerRejected);
            });

        await Assert.That(result.InitialValue).IsEqualTo(InitialValue);
        await Assert.That(result.DependencyValue).IsEqualTo(UpdatedValue);
        await Assert.That(result.IsDependencyTarget).IsTrue();
        await Assert.That(result.DependencyPropertyType).IsEqualTo(typeof(string));
        await Assert.That(result.ClrValue).IsEqualTo("CLR");
        await Assert.That(result.ClrPropertyType).IsEqualTo(typeof(string));
        await Assert.That(result.TemplateReturnsExtension).IsTrue();
        await Assert.That(result.IsTemplateTargetAlive).IsTrue();
        await Assert.That(result.NullManagerRejected).IsTrue();
    }

    [Test]
    internal async Task ResxAndUiCultureExtensionsUpdateRegisteredTargets()
    {
        var result = RunOnSta(
            static () =>
            {
                var target = new TextBlock();
                Lean.ResxExtension.SetDefaultResxName(target, TestResxName);
                Reactive.ResxExtension.SetDefaultResxName(target, TestResxName);
                var lean = new Lean.ResxExtension(GreetingKey) { ResxName = TestResxName };
                var reactive = new Reactive.ResxExtension(GreetingKey) { ResxName = TestResxName };
                EventHandler<Lean.GetResourceEventArgs> leanHandler = (_, args) => args.Resource = "Lean override";
                EventHandler<Reactive.GetResourceEventArgs> reactiveHandler = (_, args) => args.Resource = "Reactive override";
                Lean.ResxExtension.GetResource += leanHandler;
                Reactive.ResxExtension.GetResource += reactiveHandler;

                try
                {
                    var provider = new TestServiceProvider(target, TextBlock.TextProperty);
                    var leanValue = lean.ProvideValue(provider);
                    var reactiveValue = reactive.ProvideValue(provider);
                    Lean.ResxExtension.UpdateTarget(GreetingKey);
                    Reactive.ResxExtension.UpdateTarget(GreetingKey);
                    Reactive.ResxExtension.UpdateAllTargets();
                    var languageProvider = new TestServiceProvider(target, FrameworkElement.LanguageProperty);
                    var leanLanguage = new Lean.UICultureExtension().ProvideValue(languageProvider);
                    var reactiveLanguage = new Reactive.UICultureExtension().ProvideValue(languageProvider);
                    Lean.UICultureExtension.UpdateAllTargets();
                    Reactive.UICultureExtension.UpdateAllTargets();

                    return new ResxExtensionResult(
                        Lean.ResxExtension.GetDefaultResxName(target),
                        Reactive.ResxExtension.GetDefaultResxName(target),
                        leanValue,
                        reactiveValue,
                        leanLanguage is XmlLanguage,
                        reactiveLanguage is XmlLanguage);
                }
                finally
                {
                    Lean.ResxExtension.GetResource -= leanHandler;
                    Reactive.ResxExtension.GetResource -= reactiveHandler;
                }
            });

        await Assert.That(result.LeanDefaultResxName).IsEqualTo(TestResxName);
        await Assert.That(result.ReactiveDefaultResxName).IsEqualTo(TestResxName);
        await Assert.That(result.LeanValue).IsEqualTo("Lean override");
        await Assert.That(result.ReactiveValue).IsEqualTo("Reactive override");
        await Assert.That(result.LeanLanguageCreated).IsTrue();
        await Assert.That(result.ReactiveLanguageCreated).IsTrue();
    }

    [Test]
    internal async Task ReactiveManagedExtensionsRegisterAndUpdateTargets()
    {
        var result = RunOnSta(
            static () =>
            {
                var manager = new Reactive.MarkupExtensionManager(0);
                var target = new TextBlock();
                var first = new ReactiveTestExtension(manager) { Value = InitialValue };
                var initialValue = first.ProvideValue(new TestServiceProvider(target, TextBlock.TextProperty));
                first.Value = UpdatedValue;
                var second = new ReactiveTestExtension(manager) { Value = "Second" };
                _ = second.ProvideValue(new EmptyServiceProvider());
                manager.UpdateAllTargets();
                manager.CleanupInactiveExtensions();
                var initialExtensionCount = manager.ActiveExtensions.Count;
                var clrTarget = new MutableTarget();
                var clrExtension = new ReactiveTestExtension(manager) { Value = "CLR" };
                _ = clrExtension.ProvideValue(new TestServiceProvider(clrTarget, typeof(MutableTarget).GetProperty(nameof(MutableTarget.Value))!));
                clrExtension.UpdateTargets();
                _ = clrExtension.ExposedTargetPropertyType;
                var nullManagerRejected = false;
                try
                {
                    _ = new ReactiveTestExtension(null!);
                }
                catch (ArgumentNullException)
                {
                    nullManagerRejected = true;
                }

                return new ReactiveManagerResult(
                    initialValue,
                    target.Text,
                    first.IsTarget(target),
                    first.ExposedTargetPropertyType,
                    initialExtensionCount,
                    clrTarget.Value,
                    nullManagerRejected);
            });

        await Assert.That(result.InitialValue).IsEqualTo(InitialValue);
        await Assert.That(result.UpdatedValue).IsEqualTo(UpdatedValue);
        await Assert.That(result.IsTarget).IsTrue();
        await Assert.That(result.TargetPropertyType).IsEqualTo(typeof(string));
        await Assert.That(result.ActiveExtensionCount).IsEqualTo(ExpectedEnumValueCount);
        await Assert.That(result.ClrValue).IsEqualTo("CLR");
        await Assert.That(result.NullManagerRejected).IsTrue();
    }

    [Test]
    internal async Task ResxBindingPropertyWrappersRoundTripForBothVariants()
    {
        var result = RunOnSta(
            static () => ValidateLeanBindingProperties() && ValidateReactiveBindingProperties());

        await Assert.That(result).IsTrue();
    }

    /// <summary>Verifies template property types and removal of expired target references.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task ManagedExtensionsCleanUpExpiredAndUnknownTargets()
    {
        var result = RunOnSta(
            static () =>
            {
                var leanManager = new Lean.MarkupExtensionManager(0);
                var reactiveManager = new Reactive.MarkupExtensionManager(0);
                var lean = new LeanTestExtension(leanManager);
                var reactive = new ReactiveTestExtension(reactiveManager);
                var emptyLeanType = lean.ExposedTargetPropertyType;
                var emptyReactiveType = reactive.ExposedTargetPropertyType;
                var target = new MutableTarget();
                var unknownProperty = new object();
                _ = lean.ProvideValue(new TestServiceProvider(target, unknownProperty));
                _ = reactive.ProvideValue(new TestServiceProvider(target, unknownProperty));
                var unknownLeanType = lean.ExposedTargetPropertyType;
                var unknownReactiveType = reactive.ExposedTargetPropertyType;
                lean.UpdateTargets();
                reactive.UpdateTargets();
                lean.DeactivateTargets();
                reactive.DeactivateTargets();
                lean.UpdateTargets();
                reactive.UpdateTargets();
                leanManager.CleanupInactiveExtensions();
                reactiveManager.CleanupInactiveExtensions();
                var leanAlive = lean.IsTargetAlive;
                var reactiveAlive = reactive.IsTargetAlive;
                const BindingFlags debuggerFlags = BindingFlags.NonPublic | BindingFlags.Instance;
                _ = typeof(Lean.MarkupExtensionManager).GetProperty(DebuggerDisplayProperty, debuggerFlags)!.GetValue(leanManager);
                _ = typeof(Reactive.MarkupExtensionManager).GetProperty(DebuggerDisplayProperty, debuggerFlags)!.GetValue(reactiveManager);
                _ = typeof(Lean.UICultureExtension).GetProperty(DebuggerDisplayProperty, debuggerFlags)!.GetValue(new Lean.UICultureExtension());
                _ = typeof(Reactive.UICultureExtension).GetProperty(DebuggerDisplayProperty, debuggerFlags)!.GetValue(new Reactive.UICultureExtension());
                return (emptyLeanType, emptyReactiveType, unknownLeanType, unknownReactiveType, leanAlive, reactiveAlive,
                    leanManager.ActiveExtensions.Count, reactiveManager.ActiveExtensions.Count);
            });

        await Assert.That(result.emptyLeanType).IsNull();
        await Assert.That(result.emptyReactiveType).IsNull();
        await Assert.That(result.unknownLeanType).IsEqualTo(typeof(object));
        await Assert.That(result.unknownReactiveType).IsEqualTo(typeof(object));
        await Assert.That(result.leanAlive).IsFalse();
        await Assert.That(result.reactiveAlive).IsFalse();
        await Assert.That(result.Item7).IsEqualTo(0);
        await Assert.That(result.Item8).IsEqualTo(0);
    }

    private static bool CultureMatches(CultureInfo? culture, string name) =>
        culture?.Name.Equals(name, StringComparison.OrdinalIgnoreCase) == true;

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

    private static T RunOnSta<T>(Func<T> action)
    {
        T? result = default;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = action();
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
            });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result!;
    }

    private static bool ValidateLeanBindingProperties()
    {
        var extension = new Lean.ResxExtension();
        var asyncState = new object();
        var converter = new PassThroughConverter();
        var converterParameter = new object();
        var fallbackValue = new object();
        var source = new MutableTarget { Value = "Source" };
        var targetNullValue = new object();
        extension.BindingAsyncState = asyncState;
        extension.BindingConverter = converter;
        extension.BindingConverterCulture = CultureInfo.InvariantCulture;
        extension.BindingConverterParameter = converterParameter;
        extension.BindingElementName = BindingElementName;
        _ = extension.BindingElementName;
        extension.BindingFallbackValue = fallbackValue;
        extension.BindingGroupName = BindingGroupName;
        extension.BindingIsAsync = true;
        extension.BindingMode = BindingMode.OneWay;
        extension.BindingNotifyOnSourceUpdated = true;
        extension.BindingNotifyOnTargetUpdated = true;
        extension.BindingNotifyOnValidationError = true;
        extension.BindingPath = new(nameof(MutableTarget.Value));
        Lean.ResxExtension relativeExtension = new();
        relativeExtension.BindingRelativeSource = new(RelativeSourceMode.Self);
        _ = relativeExtension.BindingRelativeSource;
        Lean.ResxExtension sourceExtension = new();
        sourceExtension.BindingSource = source;
        _ = sourceExtension.BindingSource;
        extension.BindingTargetNullValue = targetNullValue;
        extension.BindingUpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
        extension.BindingValidatesOnDataErrors = true;
        extension.BindingValidatesOnExceptions = true;
        extension.BindingXPath = BindingXPath;
        extension.BindsDirectlyToSource = true;
        extension.Children.Add(new("Child"));

        _ = extension.BindingAsyncState;
        _ = extension.BindingConverter;
        _ = extension.BindingConverterCulture;
        _ = extension.BindingConverterParameter;
        _ = extension.BindingFallbackValue;
        _ = extension.BindingGroupName;
        _ = extension.BindingIsAsync;
        _ = extension.BindingMode;
        _ = extension.BindingNotifyOnSourceUpdated;
        _ = extension.BindingNotifyOnTargetUpdated;
        _ = extension.BindingNotifyOnValidationError;
        _ = extension.BindingPath;
        _ = extension.BindingTargetNullValue;
        _ = extension.BindingUpdateSourceTrigger;
        _ = extension.BindingValidatesOnDataErrors;
        _ = extension.BindingValidatesOnExceptions;
        _ = extension.BindingValidationRules;
        _ = extension.BindingXPath;
        _ = extension.BindsDirectlyToSource;
        _ = extension.Children;
        return true;
    }

    private static bool ValidateReactiveBindingProperties()
    {
        var extension = new Reactive.ResxExtension();
        var asyncState = new object();
        var converter = new PassThroughConverter();
        var converterParameter = new object();
        var fallbackValue = new object();
        var source = new MutableTarget { Value = "Source" };
        var targetNullValue = new object();
        extension.BindingAsyncState = asyncState;
        extension.BindingConverter = converter;
        extension.BindingConverterCulture = CultureInfo.InvariantCulture;
        extension.BindingConverterParameter = converterParameter;
        extension.BindingElementName = BindingElementName;
        _ = extension.BindingElementName;
        extension.BindingFallbackValue = fallbackValue;
        extension.BindingGroupName = BindingGroupName;
        extension.BindingIsAsync = true;
        extension.BindingMode = BindingMode.OneWay;
        extension.BindingNotifyOnSourceUpdated = true;
        extension.BindingNotifyOnTargetUpdated = true;
        extension.BindingNotifyOnValidationError = true;
        extension.BindingPath = new(nameof(MutableTarget.Value));
        Reactive.ResxExtension relativeExtension = new();
        relativeExtension.BindingRelativeSource = new(RelativeSourceMode.Self);
        _ = relativeExtension.BindingRelativeSource;
        Reactive.ResxExtension sourceExtension = new();
        sourceExtension.BindingSource = source;
        _ = sourceExtension.BindingSource;
        extension.BindingTargetNullValue = targetNullValue;
        extension.BindingUpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
        extension.BindingValidatesOnDataErrors = true;
        extension.BindingValidatesOnExceptions = true;
        extension.BindingXPath = BindingXPath;
        extension.BindsDirectlyToSource = true;
        extension.Children.Add(new("Child"));

        _ = extension.BindingAsyncState;
        _ = extension.BindingConverter;
        _ = extension.BindingConverterCulture;
        _ = extension.BindingConverterParameter;
        _ = extension.BindingFallbackValue;
        _ = extension.BindingGroupName;
        _ = extension.BindingIsAsync;
        _ = extension.BindingMode;
        _ = extension.BindingNotifyOnSourceUpdated;
        _ = extension.BindingNotifyOnTargetUpdated;
        _ = extension.BindingNotifyOnValidationError;
        _ = extension.BindingPath;
        _ = extension.BindingTargetNullValue;
        _ = extension.BindingUpdateSourceTrigger;
        _ = extension.BindingValidatesOnDataErrors;
        _ = extension.BindingValidatesOnExceptions;
        _ = extension.BindingValidationRules;
        _ = extension.BindingXPath;
        _ = extension.BindsDirectlyToSource;
        _ = extension.Children;
        return true;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class LeanTestExtension(Lean.MarkupExtensionManager manager) : Lean.ManagedMarkupExtension(manager)
    {
        public Type? ExposedTargetPropertyType => TargetPropertyType;

        public object Value { get; set; } = string.Empty;

        internal void DeactivateTargets()
        {
            foreach (var reference in TargetObjects)
            {
                reference.Target = null;
            }
        }

        protected override object GetValue() => Value;
    }

    private sealed class MutableTarget
    {
        public string? Value { get; set; }
    }

    private sealed class PassThroughConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value;
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

    private sealed class ReactiveTestExtension(Reactive.MarkupExtensionManager manager) : Reactive.ManagedMarkupExtension(manager)
    {
        public Type? ExposedTargetPropertyType => TargetPropertyType;

        public object Value { get; set; } = string.Empty;

        internal void DeactivateTargets()
        {
            foreach (var reference in TargetObjects)
            {
                reference.Target = null;
            }
        }

        protected override object GetValue() => Value;
    }

    private sealed class TestServiceProvider(object target, object property) : IServiceProvider, IProvideValueTarget
    {
        public object TargetObject { get; set; } = target;

        public object TargetProperty { get; set; } = property;

        public object? GetService(Type serviceType) => serviceType == typeof(IProvideValueTarget) ? this : null;
    }

    private sealed record ManagedExtensionResult(
        object InitialValue,
        string DependencyValue,
        bool IsDependencyTarget,
        Type? DependencyPropertyType,
        string? ClrValue,
        Type? ClrPropertyType,
        bool TemplateReturnsExtension,
        bool IsTemplateTargetAlive,
        bool NullManagerRejected);

    private sealed record ResxExtensionResult(
        string? LeanDefaultResxName,
        string? ReactiveDefaultResxName,
        object LeanValue,
        object ReactiveValue,
        bool LeanLanguageCreated,
        bool ReactiveLanguageCreated);

    private sealed record ReactiveManagerResult(
        object InitialValue,
        string UpdatedValue,
        bool IsTarget,
        Type? TargetPropertyType,
        int ActiveExtensionCount,
        string? ClrValue,
        bool NullManagerRejected);
}
