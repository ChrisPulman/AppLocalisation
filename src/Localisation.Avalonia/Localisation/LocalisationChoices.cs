// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

#if REACTIVE_SHIM
namespace CP.Localisation.Avalonia.Reactive;
#else
namespace CP.Localisation.Avalonia;
#endif

/// <summary>Creates localized selector items while preserving their identities across culture changes.</summary>
public static class LocalisationChoices
{
    /// <summary>Creates stable, immutable selector items from resource keys.</summary>
    /// <param name="context">The context that resolves the labels.</param>
    /// <param name="keys">The resource keys in selector order.</param>
    /// <returns>A read-only collection of stable selector items.</returns>
    public static ReadOnlyCollection<LocalisationChoiceItem> CreateItems(LocalisationContext context, IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(keys);
        var items = new LocalisationChoiceItem[keys.Count];
        for (var index = 0; index < keys.Count; index++)
        {
            items[index] = new(context, keys[index]);
        }

        return Array.AsReadOnly(items);
    }

    /// <summary>Creates a converter with a cache owned by one view binding.</summary>
    /// <param name="context">The context that resolves the labels.</param>
    /// <returns>A converter retaining the same collection while the key-list reference is unchanged.</returns>
    /// <remarks>Supply immutable key lists. A new list reference creates a new collection.</remarks>
    public static Func<IReadOnlyList<string>, ReadOnlyCollection<LocalisationChoiceItem>> CreateItemsConverter(LocalisationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new ChoiceCache(context).GetItems;
    }

    /// <summary>Creates a template whose translated labels refresh without replacing selector items.</summary>
    /// <returns>The culture-aware text template.</returns>
    public static IDataTemplate CreateTemplate() => new FuncDataTemplate<LocalisationChoiceItem>(static (item, _) => item is null ? null : new TextBlock
    {
        [!TextBlock.TextProperty] = LocalisationBinding.Create(item.Context, item, static (choice, culture) => choice.Context.Get(choice.Key, culture)),
    });

    private sealed class ChoiceCache(LocalisationContext context)
    {
        private IReadOnlyList<string>? _source;

        private ReadOnlyCollection<LocalisationChoiceItem>? _items;

        internal ReadOnlyCollection<LocalisationChoiceItem> GetItems(IReadOnlyList<string> keys)
        {
            if (_items is null || !ReferenceEquals(_source, keys))
            {
                _items = CreateItems(context, keys);
                _source = keys;
            }

            return _items;
        }
    }
}
