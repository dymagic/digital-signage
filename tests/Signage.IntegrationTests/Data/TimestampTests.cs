using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Data;

public class TimestampTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Created_and_updated_times_come_from_the_time_provider()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));
        await using var factory = new SignageWebFactory(await postgres.CreateDatabaseAsync(), timeProvider: time);
        var created = time.GetUtcNow();
        var slide = new Slide { Title = "t", Type = SlideType.Html, Html = "x" };
        await factory.WithDbAsync(async db =>
        {
            db.Slides.Add(slide);
            await db.SaveChangesAsync();
        });

        time.Advance(TimeSpan.FromMinutes(5));
        await factory.WithDbAsync(async db =>
        {
            var tracked = await db.Slides.SingleAsync(s => s.Id == slide.Id);
            tracked.Title = "changed";
            await db.SaveChangesAsync();
        });

        var loaded = await factory.WithDbAsync(db => db.Slides.AsNoTracking().SingleAsync(s => s.Id == slide.Id));
        Assert.Equal(created, loaded.CreatedAt);
        Assert.Equal(created.AddMinutes(5), loaded.UpdatedAt);
    }

    [Fact]
    public async Task An_explicit_created_time_is_kept()
    {
        await using var factory = new SignageWebFactory(await postgres.CreateDatabaseAsync());
        var explicitTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var screen = new Screen { Name = "S", CreatedAt = explicitTime };

        await factory.WithDbAsync(async db =>
        {
            db.Screens.Add(screen);
            await db.SaveChangesAsync();
        });

        Assert.Equal(explicitTime, (await factory.WithDbAsync(db => db.Screens.AsNoTracking().SingleAsync())).CreatedAt);
    }
}
