// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using Avalonia.Data.Converters;

#if REACTIVE_SHIM
namespace CP.Localisation.Avalonia.Reactive;
#else
namespace CP.Localisation.Avalonia;
#endif

/// <summary>Converts a statically known enumeration without TypeDescriptor discovery.</summary>
/// <typeparam name="TEnum">The enumeration type.</typeparam>
public sealed class ResourceEnumConverter<TEnum> : IValueConverter
    where TEnum : struct, Enum
{
    private readonly bool _isFlags;

    private readonly Func<TEnum, string> _resourceName;

    private readonly Func<string, CultureInfo, string?> _resolve;

    /// <summary>Initializes a new instance of the <see cref="ResourceEnumConverter{TEnum}"/> class.</summary>
    /// <param name="resourceManager">The resource manager containing localized names.</param>
    /// <param name="isFlags">Whether combinations of named flag values are supported.</param>
    /// <param name="resourceName">Creates the resource key, or uses the enum type and value names.</param>
    public ResourceEnumConverter(ResourceManager resourceManager, bool isFlags, Func<TEnum, string>? resourceName)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);
        _resolve = resourceManager.GetString;
        _isFlags = isFlags;
        _resourceName = resourceName ?? (value => $"{typeof(TEnum).Name}_{value}");
    }

    /// <summary>Initializes a new instance of the <see cref="ResourceEnumConverter{TEnum}"/> class.</summary>
    /// <param name="dictionary">The registered dictionary name.</param>
    /// <param name="isFlags">Whether combinations of named flag values are supported.</param>
    /// <param name="resourceName">Creates the resource key, or uses the enum type and value names.</param>
    public ResourceEnumConverter(string dictionary, bool isFlags, Func<TEnum, string>? resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dictionary);
        _resolve = (key, culture) => CP.Localisation.LocalisationResourceRegistry.TryGetResource(dictionary, key, culture, out var value)
            ? value as string
            : null;
        _isFlags = isFlags;
        _resourceName = resourceName ?? (value => $"{typeof(TEnum).Name}_{value}");
    }

    /// <summary>Initializes a new instance of the <see cref="ResourceEnumConverter{TEnum}"/> class.</summary>
    /// <param name="resourceManager">The resource manager containing localized names.</param>
    public ResourceEnumConverter(ResourceManager resourceManager)
        : this(resourceManager, false, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ResourceEnumConverter{TEnum}"/> class.</summary>
    /// <param name="resourceManager">The resource manager containing localized names.</param>
    /// <param name="isFlags">Whether combinations of named flag values are supported.</param>
    public ResourceEnumConverter(ResourceManager resourceManager, bool isFlags)
        : this(resourceManager, isFlags, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ResourceEnumConverter{TEnum}"/> class.</summary>
    /// <param name="dictionary">The registered dictionary name.</param>
    public ResourceEnumConverter(string dictionary)
        : this(dictionary, false, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ResourceEnumConverter{TEnum}"/> class.</summary>
    /// <param name="dictionary">The registered dictionary name.</param>
    /// <param name="isFlags">Whether combinations of named flag values are supported.</param>
    public ResourceEnumConverter(string dictionary, bool isFlags)
        : this(dictionary, isFlags, null)
    {
    }

    /// <inheritdoc cref="ConvertToString(TEnum, CultureInfo)"/>
    public string ConvertToString(TEnum value) => ConvertToString(value, null);

    /// <summary>Gets the localized display text.</summary>
    /// <param name="value">The enumeration value.</param>
    /// <param name="culture">The lookup culture, or the active UI culture.</param>
    /// <returns>The localized text.</returns>
    public string ConvertToString(TEnum value, CultureInfo? culture)
    {
        culture ??= CultureManager.UICulture;
        if (!_isFlags || Enum.IsDefined(value))
        {
            return GetText(value, culture);
        }

        var bits = GetBits(value);
        var parts = new List<string>();
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            var flag = GetBits(candidate);
            if (flag != 0 && (flag & (flag - 1)) == 0 && (bits & flag) == flag)
            {
                parts.Add(GetText(candidate, culture));
            }
        }

        return string.Join(", ", parts);
    }

    /// <inheritdoc cref="ConvertFromString(string, CultureInfo)"/>
    public TEnum? ConvertFromString(string text) => ConvertFromString(text, null);

    /// <summary>Resolves localized text to its enumeration value.</summary>
    /// <param name="text">The localized text.</param>
    /// <param name="culture">The lookup culture, or the active UI culture.</param>
    /// <returns>The enumeration value, or null if no value matches.</returns>
    public TEnum? ConvertFromString(string text, CultureInfo? culture)
    {
        ArgumentNullException.ThrowIfNull(text);
        culture ??= CultureManager.UICulture;
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(GetText(candidate, culture), text, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        if (!_isFlags)
        {
            return Enum.TryParse<TEnum>(text, out var parsed) ? parsed : null;
        }

        var names = new List<string>();
        foreach (var part in text.Split(','))
        {
            var name = FindName(part.Trim(), culture);
            if (name is null)
            {
                return null;
            }

            names.Add(name);
        }

        return Enum.TryParse<TEnum>(string.Join(", ", names), out var flags) ? flags : null;
    }

    /// <inheritdoc cref="GetValues(CultureInfo)"/>
    public List<KeyValuePair<TEnum, string>> GetValues() => GetValues(null);

    /// <summary>Gets every enumeration value and its localized display text.</summary>
    /// <param name="culture">The lookup culture, or the active UI culture.</param>
    /// <returns>The localized enumeration values.</returns>
    public List<KeyValuePair<TEnum, string>> GetValues(CultureInfo? culture)
    {
        var result = new List<KeyValuePair<TEnum, string>>();
        foreach (var value in Enum.GetValues<TEnum>())
        {
            result.Add(new(value, ConvertToString(value, culture)));
        }

        return result;
    }

    /// <inheritdoc />
    object? IValueConverter.Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TEnum enumValue ? ConvertToString(enumValue, culture) : null;

    /// <inheritdoc />
    object? IValueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text ? ConvertFromString(text, culture) : null;

    private static ulong GetBits(TEnum value) => ulong.Parse(value.ToString("X"), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private string? FindName(string text, CultureInfo culture)
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(GetText(candidate, culture), text, StringComparison.Ordinal))
            {
                return candidate.ToString();
            }
        }

        return null;
    }

    private string GetText(TEnum value, CultureInfo culture)
    {
        var key = _resourceName(value);
        return _resolve(key, culture) ?? key;
    }
}
