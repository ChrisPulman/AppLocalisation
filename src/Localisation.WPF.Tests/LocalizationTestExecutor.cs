// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using TUnit.Core.Executors;
using TUnit.Core.Interfaces;

[assembly: TestExecutor<Localisation.WPF.Tests.LocalizationTestExecutor>]

namespace Localisation.WPF.Tests;

/// <summary>Isolates the process-wide markup registries between tests that create their own STA threads.</summary>
internal sealed class LocalizationTestExecutor : ITestExecutor
{
    /// <summary>Runs a test with empty markup registries and removes its registrations afterwards.</summary>
    /// <param name="context">The executing test context.</param>
    /// <param name="action">The test body.</param>
    /// <returns>A task representing the asynchronous test execution.</returns>
    public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
    {
        ResetMarkupRegistries();
        try
        {
            await action();
        }
        finally
        {
            ResetMarkupRegistries();
        }
    }

    private static void ResetMarkupRegistries()
    {
        CP.Localisation.ResxExtension.MarkupManager.ActiveExtensions.Clear();
        CP.Localisation.UICultureExtension.MarkupManager.ActiveExtensions.Clear();
        CP.Localisation.Reactive.ResxExtension.MarkupManager.ActiveExtensions.Clear();
        CP.Localisation.Reactive.UICultureExtension.MarkupManager.ActiveExtensions.Clear();
    }
}
