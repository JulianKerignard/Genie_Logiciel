using EasySave.Services;

namespace EasySave.Tests;

public class EncryptResultTests
{
    [Fact]
    public void Succeeded_PositiveTime_ReturnsConsistentResult()
    {
        var result = EncryptResult.Succeeded(42);

        Assert.True(result.Success);
        Assert.Equal(42, result.EncryptionTimeMs);
    }

    [Fact]
    public void Succeeded_ZeroTime_AllowedAsBoundaryCase()
    {
        var result = EncryptResult.Succeeded(0);

        Assert.True(result.Success);
        Assert.Equal(0, result.EncryptionTimeMs);
    }

    [Fact]
    public void Succeeded_NegativeTime_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EncryptResult.Succeeded(-1));
    }

    [Fact]
    public void Failed_DefaultErrorCode_IsMinusOne()
    {
        var result = EncryptResult.Failed();

        Assert.False(result.Success);
        Assert.Equal(-1, result.EncryptionTimeMs);
    }

    [Fact]
    public void Failed_CustomNegativeErrorCode_Preserved()
    {
        var result = EncryptResult.Failed(-42);

        Assert.False(result.Success);
        Assert.Equal(-42, result.EncryptionTimeMs);
    }

    [Fact]
    public void Failed_NonNegativeErrorCode_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EncryptResult.Failed(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncryptResult.Failed(5));
    }
}
