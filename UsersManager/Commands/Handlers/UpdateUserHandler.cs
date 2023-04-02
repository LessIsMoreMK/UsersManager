


// ---------------------------------------------------------------------------------------------------------------
// As this solution is just part of the service in DDD architecture left as example of command fired from endpoint
// ---------------------------------------------------------------------------------------------------------------



/*namespace UsersManager.Commands.Handlers;

public sealed class UpdateUserHandler : CommandHandlerBase<UpdateUserRequest>
{
	#region Fields

	private readonly IAccessService _accessService;
	private readonly IHttpContextAccessor _httpContextAccessor;
	private readonly ILogger<UpdateUserHandler> _logger;
	private readonly ISonarEngineUserManager _sonarEngineUserManager;
	private readonly ISonarEngineHelperService _sonarEngineHelperService;
	private readonly ISonarEngineConfigurationService _sonarEngineConfigurationService;
	
	#endregion 
	
	#region Constructors

	public UpdateUserHandler(
		IEventProcessor eventProcessor,
		IAccessService accessService, 
		ISonarEngineConfigurationService sonarEngineConfigurationService, 
		ISonarEngineUserManager sonarEngineUserManager, 
		IHttpContextAccessor httpContextAccessor, 
		ISonarEngineHelperService sonarEngineHelperService, 
		ILogger<UpdateUserHandler> logger)
		: base(eventProcessor)
	{
		_accessService = accessService;
		_sonarEngineConfigurationService = sonarEngineConfigurationService;
		_sonarEngineUserManager = sonarEngineUserManager;
		_httpContextAccessor = httpContextAccessor;
		_sonarEngineHelperService = sonarEngineHelperService;
		_logger = logger;
	}

	#endregion 

	#region Methods

	public override async Task HandleAsync(UpdateUserRequest command)
	{
		if (string.IsNullOrEmpty(command.Id))
			throw new DataException(nameof(UpdateUserRequest.Id), "ValueCanNotBeEmpty");
		if (string.IsNullOrEmpty(command.Username))
			throw new DataException(nameof(UpdateUserRequest.Username), "ValueCanNotBeEmpty");
		if (string.IsNullOrEmpty(command.FirstName))
			throw new DataException(nameof(UpdateUserRequest.FirstName), "ValueCanNotBeEmpty");
		if (string.IsNullOrEmpty(command.LastName))
			throw new DataException(nameof(UpdateUserRequest.LastName), "ValueCanNotBeEmpty");
		if (string.IsNullOrEmpty(command.ClientId))
			throw new DataException(nameof(UpdateUserRequest.ClientId), "ValueCanNotBeEmpty");

		var accessToken = await _sonarEngineHelperService.GetKeycloakDevadminAccessTokenAsync();
		var clientRoles = command.ClientRoles?.Select(e => Dto.Extensions.AsEntity(e)).ToArray();
		string userExternalId = null;
		var user = User.CreateInstance(null, command.Username, command.Email, command.FirstName, command.LastName,
			command.Enabled, command.Password, clientRoles, command.Groups.Select(group => Dto.Extensions.AsEntity(group)).ToArray());

		try
		{
			if (_sonarEngineConfigurationService.GetSonarEngineConfiguration().Enabled)
			{
				userExternalId = await _sonarEngineHelperService.GetKeycloakUserExternalId(command.Id);
				await _sonarEngineUserManager.UpdateUserAsync(userExternalId, user, 
					_sonarEngineHelperService.GetTenantNameFromRequestHeader(_httpContextAccessor?.HttpContext?.Request.Headers["TenantName"]));
			}
		}
		catch (Exception e)
		{
			_logger.LogError("Something went wrong when updating user in external service. " +
			                 "NOT UPDATING IN LOCAL STORAGE.\n" + e);
			throw;
		}
		
		await _accessService.UpdateUserAsync(accessToken, command.Id, user, command.ClientId, userExternalId);
		user = await _accessService.GetUserAsync(accessToken, command.Id, command.ClientId);
			
		await ProcessAsync(new IDomainEvent[]
		{
			new UserChanged(user), 
			new UserUpdated(user)
		});
	}
	
	#endregion
}*/