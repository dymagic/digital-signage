using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Signage.Web.Domain;

namespace Signage.Web.Data;

public class SignageDbContext(DbContextOptions<SignageDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>ICU, case- and accent-sensitive but case-insensitive for equality and uniqueness.</summary>
    public const string CaseInsensitiveCollation = "case_insensitive";

    public DbSet<User> Users => Set<User>();
    public DbSet<Slide> Slides => Set<Slide>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistSlide> PlaylistSlides => Set<PlaylistSlide>();
    public DbSet<Screen> Screens => Set<Screen>();
    public DbSet<ScreenSlide> ScreenSlides => Set<ScreenSlide>();
    public DbSet<ScreenPlaylist> ScreenPlaylists => Set<ScreenPlaylist>();
    public DbSet<ScreenGroup> ScreenGroups => Set<ScreenGroup>();
    public DbSet<ScreenGroupMember> ScreenGroupMembers => Set<ScreenGroupMember>();
    public DbSet<GroupPlaylist> GroupPlaylists => Set<GroupPlaylist>();
    public DbSet<PairingRequest> PairingRequests => Set<PairingRequest>();
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();

    /// <summary>ASP.NET Core data-protection key ring, shared by all replicas (R-63).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasCollation(CaseInsensitiveCollation, locale: "und-u-ks-level2", provider: "icu", deterministic: false);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SignageDbContext).Assembly);
    }
}
