using Discord;
using Discord.WebSocket;
using MessengerNicknameSyncer.Models;
using MessengerNicknameSyncer.Services;
using Microsoft.Extensions.Configuration;

namespace MessengerNicknameSyncer;

internal class Program
{
	private const int DefaultResyncMessageCount = 2000;

	private DiscordSocketClient? _client;
	private readonly NicknameSyncService _nicknameSyncService;
	private readonly ChannelRenameService _renameService;
	private readonly CommandHandler _commandHandler;
	private readonly ulong _nicknameSyncChannelId;

	private static async Task Main(string[] _)
	{
		Program program = new();
		await program.RunBotAsync();
	}

	private Program()
	{
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.SetBasePath(Directory.GetCurrentDirectory())
			.AddJsonFile("appsettings.json", optional: false)
			.AddEnvironmentVariables()
			.Build();

		// Load configuration
		string channelIdString = configuration["Discord:NicknameSyncChannelId"]
		                         ?? throw new InvalidOperationException("Nickname sync channel ID not configured");

		if (!ulong.TryParse(channelIdString, out _nicknameSyncChannelId))
		{
			throw new InvalidOperationException("Invalid channel ID format");
		}

		if (!int.TryParse(configuration["Discord:ResyncMessageCount"], out int resyncMessageCount))
			resyncMessageCount = DefaultResyncMessageCount;

		string clearBehaviorString = configuration.GetValue<string>(
			"Discord:NicknameClearBehavior",
			"ResetToFirstName");

		if (!Enum.TryParse(clearBehaviorString, true, out NicknameClearBehavior nicknameClearBehavior))
		{
			Console.WriteLine($"Invalid NicknameClearBehavior '{clearBehaviorString}', defaulting to ResetToFirstName");
			nicknameClearBehavior = NicknameClearBehavior.ResetToFirstName;
		}

		// Initialize services
		var mappingService = new UserMappingService("user_mappings.json");
		var authService = new AuthorizationService(configuration);

		_nicknameSyncService = new NicknameSyncService(mappingService, nicknameClearBehavior);
		_renameService = new ChannelRenameService(configuration, authService);
		_commandHandler = new CommandHandler(
			mappingService,
			authService,
			_nicknameSyncService,
			_nicknameSyncChannelId,
			resyncMessageCount);
	}

	private async Task RunBotAsync()
	{
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.SetBasePath(Directory.GetCurrentDirectory())
			.AddJsonFile("appsettings.json", optional: false)
			.Build();

		string token = configuration["Discord:BotToken"]
		               ?? throw new InvalidOperationException("Bot token not configured");

		DiscordSocketConfig config = new()
		{
			GatewayIntents = GatewayIntents.Guilds |
			                 GatewayIntents.GuildMessages |
			                 GatewayIntents.MessageContent |
			                 GatewayIntents.GuildMembers,
			AlwaysDownloadUsers = true
		};

		_client = new DiscordSocketClient(config);

		_client.Log += LogAsync;
		_client.Ready += ReadyAsync;
		_client.MessageReceived += MessageReceivedAsync;

		await _client.LoginAsync(TokenType.Bot, token);
		await _client.StartAsync();

		await Task.Delay(-1);
	}

	private static Task LogAsync(LogMessage log)
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
		// Handle commands (with authorization)
		if (message.Content.StartsWith('!'))
		{
			await _commandHandler.HandleCommandAsync(message);
			return;
		}

		// Handle channel auto-rename (if enabled and in configured channel)
		if (_renameService.IsEnabled &&
		    _renameService.ShouldProcessChannel(message.Channel.Id))
		{
			await HandleChannelRenameAsync(message);
		}

		// Handle nickname sync (only in the specified channel)
		if (message.Channel.Id == _nicknameSyncChannelId)
		{
			await HandleNicknameSyncAsync(message);
		}
	}

	private async Task HandleChannelRenameAsync(SocketMessage message)
	{
		(bool isMatch, string? firstName, string? newName) = _renameService.ParseRenameMessage(message.Content);

		if (!isMatch || string.IsNullOrEmpty(newName))
			return;

		Console.WriteLine($"Channel rename detected: '{firstName}' renamed to '{newName}'");

		if (!_renameService.IsAuthorizedToRename(message))
		{
			Console.WriteLine($"Unauthorized rename attempt by {message.Author.Username}");
			await message.AddReactionAsync(new Emoji("🔒"));
			return;
		}

		if (message.Channel is not SocketTextChannel textChannel)
		{
			Console.WriteLine("Cannot rename - not a text channel");
			return;
		}

		bool success = await _renameService.TryRenameChannelAsync(
			textChannel,
			newName,
			$"{firstName} via Facebook");

		if (success)
		{
			await message.AddReactionAsync(new Emoji("✅"));
		}
		else
		{
			await message.AddReactionAsync(new Emoji("❌"));
		}
	}

	private async Task HandleNicknameSyncAsync(SocketMessage message)
	{
		// Check for nickname clear first
		var clearMatch = NicknameMessageParser.MatchClearPattern(message.Content);
		if (clearMatch.Success)
		{
			await _nicknameSyncService.HandleNicknameClearAsync(message, clearMatch);
			return;
		}

		// Check for nickname set
		var setMatch = NicknameMessageParser.MatchSetPattern(message.Content);
		if (setMatch.Success)
		{
			await _nicknameSyncService.HandleNicknameSetAsync(message, setMatch);
		}
	}
}