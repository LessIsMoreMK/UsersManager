using UsersManager.Enums;

namespace UsersManager.Commands;

public record GetSynchronizeStatusResponse(SynchronizationStatus Status, DateTime? LastSuccessfulDate, List<string> FailedUsers);