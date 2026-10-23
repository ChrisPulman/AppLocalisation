// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CP.Localisation;

namespace Localisation.Avalonia.Tests;

/// <summary>Verifies explicit graph traversal preserves state and reference identity.</summary>
internal sealed class LocalisationRefreshTests
{
    /// <summary>Verifies cycles and shared equal-valued children refresh each reference only once.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Refresh_CyclesAndSharedReferences_VisitsEachReferenceOnce()
    {
        var first = new Node();
        var second = new Node();
        var root = new Node();
        root.Children.AddRange([first, second, first, root, null, "text"]);
        first.Children.Add(root);
        LocalisationRefresh.Refresh(root);
        await Assert.That(root.Refreshes).IsEqualTo(1);
        await Assert.That(first.Refreshes).IsEqualTo(1);
        await Assert.That(second.Refreshes).IsEqualTo(1);
    }

    /// <summary>Verifies traversal does not discover lazy properties or enumerate strings as characters.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task Refresh_ExplicitChildren_DoesNotReadLazyPropertiesOrVisitStrings()
    {
        var node = new Node();
        LocalisationRefresh.Refresh(new object?[] { node, node, "text", null });
        await Assert.That(node.Refreshes).IsEqualTo(1);
        HashSet<object> visited = [];
        LocalisationRefresh.RefreshCore("text", visited);
        LocalisationRefresh.RefreshCore(null, visited);
        await Assert.That(visited.Count).IsEqualTo(0);
        await Assert.That(() => LocalisationRefresh.RefreshCore(node, null!)).Throws<ArgumentNullException>();
    }

    private sealed class Node : ILocalisationChildren
    {
        public List<object?> Children { get; } = [];

        public int Refreshes { get; private set; }

        public object LazyChild => throw new InvalidOperationException($"Lazy properties must not be inspected: {Refreshes}.");

        public override bool Equals(object? obj) => obj is Node;

        public override int GetHashCode() => 0;

        void ILocalisationChildren.Refresh(HashSet<object> visited)
        {
            Refreshes++;
            LocalisationRefresh.RefreshCore(Children, visited);
        }
    }
}
