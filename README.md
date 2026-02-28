# MessengerNicknameSyncer

A Discord bot that syncs nicknames (and group chat names) from Facebook Messenger to Discord, assuming the two are already bridged (typically via a Matrix bridge). It works by watching a channel for Messenger activity messages and applying the corresponding changes in Discord.

## How It Works

The bot monitors a designated Discord channel for messages matching the patterns posted by Messenger bridges:

- **Nickname changes:** `<User> set the nickname for <Name> to <Nickname>.`
- **Group renames:** `<First Name> named the group <New Name>.`

When a match is found, it looks up the Facebook name in `user_mappings.json` to find the corresponding Discord user, then applies the change.

## Prerequisites

1. Create a bot in the [Discord Developer Portal](https://discord.com/developers/applications)
2. Under **Bot**, enable the following **Privileged Gateway Intents**:
   - Server Members Intent
   - Message Content Intent
3. Invite the bot to your server with permissions: `Change Nickname`, `Manage Nicknames`, `Read Messages`, `Add Reactions`

## Configuration

Copy `appsettings_EXAMPLE.json` to `appsettings.json` and fill in your values:

```jsonc
{
  "Discord": {
    "BotToken": "<your bot token>",
    "NicknameSyncChannelId": "<channel ID to watch for nickname changes>",
    "ResyncMessageCount": 2000,          // default message count for !resync
    "Authorization": {
      "MappingManagement": {
        "AllowedRoleIds": ["<role ID>"],  // roles that can use mapping commands
        "AllowedUserIds": ["<user ID>"]   // individual users (optional fallback)
      },
      "ChannelRename": {
        "AllowedRoleIds": [],
        "AllowedUserIds": []
      },
      "InfoCommands": {
        "AllowedRoleIds": [],
        "AllowedUserIds": [],
        "AllowEveryone": true             // set true to allow all users
      }
    },
    "AutoRenameChannels": {
      "Enabled": false,
      "ChannelIds": ["<channel ID>"],     // Discord channels to rename on group rename
      "RequireAuthorization": true
    }
  }
}
```

The bot token can also be provided via the `Discord__BotToken` environment variable (useful for Docker).

## User Mappings

The bot uses `user_mappings.json` to map Facebook display names to Discord user IDs:

```json
{
  "Mappings": {
    "John Smith": 123456789012345678,
    "Jane Doe": 987654321098765432
  }
}
```

If the file doesn't exist on startup, a template is created automatically. You can manage mappings at runtime using the `!map` and `!unmap` commands, or by editing the file directly and running `!reloadMappings`.

## Commands

Use `!nicknameHelp` in Discord to display the command reference. All commands require the appropriate permission configured in `appsettings.json`.

**Mapping Management** *(requires MappingManagement permission)*

| Command | Description |
|---|---|
| `!map <user_id> <facebook_name>` | Add or update a mapping |
| `!unmap <facebook_name>` | Remove a mapping |
| `!reloadMappings` | Reload mappings from `user_mappings.json` |
| `!resync [count] [-reset]` | Re-apply nicknames from message history (sync channel only) |

`!resync` scans the last `count` messages (default from config) and applies the most recent nickname for each mapped user. The `-reset` flag will reset any mapped users with no recent nickname change back to their first name.

**Info Commands** *(requires InfoCommands permission, default: everyone)*

| Command | Description |
|---|---|
| `!listMaps` | Show all current mappings |
| `!nicknameHelp` | Show the help message |

## Logging

The bot logs to both the console and a daily rolling file in the `logs/` directory (e.g. `logs/bot-20260227.log`). Discord.Net's internal log messages are also captured.

## Deployment

### Run directly

```bash
dotnet run
```

### Docker

A `docker-compose.yml` is included. Copy `EXAMPLE.env` to `.env` and set your bot token:

```
DISCORD_BOT_TOKEN=your_token_here
```

Update the volume paths in `docker-compose.yml` to point to your `appsettings.json` and `user_mappings.json`, then run:

```bash
docker compose up -d
```

To persist log files, add a logs volume to `docker-compose.yml`:
```yaml
volumes:
  - /path/to/your/appsettings.json:/app/appsettings.json:ro
  - /path/to/your/user_mappings.json:/app/user_mappings.json
  - /path/to/your/logs:/app/logs
```
