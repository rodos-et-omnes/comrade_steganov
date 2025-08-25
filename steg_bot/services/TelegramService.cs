using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace StegBot.services;

public class TelegramService
{
    private readonly ITelegramBotClient _bot;
    private readonly GrpcStegClient _grpcClient;
    private readonly CryptoService _crypto;
    private readonly Dictionary<long, UserState> _userStates = new();

    public TelegramService(string token, string grpcAddress)
    {
        _bot = new TelegramBotClient(token);
        _grpcClient = new GrpcStegClient(grpcAddress);
        _crypto = new CryptoService();

        var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };
        _bot.StartReceiving(
            updateHandler: UpdateHandler,
            errorHandler: ErrorHandler,
            receiverOptions: receiverOptions
        );
        Console.WriteLine("Telegram service is running");
    }
    private abstract class UserState
    {
        public byte[]? Image { get; set; }
        public abstract Task<Message> HandleMessage(Message message, ITelegramBotClient bot, TelegramService service, CancellationToken ct);
    }

    private class InitialState : UserState
    {
        public override Task<Message> HandleMessage(Message message, ITelegramBotClient bot, TelegramService service, CancellationToken ct)
            => throw new InvalidOperationException("No operation in progress");
    }

    private class HideState : UserState
    {
        private string _currentState = "WaitingForSourceImage";
        public bool IsCompleted { get; private set; }
        public override async Task<Message> HandleMessage(Message message, ITelegramBotClient bot, TelegramService service, CancellationToken ct)
        {
            switch (_currentState)
            {
                case "WaitingForSourceImage":
                    if (message.Document != null && Image == null)
                    {
                        var fileInfo = await bot.GetFile(message.Document.FileId, ct);

                        if (fileInfo.FilePath != null)
                        {
                            using var stream = new MemoryStream();
                            await bot.DownloadFile(fileInfo.FilePath, stream, ct);
                            Image = stream.ToArray();
                            _currentState = "WaitingForSourceText";
                            return await bot.SendMessage(
                                message.Chat.Id,
                                "Enter text (without special symbols):",
                                cancellationToken: ct);
                        }
                        else return await bot.SendMessage(message.Chat.Id, "Invalid image, try again", cancellationToken: ct);
                    }
                    break;
                case "WaitingForSourceText":
                    if (!string.IsNullOrEmpty(message.Text) && Image != null)
                    {
                        string password = service._crypto.GeneratePassword();
                        var (cipherText, iv, salt) = service._crypto.Encrypt(message.Text, password);
                        byte[] stegoImage = await service._grpcClient.HideImageAsync(Image, cipherText);

                        using var resultStream = new MemoryStream(stegoImage);
                        IsCompleted = true;
                        return await bot.SendDocument(
                            message.Chat.Id,
                            InputFile.FromStream(resultStream, "pbkdf2.png"),
                            caption: $"Password: \n \n {password}\n \n iv(Base64): \n \n {Convert.ToBase64String(iv)}\n \n Salt(Base64): \n \n {Convert.ToBase64String(salt)}",
                            cancellationToken: ct);
                    }
                    break;
            }

            throw new InvalidOperationException("Unexpected message type");
        }
    }

    private class ExtractState : UserState
    {
        public string? Password { get; set; }
        public byte[]? IV { get; set; }
        public byte[]? Salt { get; set; }
        private string _currentState = "WaitingForImage";
        public bool IsCompleted { get; private set; }
        
        public override async Task<Message> HandleMessage(Message message, ITelegramBotClient bot, TelegramService service, CancellationToken ct)
        {
            switch (_currentState)
            {
                case "WaitingForImage" when message.Document != null:
                    var fileInfo = await bot.GetFile(message.Document.FileId, ct);
                    if (fileInfo.FilePath != null)
                    {
                        using (var stream = new MemoryStream())
                        {
                            await bot.DownloadFile(fileInfo.FilePath, stream, ct);
                            Image = stream.ToArray();
                        }
                        _currentState = "WaitingForPassword";
                        return await bot.SendMessage(message.Chat.Id, "Enter password:", cancellationToken: ct);
                    }
                    else return await bot.SendMessage(message.Chat.Id, "Invalid image, try again", cancellationToken: ct);
                case "WaitingForPassword" when message.Text != null:
                    Password = message.Text;
                    _currentState = "WaitingForIV";
                    return await bot.SendMessage(message.Chat.Id, "Enter iv (Base64):", cancellationToken: ct);

                case "WaitingForIV" when message.Text != null:
                    try
                    {
                        IV = Convert.FromBase64String(message.Text);
                        _currentState = "WaitingForSalt";
                        return await bot.SendMessage(message.Chat.Id, "Enter salt (Base64):", cancellationToken: ct);
                    }
                    catch
                    {
                        return await bot.SendMessage(message.Chat.Id, "Invalid Base64 encoding, try again:", cancellationToken: ct);
                    }

                case "WaitingForSalt" when message.Text != null:
                    try
                    {
                        Salt = Convert.FromBase64String(message.Text);
                        var extractedText = await service.ExtractTextFromImage(this);
                        IsCompleted = true;
                        return await bot.SendMessage(message.Chat.Id, $"Extracted text:\n{extractedText}", cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        return await bot.SendMessage(message.Chat.Id, $"Error: {ex.Message}", cancellationToken: ct);
                    }

                default:
                    throw new InvalidOperationException("Unexpected message");
            }
        }
    }
    
    private async Task UpdateHandler(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message is not { } message) return;

        try
        {
            if (message.Text == "/start" || message.Text == "/help")
            {
                await SendHelpMessage(message.Chat.Id, bot, ct);
                return;
            }

            if (message.Text == "/ping")
            {
                await bot.SendMessage(message.Chat.Id, "pong", cancellationToken: ct);
                return;
            }

            if (message.Text == "/hide")
            {
                _userStates[message.Chat.Id] = new HideState();
                await bot.SendMessage(message.Chat.Id, "Send PNG image as a file (without compression)", cancellationToken: ct);
                return;
            }

            if (message.Text == "/extract")
            {
                _userStates[message.Chat.Id] = new ExtractState();
                await bot.SendMessage(message.Chat.Id, "Send PNG image as a file (without compression)", cancellationToken: ct);
                return;
            }

            if (_userStates.TryGetValue(message.Chat.Id, out var state))
            {
                var response = await state.HandleMessage(message, bot, this, ct);
                if (state is HideState hideState && hideState.IsCompleted)
                {
                    _userStates.Remove(message.Chat.Id);
                }
                else if (state is ExtractState extractState && extractState.IsCompleted)
                {
                    _userStates.Remove(message.Chat.Id);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            await bot.SendMessage(message.Chat.Id, $"Error: {ex.Message}", cancellationToken: ct);
        }
    }

    private async Task<string> ExtractTextFromImage(ExtractState state)
    {
        if (state.Image == null || state.Password == null || state.IV == null || state.Salt == null)
            throw new Exception("Extraction impossible");
    
        var encryptedData = await _grpcClient.ExtractTextAsync(state.Image);
        return _crypto.Decrypt(encryptedData, state.IV, state.Salt, state.Password);
    }

    private static async Task<Message> SendHelpMessage(long chatId, ITelegramBotClient bot, CancellationToken ct)
    {
        return await bot.SendMessage(
            chatId,
            "This is a steganographical instrument with salted AES256 encryption. \nIt provides inserting encrypted text into a PNG image. For correct decryption insert small messages without any special symbols ([aA-zZ 0-9] allowed). Extraction and decryption might take up to 2 minutes. \n \n ~For legal purposes only~ \n \n Command list: \n /hide - insert text into image \n /extract - extract text from image \n /help - show this message \n /ping - check if the bot's alive\n \n Consider deleting messages that you don't need anymore.",
            cancellationToken: ct
        );
    }

    private Task ErrorHandler(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        Console.WriteLine($"Error: {ex}");
        return Task.CompletedTask;
    }
}