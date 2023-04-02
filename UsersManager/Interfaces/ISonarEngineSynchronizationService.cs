using Hangfire;
using UsersManager.Commands;

namespace UsersManager.Interfaces;

public interface ISonarEngineSynchronizationService
{
    [AutomaticRetry(OnAttemptsExceeded = AttemptsExceededAction.Fail, Attempts = 3)]
    Task SynchronizeUsersAsync(string? tenantName = null);

    Task<GetSynchronizeStatusResponse> GetSynchronizationStatus();
}