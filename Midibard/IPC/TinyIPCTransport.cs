using System;
using System.Linq;
using System.Threading.Tasks;

using TinyIpc.IO;
using TinyIpc.Messaging;

using static Dalamud.api;

namespace MidiBard.IPC;

internal class TinyIPCTransport : IIPCTransport
{
    private readonly TinyMessageBus _messageBus;
    private bool _disposed;

    public event EventHandler<MessageReceivedEventArgs> MessageReceived;
    public bool IsInitialized { get; private set; }

    public TinyIPCTransport()
    {
        try
        {
            const long maxFileSize = 1 << 24;
            _messageBus = new TinyMessageBus(new TinyMemoryMappedFile("Midibard.IPC", maxFileSize), true);
            _messageBus.MessageReceived += OnTinyMessageReceived;
            IsInitialized = true;
            PluginLog.Information("TinyIPC transport initialized successfully");
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "Failed to initialize TinyIPC transport");
            IsInitialized = false;
        }
    }

    private void OnTinyMessageReceived(object sender, TinyMessageReceivedEventArgs e)
    {
        if (_disposed) return;

        try
        {
            var message = e.Message.ToArray<byte>();
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message));
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Error processing received message in TinyIPC transport");
        }
    }

    public async Task<bool> PublishAsync(byte[] message)
    {
        if (!IsInitialized || _disposed) return false;

        try
        {
            await _messageBus.PublishAsync(message);
            return true;
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "Error publishing message via TinyIPC transport");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        if (_messageBus != null)
        {
            _messageBus.MessageReceived -= OnTinyMessageReceived;
            _messageBus.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
