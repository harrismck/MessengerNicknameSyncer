namespace MessengerNicknameSyncer.Models;

public class PermissionConfig
{
    public HashSet<ulong> AllowedRoleIds { get; set; } = new();
    public HashSet<ulong> AllowedUserIds { get; set; } = new();
    public bool AllowEveryone { get; set; }
}