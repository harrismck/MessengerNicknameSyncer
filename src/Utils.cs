using Microsoft.Extensions.Configuration;
using Serilog;

namespace MessengerNicknameSyncer;

public static class Utils
{
	public static HashSet<ulong> ParseUlongArray(IConfigurationSection parentSection, string key)
	{
		IConfigurationSection section = parentSection.GetSection(key);
		HashSet<ulong> values = [];

		foreach (IConfigurationSection child in section.GetChildren())
		{
			if (ulong.TryParse(child.Value, out ulong id))
			{
				values.Add(id);
			}
			else
			{
				Log.Warning("Invalid ID in {Path}:{Key}: {Value}", parentSection.Path, key, child.Value);
			}
		}

		return values;
	}
}