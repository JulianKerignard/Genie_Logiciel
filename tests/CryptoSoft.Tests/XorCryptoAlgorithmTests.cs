namespace CryptoSoft.Tests;

public sealed class XorCryptoAlgorithmTests
{
    [Fact]
    public void Transform_TwiceWithSameKey_ReturnsOriginalBytes()
    {
        var algo = new XorCryptoAlgorithm(0xA5);
        byte[] plain = "the quick brown fox"u8.ToArray();

        byte[] encrypted = algo.Transform(plain);
        byte[] roundTrip = algo.Transform(encrypted);

        Assert.NotEqual(plain, encrypted);
        Assert.Equal(plain, roundTrip);
    }

    [Fact]
    public void Transform_EmptyInput_ReturnsEmptyOutput()
    {
        var algo = new XorCryptoAlgorithm(0x42);

        byte[] result = algo.Transform(Array.Empty<byte>());

        Assert.Empty(result);
    }

    [Fact]
    public void Transform_DoesNotMutateInput()
    {
        var algo = new XorCryptoAlgorithm(0x7F);
        byte[] plain = new byte[] { 0x01, 0x02, 0x03 };
        byte[] snapshot = plain.ToArray();

        _ = algo.Transform(plain);

        Assert.Equal(snapshot, plain);
    }

    [Fact]
    public void Transform_NullInput_Throws()
    {
        var algo = new XorCryptoAlgorithm(0x00);

        Assert.Throws<ArgumentNullException>(() => algo.Transform(null!));
    }
}
