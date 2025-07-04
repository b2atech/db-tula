namespace B2A.DbTula.Core.Models;

public class ForeignKeyDefinition
{
    public string Name { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string ReferencedTable { get; set; } = string.Empty;
    public string ReferencedColumn { get; set; } = string.Empty;
}