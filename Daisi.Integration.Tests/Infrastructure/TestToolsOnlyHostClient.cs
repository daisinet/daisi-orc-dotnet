using Daisi.Protos.V1;
using Daisi.SDK.Models;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using System.Threading.Channels;

namespace Daisi.Integration.Tests.Infrastructure;

/// <summary>
/// Simulates a tools-only host that connects to the ORC and handles
/// ExecuteToolRequest commands (tool delegation). Does not handle inference.
/// </summary>
public class TestToolsOnlyHostClient : IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly string _clientKey;
    private readonly CancellationTokenSource _cts = new();
    private AsyncDuplexStreamingCall<Command, Command>? _stream;
    private Task? _readTask;

    private readonly Channel<Command> _outgoing = Channel.CreateUnbounded<Command>();

    public bool IsConnected { get; private set; }
    public List<string> ReceivedCommandNames { get; } = new();
    public List<ExecuteToolRequest> ReceivedToolRequests { get; } = new();

    public TestToolsOnlyHostClient(GrpcChannel channel, string clientKey)
    {
        _channel = channel;
        _clientKey = clientKey;
    }

    public async Task ConnectAsync()
    {
        var client = new HostCommandsProto.HostCommandsProtoClient(_channel);

        var headers = new Metadata
        {
            { DaisiStaticSettings.ClientKeyHeader, _clientKey }
        };

        _stream = client.Open(headers, cancellationToken: _cts.Token);

        // Send EnvironmentRequest (like real host)
        var envCommand = new Command
        {
            Name = nameof(EnvironmentRequest),
            Payload = Any.Pack(new EnvironmentRequest
            {
                OperatingSystem = "Android",
                OperatingSystemVersion = "14",
                AppVersion = "1.0.0"
            })
        };
        await _stream.RequestStream.WriteAsync(envCommand);

        _readTask = Task.Run(ReadIncomingCommandsAsync);
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
                    Console.WriteLine($"TestToolsOnlyHostClient: Error handling {command.Name}: {ex.Message}");
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

    private Task HandleCommandAsync(Command command)
    {
        switch (command.Name)
        {
            case nameof(ExecuteToolRequest):
                return HandleExecuteToolRequest(command);

            case nameof(HeartbeatRequest):
                return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    private Task HandleExecuteToolRequest(Command command)
    {
        var request = command.Payload.Unpack<ExecuteToolRequest>();
        ReceivedToolRequests.Add(request);

        var response = new Command
        {
            Name = nameof(ExecuteToolResponse),
            RequestId = command.RequestId,
            Payload = Any.Pack(new ExecuteToolResponse
            {
                Success = true,
                Output = $"Tool {request.ToolId} executed successfully",
                OutputMessage = "OK",
                OutputFormat = "text"
            })
        };
        _outgoing.Writer.TryWrite(response);
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
