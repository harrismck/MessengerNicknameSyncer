using Serilog;
using System.Text.Json;

namespace MessengerNicknameSyncer.Services;

public class UserMappingService
{
	private readonly string _filePath;
	private readonly UserMappings _mappings;
	private readonly object _lockObj = new();
	private JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
	
	public UserMappingService(string filePath)
	{
		_filePath = filePath;
		_mappings = LoadOrCreateMappings();
	}

	private UserMappings LoadOrCreateMappings()
	{
		UserMappings mappings;
		lock (_lockObj)
		{
			if (File.Exists(_filePath))
			{
				try
				{
					string json = File.ReadAllText(_filePath);
					mappings = JsonSerializer.Deserialize<UserMappings>(json) ?? new UserMappings();
					Log.Information("Loaded {MappingCount} user mapping(s)", mappings.Mappings.Count);
				}
				catch (Exception ex)
				{
					Log.Error(ex, "Error loading mappings");
					mappings = new UserMappings();
				}
			}
			else
			{
				Log.Warning("No mapping file found, creating template...");
				mappings = new UserMappings
				{
					Mappings = new Dictionary<string, ulong>
					{
						// Template entries
						["John Smith"] = 123456789012345678,
						["Jane Doe"] = 987654321098765432
					}
				};
				SaveMappings();
				Log.Information("Created template mapping file at: {FilePath}", Path.GetFullPath(_filePath));
				Log.Information("Please edit this file with actual Facebook names and Discord user IDs");
			}
		}

		return mappings;
	}

	private void SaveMappings()
	{
		lock (_lockObj)
		{
			string json = JsonSerializer.Serialize(_mappings, _jsonOptions);
			File.WriteAllText(_filePath, json);
		}
	}

	public ulong? GetDiscordUserId(string facebookName)
	{
		lock (_lockObj)
		{
			// Try exact match first
			if (_mappings.Mappings.TryGetValue(facebookName, out ulong discordUserId))
			{
				return discordUserId;
			}
            
			// Fallback: try matching by first name
			return GetDiscordUserIdByFirstName(facebookName);
		}
	}

	private ulong? GetDiscordUserIdByFirstName(string firstName)
	{
		// Find all mappings where the key starts with the first name
		List<KeyValuePair<string, ulong>> matches = _mappings.Mappings
			.Where(m => 
				m.Key.StartsWith(firstName + " ", StringComparison.OrdinalIgnoreCase) 
				|| m.Key.Equals(firstName, StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (matches.Count == 0)
		{
			return null;
		}

		if (matches.Count == 1)
		{
			Console.WriteLine($"ℹ️ Matched '{firstName}' to '{matches[0].Key}' by first name");
			return matches[0].Value;
		}

		// Multiple matches - log warning
		Console.WriteLine($"⚠️ Multiple mappings found for first name '{firstName}':");
		foreach (KeyValuePair<string, ulong> match in matches)
		{
			Console.WriteLine($"   - {match.Key}");
		}
		Console.WriteLine($"   Using first match: {matches[0].Key}");
        
		return matches[0].Value;
	}

	public void AddOrUpdateMapping(string facebookName, ulong discordUserId)
	{
		lock (_lockObj)
		{
			bool isNew = !_mappings.Mappings.ContainsKey(facebookName);
			_mappings.Mappings[facebookName] = discordUserId;
			SaveMappings();

			Log.Information("{Action} mapping: '{FacebookName}' -> {DiscordUserId}", isNew ? "Added" : "Updated", facebookName, discordUserId);
		}
	}

	public bool RemoveMapping(string facebookName)
	{
		lock (_lockObj)
		{
			bool removed = _mappings.Mappings.Remove(facebookName);
			if (removed)
			{
				SaveMappings();
				Log.Information("Removed mapping for '{FacebookName}'", facebookName);
			}
			return removed;
		}
	}

	public Dictionary<string, ulong> GetAllMappings()
	{
		lock (_lockObj)
		{
			return new Dictionary<string, ulong>(_mappings.Mappings);
		}
	}
	
	public string? GetPreferredFacebookName(ulong discordUserId)
	{
		lock (_lockObj)
		{
			// Find all Facebook names that map to this Discord user
			List<string> matchingNames = _mappings.Mappings
				.Where(m => m.Value == discordUserId)
				.Select(m => m.Key)
				.ToList();

			if (matchingNames.Count == 0)
				return null;

			if (matchingNames.Count == 1)
				return matchingNames[0];

			// Multiple names - prefer the "real" name over special keywords
			// Special keywords to deprioritize: "your"
			var specialKeywords = new[] { "your" };

			List<string> realNames = matchingNames
				.Where(name => !specialKeywords.Contains(name, StringComparer.OrdinalIgnoreCase))
				.ToList();

			if (realNames.Count > 0)
			{
				// Prefer the longest name (likely most complete)
				string preferredName = realNames.OrderByDescending(n => n.Length).First();
				Console.WriteLine($"ℹ️ User {discordUserId} has multiple mappings, preferring '{preferredName}' over {string.Join(", ", matchingNames.Where(n => n != preferredName).Select(n => $"'{n}'"))}");
				return preferredName;
			}

			// Fallback to first special keyword if that's all we have
			return matchingNames[0];
		}
	}
}

public class UserMappings
{
	public Dictionary<string, ulong> Mappings { get; set; } = new();
}