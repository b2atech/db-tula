using B2A.DbTula.Core.Enums;

namespace B2A.DbTula.Core.Extensions;

public static class ComparisonStatusExtensions
{
    public static string ToDisplayString(this ComparisonStatus status)
    {
        return status switch
        {
            ComparisonStatus.Match => "Match",
            ComparisonStatus.MissingInSource => "Missing in Source",
            ComparisonStatus.MissingInTarget => "Missing in Target",
            ComparisonStatus.Mismatch => "Mismatch",
            _ => status.ToString()
        };
    }

    public static ConsoleColor ToColor(this ComparisonStatus status)
    {
        return status switch
        {
            ComparisonStatus.Match => ConsoleColor.Green,
            ComparisonStatus.MissingInSource => ConsoleColor.Red,
            ComparisonStatus.MissingInTarget => ConsoleColor.Yellow,
            ComparisonStatus.Mismatch => ConsoleColor.DarkRed,
            _ => ConsoleColor.Gray
        };
    }
}
