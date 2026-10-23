# Localisation for WPF and Avalonia

Culture-aware RESX markup extensions, runtime culture switching, resource overrides, and localized enum conversion for WPF and Avalonia.

![NuGet downloads](https://img.shields.io/nuget/dt/Localisation.WPF?color=pink&style=plastic)
[![Localisation.WPF](https://img.shields.io/nuget/v/Localisation.WPF.svg?style=plastic)](https://www.nuget.org/packages/Localisation.WPF)

## V3 user guide

V3 adds generated RESX lookup for trimming and Native AOT, UI-independent localisation contexts, and reusable helpers for updating an existing interface when its language changes. Existing V2 resource extensions and converters remain available. The generated Avalonia path uses explicit conversion instead of discovering custom `TypeConverter` implementations.

Start here:

- [Install the packages](#1-install-the-packages).
- [Add translated resources](#2-add-translated-resources).
- [Create an application context](#3-create-an-application-context).
- [Display text in Avalonia](#4-display-text-in-avalonia).
- [Switch language immediately](#5-switch-language-immediately).
- [Refresh computed properties](#6-refresh-computed-properties).
- [Translate messages and choices](#7-translate-messages-and-choices).
- [Publish with trimming and Native AOT](#8-publish-with-trimming-and-native-aot).

For WPF, follow the [WPF quick start](#wpf-quick-start). The [package comparison](#choose-a-package), [existing culture APIs](#changing-and-observing-culture), and [public API reference](#complete-public-api-reference) follow this guide.

### 1. Install the packages

Choose one UI package: `Localisation.Avalonia` or `Localisation.WPF`. Choose its `.Reactive` variant instead if your application needs System.Reactive APIs. `Localisation.Core` provides the toolkit-independent APIs in `CP.Localisation` and is a dependency of the UI packages. A console application or shared library can reference Core directly.

Add `Localisation.Generators` to **each project containing RESX files** that needs generated lookup. For a new Avalonia application, add these references to its existing project:

```xml
<ItemGroup>
  <PackageReference Include="Localisation.Avalonia" Version="3.0.0" />
  <PackageReference Include="Localisation.Generators" Version="3.0.0"
                    PrivateAssets="all" />
</ItemGroup>
```

These examples pin the initial V3 release; use the V3 patch version selected for your application consistently. The generator runs during compilation; consumers do not call generated implementation classes or manually register generated providers.

### 2. Add translated resources

This walkthrough uses an application with assembly name and root namespace `MyApp`. Create ordinary string RESX files with build action `EmbeddedResource`:

| File | Culture | Example `Greeting` value |
| --- | --- | --- |
| `Strings.resx` | Neutral English | `Hello` |
| `Strings.fr.resx` | French | `Bonjour` |
| `Strings.de.resx` | German | `Hallo` |
| `Strings.zh-Hans.resx` | Simplified Chinese | `你好` |
| `Strings.ar.resx` | Arabic | `مرحباً` |

Also add `SavedFile` (for example, `Saved {0}`), `Light`, and `Dark` to the neutral file and translate them in the other files. Keep resource keys identical across cultures and preserve composite-format placeholders such as `{0}`.

```xml
<PropertyGroup>
  <AssemblyName>MyApp</AssemblyName>
  <RootNamespace>MyApp</RootNamespace>
  <NeutralLanguage>en</NeutralLanguage>
  <LocalisationGenerateOnly>true</LocalisationGenerateOnly>
</PropertyGroup>
```

The resource base name is `MyApp.Strings`, without a culture suffix or `.resources`. For a file under `Properties/Strings.resx`, it would normally be `MyApp.Properties.Strings`. Custom resource metadata can change this name.

`LocalisationGenerateOnly` removes binary resources and satellite assemblies while retaining generated lookups. Omit it if existing code still reads these files through `ResourceManager` or RESX designer properties. Generated providers register when their assembly loads. For resources in a separate assembly, call a strongly referenced startup method in that assembly before creating localized controls; a resource-name string alone cannot load it.

Lookup tries the requested culture, its parents, then the neutral file: `fr-CA` can use `Strings.fr.resx`. Missing translations therefore fall back to English in this example. Core's `Get` returns the key when no string is found; markup extensions can supply `DefaultValue`. The library does not generate translations or limit users to the cultures you ship.

### 3. Create an application context

Own one context for the application's interface, or separate contexts for independently localized views. This application-owned helper makes the context accessible to views and view models:

```csharp
using CP.Localisation;

namespace MyApp;

public static class AppText
{
    public static LocalisationContext Context { get; } =
        new LocalisationContext("MyApp", "MyApp.Strings");
}
```

After the resource assembly loads, call `AppText.Context.Initialize("Greeting")` during startup. It reports a missing provider or required key early. Dispose the context when its owner shuts down, after its consumers have finished.

Use `AppText.Context.Get("Greeting")` for text and `AppText.Context.Format("SavedFile", "report.pdf")` for formatted text. `SetCulture` changes only this context. `InterfaceCulture.Apply` also changes ambient UI cultures; neither changes number or date formatting cultures.

### 4. Display text in Avalonia

For generated, reflection-free resource lookup, use `GeneratedResx`:

```xml
<UserControl
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:loc="https://github.com/ChrisPulman/Localisation.Avalonia"
    loc:ResxExtension.DefaultResxName="MyApp.Strings">
    <TextBlock Text="{loc:GeneratedResx Greeting}" />
</UserControl>
```

This markup observes the UI package's `CultureManager`. To bind directly to your independent context, use the typed binding helper in view code:

```csharp
using Avalonia.Controls;
using CP.Localisation.Avalonia;

var greeting = new TextBlock
{
    [!TextBlock.TextProperty] = LocalisationBinding.Create(
        AppText.Context, AppText.Context,
        static (text, culture) => text.Get("Greeting", culture)),
};
```

The binding evaluates initially and again on context culture changes. A plain string captured during construction does not update itself. For `.Reactive`, use that package's XML namespace and `CP.Localisation.Avalonia.Reactive` C# namespace throughout the UI examples.

### 5. Switch language immediately

In your Avalonia `App`, capture the desktop culture before applying a saved preference. During startup, before creating the main window:

```csharp
using System;
using System.Globalization;
using CP.Localisation;
using CP.Localisation.Avalonia;

// In App startup; supply savedLanguage from your own settings storage.
var desktopCulture = CultureInfo.CurrentUICulture;
string? savedLanguage = null;
var culture = InterfaceCulture.Resolve(
    Environment.GetCommandLineArgs(), savedLanguage, desktopCulture);

AppText.Context.Initialize("Greeting");
InterfaceCulture.Apply(AppText.Context, culture);
LocalisationWindows.ApplyDirection(this, AppText.Context); // Install once.
LocalisationWindows.Refresh(this, AppText.Context, culture);
```

In the same `App` class, call this method from your language selector on the UI thread:

```csharp
public void ChangeLanguage(string cultureName)
{
    var culture = CultureInfo.GetCultureInfo(cultureName);
    InterfaceCulture.Apply(AppText.Context, culture);
    LocalisationWindows.Refresh(this, AppText.Context, culture);
    // Persist culture.Name using the application's settings service.
}
```

For example, select `en`, `fr`, `de`, `zh-Hans`, or `ar`. `Resolve` accepts `--language=fr` and `--language fr`; the last command-line override wins, followed by the saved preference and then the captured desktop culture. An empty or invalid selected name uses the desktop culture. Populate supported language choices yourself, or inspect `LocalisationResourceRegistry.GetAvailableCultures("MyApp")` for registered culture metadata.

`ApplyDirection` updates window direction for languages such as Arabic. Preserve explicit direction overrides for content such as documents where appropriate. `Refresh` synchronizes the selected UI package's `CultureManager`, refreshes context-owned string lists, and visits existing desktop windows' data contexts and item sources. It does not open windows or recreate view models. Outside a desktop application lifetime, synchronize the culture/choices yourself and call `LocalisationWindows.Refresh(window)` for each existing window.

### 6. Refresh computed properties

A computed property still needs a property-change notification after switching culture. Implement `ILocalisationChildren` on participating models and name the properties and existing children explicitly:

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using CP.Localisation;

public sealed class SettingsViewModel : INotifyPropertyChanged, ILocalisationChildren
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title => MyApp.AppText.Context.Get("Greeting");

    public object? ExistingChild { get; set; }

    public void Refresh(HashSet<object> visited)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        LocalisationRefresh.RefreshCore(ExistingChild, visited);
    }
}
```

`LocalisationWindows.Refresh` starts this traversal for existing desktop window models. For a UI-independent graph, call `LocalisationRefresh.Refresh(root)`. Shared children and cycles are processed once by reference. Pass the supplied visited set to child calls and avoid evaluating lazy properties merely to refresh them. Use Avalonia compiled bindings for your view-model properties in an AOT application.

### 7. Translate messages and choices

Retain a deferred message when its text must be resolved in the currently selected language:

```csharp
var saved = LocalisationMessage.Format(
    AppText.Context, "SavedFile", "report.pdf");
string currentText = saved.ToString();
// Reading saved.ToString() after a language change resolves the new translation.
```

`Get`, `Format`, `Literal`, and `Concat` preserve resource identities and nested messages. Use `Literal` for user-entered text or document content. A previously produced string remains a snapshot; notify or bind the property that displays the message. Format arguments use `CurrentCulture` for numbers and dates.

For an Avalonia selector, create stable items and a culture-aware template once:

```csharp
var labels = AppText.Context.CreateChoices("Light", "Dark");
var items = LocalisationChoices.CreateItems(AppText.Context, labels.ResourceKeys);
var themeSelector = new ComboBox
{
    ItemsSource = items,
    ItemTemplate = LocalisationChoices.CreateTemplate(),
    SelectedIndex = 0,
};
```

Labels change without replacing item identities or losing selection. Store the selected resource key or application value, rather than its translated label. For ordinary string-list consumers, call `context.RefreshChoices()` after selecting a culture; the window refresh helper already does this. Directly constructed `LocalisationStringList` instances require their own `Refresh()` call. See [localized enums](#localized-enums) and the [generic AOT-safe converter](#native-aot-and-trimming) for enum-valued choices.

### 8. Publish with trimming and Native AOT

Keep `GeneratedResx`, typed bindings, registered providers, and generic enum converters on the native path. Supply explicit converters for custom target types. The original Avalonia `Resx` and reflection-based enum APIs remain for compatibility and can produce trimming/AOT warnings. Non-string RESX entries require an [explicit resource provider](#native-aot-and-trimming).

For an Avalonia desktop project configured for Native AOT, publish for a concrete runtime, with the matching native toolchain installed:

```powershell
dotnet publish MyApp.csproj -c Release -r win-x64 -p:PublishAot=true
```

Review trimming/AOT diagnostics for the whole application and test the published executable, including live changes between Latin, Chinese, and Arabic text, font availability, fallback, existing dialogs, selections, and writing direction. WPF itself does not support Native AOT or trimming. The library's [AOT verification instructions](#native-aot-and-trimming) describe its own smoke consumers.

If text stays in the old language, check that the language handler calls both `InterfaceCulture.Apply` and `LocalisationWindows.Refresh` on the UI thread, uses the same context/package variant as the views, and notifies computed properties. If a key appears instead of text, check the assembly name, resource base name, key spelling, generator reference in the resource-owning project, and resource assembly startup reference.

## Migration from V1 to V2 and V3

V2 replaces the lean package's direct System.Reactive dependency with [ReactiveUI.Primitives](https://www.nuget.org/packages/ReactiveUI.Primitives) and adds separate `*.Reactive` packages for applications that use System.Reactive/Rx APIs.

The source-breaking changes are:

- `CultureManager.UICultureChangedObserver` is now `IObservable<ReactiveUI.Primitives.RxVoid>` in the lean packages. Use the matching `*.Reactive` package if the application requires `IObservable<System.Reactive.Unit>`.
- The old `GetResourceHandler` delegate was removed. `ResxExtension.GetResource` is now `EventHandler<GetResourceEventArgs>`; assign the override value to `args.Resource`.
- The System.Reactive variants use distinct package names, assemblies, and namespaces, so lean and Rx-first variants can coexist without type collisions.
- Avalonia support is supplied by the new `Localisation.Avalonia` and `Localisation.Avalonia.Reactive` packages.

V1 resource override:

```csharp
// No longer compiles in V2:
// ResxExtension.GetResource += (resxName, key, culture) => "Override";
```

V2 resource override:

```csharp
ResxExtension.GetResource += (_, args) =>
{
    if (args.Key == "Greeting")
    {
        args.Resource = "Override";
    }
};
```

## Choose a package

| UI framework | Default package and namespace | System.Reactive package and namespace | Culture notification value |
| --- | --- | --- | --- |
| WPF | `Localisation.WPF` / `CP.Localisation` | `Localisation.WPF.Reactive` / `CP.Localisation.Reactive` | `RxVoid` / `Unit` |
| Avalonia | `Localisation.Avalonia` / `CP.Localisation.Avalonia` | `Localisation.Avalonia.Reactive` / `CP.Localisation.Avalonia.Reactive` | `RxVoid` / `Unit` |

Use a default package for new applications, BCL `IObservable<T>` pipelines, ReactiveUI.Primitives signals, or R3 bridge boundaries. Use a `*.Reactive` package when existing code exposes System.Reactive `Unit`, uses Rx operators, or expects System.Reactive conventions.

Do not mix lean and `.Reactive` operators in one pipeline. Select one variant for the application and convert only at a deliberate package boundary.

Install one package for the selected UI framework and reactive model:

```powershell
dotnet add package Localisation.WPF
dotnet add package Localisation.WPF.Reactive
dotnet add package Localisation.Avalonia
dotnet add package Localisation.Avalonia.Reactive
```

The WPF packages target .NET Framework 4.6.2 and .NET 8, 9, 10, and 11 Windows targets. The Avalonia packages target .NET 8, 9, 10, and 11.

## WPF quick start

Add a RESX file such as `Properties/Resources.resx` and culture-specific files such as `Resources.fr-FR.resx`. A resource base name is the fully qualified resource name without `.resources`, for example `MyApp.Properties.Resources`.

Set the initial culture in `App.xaml.cs`:

```csharp
using System.Globalization;
using CP.Localisation;

public partial class App
{
    public App()
    {
        CultureManager.UICulture = CultureInfo.GetCultureInfo("en-GB");
    }
}
```

Add the localisation namespace, the inherited default resource base name, the dynamic language, and resource markup:

```xml
<Window
    x:Class="MyApp.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:loc="clr-namespace:CP.Localisation;assembly=Localisation.WPF"
    x:Name="Root"
    Language="{loc:UICulture}"
    loc:ResxExtension.DefaultResxName="MyApp.Properties.Resources">

    <StackPanel>
        <TextBlock Text="{loc:Resx Greeting}" />

        <Button Content="{loc:Resx Save,
                                  ResxName=MyApp.Properties.Resources,
                                  DefaultValue=Save}" />

        <!-- TotalFormat can contain: Total: {0:N2} -->
        <TextBlock Text="{loc:Resx TotalFormat,
                                  BindingElementName=Root,
                                  BindingPath=DataContext.Total}" />
    </StackPanel>
</Window>
```

`DefaultResxName` is inherited by descendants. An explicit `ResxName` takes precedence. Changing `CultureManager.UICulture` automatically refreshes `ResxExtension` and `UICultureExtension` targets.

For `Localisation.WPF.Reactive`, change the XAML namespace to:

```xml
xmlns:loc="clr-namespace:CP.Localisation.Reactive;assembly=Localisation.WPF.Reactive"
```

## Avalonia quick start

Set the culture from application startup or the UI thread:

```csharp
using System.Globalization;
using CP.Localisation.Avalonia;

CultureManager.UICulture = CultureInfo.GetCultureInfo("en-GB");
```

Use the Avalonia XML namespace and the same RESX base-name convention:

```xml
<UserControl
    x:Class="MyApp.MainView"
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:loc="https://github.com/ChrisPulman/Localisation.Avalonia"
    xml:lang="{loc:UICulture}"
    loc:ResxExtension.DefaultResxName="MyApp.Properties.Resources">

    <StackPanel>
        <TextBlock Text="{loc:Resx Greeting}" />
        <TextBlock Text="{loc:Resx Greeting, BindingStringFormat='Value: {0}'}" />
        <TextBlock Text="{loc:Resx Missing, DefaultValue='Not available'}" />
    </StackPanel>
</UserControl>
```

For `Localisation.Avalonia.Reactive`, use `https://github.com/ChrisPulman/Localisation.Avalonia.Reactive` or the CLR namespace `CP.Localisation.Avalonia.Reactive` from assembly `Localisation.Avalonia.Reactive`.

Avalonia refresh notifications are published on the calling thread. Invoke culture and refresh APIs on the UI thread when live controls are bound.

## Changing and observing culture

`SynchronizeThreadCulture` defaults to `true`. Assigning `UICulture` then updates the current and default thread culture values as well as localized targets. Assigning the same culture name is ignored.

```csharp
using System;
using System.Globalization;
using CP.Localisation; // Or CP.Localisation.Avalonia.
using ReactiveUI.Primitives;

public sealed class LocalisationHost : IDisposable
{
    private readonly EventHandler _handler;
    private readonly IDisposable _subscription;

    public LocalisationHost()
    {
        CultureManager.SynchronizeThreadCulture = true;
        CultureManager.UICulture = CultureInfo.GetCultureInfo("en-GB");

        _handler = (_, _) => ReloadCultureDependentData();
        CultureManager.UICultureChanged += _handler;

        _subscription = CultureManager.UICultureChangedObserver.Subscribe(
            new ActionObserver<RxVoid>(_ => ReloadCultureDependentData()));
    }

    public void SwitchToFrench() =>
        CultureManager.UICulture = CultureInfo.GetCultureInfo("fr-FR");

    public void Dispose()
    {
        CultureManager.UICultureChanged -= _handler;
        _subscription.Dispose();
    }

    private static void ReloadCultureDependentData()
    {
    }

    private sealed class ActionObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
```

Static events and observable subscriptions retain callbacks. Always unsubscribe and dispose them when the owning object is released.

`SyncCultureInfo()` affects only the calling thread. It is useful when automatic synchronization is disabled:

```csharp
using System.Globalization;
using CP.Localisation;

var culture = CultureInfo.GetCultureInfo("fr-FR");

CultureManager.SynchronizeThreadCulture = false;
CultureManager.UICulture = culture; // Changes resource resolution and targets.
culture.SyncCultureInfo();          // Changes this thread's cultures.
```

WPF silently ignores a null `UICulture` assignment. Avalonia throws `ArgumentNullException`. Avalonia also exposes `CultureManager.Refresh()` to force all culture-aware bindings to publish again without changing culture.

## System.Reactive applications and `*.Reactive`

The `.Reactive` packages compile the same localisation implementation against `System.Reactive.Unit` and System.Reactive scheduling conventions. They are package variants, not runtime adapters.

WPF Rx example:

```csharp
using System;
using CP.Localisation.Reactive;
using System.Reactive;
using System.Reactive.Linq;

IObservable<Unit> changes = CultureManager.UICultureChangedObserver;
using IDisposable subscription = changes.Subscribe(
    _ => Console.WriteLine($"Culture: {CultureManager.UICulture.Name}"));
```

Avalonia uses the same pattern with `using CP.Localisation.Avalonia.Reactive;`.

The `.Reactive` projects share their implementation with the lean projects. Their public members are identical except for the namespace and the `RxVoid` to `Unit` substitution on `UICultureChangedObserver`.

## R3 bridge

Use the ReactiveUI.Primitives R3 bridge with a lean localisation package when an application has an R3 boundary. Add `R3`; the bridge analyzer supplied with ReactiveUI.Primitives emits adapters only when the consuming compilation can see the required R3 symbols.

```powershell
dotnet add package Localisation.WPF
dotnet add package R3
```

Convert the culture notification at the boundary:

```csharp
using System;
using CP.Localisation;
using R3;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.R3Bridge;

Observable<RxVoid> r3CultureChanges =
    CultureManager.UICultureChangedObserver.AsR3Observable();

using IDisposable subscription = r3CultureChanges.Subscribe(
    _ => Console.WriteLine(CultureManager.UICulture.Name));
```

The reverse adapter converts an R3 source to a Primitives/BCL observable:

```csharp
using System;
using ReactiveUI.Primitives.R3Bridge;

R3.Observable<int> r3Source = R3.Observable.Return(42);
IObservable<int> primitivesSource = r3Source.AsPrimitivesSignal();
```

The generated methods are:

- `AsR3Observable<T>(this IObservable<T>)`
- `AsPrimitivesSignal<T>(this R3.Observable<T>)`

Use bridge calls only at boundaries and keep the rest of a pipeline in one reactive model. Do not add `ReactiveUI.Primitives.R3Bridge.Generator` directly.

The localisation libraries do not expose async-observable APIs. If the consuming application independently references `ReactiveUI.Primitives.Async`, the bridge emits additional methods when the required external symbols are visible:

- With `R3.Observable<T>`: `AsPrimitivesAsyncObservable<T>(this R3.Observable<T>)` and `AsR3Observable<T>(this IObservableAsync<T>)`.
- With `R3Async.AsyncObservable<T>`, `R3Async.AsyncObserver<T>`, and `R3Async.Result`: `AsPrimitivesAsyncObservable<T>(this R3Async.AsyncObservable<T>)` and `AsR3AsyncObservable<T>(this IObservableAsync<T>)`.

Keep these async conversions at application or package boundaries too.

## Dynamic resource providers

`GetResource` runs before embedded RESX lookup. Set `args.Resource` to a non-null value to supply a dynamic resource; leave it null to continue with normal lookup.

```csharp
using System;
using CP.Localisation; // Or the selected Avalonia/.Reactive namespace.

private static readonly EventHandler<GetResourceEventArgs> ResourceOverride =
    (_, args) =>
    {
        if (args.ResxName == "MyApp.Properties.Resources" &&
            args.Key == "Greeting")
        {
            args.Resource = $"Hello from {args.Culture.Name}";
        }
    };

ResxExtension.GetResource += ResourceOverride;
try
{
    ResxExtension.UpdateTarget("Greeting");
}
finally
{
    ResxExtension.GetResource -= ResourceOverride;
}
```

`GetResourceEventArgs` can also be created directly for provider tests:

```csharp
var args = new GetResourceEventArgs(
    "MyApp.Properties.Resources",
    "Greeting",
    CultureManager.UICulture)
{
    Resource = "Test value",
};
```

## Native AOT and trimming

Use `Localisation.Generators` in every project that owns RESX files. It compiles string resources into C# lookup code and registers them at module initialization; runtime lookup requires no assembly scanning, resource deserialization, or reflection. `Localisation.Core` contains the shared provider registry.

Load a separate resource assembly before creating localized controls, for example by calling its strongly referenced startup method. A `ResxName` string alone does not load an assembly. This also gives the trimmer a static reference to the generated providers.

```powershell
dotnet add package Localisation.Generators
```

Keep the RESX files as ordinary `EmbeddedResource` items. The package's build targets supply resource names and culture metadata automatically, including custom manifest names and linked resources. Set `NeutralLanguage` to the culture represented by the neutral RESX file. Culture lookup tries the requested culture, its parents, then neutral resources. Missing keys follow the extension's existing default-value behavior.

Projects that use only generated lookups can omit binary RESX resources and satellite assemblies:

```xml
<PropertyGroup>
  <LocalisationGenerateOnly>true</LocalisationGenerateOnly>
</PropertyGroup>
```

The targets capture resource metadata before removing RESX inputs from binary resource compilation. Generated lookups retain ordinal, case-sensitive keys, including keys that differ only by case. Enable this option only when the project does not use `ResourceManager` or generated RESX designer properties to read those resources. Culture comparisons are ordinal and case-insensitive without allocating normalized culture names. Each culture has its own generated lookup method to keep native compilation manageable for large translation catalogs. Dictionary-only lookups read a sorted provider snapshot without allocating per lookup; registration pays the sorting cost.

For Avalonia, use `GeneratedResxExtension` from the namespace of your selected lean or `.Reactive` package:

```xml
<TextBlock Text="{loc:GeneratedResx Greeting, ResxName=MyApp.Properties.Resources}" />
```

It shares inherited `DefaultResxName`, culture switching, `GetResource`, `UpdateTarget`, `UpdateAllTargets`, `BindingConverter`, and `BindingStringFormat` with `ResxExtension`. Supply fallback values compatible with the target property, or an explicit `BindingConverter` for custom conversion. The generated path never discovers `TypeConverter` implementations automatically. Existing `ResxExtension` and enum APIs retain their behavior; reflection-dependent entry points report trimming/AOT analyzer warnings and should be avoided in native applications.

For images, objects, external data, or custom resource formats, register a statically referenced provider:

```csharp
using CP.Localisation;

LocalisationResourceRegistry.Register(
    "MyApp",
    "MyApp.CustomResources",
    static (key, culture) => key == "Greeting" ? "Hello" : null,
    new[] { "en-US" });

bool found = LocalisationResourceRegistry.TryGetResource(
    "MyApp", "MyApp.CustomResources", "Greeting",
    CultureInfo.GetCultureInfo("en-US"), out object? value);

var cultures = LocalisationResourceRegistry.GetAvailableCultures("MyApp");
```

Providers implement their own fallback and return null for missing keys. Registration replaces a provider and its advertised cultures atomically. Non-string RESX entries produce `LOCGEN002`; malformed files and invalid metadata produce `LOCGEN001`. Move non-string values into a dedicated explicit provider rather than relying on resource deserialization in native applications.

WPF and WPF.Reactive also prefer registered resources while retaining their existing lookup fallback. WPF itself does not support trimming or Native AOT; see Microsoft's [known trimming incompatibilities](https://learn.microsoft.com/dotnet/core/deploying/trimming/incompatibilities). Use the Avalonia packages for a native UI application.

The repository verifies real trimmed and native executables on Windows:

```powershell
pwsh -File ./build/Verify-Aot.ps1
```

This requires the Native AOT C++ build tools. The smoke consumer checks generated resource lookup and culture fallback, live bindings, refresh notifications, and generic enum conversion in both Avalonia variants.
If the SDK cannot discover a newer Visual Studio installation, use `-UseEnvironmentalTools` to initialize the installed x64 C++ tools explicitly.

Use the generic enum converter for statically known enums in native applications:

```csharp
var converter = new ResourceEnumConverter<OrderStatus>("MyApp.Properties.Resources");
string text = converter.ConvertToString(OrderStatus.Pending);
OrderStatus? value = converter.ConvertFromString(text);
var choices = converter.GetValues(CultureInfo.GetCultureInfo("fr-FR"));
```

Pass `isFlags: true` for a flags enum. A third constructor argument accepts a key-building delegate when `<EnumTypeName>_<Value>` does not match your keys. The converter implements Avalonia's `IValueConverter` directly and can be assigned explicitly to a binding.

## Reusable live localisation

`Localisation.Core` also provides resource-scoped culture contexts, deferred messages and fixed translated string lists. Resources stay in the consuming application. These APIs resolve registered providers without reflection and do not depend on a UI toolkit or ReactiveUI.

```csharp
using System.Globalization;
using CP.Localisation;
using CP.Localisation.Avalonia;

using var context = new LocalisationContext("MyApp", "MyApp.Strings");
var message = LocalisationMessage.Format(context, "SavedFile", "report.pdf");
var labels = context.CreateChoices("Light", "Dark");
var items = LocalisationChoices.CreateItems(context, labels.ResourceKeys);
comboBox.ItemsSource = items;
comboBox.ItemTemplate = LocalisationChoices.CreateTemplate();

context.SetCulture(CultureInfo.GetCultureInfo("fr"));
context.RefreshChoices(); // Notify observers of string labels, when used outside the selector.
string status = message.ToString();
```

`LocalisationContext.CultureChanges` replays the current culture and publishes subsequent changes. Until the first `SetCulture`, lookup follows the caller's `CurrentUICulture`. Once selected, the context's culture is independent of captured asynchronous execution contexts. Contexts can use different languages simultaneously. Setting a context culture never changes thread cultures, number/date formatting, the legacy `CultureManager`, or another context. The application owns any bridge to those policies. Dispose a context when its owner ends; existing subscriptions complete. Resource lookup remains available after disposal, but further culture changes are rejected.

`LocalisationMessage.Get`, `Format`, `Literal` and `Concat` retain resource identities and nested messages until their value is read. Literal document or user text remains literal; composite arguments use the caller's `CurrentCulture`. `LocalisationStringList` copies its keys, exposes immutable `ResourceKeys`, and implements read-only `IList` plus collection replacement notifications through explicit `Refresh()`. `GetResourceKeys` accepts either a translated list or an ordinary key list. A context's `CreateChoices` weakly tracks its lists for `RefreshChoices`; direct construction leaves refresh ownership with the caller. Call refresh on the collection observers' owning thread. `Initialize(probeKey)` validates an application-owned resource key before controls are constructed.

For Avalonia, `LocalisationBinding.Create(context, state, selector)` creates a typed culture-driven binding. For example, bind `FlowDirection` through a selector using `culture.TextInfo.IsRightToLeft`. Bindings run on the publishing thread; UI consumers should select the context culture on their UI thread. `LocalisationChoices` supplies stable items and a live label template, so language changes retain selection and localized keyboard search. `CreateItemsConverter(context)` creates a cache owned by one binding; reuse immutable key-list references to retain its item identities. The same helpers are available in `CP.Localisation.Avalonia.Reactive`.

`ILocalisationChildren` and `LocalisationRefresh` provide reflection-free graph refresh. Implement `Refresh(HashSet<object> visited)` to notify the properties your model owns and pass existing children to `LocalisationRefresh.RefreshCore(child, visited)`. Start a traversal with `LocalisationRefresh.Refresh(root)`. Shared children and cycles are visited once by reference; strings are excluded, enumerable items are visited, and no properties or lazy factories are discovered. Models retain control over which state participates.

`InterfaceCulture.Resolve(args, savedLanguage, desktopCulture)` resolves the last `--language=name` or `--language name` override, then a saved culture, then the desktop fallback. Empty or invalid names follow the desktop. `InterfaceCulture.Apply(context, culture)` changes `CurrentUICulture` and `DefaultThreadCurrentUICulture` before publishing the context culture, preserving number and date cultures. Applications supply their own settings storage, supported-language choices and captured desktop preference.

For Avalonia, `LocalisationWindows.ApplyDirection(application, context)` installs a live window direction style. `LocalisationWindows.Refresh(application, context, culture)` synchronizes existing generated resource bindings, refreshes context-owned choices, and traverses data contexts and item sources in existing desktop windows. `Refresh(window)` supports hosts without a desktop application lifetime. Both Avalonia package variants provide these helpers. Window creation/lifetime, explicit model notifications, translations, styling and document-specific direction overrides remain consumer responsibilities.

## Localized enums

Enum resource keys use `<EnumTypeName>_<MemberName>`, for example `OrderStatus_Pending`.

```csharp
using System;
using System.ComponentModel;
using CP.Localisation; // Or CP.Localisation.Avalonia.

[TypeConverter(typeof(OrderStatusConverter))]
public enum OrderStatus
{
    Pending,
    Complete,
}

public sealed class OrderStatusConverter : ResourceEnumConverter
{
    public OrderStatusConverter()
        : this(typeof(OrderStatus))
    {
    }

    public OrderStatusConverter(Type type)
        : base(type, Properties.Resources.ResourceManager)
    {
    }
}
```

Use the converter and helper methods:

```csharp
using System.Globalization;

string? text = OrderStatus.Pending.ConvertToString();

var frenchValues = ResourceEnumConverter.GetValues(
    typeof(OrderStatus),
    CultureInfo.GetCultureInfo("fr-FR"));

var currentValues = ResourceEnumConverter.GetValues(typeof(OrderStatus));
```

The converter can also be used as a WPF or Avalonia binding converter:

```xml
<Window.Resources>
    <local:OrderStatusConverter x:Key="OrderStatusConverter" />
</Window.Resources>

<TextBlock Text="{Binding Status, Converter={StaticResource OrderStatusConverter}}" />
```

`[Flags]` values are joined and parsed as comma-separated localized names. Unknown flag text returns null. A defined value without a resource displays its generated resource key. Override `GetResourceName(object value)` in a derived converter to use a different naming convention.

## Resource fallback and conversion behavior

| Situation | WPF | Avalonia |
| --- | --- | --- |
| Missing string or `object` resource, no default | `#<Key>` | `#<Key>` |
| Missing typed resource, no usable default | `null` | `null` |
| `DefaultValue` type | `string?` | `object?` |
| Explicit resource override | `GetResourceEventArgs.Resource` | `GetResourceEventArgs.Resource` |
| Resource key assignment | Preserved | Trimmed |
| Invalid or whitespace targeted refresh key | No validation | `ArgumentException` |
| Null attached-property target | Returns null/no-op | `ArgumentNullException` |
| Refresh thread | WPF culture changes marshal target updates to the application dispatcher when available | Calling thread |

WPF converts string resources to non-string target types with the target type converter and converts `System.Drawing.Icon`/`Bitmap` resources to WPF image values. Avalonia applies `BindingConverter`, then `BindingStringFormat`, after resolving the resource or fallback.

## Complete public API reference

The `.Reactive` packages mirror the API below in their `.Reactive` namespace. Substitute `System.Reactive.Unit` for `ReactiveUI.Primitives.RxVoid` on `CultureManager.UICultureChangedObserver`; all other documented members retain the same shape.

### WPF API (`CP.Localisation`)

#### `CultureManager`

| Member | Documentation |
| --- | --- |
| `event EventHandler? UICultureChanged` | Raised after a real UI-culture change and target refresh. Because it is static, subscribers must unsubscribe. |
| `bool SynchronizeThreadCulture { get; set; }` | Controls whether `CurrentCulture` and `DefaultThreadCurrentCulture` follow `UICulture`. Setting it to `true` immediately synchronizes `CurrentCulture`. Default: `true`. |
| `CultureInfo UICulture { get; set; }` | Gets or changes the resource culture. Null and same-name assignments are ignored. A real change always updates `CurrentUICulture` and `DefaultThreadCurrentUICulture`, conditionally synchronizes `CurrentCulture`, refreshes WPF targets, and publishes both notification surfaces. |
| `IObservable<RxVoid> UICultureChangedObserver { get; }` | Observable notification after each real culture change. The `.Reactive` package exposes `IObservable<Unit>`. |

#### `Extensions`

| Member | Documentation |
| --- | --- |
| `void SyncCultureInfo(this CultureInfo culture)` | Sets `CurrentCulture` and `CurrentUICulture` on the calling thread. It does not change `CultureManager.UICulture`, defaults for new threads, or markup targets. |
| `string? ConvertToString(this Enum value)` | Resolves the enum's registered type converter and returns its localized string. |

#### `GetResourceEventArgs`

| Member | Documentation |
| --- | --- |
| `GetResourceEventArgs(string? resxName, string? key, CultureInfo culture)` | Creates event data. A null culture throws `ArgumentNullException`. |
| `CultureInfo Culture { get; }` | Culture requested by the localisation lookup. |
| `string? Key { get; }` | Requested resource key. |
| `string? ResxName { get; }` | Fully qualified RESX base name. |
| `object? Resource { get; set; }` | Dynamic value supplied by a `GetResource` handler. Leave null for normal RESX lookup. |

#### `ResxExtension`

| Member | Documentation |
| --- | --- |
| `static readonly DependencyProperty DefaultResxNameProperty` | Inheritable WPF attached property containing the default RESX base name. |
| `ResxExtension()` | Creates an extension whose key can be assigned through `Key`. |
| `ResxExtension(string key)` | Creates an extension for a resource key; enables `{loc:Resx Greeting}` syntax. |
| `static event EventHandler<GetResourceEventArgs>? GetResource` | Allows dynamic lookup before the embedded resource manager is queried. |
| `static MarkupExtensionManager MarkupManager { get; }` | Manager tracking active `ResxExtension` instances through weak target references. |
| `Binding Binding { get; }` | Lazily creates and returns the backing WPF binding used by the `Binding*` façade. |
| `Collection<ResxExtension> Children { get; }` | Child resource bindings used with the parent localized string as a `MultiBinding.StringFormat`. |
| `string? DefaultValue { get; set; }` | Fallback when lookup fails. It is converted to the target type when possible. |
| `string? Key { get; set; }` | Resource key. |
| `string? ResxName { get; set; }` | Explicit RESX base name, or the inherited `DefaultResxName` when unset. |
| `static string? GetDefaultResxName(DependencyObject target)` | Reads the attached default base name. A null runtime target returns null. |
| `static void SetDefaultResxName(DependencyObject target, string value)` | Sets the inheritable attached base name. A null runtime target is ignored. |
| `static void UpdateAllTargets()` | Re-evaluates every active WPF resource extension. |
| `static void UpdateTarget(string key)` | Re-evaluates active extensions whose key exactly equals `key`. |
| `override object ProvideValue(IServiceProvider serviceProvider)` | Registers the target and returns a direct resource, binding, multi-binding, or the extension for templates. Throws `ArgumentException` when neither a key nor a real binding expression is configured. |
| `protected override object GetValue()` | Resolves event override, embedded resource, and fallback in that order. A missing key returns a new object. |
| `protected override void UpdateTarget(object target)` | Rebuilds binding/multi-binding targets or delegates direct-value updates to the base class. |

The WPF binding façade forwards these properties to `Binding`:

| Property | Forwarded WPF binding member |
| --- | --- |
| `BindingAsyncState` | `Binding.AsyncState` |
| `BindingConverter` | `Binding.Converter` |
| `BindingConverterCulture` | `Binding.ConverterCulture` |
| `BindingConverterParameter` | `Binding.ConverterParameter` |
| `BindingElementName` | `Binding.ElementName` |
| `BindingFallbackValue` | `BindingBase.FallbackValue` |
| `BindingGroupName` | `BindingBase.BindingGroupName` |
| `BindingIsAsync` | `Binding.IsAsync` |
| `BindingMode` | `Binding.Mode` |
| `BindingNotifyOnSourceUpdated` | `Binding.NotifyOnSourceUpdated` |
| `BindingNotifyOnTargetUpdated` | `Binding.NotifyOnTargetUpdated` |
| `BindingNotifyOnValidationError` | `Binding.NotifyOnValidationError` |
| `BindingPath` | `Binding.Path` |
| `BindingRelativeSource` | `Binding.RelativeSource` |
| `BindingSource` | `Binding.Source` |
| `BindingTargetNullValue` | `BindingBase.TargetNullValue` |
| `BindingUpdateSourceTrigger` | `Binding.UpdateSourceTrigger` |
| `BindingValidatesOnDataErrors` | `Binding.ValidatesOnDataErrors` |
| `BindingValidatesOnExceptions` | `Binding.ValidatesOnExceptions` |
| `BindingValidationRules` | Mutable `Binding.ValidationRules` collection |
| `BindingXPath` | `Binding.XPath` |
| `BindsDirectlyToSource` | `Binding.BindsDirectlyToSource` |

A binding expression is selected only when `BindingSource`, `BindingRelativeSource`, `BindingElementName`, `BindingXPath`, or `BindingPath` is set. The resolved resource becomes `Binding.StringFormat`. Setting only a converter or binding mode does not create a binding expression.

WPF resource-format multi-binding example:

```xml
<TextBlock>
    <TextBlock.Text>
        <!-- WelcomeFormat: Welcome, {0}! -->
        <loc:Resx Key="WelcomeFormat">
            <loc:Resx.Children>
                <loc:Resx Key="UserName" />
            </loc:Resx.Children>
        </loc:Resx>
    </TextBlock.Text>
</TextBlock>
```

#### `UICultureExtension`

| Member | Documentation |
| --- | --- |
| `UICultureExtension()` | Creates a WPF markup extension for the current culture's `XmlLanguage`. |
| `static MarkupExtensionManager MarkupManager { get; }` | Tracks active language targets. |
| `static void UpdateAllTargets()` | Forces all tracked language targets to update. |
| `protected override object GetValue()` | Returns `XmlLanguage.GetLanguage(CultureManager.UICulture.IetfLanguageTag)`. |

```csharp
UICultureExtension.UpdateAllTargets();
var trackedLanguageTargets = UICultureExtension.MarkupManager.ActiveExtensions.Count;
```

#### `ResourceEnumConverter`

| Member | Documentation |
| --- | --- |
| `ResourceEnumConverter(Type type, ResourceManager resourceManager)` | Creates an enum/type and WPF binding converter using the supplied resources. |
| `static string? ConvertToString(Enum value)` | Uses the enum's registered type converter to produce display text. |
| `static List<KeyValuePair<Enum, string?>> GetValues(Type enumType, CultureInfo culture)` | Returns every defined value with text for the specified culture. |
| `static List<KeyValuePair<Enum, string?>> GetValues(Type enumType)` | Uses `CultureInfo.CurrentUICulture`. |
| `override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)` | Converts localized text to an enum. Supports comma-separated flags; delegates non-string values to `EnumConverter`. |
| `override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)` | Converts an enum to localized text/object; delegates other destination types to `EnumConverter`. |
| `IValueConverter.Convert(...)` | WPF binding entry point; delegates to `ConvertTo`. |
| `IValueConverter.ConvertBack(...)` | WPF binding entry point; delegates to `ConvertFrom`. |
| `protected virtual string GetResourceName(object value)` | Returns `<EnumTypeName>_<Value>`; override to customize keys. |

#### `ManagedMarkupExtension`

This is advanced WPF extension infrastructure. Application code normally composes the built-in markup extensions rather than deriving from it.

| Member | Documentation |
| --- | --- |
| `protected ManagedMarkupExtension(MarkupExtensionManager manager)` | Associates a custom extension with a manager. Null throws `ArgumentNullException`. |
| `bool IsTargetAlive { get; }` | True when at least one weak target is alive, or when no concrete target exists yet for a template. |
| `bool IsTarget(object target)` | Tests whether an alive tracked weak reference points to `target`. |
| `override object ProvideValue(IServiceProvider serviceProvider)` | Registers the target and returns `GetValue()`, or the extension itself for unresolved/template targets. |
| `void UpdateTargets()` | Calls `UpdateTarget` for every live target. |
| `protected List<WeakReference> TargetObjects { get; }` | Weak target references available to derived extensions. |
| `protected object? TargetProperty { get; }` | Target dependency property or CLR `PropertyInfo`. |
| `protected Type? TargetPropertyType { get; }` | Resolved target property type. |
| `protected abstract object GetValue()` | Derived value factory. |
| `protected virtual void RegisterTarget(IServiceProvider serviceProvider)` | Registers the extension once, discovers its target, and records a weak reference. |
| `protected virtual void UpdateTarget(object target)` | Sets a dependency or CLR property to a newly resolved value. |

Minimal custom extension:

```csharp
public sealed class CurrentCultureNameExtension : ManagedMarkupExtension
{
    private static readonly MarkupExtensionManager Manager = new(cleanupInterval: 20);

    public CurrentCultureNameExtension()
        : base(Manager)
    {
    }

    protected override object GetValue() => CultureManager.UICulture.DisplayName;
}
```

#### `MarkupExtensionManager`

| Member | Documentation |
| --- | --- |
| `MarkupExtensionManager(int cleanupInterval)` | Creates a manager and configures how many registrations occur between automatic dead-target cleanup passes. |
| `List<ManagedMarkupExtension> ActiveExtensions { get; }` | Current managed extensions. Treat the mutable list as manager-owned. |
| `void CleanupInactiveExtensions()` | Removes extensions whose weak targets have been collected. |
| `virtual void UpdateAllTargets()` | Updates a snapshot of all active extensions. |

```csharp
var manager = ResxExtension.MarkupManager;
manager.CleanupInactiveExtensions();
manager.UpdateAllTargets();
```

#### `CultureSelectWindow`

| Member | Documentation |
| --- | --- |
| `CultureSelectWindow()` | Creates the WPF culture-selection window, loads specific cultures, sorts them by display name, and selects `CultureManager.UICulture`. Intended for the design/debug culture selector. |

```csharp
var selector = new CultureSelectWindow();
selector.Show();
```

### Avalonia API (`CP.Localisation.Avalonia`)

#### `CultureManager`

| Member | Documentation |
| --- | --- |
| `event EventHandler? UICultureChanged` | Raised after a real culture change or an explicit `Refresh()`. Unsubscribe static handlers. |
| `bool SynchronizeThreadCulture { get; set; }` | Controls current/default thread synchronization. Enabling it immediately applies the selected UI culture. Default: `true`. |
| `CultureInfo UICulture { get; set; }` | Gets or changes resource culture. Null throws `ArgumentNullException`; same-name assignments are ignored. |
| `IObservable<RxVoid> UICultureChangedObserver { get; }` | Publishes after changes and explicit refreshes. The `.Reactive` package uses `Unit`. |
| `static void Refresh()` | Re-publishes the event and observable without changing culture. |

```csharp
CultureManager.UICulture = CultureInfo.GetCultureInfo("de-DE");
CultureManager.Refresh();
```

#### `Extensions`

| Member | Documentation |
| --- | --- |
| `void SyncCultureInfo(this CultureInfo culture)` | Sets `CurrentCulture` and `CurrentUICulture` on the calling thread only. |
| `string? ConvertToString(this Enum value)` | Uses the registered enum converter to produce localized text. |

#### `GetResourceEventArgs`

| Member | Documentation |
| --- | --- |
| `GetResourceEventArgs(string? resxName, string? key, CultureInfo culture)` | Creates event data. A null culture throws `ArgumentNullException`. |
| `CultureInfo Culture { get; }` | Culture requested by the localisation lookup. |
| `string? Key { get; }` | Requested resource key. |
| `string? ResxName { get; }` | Fully qualified RESX base name. |
| `object? Resource { get; set; }` | Dynamic value supplied by a `GetResource` handler. Leave null for normal RESX lookup. |

#### `ResxExtension`

| Member | Documentation |
| --- | --- |
| `static readonly AttachedProperty<string?> DefaultResxNameProperty` | Inheritable Avalonia attached property for the default RESX base name. |
| `ResxExtension()` | Creates an extension whose properties can be assigned explicitly. |
| `ResxExtension(string key)` | Creates an extension for `key`. The assigned key is trimmed. |
| `static event EventHandler<GetResourceEventArgs>? GetResource` | Dynamic provider hook invoked before embedded-resource lookup. |
| `IValueConverter? BindingConverter { get; set; }` | Optional Avalonia converter applied after resource/fallback resolution. |
| `object? BindingConverterParameter { get; set; }` | Parameter supplied to `BindingConverter`. |
| `string? BindingStringFormat { get; set; }` | Composite format applied to the resolved value after conversion. |
| `object? DefaultValue { get; set; }` | Missing-resource fallback; converted to the target type when possible. |
| `string? Key { get; set; }` | Trimmed resource key. |
| `string? ResxName { get; set; }` | Explicit RESX base name; otherwise the attached default is used. |
| `static string? GetDefaultResxName(AvaloniaObject target)` | Reads the inherited default. A null target throws `ArgumentNullException`. |
| `static void SetDefaultResxName(AvaloniaObject target, string? value)` | Sets or clears the inherited default. A null target throws `ArgumentNullException`. |
| `static void UpdateAllTargets()` | Requests re-evaluation of every active resource binding. |
| `static void UpdateTarget(string key)` | Requests re-evaluation for one trimmed key. Null/empty/whitespace throws `ArgumentException`. |
| `object ProvideValue(IServiceProvider? serviceProvider)` | Returns an observable Avalonia binding that resolves again after culture or matching resource refresh. |

Attached property and refresh API example:

```csharp
using Avalonia.Controls;
using CP.Localisation.Avalonia;

var root = new Grid();
ResxExtension.SetDefaultResxName(root, "MyApp.Properties.Resources");

string? baseName = ResxExtension.GetDefaultResxName(root);
ResxExtension.UpdateTarget("Greeting");
ResxExtension.UpdateAllTargets();
```

Avalonia does not expose WPF's `Binding*` façade, `Children`, resource multi-binding, or public markup-manager infrastructure.

#### `UICultureExtension`

| Member | Documentation |
| --- | --- |
| `UICultureExtension()` | Implicit public constructor for the language-tag markup extension. |
| `static void UpdateAllTargets()` | Delegates to `CultureManager.Refresh()`. |
| `object ProvideValue(IServiceProvider? serviceProvider)` | Returns an observable Avalonia binding for `UICulture.IetfLanguageTag`. |

```csharp
UICultureExtension.UpdateAllTargets();
```

#### `ResourceEnumConverter`

| Member | Documentation |
| --- | --- |
| `ResourceEnumConverter(Type type, ResourceManager resourceManager)` | Creates an Avalonia enum converter. Null arguments throw `ArgumentNullException`. |
| `static string? ConvertToString(Enum value)` | Uses the registered enum converter. Null throws `ArgumentNullException`. |
| `static List<KeyValuePair<Enum, string?>> GetValues(Type enumType, CultureInfo culture)` | Returns all values and localized text. Null arguments throw. |
| `static List<KeyValuePair<Enum, string?>> GetValues(Type enumType)` | Uses `CurrentUICulture`. |
| `override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)` | Converts localized simple/flags text to an enum. |
| `override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)` | Converts enum values to localized text/object and returns null for null input. |
| `IValueConverter.Convert(...)` | Avalonia binding entry point delegating to `ConvertTo`. |
| `IValueConverter.ConvertBack(...)` | Avalonia binding entry point delegating to `ConvertFrom`. |
| `protected virtual string GetResourceName(object value)` | Returns `<EnumTypeName>_<Value>`; override to customize keys. |

## Build and test

The solution uses Microsoft Testing Platform with TUnit and TUnit assertions.

```powershell
dotnet build .\src\Localisation.slnx --configuration Release -m:1
dotnet test --solution .\src\Localisation.slnx --configuration Release --coverage --coverage-output-format cobertura --results-directory .\TestResults
pwsh -File .\build\Assert-Coverage.ps1 -ResultsDirectory .\TestResults
```

Release packages include portable PDBs in `.snupkg` files. Before upload, `build/Prepare-SymbolPackages.ps1` checks each library's PDB identity and checksum against its DLL. Resource satellite assemblies do not require PDBs.

For a partially published version, the release workflow preserves the existing NuGet package and recovers its matching symbols from that package. Primary packages and symbol packages are pushed separately, allowing missing symbols to be published even when the primary package already exists. To validate local packages without accessing NuGet.org:

```powershell
pwsh -File .\build\Prepare-SymbolPackages.ps1 -PackagesDirectory .\packages
```

## License

Localisation is licensed under the [MIT License](LICENSE).
