using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using UsersManager.Entities;
using UsersManager.Interfaces;

namespace UsersManager.Services;

public class SonarEngineConfigurationService : ISonarEngineConfigurationService
{
    private SonarEngineConfiguration _sonarEngineConfiguration;
    private readonly ILogger<SonarEngineConfigurationService> _logger;
    private readonly IConfiguration _configuration;
    public SonarEngineConfigurationService(
        ILogger<SonarEngineConfigurationService> logger, 
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        LoadSonarEngineConfiguration();
    }
        
    public void LoadSonarEngineConfiguration()
    {
        var sonarEngineConfiguration = _configuration.GetSection("sonarEngine").Get<SonarEngineConfiguration>();
            
        if (sonarEngineConfiguration.Enabled)
            _sonarEngineConfiguration = sonarEngineConfiguration with {Address = sonarEngineConfiguration.Address.EndsWith("/") ? 
                sonarEngineConfiguration.Address[..^1] : 
                sonarEngineConfiguration.Address};
        else 
            _sonarEngineConfiguration = new SonarEngineConfiguration(
                false, "", "", "", "",
                "0 0 29 2 1"); //Cron never fires

        var serializedConfiguration = JsonConvert.SerializeObject(_sonarEngineConfiguration, Formatting.Indented);
        _logger.LogInformation("SONAR ENGINE LOADED CONFIGURATION: {serializedConfiguration}", serializedConfiguration);
    }

    public SonarEngineConfiguration GetSonarEngineConfiguration()
        => _sonarEngineConfiguration;
}