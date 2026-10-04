// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;

#if REACTIVE_SHIM
namespace CP.Localisation.Avalonia.Reactive;
#else
namespace CP.Localisation.Avalonia;
#endif

/// <summary>Resolves embedded RESX values and exposes them as culture-aware Avalonia bindings.</summary>
public sealed class ResxExtension
{
    /// <summary>Identifies the inheritable attached default resource-name property.</summary>
    public static readonly AttachedProperty<string?> DefaultResxNameProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, string?>(
            "DefaultResxName",
            typeof(ResxExtension),
            defaultValue: null,
            inherits: true);

    private static readonly Dictionary<string, WeakReference<ResourceManager>> _resourceManagers = [with(StringComparer.Ordinal)];

    private static readonly object _resourceManagersLock = new();

    /// <summary>Initializes a new instance of the <see cref="ResxExtension"/> class.</summary>
    public ResxExtension()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ResxExtension"/> class.</summary>
    /// <param name="key">The resource key.</param>
    public ResxExtension(string key) => Key = key;

    /// <summary>Raised before the default embedded-resource lookup is attempted.</summary>
    public static event EventHandler<GetResourceEventArgs>? GetResource;

    /// <summary>Raised internally when one or more active resource bindings must refresh.</summary>
    internal static event EventHandler<ResourceRefreshEventArgs>? RefreshRequested;

    /// <summary>Gets or sets an optional value converter applied to the resolved resource.</summary>
    public IValueConverter? BindingConverter { get; set; }

    /// <summary>Gets or sets the parameter passed to <see cref="BindingConverter"/>.</summary>
    public object? BindingConverterParameter { get; set; }

    /// <summary>Gets or sets an optional composite formatting string.</summary>
    public string? BindingStringFormat { get; set; }

    /// <summary>Gets or sets the value returned when no resource can be resolved.</summary>
    public object? DefaultValue { get; set; }

    /// <summary>Gets or sets the embedded resource key.</summary>
    [ConstructorArgument("key")]
    public string? Key
    {
        get;
        set => field = value?.Trim();
    }

    /// <summary>Gets or sets the fully qualified embedded RESX base name.</summary>
    public string? ResxName { get; set; }

    /// <summary>Gets the inherited default RESX base name from an Avalonia object.</summary>
    /// <param name="target">The target object.</param>
    /// <returns>The inherited RESX base name.</returns>
    public static string? GetDefaultResxName(AvaloniaObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(DefaultResxNameProperty);
    }

    /// <summary>Sets the inherited default RESX base name on an Avalonia object.</summary>
    /// <param name="target">The target object.</param>
    /// <param name="value">The fully qualified RESX base name.</param>
    public static void SetDefaultResxName(AvaloniaObject target, string? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        _ = target.SetValue(DefaultResxNameProperty, value);
    }

    /// <summary>Forces every active resource binding to reevaluate its value.</summary>
    public static void UpdateAllTargets() => RefreshRequested?.Invoke(null, new ResourceRefreshEventArgs(null));

    /// <summary>Forces bindings for one resource key to reevaluate their values.</summary>
    /// <param name="key">The resource key.</param>
    public static void UpdateTarget(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        RefreshRequested?.Invoke(null, new ResourceRefreshEventArgs(key.Trim()));
    }

    /// <summary>Creates an Avalonia binding that tracks culture and resource refresh notifications.</summary>
    /// <param name="serviceProvider">The XAML service provider.</param>
    /// <returns>An observable Avalonia binding.</returns>
    public object ProvideValue(IServiceProvider? serviceProvider)
    {
        var targetService = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        var target = targetService?.TargetObject as AvaloniaObject;
        var targetType = targetService?.TargetProperty switch
        {
            AvaloniaProperty property => property.PropertyType,
            PropertyInfo property => GetClrPropertyType(property),
            _ => typeof(object),
        };

        return new RefreshingObservable<object?>(
            () => ResolveValue(target, targetType),
            Key,
            observeResourceChanges: true).ToBinding();
    }

    internal object? ResolveValue(AvaloniaObject? target, Type targetType)
    {
        var culture = CultureManager.UICulture;
        var resxName = ResxName ?? (target is null ? null : GetDefaultResxName(target));
        var eventArgs = new GetResourceEventArgs(resxName, Key, culture);
        GetResource?.Invoke(this, eventArgs);
        var value = eventArgs.Resource;

        if (value is null && resxName is not null && Key is not null)
        {
            value = FindResourceManager(resxName)?.GetObject(Key, culture);
        }

        value ??= GetDefaultValue(targetType);
        if (BindingConverter is not null)
        {
            value = BindingConverter.Convert(value, targetType, BindingConverterParameter, culture);
        }

        if (BindingStringFormat is not null)
        {
            value = string.Format(culture, BindingStringFormat, value);
        }

        return value;
    }

    private static ResourceManager? FindResourceManager(string resxName)
    {
        lock (_resourceManagersLock)
        {
            if (_resourceManagers.TryGetValue(resxName, out var reference)
                && reference.TryGetTarget(out var cachedManager))
            {
                return cachedManager;
            }

            _ = _resourceManagers.Remove(resxName);
            var resourceName = $"{resxName}.resources";
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .Where(static candidate => !candidate.IsDynamic)
                .FirstOrDefault(candidate => candidate.GetManifestResourceNames().Contains(resourceName, StringComparer.Ordinal));
            if (assembly is null)
            {
                return null;
            }

            var manager = new ResourceManager(resxName, assembly);
            _resourceManagers.Add(resxName, new(manager));
            return manager;
        }
    }

    private static Type GetClrPropertyType(PropertyInfo property) => property.PropertyType;

    private object? GetDefaultValue(Type targetType)
    {
        if (DefaultValue is null)
        {
            return targetType == typeof(string) || targetType == typeof(object) ? $"#{Key}" : null;
        }

        if (targetType == typeof(string) || targetType == typeof(object) || targetType.IsInstanceOfType(DefaultValue))
        {
            return DefaultValue;
        }

        try
        {
            return TypeDescriptor.GetConverter(targetType).ConvertFrom(null, CultureInfo.InvariantCulture, DefaultValue);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or FormatException)
        {
            return DefaultValue;
        }
    }
}
