using B2A.DbTula.Core.Enums;

namespace B2A.DbTula.Core.Extensions;

public static class SchemaObjectTypeExtensions
{
    public static string ToDisplayString(this SchemaObjectType type)
    {
        return type switch
        {
            SchemaObjectType.PrimaryKey => "Primary Key",
            SchemaObjectType.ForeignKey => "Foreign Key",
            SchemaObjectType.Function => "Function",
            SchemaObjectType.Procedure => "Procedure",
            SchemaObjectType.Index => "Index",
            SchemaObjectType.Column => "Column",
            SchemaObjectType.Table => "Table",
            SchemaObjectType.Sequence => "Sequence",
            _ => type.ToString()
        };
    }
}
