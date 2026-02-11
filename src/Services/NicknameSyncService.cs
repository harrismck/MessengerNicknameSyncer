using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using MessengerNicknameSyncer.Models;

namespace MessengerNicknameSyncer.Services;

public class NicknameSyncService
{
	private readonly UserMappingService _mappingService;
	private readonly NicknameClearBehavior _nicknameClearBehavior;

	public NicknameSyncService(UserMappingService mappingService, NicknameClearBehavior clearBehavior)
	{
		_mappingService = mappingService;
		_nicknameClearBehavior = clearBehavior;
	}

	public async Task HandleNicknameSetAsync(SocketMessage message, Match match)
	{
		string targetName = NicknameMessageParser.ExtractTargetNameFromSetMatch(match);
		string newNickname = NicknameMessageParser.ExtractNewNicknameFromSetMatch(match);

		Console.WriteLine($"Nickname change detected: '{targetName}' -> '{newNickname}'");

		if (message.Channel is not SocketGuildChannel guildChannel)
			return;

		SocketGuild guild = guildChannel.Guild;
		ulong? discordUserId = _mappingService.GetDiscordUserId(targetName);

		if (discordUserId == null)
		{
			Console.WriteLine($"❌ No mapping found for Facebook name: '{targetName}'");
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

		await TryApplyNicknameAsync(member, newNickname, message);
	}

	public async Task HandleNicknameClearAsync(SocketMessage message, Match match)
	{
		// If configured to do nothing, just ignore clear messages
		if (_nicknameClearBehavior == NicknameClearBehavior.DoNothing)
		{
			Console.WriteLine("ℹ️ Nickname clear ignored (behavior set to DoNothing)");
			return;
		}

		string? targetName = NicknameMessageParser.ExtractTargetNameFromClearMatch(match);

		if (targetName == null)
		{
			Console.WriteLine("⚠️ Cannot determine target for 'You cleared your own nickname'");
			await message.AddReactionAsync(new Emoji("❓"));
			return;
		}

		Console.WriteLine($"Nickname clear detected for: '{targetName}'");

		if (message.Channel is not SocketGuildChannel guildChannel)
			return;

		SocketGuild? guild = guildChannel.Guild;
		ulong? discordUserId = _mappingService.GetDiscordUserId(targetName);

		if (discordUserId == null)
		{
			Console.WriteLine($"❌ No mapping found for Facebook name: '{targetName}'");
			await message.AddReactionAsync(new Emoji("❓"));
			return;
		}

		SocketGuildUser? member = guild.GetUser(discordUserId.Value);

		if (member == null)
		{
			Console.WriteLine($"❌ Discord user ID {discordUserId} not found in guild");
			await message.AddReactionAsync(new Emoji("❌"));
			return;
		}

		string? newNickname;
		if (_nicknameClearBehavior == NicknameClearBehavior.ClearCompletely)
		{
			newNickname = null;
		}
		else // NicknameClearBehavior.ResetToFirstName
		{
			// Get the preferred Facebook name for this user (handles "your" vs real name)
			string? preferredFacebookName = _mappingService.GetPreferredFacebookName(discordUserId.Value);
			newNickname = NicknameMessageParser.ExtractFirstName(preferredFacebookName ?? targetName);
		}

		await TryApplyNicknameAsync(member, newNickname, message);
	}

	private static async Task<bool> TryApplyNicknameAsync(
		SocketGuildUser member,
		string? newNickname,
		SocketMessage? triggerMessage = null)
	{
		try
		{
			// Truncate nickname if it exceeds Discord's 32 character limit
			string? truncatedNickname = newNickname;
			if (truncatedNickname != null && truncatedNickname.Length > 32)
			{
				truncatedNickname = truncatedNickname.Substring(0, 32);
				Console.WriteLine($"⚠️ Nickname '{newNickname}' truncated to '{truncatedNickname}' (Discord 32 char limit)");
			}

			await member.ModifyAsync(x => x.Nickname = truncatedNickname);

			string displayNickname = truncatedNickname ?? "(cleared)";
			Console.WriteLine($"✅ Changed {member.Username}'s nickname to '{displayNickname}'");

			if (triggerMessage != null)
				await triggerMessage.AddReactionAsync(new Emoji("✅"));

			return true;
		}
		catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.Forbidden)
		{
			// Check if this is the server owner
			if (member.Guild.OwnerId == member.Id)
			{
				Console.WriteLine($"ℹ️ Cannot change server owner's ({member.Username}) nickname - sending DM reminder");
				await SendOwnerNicknameReminderAsync(member, newNickname);
			}
			else
			{
				Console.WriteLine($"❌ No permission to change {member.Username}'s nickname");
				if (triggerMessage != null)
					await triggerMessage.AddReactionAsync(new Emoji("⛔"));
			}

			return false;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"❌ Error changing nickname: {ex.Message}");
			if (triggerMessage != null)
				await triggerMessage.AddReactionAsync(new Emoji("❌"));

			return false;
		}
	}

	public async Task<Dictionary<string, string?>> FindLatestNicknameChangesAsync(
		ISocketMessageChannel channel,
		int messageCount)
	{
		// Store by Discord user ID to avoid duplicates when multiple FB names map to same user
		Dictionary<ulong, (string FacebookName, string? Nickname, DateTimeOffset Timestamp)> nicknameChangesByUserId = [];
		int messagesProcessed = 0;

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
				string? targetName = null;
				string? newNickname = null;

				Match clearMatch = NicknameMessageParser.MatchClearPattern(msg.Content);
				if (clearMatch.Success)
				{
					if (_nicknameClearBehavior == NicknameClearBehavior.DoNothing)
						continue;

					targetName = NicknameMessageParser.ExtractTargetNameFromClearMatch(clearMatch);

					if (targetName != null)
					{
						ulong? tempDiscordUserId = _mappingService.GetDiscordUserId(targetName);
						if (tempDiscordUserId != null)
						{
							if (_nicknameClearBehavior == NicknameClearBehavior.ClearCompletely)
							{
								newNickname = null;
							}
							else
							{
								// Get preferred Facebook name for first name extraction
								string? preferredFacebookName = _mappingService.GetPreferredFacebookName(tempDiscordUserId.Value);
								newNickname = NicknameMessageParser.ExtractFirstName(preferredFacebookName ?? targetName);
							}
						}
					}
				}
				else
				{
					Match setMatch = NicknameMessageParser.MatchSetPattern(msg.Content);
					if (setMatch.Success)
					{
						targetName = NicknameMessageParser.ExtractTargetNameFromSetMatch(setMatch);
						newNickname = NicknameMessageParser.ExtractNewNicknameFromSetMatch(setMatch);
					}
				}

				// Resolve to Discord user ID to deduplicate
				if (targetName != null)
				{
					ulong? discordUserId = _mappingService.GetDiscordUserId(targetName);
	
					if (discordUserId != null)
					{
						// Keep the message with the latest (most recent) timestamp
						if (!nicknameChangesByUserId.ContainsKey(discordUserId.Value) ||
						    msg.Timestamp > nicknameChangesByUserId[discordUserId.Value].Timestamp)
						{
							nicknameChangesByUserId[discordUserId.Value] = (targetName, newNickname, msg.Timestamp);
							string displayNickname = newNickname ?? "(cleared)";
							Console.WriteLine($"Found: '{targetName}' -> '{displayNickname}' from {msg.Timestamp}");
						}
						else
						{
							// Log when we skip an older message for the same user
							string displayNickname = newNickname ?? "(cleared)";
							Console.WriteLine($"Skipping older: '{targetName}' -> '{displayNickname}' from {msg.Timestamp}");
						}
					}
					else
					{
						// Log when Discord ID lookup fails
						Console.WriteLine($"⚠️ Could not resolve '{targetName}' to Discord user");
					}
				}
			}

			lastMessage = messageList.Last();
			messagesProcessed += messageList.Count;

			if (messageList.Count < batchSize)
				break;
		}

		Console.WriteLine($"Processed {messagesProcessed} messages, found {nicknameChangesByUserId.Count} unique users");

		return nicknameChangesByUserId.ToDictionary(
			kvp => kvp.Value.FacebookName,
			kvp => kvp.Value.Nickname);
	}

	public async Task<ResyncResults> ApplyNicknameChangesAsync(
		SocketGuild guild,
		Dictionary<string, string?> nicknameChanges)
	{
		var results = new ResyncResults();

		foreach ((string facebookName, string? newNickname) in nicknameChanges)
		{
			ulong? discordUserId = _mappingService.GetDiscordUserId(facebookName);

			if (discordUserId == null)
			{
				Console.WriteLine($"⚠️ '{facebookName}' not in mappings, skipping");
				results.NotMapped++;
				continue;
			}

			SocketGuildUser? member = guild.GetUser(discordUserId.Value);

			if (member == null)
			{
				Console.WriteLine($"⚠️ User ID {discordUserId} not found in guild");
				results.NotMapped++;
				continue;
			}

			bool isFirstNameReset = newNickname != null &&
			                        newNickname == NicknameMessageParser.ExtractFirstName(facebookName);

			bool success = await TryApplyNicknameAsync(member, newNickname);

			if (success)
			{
				results.Success++;
				if (isFirstNameReset)
					results.ResetToFirstName++;
			}
			else
			{
				results.Errors++;
			}

			await Task.Delay(100);
		}

		return results;
	}

	private static async Task SendOwnerNicknameReminderAsync(SocketGuildUser owner, string? newNickname)
	{
		try
		{
			IDMChannel? dmChannel = await owner.CreateDMChannelAsync();

			string reminderMessage;
			if (newNickname == null)
			{
				reminderMessage = "**Nickname Sync Reminder**\n\n" +
				                  "Your nickname was cleared in the Facebook group chat. " +
				                  "As the server owner, I cannot change your Discord nickname automatically.\n\n" +
				                  "Please manually clear your nickname in the server if desired.";
			}
			else
			{
				reminderMessage = $"**Nickname Sync Reminder**\n\n" +
				                  $"Your nickname was changed to **{newNickname}** in the Facebook group chat. " +
				                  $"As the server owner, I cannot change your Discord nickname automatically.\n\n" +
				                  $"Please manually update your nickname to: `{newNickname}`";
			}

			await dmChannel.SendMessageAsync(reminderMessage);
			Console.WriteLine($"✅ Sent DM reminder to server owner");
		}
		catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.Forbidden)
		{
			Console.WriteLine($"⚠️ Cannot DM server owner - they may have DMs disabled");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"⚠️ Error sending DM to server owner: {ex.Message}");
		}
	}
}