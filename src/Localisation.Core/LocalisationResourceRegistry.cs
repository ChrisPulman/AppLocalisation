// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace CP.Localisation;

/// <summary>Stores statically registered resource providers for trimming and Native AOT.</summary>
public static class LocalisationResourceRegistry
{
    private static readonly ConcurrentDictionary<(string Assembly, string Dictionary), Registration> Providers = new();

    private static readonly object RegistrationGate = new();

    private static ProviderEntry[] _orderedProviders = [];

    /// <summary>Registers a provider without advertising supported cultures.</summary>
    /// <param name="assemblyName">The assembly simple name.</param>
    /// <param name="dictionary">The resource base name.</param>
    /// <param name="provider">The provider, including culture fallback.</param>
    public static void Register(string assemblyName, string dictionary, Func<string, CultureInfo, object?> provider) =>
        Register(assemblyName, dictionary, provider, null);

    /// <summary>Atomically replaces a provider and its advertised cultures. Null results denote missing resources.</summary>
    /// <param name="assemblyName">The assembly simple name.</param>
    /// <param name="dictionary">The fully qualified resource base name.</param>
    /// <param name="provider">The provider, including culture fallback.</param>
    /// <param name="cultureNames">Supported culture names, including the neutral language.</param>
    public static void Register(string assemblyName, string dictionary, Func<string, CultureInfo, object?> provider, IEnumerable<string>? cultureNames)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrEmpty(assemblyName);
        ArgumentException.ThrowIfNullOrEmpty(dictionary);
        ArgumentNullException.ThrowIfNull(provider);
#else
        if (string.IsNullOrEmpty(assemblyName))
        {
            throw new ArgumentException("An assembly name is required.", nameof(assemblyName));
        }

        if (string.IsNullOrEmpty(dictionary))
        {
            throw new ArgumentException("A dictionary is required.", nameof(dictionary));
        }

        if (provider is null)
        {
            throw new ArgumentNullException(nameof(provider));
        }
#endif
        var cultures = cultureNames?.Select(CultureInfo.GetCultureInfo).ToArray() ?? [];
        lock (RegistrationGate)
        {
            Providers[(assemblyName, dictionary)] = new(provider, cultures);
            var orderedProviders = Providers.OrderBy(static item => item.Key.Assembly, StringComparer.Ordinal)
                .Select(static item => new ProviderEntry(item.Key.Dictionary, item.Value)).ToArray();
            Volatile.Write(ref _orderedProviders, orderedProviders);
        }
    }

    /// <summary>Looks up a resource in a registered assembly and dictionary.</summary>
    /// <param name="assemblyName">The assembly simple name.</param>
    /// <param name="dictionary">The fully qualified resource base name.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="culture">The requested culture.</param>
    /// <param name="value">The resource, or null when absent.</param>
    /// <returns>Whether a non-null resource was found.</returns>
    public static bool TryGetResource(string assemblyName, string dictionary, string key, CultureInfo culture, out object? value)
    {
        ValidateCulture(culture);
        value = Providers.TryGetValue((assemblyName, dictionary), out var registration) ? registration.Provider(key, culture) : null;
        return value is not null;
    }

    /// <summary>Looks up a resource base name across assemblies in ordinal assembly-name order.</summary>
    /// <param name="dictionary">The resource base name.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="culture">The requested culture.</param>
    /// <param name="value">The resource, or null when absent.</param>
    /// <returns>Whether a non-null resource was found.</returns>
    public static bool TryGetResource(string dictionary, string key, CultureInfo culture, out object? value)
    {
        ValidateCulture(culture);
        foreach (var item in Volatile.Read(ref _orderedProviders))
        {
            if (item.Dictionary == dictionary && item.Registration.Provider(key, culture) is { } resource)
            {
                value = resource;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>Gets all registered cultures in ordinal name order.</summary>
    /// <returns>The registered cultures.</returns>
    public static IReadOnlyList<CultureInfo> GetAvailableCultures() => GetAvailableCultures(null);

    /// <summary>Gets registered cultures in ordinal name order.</summary>
    /// <param name="assemblyName">An optional assembly filter.</param>
    /// <returns>The registered cultures.</returns>
    public static IReadOnlyList<CultureInfo> GetAvailableCultures(string? assemblyName) =>
        Providers.Where(item => assemblyName is null || item.Key.Assembly == assemblyName)
            .SelectMany(static item => item.Value.Cultures)
            .GroupBy(static culture => culture.Name, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => group.First()).ToArray();

    private static void ValidateCulture(CultureInfo culture)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(culture);
#else
        if (culture is not null)
        {
            return;
        }

        throw new ArgumentNullException(nameof(culture));
#endif
    }

    private sealed class ProviderEntry(string dictionary, Registration registration)
    {
        public string Dictionary { get; } = dictionary;

        public Registration Registration { get; } = registration;
    }

    private sealed class Registration(Func<string, CultureInfo, object?> provider, CultureInfo[] cultures)
    {
        public Func<string, CultureInfo, object?> Provider { get; } = provider;

        public CultureInfo[] Cultures { get; } = cultures;
    }
}
