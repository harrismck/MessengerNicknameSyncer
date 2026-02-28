using Discord;
using Discord.WebSocket;
using MessengerNicknameSyncer.Models;
using Serilog;

namespace MessengerNicknameSyncer.Services;

public class CommandHandler
{
	private const string ResyncCommand = "!resync";
	private const string ReloadCommand = "!reloadmappings";
	private const string HelpCommand = "!nicknamehelp";
	private const string MapCommand = "!map";
	private const string UnmapCommand = "!unmap";
	private const string ListMapsCommand = "!listmaps";

	private UserMappingService _mappingService;
	private readonly AuthorizationService _authService;
	private readonly NicknameSyncService _nicknameSyncService;
	private readonly ulong _nicknameSyncChannelId;
	private readonly int _resyncMessageCount;

	public CommandHandler(
		UserMappingService mappingService,
		AuthorizationService authService,
		NicknameSyncService nicknameSyncService,
		ulong nicknameSyncChannelId,
		int resyncMessageCount)
	{
		_mappingService = mappingService;
		_authService = authService;
		_nicknameSyncService = nicknameSyncService;
		_nicknameSyncChannelId = nicknameSyncChannelId;
		_resyncMessageCount = resyncMessageCount;
	}

	public async Task HandleCommandAsync(SocketMessage message)
	{
		string[] parts = message.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		string command = parts[0].ToLower();

		// Determine required permission level for the command
		PermissionAction? requiredPermission = command switch
		{
			MapCommand or UnmapCommand or ReloadCommand or ResyncCommand => PermissionAction.MappingManagement,
			HelpCommand or ListMapsCommand => PermissionAction.InfoCommands,
			_ => null
		};

		// If this is a known command, check authorization
		if (requiredPermission.HasValue)
		{
			if (!_authService.IsAuthorized(message, requiredPermission.Value))
			{
				Log.Warning("Unauthorized {Permission} command attempt by {Username}", requiredPermission.Value, message.Author.Username);
				await message.AddReactionAsync(new Emoji("🔒"));
				return;
			}
		}

		switch (command)
		{
			case ReloadCommand:
				await HandleReloadCommand(message);
				break;
			case HelpCommand:
				await HandleHelpCommand(message);
				break;
			case MapCommand:
				await HandleMapCommand(message, parts);
				break;
			case UnmapCommand:
				await HandleUnmapCommand(message, parts);
				break;
			case ListMapsCommand:
				await HandleListMapsCommand(message);
				break;
			case ResyncCommand:
				await HandleResyncCommand(message, parts);
				break;
			default:
				// Unknown command, ignore silently
				break;
		}
	}

	private async Task HandleReloadCommand(SocketMessage message)
	{
		try
		{
			var newMappingService = new UserMappingService("user_mappings.json");
			_mappingService = newMappingService;
			await message.Channel.SendMessageAsync("✅ Mappings reloaded successfully!");
			Log.Information("Mappings reloaded by {Username}", message.Author.Username);
		}
		catch (Exception ex)
		{
			await message.Channel.SendMessageAsync($"❌ Error reloading mappings: {ex.Message}");
			Log.Error(ex, "Error reloading mappings");
		}
	}

	private async Task HandleHelpCommand(SocketMessage message)
	{
		string helpText = $@"**Nickname Sync Bot Commands**

**Mapping Management:** *(Requires MappingManagement permission)*
`{MapCommand} <user_id> <facebook_name>` - Add/update a mapping
  Example: `{MapCommand} 123456789012345678 John Smith`
`{UnmapCommand} <facebook_name>` - Remove a mapping
  Example: `{UnmapCommand} John Smith`
`{ReloadCommand}` - Reload mappings from file
`{ResyncCommand} [count] [-reset]` - Re-sync all nicknames from message history
  Example: `{ResyncCommand} 1000` (default: {_resyncMessageCount})
  The -reset flag will rename mapped (but not recently renamed) discord users to their first name from FB
  Note: Can only be used in the nickname sync channel

**Info Commands:**
`{ListMapsCommand}` - Show all current mappings
`{HelpCommand}` - Show this help message

**Automatic Features:**
- Syncs nicknames from Facebook Messenger messages
  Format: `<User> set the nickname for <Name> to <Nickname>.`
- Auto-renames configured channels when Facebook group is renamed
  Format: `<First Name> named the group <New Name>.`
  *(Requires ChannelRename permission if RequireAuthorization is true)*";

		await message.Channel.SendMessageAsync(helpText);
	}

	private async Task HandleMapCommand(SocketMessage message, string[] parts)
	{
		// Usage: !map <user_id> <facebook_name>
		// Example: !map 123456789012345678 John Smith
		if (parts.Length < 3)
		{
			await message.Channel.SendMessageAsync(
				"❌ Usage: `!map <discord_user_id> <facebook_name>`\n" +
				"Example: `!map 123456789012345678 John Smith`");
			return;
		}

		string userIdString = parts[1];
		string facebookName = string.Join(' ', parts.Skip(2));

		if (!ulong.TryParse(userIdString, out ulong discordUserId))
		{
			await message.Channel.SendMessageAsync("❌ Invalid Discord user ID. Must be a numeric ID.");
			return;
		}

		// Optional: Verify the user exists in the guild
		if (message.Channel is SocketGuildChannel guildChannel)
		{
			SocketGuild guild = guildChannel.Guild;
			SocketGuildUser targetUser = guild.GetUser(discordUserId);

			if (targetUser == null)
			{
				await message.Channel.SendMessageAsync(
					$"⚠️ Warning: User ID {discordUserId} not found in this server. Mapping will still be created.");
			}
			else
			{
				_mappingService.AddOrUpdateMapping(facebookName, discordUserId);
				await message.Channel.SendMessageAsync(
					$"✅ Mapped `{facebookName}` → {targetUser.Mention} ({targetUser.Username})");
				return;
			}
		}

		_mappingService.AddOrUpdateMapping(facebookName, discordUserId);
		await message.Channel.SendMessageAsync($"✅ Mapped `{facebookName}` → User ID: {discordUserId}");
	}

	private async Task HandleUnmapCommand(SocketMessage message, string[] parts)
	{
		// Usage: !unmap <facebook_name>
		// Example: !unmap John Smith
		if (parts.Length < 2)
		{
			await message.Channel.SendMessageAsync(
				"❌ Usage: `!unmap <facebook_name>`\n" +
				"Example: `!unmap John Smith`");
			return;
		}

		string facebookName = string.Join(' ', parts.Skip(1));

		if (_mappingService.RemoveMapping(facebookName))
		{
			await message.Channel.SendMessageAsync($"✅ Removed mapping for `{facebookName}`");
		}
		else
		{
			await message.Channel.SendMessageAsync($"❌ No mapping found for `{facebookName}`");
		}
	}

	private async Task HandleListMapsCommand(SocketMessage message)
	{
		Dictionary<string, ulong> mappings = _mappingService.GetAllMappings();

		if (mappings.Count == 0)
		{
			await message.Channel.SendMessageAsync("No mappings configured.");
			return;
		}

		List<string> lines = ["**Current Mappings:**", ""];

		// Get guild to resolve usernames if possible
		SocketGuild? guild = null;
		if (message.Channel is SocketGuildChannel guildChannel)
		{
			guild = guildChannel.Guild;
		}

		foreach (KeyValuePair<string, ulong> mapping in mappings.OrderBy(m => m.Key))
		{
			string discordInfo = guild?.GetUser(mapping.Value)?.Username ?? $"ID: {mapping.Value}";
			lines.Add($"• `{mapping.Key}` → {discordInfo}");
		}

		string response = string.Join('\n', lines);

		// Discord has a 2000 character limit, split if needed
		if (response.Length > 2000)
		{
			IEnumerable<string> chunks = SplitMessage(response, 2000);
			foreach (string chunk in chunks)
			{
				await message.Channel.SendMessageAsync(chunk);
			}
		}
		else
		{
			await message.Channel.SendMessageAsync(response);
		}
	}

	private async Task HandleResyncCommand(SocketMessage message, string[] parts)
	{
		// Usage: !resync [message_count] [-reset]
		// Example: !resync 1000 -reset
		if (message.Channel.Id != _nicknameSyncChannelId)
		{
			await message.Channel.SendMessageAsync(
				"❌ This command can only be used in the nickname sync channel.");
			return;
		}

		// Parse arguments
		int messageCount = _resyncMessageCount;
		bool resetToFirstName = false;

		foreach (string? part in parts.Skip(1))
		{
			if (part.Equals("-reset", StringComparison.OrdinalIgnoreCase))
			{
				resetToFirstName = true;
			}
			else if (int.TryParse(part, out int customCount))
			{
				messageCount = Math.Min(customCount, 10000);
			}
		}

		string resetInfo = resetToFirstName
			? " (will reset mapped users with no recent nickname to first name)"
			: "";

		await message.Channel.SendMessageAsync(
			$"🔄 Searching last {messageCount} messages for nickname changes{resetInfo}...");

		try
		{
			Dictionary<string, string?> nicknameChanges = 
				await _nicknameSyncService.FindLatestNicknameChangesAsync(message.Channel, messageCount);

			// If -reset flag is set, add first names for unmapped users
			if (resetToFirstName)
			{
				Dictionary<string, ulong> allMappings = _mappingService.GetAllMappings();
				var allDiscordUserIds = allMappings.Values.Distinct();
	
				// Get Discord user IDs that already have recent nickname changes
				var userIdsWithChanges = nicknameChanges.Keys
					.Select(fbName => _mappingService.GetDiscordUserId(fbName))
					.Where(id => id.HasValue)
					.Select(id => id.Value)
					.ToHashSet();

				foreach (ulong discordUserId in allDiscordUserIds)
				{
					if (userIdsWithChanges.Contains(discordUserId))
						continue;
					// Get the preferred Facebook name for this user
					string? preferredFacebookName = _mappingService.GetPreferredFacebookName(discordUserId);
					if (preferredFacebookName == null)
						continue;
					
					string firstName = NicknameMessageParser.ExtractFirstName(preferredFacebookName);
					nicknameChanges[preferredFacebookName] = firstName;
					Log.Information("No recent change for Discord user {DiscordUserId}, will reset to: {FirstName}", discordUserId, firstName);
				}
			}

			if (nicknameChanges.Count == 0)
			{
				await message.Channel.SendMessageAsync("❌ No nickname changes to apply.");
				return;
			}

			await message.Channel.SendMessageAsync(
				$"Found nicknames for {nicknameChanges.Count} user(s). Applying...");

			if (message.Channel is not SocketGuildChannel guildChannel)
				return;

			ResyncResults results = await _nicknameSyncService.ApplyNicknameChangesAsync(
				guildChannel.Guild, 
				nicknameChanges);

			string resultMessage = $"✅ Resync complete!\n" +
			                       $"• Successfully updated: {results.Success}\n" +
			                       $"• Not mapped: {results.NotMapped}\n" +
			                       $"• Errors: {results.Errors}";

			if (resetToFirstName)
			{
				resultMessage += $"\n• Reset to first name: {results.ResetToFirstName}";
			}

			await message.Channel.SendMessageAsync(resultMessage);
		}
		catch (Exception ex)
		{
			await message.Channel.SendMessageAsync($"❌ Error during resync: {ex.Message}");
			Log.Error(ex, "Error during resync");
		}
	}

	private static IEnumerable<string> SplitMessage(string message, int maxLength)
	{
		string[] lines = message.Split('\n');
		List<string> currentChunk = [];
		int currentLength = 0;

		foreach (string line in lines)
		{
			if (currentLength + line.Length + 1 > maxLength && currentChunk.Count != 0)
			{
				yield return string.Join('\n', currentChunk);
				currentChunk.Clear();
				currentLength = 0;
			}

			currentChunk.Add(line);
			currentLength += line.Length + 1;
		}

		if (currentChunk.Count != 0)
		{
			yield return string.Join('\n', currentChunk);
		}
	}
}