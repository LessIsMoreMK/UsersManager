namespace UsersManager.Entities;

public record User(string Id, string Username, string Email, string FirstName, string LastName,
    bool? Enabled, string Password, Role[] ClientRoles, Group[] Groups);