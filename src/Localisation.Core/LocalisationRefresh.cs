// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CP.Localisation;

/// <summary>Refreshes explicit localization graphs without reflection or property discovery.</summary>
public static class LocalisationRefresh
{
    /// <summary>Refreshes an existing graph once per object reference, safely handling shared children and cycles.</summary>
    /// <param name="value">The existing graph root.</param>
    public static void Refresh(object? value) => RefreshCore(value, [with(ReferenceComparer.Instance)]);

    /// <summary>Refreshes an object and its explicit children and enumerable items.</summary>
    /// <remarks>Share the reference-identity visited set supplied to <see cref="ILocalisationChildren.Refresh"/> when traversing children.</remarks>
    /// <param name="value">An existing child, collection, or graph root.</param>
    /// <param name="visited">Objects already visited in this traversal.</param>
    public static void RefreshCore(object? value, HashSet<object> visited)
    {
        if (visited is null)
        {
            throw new ArgumentNullException(nameof(visited));
        }

        if (value is null || value is string || !visited.Add(value))
        {
            return;
        }

        if (value is ILocalisationChildren children)
        {
            children.Refresh(visited);
        }

        if (value is not IEnumerable values)
        {
            return;
        }

        foreach (var item in values)
        {
            RefreshCore(item, visited);
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
