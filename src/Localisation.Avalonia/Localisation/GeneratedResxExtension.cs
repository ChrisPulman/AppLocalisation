// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Metadata;

#if REACTIVE_SHIM
namespace CP.Localisation.Avalonia.Reactive;
#else
namespace CP.Localisation.Avalonia;
#endif

/// <summary>Resolves generated resources without assembly scanning or automatic type-converter discovery.</summary>
/// <remarks>Register dictionaries before creating bindings. Use <see cref="BindingConverter"/> for custom fallback conversions.</remarks>
public sealed class GeneratedResxExtension
{
    /// <summary>Identifies the inheritable attached default dictionary-name property.</summary>
    public static readonly AttachedProperty<string?> DefaultResxNameProperty = ResxExtension.DefaultResxNameProperty;

    private readonly ResxExtension _extension = new();

    /// <summary>Initializes a new instance of the <see cref="GeneratedResxExtension"/> class.</summary>
    public GeneratedResxExtension()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GeneratedResxExtension"/> class.</summary>
    /// <param name="key">The resource key.</param>
    public GeneratedResxExtension(string key) => Key = key;

    /// <summary>Raised before generated resource lookup is attempted.</summary>
    public static event EventHandler<GetResourceEventArgs>? GetResource
    {
        add => ResxExtension.GetResource += value;
        remove => ResxExtension.GetResource -= value;
    }

    /// <summary>Gets or sets a converter applied to resource and fallback values.</summary>
    public IValueConverter? BindingConverter { get => _extension.BindingConverter; set => _extension.BindingConverter = value; }

    /// <summary>Gets or sets the converter parameter.</summary>
    public object? BindingConverterParameter { get => _extension.BindingConverterParameter; set => _extension.BindingConverterParameter = value; }

    /// <summary>Gets or sets a composite formatting string.</summary>
    public string? BindingStringFormat { get => _extension.BindingStringFormat; set => _extension.BindingStringFormat = value; }

    /// <summary>Gets or sets the fallback value. Supply a value compatible with the target property or a converter.</summary>
    public object? DefaultValue { get => _extension.DefaultValue; set => _extension.DefaultValue = value; }

    /// <summary>Gets or sets the resource key.</summary>
    [ConstructorArgument("key")]
    public string? Key { get => _extension.Key; set => _extension.Key = value; }

    /// <summary>Gets or sets the registered dictionary name.</summary>
    public string? ResxName { get => _extension.ResxName; set => _extension.ResxName = value; }

    /// <summary>Gets the inherited dictionary name.</summary>
    /// <param name="target">The target object.</param>
    /// <returns>The inherited dictionary name.</returns>
    public static string? GetDefaultResxName(AvaloniaObject target) => ResxExtension.GetDefaultResxName(target);

    /// <summary>Sets the inherited dictionary name.</summary>
    /// <param name="target">The target object.</param>
    /// <param name="value">The registered dictionary name.</param>
    public static void SetDefaultResxName(AvaloniaObject target, string? value) => ResxExtension.SetDefaultResxName(target, value);

    /// <summary>Refreshes all active resource bindings.</summary>
    public static void UpdateAllTargets() => ResxExtension.UpdateAllTargets();

    /// <summary>Refreshes active bindings for a resource key.</summary>
    /// <param name="key">The resource key.</param>
    public static void UpdateTarget(string key) => ResxExtension.UpdateTarget(key);

    /// <summary>Creates a binding that observes culture and resource changes.</summary>
    /// <param name="serviceProvider">The XAML service provider.</param>
    /// <returns>The observable binding.</returns>
    public object ProvideValue(IServiceProvider? serviceProvider) => _extension.ProvideGeneratedValue(serviceProvider);

    internal object? ResolveValue(AvaloniaObject? target, Type targetType) => _extension.ResolveGeneratedValue(target, targetType);
}
