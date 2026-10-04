// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WpfImage = System.Windows.Controls.Image;

#if REACTIVE_SHIM
namespace CP.Localisation.Reactive;
#else
namespace CP.Localisation;
#endif

/// <summary>
/// A markup extension to allow resources for WPF Windows and controls to be retrieved from an
/// embedded resource (resx) file associated with the window or control.
/// </summary>
/// <remarks>
/// <para>
/// Supports design time switching of the Culture via a Tray Notification icon. Loading of
/// culture specific satellite assemblies at design time (within the XDesProc designer
/// process) is done by probing the sub-directories associated with the running Visual Studio
/// hosting process (*.vshost) for the latest matching assembly. If you have disabled the hosting
/// process or are using Expression Blend then you can set the sub-directories to search at
/// design time by creating a string Value in the registry:
/// </para>
/// <para>HKEY_CURRENT_USER\Software\ResxExtension\AssemblyPath.</para>
/// <para>and set the value to a semi-colon delimited list of directories to search.</para>
/// </remarks>
[MarkupExtensionReturnType(typeof(object))]
[ContentProperty(nameof(Children))]
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public class ResxExtension : ManagedMarkupExtension
{
    /// <summary>The ResxName attached property.</summary>
    public static readonly DependencyProperty DefaultResxNameProperty =
        DependencyProperty.RegisterAttached(
        "DefaultResxName",
        typeof(string),
        typeof(ResxExtension),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits,
            new PropertyChangedCallback(OnDefaultResxNamePropertyChanged)));

    private const int CleanupInterval = 40;

    private const string ResourceFileSuffix = ".resources";

    /// <summary>The directories to probe for satellite assemblies when running inside the Visual Studio designer process (XDesProc).</summary>
    private static readonly List<string>? _assemblyProbingPaths;

    /// <summary>Cached resource managers.</summary>
    private static readonly Dictionary<string, WeakReference> _resourceManagers = [];

    /// <summary>The binding (if any) used to store the binding properties for the extension.</summary>
    private Binding? _binding;

    /// <summary>The default resx name (based on the attached property).</summary>
    private string? _defaultResxName;

    /// <summary>
    /// The resource manager to use for this extension. Holding a strong reference to the
    /// Resource Manager keeps it in the cache while ever there are ResxExtensions that are using it.
    /// </summary>
    private ResourceManager? _resourceManager;

    /// <summary>Initializes static members of the <see cref="ResxExtension"/> class. Class constructor.</summary>
    static ResxExtension()
    {
        // The Visual Studio 2012/2013 designer process (XDesProc) shadow copies the assemblies
        // to a cache location. Unfortunately it doesn't shadow copy the satellite assemblies -
        // so we have to resolve these ourselves if we want to have support for design time
        // switching of language
        if (AppDomain.CurrentDomain.FriendlyName != "XDesProc.exe")
        {
            return;
        }

        // check the registry first for a defined assembly path - use OpenBaseKey to avoid
        // Wow64 redirection
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\ResxExtension", false);
        _assemblyProbingPaths = CollectAssemblyProbingPaths(
            key?.GetValue("AssemblyPath") as string,
            Process.GetProcesses(),
            GetProcessFilepath,
            static process => process.ProcessName);

        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
    }

    /// <summary>Initializes a new instance of the <see cref="ResxExtension"/> class. Create a new instance of the markup extension.</summary>
    public ResxExtension()
        : base(MarkupManager)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ResxExtension"/> class. Create a new instance of the markup extension.</summary>
    /// <param name="key">The key used to get the value from the resources.</param>
    public ResxExtension(string key)
        : base(MarkupManager) => Key = key;

    /// <summary>
    /// This event allows a designer or preview application (such as Globalizer.NET) to intercept
    /// calls to get resources and provide the values instead dynamically
    /// </summary>
    public static event EventHandler<GetResourceEventArgs>? GetResource;

    /// <summary>Gets return the MarkupManager for this extension.</summary>
    public static MarkupExtensionManager MarkupManager { get; } = new(CleanupInterval);

    /// <summary>Gets return the associated binding for the extension.</summary>
    public Binding Binding
    {
        get
        {
            _binding ??= new Binding();
            return _binding;
        }
    }

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="Binding.AsyncState"/>.</summary>
    [DefaultValue(null)]
    public object BindingAsyncState
    {
        get => Binding.AsyncState; set => Binding.AsyncState = value;
    }

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="Binding.Converter"/>.</summary>
    [DefaultValue(null)]
    public IValueConverter BindingConverter
    {
        get => Binding.Converter; set => Binding.Converter = value;
    }

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="Binding.ConverterCulture"/>.</summary>
    [DefaultValue(null)]
    public CultureInfo BindingConverterCulture
    {
        get => Binding.ConverterCulture; set => Binding.ConverterCulture = value;
    }

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="Binding.ConverterParameter"/>.</summary>
    [DefaultValue(null)]
    public object BindingConverterParameter
    {
        get => Binding.ConverterParameter; set => Binding.ConverterParameter = value;
    }

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="Binding.ElementName"/>.</summary>
    [DefaultValue(null)]
    public string BindingElementName
    {
        get => Binding.ElementName; set => Binding.ElementName = value;
    }

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="BindingBase.FallbackValue"/>.</summary>
    [DefaultValue(null)]
    public object BindingFallbackValue
    {
        get => Binding.FallbackValue; set => Binding.FallbackValue = value;
    }

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="BindingBase.BindingGroupName"/>.</summary>
    [DefaultValue(null)]
    public string BindingGroupName
    {
        get => Binding.BindingGroupName; set => Binding.BindingGroupName = value;
    }

    /// <summary>Gets or sets a value indicating whether use the Resx value to format bound data. See <see cref="Binding.IsAsync"/>.</summary>
    [DefaultValue(false)]
    public bool BindingIsAsync
    {
        get => Binding.IsAsync; set => Binding.IsAsync = value;
    }

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="Binding.Mode"/>.</summary>
    [DefaultValue(BindingMode.Default)]
    public BindingMode BindingMode
    {
        get => Binding.Mode; set => Binding.Mode = value;
    }

    /// <summary>Gets or sets a value indicating whether use the Resx value to format bound data. See <see cref="Binding.NotifyOnSourceUpdated"/>.</summary>
    [DefaultValue(false)]
    public bool BindingNotifyOnSourceUpdated
    {
        get => Binding.NotifyOnSourceUpdated; set => Binding.NotifyOnSourceUpdated = value;
    }

    /// <summary>Gets or sets a value indicating whether use the Resx value to format bound data. See <see cref="Binding.NotifyOnTargetUpdated"/>.</summary>
    [DefaultValue(false)]
    public bool BindingNotifyOnTargetUpdated
    {
        get => Binding.NotifyOnTargetUpdated; set => Binding.NotifyOnTargetUpdated = value;
    }

    /// <summary>Gets or sets a value indicating whether use the Resx value to format bound data. See <see cref="Binding.NotifyOnValidationError"/>.</summary>
    [DefaultValue(false)]
    public bool BindingNotifyOnValidationError
    {
        get => Binding.NotifyOnValidationError; set => Binding.NotifyOnValidationError = value;
    }

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="Binding.Path"/>.</summary>
    [DefaultValue(null)]
    public PropertyPath BindingPath
    {
        get => Binding.Path; set => Binding.Path = value;
    }

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="Binding.RelativeSource"/>.</summary>
    [DefaultValue(null)]
    public RelativeSource BindingRelativeSource
    {
        get => Binding.RelativeSource; set => Binding.RelativeSource = value;
    }

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="Binding.Source"/>.</summary>
    [DefaultValue(null)]
    public object BindingSource
    {
        get => Binding.Source; set => Binding.Source = value;
    }

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="BindingBase.TargetNullValue"/>.</summary>
    [DefaultValue(null)]
    public object BindingTargetNullValue
    {
        get => Binding.TargetNullValue; set => Binding.TargetNullValue = value;
    }

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="Binding.UpdateSourceTrigger"/>.</summary>
    [DefaultValue(UpdateSourceTrigger.Default)]
    public UpdateSourceTrigger BindingUpdateSourceTrigger
    {
        get => Binding.UpdateSourceTrigger; set => Binding.UpdateSourceTrigger = value;
    }

    /// <summary>Gets or sets a value indicating whether use the Resx value to format bound data. See <see cref="Binding.ValidatesOnDataErrors"/>.</summary>
    [DefaultValue(false)]
    public bool BindingValidatesOnDataErrors
    {
        get => Binding.ValidatesOnDataErrors; set => Binding.ValidatesOnDataErrors = value;
    }

    /// <summary>Gets or sets a value indicating whether use the Resx value to format bound data. See <see cref="Binding.ValidatesOnExceptions"/>.</summary>
    [DefaultValue(false)]
    public bool BindingValidatesOnExceptions
    {
        get => Binding.ValidatesOnExceptions; set => Binding.ValidatesOnExceptions = value;
    }

    /// <summary>Gets use the Resx value to format bound data. See <see cref="Binding.ValidationRules"/>.</summary>
    [DefaultValue(false)]
    public Collection<ValidationRule> BindingValidationRules => Binding.ValidationRules;

    /// <summary>Gets or sets use the Resx value to format bound data. See <see cref="Binding.XPath"/>.</summary>
    [DefaultValue(null)]
    public string BindingXPath
    {
        get => Binding.XPath; set => Binding.XPath = value;
    }

    /// <summary>Gets or sets a value indicating whether use the Resx value to format bound data. See <see cref="Binding.BindsDirectlyToSource"/>.</summary>
    [DefaultValue(false)]
    public bool BindsDirectlyToSource
    {
        get => Binding.BindsDirectlyToSource; set => Binding.BindsDirectlyToSource = value;
    }

    /// <summary>Gets the child Resx elements (if any).</summary>
    /// <remarks>
    /// You can nest Resx elements in this case the parent Resx element value is used as a format
    /// string to format the values from child Resx elements similar to a <see
    /// cref="MultiBinding"/> eg If a Resx has two child elements then you.
    /// </remarks>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public Collection<ResxExtension> Children { get; } = [];

    /// <summary>Gets or sets the default value to use if the resource can't be found.</summary>
    /// <remarks>
    /// This particularly useful for properties which require non-null values because it allows
    /// the page to be displayed even if the resource can't be loaded.
    /// </remarks>
    public string? DefaultValue { get; set; }

    /// <summary>Gets or sets the name of the resource key.</summary>
    public string? Key { get; set; }

    /// <summary>Gets or sets the fully qualified name of the embedded resx (without .resources) to get the resource from.</summary>
    public string? ResxName
    {
        get
        {
            // if the ResxName property is not set explicitly then check the attached property
            var result = field;
            if (string.IsNullOrEmpty(result))
            {
                if (_defaultResxName is null)
                {
                    var targetRef = TargetObjects.Find(target => target.IsAlive);
                    if (targetRef?.Target is DependencyObject)
                    {
                        _defaultResxName = (targetRef.Target as DependencyObject)?.GetValue(DefaultResxNameProperty) as string;
                    }
                }

                result = _defaultResxName;
            }

            return result;
        }

        set => field = value;
    }

    /// <summary>Gets a value indicating whether have any of the binding properties been set.</summary>
    private bool IsBindingExpression => _binding is not null
                && (_binding.Source is not null || _binding.RelativeSource is not null
                 || _binding.ElementName is not null || _binding.XPath is not null
                 || _binding.Path is not null);

    /// <summary>Gets a value indicating whether is this ResxExtension being used inside another Resx Extension for multi-binding.</summary>
    private bool IsMultiBindingChild => TargetPropertyType == typeof(Collection<ResxExtension>);

    /// <summary>Gets a value indicating whether is this ResxExtension being used as a multi-binding parent.</summary>
    private bool IsMultiBindingParent => Children.Count > 0;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay => ToString() ?? GetType().Name;

    /// <summary>Get the DefaultResxName attached property for the given target.</summary>
    /// <param name="target">The Target object.</param>
    /// <returns>The name of the Resx.</returns>
    [AttachedPropertyBrowsableForChildren(IncludeDescendants = true)]
    public static string? GetDefaultResxName(DependencyObject target) => (string?)target?.GetValue(DefaultResxNameProperty);

    /// <summary>Set the DefaultResxName attached property for the given target.</summary>
    /// <param name="target">The Target object.</param>
    /// <param name="value">The name of the Resx.</param>
    public static void SetDefaultResxName(DependencyObject target, string value) => target?.SetValue(DefaultResxNameProperty, value);

    /// <summary>Use the Markup Manager to update all targets.</summary>
    public static void UpdateAllTargets() => MarkupManager.UpdateAllTargets();

    /// <summary>Update the ResxExtension target with the given key.</summary>
    /// <param name="key">todo: describe key parameter on UpdateTarget.</param>
    public static void UpdateTarget(string key)
    {
        foreach (var ext in MarkupManager.ActiveExtensions.Cast<ResxExtension>().Where(ext => ext.Key == key))
        {
            ext.UpdateTargets();
        }
    }

    /// <summary>Return the value for this instance of the Markup Extension.</summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <returns>The value of the element.</returns>
    /// <exception cref="ArgumentException">A ArgumentException.</exception>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // register the target and property so we can update them
        RegisterTarget(serviceProvider);

        // Show the icon in the notification tray to allow changing culture at design time
        if (IsInDesignMode)
        {
            CultureManager.ShowCultureNotifyIcon();
        }
        else if (Debugger.IsAttached)
        {
            // Also show when debugging to allow runtime testing
            CultureManager.ShowCultureNotifyIcon();
        }

        if (string.IsNullOrEmpty(Key) && !IsBindingExpression)
        {
            throw new ArgumentException("You must set the resource Key or Binding properties");
        }

        object? result;

        // if the extension is used in a template or as a child of another resx extension (for
        // multi-binding) then return this
        if (TargetProperty is null || IsMultiBindingChild)
        {
            result = this;
        }
        else if (IsMultiBindingParent)
        {
            // Invoke after this method returns to set up the MultiBinding on the target element.
            var binding = CreateMultiBinding();
            result = binding.ProvideValue(serviceProvider);
        }
        else if (IsBindingExpression)
        {
            // If this is a simple binding then return the binding.
            var binding = CreateBinding();
            result = binding.ProvideValue(serviceProvider);
        }
        else
        {
            // Otherwise return the value from the resources.
            result = GetValue();
        }

        return result;
    }

    /// <summary>Return a list of the current design time cultures.</summary>
    /// <returns>A Value.</returns>
    internal static List<CultureInfo> GetDesignTimeCultures()
        => FindDesignTimeCultures(_assemblyProbingPaths);

    /// <summary>Return the value for the markup extension.</summary>
    /// <returns>The value from the resources if possible otherwise the default value.</returns>
    protected override object GetValue()
    {
        if (Key is not { Length: > 0 } key)
        {
            return new();
        }

        var result = ResxName is { Length: > 0 } resxName ? GetResourceValue(resxName, key) : null;

        return (result ?? GetDefaultValue(key))!;
    }

    /// <summary>Update the given target when the culture changes.</summary>
    /// <param name="target">The target to update.</param>
    protected override void UpdateTarget(object target)
    {
        // binding of child extensions is done by the parent
        if (IsMultiBindingChild)
        {
            return;
        }

        if (IsMultiBindingParent)
        {
            if (target is FrameworkElement el)
            {
                var multiBinding = CreateMultiBinding();
                _ = el.SetBinding(TargetProperty as DependencyProperty, multiBinding);
            }
        }
        else if (IsBindingExpression)
        {
            if (target is FrameworkElement el)
            {
                var binding = CreateBinding();
                _ = el.SetBinding(TargetProperty as DependencyProperty, binding);
            }
        }
        else
        {
            base.UpdateTarget(target);
        }
    }

    private static List<string> CollectAssemblyProbingPaths(
        string? assemblyPath,
        IEnumerable<Process> processes,
        Func<int, string?> getProcessFilepath,
        Func<Process, string> getProcessName)
    {
        var paths = new List<string>();
        if (assemblyPath is not null)
        {
            foreach (var path in assemblyPath.Split(';'))
            {
                paths.Add(path.Trim());
            }
        }

        // Look for Visual Studio hosting processes and add the path to the probing path -
        // this means that if the hosting process is enabled you don't need to use a registry entry
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (getProcessName(process).Contains(".vshost"))
                    {
                        var path = getProcessFilepath(process.Id);
                        paths.Add(Path.GetDirectoryName(path)!);
                    }
                }
                catch (Win32Exception)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        return paths;
    }

    private static List<CultureInfo> FindDesignTimeCultures(IEnumerable<string>? probingPaths)
    {
        var result = new List<CultureInfo>();
        if (probingPaths is not null)
        {
            foreach (var path in probingPaths)
            {
                var subDirectories = Directory.GetDirectories(path);
                _ = new CultureInfoConverter();
                foreach (var subDirectory in subDirectories)
                {
                    var culture = GetCulture(Path.GetFileName(subDirectory));
                    if (culture is not null)
                    {
                        result.Add(culture);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Convert a culture name to a CultureInfo - without exceptions if the name is bad.</summary>
    /// <param name="name">The name of the culture.</param>
    /// <returns>The culture if the name was valid, or else null.</returns>
    /// <remarks>The CultureInfo constructor throws an exception.</remarks>
    private static CultureInfo? GetCulture(string name)
    {
        CultureInfo? result = null;
        try
        {
            result = new(name);
        }
        catch (CultureNotFoundException)
        {
        }

        return result;
    }

    /// <summary>Check if the assembly contains an embedded resx of the given name.</summary>
    /// <param name="assembly">The assembly to check.</param>
    /// <param name="resxName">The name of the resource we are looking for.</param>
    /// <returns>True if the assembly contains the resource.</returns>
    private static bool HasEmbeddedResx(Assembly assembly, string? resxName)
    {
        if (assembly.IsDynamic)
        {
            return false;
        }

        try
        {
            var resources = assembly.GetManifestResourceNames();
            var searchName = resxName!.ToLower() + ResourceFileSuffix;
            return resources.Any(resource => resource.Equals(searchName, StringComparison.CurrentCultureIgnoreCase));
        }
        catch (NotSupportedException)
        {
            // Some reflection-only or runtime-provided assemblies cannot enumerate resources.
        }
        catch (FileNotFoundException)
        {
            // GetManifestResourceNames may throw an exception for some assemblies - just ignore
            // these assemblies.
        }

        return false;
    }

    /// <summary>Return the file path associated with the main module of the given process ID.</summary>
    /// <param name="processId">The process identifier.</param>
    /// <returns>
    /// A Value.
    /// </returns>
    private static string? GetProcessFilepath(int processId)
    {
        var wmiQueryString = $"SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE ProcessId = {processId}";
        using var searcher = new ManagementObjectSearcher(wmiQueryString);
        using var results = searcher.Get();
        return results
            .Cast<ManagementObject>()
            .Select(managementObject => managementObject["ExecutablePath"] as string)
            .FirstOrDefault(path => path is not null);
    }

    /// <summary>Resolve satellite assemblies when running inside the Visual Studio designer (XDesProc) process.</summary>
    /// <param name="sender">The sender.</param>
    /// <param name="args">The <see cref="ResolveEventArgs"/> instance containing the event data.</param>
    /// <returns>
    /// The assembly if found.
    /// </returns>
    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        => ResolveAssembly(args, _assemblyProbingPaths ?? []);

    private static Assembly? ResolveAssembly(ResolveEventArgs args, IEnumerable<string> probingPaths)
    {
        Assembly? result = null;
        var nameSplit = args!.Name.Split(',');
        if (nameSplit.Length < 3)
        {
            return null;
        }

        var name = nameSplit[0];

        // Only resolve satellite resource assemblies
        if (!name.EndsWith(ResourceFileSuffix))
        {
            return null;
        }

        // ignore calls to resolve our own satellite assemblies
        var thisAssembly = Assembly.GetExecutingAssembly().GetName().Name;
        if (name == thisAssembly + ResourceFileSuffix)
        {
            return null;
        }

        // check that we haven't already loaded the assembly - for some reason AssemblyResolve is
        // still called sometimes after the assembly has already been loaded. Most recently
        // loaded assemblies are last on the list
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var i = loadedAssemblies.Length - 1; i >= 0; i--)
        {
            var assembly = loadedAssemblies[i];
            if (assembly.FullName == args.Name)
            {
                return assembly;
            }
        }

        // get the culture of the assembly to load
        var cultureSplit = nameSplit[2].Split('=');
        if (cultureSplit.Length < 2)
        {
            return null;
        }

        var culture = cultureSplit[1];

        var fileName = $"{name}.dll";

        // look for the latest version of the satellite assembly with the given culture on the
        // assembly probing paths
        string? latestFile = null;
        var latestFileTime = DateTime.MinValue;
        foreach (var path in probingPaths)
        {
            var dir = Path.Combine(path, culture);
            var file = Path.Combine(dir, fileName);
            if (File.Exists(file))
            {
                var fileTime = File.GetLastWriteTime(file);
                if (fileTime > latestFileTime)
                {
                    latestFile = file;
                    latestFileTime = fileTime;
                }
            }
        }

        if (latestFile is not null)
        {
            result = Assembly.Load(File.ReadAllBytes(latestFile));
        }

        return result;
    }

    /// <summary>Handle a change to the attached DefaultResxName property.</summary>
    /// <param name="element">the dependency object (a WPF element).</param>
    /// <param name="args">the dependency property changed event arguments.</param>
    /// <remarks>In design mode update the extension with the correct ResxName.</remarks>
    private static void OnDefaultResxNamePropertyChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (!DesignerProperties.GetIsInDesignMode(element))
        {
            return;
        }

        foreach (var ext in MarkupManager.ActiveExtensions.Cast<ResxExtension>().Where(ext => ext.IsTarget(element)))
        {
            // force the resource manager to be reloaded when the attached resx name changes
            ext._resourceManager = null;
            ext._defaultResxName = args.NewValue as string;
            ext.UpdateTarget(element);
        }
    }

    private object? GetResourceValue(string resxName, string key)
    {
        try
        {
            var getResourceEventArgs = new GetResourceEventArgs(resxName, key, CultureManager.UICulture);
            GetResource?.Invoke(null, getResourceEventArgs);
            var result = getResourceEventArgs.Resource;

            if (result is null)
            {
                _resourceManager ??= GetResourceManager(resxName);
                result = _resourceManager?.GetObject(key, CultureManager.UICulture);
            }

            return IsMultiBindingChild ? result : ConvertValue(result);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
        catch (MissingSatelliteAssemblyException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Convert a resource object to the type required by the WPF element.</summary>
    /// <param name="value">The resource value to convert.</param>
    /// <returns>The WPF element value.</returns>
    private object? ConvertValue(object? value)
    {
        BitmapSource? bitmapSource = null;

        // convert icons and bitmaps to BitmapSource objects that WPF uses
        if (value is Icon)
        {
            var icon = value as Icon;

            // For icons we must create a new BitmapFrame from the icon data stream The approach
            // we use for bitmaps (below) doesn't work when setting the Icon property of a window
            // (although it will work for other Icons)
            using var iconStream = new MemoryStream();
            icon?.Save(iconStream);
            _ = iconStream.Seek(0, SeekOrigin.Begin);
            bitmapSource = BitmapFrame.Create(iconStream);
        }
        else if (value is Bitmap)
        {
            var bitmap = value as Bitmap;
            var bitmapHandle = bitmap?.GetHbitmap() ?? IntPtr.Zero;
            bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            _ = NativeMethods.DeleteObject(bitmapHandle);
        }

        object? result;
        if (bitmapSource is not null)
        {
            // if the target property is expecting the Icon to be content then we create an
            // ImageControl and set its Source property to image
            result = TargetPropertyType == typeof(object)
                ? new WpfImage
                {
                    Source = bitmapSource,
                    Width = bitmapSource.Width,
                    Height = bitmapSource.Height
                }
                : bitmapSource;
        }
        else
        {
            result = value;

            // allow for resources to either contain simple strings or typed data
            var targetType = TargetPropertyType;
            if (targetType is not null && value is string && targetType != typeof(string) && targetType != typeof(object))
            {
                var tc = TypeDescriptor.GetConverter(targetType);
                result = tc.ConvertFromInvariantString((value as string)!);
            }
        }

        return result;
    }

    /// <summary>Create a binding for this Resx Extension.</summary>
    /// <returns>A binding for this Resx Extension.</returns>
    private Binding CreateBinding()
    {
        var binding = new Binding();
        if (IsBindingExpression)
        {
            // copy all the properties of the binding to the new binding
            if (_binding?.ElementName is not null)
            {
                binding.ElementName = _binding.ElementName;
            }

            if (_binding?.RelativeSource is not null)
            {
                binding.RelativeSource = _binding.RelativeSource;
            }

            if (_binding?.Source is not null)
            {
                binding.Source = _binding.Source;
            }

            binding.AsyncState = _binding?.AsyncState;
            binding.BindingGroupName = _binding?.BindingGroupName;
            binding.BindsDirectlyToSource = _binding!.BindsDirectlyToSource;
            binding.Converter = _binding.Converter;
            binding.ConverterCulture = _binding.ConverterCulture;
            binding.ConverterParameter = _binding.ConverterParameter;
            binding.FallbackValue = _binding.FallbackValue;
            binding.IsAsync = _binding.IsAsync;
            binding.Mode = _binding.Mode;
            binding.NotifyOnSourceUpdated = _binding.NotifyOnSourceUpdated;
            binding.NotifyOnTargetUpdated = _binding.NotifyOnTargetUpdated;
            binding.NotifyOnValidationError = _binding.NotifyOnValidationError;
            binding.Path = _binding.Path;
            binding.TargetNullValue = _binding.TargetNullValue;
            binding.UpdateSourceTrigger = _binding.UpdateSourceTrigger;
            binding.ValidatesOnDataErrors = _binding.ValidatesOnDataErrors;
            binding.ValidatesOnExceptions = _binding.ValidatesOnExceptions;
            foreach (var rule in _binding.ValidationRules)
            {
                binding.ValidationRules.Add(rule);
            }

            binding.XPath = _binding.XPath;
            binding.StringFormat = GetValue() as string;
        }
        else
        {
            binding.Source = GetValue();
        }

        return binding;
    }

    /// <summary>Create new MultiBinding that binds to the child Resx Extensioins.</summary>
    /// <returns>A Value.</returns>
    private MultiBinding CreateMultiBinding()
    {
        var result = new MultiBinding();
        foreach (var child in Children)
        {
            // ensure the child has a resx name
            child.ResxName ??= ResxName;
            result.Bindings.Add(child.CreateBinding());
        }

        result.StringFormat = GetValue() as string;
        return result;
    }

    /// <summary>Find the assembly that contains the type.</summary>
    /// <returns>The assembly if loaded (otherwise null).</returns>
    private Assembly? FindResourceAssembly()
    {
        var assembly = Assembly.GetEntryAssembly();

        // check the entry assembly first - this will short circuit a lot of searching
        if (assembly is not null && HasEmbeddedResx(assembly, ResxName))
        {
            return assembly;
        }

        foreach (var searchAssembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // skip system assemblies
            var name = searchAssembly.FullName;
            if (!name!.StartsWith("Microsoft.")
                && !name.StartsWith("System.")
                && !name.StartsWith("System,")
                && !name.StartsWith("mscorlib,")
                && !name.StartsWith("PresentationFramework,")
                && !name.StartsWith("WindowsBase,")
                && HasEmbeddedResx(searchAssembly, ResxName))
            {
                return searchAssembly;
            }
        }

        return null;
    }

    /// <summary>Return the default value for the property.</summary>
    /// <param name="key">The key.</param>
    /// <returns>A Value.</returns>
    private object? GetDefaultValue(string? key)
    {
        object? result = DefaultValue;
        var targetType = TargetPropertyType;
        if (DefaultValue is null)
        {
            if (targetType == typeof(string) || targetType == typeof(object) || IsMultiBindingChild)
            {
                result = $"#{key}";
            }
        }
        else if (targetType is not null && targetType != typeof(string) && targetType != typeof(object))
        {
            // Convert the default value if necessary to the required type.
            try
            {
                var typeConverter = TypeDescriptor.GetConverter(targetType);
                result = typeConverter.ConvertFromInvariantString(DefaultValue);
            }
            catch (NotSupportedException)
            {
            }
            catch (FormatException)
            {
            }
        }

        return result;
    }

    /// <summary>Get the resource manager for this type.</summary>
    /// <param name="resxName">The name of the embedded resx.</param>
    /// <returns>The resource manager.</returns>
    /// <remarks>Caches resource managers to improve performance.</remarks>
    private ResourceManager? GetResourceManager(string? resxName)
    {
        ResourceManager? result = null;
        if (resxName is null)
        {
            return null;
        }

        if (_resourceManagers.TryGetValue(resxName, out var reference))
        {
            result = reference.Target as ResourceManager;

            // if the resource manager has been garbage collected then remove the cache entry (it
            // will be readded)
            if (result is null)
            {
                _ = _resourceManagers.Remove(resxName);
            }
        }

        if (result is null)
        {
            var assembly = FindResourceAssembly();
            if (assembly is not null)
            {
                result = new(resxName, assembly);
            }

            _resourceManagers.Add(resxName, new(result));
        }

        return result;
    }
}
