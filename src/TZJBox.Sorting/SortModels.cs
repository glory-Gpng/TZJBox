namespace TZJBox.Sorting;

public enum SortStrategy
{
    /// <summary>用文件夹统计挑出真序号：语义词优先，其次避开补零前缀。</summary>
    Auto,

    /// <summary>去掉分辨率和日期后，取最后一个数字块。</summary>
    LastNumber,

    /// <summary>从左到右的自然排序。</summary>
    Natural,

    /// <summary>跳过前若干个分隔符，再对剩余部分自然排序。</summary>
    PrefixStrip,

    /// <summary>用正则捕获组里的数字排序。</summary>
    CustomRegex,

    /// <summary>EXIF 拍摄时间，缺失时用修改时间。不使用创建时间。</summary>
    Metadata
}

public enum SortDirection
{
    Ascending,
    Descending
}

public sealed record SortOptions
{
    public SortStrategy Strategy { get; init; } = SortStrategy.Auto;

    public SortDirection Direction { get; init; } = SortDirection.Ascending;

    public bool StripResolution { get; init; } = true;

    public bool StripDatesAndLongIds { get; init; } = true;

    public bool IgnoreConstantSegments { get; init; } = true;

    public string PrefixDelimiter { get; init; } = "_";

    public int PrefixSkipCount { get; init; } = 1;

    public string? CustomPattern { get; init; }

    public bool RegexIgnoreCase { get; init; } = true;

    public int RenamePadWidth { get; init; } = 3;
}

/// <summary>
/// 一个待排序文件。只使用文件名本身，目录里的数字不会混进排序键。
/// 时间只接受 EXIF 与修改时间；创建时间按下载完成顺序生成，不参与排序。
/// </summary>
public sealed record FileSortInput(
    string FullPath,
    string FileName,
    DateTime LastWriteTime,
    DateTime? ExifTime);

public readonly record struct TextRange(int Start, int Length);

public sealed class NameSpan
{
    public NameSpan(string text, bool highlight)
    {
        Text = text;
        Highlight = highlight;
    }

    public string Text { get; }

    public bool Highlight { get; }
}

public sealed class SortedFile
{
    public required FileSortInput Source { get; init; }

    public string FileName => Source.FileName;

    /// <summary>要保留到新文件名里的序号。自然排序、前缀剥离和元数据模式为空，改名时按顺序编号。</summary>
    public long? SequenceNumber { get; init; }

    public IReadOnlyList<TextRange> Highlights { get; init; } = [];

    public string KeyLabel { get; init; } = "";

    public string Explanation { get; init; } = "";
}

public sealed class SortResult
{
    public required IReadOnlyList<SortedFile> Files { get; init; }

    public string? PatternError { get; init; }

    public IReadOnlyList<long> IgnoredConstantValues { get; init; } = [];
}

public sealed record RenamePlanItem(
    string SourcePath,
    string SourceFileName,
    string ProposedFileName,
    string FinalPath,
    bool Unchanged);

public sealed class RenameExecutionResult
{
    public int Renamed { get; init; }

    public string? Error { get; init; }

    public bool Success => Error is null;
}
