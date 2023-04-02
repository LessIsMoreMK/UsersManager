using Newtonsoft.Json;

namespace UsersManager.Dtos;


internal record SETokenModelDto(string Refresh, string Access);
internal record SELoginCredentialsDto(string email, string password, string app);
internal record SEPasswordDto(string password);
internal record SEUserGroupsAndRolesDto(
    [JsonProperty(PropertyName = "is_active")] bool IsActive,
    [JsonProperty(PropertyName = "user")] int UserExternalId,
    [JsonProperty(PropertyName = "customer")] int CustomerExternalId,
    [JsonProperty(PropertyName = "sw_roles")] string[] Roles);
    
    