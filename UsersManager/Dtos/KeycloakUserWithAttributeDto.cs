namespace UsersManager.Dtos;

internal record KeycloakUserWithAttributeDto(string Id, AttributesDto AttributesDto);
internal record AttributesDto(List<string> Locale, List<string> ExternalId);