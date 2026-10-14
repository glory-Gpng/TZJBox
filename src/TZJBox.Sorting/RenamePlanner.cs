using System.Globalization;

namespace TZJBox.Sorting;

public static class RenamePlanner
{
    public static IReadOnlyList<RenamePlanItem> Plan(
        IReadOnlyList<SortedFile> sorted,
        int padWidth,
        Func<string, bool>? occupiedBySomeoneElse = null)
    {
        padWidth = Math.Clamp(padWidth, 1, 12);
        var comparer = PathComparer;
        var used = new HashSet<string>(comparer);
        var plan = new List<RenamePlanItem>(sorted.Count);

        for (var index = 0; index < sorted.Count; index++)
        {
            var file = sorted[index];
            var directory = Path.GetDirectoryName(file.Source.FullPath) ?? "";
            var extension = Path.GetExtension(file.FileName);
            var stem = file.SequenceNumber is long number && number >= 0
                ? Pad(number, padWidth)
                : Pad(index + 1, padWidth);

            var candidate = stem + extension;
            var suffix = 2;
            while (true)
            {
                var finalPath = Path.Combine(directory, candidate);
                var takenInPlan = !used.Add(finalPath);
                var takenOutside = !takenInPlan && (occupiedBySomeoneElse?.Invoke(finalPath) ?? false);
                if (!takenInPlan && !takenOutside)
                {
                    plan.Add(new RenamePlanItem(
                        file.Source.FullPath,
                        file.FileName,
                        candidate,
                        finalPath,
                        comparer.Equals(finalPath, file.Source.FullPath)));
                    break;
                }

                if (takenOutside)
                    used.Add(finalPath);
                candidate = $"{stem}_{suffix}{extension}";
                suffix++;
            }
        }

        return plan;
    }

    private static string Pad(long number, int padWidth)
    {
        var digits = number.ToString(CultureInfo.InvariantCulture);
        return digits.Length >= padWidth ? digits : digits.PadLeft(padWidth, '0');
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
