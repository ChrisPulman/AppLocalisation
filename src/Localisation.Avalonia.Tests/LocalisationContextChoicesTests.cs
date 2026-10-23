// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CP.Localisation;

namespace Localisation.Avalonia.Tests;

/// <summary>Verifies context-owned explicit choices and startup resource validation.</summary>
internal sealed class LocalisationContextChoicesTests
{
    private const string LabelKey = "Label";

    private const string IdenticalKey = "Identical";

    private const string AssemblyName = "Context.Choices.Tests";

    private const string ResourceName = "Choices";

    /// <summary>Verifies choice notifications remain explicit and isolated to the owning context.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task RefreshChoices_IndependentContexts_RefreshesOnlyOwnedLists()
    {
        RegisterResources();
        using var first = new LocalisationContext(AssemblyName, ResourceName);
        using var second = new LocalisationContext(AssemblyName, ResourceName);
        first.SetCulture(CultureInfo.InvariantCulture);
        second.SetCulture(CultureInfo.InvariantCulture);
        var firstList = first.CreateChoices(LabelKey);
        var secondList = second.CreateChoices(LabelKey);
        var direct = new LocalisationStringList(first, LabelKey);
        var firstChanges = 0;
        var secondChanges = 0;
        var directChanges = 0;
        firstList.CollectionChanged += (_, _) => firstChanges++;
        secondList.CollectionChanged += (_, _) => secondChanges++;
        direct.CollectionChanged += (_, _) => directChanges++;
        first.SetCulture(CultureInfo.GetCultureInfo("fr"));
        second.SetCulture(CultureInfo.GetCultureInfo("fr"));
        await Assert.That(firstChanges).IsEqualTo(0);
        first.RefreshChoices();
        await Assert.That(firstChanges).IsEqualTo(1);
        await Assert.That(secondChanges).IsEqualTo(0);
        await Assert.That(directChanges).IsEqualTo(0);
        second.RefreshChoices();
        await Assert.That(secondChanges).IsEqualTo(1);
        first.RefreshChoices();
        await Assert.That(firstChanges).IsEqualTo(1);
    }

    /// <summary>Verifies context registration does not retain abandoned lists and callbacks can create lists.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task CreateChoices_WeakOwnershipAndReentrancy_DoesNotRetainAbandonedLists()
    {
        RegisterResources();
        using var context = new LocalisationContext(AssemblyName, ResourceName);
        context.SetCulture(CultureInfo.InvariantCulture);
        var abandoned = CreateAbandoned(context);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Assert.That(abandoned.TryGetTarget(out _)).IsFalse();
        var choices = context.CreateChoices(LabelKey);
        LocalisationStringList? created = null;
        choices.CollectionChanged += (_, _) => created = context.CreateChoices(LabelKey);
        context.SetCulture(CultureInfo.GetCultureInfo("fr"));
        context.RefreshChoices();
        await Assert.That(created).IsNotNull();
        context.Dispose();
        await Assert.That(() => context.CreateChoices(LabelKey)).Throws<ObjectDisposedException>();
        context.RefreshChoices();
        GC.KeepAlive(choices);
    }

    /// <summary>Verifies startup checks distinguish absent strings from valid values equal to their keys.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Initialize_RequiredString_UsesProviderPresence()
    {
        RegisterResources();
        using var context = new LocalisationContext(AssemblyName, ResourceName);
        context.Initialize(IdenticalKey);
        await Assert.That(() => context.Initialize("Missing")).Throws<InvalidOperationException>();
        await Assert.That(() => context.Initialize("Number")).Throws<InvalidOperationException>();
        await Assert.That(() => context.Initialize(null!)).Throws<ArgumentNullException>();
        using var absent = new LocalisationContext("Absent.Choices.Tests", ResourceName);
        await Assert.That(() => absent.Initialize(IdenticalKey)).Throws<InvalidOperationException>();
    }

    /// <summary>Verifies fixed-argument formatting uses numeric culture and stable key lists preserve their identity.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Format_OverloadsAndResourceKeys_PreserveNumericCultureAndIdentity()
    {
        RegisterResources();
        using var context = new LocalisationContext(AssemblyName, ResourceName);
        context.SetCulture(CultureInfo.GetCultureInfo("fr"));
        object value = 12.5;
        await Assert.That(context.Format("One", value)).IsEqualTo(string.Format(CultureInfo.CurrentCulture, "{0:N1}", value));
        await Assert.That(context.Format("Two", value, value)).IsEqualTo(string.Format(CultureInfo.CurrentCulture, "{0:N1}/{1:N1}", value, value));
        await Assert.That(context.Format("Three", value, value, value))
            .IsEqualTo(string.Format(CultureInfo.CurrentCulture, "{0:N1}/{1:N1}/{2:N1}", value, value, value));
        var choices = context.CreateChoices(LabelKey);
        IReadOnlyList<string> literal = ["literal"];
        await Assert.That(ReferenceEquals(LocalisationStringList.GetResourceKeys(choices), choices.ResourceKeys)).IsTrue();
        await Assert.That(ReferenceEquals(LocalisationStringList.GetResourceKeys(literal), literal)).IsTrue();
        await Assert.That(() => LocalisationStringList.GetResourceKeys(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => context.CreateChoices(null!)).Throws<ArgumentNullException>();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<LocalisationStringList> CreateAbandoned(LocalisationContext context) => new(context.CreateChoices(LabelKey));

    private static void RegisterResources() => LocalisationResourceRegistry.Register(AssemblyName, ResourceName, static (key, culture) => key switch
    {
        LabelKey => culture.TwoLetterISOLanguageName == "fr" ? "Français" : "English",
        IdenticalKey => IdenticalKey,
        "Number" => 1,
        "One" => "{0:N1}",
        "Two" => "{0:N1}/{1:N1}",
        "Three" => "{0:N1}/{1:N1}/{2:N1}",
        _ => null,
    });
}
