// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using CP.Localisation;
using Lean = CP.Localisation.Avalonia;
using Reactive = CP.Localisation.Avalonia.Reactive;

namespace Localisation.AotSmoke;

/// <summary>Exercises generated localization and both Avalonia runtimes after publishing.</summary>
internal static class Program
{
    private const string ResourceName = "Localisation.AotSmoke.SmokeResources";

    private const string GreetingKey = "Greeting";

    private const string FrenchGreeting = "Bonjour";

    private const string NeutralGreeting = "Hello";

    private const string GermanCulture = "de-DE";

    private const string RefreshValue = "Refreshed";

    private const int EnumCount = 2;

    private static void Main()
    {
        var culture = CultureInfo.GetCultureInfo("fr-CA");
        Check(LocalisationResourceRegistry.TryGetResource(ResourceName, GreetingKey, culture, out var greeting), true);
        Check(greeting, FrenchGreeting);
        Check(LocalisationResourceRegistry.TryGetResource(ResourceName, "Regional", culture, out var regional), true);
        Check(regional, "Allô");
        Check(LocalisationResourceRegistry.TryGetResource(ResourceName, GreetingKey, CultureInfo.GetCultureInfo(GermanCulture), out var neutral), true);
        Check(neutral, NeutralGreeting);
        Check(LocalisationResourceRegistry.TryGetResource(ResourceName, "Missing", culture, out var missing), false);
        Check(missing, null);
        VerifyGeneratedOnlyResources(culture);
        VerifyReusableFeatures(culture);

        Lean.CultureManager.SynchronizeThreadCulture = false;
        Reactive.CultureManager.SynchronizeThreadCulture = false;
        Lean.CultureManager.UICulture = culture;
        Reactive.CultureManager.UICulture = culture;
        var leanTarget = new TextBlock();
        var reactiveTarget = new TextBlock();
        var leanExtension = new Lean.GeneratedResxExtension(GreetingKey) { ResxName = ResourceName };
        var reactiveExtension = new Reactive.GeneratedResxExtension(GreetingKey) { ResxName = ResourceName };
        using var leanBinding = leanTarget.Bind(TextBlock.TextProperty, (BindingBase)leanExtension.ProvideValue(new TargetProvider(leanTarget)));
        using var reactiveBinding = reactiveTarget.Bind(
            TextBlock.TextProperty,
            (BindingBase)reactiveExtension.ProvideValue(new TargetProvider(reactiveTarget)));
        Check(leanTarget.Text, FrenchGreeting);
        Check(reactiveTarget.Text, FrenchGreeting);
        Lean.CultureManager.UICulture = CultureInfo.GetCultureInfo(GermanCulture);
        Reactive.CultureManager.UICulture = CultureInfo.GetCultureInfo(GermanCulture);
        Check(leanTarget.Text, NeutralGreeting);
        Check(reactiveTarget.Text, NeutralGreeting);

        EventHandler<Lean.GetResourceEventArgs> leanOverride = (_, args) => args.Resource = RefreshValue;
        EventHandler<Reactive.GetResourceEventArgs> reactiveOverride = (_, args) => args.Resource = RefreshValue;
        Lean.GeneratedResxExtension.GetResource += leanOverride;
        Reactive.GeneratedResxExtension.GetResource += reactiveOverride;
        try
        {
            Lean.GeneratedResxExtension.UpdateTarget("Unrelated");
            Reactive.GeneratedResxExtension.UpdateTarget("Unrelated");
            Check(leanTarget.Text, NeutralGreeting);
            Check(reactiveTarget.Text, NeutralGreeting);
            Lean.GeneratedResxExtension.UpdateTarget(GreetingKey);
            Reactive.GeneratedResxExtension.UpdateTarget(GreetingKey);
            Check(leanTarget.Text, RefreshValue);
            Check(reactiveTarget.Text, RefreshValue);
        }
        finally
        {
            Lean.GeneratedResxExtension.GetResource -= leanOverride;
            Reactive.GeneratedResxExtension.GetResource -= reactiveOverride;
        }

        var leanEnum = new Lean.ResourceEnumConverter<SmokeValue>(ResourceName);
        var reactiveEnum = new Reactive.ResourceEnumConverter<SmokeValue>(ResourceName);
        Check(leanEnum.ConvertToString(SmokeValue.First, culture), "First localized");
        Check(reactiveEnum.ConvertFromString("Second localized", culture), SmokeValue.Second);
        Check(leanEnum.GetValues(culture).Count, EnumCount);
        Check(reactiveEnum.ConvertFromString("Missing", culture), null);
    }

    private static void Check(object? actual, object? expected)
    {
        if (Equals(actual, expected))
        {
            return;
        }

        throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }

    private static void VerifyGeneratedOnlyResources(CultureInfo culture)
    {
        const string generatedOnlyName = "Localisation.AotSmoke.GeneratedOnlyResources";
        Check(LocalisationResourceRegistry.TryGetResource(generatedOnlyName, "CaseKey", culture, out var upperCase), true);
        Check(upperCase, "Upper case key");
        Check(LocalisationResourceRegistry.TryGetResource(generatedOnlyName, "casekey", culture, out var lowerCase), true);
        Check(lowerCase, "Lower case key");
    }

    private static void VerifyReusableFeatures(CultureInfo culture)
    {
        using var context = new LocalisationContext("Localisation.AotSmoke", ResourceName);
        var numericCulture = CultureInfo.CurrentCulture;
        var ambientCulture = CultureInfo.CurrentUICulture;
        var message = LocalisationMessage.Concat(LocalisationMessage.Get(context, GreetingKey), LocalisationMessage.Literal("!"));
        context.Initialize(GreetingKey);
        var labels = context.CreateChoices(GreetingKey);
        var leanItems = Lean.LocalisationChoices.CreateItems(context, labels.ResourceKeys);
        var reactiveItems = Reactive.LocalisationChoices.CreateItems(context, labels.ResourceKeys);
        var leanLabel = new TextBlock();
        var reactiveLabel = new TextBlock();
        using var leanBinding = leanLabel.Bind(
            TextBlock.TextProperty,
            Lean.LocalisationBinding.Create(context, context, static (state, value) => state.Get(GreetingKey, value)));
        using var reactiveBinding = reactiveLabel.Bind(
            TextBlock.TextProperty,
            Reactive.LocalisationBinding.Create(context, context, static (state, value) => state.Get(GreetingKey, value)));
        context.SetCulture(culture);
        Check(message.Value, $"{FrenchGreeting}!");
        Check(labels[0], FrenchGreeting);
        Check(leanItems[0].ToString(), FrenchGreeting);
        Check(reactiveItems[0].ToString(), FrenchGreeting);
        Check(leanLabel.Text, FrenchGreeting);
        Check(reactiveLabel.Text, FrenchGreeting);
        context.SetCulture(CultureInfo.GetCultureInfo(GermanCulture));
        context.RefreshChoices();
        Check(message.Value, $"{NeutralGreeting}!");
        Check(leanItems[0].ToString(), NeutralGreeting);
        Check(reactiveItems[0].ToString(), NeutralGreeting);
        Check(leanLabel.Text, NeutralGreeting);
        Check(reactiveLabel.Text, NeutralGreeting);
        Check(CultureInfo.CurrentCulture, numericCulture);
        Check(CultureInfo.CurrentUICulture, ambientCulture);
        var node = new RefreshNode();
        LocalisationRefresh.Refresh(new object[] { node, node });
        Check(node.Count, 1);
        VerifyCulturePolicy(context, culture);
    }

    private static void VerifyCulturePolicy(LocalisationContext context, CultureInfo culture)
    {
        var previousUi = CultureInfo.CurrentUICulture;
        var previousDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        var previousNumeric = CultureInfo.CurrentCulture;
        try
        {
            Check(InterfaceCulture.Resolve(["--language=fr-CA"], null, previousUi), culture);
            InterfaceCulture.Apply(context, culture);
            Check(CultureInfo.CurrentUICulture, culture);
            Check(CultureInfo.CurrentCulture, previousNumeric);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUi;
        }
    }

    private sealed class RefreshNode : ILocalisationChildren
    {
        public int Count { get; private set; }

        public void Refresh(HashSet<object> visited)
        {
            Count++;
            LocalisationRefresh.RefreshCore(this, visited);
        }
    }

    private sealed class TargetProvider(TextBlock target) : IServiceProvider, IProvideValueTarget
    {
        public object TargetObject => target;

        public object TargetProperty => TextBlock.TextProperty;

        public object? GetService(Type serviceType) => serviceType == typeof(IProvideValueTarget) ? this : null;
    }
}
