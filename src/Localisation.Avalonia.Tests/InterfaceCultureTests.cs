// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Threading.Tasks;
using CP.Localisation;

namespace Localisation.Avalonia.Tests;

[NotInParallel]
internal sealed class InterfaceCultureTests
{
    private const string ResourceName = "InterfaceCulture.Tests";

    private const string LanguageOption = "--language";

    [Test]
    internal async Task ResolveHonorsOverridesAndDesktopFallback()
    {
        var desktop = CultureInfo.GetCultureInfo("en-GB");
        await Assert.That(InterfaceCulture.Resolve([], "fr", desktop).Name).IsEqualTo("fr");
        await Assert.That(InterfaceCulture.Resolve(["--language=ar", "document.pdf"], "fr", desktop).Name).IsEqualTo("ar");
        await Assert.That(InterfaceCulture.Resolve([LanguageOption, "de", "document.pdf"], "fr", desktop).Name).IsEqualTo("de");
        await Assert.That(InterfaceCulture.Resolve(["--language=fr", LanguageOption, "zh-Hans"], "ar", desktop).Name).IsEqualTo("zh-Hans");
        await Assert.That(InterfaceCulture.Resolve([LanguageOption], "fr", desktop).Name).IsEqualTo("fr");
        await Assert.That(InterfaceCulture.Resolve(["--language="], "fr", desktop)).IsEqualTo(desktop);
        await Assert.That(InterfaceCulture.Resolve([], null, desktop)).IsEqualTo(desktop);
        await Assert.That(InterfaceCulture.Resolve([], "not_a_culture!", desktop)).IsEqualTo(desktop);
        await Assert.That(() => InterfaceCulture.Resolve(null!, null, desktop)).Throws<ArgumentNullException>();
        await Assert.That(() => InterfaceCulture.Resolve([], null, null!)).Throws<ArgumentNullException>();
        await Assert.That(() => InterfaceCulture.Resolve([null!], null, desktop)).Throws<ArgumentException>();
        await Assert.That(() => InterfaceCulture.Resolve([LanguageOption, null!], null, desktop)).Throws<ArgumentException>();
    }

    [Test]
    internal async Task ApplyPublishesAfterUpdatingOnlyInterfaceCultures()
    {
        using var context = new LocalisationContext(ResourceName, ResourceName);
        var previousUi = CultureInfo.CurrentUICulture;
        var previousDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        var previousNumeric = CultureInfo.CurrentCulture;
        var previousDefaultNumeric = CultureInfo.DefaultThreadCurrentCulture;
        var observer = new CultureObserver();
        using var subscription = context.CultureChanges.Subscribe(observer);
        try
        {
            var selected = CultureInfo.GetCultureInfo("ar");
            InterfaceCulture.Apply(context, selected);
            await Assert.That(context.UICulture).IsEqualTo(selected);
            await Assert.That(CultureInfo.CurrentUICulture).IsEqualTo(selected);
            await Assert.That(CultureInfo.DefaultThreadCurrentUICulture).IsEqualTo(selected);
            await Assert.That(observer.Ambient).IsEqualTo(selected);
            await Assert.That(CultureInfo.CurrentCulture).IsEqualTo(previousNumeric);
            await Assert.That(CultureInfo.DefaultThreadCurrentCulture).IsEqualTo(previousDefaultNumeric);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUi;
        }
    }

    [Test]
    internal async Task ApplyValidatesBeforeChangingAmbientCulture()
    {
        using var context = new LocalisationContext(ResourceName, ResourceName);
        var previous = CultureInfo.CurrentUICulture;
        await Assert.That(() => InterfaceCulture.Apply(null!, previous)).Throws<ArgumentNullException>();
        await Assert.That(() => InterfaceCulture.Apply(context, null!)).Throws<ArgumentNullException>();
        context.Dispose();
        await Assert.That(() => InterfaceCulture.Apply(context, CultureInfo.GetCultureInfo("fr"))).Throws<ObjectDisposedException>();
        await Assert.That(CultureInfo.CurrentUICulture).IsEqualTo(previous);
    }

    private sealed class CultureObserver : IObserver<CultureInfo>
    {
        internal CultureInfo? Ambient { get; private set; }

        public void OnNext(CultureInfo value) => Ambient = CultureInfo.CurrentUICulture;

        public void OnCompleted()
        {
        }

        public void OnError(Exception error) => throw error;
    }
}
