namespace UsersManager.Entities;

public class Group
{
    public string Id { get; set; }
    public string Name { get; set; }
    public IEnumerable<Role> ClientRoles { get; set; } = null!;
    public IDictionary<string, IEnumerable<string>> Attributes { get; set; } = null!;

    public int UserCount { get; set; }

  
    public Group(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public Group(string id, string name, Role[] clientRoles, IDictionary<string, IEnumerable<string>> attributes)
    {
        Id = id;
        Name = name;
        ClientRoles = clientRoles;
        Attributes = attributes;
    }
}