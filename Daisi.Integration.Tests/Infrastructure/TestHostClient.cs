using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Daisi.SDK.Models;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using System.Threading.Channels;

namespace Daisi.Integration.Tests.Infrastructure;

/// <summary>
/// Simulates a Daisi host by opening a bidirectional gRPC stream to the ORC
/// and processing incoming commands. Handles connect, inference, heartbeat, etc.
/// </summary>
public class TestHostClient : IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly string _clientKey;
    private readonly CancellationTokenSource _cts = new();
    private AsyncDuplexStreamingCall<Command, Command>? _stream;
    private Task? _readTask;

    // Track sessions for response routing
    private readonly Channel<Command> _outgoing = Channel.CreateUnbounded<Command>();

    public bool IsConnected { get; private set; }
    public List<string> ReceivedCommandNames { get; } = new();

    public TestHostClient(GrpcChannel channel, string clientKey)
    {
        _channel = channel;
        _clientKey = clientKey;
    }

    /// <summary>
    /// Connects to the ORC, sends EnvironmentRequest, then starts reading commands in background.
    /// </summary>
    public async Task ConnectAsync()
    {
        var client = new HostCommandsProto.HostCommandsProtoClient(_channel);

        var headers = new Metadata
        {
            { DaisiStaticSettings.ClientKeyHeader, _clientKey }
        };

        _stream = client.Open(headers, cancellationToken: _cts.Token);

        // Send EnvironmentRequest first (like real host does)
        var envCommand = new Command
        {
            Name = nameof(EnvironmentRequest),
            Payload = Any.Pack(new EnvironmentRequest
            {
                OperatingSystem = "Windows",
                OperatingSystemVersion = "10.0",
                AppVersion = "1.0.0"
            })
        };
        await _stream.RequestStream.WriteAsync(envCommand);

        // Start reading incoming commands from ORC in background
        _readTask = Task.Run(ReadIncomingCommandsAsync);

        // Start writing outgoing commands
        _ = Task.Run(WriteOutgoingCommandsAsync);

        IsConnected = true;
    }

    private async Task ReadIncomingCommandsAsync()
    {
        try
        {
            await foreach (var command in _stream!.ResponseStream.ReadAllAsync(_cts.Token))
            {
                ReceivedCommandNames.Add(command.Name);

                try
                {
                    await HandleCommandAsync(command);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"TestHostClient: Error handling {command.Name}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
    }

    private async Task WriteOutgoingCommandsAsync()
    {
        try
        {
            await foreach (var command in _outgoing.Reader.ReadAllAsync(_cts.Token))
            {
                await _stream!.RequestStream.WriteAsync(command);
            }
        }
        catch (OperationCanceledException) { }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
    }

    private async Task HandleCommandAsync(Command command)
    {
        switch (command.Name)
        {
            case nameof(ConnectRequest):
                await HandleConnectRequest(command);
                break;

            case nameof(CreateInferenceRequest):
                await HandleCreateInferenceRequest(command);
                break;

            case nameof(SendInferenceRequest):
                await HandleSendInferenceRequest(command);
                break;

            case nameof(CloseInferenceRequest):
                await HandleCloseInferenceRequest(command);
                break;

            case nameof(InferenceStatsRequest):
                await HandleInferenceStatsRequest(command);
                break;

            case nameof(HeartbeatRequest):
                await HandleHeartbeatRequest(command);
                break;

            case nameof(CloseSessionRequest):
                // No response needed; session is being closed by ORC
                break;
        }
    }

    private Task HandleConnectRequest(Command command)
    {
        var request = command.Payload.Unpack<ConnectRequest>();
        var response = new Command
        {
            Name = nameof(ConnectResponse),
            SessionId = command.SessionId,
            RequestId = command.RequestId,
            Payload = Any.Pack(new ConnectResponse
            {
                Id = request.SessionId,
                HasCapacity = true,
                AlreadyConnected = false
            })
        };
        _outgoing.Writer.TryWrite(response);
        return Task.CompletedTask;
    }

    private Task HandleCreateInferenceRequest(Command command)
    {
        var request = command.Payload.Unpack<CreateInferenceRequest>();
        var inferenceId = $"inf-{Guid.NewGuid()}";

        var response = new Command
        {
            Name = nameof(CreateInferenceResponse),
            SessionId = command.SessionId,
            RequestId = command.RequestId,
            Payload = Any.Pack(new CreateInferenceResponse
            {
                InferenceId = inferenceId,
                SessionId = request.SessionId
            })
        };
        _outgoing.Writer.TryWrite(response);
        return Task.CompletedTask;
    }

    private async Task HandleSendInferenceRequest(Command command)
    {
        var request = command.Payload.Unpack<SendInferenceRequest>();

        // Simulate streaming response with a few tokens
        var tokens = new[] { "Hello", " from", " the", " test", " host", "!" };

        foreach (var token in tokens)
        {
            var tokenResponse = new Command
            {
                Name = nameof(SendInferenceResponse),
                SessionId = command.SessionId,
                RequestId = command.RequestId,
                Payload = Any.Pack(new SendInferenceResponse
                {
                    SessionId = request.SessionId,
                    InferenceId = request.InferenceId,
                    Id = Guid.NewGuid().ToString(),
                    Type = InferenceResponseTypes.Text,
                    Content = token,
                    AuthorRole = "assistant",
                    MessageTokenCount = 1,
                    SessionTokenCount = tokens.Length
                })
            };
            _outgoing.Writer.TryWrite(tokenResponse);
            await Task.Delay(10); // Simulate token generation delay
        }

        // Send ENDSTREAM marker
        var endStream = new Command
        {
            Name = "ENDSTREAM",
            SessionId = command.SessionId,
            RequestId = command.RequestId
        };
        _outgoing.Writer.TryWrite(endStream);
    }

    private Task HandleCloseInferenceRequest(Command command)
    {
        var request = command.Payload.Unpack<CloseInferenceRequest>();
        var response = new Command
        {
            Name = nameof(CloseInferenceResponse),
            SessionId = command.SessionId,
            RequestId = command.RequestId,
            Payload = Any.Pack(new CloseInferenceResponse
            {
                InferenceId = request.InferenceId,
                SessionId = request.SessionId
            })
        };
        _outgoing.Writer.TryWrite(response);
        return Task.CompletedTask;
    }

    private Task HandleInferenceStatsRequest(Command command)
    {
        var request = command.Payload.Unpack<InferenceStatsRequest>();
        var response = new Command
        {
            Name = nameof(InferenceStatsResponse),
            SessionId = command.SessionId,
            RequestId = command.RequestId,
            Payload = Any.Pack(new InferenceStatsResponse
            {
                InferenceId = request.InferenceId,
                SessionId = request.SessionId,
                LastMessageTokenCount = 6,
                SessionTokenCount = 6,
                LastMessageComputeTimeMs = 100
            })
        };
        _outgoing.Writer.TryWrite(response);
        return Task.CompletedTask;
    }

    private Task HandleHeartbeatRequest(Command command)
    {
        // HeartbeatRequest is a fire-and-forget command from the ORC.
        // The real host processes settings from it but does not send a response.
        // No response needed.
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _outgoing.Writer.TryComplete();

        if (_stream != null)
        {
            try
            {
                await _stream.RequestStream.CompleteAsync();
            }
            catch { }
            _stream.Dispose();
        }

        if (_readTask != null)
        {
            try { await _readTask; } catch { }
        }

        _cts.Dispose();
    }
}
