using UsersManager.Dtos;
using UsersManager.Entities;

namespace UsersManager.Interfaces;

/// <summary>
/// Interface responsible for management of users in external API
/// NOTE: Customer / Group / Tenant are used alternately and are essentially the same.
/// </summary>
public interface ISonarEngineUserManager
{
    /// <summary>
    /// Gets all users list form Sonar Engine API
    /// </summary>
    /// <returns></returns>
    Task<List<SEUser>> GetAllUsersAsync(int offset = 0);
    
    /// <summary>
    /// Get single user from Sonar Engine API
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    Task<User> GetSingleUserAsync(string userId);

    /// <summary>
    /// Gets a password hash algorithm model
    /// </summary>
    /// <param name="email">User email that for whom password will be taken</param>
    /// <param name="internalUserId">User internal id used later in sql. If user is new id is new guid</param>
    /// <returns></returns>
    public Task<SonarEnginePasswordDto> GetUserPassword(string email, string internalUserId);
    
    /// <summary>
    /// Add user in Sonar Engine API
    /// </summary>
    /// <param name="user"></param>
    /// <param name="tenantName"></param>
    /// <returns></returns>
    Task<string> AddUserAsync(User user, string tenantName);

    /// <summary>
    /// Edit user in Sonar Engine API
    /// </summary>
    /// <param name="userExternalId"></param>
    /// <param name="user"></param>
    /// <param name="tenantName"></param>
    /// <returns></returns>
    Task UpdateUserAsync(string userExternalId, User user, string tenantName);
    
    /// <summary>
    /// Delete user in Sonar Engine API
    /// </summary>
    /// <param name="userExternalId"></param>
    /// <returns></returns>
    Task DeleteUserAsync(string userExternalId);
    
    /// <summary>
    /// Finds tenant external id in externalAPI
    /// </summary>
    /// <param name="tenantName"></param>
    /// <returns></returns>
    Task<int> FindCustomerExternalId(string? tenantName);
 
    #region GroupsAndRoles
    
    /// <summary>
    /// Create new GroupsAndRoles / AccessId in externalAPI
    /// </summary>
    /// <param name="userExternalId"></param>
    /// <param name="tenantName"></param>
    /// <param name="rolesNames"></param>
    /// <param name="customerExternalId"></param>
    /// <returns></returns>
    Task CreateUserGroupsAndRoles(string userExternalId, string tenantName, string[] rolesNames, int customerExternalId = 0);

    /// <summary>
    /// Updates given user for roles and groups
    /// </summary>
    /// <param name="userExternalId"></param>
    /// <param name="tenantName"></param>
    /// <param name="rolesNames"></param>
    /// <returns></returns>
    Task UpdateUserGroupsAndRoles(string userExternalId, string tenantName, string[] rolesNames);
  
    /// <summary>
    /// Delete GroupsAndRoles / AccessId in external API
    /// </summary>
    /// <param name="userExternalId"></param>
    /// <param name="tenantName"></param>
    /// <returns></returns>
	Task DeleteUserGroupsAndRoles(string userExternalId, string tenantName);

    #endregion
}