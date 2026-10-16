namespace Signage.Web.Domain;

public enum UserRole
{
    Editor,
    Admin,
}

public enum SlideType
{
    Image,
    Video,
    WebPage,
    Html,
}

public enum ScreenMode
{
    Carousel,
    SingleVideo,
}

/// <summary>Where a slide attached directly to a screen plays: before or after everything else (R-12).</summary>
public enum SlidePlacement
{
    Prepend,
    Append,
}
