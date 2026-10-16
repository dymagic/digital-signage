namespace Signage.Web.Domain;

/// <summary>Set automatically on insert by <c>Data.TimestampInterceptor</c> unless already set.</summary>
public interface IHasCreatedAt
{
    DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Set automatically on every insert and update by <c>Data.TimestampInterceptor</c>.</summary>
public interface IHasUpdatedAt
{
    DateTimeOffset UpdatedAt { get; set; }
}
