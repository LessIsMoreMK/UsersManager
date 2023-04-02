namespace UsersManager.Entities;

internal record UserWithAttributes(string Id, Attributes Attributes);
internal record Attributes(List<string> Locale, List<string> ExternalId);