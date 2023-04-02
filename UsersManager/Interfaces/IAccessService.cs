using UsersManager.Entities;

namespace UsersManager.Interfaces;

//Part of system not included in sample code, simplified cut out interface to avoid errors 
public interface IAccessService
{
    Task<string> AddUserAsync(string accessToken, User user, string clientIdName, string externalId = null);
    Task UpdateUserAsync(string accessToken, string userId, User user, string clientIdName, string externalId = null);
}