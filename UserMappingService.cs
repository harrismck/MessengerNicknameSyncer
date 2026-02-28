using Serilog;
using System.Text.Json;

public class UserMappingService
{
	private readonly string _filePath;
	private readonly UserMappings _mappings;
	private readonly object _lockObj = new();

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
			JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
			string json = JsonSerializer.Serialize(_mappings, options);
			File.WriteAllText(_filePath, json);
		}
	}

	public ulong? GetDiscordUserId(string facebookName)
	{
		lock (_lockObj)
		{
			if (_mappings.Mappings.TryGetValue(facebookName, out ulong discordUserId))
			{
				return discordUserId;
			}
			return null;
		}
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

	public void Reload()
	{
		LoadOrCreateMappings();
	}
}

public class UserMappings
{
	public Dictionary<string, ulong> Mappings { get; set; } = new();
}