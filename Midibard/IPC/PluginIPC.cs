using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Dalamud.Plugin.Ipc;

namespace MidiBard2.IPC
{
    internal class PluginIPC : IDisposable
    {
        public ICallGateProvider<string, object> MidiBardPlayingFileNamePub;

        public PluginIPC()
        {
            MidiBardPlayingFileNamePub = api.PluginInterface.GetIpcProvider<string, object>("MidiBard.CurrentPlayingFileName");
        }

        private void ReleaseUnmanagedResources()
        {
            MidiBardPlayingFileNamePub.UnregisterAction();
        }

        public void Dispose()
        {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        ~PluginIPC()
        {
            ReleaseUnmanagedResources();
        }
    }
}
