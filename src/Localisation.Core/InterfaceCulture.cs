// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace CP.Localisation;

/// <summary>Resolves an interface language and applies it without changing number or date formatting.</summary>
public static class InterfaceCulture
{
    private const string LanguageAssignment = "--language=";

    /// <summary>Gets the command-line option naming the requested interface culture.</summary>
    public static string LanguageArgument => "--language";

    /// <summary>Resolves the last command-line override, saved preference, or desktop culture.</summary>
    /// <param name="args">Startup arguments accepting either --language=name or --language name.</param>
    /// <param name="savedLanguage">The stored culture name, or null to follow the desktop.</param>
    /// <param name="desktopCulture">The fallback culture captured by the application.</param>
    /// <returns>The selected culture, or the desktop culture for empty or invalid culture names.</returns>
    public static CultureInfo Resolve(IReadOnlyList<string> args, string? savedLanguage, CultureInfo desktopCulture)
    {
        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        if (desktopCulture is null)
        {
            throw new ArgumentNullException(nameof(desktopCulture));
        }

        var language = savedLanguage;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index] ?? throw new ArgumentException("Arguments cannot contain null.", nameof(args));
            if (argument.StartsWith(LanguageAssignment, StringComparison.Ordinal))
            {
                language = argument.Substring(LanguageAssignment.Length);
            }
            else if (argument == LanguageArgument && index + 1 < args.Count)
            {
                index++;
                language = args[index] ?? throw new ArgumentException("Arguments cannot contain null.", nameof(args));
            }
        }

        return ResolveName(language, desktopCulture);
    }

    /// <summary>Applies UI thread cultures and publishes the selected context culture.</summary>
    /// <param name="context">The resource context owned by the application.</param>
    /// <param name="culture">The selected interface culture.</param>
    /// <remarks>
    /// This changes CurrentUICulture and DefaultThreadCurrentUICulture, leaving numeric cultures unchanged.
    /// Call on the thread that owns the context's UI consumers. Use LocalisationContext.SetCulture for independent contexts.
    /// </remarks>
    public static void Apply(LocalisationContext context, CultureInfo culture)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        context.ThrowIfDisposed();
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        context.SetCulture(culture);
    }

    private static CultureInfo ResolveName(string? language, CultureInfo desktopCulture)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return desktopCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(language!);
        }
        catch (CultureNotFoundException)
        {
            return desktopCulture;
        }
    }
}
