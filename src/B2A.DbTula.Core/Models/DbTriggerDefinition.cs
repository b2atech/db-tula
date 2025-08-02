namespace B2A.DbTula.Core.Models;

public class DbTriggerDefinition
{
 
    public string Name { get; set; } = string.Empty;
  
    public string Table { get; set; } = string.Empty;

    public string Timing { get; set; } = string.Empty;

    public string Event { get; set; } = string.Empty;

    public string? Definition { get; set; }
}
