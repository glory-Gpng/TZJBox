using System.Globalization;
using System.Text.RegularExpressions;

namespace TZJBox.Sorting;

internal sealed class NameToken
{
    public required int Start { get; init; }

    public required int Length { get; init; }

    public required string Raw { get; init; }

    public required long Value { get; init; }

    public required bool IsNoise { get; init; }

    public required int SemanticScore { get; init; }

    public bool IsPadded => Raw.Length > 1 && Raw[0] == '0';
}

internal static partial class FileNameTokenizer
{
    public static string StemOf(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(extension) ? fileName : fileName[..^extension.Length];
    }

    public static IReadOnlyList<NameToken> Tokenize(string fileName, bool stripResolution, bool stripDatesAndLongIds)
    {
        var stem = StemOf(fileName);
        var noise = new List<(int Start, int End)>();
        if (stripResolution)
        {
            foreach (Match match in ResolutionRegex().Matches(stem))
                noise.Add((match.Index, match.Index + match.Length));
        }

        if (stripDatesAndLongIds)
        {
            foreach (Match match in SeparatedDateRegex().Matches(stem))
                noise.Add((match.Index, match.Index + match.Length));
        }

        var matches = DigitRegex().Matches(stem);
        var tokens = new List<NameToken>(matches.Count);
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            if (!long.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                continue;

            var previousEnd = index == 0 ? 0 : matches[index - 1].Index + matches[index - 1].Length;
            var nextStart = index + 1 < matches.Count ? matches[index + 1].Index : stem.Length;
            var preceding = stem[previousEnd..match.Index];
            var following = stem[(match.Index + match.Length)..nextStart];
            var isNoise = Overlaps(noise, match.Index, match.Index + match.Length)
                || (stripDatesAndLongIds && IsDateOrLongId(match.Value));

            tokens.Add(new NameToken
            {
                Start = match.Index,
                Length = match.Length,
                Raw = match.Value,
                Value = value,
                IsNoise = isNoise,
                SemanticScore = isNoise ? 0 : SemanticScore(preceding, following)
            });
        }

        return tokens;
    }

    private static bool IsDateOrLongId(string raw)
    {
        if (raw.Length >= 10)
            return true;

        return raw.Length == 8 && raw.StartsWith("20", StringComparison.Ordinal);
    }

    private static bool Overlaps(List<(int Start, int End)> spans, int start, int end)
    {
        foreach (var span in spans)
        {
            if (start < span.End && end > span.Start)
                return true;
        }

        return false;
    }

    private static int SemanticScore(string preceding, string following)
    {
        var score = 0;
        if (preceding.EndsWith('第'))
            score = 100;

        var followingTrimmed = following.TrimStart(' ', '_', '-', '.', ')', ']', '】');
        if (followingTrimmed.StartsWith('话') || followingTrimmed.StartsWith('页') || followingTrimmed.StartsWith('章'))
            score = Math.Max(score, 100);

        var cue = LatinCueRegex().Match(preceding);
        if (!cue.Success)
            return score;

        score = Math.Max(score, cue.Groups[1].Value.ToLowerInvariant() switch
        {
            "page" => 90,
            "episode" => 85,
            "chapter" => 80,
            "ep" => 80,
            "chap" => 75,
            "ch" => 70,
            "p" => 60,
            _ => 0
        });
        return score;
    }

    [GeneratedRegex(@"(?<![0-9])[0-9]{3,4}[xX×*][0-9]{3,4}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(@"(?<![0-9])20[0-9]{2}[-./][0-9]{1,2}[-./][0-9]{1,2}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatedDateRegex();

    [GeneratedRegex(@"[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRegex();

    [GeneratedRegex(@"(?:^|[^A-Za-z])(page|episode|chapter|chap|ep|ch|p)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LatinCueRegex();
}
