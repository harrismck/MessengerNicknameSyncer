using System.Text.RegularExpressions;

namespace MessengerNicknameSyncer;

public static class NicknameMessageParser
{
	private static readonly Regex NicknameSetPattern = new(
		@"^(.+?) set (?:the nickname for (.+?)|(your) nickname|(?:his|her|their) own nickname) to (.+?)\.$",
		RegexOptions.Compiled);

	private static readonly Regex NicknameClearPattern = new(
		@"^(?:You|(.+?)) cleared (?:the nickname for (.+?)|(your) nickname|(?:his|her|their) own nickname)\.$",
		RegexOptions.Compiled);

	public static Match MatchSetPattern(string content) => NicknameSetPattern.Match(content);

	public static Match MatchClearPattern(string content) => NicknameClearPattern.Match(content);

	public static string ExtractTargetNameFromSetMatch(Match match)
	{
		// Determine target name from the three possible patterns:
		// 1. "set the nickname for X" -> use group 2 (X)
		// 2. "set your nickname" -> use group 3 ("your")
		// 3. "set his/her/their own nickname" -> use group 1 (person doing action)
		if (!string.IsNullOrEmpty(match.Groups[2].Value))
			return match.Groups[2].Value;

		// This happens when changing the nickname for the user who set up the facebook <> matrix <> discord bridge!
		// For some reason, messages renaming them show up like:
		// "<User1> set your nickname to <target's new nickname>."
		// Solve this by mapping that user both as their facebook name, AND as "your"
		if (!string.IsNullOrEmpty(match.Groups[3].Value))
			return match.Groups[3].Value; // "your"

		return match.Groups[1].Value;
	}

	public static string? ExtractTargetNameFromClearMatch(Match match)
	{
		// Determine target from three possible patterns:
		// 1. "cleared the nickname for X" -> use group 2
		// 2. "cleared your nickname" -> use group 3 ("your")
		// 3. "cleared his/her/their own nickname" -> use group 1 (person doing action)
		if (!string.IsNullOrEmpty(match.Groups[2].Value))
			return match.Groups[2].Value;

		// See note in above method on why "your" is here
		if (!string.IsNullOrEmpty(match.Groups[3].Value))
			return match.Groups[3].Value; // "your"

		if (!string.IsNullOrEmpty(match.Groups[1].Value))
			return match.Groups[1].Value;

		return null; // "You cleared your own nickname" - can't determine who
	}

	public static string ExtractNewNicknameFromSetMatch(Match match)
	{
		return match.Groups[4].Value;
	}

	public static string ExtractFirstName(string facebookName)
	{
		// Extract the first word as the first name
		string[] parts = facebookName.Split([' '], StringSplitOptions.RemoveEmptyEntries);
		return parts.Length > 0 ? parts[0] : facebookName;
	}
}