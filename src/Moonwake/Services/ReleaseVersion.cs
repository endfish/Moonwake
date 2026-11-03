namespace Moonwake.Services;

internal sealed record ReleaseVersion(int Major, int Minor, int Patch, string[] PreRelease) : IComparable<ReleaseVersion>
{
    public static ReleaseVersion Parse(string value)
    {
        value = value.Trim().TrimStart('v').Split('+')[0];
        var parts = value.Split('-', 2);
        var numbers = parts[0].Split('.');
        if (numbers.Length != 3 || numbers.Any(n => !int.TryParse(n, out var v) || v < 0))
            throw new FormatException("版本号无效。");
        var pre = parts.Length == 2 ? parts[1].Split('.') : [];
        if (pre.Any(p => p.Length == 0 || p.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new FormatException("预览版本号无效。");
        return new(int.Parse(numbers[0]), int.Parse(numbers[1]), int.Parse(numbers[2]), pre);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        foreach (var difference in new[] { this.Major.CompareTo(other.Major), this.Minor.CompareTo(other.Minor), this.Patch.CompareTo(other.Patch) })
            if (difference != 0) return difference;
        if (this.PreRelease.Length == 0 || other.PreRelease.Length == 0)
            return (this.PreRelease.Length == 0).CompareTo(other.PreRelease.Length == 0);
        for (var i = 0; i < Math.Min(this.PreRelease.Length, other.PreRelease.Length); i++)
        {
            var a = this.PreRelease[i];
            var b = other.PreRelease[i];
            var an = ulong.TryParse(a, out var av);
            var bn = ulong.TryParse(b, out var bv);
            var result = an && bn ? av.CompareTo(bv) : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(a, b);
            if (result != 0) return result;
        }
        return this.PreRelease.Length.CompareTo(other.PreRelease.Length);
    }
}
