// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using CP.Localisation;
using Lean = CP.Localisation.Avalonia;
using Reactive = CP.Localisation.Avalonia.Reactive;

namespace Localisation.Avalonia.Tests;

/// <summary>Verifies culture changes refresh labels and retain stable selection identities.</summary>
internal sealed class LocalisationChoicesTests
{
    private const string GreetingKey = "Greeting";

    private const string EnglishGreeting = "Hello";

    private const string FrenchGreeting = "Bonjour";

    private const string ResourceName = "Localisation.Avalonia.Tests.GeneratedResources.SmokeResources";

    /// <summary>Verifies a live template and typed binding refresh without replacing the selected item.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task CultureChange_PreservesSelectionAndRefreshesLabels()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        var result = await session.Dispatch(
            () =>
            {
                using var context = CreateContext();
                context.SetCulture(CultureInfo.GetCultureInfo("en"));
                var converter = Lean.LocalisationChoices.CreateItemsConverter(context);
                string[] keys = [GreetingKey];
                var items = converter(keys);
                var template = Lean.LocalisationChoices.CreateTemplate();
                var label = (TextBlock)template.Build(items[0])!;
                var selector = new ComboBox { ItemsSource = items, ItemTemplate = template, SelectedIndex = 0 };
                var direction = new TextBlock
                {
                    [!TextBlock.TextProperty] = Lean.LocalisationBinding.Create(context, "prefix:", static (prefix, culture) => prefix + culture.Name),
                };
                var initial = label.Text;
                context.SetCulture(CultureInfo.GetCultureInfo("fr-CA"));
                return new PresentationResult(
                    initial,
                    label.Text,
                    items[0].ToString(),
                    direction.Text,
                    ReferenceEquals(selector.SelectedItem, items[0]),
                    ReferenceEquals(converter(keys), items));
            },
            CancellationToken.None);
        await Assert.That(result.InitialLabel).IsEqualTo(EnglishGreeting);
        await Assert.That(result.Label).IsEqualTo(FrenchGreeting);
        await Assert.That(result.SearchText).IsEqualTo(FrenchGreeting);
        await Assert.That(result.TypedValue).IsEqualTo("prefix:fr-CA");
        await Assert.That(result.SelectionPreserved).IsTrue();
        await Assert.That(result.SourcePreserved).IsTrue();
    }

    /// <summary>Verifies the Reactive runtime also updates existing template controls and search text.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task ReactiveCultureChange_PreservesSelectionAndRefreshesLabels()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        var result = await session.Dispatch(
            () =>
            {
                using var context = CreateContext();
                context.SetCulture(CultureInfo.GetCultureInfo("en"));
                var converter = Reactive.LocalisationChoices.CreateItemsConverter(context);
                string[] keys = [GreetingKey];
                var items = converter(keys);
                var template = Reactive.LocalisationChoices.CreateTemplate();
                var label = (TextBlock)template.Build(items[0])!;
                var selector = new ComboBox { ItemsSource = items, ItemTemplate = template, SelectedIndex = 0 };
                var initial = label.Text;
                context.SetCulture(CultureInfo.GetCultureInfo("fr"));
                return new PresentationResult(
                    initial,
                    label.Text,
                    items[0].ToString(),
                    null,
                    ReferenceEquals(selector.SelectedItem, items[0]),
                    ReferenceEquals(converter(keys), items));
            },
            CancellationToken.None);
        await Assert.That(result.InitialLabel).IsEqualTo(EnglishGreeting);
        await Assert.That(result.Label).IsEqualTo(FrenchGreeting);
        await Assert.That(result.SearchText).IsEqualTo(FrenchGreeting);
        await Assert.That(result.SelectionPreserved).IsTrue();
        await Assert.That(result.SourcePreserved).IsTrue();
    }

    /// <summary>Verifies cache lifetimes and cultures remain independent and a new list creates new identities.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Converter_IsolatesBindingsContextsAndSourceChanges()
    {
        using var first = CreateContext();
        using var second = CreateContext();
        first.SetCulture(CultureInfo.GetCultureInfo("en"));
        second.SetCulture(CultureInfo.GetCultureInfo("fr"));
        string[] keys = [GreetingKey];
        var converter = Lean.LocalisationChoices.CreateItemsConverter(first);
        var original = converter(keys);
        var otherBinding = Lean.LocalisationChoices.CreateItemsConverter(first)(keys);
        var otherContext = Lean.LocalisationChoices.CreateItemsConverter(second)(keys);
        var replacement = converter([GreetingKey]);
        await Assert.That(ReferenceEquals(original, otherBinding)).IsFalse();
        await Assert.That(ReferenceEquals(original[0], otherBinding[0])).IsFalse();
        await Assert.That(ReferenceEquals(original, replacement)).IsFalse();
        await Assert.That(ReferenceEquals(original[0], replacement[0])).IsFalse();
        await Assert.That(original[0].ToString()).IsEqualTo(EnglishGreeting);
        await Assert.That(otherContext[0].ToString()).IsEqualTo(FrenchGreeting);
        await Assert.That(((ICollection<Lean.LocalisationChoiceItem>)original).IsReadOnly).IsTrue();
        await Assert.That(original[0].Key).IsEqualTo(GreetingKey);
    }

    private static LocalisationContext CreateContext() => new("Localisation.Avalonia.Tests", ResourceName);

    private sealed record PresentationResult(
        string? InitialLabel,
        string? Label,
        string SearchText,
        string? TypedValue,
        bool SelectionPreserved,
        bool SourcePreserved);
}
