// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace CP.Localisation;

/// <summary>Resolves registered strings using an independently selected interface culture.</summary>
public sealed class LocalisationContext : IDisposable
{
    private readonly string _assemblyName;

    private readonly string _resourceName;

    private readonly CultureStream _changes = new();

    private readonly object _choiceGate = new();

    private readonly List<WeakReference<LocalisationStringList>> _choices = [];

    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="LocalisationContext"/> class.</summary>
    /// <param name="assemblyName">The assembly simple name.</param>
    /// <param name="resourceName">The resource base name.</param>
    public LocalisationContext(string assemblyName, string resourceName)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrEmpty(assemblyName);
        ArgumentException.ThrowIfNullOrEmpty(resourceName);
#else
        if (string.IsNullOrEmpty(assemblyName))
        {
            throw new ArgumentException("An assembly name is required.", nameof(assemblyName));
        }

        if (string.IsNullOrEmpty(resourceName))
        {
            throw new ArgumentException("A resource name is required.", nameof(resourceName));
        }

#endif
        _assemblyName = assemblyName;
        _resourceName = resourceName;
    }

    /// <summary>Gets the selected culture, or the caller's interface culture until one is selected.</summary>
    public CultureInfo UICulture => _changes.Culture;

    /// <summary>Gets serialized culture notifications, replaying the current culture on subscription.</summary>
    public IObservable<CultureInfo> CultureChanges => _changes;

    /// <summary>Validates that the configured provider contains a required string resource.</summary>
    /// <param name="probeKey">An application-owned resource key required during startup.</param>
    /// <exception cref="InvalidOperationException">The provider or required string is missing.</exception>
    public void Initialize(string probeKey)
    {
        if (probeKey is null)
        {
            throw new ArgumentNullException(nameof(probeKey));
        }

        if (LocalisationResourceRegistry.TryGetResource(_assemblyName, _resourceName, probeKey, UICulture, out var value) && value is string)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The localisation provider for '{_assemblyName}:{_resourceName}' does not contain the required string '{probeKey}'.");
    }

    /// <summary>Creates a choice list and weakly registers it for explicit collection refreshes.</summary>
    /// <param name="keys">Resource keys in display order.</param>
    /// <returns>The localized labels and their immutable resource identities.</returns>
    /// <exception cref="ObjectDisposedException">The context has been disposed.</exception>
    public LocalisationStringList CreateChoices(params string[] keys)
    {
        lock (_choiceGate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LocalisationContext));
            }

            var choices = new LocalisationStringList(this, keys);
            _choices.Add(new(choices));
            return choices;
        }
    }

    /// <summary>Explicitly refreshes surviving choice lists created by this context.</summary>
    /// <remarks>Call on the collection observers' owning thread. Culture selection does not refresh collections automatically.</remarks>
    public void RefreshChoices()
    {
        List<LocalisationStringList> live = [];
        lock (_choiceGate)
        {
            for (var index = _choices.Count - 1; index >= 0; index--)
            {
                if (_choices[index].TryGetTarget(out var choices))
                {
                    live.Add(choices);
                }
                else
                {
                    _choices.RemoveAt(index);
                }
            }
        }

        foreach (var choices in live)
        {
            choices.Refresh();
        }
    }

    /// <summary>Selects this context's culture without changing ambient formatting or interface cultures.</summary>
    /// <param name="culture">The interface culture.</param>
    /// <exception cref="ObjectDisposedException">The context has been disposed.</exception>
    public void SetCulture(CultureInfo culture)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        _changes.SetCulture(culture);
    }

    /// <summary>Resolves a string, returning its key when absent or not a string.</summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The localized string or key.</returns>
    public string Get(string key) => Get(key, UICulture);

    /// <summary>Resolves a string using the provider's culture fallback behavior.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="culture">The requested interface culture.</param>
    /// <returns>The localized string or key.</returns>
    public string Get(string key, CultureInfo culture)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return LocalisationResourceRegistry.TryGetResource(_assemblyName, _resourceName, key, culture, out var value) && value is string text ? text : key;
    }

    /// <summary>Formats a localized resource using the caller's current numeric culture.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="args">Composite format arguments.</param>
    /// <returns>The formatted localized string.</returns>
    public string Format(string key, params object?[] args)
    {
        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    /// <summary>Formats one argument using the caller's current numeric culture.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="arg0">The first argument.</param>
    /// <returns>The formatted localized string.</returns>
    public string Format(string key, object? arg0) => string.Format(CultureInfo.CurrentCulture, Get(key), arg0);

    /// <summary>Formats two arguments using the caller's current numeric culture.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="arg0">The first argument.</param>
    /// <param name="arg1">The second argument.</param>
    /// <returns>The formatted localized string.</returns>
    public string Format(string key, object? arg0, object? arg1) => string.Format(CultureInfo.CurrentCulture, Get(key), arg0, arg1);

    /// <summary>Formats three arguments using the caller's current numeric culture.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="arg0">The first argument.</param>
    /// <param name="arg1">The second argument.</param>
    /// <param name="arg2">The third argument.</param>
    /// <returns>The formatted localized string.</returns>
    public string Format(string key, object? arg0, object? arg1, object? arg2) => string.Format(CultureInfo.CurrentCulture, Get(key), arg0, arg1, arg2);

    /// <summary>Completes culture subscriptions. Resource lookup remains available after disposal.</summary>
    public void Dispose()
    {
        lock (_choiceGate)
        {
            _disposed = true;
            _choices.Clear();
        }

        _changes.Dispose();
    }

    internal void ThrowIfDisposed() => _changes.ThrowIfDisposed();

    private sealed class CultureStream : IObservable<CultureInfo>, IDisposable
    {
        private readonly object _gate = new();

        private readonly List<Subscription> _subscriptions = [];

        private readonly Queue<Notification> _pending = new();

        private CultureInfo? _culture;

        private bool _disposed;

        private bool _draining;

        public CultureInfo Culture => Volatile.Read(ref _culture) ?? CultureInfo.CurrentUICulture;

        public void ThrowIfDisposed()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(LocalisationContext));
                }
            }
        }

        public IDisposable Subscribe(IObserver<CultureInfo> observer)
        {
            if (observer is null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            Subscription subscription;
            lock (_gate)
            {
                subscription = new(this, observer);
                if (!_disposed)
                {
                    _subscriptions.Add(subscription);
                }

                _pending.Enqueue(new(subscription, _disposed ? null : Culture));
            }

            try
            {
                Drain();
                return subscription;
            }
            catch
            {
                subscription.Dispose();
                throw;
            }
        }

        public void SetCulture(CultureInfo culture)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(LocalisationContext));
                }

                if (Equals(_culture, culture))
                {
                    return;
                }

                Volatile.Write(ref _culture, culture);
                foreach (var subscription in _subscriptions)
                {
                    _pending.Enqueue(new(subscription, culture));
                }
            }

            Drain();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                foreach (var subscription in _subscriptions)
                {
                    _pending.Enqueue(new(subscription, null));
                }

                _subscriptions.Clear();
            }

            Drain();
        }

        private void Remove(Subscription subscription)
        {
            lock (_gate)
            {
                _ = _subscriptions.Remove(subscription);
            }
        }

        private void Drain()
        {
            lock (_gate)
            {
                if (_draining)
                {
                    return;
                }

                _draining = true;
            }

            ExceptionDispatchInfo? error = null;
            while (true)
            {
                Notification notification;
                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        _draining = false;
                        break;
                    }

                    notification = _pending.Dequeue();
                }

                try
                {
                    notification.Subscription.Deliver(notification.Culture);
                }
                catch (Exception exception)
                {
                    error ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            error?.Throw();
        }

        private readonly struct Notification(Subscription subscription, CultureInfo? culture)
        {
            public Subscription Subscription { get; } = subscription;

            public CultureInfo? Culture { get; } = culture;
        }

        private sealed class Subscription(CultureStream stream, IObserver<CultureInfo> observer) : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                stream.Remove(this);
            }

            public void Deliver(CultureInfo? culture)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                if (culture is not null)
                {
                    observer.OnNext(culture);
                    return;
                }

                Dispose();
                observer.OnCompleted();
            }
        }
    }
}
