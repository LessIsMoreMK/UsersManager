using System.Security.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using UsersManager.Entities;
using UsersManager.Interfaces;
using IdentityModel.Client;

namespace UsersManager.Services;

public class SonarEngineHelperService : ISonarEngineHelperService
{
    #region Fields
	
    private readonly IConfiguration _configuration;
    private readonly ILogger<SonarEngineHelperService> _logger;
    private readonly HttpClient _httpClient;
    
    private readonly Dictionary<DateTime, string> _bearer;
    
    private readonly string _keycloakBaseUrl;
    private readonly string _keycloakRealmName;
	
    #endregion
	
    #region Constructor
	
    public SonarEngineHelperService(
	    ILogger<SonarEngineHelperService> logger, 
	    IConfiguration configuration)
    {
	    _logger = logger;
	    _configuration = configuration;
		
	    var httpClientHandler = new HttpClientHandler();
	    httpClientHandler.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
	    _httpClient = new HttpClient(httpClientHandler);
	    
	    _bearer = new Dictionary<DateTime, string>();
	    
	    _keycloakBaseUrl = configuration.GetValue<string>("keycloakClient:url") ?? 
	                       throw new ArgumentException("Keycloak base url not specified.", nameof(_keycloakBaseUrl));
	    _keycloakRealmName = configuration.GetValue<string>("keycloakClient:realm") ?? 
	                         throw new ArgumentException("Keycloak realm not specified.", nameof(_keycloakRealmName));
    }
	
    #endregion
    
    #region Methods
    
    public string GetTenantNameFromRequestHeader(string tenantNameHeader) 
        => !string.IsNullOrEmpty(tenantNameHeader) ? Uri.UnescapeDataString(tenantNameHeader) : "";
    
    public string[] GetRolesNamesForExternalAPI(IEnumerable<Role> roles, string tenantName)
        =>  roles.Where(t => t.Name.StartsWith(tenantName))
            .Select(r => r.Name.Substring(
                r.Name.IndexOf("|", StringComparison.Ordinal)+1, 
                r.Name.Length - (r.Name.IndexOf("|", StringComparison.Ordinal)+1))).ToArray();
    
    public async ValueTask<string> GetKeycloakDevadminAccessTokenAsync()
	{
		if (_bearer.Any())
		{
			var tokenLifetime = (DateTime.Now - _bearer.Keys.First()).TotalSeconds;
			if (tokenLifetime < 50)
				return _bearer.Values.First();

			_bearer.Clear();
		}
		
		var dict = new Dictionary<string, string>();
		dict.Add("username", _configuration.GetValue<string>("keycloakClient:username"));
		dict.Add("password", _configuration.GetValue<string>("keycloakClient:password"));
		dict.Add("grant_type", "password");
		dict.Add("client_id", "admin-cli");
	
		var request = new HttpRequestMessage(HttpMethod.Post, 
				$"{_keycloakBaseUrl}/auth/realms/master/protocol/openid-connect/token")
				{ Content = new FormUrlEncodedContent(dict) };
		var response = await _httpClient.SendAsync(request);
		
		var responseContent = await response.Content.ReadAsStringAsync();
		var definition = new { access_token = "" };
		var responseObject = JsonConvert.DeserializeAnonymousType(responseContent, definition);
		
		_bearer.Add(DateTime.Now, responseObject.access_token);
		return _bearer.Values.First();
	}
	
	public async Task<string> GetKeycloakUserExternalId(string internalId)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, 
			$"{_keycloakBaseUrl}/auth/admin/realms/{_keycloakRealmName}/users/{internalId}");
		request.SetBearerToken(await GetKeycloakDevadminAccessTokenAsync());
		var response = await _httpClient.SendAsync(request);
		var user = JsonConvert.DeserializeObject<UserWithAttributes>(await response.Content.ReadAsStringAsync());

		if (user?.Attributes?.ExternalId.First() != null)
			return user.Attributes.ExternalId.First();
		
		throw new ArgumentException("User with internal id: " + internalId + " don't have externalId.");
	}
    
	#endregion
}