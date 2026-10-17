namespace Signage.Web.Features;

/// <summary>Outcome of a command. <see cref="ErrorKey"/> is a key in <c>SharedResource.resx</c> shown to the user.</summary>
public sealed record OperationResult(bool Succeeded, string? ErrorKey = null)
{
    public static OperationResult Ok { get; } = new(true);

    public static OperationResult Fail(string errorKey) => new(false, errorKey);
}

/// <summary>Localization keys shared by features. A unit test checks every key exists in the resource file.</summary>
public static class CommonErrors
{
    public const string Concurrency = "Error.Concurrency";
    public const string NotFound = "Error.NotFound";
}
