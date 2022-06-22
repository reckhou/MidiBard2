﻿using Dalamud.Interface.Internal.Notifications;
using Dalamud.Logging;
using MidiBard.HSC;
using MidiBard.IPC.SharedMemory;
using SharedMemory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace MidiBard
{
    public partial class MidiBard
    {
        private static MessageHandler msgHandler;
        private static bool hscmClientHandlerRunning;
        private static EventWaitHandle hscmWaitHandle;
        private static EventWaitHandle waitHandle;

        private static void StopClientMessageHandler()
        {
            hscmClientHandlerRunning = false;
        }

        private static void StartClientMessageHander()
        {
            try
            {
                hscmClientHandlerRunning = true;

                bool opened = Common.IPC.SharedMemory.CreateOrOpen();

                if (!opened)
                {
                    ImGuiUtil.AddNotification(NotificationType.Error, $"Cannot connect to HSCM");
                    PluginLog.Error($"An error occured opening or accessing shared memory.");
                    return;
                }
  
                msgHandler = new IPC.SharedMemory.MessageHandler();

                msgHandler.ChangeSongMessageReceived += MsgHandler_ChangeSongMessageReceived;
                msgHandler.ReloadPlaylistMessageReceived += MsgHandler_ReloadPlaylistMessageReceived;
                msgHandler.ReloadPlaylistSettingsMessageReceived += MsgHandler_ReloadPlaylistSettingsMessageReceived;
                msgHandler.SwitchInstrumentsMessageReceived += MsgHandler_SwitchInstrumentsMessageReceived;
                msgHandler.RestartHscmOverrideMessageReceived += MsgHandler_RestartHscmOverrideMessageReceived;
                msgHandler.ClosePerformanceMessageReceived += MsgHandler_ClosePerformanceMessageReceived;

                PluginLog.Information($"Started client message event handling.");

                while (hscmClientHandlerRunning && (DalamudApi.api.ClientState.IsLoggedIn || Configuration.config.hscmOfflineTesting))
                {
                    if (!hscmClientHandlerRunning)
                    {
                        PluginLog.Information($"Stopping client message event handler.");
                        // Clean up here, then...
                        //cancelToken.ThrowIfCancellationRequested();
                        break;
                    }

                    PluginLog.Information($"Client waiting for message.");

                    bool success = waitHandle.WaitOne();
                    PluginLog.Information($"Client message sent.");

                    if (success)
                        ConsumeMessage();
                    else
                    {
                        PluginLog.Error($"An error occured waiting on event signal");
                        break;
                    }

                    success = waitHandle.Reset();
                    if (!success)
                    {
                        PluginLog.Error($"An error occured when releasing event wait handle");
                        break;
                    }
                    Thread.Sleep(10);
                }

            }
            catch (Exception ex)
            {
                //ImGuiUtil.AddNotification(NotificationType.Error, $"Cannot connect to HSCM");
                PluginLog.Error($"An error occured when handling messages: {ex.Message}");
                //StopClientMessageHandler();
            }
        }

        private static void ConsumeMessage()
        {
            try
            {

                if (!Configuration.config.useHscmOverride)
                    return;

                int[] buffer = new int[2];
                int total = Common.IPC.SharedMemory.Read(buffer, 2);
                PluginLog.Information($"Buffer: {buffer[0]} {buffer[1]}");
                if (total == 0)
                {
                    PluginLog.Error($"Could not read from shared memory");
                    return;
                }

                if (buffer[0] == 0)
                {
                    PluginLog.Information($"Shared memory buffer has been cleared");
                    return;
                }

                msgHandler?.HandleMessage((IPC.SharedMemory.MessageType)buffer[0], buffer[1]);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error processing shared memory buffer: {ex.Message}");
            }
        }

    }
}
