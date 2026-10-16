using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Signage.Web.Domain;

namespace Signage.Web.Data.Configurations;

internal sealed class ScreenConfiguration : IEntityTypeConfiguration<Screen>
{
    public void Configure(EntityTypeBuilder<Screen> builder)
    {
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(Limits.Name).UseCollation(SignageDbContext.CaseInsensitiveCollation);
        builder.Property(s => s.Location).HasMaxLength(Limits.Description);
        builder.Property(s => s.Mode).HasConversion<string>().HasMaxLength(Limits.Enum);
        builder.Property(s => s.SingleVideoUrl).HasMaxLength(Limits.Url);
        builder.Property(s => s.TokenHash).HasMaxLength(Limits.Hash);
        builder.Property(s => s.LastIp).HasMaxLength(Limits.Ip);
        builder.Property(s => s.PlayerVersion).HasMaxLength(Limits.ShortText);
        builder.Property(s => s.UserAgent).HasMaxLength(Limits.UserAgent);
        builder.Property(s => s.Version).IsRowVersion();
        builder.HasIndex(s => s.Name).IsUnique();
        builder.HasIndex(s => s.TokenHash).IsUnique().HasFilter("token_hash IS NOT NULL");
    }
}

internal sealed class ScreenSlideConfiguration : IEntityTypeConfiguration<ScreenSlide>
{
    public void Configure(EntityTypeBuilder<ScreenSlide> builder)
    {
        builder.Property(ss => ss.Id).ValueGeneratedNever();
        builder.Property(ss => ss.Placement).HasConversion<string>().HasMaxLength(Limits.Enum);
        builder.HasOne(ss => ss.Screen).WithMany(s => s.Slides).HasForeignKey(ss => ss.ScreenId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(ss => ss.Slide).WithMany().HasForeignKey(ss => ss.SlideId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(ss => new { ss.ScreenId, ss.Placement, ss.Position });
    }
}

internal sealed class ScreenPlaylistConfiguration : IEntityTypeConfiguration<ScreenPlaylist>
{
    public void Configure(EntityTypeBuilder<ScreenPlaylist> builder)
    {
        builder.HasKey(sp => new { sp.ScreenId, sp.PlaylistId });
        builder.HasOne(sp => sp.Screen).WithMany(s => s.Playlists).HasForeignKey(sp => sp.ScreenId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(sp => sp.Playlist).WithMany().HasForeignKey(sp => sp.PlaylistId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(sp => sp.PlaylistId);
    }
}

internal sealed class ScreenGroupConfiguration : IEntityTypeConfiguration<ScreenGroup>
{
    public void Configure(EntityTypeBuilder<ScreenGroup> builder)
    {
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Name).HasMaxLength(Limits.Name).UseCollation(SignageDbContext.CaseInsensitiveCollation);
        builder.Property(g => g.Description).HasMaxLength(Limits.Description);
        builder.Property(g => g.Version).IsRowVersion();
        builder.HasIndex(g => g.Name).IsUnique();
    }
}

internal sealed class ScreenGroupMemberConfiguration : IEntityTypeConfiguration<ScreenGroupMember>
{
    public void Configure(EntityTypeBuilder<ScreenGroupMember> builder)
    {
        builder.HasKey(m => new { m.ScreenId, m.GroupId });
        builder.HasOne(m => m.Screen).WithMany(s => s.Groups).HasForeignKey(m => m.ScreenId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.Group).WithMany(g => g.Members).HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => m.GroupId);
    }
}

internal sealed class GroupPlaylistConfiguration : IEntityTypeConfiguration<GroupPlaylist>
{
    public void Configure(EntityTypeBuilder<GroupPlaylist> builder)
    {
        builder.HasKey(gp => new { gp.GroupId, gp.PlaylistId });
        builder.HasOne(gp => gp.Group).WithMany(g => g.Playlists).HasForeignKey(gp => gp.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(gp => gp.Playlist).WithMany().HasForeignKey(gp => gp.PlaylistId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(gp => gp.PlaylistId);
    }
}

internal sealed class PairingRequestConfiguration : IEntityTypeConfiguration<PairingRequest>
{
    public void Configure(EntityTypeBuilder<PairingRequest> builder)
    {
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Code).HasMaxLength(Limits.ShortText);
        builder.Property(p => p.DeviceSecretHash).HasMaxLength(Limits.Hash);
        builder.HasOne(p => p.ClaimedScreen).WithMany().HasForeignKey(p => p.ClaimedScreenId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(p => p.Code);
        builder.HasIndex(p => p.ExpiresAt);
    }
}
