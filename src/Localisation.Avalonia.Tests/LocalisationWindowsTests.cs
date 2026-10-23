// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Media;
using CP.Localisation;
using Lean = CP.Localisation.Avalonia;
using Reactive = CP.Localisation.Avalonia.Reactive;

namespace Localisation.Avalonia.Tests;

/// <summary>Verifies existing window state and explicit localization graphs survive culture refreshes.</summary>
internal sealed class LocalisationWindowsTests
{
    /// <summary>Verifies logical and visual overlap and shared enumerable roots are visited once.</summary>
    /// <param name="reactive">Whether to use the Reactive runtime.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    internal async Task Refresh_PreservesWindowStateAndVisitsSharedGraphOnce(bool reactive)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        var result = await session.Dispatch(
            () =>
        {
            var child = new RefreshNode();
            var root = new RefreshNode { Child = child };
            child.Child = root;
            var items = new[] { root, child };
            var selector = new ComboBox { ItemsSource = items, SelectedIndex = 0, DataContext = root };
            var editor = new TextBox { Text = "unchanged", DataContext = child };
            var panel = new StackPanel { Children = { selector, editor } };
            var window = new Window { DataContext = root, Content = panel };
            window.Show();
            if (reactive)
            {
                Reactive.LocalisationWindows.Refresh(window);
            }
            else
            {
                Lean.LocalisationWindows.Refresh(window);
            }

            var result = new RefreshResult(
                root.Count,
                child.Count,
                ReferenceEquals(window.Content, panel),
                ReferenceEquals(selector.ItemsSource, items),
                ReferenceEquals(selector.SelectedItem, root),
                editor.Text);
            window.Close();
            return result;
        }, CancellationToken.None);
        await Assert.That(result.RootCount).IsEqualTo(1);
        await Assert.That(result.ChildCount).IsEqualTo(1);
        await Assert.That(result.ContentPreserved).IsTrue();
        await Assert.That(result.ItemsPreserved).IsTrue();
        await Assert.That(result.SelectionPreserved).IsTrue();
        await Assert.That(result.Text).IsEqualTo("unchanged");
    }

    /// <summary>Verifies both runtime styles observe culture changes while local direction overrides remain valid.</summary>
    /// <param name="reactive">Whether to use the Reactive runtime.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    internal async Task Direction_UpdatesExistingWindows(bool reactive)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        var result = await session.Dispatch(
            () =>
        {
            using var context = new LocalisationContext("Localisation.Avalonia.Tests", "Localisation.Avalonia.Tests.TestResources");
            context.SetCulture(CultureInfo.GetCultureInfo("en"));
            var app = Application.Current!;
            if (reactive)
            {
                Reactive.LocalisationWindows.ApplyDirection(app, context);
            }
            else
            {
                Lean.LocalisationWindows.ApplyDirection(app, context);
            }

            var document = new TextBox { FlowDirection = FlowDirection.LeftToRight };
            var window = new Window { Content = document };
            window.Show();
            var initial = window.FlowDirection;
            context.SetCulture(CultureInfo.GetCultureInfo("ar"));
            if (reactive)
            {
                Reactive.LocalisationWindows.Refresh(app, context, context.UICulture);
                Reactive.LocalisationWindows.Refresh(window);
            }
            else
            {
                Lean.LocalisationWindows.Refresh(app, context, context.UICulture);
            }

            var result = new DirectionResult(initial, window.FlowDirection, document.FlowDirection);
            window.Close();
            app.Styles.Clear();
            return result;
        }, CancellationToken.None);
        await Assert.That(result.Initial).IsEqualTo(FlowDirection.LeftToRight);
        await Assert.That(result.Current).IsEqualTo(FlowDirection.RightToLeft);
        await Assert.That(result.Document).IsEqualTo(FlowDirection.LeftToRight);
    }

    /// <summary>Verifies all desktop windows share a single reference-identity traversal.</summary>
    /// <param name="reactive">Whether to use the Reactive runtime.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    internal async Task DesktopRefresh_VisitsSharedModelsOnce(bool reactive)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        var count = await session.Dispatch(
            () =>
            {
                using var context = new LocalisationContext("Localisation.Avalonia.Tests", "Localisation.Avalonia.Tests.TestResources");
                var root = new RefreshNode();
                var lifetime = new ClassicDesktopStyleApplicationLifetime();
                var first = new Window { DataContext = root };
                var second = new Window { DataContext = root };
                var app = new Application { ApplicationLifetime = lifetime };

                // Seed the real lifetime with existing windows without starting a second application loop.
                ((ICollection<Window>)lifetime.Windows).Add(first);
                ((ICollection<Window>)lifetime.Windows).Add(second);
                if (reactive)
                {
                    Reactive.LocalisationWindows.Refresh(app, context, context.UICulture);
                }
                else
                {
                    Lean.LocalisationWindows.Refresh(app, context, context.UICulture);
                }

                first.Close();
                second.Close();
                return root.Count;
            },
            CancellationToken.None);
        await Assert.That(count).IsEqualTo(1);
    }

    private sealed class RefreshNode : ILocalisationChildren
    {
        internal RefreshNode? Child { get; set; }

        internal int Count { get; private set; }

        public void Refresh(HashSet<object> visited)
        {
            Count++;
            LocalisationRefresh.RefreshCore(Child, visited);
        }
    }

    private sealed record RefreshResult(int RootCount, int ChildCount, bool ContentPreserved, bool ItemsPreserved, bool SelectionPreserved, string? Text);

    private sealed record DirectionResult(FlowDirection Initial, FlowDirection Current, FlowDirection Document);
}
