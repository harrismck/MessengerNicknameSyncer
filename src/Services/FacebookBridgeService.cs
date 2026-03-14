using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace MessengerNicknameSyncer.Services;

/// <summary>
/// Resolves Discord message authors who share a bridge-bot user ID (unsynced Facebook users)
/// to individual identities, using the existing UserMappingService.
/// </summary>
public class FacebookBridgeService
{
	private readonly ulong? _bridgeBotId;
	private readonly UserMappingService _mappingService;

	public bool IsEnabled => _bridgeBotId.HasValue;

	public FacebookBridgeService(ulong? bridgeBotId, UserMappingService mappingService)
	{
		_bridgeBotId = bridgeBotId;
		_mappingService = mappingService;
	}

	/// <summary>
	/// Returns true when the given user ID is the shared bridge-bot account.
	/// </summary>
	public bool IsBridgeUser(ulong userId) =>
		_bridgeBotId.HasValue && userId == _bridgeBotId.Value;

	/// <summary>
	/// Given the Discord display name of an unsynced bridge user (e.g. "John DoeAPP"),
	/// returns the resolved (userId, facebookName) pair.
	/// Falls back to a stable synthetic user ID when the name is not in the mapping.
	/// </summary>
	public (ulong UserId, string DisplayName) Resolve(string discordDisplayName)
	{
		string stripped = StripAppSuffix(discordDisplayName);

		ulong? mappedId = _mappingService.GetDiscordUserId(discordDisplayName)
		                  ?? _mappingService.GetDiscordUserId(stripped);

		if (mappedId.HasValue)
		{
			Log.Debug("[FacebookBridge] Resolved '{Display}' → userId={UserId} displayName='{DisplayName}'",
				discordDisplayName, mappedId.Value, stripped);
			return (mappedId.Value, stripped);
		}

		ulong syntheticId = SyntheticId(stripped);
		Log.Debug("[FacebookBridge] No mapping for '{Display}', using synthetic ID {SyntheticId}",
			discordDisplayName, syntheticId);
		return (syntheticId, stripped);
	}

	/// <summary>
	/// Discord's client appends "APP" to bridge-account display names as a visual indicator.
	/// Strip it so the name matches the Facebook mapping keys.
	/// </summary>
	public static string StripAppSuffix(string name) =>
		name.EndsWith("APP", StringComparison.OrdinalIgnoreCase)
			? name[..^3].TrimEnd()
			: name;

	/// <summary>
	/// Derives a stable 64-bit user ID from a Facebook name.
	/// Sets the high bit so the value can never collide with a real Discord Snowflake
	/// (which are currently well below 2^63).
	/// </summary>
	private static ulong SyntheticId(string name)
	{
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("bridge:" + name.ToLowerInvariant()));
		return BitConverter.ToUInt64(hash, 0) | 0x8000_0000_0000_0000UL;
	}
}
