using Discord.WebSocket;
using MessengerNicknameSyncer.Models;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace MessengerNicknameSyncer.Services;

public class AuthorizationService
{
	private readonly Dictionary<PermissionAction, PermissionConfig> _permissions;

	public AuthorizationService(IConfiguration configuration)
	{
		_permissions = new Dictionary<PermissionAction, PermissionConfig>
		{
			[PermissionAction.MappingManagement] = LoadPermissionConfig(
				configuration, "Discord:Authorization:MappingManagement"),
			[PermissionAction.ChannelRename] = LoadPermissionConfig(
				configuration, "Discord:Authorization:ChannelRename"),
			[PermissionAction.InfoCommands] = LoadPermissionConfig(
				configuration, "Discord:Authorization:InfoCommands")
		};

		LogPermissionSummary();
	}

	private void LogPermissionSummary()
	{
		Log.Information("Authorization configured:");
		foreach ((PermissionAction action, PermissionConfig config) in _permissions)
		{
			string status = config.AllowEveryone
				? "Everyone"
				: $"{config.AllowedRoleIds.Count} role(s), {config.AllowedUserIds.Count} user(s)";
			Log.Information("  {Action}: {Status}", action, status);
		}
	}

	private static PermissionConfig LoadPermissionConfig(
		IConfiguration configuration,
		string sectionPath)
	{
		IConfigurationSection section = configuration.GetSection(sectionPath);

		return new PermissionConfig
		{
			AllowedRoleIds = Utils.ParseUlongArray(section, "AllowedRoleIds"),
			AllowedUserIds = Utils.ParseUlongArray(section, "AllowedUserIds"),
			AllowEveryone = section.GetValue("AllowEveryone", false)
		};
	}
	
	private bool IsAuthorized(SocketGuildUser user, PermissionAction action)
	{
		if (!_permissions.TryGetValue(action, out PermissionConfig? config))
			return false;

		if (config.AllowEveryone)
			return true;

		// Check if user ID is explicitly allowed
		if (config.AllowedUserIds.Contains(user.Id))
			return true;

		// Check if user has any of the allowed roles
		return user.Roles.Any(role => config.AllowedRoleIds.Contains(role.Id));
	}

	public bool IsAuthorized(SocketMessage message, PermissionAction action)
	{
		if (!_permissions.TryGetValue(action, out PermissionConfig? config))
			return false;

		if (config.AllowEveryone)
			return true;

		// If message is from a guild (server), check roles
		if (message.Author is SocketGuildUser guildUser)
		{
			return IsAuthorized(guildUser, action);
		}

		// If it's a DM, only check user ID
		return config.AllowedUserIds.Contains(message.Author.Id);
	}
}