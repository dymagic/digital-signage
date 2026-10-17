using Signage.Web.Data.Configurations;
using Signage.Web.Domain;

namespace Signage.Web.Features.Slides;

/// <summary>A slide that passed validation: only the fields that apply to its type are set, times are UTC.</summary>
public sealed record ValidSlide(
    string Title,
    string? Url,
    string? Html,
    int? DurationSeconds,
    bool PlayAudio,
    DateTimeOffset? ActiveFromUtc,
    DateTimeOffset? ActiveUntilUtc,
    bool IsEnabled);

/// <summary>Errors per input field (localization keys), warnings that don't block saving, or the valid slide.</summary>
public sealed record SlideValidation(ValidSlide? Slide, IReadOnlyDictionary<string, string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Slide is not null;
}

/// <summary>R-01–R-07 input rules. Pure: no database, no clock.</summary>
public static class SlideValidator
{
    public const int MinDuration = 1;
    public const int MaxDuration = 3600;
    public const int MaxHtmlLength = 200_000;

    public static SlideValidation Validate(SlideType type, SlideInput input, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<string>();

        var title = input.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            errors[nameof(SlideInput.Title)] = SlideErrors.TitleRequired;
        }
        else if (title.Length > Limits.Title)
        {
            errors[nameof(SlideInput.Title)] = SlideErrors.TitleTooLong;
        }

        string? url = null;
        string? html = null;
        if (type == SlideType.Html)
        {
            html = input.Html;
            if (string.IsNullOrWhiteSpace(html))
            {
                errors[nameof(SlideInput.Html)] = SlideErrors.HtmlRequired;
            }
            else if (html.Length > MaxHtmlLength)
            {
                errors[nameof(SlideInput.Html)] = SlideErrors.HtmlTooLong;
            }
        }
        else
        {
            url = input.Url?.Trim();
            if (string.IsNullOrEmpty(url))
            {
                errors[nameof(SlideInput.Url)] = SlideErrors.UrlRequired;
            }
            else if (url.Length > Limits.Url || !IsAbsoluteHttpUrl(url))
            {
                errors[nameof(SlideInput.Url)] = SlideErrors.UrlInvalid;
            }
            else if (type == SlideType.Video && !LooksLikeMp4(url))
            {
                warnings.Add(SlideErrors.VideoNotMp4Warning);
            }
        }

        // Videos play until they end (R-02); the duration doesn't apply to them.
        int? duration = type == SlideType.Video ? null : input.DurationSeconds;
        if (duration is < MinDuration or > MaxDuration)
        {
            errors[nameof(SlideInput.DurationSeconds)] = SlideErrors.DurationRange;
        }

        DateTimeOffset? from = null;
        DateTimeOffset? until = null;
        if (input.ActiveFrom is { } localFrom)
        {
            if (ScheduleClock.TryToUtc(localFrom, zone, out var utc))
            {
                from = utc;
            }
            else
            {
                errors[nameof(SlideInput.ActiveFrom)] = SlideErrors.TimeDoesNotExist;
            }
        }

        if (input.ActiveUntil is { } localUntil)
        {
            if (ScheduleClock.TryToUtc(localUntil, zone, out var utc))
            {
                until = utc;
            }
            else
            {
                errors[nameof(SlideInput.ActiveUntil)] = SlideErrors.TimeDoesNotExist;
            }
        }

        if (from is not null && until is not null && until <= from)
        {
            errors[nameof(SlideInput.ActiveUntil)] = SlideErrors.UntilBeforeFrom;
        }

        if (errors.Count > 0)
        {
            return new SlideValidation(null, errors, warnings);
        }

        return new SlideValidation(
            new ValidSlide(title!, url, html, duration, type == SlideType.Video && input.PlayAudio, from, until, input.IsEnabled),
            errors,
            warnings);
    }

    public static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host);

    private static bool LooksLikeMp4(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Localization keys for slide validation. A unit test checks every one exists in the resource file.</summary>
public static class SlideErrors
{
    public const string TitleRequired = "Slides.Error.TitleRequired";
    public const string TitleTooLong = "Slides.Error.TitleTooLong";
    public const string UrlRequired = "Slides.Error.UrlRequired";
    public const string UrlInvalid = "Slides.Error.UrlInvalid";
    public const string HtmlRequired = "Slides.Error.HtmlRequired";
    public const string HtmlTooLong = "Slides.Error.HtmlTooLong";
    public const string DurationRange = "Slides.Error.DurationRange";
    public const string TimeDoesNotExist = "Slides.Error.TimeDoesNotExist";
    public const string UntilBeforeFrom = "Slides.Error.UntilBeforeFrom";
    public const string VideoNotMp4Warning = "Slides.Warning.VideoNotMp4";
}
