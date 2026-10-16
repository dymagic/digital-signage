namespace Signage.Web.Data.Configurations;

/// <summary>Column length limits, shared with form validation.</summary>
public static class Limits
{
    public const int Enum = 20;
    public const int ShortText = 64;
    public const int Ip = 64;
    public const int Hash = 64;
    public const int Name = 100;
    public const int Title = 200;
    public const int Email = 320;
    public const int UserAgent = 512;
    public const int Description = 1000;
    public const int Url = 2048;
}
