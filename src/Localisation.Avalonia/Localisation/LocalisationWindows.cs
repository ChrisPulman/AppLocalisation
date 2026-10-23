// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

#if REACTIVE_SHIM
namespace CP.Localisation.Avalonia.Reactive;
#else
namespace CP.Localisation.Avalonia;
#endif

/// <summary>Updates existing Avalonia interfaces using an owned localization context.</summary>
/// <remarks>Call these methods on the UI thread that owns the application and its controls.</remarks>
public static class LocalisationWindows
{
    /// <summary>Adds a culture-driven writing direction style for application windows.</summary>
    /// <param name="application">The application owning the styles.</param>
    /// <param name="context">The localization context supplying the interface culture.</param>
    public static void ApplyDirection(Application application, LocalisationContext context)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(context);
        var direction = LocalisationBinding.Create(
            context,
            application,
            static (_, culture) => culture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
        application.Styles.Add(new Style(static selector => selector.OfType<Window>())
        {
            Setters = { new Setter(Visual.FlowDirectionProperty, direction) },
        });
    }

    /// <summary>Refreshes choices and existing desktop windows without replacing controls or view models.</summary>
    /// <param name="application">The application owning the windows.</param>
    /// <param name="context">The localization context owning choice lists.</param>
    /// <param name="culture">The selected interface culture for legacy resource bindings.</param>
    public static void Refresh(Application application, LocalisationContext context, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(culture);
        CultureManager.SynchronizeThreadCulture = false;
        CultureManager.UICulture = culture;
        context.RefreshChoices();
        if (application.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var window in desktop.Windows)
        {
            Refresh(window, visited);
        }
    }

    /// <summary>Refreshes a window hosted outside a desktop application lifetime.</summary>
    /// <param name="window">The existing window.</param>
    public static void Refresh(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Refresh(window, [with(ReferenceEqualityComparer.Instance)]);
    }

    private static void Refresh(Window window, HashSet<object> visited)
    {
        RefreshControl(window, visited);
        foreach (var logical in window.GetLogicalDescendants())
        {
            if (logical is Control control)
            {
                RefreshControl(control, visited);
            }
        }

        foreach (var visual in window.GetVisualDescendants())
        {
            if (visual is Control control)
            {
                RefreshControl(control, visited);
            }
        }
    }

    private static void RefreshControl(Control control, HashSet<object> visited)
    {
        LocalisationRefresh.RefreshCore(control.DataContext, visited);
        if (control is not ItemsControl { ItemsSource: { } items })
        {
            return;
        }

        LocalisationRefresh.RefreshCore(items, visited);
    }
}
