namespace UsersManager.Dtos;

public record SonarEnginePasswordDto(string value, string salt, string algorithm, string iterations, string type, string email, string internalId);