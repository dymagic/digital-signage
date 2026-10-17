using Signage.Web.Features.Pairing;

namespace Signage.UnitTests.Pairing;

public class ScreenTokensTests
{
    [Fact]
    public void Secrets_are_256_bit_url_safe_and_unique()
    {
        var secrets = Enumerable.Range(0, 200).Select(_ => ScreenTokens.NewSecret()).ToList();

        Assert.All(secrets, s =>
        {
            Assert.Equal(43, s.Length); // 32 bytes, base64url without padding
            Assert.Matches("^[A-Za-z0-9_-]+$", s);
        });
        Assert.Equal(secrets.Count, secrets.Distinct().Count());
    }

    [Fact]
    public void Hashes_are_sha256_hex_and_match_only_their_secret()
    {
        var secret = ScreenTokens.NewSecret();
        var hash = ScreenTokens.Hash(secret);

        Assert.Equal(64, hash.Length);
        Assert.NotEqual(secret, hash);
        Assert.True(ScreenTokens.Matches(secret, hash));
        Assert.False(ScreenTokens.Matches(ScreenTokens.NewSecret(), hash));
        Assert.False(ScreenTokens.Matches(null, hash));
        Assert.False(ScreenTokens.Matches(secret, null));
    }

    [Fact]
    public void The_hash_of_a_known_value_is_stable()
    {
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", ScreenTokens.Hash("hello"));
    }
}
