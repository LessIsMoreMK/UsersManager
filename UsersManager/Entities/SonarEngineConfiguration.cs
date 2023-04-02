namespace UsersManager.Entities;

public record SonarEngineConfiguration(bool Enabled, string Login, string Password, string App, string Address, string UsersRefreshCron);