using Signage.Web.Domain;
using static Signage.UnitTests.Domain.GraphBuilder;

namespace Signage.UnitTests.Domain;

public class SequenceResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static string[] Titles(Screen screen, DateTimeOffset? now = null) =>
        SequenceResolver.Resolve(screen, now ?? Now).Select(r => r.Slide.Title).ToArray();

    [Fact]
    public void Screen_with_nothing_assigned_resolves_to_an_empty_sequence()
    {
        Assert.Empty(SequenceResolver.Resolve(Screen(), Now));
    }

    [Fact]
    public void Sections_come_in_order_prepend_groups_screen_playlists_append()
    {
        var screen = Screen()
            .Append(0, Slide("append"))
            .WithPlaylist(0, Playlist("direct", Slide("direct")))
            .InGroup(Group("G", 0, Playlist("grouped", Slide("grouped"))))
            .Prepend(0, Slide("prepend"));

        Assert.Equal(["prepend", "grouped", "direct", "append"], Titles(screen));
    }

    [Fact]
    public void Prepended_and_appended_slides_follow_their_positions_not_insertion_order()
    {
        var screen = Screen()
            .Prepend(1, Slide("p1")).Prepend(0, Slide("p0")).Prepend(2, Slide("p2"))
            .Append(1, Slide("a1")).Append(0, Slide("a0"));

        Assert.Equal(["p0", "p1", "p2", "a0", "a1"], Titles(screen));
    }

    [Fact]
    public void Groups_are_ordered_by_sort_order_then_name()
    {
        var screen = Screen()
            .InGroup(Group("Zeta", 1, Playlist("z", Slide("zeta"))))
            .InGroup(Group("beta", 0, Playlist("b", Slide("beta"))))
            .InGroup(Group("Alpha", 0, Playlist("a", Slide("alpha"))))
            .InGroup(Group("Gamma", -1, Playlist("g", Slide("gamma"))));

        Assert.Equal(["gamma", "alpha", "beta", "zeta"], Titles(screen));
    }

    [Fact]
    public void Group_name_ordering_ignores_case()
    {
        var screen = Screen()
            .InGroup(Group("b-group", 0, Playlist("1", Slide("b"))))
            .InGroup(Group("A-group", 0, Playlist("2", Slide("a"))))
            .InGroup(Group("c-group", 0, Playlist("3", Slide("c"))));

        Assert.Equal(["a", "b", "c"], Titles(screen));
    }

    [Fact]
    public void A_groups_playlists_follow_their_positions()
    {
        var group = Group("G", 0, Playlist("first", Slide("1")), Playlist("second", Slide("2")));
        group.Playlists[0].Position = 1;
        group.Playlists[1].Position = 0;

        Assert.Equal(["2", "1"], Titles(Screen().InGroup(group)));
    }

    [Fact]
    public void Screen_playlists_follow_their_positions()
    {
        var screen = Screen()
            .WithPlaylist(2, Playlist("c", Slide("c")))
            .WithPlaylist(0, Playlist("a", Slide("a")))
            .WithPlaylist(1, Playlist("b", Slide("b")));

        Assert.Equal(["a", "b", "c"], Titles(screen));
    }

    [Fact]
    public void Slides_within_a_playlist_follow_their_positions()
    {
        var playlist = Playlist("p", Slide("x"), Slide("y"), Slide("z"));
        playlist.Slides[0].Position = 2;
        playlist.Slides[2].Position = 0;

        Assert.Equal(["z", "y", "x"], Titles(Screen().WithPlaylist(0, playlist)));
    }

    [Fact]
    public void A_playlist_in_two_groups_plays_only_in_the_first_group()
    {
        var shared = Playlist("shared", Slide("s"));
        var first = Group("A", 0, shared, Playlist("a-own", Slide("a")));
        var second = Group("B", 1, Playlist("b-own", Slide("b")), shared);
        var screen = Screen().InGroup(second).InGroup(first);

        var resolved = SequenceResolver.Resolve(screen, Now);

        Assert.Equal(["s", "a", "b"], resolved.Select(r => r.Slide.Title));
        Assert.Equal(first.Id, resolved[0].Source.GroupId);
    }

    [Fact]
    public void A_playlist_in_a_group_and_assigned_directly_plays_only_at_the_group_position()
    {
        var shared = Playlist("shared", Slide("s"));
        var screen = Screen()
            .InGroup(Group("G", 0, shared))
            .WithPlaylist(0, Playlist("direct", Slide("d")))
            .WithPlaylist(1, shared);

        var resolved = SequenceResolver.Resolve(screen, Now);

        Assert.Equal(["s", "d"], resolved.Select(r => r.Slide.Title));
        Assert.Equal(SequenceSourceKind.Group, resolved[0].Source.Kind);
    }

    [Fact]
    public void A_playlist_assigned_twice_directly_plays_once()
    {
        var shared = Playlist("shared", Slide("s"));
        var screen = Screen().WithPlaylist(0, shared).WithPlaylist(1, Playlist("other", Slide("o"))).WithPlaylist(2, shared);

        Assert.Equal(["s", "o"], Titles(screen));
    }

    [Fact]
    public void A_slide_repeated_within_a_playlist_plays_each_time()
    {
        var slide = Slide("again");
        var screen = Screen().WithPlaylist(0, Playlist("p", slide, Slide("between"), slide));

        Assert.Equal(["again", "between", "again"], Titles(screen));
    }

    [Fact]
    public void The_same_slide_in_different_playlists_plays_in_each()
    {
        var slide = Slide("common");
        var screen = Screen()
            .WithPlaylist(0, Playlist("one", slide))
            .WithPlaylist(1, Playlist("two", slide));

        Assert.Equal(["common", "common"], Titles(screen));
    }

    [Fact]
    public void A_slide_both_prepended_and_in_a_playlist_plays_in_both_places()
    {
        var slide = Slide("both");
        var screen = Screen().Prepend(0, slide).WithPlaylist(0, Playlist("p", slide)).Append(0, slide);

        Assert.Equal(["both", "both", "both"], Titles(screen));
    }

    [Fact]
    public void Disabled_slides_are_skipped_in_every_section()
    {
        var screen = Screen()
            .Prepend(0, Slide("p-off", enabled: false)).Prepend(1, Slide("p-on"))
            .InGroup(Group("G", 0, Playlist("g", Slide("g-off", enabled: false), Slide("g-on"))))
            .WithPlaylist(0, Playlist("d", Slide("d-on"), Slide("d-off", enabled: false)))
            .Append(0, Slide("a-off", enabled: false));

        Assert.Equal(["p-on", "g-on", "d-on"], Titles(screen));
    }

    [Fact]
    public void A_playlist_whose_slides_are_all_filtered_still_counts_for_deduplication()
    {
        var shared = Playlist("shared", Slide("off", enabled: false));
        var screen = Screen().InGroup(Group("G", 0, shared)).WithPlaylist(0, shared).WithPlaylist(1, Playlist("p", Slide("x")));

        Assert.Equal(["x"], Titles(screen));
    }

    [Fact]
    public void Empty_playlists_contribute_nothing()
    {
        var screen = Screen().WithPlaylist(0, Playlist("empty")).InGroup(Group("G", 0, Playlist("also empty")));

        Assert.Empty(SequenceResolver.Resolve(screen, Now));
    }

    [Fact]
    public void Out_of_schedule_slides_are_skipped_in_every_section()
    {
        var past = Now.AddDays(-2);
        var screen = Screen()
            .Prepend(0, Slide("p-expired", until: Now.AddDays(-1)))
            .InGroup(Group("G", 0, Playlist("g", Slide("g-future", from: Now.AddDays(1)), Slide("g-now", from: past))))
            .WithPlaylist(0, Playlist("d", Slide("d-window", from: past, until: Now.AddDays(1))))
            .Append(0, Slide("a-expired", from: past, until: Now.AddHours(-1)));

        Assert.Equal(["g-now", "d-window"], Titles(screen));
    }

    [Fact]
    public void Schedule_boundaries_are_inclusive_at_the_start_and_exclusive_at_the_end()
    {
        var from = Now;
        var until = Now.AddHours(1);
        var screen = Screen().WithPlaylist(0, Playlist("p", Slide("s", from: from, until: until)));

        Assert.Empty(Titles(screen, from.AddTicks(-1)));
        Assert.Equal(["s"], Titles(screen, from));
        Assert.Equal(["s"], Titles(screen, until.AddTicks(-1)));
        Assert.Empty(Titles(screen, until));
    }

    [Fact]
    public void Sources_describe_where_each_slide_came_from()
    {
        var grouped = Playlist("Canteen", Slide("g"));
        var direct = Playlist("Marketing", Slide("d"));
        var group = Group("Grand hall", 0, grouped);
        var screen = Screen().Prepend(0, Slide("p")).InGroup(group).WithPlaylist(0, direct).Append(0, Slide("a"));

        var sources = SequenceResolver.Resolve(screen, Now).Select(r => r.Source).ToArray();

        Assert.Equal(new SequenceSource(SequenceSourceKind.Prepend), sources[0]);
        Assert.Equal(new SequenceSource(SequenceSourceKind.Group, group.Id, "Grand hall", grouped.Id, "Canteen"), sources[1]);
        Assert.Equal(new SequenceSource(SequenceSourceKind.ScreenPlaylist, null, null, direct.Id, "Marketing"), sources[2]);
        Assert.Equal(new SequenceSource(SequenceSourceKind.Append), sources[3]);
    }

    [Fact]
    public void A_full_composition_matches_the_R12_example()
    {
        // Prepend P1, P2; group A (sort 0): L1, L2; group B (sort 0): L2, L3; direct: L3, L4; append X.
        var l1 = Playlist("L1", Slide("l1a"), Slide("l1b"));
        var l2 = Playlist("L2", Slide("l2"));
        var l3 = Playlist("L3", Slide("l3"));
        var l4 = Playlist("L4", Slide("l4-off", enabled: false), Slide("l4"));
        var screen = Screen()
            .Prepend(0, Slide("P1")).Prepend(1, Slide("P2"))
            .InGroup(Group("B", 0, l2, l3))
            .InGroup(Group("A", 0, l1, l2))
            .WithPlaylist(0, l3).WithPlaylist(1, l4)
            .Append(0, Slide("X"));

        Assert.Equal(["P1", "P2", "l1a", "l1b", "l2", "l3", "l4", "X"], Titles(screen));
    }
}
