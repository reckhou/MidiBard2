using System;
using System.Threading.Tasks;

namespace MidiBard.IPC;

public interface IIPCTransport : IDisposable
{
    event EventHandler<MessageReceivedEventArgs> MessageReceived;
    Task<bool> PublishAsync(byte[] message);
    bool IsInitialized { get; }
}

public class MessageReceivedEventArgs : EventArgs
{
    public byte[] Message { get; set; }

    public MessageReceivedEventArgs(byte[] message)
    {
        Message = message;
    }
}

public class NullIPCTransport : IIPCTransport
{
    public event EventHandler<MessageReceivedEventArgs> MessageReceived;
    public bool IsInitialized => false;

    public Task<bool> PublishAsync(byte[] message)
    {
        return Task.FromResult(false);
    }

    public void Dispose()
    {
    }
}
