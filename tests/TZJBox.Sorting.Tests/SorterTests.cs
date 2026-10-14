using TZJBox.Sorting;

namespace TZJBox.Sorting.Tests;

public class SorterTests
{
    [Fact]
    public void LastNumber_SortsByRealSuffix_NotPaddedPrefix()
    {
        var result = Sort(SortStrategy.LastNumber, "001_10.jpg", "002_1.jpg", "003_2.jpg", "010_9.jpg");

        Assert.Equal(["002_1.jpg", "003_2.jpg", "010_9.jpg", "001_10.jpg"], Names(result));
        Assert.Equal(["1", "2", "9", "10"], Hits(result));
        Assert.Equal([1, 2, 9, 10], result.Files.Select(file => file.SequenceNumber));
    }

    [Fact]
    public void Auto_PrefersUnpaddedSuffix_WhenPrefixIsZeroPadded()
    {
        var names = Enumerable.Range(1, 10)
            .Select(index => $"{index:000}_{(index == 1 ? 10 : index - 1)}.jpg")
            .ToArray();

        var result = Sort(SortStrategy.Auto, names);

        Assert.Equal(
            ["002_1.jpg", "003_2.jpg", "004_3.jpg", "005_4.jpg", "006_5.jpg", "007_6.jpg", "008_7.jpg", "009_8.jpg", "010_9.jpg", "001_10.jpg"],
            Names(result));
        Assert.All(result.Files, file => Assert.Contains("未补零序号", file.Explanation));
    }

    [Fact]
    public void Auto_GivesSemanticNumbersPriority()
    {
        var result = Sort(SortStrategy.Auto, "001_page10.jpg", "002_page2.jpg", "003_page1.jpg");

        Assert.Equal(["003_page1.jpg", "002_page2.jpg", "001_page10.jpg"], Names(result));
        Assert.Equal(["1", "2", "10"], Hits(result));
        Assert.All(result.Files, file => Assert.Contains("语义优先", file.Explanation));
    }

    [Fact]
    public void Auto_UnderstandsChineseEpisodeMarkers()
    {
        var result = Sort(SortStrategy.Auto, "001_第10话.jpg", "002_第2话.jpg", "003_第1页.jpg");

        Assert.Equal(["003_第1页.jpg", "002_第2话.jpg", "001_第10话.jpg"], Names(result));
        Assert.Equal(["1", "2", "10"], Hits(result));
    }

    [Fact]
    public void Auto_DropsConstantChapter_ThenUsesTheVaryingPage()
    {
        var result = Sort(
            SortStrategy.Auto,
            "001_ch1_5.jpg",
            "002_ch1_3.jpg",
            "003_ch1_12.jpg");

        Assert.Equal(["002_ch1_3.jpg", "001_ch1_5.jpg", "003_ch1_12.jpg"], Names(result));
        Assert.Equal([1L], result.IgnoredConstantValues);
        Assert.Equal(["3", "5", "12"], Hits(result));
    }

    [Fact]
    public void Auto_PrefersVaryingSemanticColumn_OverLastNumber()
    {
        var auto = Sort(SortStrategy.Auto, "001_ch2_05.jpg", "002_ch1_09.jpg");
        var last = Sort(SortStrategy.LastNumber, "001_ch2_05.jpg", "002_ch1_09.jpg");

        Assert.Equal(["002_ch1_09.jpg", "001_ch2_05.jpg"], Names(auto));
        Assert.Equal(["1", "2"], Hits(auto));
        Assert.Equal(["001_ch2_05.jpg", "002_ch1_09.jpg"], Names(last));
        Assert.Equal(["05", "09"], Hits(last));
    }

    [Fact]
    public void LastNumber_StripsResolutionDatesAndLongIds()
    {
        var result = Sort(
            SortStrategy.LastNumber,
            "001_10_1920x1080.jpg",
            "002_2_1920X1080.jpg",
            "003_4_1920×1080.png",
            "004_7_800*600.webp",
            "005_3_20261013.jpg",
            "006_8_2026-10-13.jpg",
            "007_6_12345678901.jpg");

        Assert.Equal(
            ["002_2_1920X1080.jpg", "005_3_20261013.jpg", "003_4_1920×1080.png", "007_6_12345678901.jpg", "004_7_800*600.webp", "006_8_2026-10-13.jpg", "001_10_1920x1080.jpg"],
            Names(result));
        Assert.Equal(["2", "3", "4", "6", "7", "8", "10"], Hits(result));
        Assert.All(result.Files, file => Assert.Contains("已跳过干扰项", file.Explanation));
    }

    [Fact]
    public void LastNumber_KeepsEightDigitValuesThatAreNotDates()
    {
        var result = Sort(SortStrategy.LastNumber, "005_3_12345678.jpg", "001_9_87654321.jpg");

        Assert.Equal(["005_3_12345678.jpg", "001_9_87654321.jpg"], Names(result));
        Assert.Equal(["12345678", "87654321"], Hits(result));
    }

    [Fact]
    public void NoiseFilters_CanBeTurnedOff()
    {
        var result = SmartImageSorter.Sort(
            [File("10_1920x1080.jpg"), File("2_800x600.jpg")],
            new SortOptions
            {
                Strategy = SortStrategy.LastNumber,
                StripResolution = false,
                IgnoreConstantSegments = false
            });

        Assert.Equal(["600", "1080"], Hits(result));
    }

    [Fact]
    public void ConstantSegment_IsIgnoredOnlyWhenEveryFileSharesIt()
    {
        var filtered = Sort(SortStrategy.LastNumber, "01_1024.jpg", "02_1024.jpg", "03_1024.jpg");
        Assert.Equal(["01_1024.jpg", "02_1024.jpg", "03_1024.jpg"], Names(filtered));
        Assert.Equal(["01", "02", "03"], Hits(filtered));
        Assert.Equal([1024L], filtered.IgnoredConstantValues);

        var raw = SmartImageSorter.Sort(
            [File("01_1024.jpg"), File("02_1024.jpg")],
            new SortOptions { Strategy = SortStrategy.LastNumber, IgnoreConstantSegments = false });
        Assert.Equal(["1024", "1024"], Hits(raw));

        var single = Sort(SortStrategy.LastNumber, "001_10.jpg");
        Assert.Empty(single.IgnoredConstantValues);
        Assert.Equal("10", Hit(single.Files[0]));
    }

    [Fact]
    public void ConstantValue_DoesNotUseNumbersFromTheDirectory()
    {
        var result = SmartImageSorter.Sort(
            [
                new FileSortInput(Path.Combine("C:", "999", "001_2.jpg"), "001_2.jpg", DefaultTime, null),
                new FileSortInput(Path.Combine("D:", "1", "002_10.jpg"), "002_10.jpg", DefaultTime, null)
            ],
            new SortOptions { Strategy = SortStrategy.LastNumber });

        Assert.Equal(["001_2.jpg", "002_10.jpg"], Names(result));
    }

    [Fact]
    public void NaturalSort_IsLeftToRight_NotByTheLastNumber()
    {
        var result = Sort(SortStrategy.Natural, "b2.jpg", "a10.jpg", "a2.jpg");

        Assert.Equal(["a2.jpg", "a10.jpg", "b2.jpg"], Names(result));
        Assert.All(result.Files, file => Assert.Null(file.SequenceNumber));
    }

    [Fact]
    public void PrefixStrip_SkipsLeadingDelimiter_ThenSortsNaturally()
    {
        var result = SmartImageSorter.Sort(
            [File("001_02_正文10.jpg"), File("001_01_正文2.jpg"), File("001_02_正文2.jpg")],
            new SortOptions { Strategy = SortStrategy.PrefixStrip, PrefixDelimiter = "_", PrefixSkipCount = 1 });

        Assert.Equal(["001_01_正文2.jpg", "001_02_正文2.jpg", "001_02_正文10.jpg"], Names(result));
        Assert.Equal(["01,2", "02,2", "02,10"], Hits(result));
        Assert.All(result.Files, file => Assert.Null(file.SequenceNumber));
    }

    [Fact]
    public void CustomRegex_UsesTheFirstCaptureGroup()
    {
        var result = SmartImageSorter.Sort(
            [File("img_001_real_10_v2.jpg"), File("img_009_real_2_v2.jpg")],
            new SortOptions { Strategy = SortStrategy.CustomRegex, CustomPattern = @"real_(\d+)" });

        Assert.Equal(["img_009_real_2_v2.jpg", "img_001_real_10_v2.jpg"], Names(result));
        Assert.Equal(["2", "10"], Hits(result));
        Assert.Equal([2L, 10L], result.Files.Select(file => file.SequenceNumber));
    }

    [Fact]
    public void CustomRegex_InvalidPattern_DoesNotThrow()
    {
        var result = SmartImageSorter.Sort(
            [File("001_10.jpg")],
            new SortOptions { Strategy = SortStrategy.CustomRegex, CustomPattern = "(" });

        Assert.NotNull(result.PatternError);
        Assert.Contains("正则无效", result.PatternError);
    }

    [Fact]
    public void CustomRegex_CatastrophicPattern_TimesOutInsteadOfHanging()
    {
        var name = new string('a', 28) + "X.jpg";
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var result = SmartImageSorter.Sort(
            [File(name)],
            new SortOptions { Strategy = SortStrategy.CustomRegex, CustomPattern = @"(a+)+$" });
        elapsed.Stop();

        Assert.Null(result.PatternError);
        Assert.Null(result.Files[0].SequenceNumber);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Metadata_UsesExifBeforeLastWriteTime_AndNeverNeedsCreationTime()
    {
        var earlyWrite = new DateTime(2019, 5, 1, 8, 0, 0);
        var midWrite = new DateTime(2024, 1, 1, 8, 0, 0);
        var result = SmartImageSorter.Sort(
            [
                File("hash-b.jpg", exif: new DateTime(2021, 2, 2, 1, 0, 0), write: earlyWrite),
                File("hash-a.jpg", exif: new DateTime(2020, 1, 1, 1, 0, 0), write: midWrite),
                File("hash-c.jpg", write: new DateTime(2019, 1, 1, 1, 0, 0))
            ],
            new SortOptions { Strategy = SortStrategy.Metadata });

        Assert.Equal(["hash-c.jpg", "hash-a.jpg", "hash-b.jpg"], Names(result));
        Assert.Contains("修改", result.Files[0].KeyLabel);
        Assert.Contains("EXIF", result.Files[1].KeyLabel);
        Assert.Contains("未使用创建时间", result.Files[0].Explanation);
        Assert.All(result.Files, file => Assert.Null(file.SequenceNumber));
    }

    [Fact]
    public void Descending_ReversesNumbers_ButStillPutsFilesWithoutNumbersLast()
    {
        var result = SmartImageSorter.Sort(
            [File("001_10.jpg"), File("notes.jpg"), File("002_1.jpg")],
            new SortOptions { Strategy = SortStrategy.LastNumber, Direction = SortDirection.Descending });

        Assert.Equal(["001_10.jpg", "002_1.jpg", "notes.jpg"], Names(result));
    }

    [Fact]
    public void FilesWithoutNumbers_SortByName_AfterNumberedFiles()
    {
        var result = Sort(SortStrategy.LastNumber, "readme.txt", "002_1.jpg", "album.txt");

        Assert.Equal(["002_1.jpg", "album.txt", "readme.txt"], Names(result));
        Assert.Equal("文件名", result.Files[1].KeyLabel);
    }

    private static readonly DateTime DefaultTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static FileSortInput File(string name, DateTime? exif = null, DateTime? write = null)
        => new(name, name, write ?? DefaultTime, exif);

    private static SortResult Sort(SortStrategy strategy, params string[] names)
        => SmartImageSorter.Sort(names.Select(name => File(name)).ToArray(), new SortOptions { Strategy = strategy });

    private static IReadOnlyList<string> Names(SortResult result) => result.Files.Select(file => file.FileName).ToArray();

    private static IReadOnlyList<string> Hits(SortResult result) => result.Files.Select(Hit).ToArray();

    private static string Hit(SortedFile file)
        => string.Join(",", file.Highlights.Select(range => file.FileName.Substring(range.Start, range.Length)));
}
