using Grpc.Net.Client;
using Steg;
using Google.Protobuf;

namespace StegBot.services;

public class GrpcStegClient : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly SteganographyService.SteganographyServiceClient _client;

    public GrpcStegClient(string address)
    {
        _channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = 100 * 1024 * 1024,
            HttpHandler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(30)
            }
        });

        _client = new SteganographyService.SteganographyServiceClient(_channel);
    }

    public async Task<Byte[]> HideImageAsync(byte[] image, byte[] secretData)
    {
        var request = new HideRequest
        {
            Image = ByteString.CopyFrom(image),
            SecretEncrypted = ByteString.CopyFrom(secretData)
        };

        var response = await _client.HideAsync(request);

        if (!string.IsNullOrEmpty(response.Error))
        {
            throw new Exception($"gRPC error: {response.Error}");
        }

        return response.StegoImage.ToByteArray();
    }

    public async Task<byte[]> ExtractTextAsync(byte[] stegoImage)
    {
        var request = new ExtractRequest
        {
            StegoImage = ByteString.CopyFrom(stegoImage)
        };

        var response = await _client.ExtractAsync(request);
        
        if (!string.IsNullOrEmpty(response.Error))
            throw new Exception($"gRPC error: {response.Error}");

        return response.SecretDecrypted.ToByteArray();
    }


    public void Dispose() => _channel?.Dispose();
}