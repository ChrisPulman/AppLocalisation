// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using CP.Localisation;

namespace Localisation.Avalonia.Tests;

/// <summary>Verifies independent resource contexts and deferred messages.</summary>
internal sealed class LocalisationContextTests
{
    private const string FrenchCulture = "fr-FR";

    private const string EnglishCulture = "en-US";

    private const string GreetingKey = "Greeting";

    private const string FrenchGreeting = "Bonjour";

    private const string EnglishGreeting = "Hello";

    private const string UnknownKey = "Unknown";

    private const double NumericValue = 12.5;

    private const int ExpectedChangedCount = 2;

    private const int PreviousCultureIndex = 2;

    private const int LookupCount = 1000;

    private const int NonStringValue = 42;

    private const string AssemblyName = "Context.Tests";

    private const string ResourceName = "Context.Tests.Resources";

    private const string GeneratedResourceName = "Localisation.Avalonia.Tests.GeneratedResources.SmokeResources";

    /// <summary>Verifies independent cultures and preservation of ambient numeric and interface cultures.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task SetCulture_IndependentContexts_PreservesAmbientCultures()
    {
        RegisterResources();
        using var first = new LocalisationContext(AssemblyName, ResourceName);
        using var second = new LocalisationContext(AssemblyName, ResourceName);
        var numeric = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        await Assert.That(first.UICulture).IsEqualTo(ui);
        first.SetCulture(CultureInfo.GetCultureInfo(FrenchCulture));
        second.SetCulture(CultureInfo.GetCultureInfo(EnglishCulture));
        await Assert.That(first.Get(GreetingKey)).IsEqualTo(FrenchGreeting);
        await Assert.That(second.Get(GreetingKey)).IsEqualTo(EnglishGreeting);
        await Assert.That(CultureInfo.CurrentCulture).IsEqualTo(numeric);
        await Assert.That(CultureInfo.CurrentUICulture).IsEqualTo(ui);
        await Assert.That(first.Format("Number", NumericValue)).IsEqualTo(string.Format(numeric, "Valeur {0:N1}", NumericValue));
    }

    /// <summary>Verifies string fallback and generated provider parent and neutral culture behavior.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Get_ProviderFallback_UsesParentNeutralAndKey()
    {
        RegisterResources();
        using var context = new LocalisationContext(AssemblyName, ResourceName);
        await Assert.That(context.Get(UnknownKey)).IsEqualTo(UnknownKey);
        await Assert.That(context.Get("NonString")).IsEqualTo("NonString");
        using var missing = new LocalisationContext("Absent.Assembly", ResourceName);
        await Assert.That(missing.Get(GreetingKey)).IsEqualTo(GreetingKey);
        using var generated = new LocalisationContext("Localisation.Avalonia.Tests", GeneratedResourceName);
        var culture = CultureInfo.GetCultureInfo("fr-CA");
        await Assert.That(generated.Get(GreetingKey, culture)).IsEqualTo(FrenchGreeting);
        await Assert.That(generated.Get("Regional", culture)).IsEqualTo("Allô");
        await Assert.That(generated.Get("SmokeValue_First", culture)).IsEqualTo("First localized");
    }

    /// <summary>Verifies replay, unsubscribe, completion, and disposed selection behavior.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task CultureChanges_ReplayAndDispose_StopsNotificationsAndCompletes()
    {
        using var context = new LocalisationContext(AssemblyName, ResourceName);
        var observer = new RecordingObserver();
        using var subscription = context.CultureChanges.Subscribe(observer);
        await Assert.That(observer.Cultures.Count).IsEqualTo(1);
        await Assert.That(observer.Cultures[0]).IsEqualTo(context.UICulture.Name);
        context.SetCulture(CultureInfo.GetCultureInfo(FrenchCulture));
        context.SetCulture(CultureInfo.GetCultureInfo(FrenchCulture));
        await Assert.That(observer.Cultures.Count).IsEqualTo(ExpectedChangedCount);
        var unsubscribed = new RecordingObserver();
        var removed = context.CultureChanges.Subscribe(unsubscribed);
        removed.Dispose();
        context.SetCulture(CultureInfo.GetCultureInfo(EnglishCulture));
        await Assert.That(unsubscribed.Cultures.Count).IsEqualTo(1);
        context.Dispose();
        context.Dispose();
        await Assert.That(observer.Completions).IsEqualTo(1);
        await Assert.That(unsubscribed.Completions).IsEqualTo(0);
        var late = new RecordingObserver();
        using var lateSubscription = context.CultureChanges.Subscribe(late);
        await Assert.That(late.Cultures.Count).IsEqualTo(0);
        await Assert.That(late.Completions).IsEqualTo(1);
        await Assert.That(() => context.SetCulture(CultureInfo.InvariantCulture)).Throws<ObjectDisposedException>();
        await Assert.That(context.Get(UnknownKey)).IsEqualTo(UnknownKey);
    }

    /// <summary>Verifies reentrant culture selection delivers a consistent order to every observer.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task CultureChanges_ReentrantSelection_PreservesNotificationOrder()
    {
        using var context = new LocalisationContext(AssemblyName, ResourceName);
        var first = new RecordingObserver(culture =>
        {
            if (culture.Name != FrenchCulture)
            {
                return;
            }

            context.SetCulture(CultureInfo.GetCultureInfo(EnglishCulture));
        });
        var second = new RecordingObserver();
        using var firstSubscription = context.CultureChanges.Subscribe(first);
        using var secondSubscription = context.CultureChanges.Subscribe(second);
        context.SetCulture(CultureInfo.GetCultureInfo(FrenchCulture));
        await Assert.That(string.Join(",", first.Cultures)).IsEqualTo(string.Join(",", second.Cultures));
        await Assert.That(second.Cultures[^PreviousCultureIndex]).IsEqualTo(FrenchCulture);
        await Assert.That(second.Cultures[^1]).IsEqualTo(EnglishCulture);
    }

    /// <summary>Verifies nested messages resolve after culture changes and arguments are captured independently.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Message_DeferredComposition_PreservesLiteralAndNestedResources()
    {
        RegisterResources();
        using var context = new LocalisationContext(AssemblyName, ResourceName);
        context.SetCulture(CultureInfo.GetCultureInfo(EnglishCulture));
        object?[] arguments = [LocalisationMessage.Get(context, GreetingKey), LocalisationMessage.Literal(EnglishGreeting)];
        var message = LocalisationMessage.Format(context, "Pair", arguments);
        arguments[0] = "changed";
        await Assert.That(message.Value).IsEqualTo("Hello / Hello");
        context.SetCulture(CultureInfo.GetCultureInfo(FrenchCulture));
        await Assert.That(message.Value).IsEqualTo("Bonjour / Hello");
        var combined = LocalisationMessage.Concat(message, LocalisationMessage.Literal("!"));
        await Assert.That(combined.ToString()).IsEqualTo("Bonjour / Hello!");
        await Assert.That(LocalisationMessage.Literal(null).Value).IsNull();
        await Assert.That(default(LocalisationMessage).ToString()).IsEqualTo(string.Empty);
        await Assert.That(LocalisationMessage.Concat(default, default).Value).IsEqualTo(string.Empty);
    }

    /// <summary>Verifies required values reject null without treating null literal user data as a key.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task RequiredArguments_Null_ThrowsArgumentExceptions()
    {
        using var context = new LocalisationContext(AssemblyName, ResourceName);
        await Assert.That(() => new LocalisationContext(null!, ResourceName)).Throws<ArgumentException>();
        await Assert.That(() => new LocalisationContext(AssemblyName, string.Empty)).Throws<ArgumentException>();
        await Assert.That(() => context.Get(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => context.Get(GreetingKey, null!)).Throws<ArgumentNullException>();
        await Assert.That(() => context.SetCulture(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => context.CultureChanges.Subscribe(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => context.Format("Pair", null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LocalisationMessage.Get(null!, GreetingKey)).Throws<ArgumentNullException>();
        await Assert.That(() => LocalisationMessage.Get(context, null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LocalisationMessage.Format(context, "Pair", null!)).Throws<ArgumentNullException>();
    }

    /// <summary>Verifies concurrent culture selection never overlaps observer callbacks or loses updates.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task CultureChanges_ConcurrentSelection_SerializesObserverCallbacks()
    {
        using var context = new LocalisationContext(AssemblyName, ResourceName);
        var active = 0;
        var overlaps = 0;
        var count = 0;
        var observer = new RecordingObserver(culture =>
        {
            _ = culture;
            if (System.Threading.Interlocked.Increment(ref active) != 1)
            {
                _ = System.Threading.Interlocked.Increment(ref overlaps);
            }

            _ = System.Threading.Interlocked.Increment(ref count);
            _ = System.Threading.Interlocked.Decrement(ref active);
        });
        using var subscription = context.CultureChanges.Subscribe(observer);
        var cultures = new[] { EnglishCulture, FrenchCulture, "de-DE", "es-ES" };
        await Task.WhenAll(Array.ConvertAll(cultures, name => Task.Run(() => context.SetCulture(CultureInfo.GetCultureInfo(name)))));
        await Assert.That(overlaps).IsEqualTo(0);
        await Assert.That(count).IsEqualTo(cultures.Length + 1);
        await Assert.That(observer.Cultures[^1]).IsEqualTo(context.UICulture.Name);
    }

    /// <summary>Verifies successful repeated string resolution does not allocate after provider warmup.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Get_RepeatedStringLookup_DoesNotAllocate()
    {
        RegisterResources();
        using var context = new LocalisationContext(AssemblyName, ResourceName);
        context.SetCulture(CultureInfo.GetCultureInfo(FrenchCulture));
        _ = context.Get(GreetingKey);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < LookupCount; index++)
        {
            _ = context.Get(GreetingKey);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsEqualTo(0);
    }

    /// <summary>Verifies observer failures propagate after other queued callbacks and do not strand the stream.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task CultureChanges_ObserverThrows_PropagatesAndContinuesDelivery()
    {
        using var context = new LocalisationContext(AssemblyName, ResourceName);
        var rejected = new RecordingObserver(static _ => throw new InvalidOperationException());
        await Assert.That(() => context.CultureChanges.Subscribe(rejected)).Throws<InvalidOperationException>();
        var throwing = new RecordingObserver(culture =>
        {
            if (culture.Name != FrenchCulture)
            {
                return;
            }

            throw new InvalidOperationException();
        });
        using var throwingSubscription = context.CultureChanges.Subscribe(throwing);
        var healthy = new RecordingObserver();
        using var healthySubscription = context.CultureChanges.Subscribe(healthy);
        await Assert.That(() => context.SetCulture(CultureInfo.GetCultureInfo(FrenchCulture))).Throws<InvalidOperationException>();
        await Assert.That(healthy.Cultures[^1]).IsEqualTo(FrenchCulture);
        context.SetCulture(CultureInfo.GetCultureInfo(EnglishCulture));
        await Assert.That(healthy.Cultures[^1]).IsEqualTo(EnglishCulture);
        await Assert.That(rejected.Cultures.Count).IsEqualTo(1);
    }

    /// <summary>Verifies an observer can remove a later subscriber from the same notification batch.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task CultureChanges_UnsubscribeDuringNotification_SkipsPendingSubscriber()
    {
        using var context = new LocalisationContext(AssemblyName, ResourceName);
        IDisposable? laterSubscription = null;
        var first = new RecordingObserver(culture =>
        {
            if (culture.Name != FrenchCulture)
            {
                return;
            }

            laterSubscription?.Dispose();
        });
        using var firstSubscription = context.CultureChanges.Subscribe(first);
        var later = new RecordingObserver();
        laterSubscription = context.CultureChanges.Subscribe(later);
        using (laterSubscription)
        {
            context.SetCulture(CultureInfo.GetCultureInfo(FrenchCulture));
            await Assert.That(later.Cultures.Count).IsEqualTo(1);
        }
    }

    private static void RegisterResources() => LocalisationResourceRegistry.Register(AssemblyName, ResourceName, static (key, culture) => key switch
    {
        GreetingKey => culture.TwoLetterISOLanguageName == "fr" ? FrenchGreeting : EnglishGreeting,
        "Number" => culture.TwoLetterISOLanguageName == "fr" ? "Valeur {0:N1}" : "Value {0:N1}",
        "Pair" => "{0} / {1}",
        "NonString" => NonStringValue,
        _ => null,
    });

    private sealed class RecordingObserver(Action<CultureInfo>? next = null) : IObserver<CultureInfo>
    {
        public List<string> Cultures { get; } = [];

        public int Completions { get; private set; }

        public void OnNext(CultureInfo value)
        {
            Cultures.Add(value.Name);
            next?.Invoke(value);
        }

        public void OnCompleted() => Completions++;

        public void OnError(Exception error) => throw error;
    }
}
