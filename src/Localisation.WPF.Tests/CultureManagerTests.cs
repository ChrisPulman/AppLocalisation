// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Markup;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Localisation.WPF.Tests;

/// <summary>Verifies designer culture selection and notification menu behaviour.</summary>
internal sealed class CultureManagerTests
{
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic;

    private const string GermanCulture = "de-DE";

    private const string FrenchCulture = "fr-FR";

    private const string CultureCheckMethod = "OnCultureMenuCheckChanged";

    private const string DesignerExitMethod = "OnDesignerExit";

    private const int DoubleClickCount = 2;

    private const int CallbackMessage = 2048;

    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>Verifies culture menus select cultures and the designer window follows the selection.</summary>
    /// <param name="reactive">Whether to exercise the reactive package.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    internal async Task DesignerMenu_SelectsCulturesAndReusesOpenWindow(bool reactive)
    {
        var result = await OnSta(() => ExerciseDesigner(reactive));
        await Assert.That(result.MenuAdded).IsTrue();
        await Assert.That(result.SelectedCulture).IsEqualTo(GermanCulture);
        await Assert.That(result.WindowReused).IsTrue();
        await Assert.That(result.WindowReleased).IsTrue();
        await Assert.That(result.CulturesSorted).IsTrue();
        await Assert.That(result.NullCulturesRejected).IsTrue();
        await Assert.That(result.NativeDataSize).IsGreaterThan(0);
        await Assert.That(result.DesignResourceValue).IsEqualTo("#DesignerKey");
    }

    /// <summary>Verifies UI culture notifications are dispatched to the application thread.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task UICulture_ApplicationDispatcherPublishesOnUiThread()
    {
        var result = await OnSta(ExerciseDispatcher);
        await Assert.That(result).IsTrue();
        await Assert.That(System.Windows.Application.Current).IsNull();
    }

    private static bool ExerciseDispatcher()
    {
        var originalLean = CP.Localisation.CultureManager.UICulture;
        var originalReactive = CP.Localisation.Reactive.CultureManager.UICulture;
        var defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var uiThread = Environment.CurrentManagedThreadId;
        var validThreads = true;
        EventHandler handler = (_, _) => validThreads &= Environment.CurrentManagedThreadId == uiThread;
        CP.Localisation.CultureManager.UICultureChanged += handler;
        CP.Localisation.Reactive.CultureManager.UICultureChanged += handler;
        try
        {
            CP.Localisation.CultureManager.UICulture = new(FrenchCulture);
            CP.Localisation.Reactive.CultureManager.UICulture = new(FrenchCulture);
            ApplyCulturesFromWorker(application);
            return validThreads;
        }
        finally
        {
            CP.Localisation.CultureManager.UICultureChanged -= handler;
            CP.Localisation.Reactive.CultureManager.UICultureChanged -= handler;
            try
            {
                CP.Localisation.CultureManager.UICulture = originalLean;
                CP.Localisation.Reactive.CultureManager.UICulture = originalReactive;
            }
            finally
            {
                CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
                application.Shutdown();
                application.Dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                System.Windows.Threading.Dispatcher.Run();
            }
        }
    }

    private static void ApplyCulturesFromWorker(System.Windows.Application application)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                CP.Localisation.CultureManager.UICulture = new(GermanCulture);
                CP.Localisation.Reactive.CultureManager.UICulture = new(GermanCulture);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                _ = application.Dispatcher.InvokeAsync(() => frame.Continue = false);
            }
        });
        worker.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        if (failure is null)
        {
            return;
        }

        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static DesignerResult ExerciseDesigner(bool reactive)
    {
        var manager = reactive ? typeof(CP.Localisation.Reactive.CultureManager) : typeof(CP.Localisation.CultureManager);
        var assembly = manager.Assembly;
        var cultureProperty = manager.GetProperty("UICulture")!;
        var originalCulture = (CultureInfo)cultureProperty.GetValue(null)!;
        var originalThreadCulture = Thread.CurrentThread.CurrentCulture;
        var originalThreadUiCulture = Thread.CurrentThread.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var iconField = manager.GetField("_notifyIcon", StaticFlags)!;
        var handleField = manager.GetField("_notifyIconHandle", StaticFlags)!;
        var windowField = manager.GetField("_cultureSelectWindow", StaticFlags)!;
        NotifyIcon? icon = null;

        try
        {
            Invoke(manager, DesignerExitMethod, null, EventArgs.Empty);
            Invoke(manager, "OnMenuStripOpening", null, new CancelEventArgs());
            Invoke(manager, "AddCultureMenuItem", new CultureInfo(FrenchCulture));
            Invoke(manager, "ApplyUICulture", [null]);
            Invoke(manager, CultureCheckMethod, null, EventArgs.Empty);
            Invoke(manager, "ShowCultureNotifyIcon");
            icon = (NotifyIcon)iconField.GetValue(null)!;
            icon.Visible = false;
            Invoke(manager, "ShowCultureNotifyIcon");
            var menu = icon.ContextMenuStrip!;
            cultureProperty.SetValue(null, new CultureInfo(FrenchCulture));
            Invoke(manager, "OnMenuStripOpening", menu, new CancelEventArgs());
            Invoke(manager, "AddCultureMenuItem", new CultureInfo(FrenchCulture));
            var menuAdded = menu.Items.Count >= 3 && menu.Items[0].Tag is CultureInfo;
            using var invalidItem = new ToolStripMenuItem { Checked = true };
            Invoke(manager, CultureCheckMethod, invalidItem, EventArgs.Empty);
            using var uncheckedItem = new ToolStripMenuItem { Tag = new CultureInfo(GermanCulture) };
            Invoke(manager, CultureCheckMethod, uncheckedItem, EventArgs.Empty);
            uncheckedItem.Checked = true;
            Invoke(manager, CultureCheckMethod, uncheckedItem, EventArgs.Empty);
            Invoke(manager, "OnCultureNotifyIconMouseClick", icon, new MouseEventArgs(MouseButtons.Right, 1, 0, 0, 0));
            ClickWithoutContextMenu(manager, icon);

            return ExerciseWindow(manager, assembly, cultureProperty, handleField, windowField, icon, menuAdded);
        }
        finally
        {
            ((Window?)windowField.GetValue(null))?.Close();
            icon?.ContextMenuStrip?.Dispose();
            icon?.Dispose();
            var exitHandler = manager.GetMethod(DesignerExitMethod, StaticFlags)!.CreateDelegate<EventHandler>();
            AppDomain.CurrentDomain.ProcessExit -= exitHandler;
            iconField.SetValue(null, null);
            windowField.SetValue(null, null);
            handleField.SetValue(null, IntPtr.Zero);
            cultureProperty.SetValue(null, originalCulture);
            Thread.CurrentThread.CurrentCulture = originalThreadCulture;
            Thread.CurrentThread.CurrentUICulture = originalThreadUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
        }
    }

    private static void ClickWithoutContextMenu(Type manager, NotifyIcon icon)
    {
        var contextMenu = icon.ContextMenuStrip;
        icon.ContextMenuStrip = null;
        try
        {
            Invoke(manager, "OnCultureNotifyIconMouseClick", icon, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
        }
        finally
        {
            icon.ContextMenuStrip = contextMenu;
        }
    }

    private static DesignerResult ExerciseWindow(
        Type manager,
        Assembly assembly,
        PropertyInfo cultureProperty,
        FieldInfo handleField,
        FieldInfo windowField,
        NotifyIcon icon,
        bool menuAdded)
    {
        Invoke(manager, "OnCultureSelectMenuClick", null, EventArgs.Empty);
        var window = (Window)windowField.GetValue(null)!;
        window.Hide();
        Invoke(manager, "OnCultureNotifyIconMouseDoubleClick", icon, new MouseEventArgs(MouseButtons.Left, DoubleClickCount, 0, 0, 0));
        var reused = ReferenceEquals(window, windowField.GetValue(null));
        var windowType = window.GetType();
        _ = windowType.GetProperty("DebuggerDisplay", InstanceFlags)!.GetValue(window);
        var combo = (ComboBox)windowType.GetField("_cultureCombo", InstanceFlags)!.GetValue(window)!;
        combo.SelectedItem = null;
        var sorted = true;
        CultureInfo? previous = null;
        foreach (CultureInfo culture in combo.Items)
        {
            if (previous?.DisplayName.CompareTo(culture.DisplayName) > 0)
            {
                sorted = false;
            }

            previous = culture;
        }

        combo.SelectedItem = CultureInfo.GetCultureInfo(GermanCulture);
        var selected = ((CultureInfo)cultureProperty.GetValue(null)!).Name;
        var comparerType = windowType.GetNestedType("CultureInfoComparer", BindingFlags.NonPublic)!;
        var comparer = (System.Collections.Generic.Comparer<CultureInfo>)Activator.CreateInstance(comparerType, true)!;
        var nullXRejected = CaptureNullArgument(() => comparer.Compare(null, CultureInfo.InvariantCulture));
        var nullYRejected = CaptureNullArgument(() => comparer.Compare(CultureInfo.InvariantCulture, null));
        window.Close();
        var released = windowField.GetValue(null) is null;
        var nativeType = assembly.GetType($"{manager.Namespace}.NativeMethods+NotifyIconData")!;
        var nativeData = Activator.CreateInstance(nativeType, InstanceFlags, null, [IntPtr.Zero, 1, 1, CallbackMessage], CultureInfo.InvariantCulture)!;
        var nativeSize = (int)nativeType.GetField("_size", InstanceFlags)!.GetValue(nativeData)!;
        handleField.SetValue(null, new IntPtr(1));
        Invoke(manager, DesignerExitMethod, null, EventArgs.Empty);
        handleField.SetValue(null, IntPtr.Zero);
        return new(menuAdded, selected, reused, released, sorted, nativeSize, nullXRejected && nullYRejected, ExerciseDesignExtension(manager));
    }

    private static object ExerciseDesignExtension(Type manager)
    {
        var extensionType = manager.Assembly.GetType($"{manager.Namespace}.ResxExtension")!;
        var extension = (MarkupExtension)Activator.CreateInstance(extensionType)!;
        extensionType.GetProperty("Key")!.SetValue(extension, "DesignerKey");
        var target = new System.Windows.Controls.TextBlock();
        DesignerProperties.SetIsInDesignMode(target, true);
        return extension.ProvideValue(new DesignTargetProvider(target));
    }

    private static bool CaptureNullArgument(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentNullException)
        {
            return true;
        }
    }

    private static void Invoke(Type type, string name, params object?[] arguments) =>
        _ = type.GetMethod(name, StaticFlags)!.Invoke(null, arguments);

    private static Task<T> OnSta<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private sealed class DesignTargetProvider(System.Windows.Controls.TextBlock target) : IServiceProvider, IProvideValueTarget
    {
        public object TargetObject => target;

        public object TargetProperty => System.Windows.Controls.TextBlock.TextProperty;

        public object? GetService(Type serviceType) => serviceType == typeof(IProvideValueTarget) ? this : null;
    }

    private sealed record DesignerResult(
        bool MenuAdded,
        string SelectedCulture,
        bool WindowReused,
        bool WindowReleased,
        bool CulturesSorted,
        int NativeDataSize,
        bool NullCulturesRejected,
        object DesignResourceValue);
}
