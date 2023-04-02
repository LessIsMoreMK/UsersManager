using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UsersManager.Interfaces;

namespace UsersManager.BackgroundJob;

public class CreateHangfireJobs : BackgroundService
{
    private readonly IRecurringJobManager _jobManager;
    private readonly ILogger<CreateHangfireJobs> _logger;
    private readonly ISonarEngineConfigurationService _sonarEngineConfigurationService;

    public CreateHangfireJobs(
        IRecurringJobManager jobManager, 
        ILogger<CreateHangfireJobs> logger, 
        ISonarEngineConfigurationService sonarEngineConfigurationService)
    {
        _jobManager = jobManager;
        _logger = logger;
        _sonarEngineConfigurationService = sonarEngineConfigurationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = _sonarEngineConfigurationService.GetSonarEngineConfiguration();
        if (!Convert.ToBoolean(configuration.Enabled))
            return;
            
        var usersRefreshCron = configuration.UsersRefreshCron;
        
        _jobManager.AddOrUpdate<ISonarEngineSynchronizationService>(JobsIdentifier.UsersSynchronizationJob, 
            job => job.SynchronizeUsersAsync(null), usersRefreshCron);
    }
}