namespace Finance.Application.Categories.Rename;

public sealed record RenameCategoryCommand(Guid CategoryId, string? Name);
