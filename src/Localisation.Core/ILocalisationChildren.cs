// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace CP.Localisation;

/// <summary>Explicitly refreshes localized properties and existing children without evaluating lazy factories.</summary>
public interface ILocalisationChildren
{
    /// <summary>Refreshes localized properties and traverses existing children with the shared visited set.</summary>
    /// <param name="visited">Objects already visited in this traversal.</param>
    void Refresh(HashSet<object> visited);
}
