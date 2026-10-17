using Signage.Web.Features.Pairing;

namespace Signage.UnitTests.Pairing;

public class PairingCodeTests
{
    [Fact]
    public void Codes_have_six_characters_from_the_unambiguous_alphabet()
    {
        var codes = Enumerable.Range(0, 500).Select(_ => PairingCode.Generate()).ToList();

        Assert.All(codes, c =>
        {
            Assert.Equal(PairingCode.Length, c.Length);
            Assert.All(c, ch => Assert.Contains(ch, PairingCode.Alphabet));
        });
        Assert.True(codes.Distinct().Count() > 490);
    }

    [Fact]
    public void The_alphabet_leaves_out_look_alike_characters()
    {
        Assert.DoesNotContain(PairingCode.Alphabet, c => "0O1IL2Z5S8B".Contains(c, StringComparison.Ordinal));
    }

    [Fact]
    public void Codes_are_shown_as_two_groups_of_three()
    {
        Assert.Equal("K7P-Q3M", PairingCode.Format("K7PQ3M"));
    }

    [Theory]
    [InlineData("k7p-q3m", "K7PQ3M")]
    [InlineData(" K7P Q3M ", "K7PQ3M")]
    [InlineData("K7PQ3M", "K7PQ3M")]
    [InlineData("K7PQ3", null)]
    [InlineData("K7PQ3MX", null)]
    [InlineData("K0PQ3M", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Typed_codes_are_normalised_or_rejected(string? typed, string? expected)
    {
        Assert.Equal(expected, PairingCode.Normalize(typed));
    }
}
