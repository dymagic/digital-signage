namespace Signage.Web.Domain;

/// <summary>A row in an ordered list (playlist slides, screen slides, screen and group playlists).</summary>
public interface IPositioned
{
    /// <summary>Dense, zero-based within its list.</summary>
    int Position { get; set; }
}

/// <summary>In-memory edits of an ordered list. Callers save; positions come out dense.</summary>
public static class OrderedList
{
    /// <summary>Moves <paramref name="item"/> to <paramref name="toIndex"/>, clamped to the list.</summary>
    public static void Move<T>(List<T> items, T item, int toIndex) where T : IPositioned
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!items.Remove(item))
        {
            throw new ArgumentException("Item is not in the list.", nameof(item));
        }

        items.Insert(Math.Clamp(toIndex, 0, items.Count), item);
        Renumber(items);
    }

    /// <summary>Sets every position to its index (0, 1, 2, …).</summary>
    public static void Renumber<T>(IList<T> items) where T : IPositioned
    {
        ArgumentNullException.ThrowIfNull(items);
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Position = i;
        }
    }
}
