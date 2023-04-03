using System.Security.Authentication;
using System.Text;
using IdentityModel.Client;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Npgsql;
using NpgsqlTypes;
using UsersManager.Commands;
using UsersManager.Dtos;
using UsersManager.Entities;
using UsersManager.Enums;
using UsersManager.Interfaces;

namespace UsersManager.Services;

public class SonarEngineSynchronizationService : ISonarEngineSynchronizationService
{
    #region Fields
	
    private readonly IAccessService _accessService;
    private readonly ISonarEngineHelperService _sonarEngineHelperService;
    private readonly ISonarEngineUserManager _sonarEngineUserManager;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<SonarEngineSynchronizationService> _logger;
    
    private readonly HttpClient _httpClient;
    
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IDistributedCache _distributedCache;
    private SynchronizationStatus _synchronizationStatus;
    private List<string> _failedUsers;
    
    private readonly string _keycloakBaseUrl;
    private readonly string _keycloakRealmName;
    private readonly string _clientIdName;
    private readonly string _postgresHost;
    private readonly string _postgresUsername;
    private readonly string _postgresPassword;

    #endregion
	
    #region Constructor
	
    public SonarEngineSynchronizationService(
	    ILogger<SonarEngineSynchronizationService> logger, 
	    IAccessService accessService, 
	    ISonarEngineHelperService sonarEngineHelperService, 
	    ISonarEngineUserManager sonarEngineUserManager, 
	    IConfiguration configuration,  
	    IServiceScopeFactory serviceScopeFactory, 
	    IDistributedCache distributedCache)
    {
	    _logger = logger;
	    _accessService = accessService;
	    _sonarEngineHelperService = sonarEngineHelperService;
	    _sonarEngineUserManager = sonarEngineUserManager;
	    _serviceScopeFactory = serviceScopeFactory;
	    _distributedCache = distributedCache;

	    var httpClientHandler = new HttpClientHandler();
	    httpClientHandler.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
	    _httpClient = new HttpClient(httpClientHandler);
	    
	    _synchronizationStatus = SynchronizationStatus.Undone;
	    _failedUsers = new List<string>();

	    _keycloakBaseUrl = configuration.GetValue<string>("keycloakClient:url") ?? 
	                       throw new ArgumentException("Keycloak base url not specified.", nameof(_keycloakBaseUrl));
	    _keycloakRealmName = configuration.GetValue<string>("keycloakClient:realm") ?? 
	                         throw new ArgumentException("Keycloak realm not specified.", nameof(_keycloakRealmName));
	    _clientIdName = configuration.GetValue<string>("keycloakClient:clientId") ?? 
	                    throw new ArgumentException("Keycloak client id not specified.", nameof(_clientIdName));
	    _postgresHost = configuration.GetValue<string>("postgresConnection:host") ?? 
	                    throw new ArgumentException("Postgres host not specified.", nameof(_postgresHost));
	    _postgresUsername = configuration.GetValue<string>("postgresConnection:username") ?? 
	                        throw new ArgumentException("Postgres username not specified.", nameof(_postgresUsername));
	    _postgresPassword = configuration.GetValue<string>("postgresConnection:password") ?? 
	                        throw new ArgumentException("Postgres password not specified.", nameof(_postgresPassword));
    }

    #endregion
    
    #region Methods
    
    public async Task SynchronizeUsersAsync(string? tenantName = null)
    {
        _failedUsers.Clear();
        _synchronizationStatus = SynchronizationStatus.Running;
        
        var groupTenantList = await GetKeycloakGroupList();
        var externalUsers = await PrepareUsers(groupTenantList, tenantName);
        var internalUsers = await GetKeycloakUsersList();
        var accessToken = await _sonarEngineHelperService.GetKeycloakDevadminAccessTokenAsync();
        
        _logger.LogInformation("externalUsers: " + JsonConvert.SerializeObject(externalUsers, Formatting.Indented));
        _logger.LogInformation("internalUsers: " + JsonConvert.SerializeObject(internalUsers, Formatting.Indented));
        
        var options = new ParallelOptions { MaxDegreeOfParallelism = 8 };
        await Parallel.ForEachAsync(externalUsers, options, async (user, token) =>
        {
	        if (internalUsers.Any(u => u.AttributesDto.ExternalId.First() == user.Id)) //User already in system - UPDATE
	        {
		        try
		        {
			        var externalId = internalUsers.First(u => u.AttributesDto.ExternalId.First() == user.Id).AttributesDto.ExternalId.First();
			        user = user with {Id = internalUsers.First(u => u.AttributesDto.ExternalId.First() == externalId).Id};
#pragma warning disable CS4014
			        _accessService.UpdateUserAsync(accessToken, user.Id, user, _clientIdName, externalId);
			        PropagateEvents(user, groupTenantList);
#pragma warning restore CS4014
			        _logger.LogInformation($"SonarEngineSynchronizationService> User: {user.Email} updated in local storage.", user.Email);
		        }
		        catch (Exception e)
		        {
			        _failedUsers.Add(user.Email);
			        _synchronizationStatus = SynchronizationStatus.Error;
			        _logger.LogError("SonarEngineSynchronizationService> User update error: " + e);
		        }
	        }
	        else //New user - ADD
	        {
		        try
		        {
			        var userExternalId = user.Id; 
			        user = user with{ Id = await _accessService.AddUserAsync(accessToken, user, _clientIdName, userExternalId)};
		            
#pragma warning disable CS4014
			        PropagateEvents(user, groupTenantList, true);
#pragma warning restore CS4014
			        _logger.LogInformation("SonarEngineSynchronizationService> New user synchronized: " + JsonConvert.SerializeObject(user, Formatting.Indented));
		        }
		        catch (Exception e)
		        {
			        _failedUsers.Add(user.Email);
			        _synchronizationStatus = SynchronizationStatus.Error;
			        _logger.LogError("SonarEngineSynchronizationService> New user synchronization error: " + e);
		        }
	        }
        });
        
        var passwordHashes = await GetUsersPasswordsAsync(externalUsers);
        await UpdatePostgresDatabase(passwordHashes);
        await ClearKeycloakUserCache();
        
        _failedUsers = _failedUsers.Distinct().ToList();
        if (_synchronizationStatus == SynchronizationStatus.Running)
	        _synchronizationStatus = SynchronizationStatus.Done;
            
        await SetSynchronizationState(tenantName, groupTenantList);
    }
    
    public async Task<GetSynchronizeStatusResponse> GetSynchronizationStatus(string tenantName)
    {
        try
        {
            await _lock.WaitAsync();
            var cacheKey = $"{tenantName}|usersSyncResult";
            var cachedData = await _distributedCache.GetAsync(cacheKey);

            if (cachedData is null) 
    	        return new GetSynchronizeStatusResponse(SynchronizationStatus.Undone, null, new List<string>());
            
            var cachedString = Encoding.UTF8.GetString(cachedData);
            return JsonConvert.DeserializeObject<GetSynchronizeStatusResponse>(cachedString);
        }
        finally
        {
            _lock.Release();
        }
    }
    
    #endregion
    
    #region Private Helpers 
    
    private async Task SetSynchronizationState(string? tenantName, List<KeycloakGroupDto> groupTenantList)
    {
	    var state = new GetSynchronizeStatusResponse(_synchronizationStatus, DateTime.UtcNow, _failedUsers);
	        
	    try
	    {
		    await _lock.WaitAsync();

		    string cacheKey;
		    if (tenantName is null)
		    {
			    foreach (var tenant in groupTenantList)
			    {
				    cacheKey = $"{tenant.Name}|usersSyncResult";
				    await _distributedCache.RemoveAsync(cacheKey);
				    await _distributedCache.SetAsync(cacheKey, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state, Formatting.Indented)));
			    }

			    return;
		    }
		        
		    cacheKey = $"{tenantName}|usersSyncResult";
		    await _distributedCache.RemoveAsync(cacheKey);
		    await _distributedCache.SetAsync(cacheKey, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state, Formatting.Indented)));
	    }
	    finally
	    {
		    _lock.Release();
	    }
    }
    
    private async Task<List<KeycloakRoleDto>> GetKeycloakRolesList()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, 
	        $"{_keycloakBaseUrl}/auth/admin/realms/{_keycloakRealmName}/roles");
        request.SetBearerToken(await _sonarEngineHelperService.GetKeycloakDevadminAccessTokenAsync());
        var response = await _httpClient.SendAsync(request);
		
        return JsonConvert.DeserializeObject<List<KeycloakRoleDto>>(await response.Content.ReadAsStringAsync());
    }
    
    private async Task<List<KeycloakGroupDto>> GetKeycloakGroupList()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, 
	        $"{_keycloakBaseUrl}/auth/admin/realms/{_keycloakRealmName}/groups");
        request.SetBearerToken(await _sonarEngineHelperService.GetKeycloakDevadminAccessTokenAsync());
        var response = await _httpClient.SendAsync(request);
		
        return JsonConvert.DeserializeObject<List<KeycloakGroupDto>>(await response.Content.ReadAsStringAsync());
    }
    
    /// <summary>
    /// Gets internal users id with attribute externalId
    /// </summary>
    /// <returns></returns>
    private async Task<List<KeycloakUserWithAttributeDto>> GetKeycloakUsersList()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, 
	        $"{_keycloakBaseUrl}/auth/admin/realms/{_keycloakRealmName}/users");
        request.SetBearerToken(await _sonarEngineHelperService.GetKeycloakDevadminAccessTokenAsync());
        var response = await _httpClient.SendAsync(request);
        var users = JsonConvert.DeserializeObject<List<KeycloakUserWithAttributeDto>>(await response.Content.ReadAsStringAsync());
        
        var usersWithAttributes = 
	        users?.Where(u => u.AttributesDto != null && 
	                          u.AttributesDto.ExternalId != null && 
	                          u.AttributesDto.ExternalId.First() != "-1").ToList();

        return usersWithAttributes;
    }
    
    private async Task ClearKeycloakUserCache()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, 
	        $"{_keycloakBaseUrl}/auth/admin/realms/{_keycloakRealmName}/clear-user-cache");
        request.SetBearerToken(await _sonarEngineHelperService.GetKeycloakDevadminAccessTokenAsync());
        var response = await _httpClient.SendAsync(request);
        
        if (response.IsSuccessStatusCode)
	        _logger.LogInformation("Users cache cleared.");
        else
			_logger.LogError("User clear cache error: " + await response.Content.ReadAsStringAsync());
    }

    
    private async Task<List<User>> PrepareUsers(IReadOnlyCollection<KeycloakGroupDto> groupsList, string? tenantName = null)
    {
        int? tenantExternalId = null;
        if (tenantName is not null)
	        tenantExternalId = await _sonarEngineUserManager.FindCustomerExternalId(tenantName);
        
        var rolesList = await GetKeycloakRolesList();
        var defaultRole = rolesList.FirstOrDefault(r => r.Name == "default-roles-sonar-web");
        
        var seUsers = await _sonarEngineUserManager.GetAllUsersAsync();
        seUsers = await FilterCustomersNotInSystem(seUsers, groupsList);
        
        var externalUsers = new List<User>();
        foreach (var seUser in seUsers)
        {
	        if (tenantExternalId is not null && seUser.Customers!.All(c => c.Id != tenantExternalId))
		        continue;
	        
	        var seUserRolesList = seUser.Customers
		        .SelectMany(customer => customer.Roles.Select(role => (customer.Name, role)))
		        .Select(tuple =>
		        {
			        var role = rolesList.FirstOrDefault(r => r.Name == $"{tuple.Name}|{tuple.role}");
			        var roleId = role?.Id ?? defaultRole?.Id;
			        var roleDesc = role?.Name ?? $"{tuple.role}Description";
			        return new Role(roleId, tuple.Name, roleDesc);
		        })
		        .ToList();

	        if (defaultRole?.Id != null) seUserRolesList.Add(new Role(defaultRole?.Id, defaultRole?.Name, defaultRole?.Name + "Description"));

	        var groups = seUser.Customers
		        .Where(customer => groupsList.Any(group => group.Name.Equals(customer.Name)))
		        .Select(customer => new Group(groupsList.First(group => group.Name.Equals(customer.Name)).Id, customer.Name))
		        .ToList();

	        externalUsers.Add(new User(
		        seUser.Id.ToString(),
		        seUser.UserName,
		        seUser.Email,
		        seUser.FirstName,
		        seUser.LastName,
		        seUser.IsActive, 
		        "", 
		        seUserRolesList.ToArray(),
		        groups.ToArray()
	        ));
        }

        return externalUsers;
    }

    private Task<List<SEUser>> FilterCustomersNotInSystem(IEnumerable<SEUser> seUsers, IEnumerable<KeycloakGroupDto> groupsList)
    {
        var groupNames = groupsList.Select(g => g.Name).ToList();
        
        var seModifiedUsers = seUsers.Select(seUser => seUser with 
	        {Customers = seUser.Customers.Where(customer => groupNames.Contains(customer.Name)).ToArray()}).ToList();

        return Task.FromResult(seModifiedUsers);
    }

    private async Task PropagateEvents(User user, List<KeycloakGroupDto> groups, bool isNew = false)
    {
        foreach (var tenant in groups)
        {
	        if (user.Groups.All(g => g.Name != tenant.Name)) 
		        continue;
			            
	        //Commented out as it's part of system not included in sample code
	        /*using var scope = _serviceScopeFactory.CreateScope();
	        scope.ServiceProvider.SetCurrentTenantId(tenant.Id);
	        var eventProcessor = scope.ServiceProvider.GetRequiredService<IEventProcessor>();
	        if (isNew)
		        await eventProcessor.ProcessAsync(new IDomainEvent[] {new UserAdded(user)});
	        else
				await eventProcessor.ProcessAsync(new IDomainEvent[] {new UserChanged(user), new UserUpdated(user)});*/
        }
    }
    
    
    private async Task<List<SonarEnginePasswordDto>> GetUsersPasswordsAsync(IReadOnlyCollection<User> users)
    {
        var tasks = new List<Task<SonarEnginePasswordDto>>();
        for (var i = 0; i < users.Count; i++)
	        tasks.Add(_sonarEngineUserManager.GetUserPassword(users.ElementAt(i).Email, users.ElementAt(i).Id));

        var results = await Task.WhenAll(tasks);
        
        for (var i = 0; i < results.Length; i++)
	        if (results[i] == null)
		        _failedUsers.Add(users.ElementAt(i).Email);
        
        return results.Where(r => r != null).ToList();
    }
    
    private async Task AddUniqueConstrainOnPostgresDatabaseCredentialTable()
    {
	    try
	    {
		    var connectionString = $"Host={_postgresHost};User Id={_postgresUsername};Password={_postgresPassword};Database=postgres;Enlist=true";
		    await using var connection = new NpgsqlConnection(connectionString);
		    await connection.OpenAsync();

		    // Query the schema to check if the unique constraint already exists
		    await using var command1 = new NpgsqlCommand(
			    @"SELECT COUNT(*)
				          FROM pg_constraint
				          WHERE conname = 'unique_user_id' AND conrelid = 'public.credential'::regclass;",
			    connection);

		    var constraintExists = Convert.ToInt32(await command1.ExecuteScalarAsync()) > 0;
		    if (!constraintExists)
		    {
			    // Add the unique constraint if it doesn't exist
			    await using var command2 = new NpgsqlCommand(
				    @"ALTER TABLE public.credential ADD CONSTRAINT unique_user_id UNIQUE (user_id);",
				    connection);
			    await command2.ExecuteNonQueryAsync();

			    _logger.LogInformation("Postgres credential table unique_user_id constraint added.");
		    }
		    else
			    _logger.LogInformation("Postgres credential table unique_user_id constraint already exists.");
	    }
	    catch (Exception e)
	    {
		    _logger.LogError(e, "Error adding unique constraint to Postgres credential table.");
	    }
    }
    
    private async Task UpdatePostgresDatabase(List<SonarEnginePasswordDto> users)
    {
        await AddUniqueConstrainOnPostgresDatabaseCredentialTable();
        
        try
        {
	        var connectionString = $"Host={_postgresHost};User Id={_postgresUsername};Password={_postgresPassword};Database=postgres;Enlist=true";
	        await using var connection = new NpgsqlConnection(connectionString);
	        await connection.OpenAsync();

	        foreach (var user in users)
	        {
		        try
		        {
			        await using var command = new NpgsqlCommand(
				        @"
    INSERT INTO public.credential (
        id, salt, type, user_id, created_date, user_label, secret_data, credential_data, priority
    ) VALUES (
        @id, @salt, @type, @user_id, @created_date, @user_label, @secret_data, @credential_data, @priority
    ) ON CONFLICT (user_id) DO UPDATE SET
        salt = excluded.salt,
        type = excluded.type,
        created_date = excluded.created_date,
        user_label = excluded.user_label,
        secret_data = excluded.secret_data,
        credential_data = excluded.credential_data,
        priority = excluded.priority;
",
				        connection);
		        
			        var unixTimestamp = (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
			        var base64EncodedSalt = Convert.ToBase64String(Encoding.UTF8.GetBytes(user.salt));
		        
			        command.Parameters.AddWithValue("id", Guid.NewGuid());
			        command.Parameters.AddWithValue("salt", NpgsqlDbType.Bytea, Encoding.UTF8.GetBytes(""));
			        command.Parameters.AddWithValue("type", "password");
			        command.Parameters.AddWithValue("user_id", user.internalId);
			        command.Parameters.AddWithValue("created_date", unixTimestamp);
			        command.Parameters.AddWithValue("user_label", "");
			        command.Parameters.AddWithValue("secret_data", $"{{\"value\":\"{user.value}\",\"salt\":\"{base64EncodedSalt}\",\"additionalParameters\":{{}}}}");
			        command.Parameters.AddWithValue("credential_data", $"{{\"hashIterations\":{user.iterations},\"algorithm\":\"pbkdf2-sha256\",\"additionalParameters\":{{}}}}");
			        command.Parameters.AddWithValue("priority", 10);
	        
			        await command.ExecuteNonQueryAsync();
		        }
		        catch (Exception e)
		        {
			        var email = user.email;
			        _logger.LogError("Postgres credential update table user.Email: {email} ERROR: \n{e}", email, e);
				   _failedUsers.Add(user.email);
		        }
	        }
	        
	        _logger.LogInformation("Postgres credential table updated.");
        }
        catch (Exception e)
        {
	        _synchronizationStatus = SynchronizationStatus.Error;
	        _logger.LogError("Postgres credential table update error.\n" + e);
        }
    }

    #endregion
}