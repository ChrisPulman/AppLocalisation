// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Threading.Tasks;
using CP.Localisation;

namespace Localisation.Avalonia.Tests;

internal sealed class LocalisationStringListTests
{
    private const string AssemblyName = "StringList.Tests";

    private const string DictionaryName = "StringList.Resources";

    private const string Key = "Greeting";

    private const string English = "Hello";

    private const string French = "Bonjour";

    private const int PairCount = 2;

    [Test]
    internal async Task LabelsRefreshWithoutChangingResourceKeys()
    {
        using var context = CreateContext();
        string[] keys = [Key, "Literal"];
        var labels = new LocalisationStringList(context, keys);
        keys[0] = "Mutated";
        var identities = labels.ResourceKeys;
        var changes = new List<NotifyCollectionChangedEventArgs>();
        labels.CollectionChanged += (_, args) => changes.Add(args);
        await Assert.That(labels[0]).IsEqualTo(English);
        context.SetCulture(CultureInfo.GetCultureInfo("fr"));
        await Assert.That(labels[0]).IsEqualTo(French);
        labels.Refresh();
        labels.Refresh();
        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].Action).IsEqualTo(NotifyCollectionChangedAction.Replace);
        await Assert.That(changes[0].OldItems![0]).IsEqualTo(English);
        await Assert.That(changes[0].NewItems![0]).IsEqualTo(French);
        await Assert.That(changes[0].NewStartingIndex).IsEqualTo(0);
        await Assert.That(ReferenceEquals(identities, labels.ResourceKeys)).IsTrue();
        await Assert.That(identities[0]).IsEqualTo(Key);
        await Assert.That(labels.Count).IsEqualTo(PairCount);
        await Assert.That(() => ((IList<string>)identities).Add(Key)).Throws<NotSupportedException>();
        var generic = new List<string>(labels);
        var untyped = new List<object>();
        foreach (var value in (IEnumerable)labels)
        {
            untyped.Add(value);
        }

        await Assert.That(generic[0]).IsEqualTo(French);
        await Assert.That(untyped[1]).IsEqualTo("Literal");
    }

    [Test]
    internal async Task ReadOnlyListSupportsCollectionConsumers()
    {
        using var context = CreateContext();
        var labels = new LocalisationStringList(context, Key);
        var list = (IList)labels;
        await Assert.That(list.IsReadOnly).IsTrue();
        await Assert.That(list.IsFixedSize).IsTrue();
        await Assert.That(list.IsSynchronized).IsFalse();
        await Assert.That(ReferenceEquals(list.SyncRoot, labels)).IsTrue();
        await Assert.That(list[0]).IsEqualTo(English);
        await Assert.That(list.Contains(English)).IsTrue();
        await Assert.That(list.Contains("Missing")).IsFalse();
        await Assert.That(list.IndexOf(1)).IsEqualTo(-1);
        await Assert.That(() => list.Add(Key)).Throws<NotSupportedException>();
        await Assert.That(() => list.Clear()).Throws<NotSupportedException>();
        await Assert.That(() => list.Insert(0, Key)).Throws<NotSupportedException>();
        await Assert.That(() => list.Remove(Key)).Throws<NotSupportedException>();
        await Assert.That(() => list.RemoveAt(0)).Throws<NotSupportedException>();
        await Assert.That(() => list[0] = Key).Throws<NotSupportedException>();
        object[] destination = ["Before", "After"];
        list.CopyTo(destination, 1);
        await Assert.That(destination[0]).IsEqualTo("Before");
        await Assert.That(destination[1]).IsEqualTo(English);
        await Assert.That(() => list.CopyTo(null!, 0)).Throws<ArgumentNullException>();
        await Assert.That(() => list.CopyTo(Array.CreateInstance(typeof(string), [1, 1]), 0)).Throws<ArgumentException>();
        await Assert.That(() => list.CopyTo(Array.CreateInstance(typeof(string), [1], [1]), 0)).Throws<ArgumentException>();
        await Assert.That(() => list.CopyTo(destination, -1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => list.CopyTo(destination, destination.Length)).Throws<ArgumentException>();
        await Assert.That(() => list.CopyTo(destination, destination.Length + 1)).Throws<ArgumentException>();
    }

    [Test]
    internal async Task ConstructorValidatesAndAllowsEmptyKeys()
    {
        using var context = CreateContext();
        await Assert.That(() => new LocalisationStringList(null!, Key)).Throws<ArgumentNullException>();
        await Assert.That(() => new LocalisationStringList(context, null!)).Throws<ArgumentNullException>();
        await Assert.That(() => new LocalisationStringList(context, [null!])).Throws<ArgumentException>();
        var empty = new LocalisationStringList(context);
        empty.Refresh();
        ((IList)empty).CopyTo(Array.Empty<string>(), 0);
        await Assert.That(empty.Count).IsEqualTo(0);
        var unobserved = new LocalisationStringList(context, Key);
        context.SetCulture(CultureInfo.GetCultureInfo("fr"));
        unobserved.Refresh();
        await Assert.That(unobserved[0]).IsEqualTo(French);
    }

    private static LocalisationContext CreateContext()
    {
        LocalisationResourceRegistry.Register(AssemblyName, DictionaryName, Resolve);
        var context = new LocalisationContext(AssemblyName, DictionaryName);
        context.SetCulture(CultureInfo.GetCultureInfo("en"));
        return context;
    }

    private static object? Resolve(string key, CultureInfo culture)
    {
        if (key != Key)
        {
            return null;
        }

        return culture.TwoLetterISOLanguageName == "fr" ? French : English;
    }
}
