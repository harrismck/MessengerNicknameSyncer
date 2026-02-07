using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

internal class Program
{
	private const string ResyncCommand = "!resync";
	private const string ReloadCommand = "!reloadMappings";
	private const string HelpCommand = "!nicknameHelp";
	private const string MapCommand = "!map";
	private const string UnmapCommand = "!unmap";
	private const string ListMapsCommand = "!listMaps";
	private const int DefaultResyncMessageCount = 2000;

	private DiscordSocketClient? _client;
	private UserMappingService _mappingService;
	private readonly AuthorizationService _authService;
	private ulong _nicknameSyncChannelId;
	private readonly IConfigurationRoot _configuration;
	private int _resyncMessageCount;

	private static readonly Regex NicknamePattern =
		new Regex(@"^.+ set the nickname for (.+?) to (.+?)\.$", RegexOptions.Compiled);

	private static async Task Main(string[] args)
	{
		Program program = new();
		await program.RunBotAsync();
	}

	private Program()
	{
		_configuration = new ConfigurationBuilder()
			.SetBasePath(Directory.GetCurrentDirectory())
			.AddJsonFile("appsettings.json", optional: false)
			.AddEnvironmentVariables()
			.Build();

		_mappingService = new("user_mappings.json");
		_authService = new AuthorizationService(_configuration);
	}

	public async Task RunBotAsync()
	{
		string token = _configuration["Discord:BotToken"]
					?? throw new Exception("Bot token not configured");
		string channelIdString = _configuration["Discord:NicknameSyncChannelId"]
							  ?? throw new Exception("Nickname sync channel ID not configured");

		if (!int.TryParse(_configuration["Discord:ResyncMessageCount"], out _resyncMessageCount))
			_resyncMessageCount = DefaultResyncMessageCount;

		if (!ulong.TryParse(channelIdString, out _nicknameSyncChannelId))
		{
			throw new Exception("Invalid channel ID format");
		}

		DiscordSocketConfig config = new DiscordSocketConfig
		{
			GatewayIntents = GatewayIntents.Guilds |
						   GatewayIntents.GuildMessages |
						   GatewayIntents.MessageContent |
						   GatewayIntents.GuildMembers,
			AlwaysDownloadUsers = true  // Download all users on startup
		};

		_client = new DiscordSocketClient(config);

		_client.Log += LogAsync;
		_client.Ready += ReadyAsync;
		_client.MessageReceived += MessageReceivedAsync;

		await _client.LoginAsync(TokenType.Bot, token);
		await _client.StartAsync();

		await Task.Delay(-1);
	}

	private Task LogAsync(LogMessage log)
	{
		Console.WriteLine(log.ToString());
		return Task.CompletedTask;
	}

	private async Task ReadyAsync()
	{
		if (_client == null)
		{
			Console.WriteLine("Failed to init client");
			return;
		}

		Console.WriteLine($"Connected as {_client.CurrentUser}");
		Console.WriteLine($"Monitoring channel ID: {_nicknameSyncChannelId}");

		// Download all members for all guilds
		foreach (SocketGuild? guild in _client.Guilds)
		{
			Console.WriteLine($"Downloading members for guild: {guild.Name}");
			await guild.DownloadUsersAsync();
			Console.WriteLine($"  Total members: {guild.Users.Count}");
		}

		Console.WriteLine("Bot is ready!");
	}

	private async Task MessageReceivedAsync(SocketMessage message)
	{
		if (message.Author.IsBot)
			return;

		// Handle commands (with authorization)
		if (message.Content.StartsWith("!"))
		{
			await HandleCommandAsync(message);
			return;
		}

		// Handle nickname sync (only in the specified channel)
		if (message.Channel.Id == _nicknameSyncChannelId)
		{
			await HandleNicknameSyncAsync(message);
		}
	}

	private async Task HandleNicknameSyncAsync(SocketMessage message)
	{
		if (message.Author.IsBot)
			return;

		Match match = NicknamePattern.Match(message.Content);
		if (!match.Success)
			return;

		string facebookName = match.Groups[1].Value;
		string newNickname = match.Groups[2].Value;

		Console.WriteLine($"Nickname change detected: '{facebookName}' -> '{newNickname}'");

		if (message.Channel is not SocketGuildChannel guildChannel)
			return;

		SocketGuild guild = guildChannel.Guild;

		// Try to get the Discord user from the mapping
		ulong? discordUserId = _mappingService.GetDiscordUserId(facebookName);

		if (discordUserId == null)
		{
			Console.WriteLine($"❌ No mapping found for Facebook name: '{facebookName}'");
			Console.WriteLine($"   Please add a mapping in user_mappings.json");
			await message.AddReactionAsync(new Emoji("❓"));
			return;
		}

		SocketGuildUser member = guild.GetUser(discordUserId.Value);

		if (member == null)
		{
			Console.WriteLine($"❌ Discord user ID {discordUserId} not found in guild");
			await message.AddReactionAsync(new Emoji("❌"));
			return;
		}

		try
		{
			await member.ModifyAsync(x => x.Nickname = newNickname);
			Console.WriteLine($"✅ Changed {member.Username}'s nickname to '{newNickname}'");
			await message.AddReactionAsync(new Emoji("✅"));
		}
		catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.Forbidden)
		{
			Console.WriteLine($"❌ No permission to change {member.Username}'s nickname");
			await message.AddReactionAsync(new Emoji("⛔"));
		}
		catch (Exception ex)
		{
			Console.WriteLine($"❌ Error changing nickname: {ex.Message}");
			await message.AddReactionAsync(new Emoji("❌"));
		}
	}

	private async Task HandleCommandAsync(SocketMessage message)
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
				Console.WriteLine($"Unauthorized {requiredPermission.Value} command attempt by {message.Author.Username}");
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
			_mappingService = new UserMappingService("user_mappings.json");
			await message.Channel.SendMessageAsync("✅ Mappings reloaded successfully!");
			Console.WriteLine($"Mappings reloaded by {message.Author.Username}");
		}
		catch (Exception ex)
		{
			await message.Channel.SendMessageAsync($"❌ Error reloading mappings: {ex.Message}");
			Console.WriteLine($"Error reloading mappings: {ex.Message}");
		}
	}

	private async Task HandleHelpCommand(SocketMessage message)
	{
		string helpText = @$"**Nickname Sync Bot Commands**

**Mapping Management:** *(Requires MappingManagement permission)*
`{MapCommand} <user_id> <facebook_name>` - Add/update a mapping
  Example: `{MapCommand} 123456789012345678 John Smith`
`{UnmapCommand} <facebook_name>` - Remove a mapping
  Example: `{UnmapCommand} John Smith`
`{ReloadCommand}` - Reload mappings from file
`{ResyncCommand} [count]  [-reset]` - Re-sync all nicknames from message history
  Example: `{ResyncCommand} 1000` (default: 1000)
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

		List<string> lines = new List<string> { "**Current Mappings:**", "" };

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

	private static IEnumerable<string> SplitMessage(string message, int maxLength)
	{
		string[] lines = message.Split('\n');
		List<string> currentChunk = new List<string>();
		int currentLength = 0;

		foreach (string line in lines)
		{
			if (currentLength + line.Length + 1 > maxLength && currentChunk.Any())
			{
				yield return string.Join('\n', currentChunk);
				currentChunk.Clear();
				currentLength = 0;
			}

			currentChunk.Add(line);
			currentLength += line.Length + 1;
		}

		if (currentChunk.Any())
		{
			yield return string.Join('\n', currentChunk);
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
			Dictionary<string, string> nicknameChanges = await FindLatestNicknameChangesAsync(message.Channel, messageCount);

			// If -reset flag is set, add first names for unmapped users
			if (resetToFirstName)
			{
				Dictionary<string, ulong> allMappings = _mappingService.GetAllMappings();
				List<string> unmappedUsers = allMappings.Keys.Except(nicknameChanges.Keys).ToList();

				foreach (string? facebookName in unmappedUsers)
				{
					string firstName = ExtractFirstName(facebookName);
					nicknameChanges[facebookName] = firstName;
					Console.WriteLine($"No recent change for '{facebookName}', will reset to: {firstName}");
				}
			}

			if (nicknameChanges.Count == 0)
			{
				await message.Channel.SendMessageAsync(
					"❌ No nickname changes to apply.");
				return;
			}

			await message.Channel.SendMessageAsync(
				$"Found nicknames for {nicknameChanges.Count} user(s). Applying...");

			ResyncResults results = await ApplyNicknameChangesAsync(message, nicknameChanges);

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
			Console.WriteLine($"Error during resync: {ex}");
		}
	}

	private static string ExtractFirstName(string facebookName)
	{
		// Extract the first word as the first name
		string[] parts = facebookName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		return parts.Length > 0 ? parts[0] : facebookName;
	}

	private static async Task<Dictionary<string, string>> FindLatestNicknameChangesAsync(
		ISocketMessageChannel channel,
		int messageCount)
	{
		Dictionary<string, (string Nickname, DateTimeOffset Timestamp)> nicknameChanges = [];
		int messagesProcessed = 0;

		// Fetch messages in batches of 100 (Discord's limit)
		IMessage? lastMessage = null;

		while (messagesProcessed < messageCount)
		{
			int batchSize = Math.Min(100, messageCount - messagesProcessed);

			IEnumerable<IMessage> messages;
			if (lastMessage == null)
			{
				messages = await channel.GetMessagesAsync(batchSize).FlattenAsync();
			}
			else
			{
				messages = await channel.GetMessagesAsync(lastMessage, Direction.Before, batchSize)
					.FlattenAsync();
			}

			List<IMessage> messageList = [.. messages];
			if (messageList.Count == 0)
				break;

			foreach (IMessage? msg in messageList)
			{
				Match match = NicknamePattern.Match(msg.Content);
				if (match.Success)
				{
					string facebookName = match.Groups[1].Value;
					string newNickname = match.Groups[2].Value;

					// Only keep the most recent (first encountered) nickname for each user
					if (!nicknameChanges.ContainsKey(facebookName))
					{
						nicknameChanges[facebookName] = (newNickname, msg.Timestamp);
						Console.WriteLine($"Found: '{facebookName}' -> '{newNickname}' from {msg.Timestamp}");
					}
				}
			}

			lastMessage = messageList.Last();
			messagesProcessed += messageList.Count;

			// If we got fewer messages than requested, we've hit the end of history
			if (messageList.Count < batchSize)
				break;
		}

		Console.WriteLine($"Processed {messagesProcessed} messages, found {nicknameChanges.Count} unique users");

		// Return just the nicknames (most recent ones)
		return nicknameChanges.ToDictionary(
			kvp => kvp.Key,
			kvp => kvp.Value.Nickname);
	}

	private async Task<ResyncResults> ApplyNicknameChangesAsync(
		SocketMessage triggerMessage,
		Dictionary<string, string> nicknameChanges)
	{
		ResyncResults results = new();

		if (triggerMessage.Channel is not SocketGuildChannel guildChannel)
			return results;

		SocketGuild guild = guildChannel.Guild;

		foreach (KeyValuePair<string, string> change in nicknameChanges)
		{
			string facebookName = change.Key;
			string newNickname = change.Value;

			ulong? discordUserId = _mappingService.GetDiscordUserId(facebookName);

			if (discordUserId == null)
			{
				Console.WriteLine($"⚠️ '{facebookName}' not in mappings, skipping");
				results.NotMapped++;
				continue;
			}

			SocketGuildUser member = guild.GetUser(discordUserId.Value);

			if (member == null)
			{
				Console.WriteLine($"⚠️ User ID {discordUserId} not found in guild");
				results.Errors++;
				continue;
			}

			try
			{
				// Check if this is a first name reset (nickname matches first name from facebook name)
				bool isFirstNameReset = newNickname == ExtractFirstName(facebookName);

				await member.ModifyAsync(x => x.Nickname = newNickname);
				Console.WriteLine($"✅ Synced {member.Username} -> '{newNickname}'");
				results.Success++;

				if (isFirstNameReset)
				{
					results.ResetToFirstName++;
				}

				// Small delay to avoid rate limiting
				await Task.Delay(100);
			}
			catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.Forbidden)
			{
				Console.WriteLine($"❌ No permission to change {member.Username}'s nickname");
				results.Errors++;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ Error changing {member.Username}'s nickname: {ex.Message}");
				results.Errors++;
			}
		}

		return results;
	}
}