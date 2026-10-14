using TZJBox.Sorting;

namespace TZJBox.Sorting.Tests;

public class RenameAndHighlightTests
{
    [Fact]
    public void Highlighter_MarksOnlyTheChosenDigits()
    {
        var spans = FileNameHighlighter.Split("001_10.jpg", [new TextRange(4, 2)]);

        Assert.Equal(["001_", "10", ".jpg"], spans.Select(span => span.Text));
        Assert.Equal([false, true, false], spans.Select(span => span.Highlight));
    }

    [Fact]
    public void RenamePlan_PreservesExtractedNumbers_WithAtLeastThreeDigits()
    {
        var sorted = SmartImageSorter.Sort(
            [
                Input("001_10.jpg"),
                Input("002_1.jpg"),
                Input("003_2.jpg")
            ],
            new SortOptions { Strategy = SortStrategy.LastNumber });

        var plan = RenamePlanner.Plan(sorted.Files, padWidth: 3);

        Assert.Equal(["002_1.jpg", "003_2.jpg", "001_10.jpg"], plan.Select(item => item.SourceFileName));
        Assert.Equal(["001.jpg", "002.jpg", "010.jpg"], plan.Select(item => item.ProposedFileName));
    }

    [Fact]
    public void RenamePlan_UsesRank_WhenTheStrategyHasNoSingleNumber()
    {
        var sorted = SmartImageSorter.Sort(
            [Input("b.jpg"), Input("a.jpg")],
            new SortOptions { Strategy = SortStrategy.Natural });
        var plan = RenamePlanner.Plan(sorted.Files, padWidth: 3);

        Assert.Equal(["a.jpg", "b.jpg"], plan.Select(item => item.SourceFileName));
        Assert.Equal(["001.jpg", "002.jpg"], plan.Select(item => item.ProposedFileName));
    }

    [Fact]
    public void RenamePlan_AvoidsNamesHeldByFilesOutsideTheBatch()
    {
        var sorted = SmartImageSorter.Sort(
            [Input("/pics/001_10.jpg", "001_10.jpg")],
            new SortOptions { Strategy = SortStrategy.LastNumber });

        var plan = RenamePlanner.Plan(
            sorted.Files,
            3,
            path => string.Equals(path, Path.Combine("/pics", "010.jpg"), StringComparison.Ordinal));

        Assert.Equal("010_2.jpg", plan[0].ProposedFileName);
    }

    [Fact]
    public void RenameExecutor_SwapsOverlappingNamesWithoutLosingBytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tzjbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "001_10.jpg");
            var second = Path.Combine(directory, "010.jpg");
            File.WriteAllText(first, "from-prefixed");
            File.WriteAllText(second, "already-010");

            var sorted = SmartImageSorter.Sort(
                [
                    new FileSortInput(first, "001_10.jpg", DateTime.UnixEpoch, null),
                    new FileSortInput(second, "010.jpg", DateTime.UnixEpoch, null)
                ],
                new SortOptions { Strategy = SortStrategy.LastNumber });
            var plan = RenamePlanner.Plan(sorted.Files, 3, OccupiedOutside(sorted.Files));
            var execution = RenameExecutor.Execute(plan);

            Assert.Null(execution.Error);
            Assert.Equal(2, execution.Renamed);
            Assert.Equal("from-prefixed", File.ReadAllText(Path.Combine(directory, "010.jpg")));
            Assert.Equal("already-010", File.ReadAllText(Path.Combine(directory, "010_2.jpg")));
            Assert.False(File.Exists(first));
            Assert.Empty(Directory.GetFiles(directory, "*.tzjtmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NaturalComparer_OrdersEmbeddedNumbersByValue()
    {
        Assert.True(NaturalStringComparer.Compare("img2", "img10") < 0);
        Assert.True(NaturalStringComparer.Compare("img10", "img100") < 0);
        Assert.Equal(0, NaturalStringComparer.Compare("IMG2", "img2"));
    }

    private static FileSortInput Input(string path, string? fileName = null)
        => new(path, fileName ?? Path.GetFileName(path), DateTime.UnixEpoch, null);

    private static Func<string, bool> OccupiedOutside(IReadOnlyList<SortedFile> files)
    {
        var sources = new HashSet<string>(
            files.Select(file => file.Source.FullPath),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        return path => !sources.Contains(path) && File.Exists(path);
    }
}
