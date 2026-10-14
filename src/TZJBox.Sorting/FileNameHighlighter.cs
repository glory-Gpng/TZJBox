namespace TZJBox.Sorting;

public static class FileNameHighlighter
{
    public static IReadOnlyList<NameSpan> Split(string fileName, IReadOnlyList<TextRange> ranges)
    {
        if (string.IsNullOrEmpty(fileName) || ranges.Count == 0)
            return [new NameSpan(fileName ?? "", false)];

        var merged = new List<(int Start, int End)>();
        foreach (var range in ranges.OrderBy(range => range.Start))
        {
            if (range.Length <= 0 || range.Start >= fileName.Length || range.Start < 0)
                continue;

            var start = range.Start;
            var end = Math.Min(fileName.Length, range.Start + range.Length);
            if (merged.Count > 0 && start <= merged[^1].End)
            {
                var previous = merged[^1];
                merged[^1] = (previous.Start, Math.Max(previous.End, end));
                continue;
            }

            merged.Add((start, end));
        }

        if (merged.Count == 0)
            return [new NameSpan(fileName, false)];

        var spans = new List<NameSpan>(merged.Count * 2 + 1);
        var cursor = 0;
        foreach (var (start, end) in merged)
        {
            if (start > cursor)
                spans.Add(new NameSpan(fileName[cursor..start], false));
            spans.Add(new NameSpan(fileName[start..end], true));
            cursor = end;
        }

        if (cursor < fileName.Length)
            spans.Add(new NameSpan(fileName[cursor..], false));

        return spans;
    }
}
