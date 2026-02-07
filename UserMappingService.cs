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
					Console.WriteLine($"Loaded {mappings.Mappings.Count} user mapping(s)");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error loading mappings: {ex.Message}");
					mappings = new UserMappings();
				}
			}
			else
			{
				Console.WriteLine("No mapping file found, creating template...");
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
				Console.WriteLine($"Created template mapping file at: {Path.GetFullPath(_filePath)}");
				Console.WriteLine("Please edit this file with actual Facebook names and Discord user IDs");
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

			Console.WriteLine($"{(isNew ? "Added" : "Updated")} mapping: '{facebookName}' -> {discordUserId}");
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
				Console.WriteLine($"Removed mapping for '{facebookName}'");
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