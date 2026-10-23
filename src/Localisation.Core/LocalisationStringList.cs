// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;

namespace CP.Localisation;

/// <summary>A fixed list of resource keys whose string labels follow a localisation context.</summary>
/// <remarks>
/// Call <see cref="Refresh"/> after changing the context to notify string-list observers.
/// Selection controls should bind stable resource-key items rather than these replaceable string labels.
/// The list does not subscribe to the context or extend the lifetime of its consumers.
/// </remarks>
public sealed class LocalisationStringList : IReadOnlyList<string>, IList, INotifyCollectionChanged
{
    private readonly LocalisationContext _context;

    private CultureInfo _culture;

    /// <summary>Initializes a new instance of the <see cref="LocalisationStringList"/> class.</summary>
    /// <param name="context">The resource identity and interface culture.</param>
    /// <param name="keys">The resource keys, copied in display order.</param>
    public LocalisationStringList(LocalisationContext context, params string[] keys)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (keys is null)
        {
            throw new ArgumentNullException(nameof(keys));
        }

        foreach (var key in keys)
        {
            if (key is null)
            {
                throw new ArgumentException("Resource keys cannot contain null.", nameof(keys));
            }
        }

        _context = context;
        _culture = context.UICulture;
        ResourceKeys = Array.AsReadOnly((string[])keys.Clone());
    }

    /// <inheritdoc/>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <inheritdoc/>
    public int Count => ResourceKeys.Count;

    /// <summary>Gets the immutable resource identities, independent of the displayed language.</summary>
    public ReadOnlyCollection<string> ResourceKeys { get; }

    /// <inheritdoc/>
    public string this[int index] => _context.Get(ResourceKeys[index]);

    /// <inheritdoc/>
    bool IList.IsReadOnly => true;

    /// <inheritdoc/>
    bool IList.IsFixedSize => true;

    /// <inheritdoc/>
    bool ICollection.IsSynchronized => false;

    /// <inheritdoc/>
    object ICollection.SyncRoot => this;

    /// <inheritdoc/>
    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    /// <summary>Gets stable resource keys for a localized list, or preserves any other label list.</summary>
    /// <param name="labels">The displayed labels.</param>
    /// <returns>The immutable resource identities or the original list.</returns>
    public static IReadOnlyList<string> GetResourceKeys(IReadOnlyList<string> labels)
    {
        if (labels is null)
        {
            throw new ArgumentNullException(nameof(labels));
        }

        return labels is LocalisationStringList choices ? choices.ResourceKeys : labels;
    }

    /// <summary>Publishes replacements for labels changed since the previous refresh.</summary>
    /// <remarks>Call on the thread that owns collection observers, normally the UI thread.</remarks>
    public void Refresh()
    {
        var previous = _culture;
        var current = _context.UICulture;
        _culture = current;
        for (var index = 0; index < Count; index++)
        {
            var before = _context.Get(ResourceKeys[index], previous);
            var after = _context.Get(ResourceKeys[index], current);
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, after, before, index));
            }
        }
    }

    /// <inheritdoc/>
    public IEnumerator<string> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    int IList.Add(object? value) => throw new NotSupportedException();

    /// <inheritdoc/>
    void IList.Clear() => throw new NotSupportedException();

    /// <inheritdoc/>
    bool IList.Contains(object? value) => ((IList)this).IndexOf(value) >= 0;

    /// <inheritdoc/>
    int IList.IndexOf(object? value)
    {
        if (value is string text)
        {
            for (var index = 0; index < Count; index++)
            {
                if (string.Equals(this[index], text, StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }

        return -1;
    }

    /// <inheritdoc/>
    void IList.Insert(int index, object? value) => throw new NotSupportedException();

    /// <inheritdoc/>
    void IList.Remove(object? value) => throw new NotSupportedException();

    /// <inheritdoc/>
    void IList.RemoveAt(int index) => throw new NotSupportedException();

    /// <inheritdoc/>
    void ICollection.CopyTo(Array array, int index)
    {
        if (array is null)
        {
            throw new ArgumentNullException(nameof(array));
        }

        if (array.Rank != 1 || array.GetLowerBound(0) != 0)
        {
            throw new ArgumentException("The destination must be a one-dimensional zero-based array.", nameof(array));
        }

        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (index > array.Length || Count > array.Length - index)
        {
            throw new ArgumentException("The destination does not have enough space.", nameof(array));
        }

        for (var item = 0; item < Count; item++)
        {
            array.SetValue(this[item], index + item);
        }
    }
}
