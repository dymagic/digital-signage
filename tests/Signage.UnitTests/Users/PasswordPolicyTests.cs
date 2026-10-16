using Signage.Web.Features.Users;

namespace Signage.UnitTests.Users;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData(null, AccountErrors.PasswordTooShort)]
    [InlineData("", AccountErrors.PasswordTooShort)]
    [InlineData("123456789", AccountErrors.PasswordTooShort)]
    [InlineData("1234567890", null)]
    [InlineData("correct horse battery staple", null)]
    public void Passwords_need_at_least_ten_characters(string? password, string? expectedError)
    {
        Assert.Equal(expectedError, PasswordPolicy.Validate(password));
    }

    [Fact]
    public void Very_long_passwords_are_rejected()
    {
        Assert.Null(PasswordPolicy.Validate(new string('x', PasswordPolicy.MaxLength)));
        Assert.Equal(AccountErrors.PasswordTooLong, PasswordPolicy.Validate(new string('x', PasswordPolicy.MaxLength + 1)));
    }

    [Fact]
    public void Temporary_passwords_are_valid_random_and_free_of_look_alike_characters()
    {
        var samples = Enumerable.Range(0, 200).Select(_ => PasswordPolicy.GenerateTemporary()).ToList();

        Assert.All(samples, p =>
        {
            Assert.Null(PasswordPolicy.Validate(p));
            Assert.Equal(16, p.Length);
            Assert.DoesNotContain(p, c => "0O1lI".Contains(c, StringComparison.Ordinal));
        });
        Assert.Equal(samples.Count, samples.Distinct(StringComparer.Ordinal).Count());
    }
}
