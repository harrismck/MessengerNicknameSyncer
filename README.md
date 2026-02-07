# MessengerNicknameSyncer
Syncs nicknames (and chat names) from messenger to Discord, assuming they're already bridged (usually via Matrix)
Very simply just tracks nickname changes by looking for messages of a specific format.
Has a basic setup for authentication, and some commands to interact with the bot.

# Commands
The below output can be seen by sending the message "!nicknameHelp" command once the bot is set up

**Mapping Management:** *(Requires MappingManagement permission)*
`!map <user_id> <facebook_name>` - Add/update a mapping
  Example: `!map 123456789012345678 John Smith`
`!unmap <facebook_name>` - Remove a mapping
  Example: `!unmap John Smith`
`!reloadMappings` - Reload mappings from file
`!resync [count]  [-reset]` - Re-sync all nicknames from message history
  Example: `!resync 1000` (default: 1000)
  The -reset flag will rename mapped (but not recently renamed) discord users to their first name from FB
  Note: Can only be used in the nickname sync channel

**Info Commands:**
`!listMaps` - Show all current mappings
`!nicknameHelp` - Show this help message

**Automatic Features:**
- Syncs nicknames from Facebook Messenger messages
  Format: `<User> set the nickname for <Name> to <Nickname>.`
- Auto-renames configured channels when Facebook group is renamed
  Format: `<First Name> named the group <New Name>.`
  *(Requires ChannelRename permission if RequireAuthorization is true)*

# Deployment
You will need to set up and register the bot on your own int he Discord developer portal.
Once that is done, it can be deployed by either building and running the C# solution, or using docker.
There is an example docker-compose.yml if you wish to deploy that way.
