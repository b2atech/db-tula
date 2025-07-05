namespace B2A.DbTula.Core.Models;

public class TableDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<ColumnDefinition> Columns { get; set; } = new();
    public List<PrimaryKeyDefinition> PrimaryKeys { get; set; } = new(); // just column names
    public List<ForeignKeyDefinition> ForeignKeys { get; set; } = new();
    public List<IndexDefinition> Indexes { get; set; } = new();
    public string CreateScript { get; set; } = string.Empty; // full CREATE TABLE script

}