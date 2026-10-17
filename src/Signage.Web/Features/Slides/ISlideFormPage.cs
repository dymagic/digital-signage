using Signage.Web.Domain;

namespace Signage.Web.Features.Slides;

/// <summary>What the shared slide form partial needs from the create and edit pages.</summary>
public interface ISlideFormPage
{
    SlideType Type { get; }

    SlideInput Input { get; }

    /// <summary>The system time zone the schedule fields are entered in (R-03).</summary>
    string TimeZoneId { get; }
}
