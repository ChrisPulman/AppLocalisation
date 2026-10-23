// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

#if REACTIVE_SHIM
namespace CP.Localisation.Avalonia.Reactive;
#else
namespace CP.Localisation.Avalonia;
#endif

/// <summary>A stable resource identity whose text-search and automation representation follows its context.</summary>
public sealed class LocalisationChoiceItem
{
    /// <summary>Initializes a new instance of the <see cref="LocalisationChoiceItem"/> class.</summary>
    /// <param name="context">The localization context that resolves this item.</param>
    /// <param name="key">The immutable resource key.</param>
    public LocalisationChoiceItem(LocalisationContext context, string key)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(key);
        Context = context;
        Key = key;
    }

    /// <summary>Gets the context that owns this item's presentation.</summary>
    public LocalisationContext Context { get; }

    /// <summary>Gets the immutable resource key.</summary>
    public string Key { get; }

    /// <inheritdoc/>
    public override string ToString() => Context.Get(Key);
}
