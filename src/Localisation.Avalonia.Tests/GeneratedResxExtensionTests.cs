// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Threading.Tasks;
using Lean = CP.Localisation.Avalonia;
using Reactive = CP.Localisation.Avalonia.Reactive;

namespace Localisation.Avalonia.Tests;

/// <summary>Verifies Avalonia resource extensions consume statically generated providers.</summary>
internal sealed class GeneratedResxExtensionTests
{
    private const string FallbackValue = "Fallback";

    private const string OverrideValue = "Override";

    private const string GreetingKey = "Greeting";

    private const string ResourceName = "Localisation.Avalonia.Tests.GeneratedResources.SmokeResources";

    /// <summary>Verifies generated parent-culture values and missing-key defaults in both runtimes.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task ResolveValue_GeneratedResources_UsesCultureFallbackAndDefaults()
    {
        var originalLean = Lean.CultureManager.UICulture;
        var originalReactive = Reactive.CultureManager.UICulture;
        try
        {
            Lean.CultureManager.UICulture = CultureInfo.GetCultureInfo("fr-CA");
            Reactive.CultureManager.UICulture = CultureInfo.GetCultureInfo("fr-CA");
            var lean = new Lean.GeneratedResxExtension(GreetingKey) { ResxName = ResourceName };
            var reactive = new Reactive.GeneratedResxExtension(GreetingKey) { ResxName = ResourceName };
            await Assert.That(lean.ResolveValue(null, typeof(string))).IsEqualTo("Bonjour");
            await Assert.That(reactive.ResolveValue(null, typeof(string))).IsEqualTo("Bonjour");
            lean.Key = "Missing";
            reactive.Key = "Missing";
            await Assert.That(lean.ResolveValue(null, typeof(string))).IsEqualTo("#Missing");
            await Assert.That(reactive.ResolveValue(null, typeof(string))).IsEqualTo("#Missing");
            lean.DefaultValue = FallbackValue;
            reactive.DefaultValue = FallbackValue;
            await Assert.That(lean.ResolveValue(null, typeof(string))).IsEqualTo(FallbackValue);
            await Assert.That(reactive.ResolveValue(null, typeof(string))).IsEqualTo(FallbackValue);
        }
        finally
        {
            Lean.CultureManager.UICulture = originalLean;
            Reactive.CultureManager.UICulture = originalReactive;
        }
    }

    /// <summary>Verifies application overrides retain priority over generated resource providers.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task ResolveValue_ApplicationOverride_TakesPrecedenceOverGeneratedResource()
    {
        var lean = new Lean.GeneratedResxExtension(GreetingKey) { ResxName = ResourceName };
        var reactive = new Reactive.GeneratedResxExtension(GreetingKey) { ResxName = ResourceName };
        EventHandler<Lean.GetResourceEventArgs> leanHandler = (_, args) => args.Resource = OverrideValue;
        EventHandler<Reactive.GetResourceEventArgs> reactiveHandler = (_, args) => args.Resource = OverrideValue;
        Lean.GeneratedResxExtension.GetResource += leanHandler;
        Reactive.GeneratedResxExtension.GetResource += reactiveHandler;
        try
        {
            await Assert.That(lean.ResolveValue(null, typeof(string))).IsEqualTo(OverrideValue);
            await Assert.That(reactive.ResolveValue(null, typeof(string))).IsEqualTo(OverrideValue);
        }
        finally
        {
            Lean.GeneratedResxExtension.GetResource -= leanHandler;
            Reactive.GeneratedResxExtension.GetResource -= reactiveHandler;
        }
    }
}
