using StegBot.services;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .Build();

string TelegramToken = config["BotToken"];
string GrpcAddress = config["GrpcAddress"];

var botService = new TelegramService(TelegramToken, GrpcAddress);

Console.WriteLine("Bot awake");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => 
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("exiting...");
};

await Task.Delay(-1, cts.Token);