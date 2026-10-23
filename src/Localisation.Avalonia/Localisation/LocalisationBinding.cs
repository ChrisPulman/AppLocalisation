// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;

#if REACTIVE_SHIM
namespace CP.Localisation.Avalonia.Reactive;
#else
namespace CP.Localisation.Avalonia;
#endif

/// <summary>Creates typed bindings that reevaluate when an owned localization context changes culture.</summary>
public static class LocalisationBinding
{
    /// <summary>Creates a culture-driven binding without reflecting over the supplied state.</summary>
    /// <typeparam name="TState">The state read by the selector.</typeparam>
    /// <typeparam name="TResult">The property value produced by the selector.</typeparam>
    /// <param name="context">The localization context whose culture is observed.</param>
    /// <param name="state">The typed state retained by this binding.</param>
    /// <param name="selector">Resolves the property value for the supplied culture.</param>
    /// <returns>A binding that initially resolves the current culture and observes subsequent changes.</returns>
    public static BindingBase Create<TState, TResult>(LocalisationContext context, TState state, Func<TState, CultureInfo, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(selector);
        return new CultureProjection<TState, TResult>(context, state, selector).ToBinding();
    }

    private sealed class CultureProjection<TState, TResult>(
        LocalisationContext context,
        TState state,
        Func<TState, CultureInfo, TResult> selector) : IObservable<TResult>
    {
        public IDisposable Subscribe(IObserver<TResult> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            return context.CultureChanges.Subscribe(new ProjectionObserver(observer, state, selector));
        }

        private sealed class ProjectionObserver(IObserver<TResult> observer, TState state, Func<TState, CultureInfo, TResult> selector) : IObserver<CultureInfo>
        {
            public void OnCompleted() => observer.OnCompleted();

            public void OnError(Exception error) => observer.OnError(error);

            public void OnNext(CultureInfo value) => observer.OnNext(selector(state, value));
        }
    }
}
