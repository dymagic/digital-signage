using System.ComponentModel.DataAnnotations;

namespace Signage.Web.Features.Slides;

/// <summary>What an editor fills in for a slide. Schedule times are wall-clock times in the system time zone.</summary>
public sealed class SlideInput
{
    [Display(Name = "Slides.Field.Title")]
    public string? Title { get; set; }

    [Display(Name = "Slides.Field.Url")]
    public string? Url { get; set; }

    [Display(Name = "Slides.Field.Html")]
    public string? Html { get; set; }

    [Display(Name = "Slides.Field.Duration")]
    public int? DurationSeconds { get; set; }

    [Display(Name = "Slides.Field.PlayAudio")]
    public bool PlayAudio { get; set; }

    [Display(Name = "Slides.Field.ActiveFrom")]
    public DateTime? ActiveFrom { get; set; }

    [Display(Name = "Slides.Field.ActiveUntil")]
    public DateTime? ActiveUntil { get; set; }

    [Display(Name = "Slides.Field.IsEnabled")]
    public bool IsEnabled { get; set; } = true;
}
