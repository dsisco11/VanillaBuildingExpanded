using System;
using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.UI;

/// <summary>Registers an owning table as one composer element without exposing its children to native registration.</summary>
internal static class GuiElementTableComposerExtensions
{
    /// <summary>Settles layout and transfers table ownership only if native conditional registration actually inserts it.</summary>
    /// <param name="composer">An uncomposed composer that will own a successfully inserted table.</param>
    /// <param name="table">An unregistered table with a complete, valid layout.</param>
    /// <param name="key">An explicit, nonblank composer-local key; child keys remain table-local.</param>
    /// <returns>False only for a skipped native conditional addition; the caller then retains ownership.</returns>
    /// <remarks>
    /// If native addition throws after insertion, the table remains registered and composer-owned.
    /// Dispose the failed composer rather than continuing to use its potentially partial bounds graph.
    /// </remarks>
    public static bool TryAddTable(this GuiComposer composer, GuiElementTable table, string key)
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (composer.Composed) throw new InvalidOperationException("Cannot add a table to a composed composer.");
        if (composer.GetElement(key) is not null)
            throw new ArgumentException($"Composer key '{key}' is already in use.", nameof(key));
        table.ValidateRegistration(composer);
        try
        {
            composer.AddInteractiveElement(table, key);
        }
        finally
        {
            // Native maps acquire the element before clip/bounds callbacks, which can still throw.
            if (ReferenceEquals(composer.GetElement(key), table)) table.MarkRegistered();
        }
        return table.IsRegistered;
    }
}
