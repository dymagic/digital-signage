using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Signage.Web.Domain;

namespace Signage.Web.Data.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Email).HasMaxLength(Limits.Email);
        builder.Property(u => u.NormalizedEmail).HasMaxLength(Limits.Email);
        builder.Property(u => u.DisplayName).HasMaxLength(Limits.Name);
        builder.Property(u => u.PasswordHash).HasMaxLength(Limits.Description);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(Limits.Enum);
        builder.Property(u => u.SecurityStamp).HasMaxLength(Limits.ShortText);
        builder.Property(u => u.Version).IsRowVersion();
        builder.HasIndex(u => u.NormalizedEmail).IsUnique();
    }
}

internal sealed class SystemSettingsConfiguration : IEntityTypeConfiguration<SystemSettings>
{
    public void Configure(EntityTypeBuilder<SystemSettings> builder)
    {
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.TimeZoneId).HasMaxLength(Limits.ShortText);
        builder.Property(s => s.Version).IsRowVersion();
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_system_settings_singleton", $"id = {SystemSettings.SingletonId}");
            t.HasCheckConstraint("ck_system_settings_duration_positive", "default_slide_duration_seconds > 0");
        });
    }
}

internal sealed class UserLockoutConfiguration : IEntityTypeConfiguration<UserLockout>
{
    public void Configure(EntityTypeBuilder<UserLockout> builder)
    {
        builder.HasKey(l => l.UserId);
        builder.HasOne<User>().WithOne().HasForeignKey<UserLockout>(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
