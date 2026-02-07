using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

public class ChannelRenameService
{
	private readonly HashSet<ulong> _autoRenameChannelIds;
	private readonly bool _enabled;
	private readonly bool _requireAuthorization;
	private readonly AuthorizationService? _authService;

	private static readonly Regex _renamePattern =
		new(@"^(.+?) named the group (.+?)\.$", RegexOptions.Compiled);

	public ChannelRenameService(
		IConfiguration configuration,
		AuthorizationService? authService = null)
	{
		_authService = authService;

		_enabled = configuration.GetValue<bool>("Discord:AutoRenameChannels:Enabled", false);
		_requireAuthorization = configuration.GetValue<bool>(
			"Discord:AutoRenameChannels:RequireAuthorization", false);


		IConfigurationSection section = configuration.GetSection("Discord:AutoRenameChannels");
		_autoRenameChannelIds = Utils.ParseUlongArray(
			section,
			"ChannelIds");

		if (_enabled)
		{
			Console.WriteLine($"Auto-rename enabled for {_autoRenameChannelIds.Count} channel(s)");
			Console.WriteLine($"Authorization required: {_requireAuthorization}");
		}
	}

	public bool IsEnabled => _enabled;

	public bool ShouldProcessChannel(ulong channelId)
	{
		return _enabled && _autoRenameChannelIds.Contains(channelId);
	}

	public static (bool IsMatch, string? FirstName, string? NewName) ParseRenameMessage(string content)
	{
		Match match = _renamePattern.Match(content);
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

	public static async Task<bool> TryRenameChannelAsync(
		SocketTextChannel channel,
		string newName,
		string triggeredBy)
	{
		try
		{
			string sanitizedName = SanitizeChannelName(newName);

			if (channel.Name == sanitizedName)
			{
				Console.WriteLine($"Channel '{channel.Name}' already has the correct name, skipping");
				return false;
			}

			await channel.ModifyAsync(x => x.Name = sanitizedName);
			Console.WriteLine($"✅ Renamed channel to '{sanitizedName}' (triggered by: {triggeredBy})");
			return true;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"❌ Error renaming channel: {ex.Message}");
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
}