# Localisation for WPF and Avalonia

Culture-aware RESX markup extensions, runtime culture switching, resource overrides, and localized enum conversion for WPF and Avalonia.

![NuGet downloads](https://img.shields.io/nuget/dt/Localisation.WPF?color=pink&style=plastic)
[![Localisation.WPF](https://img.shields.io/nuget/v/Localisation.WPF.svg?style=plastic)](https://www.nuget.org/packages/Localisation.WPF)

## V2 is a breaking release

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

## License

Localisation is licensed under the [MIT License](LICENSE).
