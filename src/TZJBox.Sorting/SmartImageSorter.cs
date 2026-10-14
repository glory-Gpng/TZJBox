using System.Globalization;
using System.Text.RegularExpressions;

namespace TZJBox.Sorting;

public static partial class SmartImageSorter
{
    public static SortResult Sort(IReadOnlyList<FileSortInput> files, SortOptions? options = null)
    {
        options ??= new SortOptions();
        if (files.Count == 0)
            return new SortResult { Files = [] };

        if (options.Strategy == SortStrategy.CustomRegex)
        {
            var patternError = TryCompile(options, out var userRegex);
            if (patternError is not null || userRegex is null)
            {
                return new SortResult
                {
                    Files = files.Select(file => Draft(file, null, [], "—", "等待有效正则")).ToList(),
                    PatternError = patternError ?? "正则无效。"
                };
            }

            return Finish(BuildRegexRows(files, userRegex), [], options.Direction);
        }

        var prepared = files.Select(file => Prepare(file, options)).ToList();
        var useColumns = options.Strategy is SortStrategy.Auto or SortStrategy.LastNumber;
        var constantColumns = useColumns
            ? FindConstantColumns(prepared, options.IgnoreConstantSegments)
            : [];

        List<Row> rows = options.Strategy switch
        {
            SortStrategy.LastNumber => BuildLastNumberRows(prepared, constantColumns),
            SortStrategy.Auto => BuildAutoRows(prepared, constantColumns),
            SortStrategy.Natural => BuildNaturalRows(prepared),
            SortStrategy.PrefixStrip => BuildPrefixRows(prepared, options),
            SortStrategy.Metadata => BuildMetadataRows(prepared),
            _ => BuildNaturalRows(prepared)
        };

        var ignored = constantColumns
            .OrderBy(column => column.Key)
            .Select(column => column.Value)
            .Distinct()
            .ToArray();

        return Finish(rows, useColumns ? ignored : [], options.Direction);
    }

    private static SortResult Finish(List<Row> rows, IReadOnlyList<long> ignored, SortDirection direction)
    {
        rows.Sort(new RankComparer(direction));
        return new SortResult
        {
            Files = rows.Select(row => row.File).ToList(),
            IgnoredConstantValues = ignored
        };
    }

    private static string? TryCompile(SortOptions options, out Regex? regex)
    {
        regex = null;
        var pattern = options.CustomPattern?.Trim();
        if (string.IsNullOrEmpty(pattern))
            return "请输入正则，并用括号标出要排序的数字。";

        try
        {
            var flags = RegexOptions.CultureInvariant;
            if (options.RegexIgnoreCase)
                flags |= RegexOptions.IgnoreCase;
            regex = new Regex(pattern, flags, TimeSpan.FromMilliseconds(250));
            return null;
        }
        catch (ArgumentException ex)
        {
            return "正则无效：" + ex.Message;
        }
    }

    private static Prepared Prepare(FileSortInput file, SortOptions options)
    {
        var tokens = FileNameTokenizer.Tokenize(file.FileName, options.StripResolution, options.StripDatesAndLongIds);
        return new Prepared
        {
            Source = file,
            Stem = FileNameTokenizer.StemOf(file.FileName),
            Tokens = tokens,
            Useful = tokens.Where(token => !token.IsNoise).ToList()
        };
    }

    private static Dictionary<int, long> FindConstantColumns(List<Prepared> prepared, bool enabled)
    {
        var columns = new Dictionary<int, long>();
        if (!enabled || prepared.Count < 2)
            return columns;

        var maxColumns = prepared.Max(item => item.Useful.Count);
        for (var column = 0; column < maxColumns; column++)
        {
            long? shared = null;
            var presentInAll = true;
            foreach (var item in prepared)
            {
                if (column >= item.Useful.Count)
                {
                    presentInAll = false;
                    break;
                }

                var value = item.Useful[column].Value;
                if (shared is null)
                    shared = value;
                else if (shared.Value != value)
                {
                    presentInAll = false;
                    break;
                }
            }

            if (presentInAll && shared is long constant)
                columns[column] = constant;
        }

        return columns;
    }

    private static List<Row> BuildLastNumberRows(List<Prepared> prepared, Dictionary<int, long> constantColumns)
    {
        var rows = new List<Row>(prepared.Count);
        foreach (var item in prepared)
        {
            NameToken? chosen = null;
            for (var column = item.Useful.Count - 1; column >= 0; column--)
            {
                if (constantColumns.ContainsKey(column))
                    continue;
                chosen = item.Useful[column];
                break;
            }

            if (chosen is null)
            {
                rows.Add(NameFallback(item, "没有可用数字，按文件名排在后面"));
                continue;
            }

            var skippedNoise = item.Tokens.Any(token => token.IsNoise);
            var explanation = skippedNoise
                ? $"末尾数字 {chosen.Value} · 已跳过干扰项"
                : $"末尾数字 {chosen.Value}";
            rows.Add(NumberRow(item, chosen, explanation));
        }

        return rows;
    }

    private static List<Row> BuildAutoRows(List<Prepared> prepared, Dictionary<int, long> constantColumns)
    {
        var features = DescribeColumns(prepared, constantColumns);
        var winner = features.MaxBy(feature => feature.Score);
        var rows = new List<Row>(prepared.Count);
        foreach (var item in prepared)
        {
            NameToken? chosen = null;
            var explanation = "";
            if (winner is not null && winner.Index < item.Useful.Count && !constantColumns.ContainsKey(winner.Index))
            {
                chosen = item.Useful[winner.Index];
                explanation = ExplainAuto(winner, chosen.Value, item.Tokens.Any(token => token.IsNoise));
            }
            else
            {
                for (var column = item.Useful.Count - 1; column >= 0; column--)
                {
                    if (constantColumns.ContainsKey(column))
                        continue;
                    chosen = item.Useful[column];
                    break;
                }

                if (chosen is not null)
                    explanation = $"该文件没有对应段，改用末尾数字 {chosen.Value}";
            }

            rows.Add(chosen is null
                ? NameFallback(item, "没有可用数字，按文件名排在后面")
                : NumberRow(item, chosen, explanation));
        }

        return rows;
    }

    private static List<ColumnFeature> DescribeColumns(List<Prepared> prepared, Dictionary<int, long> constantColumns)
    {
        var maxColumns = prepared.Count == 0 ? 0 : prepared.Max(item => item.Useful.Count);
        var drafts = new List<ColumnFeature>();
        for (var column = 0; column < maxColumns; column++)
        {
            if (constantColumns.ContainsKey(column))
                continue;

            var cells = new List<NameToken>();
            foreach (var item in prepared)
            {
                if (column < item.Useful.Count)
                    cells.Add(item.Useful[column]);
            }

            if (cells.Count == 0)
                continue;

            var padded = cells.Count(token => token.IsPadded);
            var semanticHits = cells.Where(token => token.SemanticScore > 0).ToList();
            var distinct = cells.Select(token => token.Value).Distinct().OrderBy(value => value).ToArray();
            var continuity = Continuity(distinct, cells.Count, prepared.Count);
            drafts.Add(new ColumnFeature
            {
                Index = column,
                MajorityPadded = padded * 2 >= cells.Count,
                MajoritySemantic = semanticHits.Count > 0 && semanticHits.Count * 2 >= cells.Count,
                Semantic = semanticHits.Count == 0 ? 0 : semanticHits.Max(token => token.SemanticScore),
                Continuity = continuity,
                Unique = distinct.Length == cells.Count
            });
        }

        var lastIndex = drafts.Count == 0 ? -1 : drafts.Max(feature => feature.Index);
        foreach (var feature in drafts)
        {
            var score = 0d;
            if (feature.MajoritySemantic)
                score += 1000 + feature.Semantic;
            score += feature.Continuity * 220;
            if (feature.Unique)
                score += 40;

            var laterUnpadded = drafts.Any(other => other.Index > feature.Index && !other.MajorityPadded);
            if (feature.MajorityPadded && laterUnpadded)
                score -= 350;
            if (feature.Index == lastIndex)
                score += 35;
            score += feature.Index * 2;
            feature.Score = score;
            feature.Reason = ReasonFor(feature, drafts);
        }

        return drafts;
    }

    private static double Continuity(long[] distinct, int cellCount, int fileCount)
    {
        if (distinct.Length == 0)
            return 0;

        var span = distinct[^1] - distinct[0] + 1;
        if (span <= 0)
            return 1;
        if (distinct.Length == cellCount && cellCount == fileCount && span == fileCount)
            return 1;

        return Math.Min(1d, distinct.Length / (double)span);
    }

    private static string ReasonFor(ColumnFeature feature, List<ColumnFeature> all)
    {
        if (feature.MajoritySemantic)
            return "语义";
        if (!feature.MajorityPadded && all.Any(other => other.Index != feature.Index && other.MajorityPadded))
            return "未补零";
        if (feature.Continuity >= 0.8)
            return "连续";
        return "靠后";
    }

    private static string ExplainAuto(ColumnFeature feature, long value, bool skippedNoise)
    {
        var noise = skippedNoise ? " · 已跳过干扰项" : "";
        return feature.Reason switch
        {
            "语义" => $"语义优先 {value}{noise}",
            "未补零" => $"未补零序号 {value}{noise}",
            "连续" => $"连续序号 {value}{noise}",
            _ => $"靠后数字 {value}{noise}"
        };
    }

    private static List<Row> BuildNaturalRows(List<Prepared> prepared)
    {
        return prepared.Select(item =>
        {
            var highlights = item.Useful.Select(token => new TextRange(token.Start, token.Length)).ToArray();
            return new Row
            {
                Rank = new Rank(0, 0, 0, item.Source.FileName, item.Source.FileName),
                File = Draft(item.Source, null, highlights, "全文", "从左到右自然排序")
            };
        }).ToList();
    }

    private static List<Row> BuildPrefixRows(List<Prepared> prepared, SortOptions options)
    {
        var skip = Math.Clamp(options.PrefixSkipCount, 0, 50);
        var delimiter = options.PrefixDelimiter ?? "";
        return prepared.Select(item =>
        {
            var cut = CutIndex(item.Stem, delimiter, skip);
            var remainder = cut >= item.Stem.Length ? "" : item.Stem[cut..];
            var highlights = item.Useful
                .Where(token => token.Start >= cut)
                .Select(token => new TextRange(token.Start, token.Length))
                .ToArray();
            var label = string.IsNullOrEmpty(remainder) ? "(空)" : Truncate(remainder, 40);
            return new Row
            {
                Rank = new Rank(0, 0, 0, remainder, item.Source.FileName),
                File = Draft(item.Source, null, highlights, label, $"跳过前 {skip} 个分隔符后自然排序")
            };
        }).ToList();
    }

    private static List<Row> BuildMetadataRows(List<Prepared> prepared)
    {
        return prepared.Select(item =>
        {
            var exif = item.Source.ExifTime;
            var time = exif ?? item.Source.LastWriteTime;
            var fromExif = exif is not null;
            var label = (fromExif ? "EXIF " : "修改 ") + time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var explanation = fromExif ? "EXIF 拍摄时间" : "修改时间（未使用创建时间）";
            return new Row
            {
                Rank = new Rank(1, 0, time.Ticks, item.Source.FileName, item.Source.FileName),
                File = Draft(item.Source, null, [], label, explanation)
            };
        }).ToList();
    }

    private static List<Row> BuildRegexRows(IReadOnlyList<FileSortInput> files, Regex regex)
    {
        var rows = new List<Row>(files.Count);
        foreach (var file in files)
        {
            Match match;
            try
            {
                match = regex.Match(file.FileName);
            }
            catch (RegexMatchTimeoutException)
            {
                rows.Add(NameFallback(file, "正则匹配超时，按文件名排在后面"));
                continue;
            }

            if (!match.Success || !TryReadCapture(match, out var number, out var start, out var length))
            {
                rows.Add(NameFallback(file, "正则没有捕获到数字，按文件名排在后面"));
                continue;
            }

            rows.Add(new Row
            {
                Rank = new Rank(0, number, 0, file.FileName, file.FileName),
                File = Draft(file, number, [new TextRange(start, length)], number.ToString(CultureInfo.InvariantCulture), $"正则捕获 {number}")
            });
        }

        return rows;
    }

    private static bool TryReadCapture(Match match, out long number, out int start, out int length)
    {
        number = 0;
        start = 0;
        length = 0;
        Group group = match;
        for (var index = 1; index < match.Groups.Count; index++)
        {
            if (match.Groups[index].Success && match.Groups[index].Length > 0)
            {
                group = match.Groups[index];
                break;
            }
        }

        if (long.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            start = group.Index;
            length = group.Length;
            return true;
        }

        var digits = DigitRun().Match(group.Value);
        if (!digits.Success || !long.TryParse(digits.Value, NumberStyles.None, CultureInfo.InvariantCulture, out number))
            return false;

        start = group.Index + digits.Index;
        length = digits.Length;
        return true;
    }

    private static int CutIndex(string stem, string delimiter, int skip)
    {
        if (skip <= 0 || delimiter.Length == 0)
            return 0;

        var start = 0;
        for (var count = 0; count < skip; count++)
        {
            var index = stem.IndexOf(delimiter, start, StringComparison.Ordinal);
            if (index < 0)
                return start;
            start = index + delimiter.Length;
        }

        return Math.Min(start, stem.Length);
    }

    private static Row NumberRow(Prepared item, NameToken chosen, string explanation)
    {
        return new Row
        {
            Rank = new Rank(0, chosen.Value, 0, item.Source.FileName, item.Source.FileName),
            File = Draft(
                item.Source,
                chosen.Value,
                [new TextRange(chosen.Start, chosen.Length)],
                chosen.Value.ToString(CultureInfo.InvariantCulture),
                explanation)
        };
    }

    private static Row NameFallback(Prepared item, string explanation) => NameFallback(item.Source, explanation);

    private static Row NameFallback(FileSortInput file, string explanation)
    {
        return new Row
        {
            Rank = new Rank(2, 0, 0, file.FileName, file.FileName),
            File = Draft(file, null, [], "文件名", explanation)
        };
    }

    private static SortedFile Draft(
        FileSortInput source,
        long? sequenceNumber,
        IReadOnlyList<TextRange> highlights,
        string keyLabel,
        string explanation)
    {
        return new SortedFile
        {
            Source = source,
            SequenceNumber = sequenceNumber,
            Highlights = highlights,
            KeyLabel = keyLabel,
            Explanation = explanation
        };
    }

    private static string Truncate(string value, int max)
    {
        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }

    [GeneratedRegex(@"[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRun();

    private sealed class Prepared
    {
        public required FileSortInput Source { get; init; }

        public required string Stem { get; init; }

        public required IReadOnlyList<NameToken> Tokens { get; init; }

        public required List<NameToken> Useful { get; init; }
    }

    private sealed class ColumnFeature
    {
        public int Index { get; init; }

        public bool MajorityPadded { get; init; }

        public bool MajoritySemantic { get; init; }

        public int Semantic { get; init; }

        public double Continuity { get; init; }

        public bool Unique { get; init; }

        public double Score { get; set; }

        public string Reason { get; set; } = "";
    }

    private readonly record struct Rank(int Bucket, long Number, long TimeTicks, string Text, string SourceName);

    private sealed class Row
    {
        public required Rank Rank { get; init; }

        public required SortedFile File { get; init; }
    }

    private sealed class RankComparer(SortDirection direction) : IComparer<Row>
    {
        public int Compare(Row? left, Row? right)
        {
            if (left is null || right is null)
                return 0;

            var bucket = left.Rank.Bucket.CompareTo(right.Rank.Bucket);
            if (bucket != 0)
                return bucket;

            var sign = direction == SortDirection.Descending ? -1 : 1;
            if (left.Rank.Bucket == 0)
            {
                var number = left.Rank.Number.CompareTo(right.Rank.Number);
                if (number != 0)
                    return number * sign;
            }
            else if (left.Rank.Bucket == 1)
            {
                var time = left.Rank.TimeTicks.CompareTo(right.Rank.TimeTicks);
                if (time != 0)
                    return time * sign;
            }

            var text = NaturalStringComparer.Compare(left.Rank.Text, right.Rank.Text);
            if (text != 0)
                return text * sign;

            return string.Compare(left.Rank.SourceName, right.Rank.SourceName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
