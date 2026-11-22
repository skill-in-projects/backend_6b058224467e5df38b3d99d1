using Backend.Domain;
using Backend.Filters;

namespace Tests;

public class SecurityTests
{
    [Fact]
    public void Manage_tokens_are_url_safe_unique_and_only_the_hash_is_kept()
    {
        var a = ManageToken.Create();
        var b = ManageToken.Create();

        Assert.NotEqual(a, b);
        Assert.True(ManageToken.LooksValid(a));
        Assert.Equal(64, ManageToken.Hash(a).Length);
        Assert.Equal(ManageToken.Hash(a), ManageToken.Hash(a));
        Assert.DoesNotContain(a, ManageToken.Hash(a));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("../../../../etc/passwd/aaaaaaaaaaa")]
    public void Junk_tokens_never_reach_the_database(string? token)
    {
        Assert.False(ManageToken.LooksValid(token));
    }

    [Fact]
    public void Staff_key_must_match_exactly_and_an_empty_key_never_matches()
    {
        Assert.True(StaffKeyAttribute.Matches("front-desk-2026", "front-desk-2026"));
        Assert.False(StaffKeyAttribute.Matches("front-desk-2025", "front-desk-2026"));
        Assert.False(StaffKeyAttribute.Matches("", ""));
    }
}
