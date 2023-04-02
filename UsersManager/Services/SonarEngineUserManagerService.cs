using System.Data;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using IdentityModel.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using UsersManager.Dtos;
using UsersManager.Entities;
using UsersManager.Interfaces;

namespace UsersManager.Services;

public sealed class SonarEngineUserManagerService : ISonarEngineUserManager
{
	#region Fields
	
	private readonly SonarEngineConfiguration _sonarEngineConfiguration;
	private readonly ILogger<SonarEngineUserManagerService> _logger;
	private readonly ISonarEngineHelperService _sonarEngineHelperService;
	private readonly HttpClient _httpClient;
	private readonly Dictionary<DateTime, string> _bearer;

	#endregion
	
	#region Constructor
	
	public SonarEngineUserManagerService(
		ISonarEngineConfigurationService sonarEngineConfigurationService, 
		ILogger<SonarEngineUserManagerService> logger, 
		ISonarEngineHelperService sonarEngineHelperService)
	{
		_logger = logger;
		_sonarEngineHelperService = sonarEngineHelperService;
		_sonarEngineConfiguration = sonarEngineConfigurationService.GetSonarEngineConfiguration();
		
		var httpClientHandler = new HttpClientHandler();
		httpClientHandler.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
		_httpClient = new HttpClient(httpClientHandler);

		_bearer = new Dictionary<DateTime, string>();
	}
	
	#endregion
	
	#region Methods
	
	public async Task<List<SEUser>> GetAllUsersAsync(int offset = 0)
	{
		var pageSize = 100;
		var request = new HttpRequestMessage(HttpMethod.Get, 
			$"{_sonarEngineConfiguration.Address}/api/telemetria/users/?offset={offset}&pageSize={pageSize}");
		request.SetBearerToken(await GetExternalApiTokenAsync());
		
		var response = await _httpClient.SendAsync(request);
		LogAndThrowIfError(response, nameof(GetAllUsersAsync));
		
		var usersList = JsonConvert.DeserializeObject<SEUsersList>(await response.Content.ReadAsStringAsync());
		
		var results = usersList is null ? new List<SEUser>() : usersList?.Results;

		if (results.Count < pageSize)
			return results;

		var remainingResults = await GetAllUsersAsync(offset + pageSize);
		results.AddRange(remainingResults);

		return results;
	}
	
	public async Task<User> GetSingleUserAsync(string userId)
	{
		if (userId.Contains(":"))
			userId = userId.Split(":")[2];
		
		var request = new HttpRequestMessage(HttpMethod.Get, 
			$"{_sonarEngineConfiguration.Address}/api/telemetria/users/{userId}/");
		request.SetBearerToken(await GetExternalApiTokenAsync());
		
		var response = await _httpClient.SendAsync(request);
		var user = JsonConvert.DeserializeObject<SEUser>(await response.Content.ReadAsStringAsync());
		
		var roles = new List<Role>();
		foreach (var customer in user.Customers)
			foreach (var role in customer.Roles)
				roles.Add(new Role(customer.Id.ToString(),customer.Name+"|"+role,role+"Description"));

		return new User(user.Id.ToString(), user.UserName, user.Email, user.FirstName, user.LastName, user.IsActive,null, roles.ToArray(),new Group[]{});
	}
	
	public async Task<SonarEnginePasswordDto> GetUserPassword(string email, string internalUserId)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, 
			$"{_sonarEngineConfiguration.Address}/api/telemetria/users/password_hash/?email={email}");
		request.SetBearerToken(await GetExternalApiTokenAsync());
		var response = await _httpClient.SendAsync(request);
		
		LogAndThrowIfError(response, nameof(GetUserPassword), email);

		if (!response.IsSuccessStatusCode)
			return null;
		
		var deserializedResponse = JsonConvert.DeserializeObject<SonarEnginePasswordDto>(await response.Content.ReadAsStringAsync());
		var responseWithInternalData = deserializedResponse with {email = email, internalId = internalUserId};

		return responseWithInternalData;
	}

	public async Task UpdateUserAsync(string userExternalId, User user, string tenantName)
	{
		var userDto = new SEUser(null, user.Username, user.FirstName, user.LastName, user.Email, user.Enabled, null);
		
		var request = new HttpRequestMessage(HttpMethod.Patch, $"{_sonarEngineConfiguration.Address}/api/telemetria/users/{userExternalId}/");
		request.SetBearerToken(await GetExternalApiTokenAsync());
		
		request.Content = new StringContent(JsonConvert.SerializeObject(userDto), Encoding.UTF8);
		request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
		var response = await _httpClient.SendAsync(request);
		
		if (user.Password != null)
			await ChangePassword(int.Parse(userExternalId), user.Password);

		LogAndThrowIfError(response, nameof(UpdateUserAsync), user.Email);

		var rolesForExternalApi = _sonarEngineHelperService.GetRolesNamesForExternalAPI(user.ClientRoles.ToArray(), tenantName);
		if (rolesForExternalApi.Any())
			await UpdateUserGroupsAndRoles(userExternalId, tenantName, rolesForExternalApi);
		else
			await DeleteUserGroupsAndRoles(userExternalId, tenantName);
	}
	
	public async Task<string> AddUserAsync(User user, string tenantName)
	{
		if (user.Password != null)
			if (!ValidatePassword(user.Password))
			{
				_logger.LogError("SonarEngineUserManagerService>UpdateUserAsync: Cannot add user because password requirements not passed.");
				throw new DataException(nameof(user.Password));//, "Password_Requirements_Not_Passed");
			}
		
		var customerExternalId = await FindCustomerExternalId(tenantName);
		
		var addUserDto = new SEUserToAdd(null, user.Username, user.FirstName, user.LastName, 
			user.Email, user.Enabled, null, user.Password);
		
		var request = new HttpRequestMessage(HttpMethod.Post, $"{_sonarEngineConfiguration.Address}/api/telemetria/users/");
		request.SetBearerToken(await GetExternalApiTokenAsync());
		
		request.Content = new StringContent(JsonConvert.SerializeObject(addUserDto), Encoding.UTF8);
		
		request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
		var response = await _httpClient.SendAsync(request);
		
		LogAndThrowIfError(response, nameof(AddUserAsync), user.Email);
		
		var userResponse = JsonConvert.DeserializeObject<SEUser>(await response.Content.ReadAsStringAsync());
		var rolesForExternalApi = _sonarEngineHelperService.GetRolesNamesForExternalAPI(user.ClientRoles.ToArray(), tenantName);
		if (rolesForExternalApi.Any())
			await CreateUserGroupsAndRoles(userResponse?.Id.ToString(), tenantName, rolesForExternalApi, customerExternalId);

		return userResponse?.Id.ToString();
	}

	public async Task DeleteUserAsync(string userExternalId)
	{
		var request = new HttpRequestMessage(HttpMethod.Delete, $"{_sonarEngineConfiguration.Address}/api/telemetria/users/{userExternalId}/");
		request.SetBearerToken(await GetExternalApiTokenAsync());

		var response = await _httpClient.SendAsync(request);

		LogAndThrowIfError(response, nameof(DeleteUserAsync));
	}
	
	public async Task<int> FindCustomerExternalId(string tenantName)
	{
		var customers = await GetSonarEngineCustomerList();
		var customerId = customers.Where(c => c.Name == tenantName).Select(c => c.Id).FirstOrDefault();
		return customerId;
	}
	
	#region GroupsAndRoles
	
	public async Task UpdateUserGroupsAndRoles(string userExternalId, string tenantName, string[] rolesNames)
	{
		var tenantCustomerExternalId = await FindCustomerExternalId(tenantName);
		var accessId = await FindAccessId(Convert.ToInt32(userExternalId), tenantCustomerExternalId);
		
		var seUserGroupRolesObject = new SEUserGroupsAndRolesDto(true, 
			Convert.ToInt32(userExternalId), 
			Convert.ToInt32(userExternalId),
			rolesNames);
		
		var request = new HttpRequestMessage(HttpMethod.Put, 
			$"{_sonarEngineConfiguration.Address}/api/telemetria/sonar-web/sw_access/{accessId}/");
		request.SetBearerToken(await GetExternalApiTokenAsync());
		request.Content = new StringContent(JsonConvert.SerializeObject(seUserGroupRolesObject), Encoding.UTF8);
		request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
		
		var response = await _httpClient.SendAsync(request);
		LogAndThrowIfError(response, nameof(UpdateUserGroupsAndRoles));
	}
	
	public async Task CreateUserGroupsAndRoles(string userExternalId, string tenantName, string[] rolesNames, int customerExternalId = 0)
	{
		var seUserGroupRolesObject = new SEUserGroupsAndRolesDto(true, 
			Convert.ToInt32(userExternalId), 
			customerExternalId == 0 ? await FindCustomerExternalId(tenantName) : customerExternalId, 
			rolesNames);
		
		var request = new HttpRequestMessage(HttpMethod.Post, 
			$"{_sonarEngineConfiguration.Address}/api/telemetria/sonar-web/sw_access/");
		request.SetBearerToken(await GetExternalApiTokenAsync());
		request.Content = new StringContent(JsonConvert.SerializeObject(seUserGroupRolesObject), Encoding.UTF8);
		request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
		
		var response = await _httpClient.SendAsync(request);
		LogAndThrowIfError(response, nameof(CreateUserGroupsAndRoles));
	}
	
	public async Task DeleteUserGroupsAndRoles(string userExternalId, string tenantName)
	{
		var tenantCustomerExternalId = await FindCustomerExternalId(tenantName);
		var accessId = await FindAccessId(Convert.ToInt32(userExternalId), tenantCustomerExternalId);
		
		var request = new HttpRequestMessage(HttpMethod.Delete, 
			$"{_sonarEngineConfiguration.Address}/api/telemetria/sonar-web/sw_access/{accessId}/");
		request.SetBearerToken(await GetExternalApiTokenAsync());
		
		var response = await _httpClient.SendAsync(request);
		LogAndThrowIfError(response, nameof(DeleteUserGroupsAndRoles));
	}
	
	#endregion
	
	#endregion
	
	#region Private Helpers
	
	private async ValueTask<string> GetExternalApiTokenAsync()
	{
		if (_bearer.Any())
		{
			var tokenTime = (DateTime.Now - _bearer.Keys.First()).TotalSeconds;
			if (tokenTime < 270)
				return _bearer.Values.First();

			_bearer.Clear();
		}
		/*var serializationSettings = new JsonSerializerSettings
		{
			Formatting = Formatting.Indented,
			DateFormatHandling = DateFormatHandling.IsoDateFormat,
			DateTimeZoneHandling = DateTimeZoneHandling.Utc,
			NullValueHandling = NullValueHandling.Ignore,
			ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
			ContractResolver = new ReadOnlyJsonContractResolver(),
			Converters = new List<JsonConverter>
			{
				new Iso8601TimeSpanConverter()
			}
		};*/
		
		var request = new HttpRequestMessage(HttpMethod.Post, $"{_sonarEngineConfiguration.Address}/api/token/");
		var loginCredentials = new SELoginCredentialsDto(_sonarEngineConfiguration.Login, _sonarEngineConfiguration.Password, _sonarEngineConfiguration.App);
		request.Content= new StringContent(JsonConvert.SerializeObject(loginCredentials), Encoding.UTF8);//, serializationSettings);
		request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
		var response = await _httpClient.SendAsync(request);

		if (!response.IsSuccessStatusCode)
		{
			_logger.LogError("External API(Spidap Engine) not available.");
			throw new Exception("External_Service_Unavailable");//, "External_Service_Unavailable");
		}
		
		var tokenModel = JsonConvert.DeserializeObject<SETokenModelDto>(await response.Content.ReadAsStringAsync());
		_bearer.Add(DateTime.Now, tokenModel.Access);
		return _bearer.Values.First();
	}
	
	private async Task<List<SECustomerDto>> GetSonarEngineCustomerList(int offset = 0)
	{
		var pageSize = 100;
		var request = new HttpRequestMessage(HttpMethod.Get, 
			$"{_sonarEngineConfiguration.Address}/api/telemetria/customers/?offset={offset}&pageSize={pageSize}");
		request.SetBearerToken(await GetExternalApiTokenAsync());
		
		var response = await _httpClient.SendAsync(request);
		LogAndThrowIfError(response, nameof(FindCustomerExternalId));
		
		var customerList = JsonConvert.DeserializeObject<SECustomerListDto>(await response.Content.ReadAsStringAsync());
		var results = customerList is null ? new List<SECustomerDto>() : customerList?.Results;

		if (results.Count < pageSize)
			return results;

		var remainingResults = await GetSonarEngineCustomerList(offset + pageSize);
		results.AddRange(remainingResults);

		return results;
	}
	
	private async Task<List<SEAccessDto>> GetSonarEngineAccessList(int offset = 0)
	{
		var pageSize = 100;
		var request = new HttpRequestMessage(HttpMethod.Get, 
			$"{_sonarEngineConfiguration.Address}/api/telemetria/sonar-web/sw_access/?offset={offset}&pageSize={pageSize}");
		request.SetBearerToken(await GetExternalApiTokenAsync());
		
		var response = await _httpClient.SendAsync(request);
		LogAndThrowIfError(response, nameof(GetSonarEngineAccessList));
		
		var accessList = JsonConvert.DeserializeObject<SEAccessListDto>(await response.Content.ReadAsStringAsync());
		var results = accessList is null ? new List<SEAccessDto>() : accessList?.Results;

		if (results.Count < pageSize)
			return results;

		var remainingResults = await GetSonarEngineAccessList(offset + pageSize);
		results.AddRange(remainingResults);

		return results;
	}
	
	private async Task<int> FindAccessId(int userExternalId, int tenantCustomerExternalId)
	{
		var accessList = await GetSonarEngineAccessList();

		var accessId = accessList.Where(c => c.CustomerDto.Id.Equals(tenantCustomerExternalId))
			.Where(u => u.UserDto.Id.Equals(userExternalId))
			.Select(a => a.Id).FirstOrDefault();

		return accessId;
	}
	
	private async Task ChangePassword(int userExternalId, string password)
	{
		if (password is "" or null)
			return;
		
		if (!ValidatePassword(password))
		{
			_logger.LogError("SonarEngineUserManagerService>ChangePasswordAsync: Cannot change password because requirements not passed.");
			throw new DataException(nameof(password));//, "Common_PasswordValidationFormat");
		}
		
		var changePasswordDto = new SEPasswordDto(password);
		var request = new HttpRequestMessage(HttpMethod.Post, 
			$"{_sonarEngineConfiguration.Address}/api/telemetria/users/{userExternalId}/reset_password/");
		request.SetBearerToken(await GetExternalApiTokenAsync());
		request.Content = new StringContent(JsonConvert.SerializeObject(changePasswordDto), Encoding.UTF8);
		request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
		
		var changePasswordResponse = await _httpClient.SendAsync(request);
		
		LogAndThrowIfError(changePasswordResponse, nameof(ChangePassword));
	}
	
	private bool ValidatePassword(string password)
	{
		var digit = password.Any(c => char.IsDigit(c));
		var upperCase = password.Any(c => char.IsUpper(c));
		return password is {Length: > 7} && digit && upperCase;
	}

	private void LogAndThrowIfError(HttpResponseMessage response, string methodName, string email = null)
	{
		var emailContent = email != null ? $" User email: {email}" : "";
		var statusCode = response.StatusCode;
		
		if (response.IsSuccessStatusCode)
			_logger.LogInformation("SonarEngineUserManagerService>{methodName}: {statusCode}{emailContent}", methodName, statusCode, emailContent);
		else
		{
			var reason = response.Content.ReadAsStringAsync().Result;

			_logger.LogError("SonarEngineUserManagerService>{methodName}: StatusCode: {statusCode} " +
			                 "Reason: {reason}{emailContent}", methodName, statusCode, reason, emailContent);

			if (methodName == nameof(GetUserPassword))
				return;
			
			if (reason.Contains("too common"))
				throw new DataException("Password");//, "Password_Is_Too_Common");
			
			if (reason.Contains("User with e-mail"))
				throw new DataException("Email");//, "Access_UserWithTheSameEmailExists");
			
			if (reason.Contains("Enter a valid email address."))
				throw new DataException("Email");//, "Access_UserEmailIncorrect");
			
			if (reason.Contains("A user with that username already exists."))
				throw new DataException("Username");//, "Access_UserWithTheSameUsernameExists");
			
			if (reason.Contains("No permission to update this user. You are trying to update Apator user"))
				throw new Exception("No_Permission_To_Update_This_User");//, "No_Permission_To_Update_This_User");
			
			throw new Exception("External_Service_Unknown_Exception");//, "External_Service_Unknown_Exception");
		}
	}

	#endregion
}