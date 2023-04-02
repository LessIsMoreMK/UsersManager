using UsersManager.Entities;

namespace UsersManager.Interfaces;

public interface ISonarEngineHelperService
{
    /// <summary>
    /// Gets tenantName from request tenantNameHeader
    /// </summary>
    /// <param name="tenantNameHeader"></param>
    /// <returns></returns>
    string GetTenantNameFromRequestHeader(string tenantNameHeader);
        
    /// <summary>
    /// Converts object ClientRoles to just names needed for external API
    /// </summary>
    /// <param name="roles"></param>
    /// <param name="tenantName"></param>
    /// <returns></returns>
    string[] GetRolesNamesForExternalAPI(IEnumerable<Role> roles, string tenantName);
        
    /// <summary>
    /// Generate access token for keycloak requests on master realm
    /// </summary>
    /// <returns></returns>
    ValueTask<string> GetKeycloakDevadminAccessTokenAsync();
        
    /// <summary>
    /// Gets user externalId from keycloak user request
    /// </summary>
    /// <param name="internalId"></param>
    /// <returns></returns>
    Task<string> GetKeycloakUserExternalId(string internalId);
}