namespace DBForge.Domain;
public sealed class ProjectDefinition
{
 public string Name { get; set; } = "Новый проект";
 public string Description { get; set; } = "";
 public string DatabaseProvider { get; set; } = "MySQL";
 public string Server { get; set; } = "localhost";
 public int Port { get; set; } = 3306;
 public string? DatabaseName { get; set; }
 public List<EntityDefinition> Entities { get; set; } = new();
}
public sealed class EntityDefinition
{
 public string Name { get; set; } = "";
 public string DisplayName { get; set; } = "";
 public List<FieldDefinition> Fields { get; set; } = new();
}
public sealed class FieldDefinition
{
 public string Name { get; set; } = "";
 public string DataType { get; set; } = "VARCHAR(255)";
 public bool IsRequired { get; set; }
 public bool IsPrimaryKey { get; set; }
 public bool IsAutoIncrement { get; set; }
 public bool IsUnique { get; set; }
 public string? ReferenceEntity { get; set; }
 public string? ReferenceField { get; set; }
}
