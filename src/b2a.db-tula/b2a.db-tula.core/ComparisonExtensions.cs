namespace b2a.db_tula.core
{
    public static class ComparisonExtensions
    {
        public static bool NeedsSync(this string comparison)
        {
            return comparison == "Missing in Target" || comparison == "Not Matching";
        }
    }
}
