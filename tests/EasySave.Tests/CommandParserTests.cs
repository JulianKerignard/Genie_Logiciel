using EasySave.CLI;

namespace EasySave.Tests;

public class CommandParserTests
{
    private readonly CommandParser _parser = new();

    [Theory]
    [InlineData("1", new[] { 1 })]
    [InlineData("3", new[] { 3 })]
    [InlineData("5", new[] { 5 })]
    [InlineData("1-3", new[] { 1, 2, 3 })]
    [InlineData("1;3", new[] { 1, 3 })]
    [InlineData("1-3;5", new[] { 1, 2, 3, 5 })]
    [InlineData("1;1;2", new[] { 1, 2 })]              // dedup
    [InlineData("3;1", new[] { 1, 3 })]                // sorted
    [InlineData(" 1 ; 2 ", new[] { 1, 2 })]            // whitespace tolerated
    public void ParseJobSelection_ValidInput_ReturnsExpectedIndices(string input, int[] expected)
    {
        Assert.Equal(expected, _parser.ParseJobSelection(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1-")]
    [InlineData("-3")]
    [InlineData("3-1")]
    [InlineData("1;abc")]
    public void ParseJobSelection_InvalidInput_ReturnsEmpty(string input)
    {
        Assert.Empty(_parser.ParseJobSelection(input));
    }

    [Theory]
    [InlineData("6")]                  // single index above max
    [InlineData("100")]
    [InlineData("1-6")]                // range upper bound above max
    [InlineData("1-9999999")]          // pathological input — must not allocate millions of entries
    [InlineData("1;6")]                // valid + invalid in selection
    public void ParseJobSelection_AboveCahierCap_ReturnsEmpty(string input)
    {
        // Cahier v1.0 caps the job count at 5; the parser must reject any index above
        // that as a defensive guard against typo'd input or DoS-style large ranges.
        var result = _parser.ParseJobSelection(input);

        Assert.Empty(result);
    }
}
