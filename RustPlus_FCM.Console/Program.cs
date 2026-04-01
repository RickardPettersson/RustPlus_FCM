// Copyright (c) 2026 Rickard Nordström Pettersson. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Source: https://github.com/RickardPettersson/RustPlus_FCM

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.Text.Json;
using System.Text.Json.Nodes;

var (command, configFile) = ParseArgs(args);

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddConsole();
    builder.AddFile(Path.Combine(AppContext.BaseDirectory, "rustplus.log"));
    builder.AddFilter<ConsoleLoggerProvider>(null, LogLevel.Information);
});
var logger = loggerFactory.CreateLogger("RustPlus");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

switch (command)
{
    case "fcm-register":
        await FcmRegisterAsync(configFile, cts.Token);
        break;
    case "fcm-listen":
        await FcmListenAsync(configFile, cts.Token);
        break;
    case "help":
    default:
        ShowUsage();
        break;
}

return;

static (string? command, string configFile) ParseArgs(string[] args)
{
    string? command = null;
    string? configFile = null;

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--config-file" && i + 1 < args.Length)
        {
            configFile = args[++i];
        }
        else if (command is null)
        {
            command = args[i];
        }
    }

    configFile ??= Path.Combine(Directory.GetCurrentDirectory(), "rustplus.config.json");

    return (command, configFile);
}

async Task FcmRegisterAsync(string configFile, CancellationToken cancellationToken)
{
    logger.LogInformation("Registering with FCM");

    var fcmLogger = loggerFactory.CreateLogger("GoogleFcm");
    FcmCredentials fcmCredentials;
    try
    {
        fcmCredentials = await GoogleFcm.RegisterAsync(fcmLogger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to register with FCM");
        loggerFactory.Dispose();
        Environment.Exit(1);
        return;
    }

    logger.LogInformation("Fetching Expo Push Token");
    string expoPushToken;
    try
    {
        expoPushToken = await ApiClient.GetExpoPushTokenAsync(fcmCredentials.Fcm.Token);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to fetch Expo Push Token");
        loggerFactory.Dispose();
        Environment.Exit(1);
        return;
    }

    logger.LogInformation("Successfully fetched Expo Push Token");
    logger.LogInformation("Expo Push Token: {ExpoPushToken}", expoPushToken);

    logger.LogInformation("Google Chrome is launching so you can link your Steam account with Rust+");
    var steamLogger = loggerFactory.CreateLogger("SteamPairing");
    var rustplusAuthToken = await SteamPairing.LinkSteamWithRustPlusAsync(steamLogger, cancellationToken);

    logger.LogInformation("Successfully linked Steam account with Rust+");
    logger.LogInformation("Rust+ AuthToken: {AuthToken}", rustplusAuthToken);

    logger.LogInformation("Registering with Rust Companion API");
    try
    {
        await ApiClient.RegisterWithRustPlusAsync(rustplusAuthToken, expoPushToken);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to register with Rust Companion API");
        loggerFactory.Dispose();
        Environment.Exit(1);
        return;
    }

    logger.LogInformation("Successfully registered with Rust Companion API.");

    var configData = new JsonObject
    {
        ["fcm_credentials"] = JsonSerializer.SerializeToNode(new
        {
            gcm = new
            {
                androidId = fcmCredentials.Gcm.AndroidId,
                securityToken = fcmCredentials.Gcm.SecurityToken,
                token = fcmCredentials.Gcm.Token,
            },
            fcm = new
            {
                token = fcmCredentials.Fcm.Token,
            },
        }),
        ["expo_push_token"] = expoPushToken,
        ["rustplus_auth_token"] = rustplusAuthToken,
    };

    ConfigManager.UpdateConfig(configFile, configData);
    logger.LogInformation("FCM, Expo and Rust+ auth tokens have been saved to {ConfigFile}", configFile);
}

async Task FcmListenAsync(string configFile, CancellationToken cancellationToken)
{
    var config = ConfigManager.ReadConfig(configFile);

    if (!config.ContainsKey("fcm_credentials"))
    {
        logger.LogError("FCM Credentials missing. Please run `fcm-register` first.");
        loggerFactory.Dispose();
        Environment.Exit(1);
        return;
    }

    var fcmCreds = config["fcm_credentials"]!.AsObject();
    var gcm = fcmCreds["gcm"]!.AsObject();
    var androidId = gcm["androidId"]!.GetValue<string>();
    var securityToken = gcm["securityToken"]!.GetValue<string>();

    logger.LogInformation("Loaded credentials: androidId={AndroidId}, securityToken={SecurityToken}", androidId, securityToken);
    logger.LogInformation("Listening for FCM Notifications");

    var mcsLogger = loggerFactory.CreateLogger("McsClient");
    using var client = new McsClient(androidId, securityToken, mcsLogger);
    string? lastBodyId = null;
    client.OnDataReceived += data =>
    {
        var notification = data.Deserialize<RustPlusNotification>();
        if (notification is not null)
        {
            var notificationBody = notification.ParseBody();
            if (notificationBody is not null)
            {
                if (notificationBody.Id is not null && notificationBody.Id == lastBodyId)
                {
                    logger.LogDebug("Duplicate notification skipped (Id={Id})", notificationBody.Id);
                    return;
                }
                lastBodyId = notificationBody.Id;

                logger.LogInformation("Notification Received");
                // Server pairing notification
                if (notification.ChannelId == "pairing" && notificationBody.Type == "server")
                {
                    logger.LogInformation(
                        "Rust+ Pairing Notification - Server: {Server}, IP: {Ip}:{Port}, Player ID: {PlayerId}, Player Token: {PlayerToken}",
                        notificationBody.Name, notificationBody.Ip, notificationBody.Port,
                        notificationBody.PlayerId, notificationBody.PlayerToken);
                }
                // Alarm notification (e.g. tool cupboard under attack)
                else if (notification.ChannelId == "alarm" && notificationBody.Type == "alarm")
                {
                    logger.LogWarning(
                        "Alarm - Server: {Server}, Title: {Title}, Message: {Message}",
                        notificationBody.Name, notification.Title, notification.Message);
                }
                else if (notification.ChannelId == "team" && notificationBody.Type == "login")
                {
                    logger.LogInformation(
                        "Team Login - Server: {Server}, Title: {Title}",
                        notificationBody.Name, notification.Title);
                }
                else if (notification.ChannelId == "pairing" && notificationBody.Type == "entity")
                {
                    logger.LogInformation(
                        "Entity Pairing - Title: {Title}, Entity Type: {EntityType}, Entity ID: {EntityId}, Entity Name: {EntityName}",
                        notification.Title, notificationBody.EntityType,
                        notificationBody.EntityId, notificationBody.EntityName);
                }
                // Unknown notification type
                else
                {
                    logger.LogDebug("Raw JSON:\n{Json}", data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                    var parts = new List<string>();
                    if (notificationBody?.Name is not null)
                        parts.Add($"Server: {notificationBody.Name}");
                    if (notification.Title is not null)
                        parts.Add($"Title: {notification.Title}");
                    if (notification.Message is not null)
                        parts.Add($"Message: {notification.Message}");

                    logger.LogInformation("Unknown Notification - {Details}", string.Join(", ", parts));
                }
            }
            else
            {
                // Body not parsed
                logger.LogDebug("Body not parsed - Raw JSON:\n{Json}", data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
        } else
        {
            // Body not parsed
            logger.LogDebug("Deserialize to RustPlusNotification failed - Raw JSON:\n{Json}", data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    };

    try
    {
        await client.ConnectAsync(cancellationToken);
    }
    catch (OperationCanceledException)
    {
        // Graceful shutdown on Ctrl+C
    }
}

static void ShowUsage()
{
    Console.WriteLine("""
        RustPlus CLI (.NET)
        A command line tool for things related to Rust+

        Usage: rustplus <options> <command>

        Commands:
          help            Print this usage guide.
          fcm-register    Registers with FCM, Expo and links your Steam account
                          with Rust+ so you can listen for Pairing Notifications.
          fcm-listen      Listens to notifications received from FCM, such as
                          Rust+ Pairing Notifications.

        Options:
          --config-file <file>    Path to config file.
                                  (default: rustplus.config.json)
        """);
}
