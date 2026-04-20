namespace EasySave.Tests;

public class StateEntryTests
{
    [Fact]
    public void Progress_NoEligibleFiles_IsZero()
    {
        var entry = new StateEntry { TotalFilesEligible = 0, FilesRemaining = 0 };

        Assert.Equal(0.0, entry.Progress);
    }

    [Fact]
    public void Progress_AllRemaining_IsZero()
    {
        var entry = new StateEntry { TotalFilesEligible = 10, FilesRemaining = 10 };

        Assert.Equal(0.0, entry.Progress);
    }

    [Fact]
    public void Progress_NoneRemaining_IsHundred()
    {
        var entry = new StateEntry { TotalFilesEligible = 10, FilesRemaining = 0 };

        Assert.Equal(100.0, entry.Progress);
    }

    [Fact]
    public void Progress_HalfRemaining_IsFifty()
    {
        var entry = new StateEntry { TotalFilesEligible = 10, FilesRemaining = 5 };

        Assert.Equal(50.0, entry.Progress);
    }

    [Fact]
    public void Progress_IsRoundedToOneDecimal()
    {
        var entry = new StateEntry { TotalFilesEligible = 3, FilesRemaining = 2 };

        Assert.Equal(33.3, entry.Progress);
    }

    [Fact]
    public void ToString_ContainsNameStateAndProgress()
    {
        var entry = new StateEntry
        {
            Name = "daily-docs",
            State = JobState.Active,
            TotalFilesEligible = 10,
            FilesRemaining = 4,
            SizeRemaining = 2048
        };

        var text = entry.ToString();

        Assert.Contains("daily-docs", text);
        Assert.Contains("Active", text);
        // Match the decimal tolerantly: local culture may use '.' or ',' as separator.
        Assert.Matches(@"60[.,]0", text);
        Assert.Contains("%", text);
        Assert.Contains("4 files", text);
        Assert.Contains("2048 bytes", text);
    }
}
