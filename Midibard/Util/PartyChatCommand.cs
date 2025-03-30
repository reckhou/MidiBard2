using System;
using System.Diagnostics;
using System.Threading.Tasks;

using Dalamud;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Logging;
using Dalamud.Plugin;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl;
using MidiBard.IPC;
using MidiBard.Managers;
using MidiBard.Managers.Ipc;
using MidiBard.Util;

using static Dalamud.api;
using static MidiBard.MidiBard;

namespace MidiBard
{
    internal class PartyChatCommand
    {
        internal static void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            if (isHandled)
                return;

            if (type != XivChatType.Party)
            {
                return;
            }

            string[] strings = message.ToString().Split(' ');
            if (strings.Length < 1)
            {
                return;
            }

            string cmd = strings[0].ToLower();

            if (cmd == "playonmultipledevices" || cmd == "pmd")
            {
                if (strings.Length < 2)
                {
                    return;
                }

                if (strings[1].ToLower() == "on")
                {
                    MidiBard.config.playOnMultipleDevices = true;
                }
                else if (strings[1].ToLower() == "off")
                {
                    MidiBard.config.playOnMultipleDevices = false;
                }
            }

            if (!MidiBard.config.playOnMultipleDevices)
            {
                return;
            }

            if (cmd == "switchto") // switchto + <song number in playlist>
            {
                if (strings.Length < 2)
                {
                    return;
                }

                int number = -1;
                bool success = Int32.TryParse(strings[1], out number);
                if (!success)
                {
                    return;
                }

                MidiPlayerControl.StopLrc();
                PlaylistManager.LoadPlayback(number - 1);
                Ui.Open();
            }
            else if (cmd == "reloadconfig") // reload the config
            {
                IPCHandles.SyncAllSettings();
            }
            else if (cmd == "reloadplaylist")
            {
                // hacky way to reload the opening play list
                PlaylistManager.CurrentContainer = PlaylistManager.LoadLastPlaylist();
            }
            else if (cmd == "updatedefaultperformer")
            {
                MidiFileConfigManager.LoadDefaultPerformer();
            }
            else if (cmd == "updateinstrument")
            {
                UpdateInstrument();
            }
            else if (cmd == "close") // switch off the instrument
            {
                MidiPlayerControl.Stop();
                SwitchInstrument.SwitchToAsync(0);
            }
            else if (cmd == "speed")
            {
                if (strings.Length < 2)
                {
                    return;
                }

                float number = -1;
                bool success = float.TryParse(strings[1], out number);
                if (!success)
                {
                    return;
                }

                MidiBard.config.PlaySpeed = Math.Max(0.1f, number);
            }
            else if (cmd == "transpose")
            {
                if (strings.Length < 2)
                {
                    return;
                }

                int number = -1;
                bool success = Int32.TryParse(strings[1], out number);
                if (!success)
                {
                    return;
                }

                MidiBard.config.SetTransposeGlobal(number);
            }
        }

        internal static void SendClose()
        {
            if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2)
            {
                return;
            }

            Chat.SendMessage("/p close");
        }

        internal static void SendSwitchTo(int songNumber)
        {
            if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2)
            {
                return;
            }

            Chat.SendMessage($"/p switchto {songNumber}");
        }

        internal static void SendPMD(bool isOn)
        {
            if (api.PartyList.Length < 2)
            {
                return;
            }

            var str = isOn ? "on" : "off";
            Chat.SendMessage($"/p pmd {str}");
        }

        internal static void SendReloadPlaylist()
        {
            if (api.PartyList.Length < 2)
            {
                return;
            }

            Chat.SendMessage($"/p reloadplaylist");
        }

        internal static void SendUpdateDefaultPerformer()
        {
            if (api.PartyList.Length < 2)
            {
                return;
            }

            Chat.SendMessage($"/p updatedefaultperformer");
        }

        internal static void SendUpdateInstrument()
        {
            if (api.PartyList.Length < 2)
            {
                return;
            }

            Chat.SendMessage($"/p updateinstrument");
        }

        private static void UpdateInstrument()
        {
            // updates midifile config and instruments
            // code copied from IPCHandles.cs
            if (CurrentPlayback == null)
            {
                return;
            }

            var dbTracks = MidiBard.CurrentPlayback.MidiFileConfig.Tracks;
            var trackStatus = MidiBard.config.TrackStatus;
            for (var i = 0; i < dbTracks.Count; i++)
            {
                try
                {
                    trackStatus[i].Enabled = dbTracks[i].Enabled && MidiFileConfig.GetFirstCidInParty(dbTracks[i]) == (long)api.ClientState.LocalContentId;
                    trackStatus[i].Transpose = dbTracks[i].Transpose;
                    trackStatus[i].Tone = Util.InstrumentHelper.GetGuitarTone(dbTracks[i].Instrument);
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, $"error when updating track {i}");
                }
            }

            uint? instrument = null;
            foreach (var cur in MidiBard.CurrentPlayback.MidiFileConfig.Tracks)
            {
                if (cur.Enabled && MidiFileConfig.IsCidOnTrack((long)api.ClientState.LocalContentId, cur))
                {
                    instrument = (uint?)cur.Instrument;
                    break;
                }
            }

            if (instrument != null)
                SwitchInstrument.SwitchToContinue((uint)instrument);

            PluginLog.Debug($"Instrument: {instrument}");
        }
    }
}
