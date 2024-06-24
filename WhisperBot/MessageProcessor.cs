using System.Net.Http.Headers;
using Newtonsoft.Json;
using Telegram.Bot;
using Xabe.FFmpeg;

namespace WhisperBot;

public class MessageProcessor
{
    private readonly string _openApiKey;

    public MessageProcessor(string openApiKey)
    {
        _openApiKey = openApiKey;
    }

    public async Task<string> ConvertMediaAsync(ITelegramBotClient botClient, string fileId, bool isVideo,
        CancellationToken ct)
    {
        var sourceFilePath = $"{Guid.NewGuid()}.file{(isVideo ? ".mp4" : "")}";
        var outputFilePath = $"{sourceFilePath}.mp3";
        
        try
        {
            await using (Stream fileStream = File.Create(sourceFilePath))
            {
                await botClient.GetInfoAndDownloadFileAsync(
                    fileId: fileId,
                    destination: fileStream,
                    cancellationToken: ct);
            }
            
            await ExtractAudioAsync(sourceFilePath, outputFilePath, ct);

            var result = await RecognizeAudioAsync(outputFilePath, ct);
            return result;
        }
        catch (Exception e)
        {
            return e.Message;
        }
        finally
        {
            if(File.Exists(sourceFilePath))
                File.Delete(sourceFilePath);  
            if(File.Exists(outputFilePath))
                File.Delete(outputFilePath);
        }
    }

    private async Task ExtractAudioAsync(string sourceFilePath, string outputFilePath, CancellationToken ct)
    {
        IMediaInfo inputFile = await FFmpeg.GetMediaInfo(sourceFilePath, ct);
        var snippet = FFmpeg.Conversions.New()
            .AddStream(inputFile.Streams)
            .SetOutputFormat(Format.mp3)
            .SetAudioBitrate(16000)
            .AddParameter("-ar 12000")
            .SetOutput(outputFilePath);
        await snippet.Start(ct);
    }

    private async Task<string> RecognizeAudioAsync(string filePath, CancellationToken ct)
    {
        const string url = "https://api.openai.com/v1/audio/transcriptions";

        using var httpClient = new HttpClient();
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(filePath, ct));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/mpeg");
        form.Add(fileContent, "file", Path.GetFileName(filePath));
        form.Add(new StringContent("whisper-1"), "model");
        form.Add(new StringContent("language"), "ru");

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openApiKey);

        var response = await httpClient.PostAsync(url, form, ct);

        if (!response.IsSuccessStatusCode)
            return $"Error: {response.ReasonPhrase}";

        var stringResponse = await response.Content.ReadAsStringAsync(ct);
        var openApiResponse = JsonConvert.DeserializeObject<OpenApiResponse>(stringResponse);
        return openApiResponse!.Text;
    }
}