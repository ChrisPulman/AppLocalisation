// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Avalonia.Data.Converters;

#if REACTIVE_SHIM
namespace CP.Localisation.Avalonia.Reactive;
#else
namespace CP.Localisation.Avalonia;
#endif

/// <summary>Converts enumeration values to and from localized resource strings.</summary>
public class ResourceEnumConverter : EnumConverter, IValueConverter
{
    private readonly Array? _flagValues;

    private readonly bool _isFlagEnum;

    private readonly Dictionary<CultureInfo, Dictionary<string, object>> _lookupTables = new();

    private readonly ResourceManager _resourceManager;

    /// <summary>Initializes a new instance of the <see cref="ResourceEnumConverter"/> class.</summary>
    /// <param name="type">The enumeration type.</param>
    /// <param name="resourceManager">The resource manager containing localized enumeration names.</param>
    public ResourceEnumConverter(Type type, ResourceManager resourceManager)
        : base(type)
    {
        ArgumentNullException.ThrowIfNull(type);
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _isFlagEnum = type.IsDefined(typeof(FlagsAttribute), inherit: true);
        if (!_isFlagEnum)
        {
            return;
        }

        _flagValues = Enum.GetValues(type);
    }

    /// <summary>Uses the registered type converter to localize an enumeration value.</summary>
    /// <param name="value">The enumeration value.</param>
    /// <returns>The localized string.</returns>
    public static string? ConvertToString(Enum value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TypeDescriptor.GetConverter(value.GetType()).ConvertToString(value);
    }

    /// <summary>Gets every enumeration value and its localized display text.</summary>
    /// <param name="enumType">The enumeration type.</param>
    /// <param name="culture">The lookup culture.</param>
    /// <returns>The localized enumeration values.</returns>
    public static List<KeyValuePair<Enum, string?>> GetValues(Type enumType, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(enumType);
        ArgumentNullException.ThrowIfNull(culture);
        var converter = TypeDescriptor.GetConverter(enumType);
        var result = new List<KeyValuePair<Enum, string?>>();
        foreach (Enum value in Enum.GetValues(enumType))
        {
            result.Add(new(value, converter.ConvertToString(null, culture, value)));
        }

        return result;
    }

    /// <summary>Gets every enumeration value and its localized display text using the current UI culture.</summary>
    /// <param name="enumType">The enumeration type.</param>
    /// <returns>The localized enumeration values.</returns>
    public static List<KeyValuePair<Enum, string?>> GetValues(Type enumType) => GetValues(enumType, CultureInfo.CurrentUICulture);

    /// <inheritdoc />
    object? IValueConverter.Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ConvertTo(null, culture, value, targetType);

    /// <inheritdoc />
    object? IValueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ConvertFrom(null, culture, value!);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (value is not string text)
        {
            return base.ConvertFrom(context, culture, value);
        }

        return _isFlagEnum
            ? GetFlagValue(culture, text)
            : GetValue(culture, text) ?? base.ConvertFrom(context, culture, value);
    }

    /// <inheritdoc />
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (value is null)
        {
            return null;
        }

        if (destinationType != typeof(string) && destinationType != typeof(object))
        {
            return base.ConvertTo(context, culture, value, destinationType);
        }

        return _isFlagEnum ? GetFlagValueText(culture, value) : GetValueText(culture, value);
    }

    /// <summary>Gets the resource key for an enumeration value.</summary>
    /// <param name="value">The enumeration value.</param>
    /// <returns>The resource key.</returns>
    protected virtual string GetResourceName(object value) => $"{value.GetType().Name}_{value}";

    private static bool IsSingleBitValue(ulong value) => value != 0 && (value & (value - 1)) == 0;

    private object? GetFlagValue(CultureInfo culture, string text)
    {
        var lookupTable = GetLookupTable(culture);
        ulong result = 0;
        foreach (var part in text.Split(','))
        {
            if (!lookupTable.TryGetValue(part.Trim(), out var value))
            {
                return null;
            }

            result |= Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }

        return Enum.ToObject(EnumType, result);
    }

    private string? GetFlagValueText(CultureInfo culture, object value)
    {
        if (Enum.IsDefined(value.GetType(), value))
        {
            return GetValueText(culture, value);
        }

        var enumValue = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        var values = new List<string>();
        foreach (var flagValue in _flagValues!)
        {
            var flagBits = Convert.ToUInt64(flagValue, CultureInfo.InvariantCulture);
            if (IsSingleBitValue(flagBits) && (flagBits & enumValue) == flagBits)
            {
                values.Add(GetValueText(culture, flagValue!));
            }
        }

        return values.Count == 0 ? null : string.Join(", ", values);
    }

    private Dictionary<string, object> GetLookupTable(CultureInfo culture)
    {
        if (_lookupTables.TryGetValue(culture, out var result))
        {
            return result;
        }

        result = [with(StringComparer.CurrentCulture)];
        if (GetStandardValues() is { } standardValues)
        {
            foreach (var value in standardValues)
            {
                result[GetValueText(culture, value!)] = value!;
            }
        }

        _lookupTables.Add(culture, result);
        return result;
    }

    private object? GetValue(CultureInfo culture, string text)
    {
        _ = GetLookupTable(culture).TryGetValue(text, out var result);
        return result;
    }

    private string GetValueText(CultureInfo culture, object value)
    {
        var resourceName = GetResourceName(value);
        return _resourceManager.GetString(resourceName, culture) ?? resourceName;
    }
}
