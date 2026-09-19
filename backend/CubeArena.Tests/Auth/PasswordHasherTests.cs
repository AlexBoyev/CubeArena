using CubeArena.Api.Features.Auth;

namespace CubeArena.Tests.Auth;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Verify_ReturnsTrue_ForCorrectPassword()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.True(_hasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForWrongPassword()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.False(_hasher.Verify("wrong password", hash));
    }

    [Fact]
    public void Hash_ProducesDifferentOutput_ForSamePasswordEachTime()
    {
        var first = _hasher.Hash("same password");
        var second = _hasher.Hash("same password");

        Assert.NotEqual(first, second);
        Assert.True(_hasher.Verify("same password", first));
        Assert.True(_hasher.Verify("same password", second));
    }
}
