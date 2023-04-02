using Newtonsoft.Json;

namespace UsersManager.Entities;


public record SEUsersList(int Count, List<SEUser> Results);

public record SEUser(
    int? Id,
    [JsonProperty(PropertyName = "username")] string UserName, 
    [JsonProperty(PropertyName = "first_name")] string FirstName,
    [JsonProperty(PropertyName = "last_name")] string LastName,
    [JsonProperty(PropertyName = "email")] string Email,
    [JsonProperty(PropertyName = "is_active")] bool? IsActive,
    Customer[]? Customers
);
public record Customer(
    int Id,
    string Name,
    [JsonProperty(PropertyName = "sw_access_id")] int AccessId,
    List<string> Roles
);

public record SEUserToAdd(
    int? Id,
    [JsonProperty(PropertyName = "username")] string UserName, 
    [JsonProperty(PropertyName = "first_name")] string FirstName,
    [JsonProperty(PropertyName = "last_name")] string LastName,
    [JsonProperty(PropertyName = "email")] string Email,
    [JsonProperty(PropertyName = "is_active")] bool? IsActive,
    Customer[]? Customers, 
    [JsonProperty(PropertyName = "password")] string Password
);