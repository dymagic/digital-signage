using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Signage.Web.Domain;

namespace Signage.Web.Data.Configurations;

internal sealed class SlideConfiguration : IEntityTypeConfiguration<Slide>
{
    public void Configure(EntityTypeBuilder<Slide> builder)
    {
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Title).HasMaxLength(Limits.Title);
        builder.Property(s => s.Type).HasConversion<string>().HasMaxLength(Limits.Enum);
        builder.Property(s => s.Url).HasMaxLength(Limits.Url);
        builder.Property(s => s.Version).IsRowVersion();
        builder.HasIndex(s => s.Title);
        builder.ToTable(t => t.HasCheckConstraint("ck_slides_duration_positive", "duration_seconds IS NULL OR duration_seconds > 0"));
    }
}

internal sealed class PlaylistConfiguration : IEntityTypeConfiguration<Playlist>
{
    public void Configure(EntityTypeBuilder<Playlist> builder)
    {
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(Limits.Name).UseCollation(SignageDbContext.CaseInsensitiveCollation);
        builder.Property(p => p.Description).HasMaxLength(Limits.Description);
        builder.Property(p => p.Version).IsRowVersion();
        builder.HasIndex(p => p.Name).IsUnique();
    }
}

internal sealed class PlaylistSlideConfiguration : IEntityTypeConfiguration<PlaylistSlide>
{
    public void Configure(EntityTypeBuilder<PlaylistSlide> builder)
    {
        builder.Property(ps => ps.Id).ValueGeneratedNever();
        builder.HasOne(ps => ps.Playlist).WithMany(p => p.Slides).HasForeignKey(ps => ps.PlaylistId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(ps => ps.Slide).WithMany().HasForeignKey(ps => ps.SlideId).OnDelete(DeleteBehavior.Cascade);

        // Not unique: dense positions are rewritten on reorder, and a unique index would trip on swaps.
        builder.HasIndex(ps => new { ps.PlaylistId, ps.Position });
    }
}
