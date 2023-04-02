using UsersManager.Entities;

namespace UsersManager.Interfaces;

public interface ISonarEngineConfigurationService
{
    void LoadSonarEngineConfiguration();

    SonarEngineConfiguration GetSonarEngineConfiguration();
}