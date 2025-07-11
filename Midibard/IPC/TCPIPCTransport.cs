using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using static Dalamud.api;

namespace MidiBard.IPC;

internal class TCPIPCTransport : IIPCTransport
{
    private readonly int _port;
    private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
    private TcpListener _server;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private bool _disposed;

    public event EventHandler<MessageReceivedEventArgs> MessageReceived;
    public bool IsInitialized { get; private set; }

    public TCPIPCTransport(int port = 21043)
    {
        _port = FindAvailablePort(port);
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            InitializeServer();
            ConnectToExistingInstances();
            IsInitialized = true;
            PluginLog.Information($"TCP transport initialized successfully on port {_port}");
        }
        catch (Exception e)
        {
            PluginLog.Error(e, $"Failed to initialize TCP transport on port {_port}");
            IsInitialized = false;
        }
    }

    private void InitializeServer()
    {
        _server = new TcpListener(IPAddress.Loopback, _port);
        _server.Start();

        // Start accepting connections
        _ = Task.Run(async () =>
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _server.AcceptTcpClientAsync();
                    var clientId = tcpClient.Client.RemoteEndPoint.ToString();
                    _clients[clientId] = tcpClient;

                    PluginLog.Debug($"TCP client connected: {clientId}");

                    // Handle client messages
                    _ = Task.Run(() => HandleClientMessages(tcpClient, clientId), _cancellationTokenSource.Token);
                }
                catch (ObjectDisposedException)
                {
                    // Server was disposed, exit gracefully
                    break;
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, "Error accepting TCP client connection");
                }
            }
        }, _cancellationTokenSource.Token);
    }


    private async Task HandleClientMessages(TcpClient client, string clientId)
    {
        try
        {
            var stream = client.GetStream();
            var buffer = new byte[4]; // For message length prefix

            while (!_cancellationTokenSource.Token.IsCancellationRequested && client.Connected)
            {
                // Read message length (4 bytes)
                int bytesRead = 0;
                while (bytesRead < 4)
                {
                    int read = await stream.ReadAsync(buffer, bytesRead, 4 - bytesRead, _cancellationTokenSource.Token);
                    if (read == 0)
                    {
                        // Client disconnected
                        return;
                    }
                    bytesRead += read;
                }

                int messageLength = BitConverter.ToInt32(buffer, 0);
                if (messageLength <= 0 || messageLength > (1 << 24)) // Max 16MB message
                {
                    PluginLog.Warning($"Invalid message length received from {clientId}: {messageLength}");
                    continue;
                }

                // Read message payload
                var messageBuffer = new byte[messageLength];
                bytesRead = 0;
                while (bytesRead < messageLength)
                {
                    int read = await stream.ReadAsync(messageBuffer, bytesRead, messageLength - bytesRead, _cancellationTokenSource.Token);
                    if (read == 0)
                    {
                        // Client disconnected
                        return;
                    }
                    bytesRead += read;
                }

                // Process all received messages (echo prevention handled at application level)
                MessageReceived?.Invoke(this, new MessageReceivedEventArgs(messageBuffer));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception e)
        {
            PluginLog.Error(e, $"Error handling messages from TCP client {clientId}");
        }
        finally
        {
            // Clean up client connection
            _clients.TryRemove(clientId, out _);
            client?.Close();
            PluginLog.Debug($"TCP client disconnected: {clientId}");
        }
    }

    public async Task<bool> PublishAsync(byte[] message)
    {
        if (!IsInitialized || _disposed) return false;

        try
        {
            var messageLength = BitConverter.GetBytes(message.Length);
            var success = false;

            // Send to all connected clients
            foreach (var kvp in _clients)
            {
                var client = kvp.Value;
                if (client.Connected)
                {
                    try
                    {
                        var stream = client.GetStream();
                        await stream.WriteAsync(messageLength, 0, 4, _cancellationTokenSource.Token);
                        await stream.WriteAsync(message, 0, message.Length, _cancellationTokenSource.Token);
                        await stream.FlushAsync(_cancellationTokenSource.Token);
                        success = true;
                    }
                    catch (Exception e)
                    {
                        PluginLog.Error(e, $"Error sending message to TCP client {kvp.Key}");
                        // Remove disconnected client
                        _clients.TryRemove(kvp.Key, out _);
                        client?.Close();
                    }
                }
            }

            return success;
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "Error publishing message via TCP transport");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            _cancellationTokenSource?.Cancel();

            // Close all client connections
            foreach (var client in _clients.Values)
            {
                client?.Close();
            }
            _clients.Clear();

            // Stop the server
            _server?.Stop();

            _cancellationTokenSource?.Dispose();
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "Error disposing TCP transport");
        }

        GC.SuppressFinalize(this);
    }

    private int FindAvailablePort(int startPort)
    {
        for (int port = startPort; port < startPort + 100; port++)
        {
            if (IsPortAvailable(port))
            {
                return port;
            }
        }

        // If no port is available in the range, use the original port and let it fail
        PluginLog.Warning($"No available ports found in range {startPort}-{startPort + 100}, using {startPort}");
        return startPort;
    }

    private bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ConnectToExistingInstances()
    {
        // Try to connect to other instances running on nearby ports
        _ = Task.Run(async () =>
        {
            try
            {
                int basePort = _port - (_port % 100); // Round down to nearest 100
                int maxPort = basePort + 100;

                for (int port = basePort; port < maxPort; port++)
                {
                    if (port == _port) continue; // Skip our own port

                    try
                    {
                        var client = new TcpClient();
                        await client.ConnectAsync(IPAddress.Loopback, port);
                        var clientId = $"instance-{port}";
                        _clients[clientId] = client;

                        PluginLog.Debug($"Connected to existing TCP instance: {clientId}");

                        // Handle messages from this instance
                        _ = Task.Run(() => HandleClientMessages(client, clientId), _cancellationTokenSource.Token);
                    }
                    catch
                    {
                        // Port not available or no server running, continue
                    }
                }
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "Error connecting to existing TCP instances");
            }
        }, _cancellationTokenSource.Token);
    }
}
