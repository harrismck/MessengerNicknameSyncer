using System.Text.RegularExpressions;
using Discord.WebSocket;
using MessengerNicknameSyncer.Models;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace MessengerNicknameSyncer.Services;

public partial class ChannelRenameService
{
	private readonly HashSet<ulong> _autoRenameChannelIds;
	private readonly bool _enabled;
	private readonly bool _requireAuthorization;
	private readonly AuthorizationService? _authService;

	private static readonly Regex RenamePattern =
        RenameRegex();

	public ChannelRenameService(
		IConfiguration configuration,
		AuthorizationService? authService = null)
	{
		_authService = authService;

		_enabled = configuration.GetValue("Discord:AutoRenameChannels:Enabled", false);
		_requireAuthorization = configuration.GetValue(
			"Discord:AutoRenameChannels:RequireAuthorization", false);

		IConfigurationSection section = configuration.GetSection("Discord:AutoRenameChannels");
		_autoRenameChannelIds = Utils.ParseUlongArray(
			section,
			"ChannelIds");

		if (_enabled)
		{
			Log.Information("Auto-rename enabled for {ChannelCount} channel(s)", _autoRenameChannelIds.Count);
			Log.Information("Authorization required: {RequireAuth}", _requireAuthorization);
		}
	}

	public bool IsEnabled => _enabled;

	public bool ShouldProcessChannel(ulong channelId)
	{
		return _enabled && _autoRenameChannelIds.Contains(channelId);
	}

	public (bool IsMatch, string? FirstName, string? NewName) ParseRenameMessage(string content)
	{
		Match match = RenamePattern.Match(content);
		if (!match.Success)
			return (false, null, null);

		string firstName = match.Groups[1].Value;
		string newName = match.Groups[2].Value;

		return (true, firstName, newName);
	}

	public bool IsAuthorizedToRename(SocketMessage message)
	{
		if (!_requireAuthorization)
			return true;

		if (_authService == null)
			return true;

		return _authService.IsAuthorized(message, PermissionAction.ChannelRename);
	}

	public async Task<bool> TryRenameChannelAsync(
		SocketTextChannel channel,
		string newName,
		string triggeredBy)
	{
		try
		{
			string sanitizedName = SanitizeChannelName(newName);

			if (channel.Name == sanitizedName)
			{
				Log.Information("Channel '{ChannelName}' already has the correct name, skipping", channel.Name);
				return false;
			}

			await channel.ModifyAsync(x => x.Name = sanitizedName);
			Log.Information("Renamed channel to '{SanitizedName}' (triggered by: {TriggeredBy})", sanitizedName, triggeredBy);
			return true;
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Error renaming channel");
			return false;
		}
	}

	private static string SanitizeChannelName(string name)
	{
		string sanitized = Regex.Replace(name, @"[^a-zA-Z0-9\-_]", "-");
		sanitized = Regex.Replace(sanitized, @"-+", "-");
		sanitized = sanitized.Trim('-');
		sanitized = sanitized.ToLowerInvariant();

		if (sanitized.Length > 100)
			sanitized = sanitized.Substring(0, 100).TrimEnd('-');

		if (string.IsNullOrWhiteSpace(sanitized))
			sanitized = "unnamed-chat";

		return sanitized;
	}

    [GeneratedRegex(@"^(.+?) named the group (.+?)\.$", RegexOptions.Compiled)]
    private static partial Regex RenameRegex();
}