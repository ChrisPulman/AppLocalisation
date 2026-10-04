// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Resources;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using Lean = CP.Localisation;
using Reactive = CP.Localisation.Reactive;

namespace Localisation.WPF.Tests;

internal sealed class WpfResxCoverageTests
{
    private const string GreetingKey = "Greeting";

    private const string MissingKey = "Missing";

    private const string SourceName = "source";

    private const string CreateBindingMethod = "CreateBinding";

    private const string ConvertValueMethod = "ConvertValue";

    private const string HasEmbeddedResxMethod = "HasEmbeddedResx";

    private const string ResolveMethod = "OnAssemblyResolve";

    private const int ImageWidth = 2;

    private const int ImageHeight = 3;

    private const double NumericFallback = 42D;

    private const double NumericResource = 17D;

    private const string UpdateTargetMethod = "UpdateTarget";

    private const string FrenchCulture = "fr-FR";

    private const string GreetingValue = "Hello";

    private const string ResourceSuffix = ".resources";

    private const string ResourceName = "Localisation.WPF.Tests.TestResources";

    /// <summary>Verifies resource lookup, fallback conversion, and target registration.</summary>
    /// <param name="reactive">Whether the reactive variant is tested.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    internal async Task Resources_ResolveAndConvertFallbacks(bool reactive)
    {
        var values = OnSta(() => ExerciseResources(reactive));
        await Assert.That(values[0]).IsEqualTo(GreetingValue);
        await Assert.That(values[1]).IsEqualTo(GreetingValue);
        await Assert.That(values[2]).IsEqualTo("#Missing");
        await Assert.That(values[3]).IsEqualTo(NumericFallback);
        await Assert.That(values[4]).IsEqualTo("invalid-number");
        await Assert.That(values[5]).IsEqualTo("opaque");
        await Assert.That(values[6]).IsEqualTo("#Missing");
        await Assert.That(values[7]).IsNull();
        await Assert.That((bool)values[8]!).IsTrue();
        await Assert.That((bool)values[9]!).IsTrue();
        await Assert.That(values[10]).IsEqualTo(ResourceName);
        await Assert.That(values[11]).IsNotNull();
    }

    /// <summary>Verifies binding copies preserve sources, rules, and child formatting.</summary>
    /// <param name="reactive">Whether the reactive variant is tested.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    internal async Task Bindings_CopyPropertiesAndCreateMultiBindings(bool reactive)
    {
        var values = OnSta(() => ExerciseBindings(reactive));
        await Assert.That((bool)values[0]!).IsTrue();
        await Assert.That(values[1]).IsEqualTo(1);
        await Assert.That(values[2]).IsEqualTo("Value: {0}");
        await Assert.That(values[3]).IsEqualTo(RelativeSourceMode.Self);
        await Assert.That(values[4]).IsEqualTo(SourceName);
        await Assert.That(values[5]).IsNull();
        await Assert.That(values[6]).IsEqualTo(ImageWidth);
        await Assert.That(values[7]).IsEqualTo(ResourceName);
        await Assert.That(values[8]).IsEqualTo("{0} / {1}");
        await Assert.That((bool)values[9]!).IsTrue();
        await Assert.That((bool)values[10]!).IsTrue();
    }

    /// <summary>Verifies string and native image resources convert into WPF property values.</summary>
    /// <param name="reactive">Whether the reactive variant is tested.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    internal async Task Conversion_ProducesTypedValuesAndImages(bool reactive)
    {
        var values = OnSta(() =>
        {
            var extension = New(ExtensionType(reactive), MissingKey);
            _ = Provide(extension, new Border(), FrameworkElement.WidthProperty);
            var typed = Invoke(extension, ConvertValueMethod, "17");
            using var bitmap = new Bitmap(ImageWidth, ImageHeight);
            var bitmapSource = (BitmapSource)Invoke(extension, ConvertValueMethod, bitmap)!;
            using var icon = (Icon)SystemIcons.Information.Clone();
            var iconSource = (BitmapSource)Invoke(extension, ConvertValueMethod, icon)!;
            var content = New(ExtensionType(reactive), MissingKey);
            _ = Provide(content, new ContentControl(), ContentControl.ContentProperty);
            var image = (System.Windows.Controls.Image)Invoke(content, ConvertValueMethod, bitmap)!;
            return (object?[])
            [
                typed, bitmapSource.PixelWidth, bitmapSource.PixelHeight, bitmapSource.IsFrozen, iconSource.PixelWidth > 0, image.Width, image.Height
            ];
        });
        await Assert.That(values[0]).IsEqualTo(NumericResource);
        await Assert.That(values[1]).IsEqualTo(ImageWidth);
        await Assert.That(values[2]).IsEqualTo(ImageHeight);
        await Assert.That((bool)values[3]!).IsTrue();
        await Assert.That((bool)values[4]!).IsTrue();
        await Assert.That(values[5]).IsEqualTo((double)ImageWidth);
        await Assert.That(values[6]).IsEqualTo((double)ImageHeight);
    }

    /// <summary>Verifies resource interception failures fall back safely.</summary>
    /// <param name="reactive">Whether the reactive variant is tested.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    internal async Task ResourceInterception_HandlesExpectedResourceFailures(bool reactive)
    {
        var values = OnSta(() =>
        {
            var type = ExtensionType(reactive);
            Exception[] failures =
            [
                new MissingManifestResourceException(),
                new MissingSatelliteAssemblyException(),
                new InvalidOperationException(),
                new NotSupportedException(),
                new ArgumentException()
            ];
            var results = new List<object?>();
            foreach (var failure in failures)
            {
                EventHandler<Lean.GetResourceEventArgs> leanHandler = (_, _) => throw failure;
                EventHandler<Reactive.GetResourceEventArgs> reactiveHandler = (_, _) => throw failure;
                if (reactive)
                {
                    Reactive.ResxExtension.GetResource += reactiveHandler;
                }
                else
                {
                    Lean.ResxExtension.GetResource += leanHandler;
                }

                try
                {
                    results.Add(Provide(New(type, GreetingKey, ResourceName), new TextBlock(), TextBlock.TextProperty));
                }
                finally
                {
                    if (reactive)
                    {
                        Reactive.ResxExtension.GetResource -= reactiveHandler;
                    }
                    else
                    {
                        Lean.ResxExtension.GetResource -= leanHandler;
                    }
                }
            }

            return results;
        });
        foreach (var value in values)
        {
            await Assert.That(value).IsEqualTo("#Greeting");
        }
    }

    /// <summary>Verifies designer changes replace inherited resource names on registered targets.</summary>
    /// <param name="reactive">Whether the reactive variant is tested.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    internal async Task AttachedResourceName_UpdatesDesignTargets(bool reactive)
    {
        var value = OnSta(() =>
        {
            var type = ExtensionType(reactive);
            var target = new TextBlock();
            var extension = New(type, GreetingKey);
            _ = Provide(extension, target, TextBlock.TextProperty);
            DesignerProperties.SetIsInDesignMode(target, true);
            _ = type.GetMethod("SetDefaultResxName")!.Invoke(null, [target, ResourceName]);
            return target.Text;
        });
        await Assert.That(value).IsEqualTo(GreetingValue);
    }

    /// <summary>Verifies helper guards reject invalid cultures and unsupported assemblies.</summary>
    /// <param name="reactive">Whether the reactive variant is tested.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    internal async Task Helpers_ValidateCulturesAndAssemblyIdentities(bool reactive)
    {
        var type = ExtensionType(reactive);
        var culture = (CultureInfo)InvokeStatic(type, "GetCulture", FrenchCulture)!;
        await Assert.That(culture.Name).IsEqualTo(FrenchCulture);
        await Assert.That(InvokeStatic(type, "GetCulture", "not valid !")).IsNull();
        await Assert.That((bool)InvokeStatic(type, HasEmbeddedResxMethod, typeof(WpfResxCoverageTests).Assembly, ResourceName)!).IsTrue();
        var dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(new("DynamicResources"), AssemblyBuilderAccess.Run);
        await Assert.That((bool)InvokeStatic(type, HasEmbeddedResxMethod, dynamicAssembly, ResourceName)!).IsFalse();
        var loadedSatellite = AssemblyBuilder.DefineDynamicAssembly(new("Loaded.resources"), AssemblyBuilderAccess.Run);
        var resolvedSatellite = (Assembly)InvokeStatic(type, ResolveMethod, null, new ResolveEventArgs(loadedSatellite.FullName!))!;
        await Assert.That(resolvedSatellite.FullName).IsEqualTo(loadedSatellite.FullName);
        await Assert.That((bool)InvokeStatic(type, HasEmbeddedResxMethod, new UnsupportedAssembly(false), ResourceName)!).IsFalse();
        await Assert.That((bool)InvokeStatic(type, HasEmbeddedResxMethod, new UnsupportedAssembly(true), ResourceName)!).IsFalse();
        await Assert.That(InvokeStatic(type, ResolveMethod, null, new ResolveEventArgs("short"))).IsNull();
        await Assert.That(InvokeStatic(type, ResolveMethod, null, new ResolveEventArgs("ordinary, Version=1, Culture=en"))).IsNull();
        var ownSatelliteName = $"{type.Assembly.GetName().Name}.resources, Version=1, Culture=en";
        await Assert.That(InvokeStatic(type, ResolveMethod, null, new ResolveEventArgs(ownSatelliteName))).IsNull();
        await Assert.That(InvokeStatic(type, ResolveMethod, null, new ResolveEventArgs("unknown.resources, Version=1, malformed"))).IsNull();
        await Assert.That(InvokeStatic(type, "GetProcessFilepath", Environment.ProcessId)).IsNotNull();
        await Assert.That(Invoke(New(type, MissingKey), "GetResourceManager", (object?)null)).IsNull();
        await Assert.That(() => new FormatFailureConverter().ConvertFromInvariantString("invalid")).Throws<FormatException>();
        var embeddedName = Array.Find(type.Assembly.GetManifestResourceNames(), static name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal))!;
        var embeddedExtension = New(type, MissingKey, embeddedName[..^ResourceSuffix.Length]);
        var resourceAssembly = (Assembly)Invoke(embeddedExtension, "FindResourceAssembly")!;
        await Assert.That(resourceAssembly.GetName().Name).IsEqualTo(type.Assembly.GetName().Name);
        await Assert.That(() => OnSta(() => Provide(New(type, null), new TextBlock(), TextBlock.TextProperty)))
            .Throws<TargetInvocationException>();
    }

    /// <summary>Verifies designer paths include registry entries and live hosting processes.</summary>
    /// <param name="reactive">Whether the reactive variant is tested.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    internal async Task DesignerProbing_CollectsPathsAndSelectsSatelliteAssemblies(bool reactive)
    {
        var type = ExtensionType(reactive);
        const string collectMethod = "CollectAssemblyProbingPaths";
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Func<int, string?> filePath = _ => Path.Combine(root, "test.exe");
        Func<Process, string> hostingName = _ => "test.vshost";
        var paths = (List<string>)InvokeStatic(
            type,
            collectMethod,
            " first ; second ",
            new[] { Process.GetCurrentProcess() },
            filePath,
            hostingName)!;
        await Assert.That(paths[0]).IsEqualTo("first");
        await Assert.That(paths[1]).IsEqualTo("second");
        await Assert.That(paths[2]).IsEqualTo(root);
        Func<Process, string> ordinaryName = _ => "ordinary";
        var ignored = (List<string>)InvokeStatic(
            type,
            collectMethod,
            null,
            new[] { Process.GetCurrentProcess() },
            filePath,
            ordinaryName)!;
        await Assert.That(ignored).IsEmpty();
        Func<Process, string> inaccessibleName = _ => throw new Win32Exception();
        var inaccessible = (List<string>)InvokeStatic(
            type,
            collectMethod,
            null,
            new[] { Process.GetCurrentProcess() },
            filePath,
            inaccessibleName)!;
        await Assert.That(inaccessible).IsEmpty();
        Func<Process, string> exitedName = _ => throw new InvalidOperationException();
        var exited = (List<string>)InvokeStatic(
            type,
            collectMethod,
            null,
            new[] { Process.GetCurrentProcess() },
            filePath,
            exitedName)!;
        await Assert.That(exited).IsEmpty();
        await VerifySatelliteSelection(type, root);
    }

    private static async Task VerifySatelliteSelection(Type type, string root)
    {
        const string findCulturesMethod = "FindDesignTimeCultures";
        const string resolveAssemblyMethod = "ResolveAssembly";
        try
        {
            var cultureDirectory = Path.Combine(root, FrenchCulture);
            _ = Directory.CreateDirectory(cultureDirectory);
            _ = Directory.CreateDirectory(Path.Combine(root, "not valid !"));
            var cultures = (List<CultureInfo>)InvokeStatic(type, findCulturesMethod, (object)new[] { root })!;
            await Assert.That(cultures).Count().IsEqualTo(1);
            await Assert.That(cultures[0].Name).IsEqualTo(FrenchCulture);
            var absent = (List<CultureInfo>)InvokeStatic(type, findCulturesMethod, (object?)null)!;
            await Assert.That(absent).IsEmpty();
            var unresolved = InvokeStatic(
            type,
            resolveAssemblyMethod,
            new ResolveEventArgs("missing.resources, Version=1, Culture=fr-FR"),
            new[] { root });
            await Assert.That(unresolved).IsNull();
            var satelliteFile = Path.Combine(cultureDirectory, "test.resources.dll");
            File.Copy(typeof(Lean.ResxExtension).Assembly.Location, satelliteFile);
            var olderRoot = Path.Combine(root, "older");
            var olderDirectory = Path.Combine(olderRoot, FrenchCulture);
            _ = Directory.CreateDirectory(olderDirectory);
            var olderFile = Path.Combine(olderDirectory, "test.resources.dll");
            File.Copy(typeof(Reactive.ResxExtension).Assembly.Location, olderFile);
            File.SetLastWriteTime(satelliteFile, DateTime.Now);
            File.SetLastWriteTime(olderFile, DateTime.Now.AddDays(-1));
            var loaded = (Assembly)InvokeStatic(
            type,
            resolveAssemblyMethod,
            new ResolveEventArgs("test.resources, Version=1, Culture=fr-FR"),
            new[] { root, olderRoot })!;
            await Assert.That(loaded.GetName().Name).IsEqualTo(typeof(Lean.ResxExtension).Assembly.GetName().Name);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static object?[] ExerciseResources(bool reactive)
    {
        var type = ExtensionType(reactive);
        var target = new TextBlock();
        _ = type.GetMethod("SetDefaultResxName")!.Invoke(null, [target, ResourceName]);
        var inherited = New(type, GreetingKey);
        var greeting = Provide(inherited, target, TextBlock.TextProperty);
        var second = New(type, GreetingKey, ResourceName);
        var cached = Provide(second, new TextBlock(), TextBlock.TextProperty);
        var unknown = New(type, MissingKey, ResourceName);
        var marker = Provide(unknown, new TextBlock(), TextBlock.TextProperty);
        var numeric = New(type, MissingKey);
        Set(numeric, nameof(Lean.ResxExtension.DefaultValue), "42");
        var number = Provide(numeric, new Border(), FrameworkElement.WidthProperty);
        var invalid = New(type, MissingKey);
        Set(invalid, nameof(Lean.ResxExtension.DefaultValue), "invalid-number");
        var invalidNumber = Provide(invalid, new FormatTarget(), typeof(FormatTarget).GetProperty(nameof(FormatTarget.Value))!);
        var unsupported = New(type, MissingKey);
        Set(unsupported, nameof(Lean.ResxExtension.DefaultValue), "opaque");
        var unsupportedValue = Provide(unsupported, new OpaqueTarget(), typeof(OpaqueTarget).GetProperty(nameof(OpaqueTarget.Value))!);
        var missingManager = New(type, MissingKey, "Unknown.Resources");
        var unknownResource = Provide(missingManager, new TextBlock(), TextBlock.TextProperty);
        _ = Invoke(missingManager, "GetValue");
        var missingTyped = New(type, MissingKey);
        var nullTyped = Provide(missingTyped, new Border(), FrameworkElement.WidthProperty);
        var template = New(type, GreetingKey);
        var deferred = Provide(template, null, null);
        var child = New(type, GreetingKey, ResourceName);
        var parent = New(type, GreetingKey, ResourceName);
        var deferredChild = Provide(child, parent, type.GetProperty(nameof(Lean.ResxExtension.Children))!);
        _ = Invoke(child, UpdateTargetMethod, parent);
        var debugger = type.GetProperty("DebuggerDisplay", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(inherited);
        return (object?[])
        [
            greeting,
            cached,
            marker,
            number,
            invalidNumber,
            unsupportedValue,
            unknownResource,
            nullTyped,
            ReferenceEquals(template, deferred),
            ReferenceEquals(child, deferredChild),
            Get(inherited, nameof(Lean.ResxExtension.ResxName)),
            debugger
        ];
    }

    private static object?[] ExerciseBindings(bool reactive)
    {
        var type = ExtensionType(reactive);
        var extension = New(type, MissingKey);
        Set(extension, nameof(Lean.ResxExtension.DefaultValue), "Value: {0}");
        var binding = (Binding)Get(extension, nameof(Lean.ResxExtension.Binding))!;
        binding.Source = new TextBlock { Text = SourceName };
        binding.Path = new("Text");
        ExceptionValidationRule rule = new();
        binding.ValidationRules.Add(rule);
        var copy = (Binding)Invoke(extension, CreateBindingMethod)!;
        var target = new TextBlock();
        var expression = Provide(extension, target, TextBlock.TextProperty);
        _ = Invoke(extension, UpdateTargetMethod, target);
        _ = Invoke(extension, UpdateTargetMethod, new object());
        var relative = New(type, MissingKey);
        RelativeSource relativeSource = new(RelativeSourceMode.Self);
        Set(relative, "BindingRelativeSource", relativeSource);
        var relativeCopy = (Binding)Invoke(relative, CreateBindingMethod)!;
        var named = New(type, MissingKey);
        Set(named, "BindingElementName", SourceName);
        var namedCopy = (Binding)Invoke(named, CreateBindingMethod)!;
        var keyless = New(type, null);
        Set(keyless, "BindingSource", new());
        var keylessBinding = (Binding)Invoke(keyless, CreateBindingMethod)!;
        _ = Provide(keyless, new TextBlock(), TextBlock.TextProperty);
        var parent = New(type, MissingKey, ResourceName);
        Set(parent, nameof(Lean.ResxExtension.DefaultValue), "{0} / {1}");
        var child1 = New(type, GreetingKey);
        var child2 = New(type, MissingKey, ResourceName);
        _ = ((IList)Get(parent, nameof(Lean.ResxExtension.Children))!).Add(child1);
        _ = ((IList)Get(parent, nameof(Lean.ResxExtension.Children))!).Add(child2);
        var multi = (MultiBinding)Invoke(parent, "CreateMultiBinding")!;
        var multiTarget = new TextBlock();
        var multiExpression = Provide(parent, multiTarget, TextBlock.TextProperty);
        _ = Invoke(parent, UpdateTargetMethod, multiTarget);
        _ = Invoke(parent, UpdateTargetMethod, new object());
        return (object?[])
        [
            ReferenceEquals(copy.Source, binding.Source),
            copy.ValidationRules.Count,
            copy.StringFormat,
            relativeCopy.RelativeSource.Mode,
            namedCopy.ElementName,
            keylessBinding.StringFormat,
            multi.Bindings.Count,
            Get(child1, nameof(Lean.ResxExtension.ResxName)),
            multi.StringFormat,
            expression is BindingExpression,
            multiExpression is MultiBindingExpression
        ];
    }

    private static Type ExtensionType(bool reactive) => reactive ? typeof(Reactive.ResxExtension) : typeof(Lean.ResxExtension);

    private static object New(Type type, string? key, string? resxName = null)
    {
        var extension = Activator.CreateInstance(type)!;
        Set(extension, "Key", key);
        Set(extension, nameof(Lean.ResxExtension.ResxName), resxName);
        return extension;
    }

    private static object? Provide(object extension, object? target, object? property) =>
        extension.GetType().GetMethod("ProvideValue")!.Invoke(extension, [new TargetProvider(target, property)]);

    private static object? Get(object instance, string name) => instance.GetType().GetProperty(name)!.GetValue(instance);

    private static void Set(object instance, string name, object? value) => instance.GetType().GetProperty(name)!.SetValue(instance, value);

    private static object? Invoke(object instance, string name, params object?[] args) =>
        instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args);

    private static object? InvokeStatic(Type type, string name, params object?[] args) =>
        type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

    private static T OnSta<T>(Func<T> action)
    {
        T? result = default;
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                error = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
        return result!;
    }

    private sealed class TargetProvider(object? target, object? property) : IServiceProvider, IProvideValueTarget
    {
        public object TargetObject => target!;

        public object TargetProperty => property!;

        public object? GetService(Type serviceType) => serviceType == typeof(IProvideValueTarget) ? this : null;
    }

    private sealed class OpaqueTarget
    {
        public OpaqueTarget? Value { get; set; }
    }

    private sealed class FormatTarget
    {
        public FormatValue? Value { get; set; } = new();
    }

    [TypeConverter(typeof(FormatFailureConverter))]
    private sealed class FormatValue
    {
        public string? Text { get; set; }
    }

    private sealed class FormatFailureConverter : TypeConverter
    {
        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            throw new FormatException();
    }

    private sealed class UnsupportedAssembly(bool missing) : Assembly
    {
        public override bool IsDynamic => false;

        public override string[] GetManifestResourceNames() => missing ? throw new FileNotFoundException() : throw new NotSupportedException();
    }
}
