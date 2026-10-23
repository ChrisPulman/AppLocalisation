// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace CP.Localisation;

/// <summary>Retains resource identity or literal user text for resolution when read.</summary>
public readonly record struct LocalisationMessage
{
    private readonly string? _text;

    private readonly LocalisationContext? _context;

    private readonly object?[]? _args;

    private readonly LocalisationMessage[]? _parts;

    private LocalisationMessage(string? text, LocalisationContext? context, object?[]? args, LocalisationMessage[]? parts)
    {
        _text = text;
        _context = context;
        _args = args;
        _parts = parts;
    }

    /// <summary>Gets the current localized value, preserving null literal text.</summary>
    public string? Value
    {
        get
        {
            if (_parts is not null)
            {
                return string.Concat(_parts[0].Value, _parts[1].Value);
            }

            if (_context is null)
            {
                return _text;
            }

            return _args is null ? _context.Get(_text!) : _context.Format(_text!, _args);
        }
    }

    /// <summary>Preserves literal text without interpreting it as a resource key.</summary>
    /// <param name="text">The literal user text.</param>
    /// <returns>The literal message.</returns>
    public static LocalisationMessage Literal(string? text) => new(text, null, null, null);

    /// <summary>Retains a resource key and its context for deferred resolution.</summary>
    /// <param name="context">The resource context.</param>
    /// <param name="key">The resource key.</param>
    /// <returns>The deferred message.</returns>
    public static LocalisationMessage Get(LocalisationContext context, string key)
    {
        Validate(context, key);
        return new(key, context, null, null);
    }

    /// <summary>Retains a format key and snapshots the argument array. Nested messages resolve when read.</summary>
    /// <param name="context">The resource context.</param>
    /// <param name="key">The composite resource key.</param>
    /// <param name="args">The arguments; mutable objects retain their own formatting behavior.</param>
    /// <returns>The deferred formatted message.</returns>
    public static LocalisationMessage Format(LocalisationContext context, string key, params object?[] args)
    {
        Validate(context, key);
        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        return new(key, context, (object?[])args.Clone(), null);
    }

    /// <summary>Concatenates two messages while preserving each resource identity.</summary>
    /// <param name="first">The first message.</param>
    /// <param name="second">The second message.</param>
    /// <returns>The deferred concatenation.</returns>
    public static LocalisationMessage Concat(LocalisationMessage first, LocalisationMessage second) => new(null, null, null, [first, second]);

    /// <summary>Resolves this message for insertion into another composite format.</summary>
    /// <returns>The current value, or an empty string for a null literal.</returns>
    public override string ToString() => Value ?? string.Empty;

    private static void Validate(LocalisationContext context, string key)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (key is not null)
        {
            return;
        }

        throw new ArgumentNullException(nameof(key));
    }
}
