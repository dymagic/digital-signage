namespace Signage.Web;

/// <summary>
/// Marker for the single shared string table, <c>Resources/SharedResource.resx</c> (R-64).
/// Views use <c>L["Key"]</c> (injected in <c>_ViewImports</c>); code uses <c>IStringLocalizer&lt;SharedResource&gt;</c>.
/// </summary>
#pragma warning disable CA1052 // Marker type for IStringLocalizer<T>; never instantiated.
public sealed class SharedResource;
#pragma warning restore CA1052
