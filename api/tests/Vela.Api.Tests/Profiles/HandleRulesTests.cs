using Vela.Api.Profiles;

namespace Vela.Api.Tests.Profiles;

public class HandleRulesTests
{
    [Theory]
    [InlineData("abc", true)]
    [InlineData("a_b_9", true)]
    [InlineData("abcdefghij0123456789", true)]
    [InlineData("ab", false)]
    [InlineData("abcdefghij01234567890", false)]
    [InlineData("has space", false)]
    [InlineData("dash-ed", false)]
    [InlineData("dot.ted", false)]
    [InlineData("émile", false)]
    [InlineData("日本語です", false)]
    [InlineData("abc\n", true)] // trimmed by Normalize
    [InlineData("a\nbc", false)]
    [InlineData("", false)]
    public void Handle_must_be_3_to_20_lowercase_alnum_underscore(string handle, bool expected) =>
        Assert.Equal(expected, HandleRules.IsValid(handle));

    [Fact]
    public void Uppercase_is_normalized_before_validation()
    {
        Assert.Equal("mixed_case", HandleRules.Normalize("  Mixed_CASE "));
        Assert.True(HandleRules.IsValid("Mixed_CASE"));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Vela")]
    [InlineData("notifications")]
    [InlineData("null")]
    [InlineData("me")]
    public void Reserved_handles_are_invalid(string handle) => Assert.False(HandleRules.IsValid(handle));

    [Fact]
    public void Null_is_invalid() => Assert.False(HandleRules.IsValid(null!));
}
