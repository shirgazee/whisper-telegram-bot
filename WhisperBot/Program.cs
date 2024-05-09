using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WhisperBot;
using Xabe.FFmpeg.Downloader;

await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetRequiredSection(nameof(Settings)).Get<Settings>();
if (string.IsNullOrEmpty(settings?.TelegramBotApiKey))
{
    Console.WriteLine("Telegram API key not found. Exiting...");
    return;
}
var allowedUsers = settings.AllowedUsers.Split(",");

var messageProcessor = new MessageProcessor(settings.OpenAiApiKey);
var botClient = new TelegramBotClient(settings.TelegramBotApiKey);

using CancellationTokenSource cts = new ();
ReceiverOptions receiverOptions = new ()
{
    AllowedUpdates = Array.Empty<UpdateType>()
};

botClient.StartReceiving(
    updateHandler: HandleUpdateAsync,
    pollingErrorHandler: HandlePollingErrorAsync,
    receiverOptions: receiverOptions,
    cancellationToken: cts.Token
);

var hostBuilder = new HostBuilder();
hostBuilder.ConfigureHostConfiguration(builder =>
{
    builder.Sources.Clear();
    builder.AddConfiguration(configuration);
});
Console.WriteLine("Starting ...");
await hostBuilder.RunConsoleAsync();
cts.Cancel();

async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
{
    if (update.Message is not { } message)
        return;
    
    if(string.IsNullOrWhiteSpace(message.From?.Username) || !allowedUsers.Contains(message.From.Username))
        return;
    
    Task<string> processTask;
    switch (message.Type)
    {
        case MessageType.Audio when message.Audio?.FileId != null:
            processTask = messageProcessor.ConvertMediaAsync(bot, message.Audio.FileId, false, cancellationToken);
            break;
        case MessageType.Voice when message.Voice?.FileId != null:
            processTask = messageProcessor.ConvertMediaAsync(bot, message.Voice.FileId, false, cancellationToken);
            break;
        case MessageType.VideoNote when message.VideoNote?.FileId != null:
            processTask = messageProcessor.ConvertMediaAsync(bot, message.VideoNote.FileId, true, cancellationToken);
            break;
        default:
            return;
    }
    var chatId = message.Chat.Id;
    Message sentMessage = await bot.SendTextMessageAsync(
        chatId: chatId,
        text: "...",
        cancellationToken: cancellationToken);

    var parsedText = await processTask;
    
    await bot.EditMessageTextAsync(chatId: chatId, messageId: sentMessage.MessageId, text: parsedText,
        cancellationToken: cancellationToken);
}

Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
{
    var errorMessage = exception switch
    {
        ApiRequestException apiRequestException
            => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
        _ => exception.ToString()
    };

    Console.WriteLine(errorMessage);
    return Task.CompletedTask;
}