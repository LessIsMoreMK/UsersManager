namespace UsersManager.Dtos;

internal record KeycloakGroupDto(string Id, string Name, string Path, List<string> SubGroups);