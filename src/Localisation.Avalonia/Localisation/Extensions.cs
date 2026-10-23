// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

#if REACTIVE_SHIM
namespace CP.Localisation.Avalonia.Reactive;
#else
namespace CP.Localisation.Avalonia;
#endif

/// <summary>Provides localization-related extension methods.</summary>
public static class Extensions
{
    /// <summary>Provides culture helpers.</summary>
    /// <param name="culture">The culture applied to the current thread.</param>
    extension(CultureInfo culture)
    {
        /// <summary>Synchronizes the current thread culture and UI culture.</summary>
        public void SyncCultureInfo()
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        }
    }

    /// <summary>Provides localized enumeration display helpers.</summary>
    /// <param name="value">The enumeration value to localize.</param>
    extension(Enum value)
    {
        /// <summary>Converts an enumeration value to its localized display string.</summary>
        /// <returns>The localized display string, when one can be resolved.</returns>
        [RequiresUnreferencedCode("Uses the registered TypeDescriptor converter. Use ResourceEnumConverter<TEnum> for trimming and Native AOT.")]
        public string? ConvertToString() => ResourceEnumConverter.ConvertToString(value);
    }
}
