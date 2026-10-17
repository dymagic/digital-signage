namespace Signage.Web.Pages.Admin;

/// <summary>Model for <c>_EntryButtons</c>: up, down and remove for one entry of an htmx-edited list (ADR-0006).</summary>
public sealed record EntryButtons(string MoveUrl, string RemoveUrl, string EntryId, string Title, int Index, int Count, string Version);
