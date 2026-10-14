namespace TZJBox.Sorting;

public static class NaturalStringComparer
{
    public static int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left is null)
            return -1;
        if (right is null)
            return 1;

        var i = 0;
        var j = 0;
        while (i < left.Length && j < right.Length)
        {
            var leftDigit = IsAsciiDigit(left[i]);
            var rightDigit = IsAsciiDigit(right[j]);
            if (leftDigit && rightDigit)
            {
                var leftZeros = i;
                while (i < left.Length && left[i] == '0')
                    i++;
                var rightZeros = j;
                while (j < right.Length && right[j] == '0')
                    j++;

                var leftStart = i;
                while (i < left.Length && IsAsciiDigit(left[i]))
                    i++;
                var rightStart = j;
                while (j < right.Length && IsAsciiDigit(right[j]))
                    j++;

                var leftLength = i - leftStart;
                var rightLength = j - rightStart;
                if (leftLength != rightLength)
                    return leftLength.CompareTo(rightLength);

                var valueCompare = string.CompareOrdinal(left, leftStart, right, rightStart, leftLength);
                if (valueCompare != 0)
                    return valueCompare;

                var zeroCompare = (leftStart - leftZeros).CompareTo(rightStart - rightZeros);
                if (zeroCompare != 0)
                    return zeroCompare;
                continue;
            }

            var compare = char.ToUpperInvariant(left[i]).CompareTo(char.ToUpperInvariant(right[j]));
            if (compare != 0)
                return compare;
            i++;
            j++;
        }

        return (left.Length - i).CompareTo(right.Length - j);
    }

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';
}
