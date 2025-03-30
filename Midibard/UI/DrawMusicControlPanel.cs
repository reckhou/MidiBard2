// Copyright (C) 2022 akira0245
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
// 
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see https://github.com/akira0245/MidiBard/blob/master/LICENSE.
// 
// This code is written by akira0245 and was originally used in the MidiBard project. Any usage of this code must prominently credit the author, akira0245, and indicate that it was originally used in the MidiBard project.

using System;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Logging;

using ImGuiNET;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl;
using MidiBard.IPC;
using MidiBard.Managers.Ipc;
using MidiBard.Util;
using MidiBard.Util.Lyrics;

using static Dalamud.api;
using static ImGuiNET.ImGui;
using static MidiBard.ImGuiUtil;
using static MidiBard2.Resources.Language;

namespace MidiBard;

public partial class PluginUI
{
    private void DrawPanelMusicControl()
    {
        //ManualDelay();
        if (Lrc.LrcLoaded())
        {
            LRCDeltaTime();
        }

        var inputDevices = InputDeviceManager.Devices;
        if (BeginCombo(setting_label_midi_input_device, InputDeviceManager.CurrentInputDevice.DeviceName()))
        {
            if (Selectable("None##device", InputDeviceManager.CurrentInputDevice is null))
            {
                InputDeviceManager.SetDevice(null);
            }

            for (int i = 0; i < inputDevices.Length; i++)
            {
                var device = inputDevices[i];
                if (Selectable($"{device.Name}##{i}", device.Name == InputDeviceManager.CurrentInputDevice?.Name))
                {
                    InputDeviceManager.SetDevice(device);
                }
            }

            EndCombo();
        }
        if (IsItemHovered() && IsMouseClicked(ImGuiMouseButton.Right)) InputDeviceManager.SetDevice(null);
        ImGuiUtil.ToolTip(setting_tooltip_select_input_device);
        //-------------------


        ComboBoxSwitchInstrument();

        SliderProgress();

        var itemWidth = ImGuiHelpers.GlobalScale * 100;
        if (InputFloat(setting_label_set_play_speed, ref MidiBard.config.PlaySpeed, 0.1f, 0.5f, GetBpmString(), ImGuiInputTextFlags.AutoSelectAll)) SetSpeed();
        if (IsItemHovered() && IsMouseClicked(ImGuiMouseButton.Right))
        {
            MidiBard.config.PlaySpeed = 1;
            SetSpeed();
        }
        ToolTip(setting_tooltip_set_speed);

        //-------------------
        SetNextItemWidth(itemWidth);
        if (InputFloat(setting_label_song_delay, ref MidiBard.config.SecondsBetweenTracks, 0.5f, 0.5f, $" {MidiBard.config.SecondsBetweenTracks:f2} s", ImGuiInputTextFlags.AutoSelectAll))
        {
            MidiBard.config.SecondsBetweenTracks = Math.Max(0, MidiBard.config.SecondsBetweenTracks);
            IPCHandles.SyncAllSettings();
        }
        if (IsItemClicked(ImGuiMouseButton.Right))
        {
            MidiBard.config.SecondsBetweenTracks = 3;
            IPCHandles.SyncAllSettings();
        }
        ToolTip(setting_tooltip_song_delay);
        //-------------------
        SameLine(ImGuiUtil.GetWindowContentRegionWidth() / 2f);
        SetNextItemWidth(itemWidth);
        if (InputInt(setting_label_transpose_all, ref MidiBard.config.TransposeGlobal, 12))
        {
            MidiBard.config.SetTransposeGlobal(MidiBard.config.TransposeGlobal);
            IPC.IPCHandles.GlobalTranspose(MidiBard.config.TransposeGlobal);
        }
        if (IsItemHovered() && IsMouseClicked(ImGuiMouseButton.Right))
        {
            MidiBard.config.SetTransposeGlobal(0);
            IPC.IPCHandles.GlobalTranspose(MidiBard.config.TransposeGlobal);
        }
        ToolTip(setting_tooltip_transpose_all);


        //-------------------
        if (Checkbox(setting_label_auto_adapt_notes, ref MidiBard.config.AdaptNotesOOR))
        {
            IPCHandles.SyncAllSettings();
        }
        ToolTip(setting_tooltip_auto_adapt_notes);
        //-------------------
        SameLine(ImGuiUtil.GetWindowContentRegionWidth() / 2f);
        SetNextItemWidth(itemWidth);
        if (ImGuiUtil.EnumCombo(setting_label_tone_mode, ref MidiBard.config.GuitarToneMode, _toolTips))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_tone_mode);

        //-------------------

        const uint DiscordColor = 0x00F26558;
        PushStyleColor(ImGuiCol.Button, 0xFF000000 | DiscordColor);
        PushStyleColor(ImGuiCol.ButtonActive, 0xDD000000 | DiscordColor);
        PushStyleColor(ImGuiCol.ButtonHovered, 0xAA000000 | DiscordColor);
        if (Button(" Join Discord "))
        {
            try
            {
                Extensions.ExecuteCmd("https://discord.gg/ejGt2mXHJM");
            }
            catch (Exception e)
            {
                PluginLog.Error(e.ToString());
            }
        }

        PopStyleColor(3);

        SameLine();

        const uint KofiColor = 0x005E5BFF;
        PushStyleColor(ImGuiCol.Button, 0xFF000000 | KofiColor);
        PushStyleColor(ImGuiCol.ButtonActive, 0xDD000000 | KofiColor);
        PushStyleColor(ImGuiCol.ButtonHovered, 0xAA000000 | KofiColor);
        if (Button(" Support us on Ko-fi! "))
        {
            try
            {
                Extensions.ExecuteCmd("https://ko-fi.com/midibard");
            }
            catch (Exception e)
            {
                PluginLog.Error(e.ToString());
            }
        }

        PopStyleColor(3);

        SameLine();

        const uint WebsiteColor = 0x00C7A416;
        PushStyleColor(ImGuiCol.Button, 0xFF000000 | WebsiteColor);
        PushStyleColor(ImGuiCol.ButtonActive, 0xDD000000 | WebsiteColor);
        PushStyleColor(ImGuiCol.ButtonHovered, 0xAA000000 | WebsiteColor);
        if (Button(" MidiBard.org "))
        {
            try
            {
                Extensions.ExecuteCmd("https://midibard.org/");
            }
            catch (Exception e)
            {
                PluginLog.Error(e.ToString());
            }
        }

        PopStyleColor(3);
    }

    private static void SetSpeed()
    {
        MidiBard.config.PlaySpeed = MidiBard.config.PlaySpeed.Clamp(0.1f, 10f);
        var currenttime = MidiBard.CurrentPlayback?.GetCurrentTime(TimeSpanType.Midi);
        if (currenttime is not null)
        {
            MidiBard.CurrentPlayback.Speed = MidiBard.config.PlaySpeed;
            MidiBard.CurrentPlayback?.MoveToTime(currenttime);
        }

        if (api.PartyList.IsPartyLeader())
            IPC.IPCHandles.PlaybackSpeed(MidiBard.config.PlaySpeed);
    }

    private static string GetBpmString()
    {
        Tempo bpm = null;
        var currentTime = MidiBard.CurrentPlayback?.GetCurrentTime(TimeSpanType.Midi);
        if (currentTime != null)
        {
            bpm = MidiBard.CurrentPlayback?.TempoMap?.GetTempoAtTime(currentTime);
        }

        var label = $" {MidiBard.config.PlaySpeed:F2}";

        if (bpm != null) label += $" ({bpm.BeatsPerMinute * MidiBard.config.PlaySpeed:F1} bpm)";
        return label;
    }

    private static void SliderProgress()
    {
        if (MidiBard.CurrentPlayback != null)
        {
            var currentTime = MidiBard.CurrentPlayback.GetCurrentTime<MetricTimeSpan>();
            var duration = MidiBard.CurrentPlayback.GetDuration<MetricTimeSpan>();
            float progress;
            try
            {
                progress = (float)currentTime.Divide(duration);
            }
            catch (Exception e)
            {
                progress = 0;
            }

            if (SliderFloat(setting_label_set_progress, ref progress, 0, 1,
                    $"{(currentTime.Hours != 0 ? currentTime.Hours + ":" : "")}{currentTime.Minutes:00}:{currentTime.Seconds:00}",
                    ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.NoRoundToFormat))
            {
                MidiPlayerControl.SetTime(duration.Multiply(progress));
                IPC.IPCHandles.SetPlaybackTime((MetricTimeSpan)duration.Multiply(progress));
            }

            if (IsItemHovered() && IsMouseClicked(ImGuiMouseButton.Right))
            {
                MidiPlayerControl.SetTime(duration.Multiply(0));
                IPC.IPCHandles.SetPlaybackTime(TimeSpan.Zero);
            }
        }
        else
        {
            float zeroprogress = 0;
            SliderFloat(setting_label_set_progress, ref zeroprogress, 0, 1, "0:00", ImGuiSliderFlags.NoInput);
        }

        ToolTip(setting_tooltip_set_progress);
    }

    private static int UIcurrentInstrument;
    private static void ComboBoxSwitchInstrument()
    {
        UIcurrentInstrument = MidiBard.CurrentInstrument;
        if (MidiBard.PlayingGuitar)
        {
            UIcurrentInstrument = MidiBard.AgentPerformance.CurrentGroupTone + MidiBard.guitarGroup[0];
        }

        if (BeginCombo(setting_label_select_instrument, MidiBard.InstrumentStrings[UIcurrentInstrument], ImGuiComboFlags.HeightLarge))
        {
            GetWindowDrawList().ChannelsSplit(2);
            for (int i = 0; i < MidiBard.Instruments.Length; i++)
            {
                var instrument = MidiBard.Instruments[i];
                GetWindowDrawList().ChannelsSetCurrent(1);
                Image(instrument.IconTextureWrap.GetWrapOrEmpty().ImGuiHandle, new Vector2(GetTextLineHeightWithSpacing()));
                SameLine();
                GetWindowDrawList().ChannelsSetCurrent(0);
                AlignTextToFramePadding();
                if (Selectable($"{instrument.InstrumentString}##{i}", UIcurrentInstrument == i, ImGuiSelectableFlags.SpanAllColumns))
                {
                    UIcurrentInstrument = i;
                    SwitchInstrument.SwitchToContinue((uint)i);
                }
            }
            GetWindowDrawList().ChannelsMerge();
            EndCombo();
        }

        //if (ImGui.Combo("Instrument".Localize(), ref UIcurrentInstrument, MidiBard.InstrumentStrings,
        //        MidiBard.InstrumentStrings.Length, 20))
        //{
        //    SwitchInstrument.SwitchToContinue((uint)UIcurrentInstrument);
        //}

        ToolTip(setting_tooltip_select_instrument);

        if (IsItemHovered() && IsMouseClicked(ImGuiMouseButton.Right))
        {
            SwitchInstrument.SwitchToContinue(0);
            MidiPlayerControl.Pause();
        }
    }

    private static void ManualDelay()
    {
        if (ImGui.Button("-10ms"))
        {
            MidiPlayerControl.ChangeDeltaTime(-10);
        }
        ImGui.SameLine();
        if (ImGui.Button("-2ms"))
        {
            MidiPlayerControl.ChangeDeltaTime(-2);
        }
        ImGui.SameLine();
        if (ImGui.Button("+2ms"))
        {
            MidiPlayerControl.ChangeDeltaTime(2);
        }
        ImGui.SameLine();
        if (ImGui.Button("+10ms"))
        {
            MidiPlayerControl.ChangeDeltaTime(10);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Manual Sync: " + $"{MidiPlayerControl.playDeltaTime} ms");
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            MidiPlayerControl.ChangeDeltaTime(-MidiPlayerControl.playDeltaTime);
        }
        ToolTip("Delay time(ms) add on top of current progress to help sync between bards.");
    }

    private static void LRCDeltaTime()
    {
        if (ImGui.Button("-50ms"))
        {
            Lrc.ChangeLRCDeltaTime(-50);
        }
        ImGui.SameLine();
        if (ImGui.Button("+50ms"))
        {
            Lrc.ChangeLRCDeltaTime(50);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("LRC Sync: " + $"{Lrc.LRCDeltaTime} ms");
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            Lrc.ChangeLRCDeltaTime(-Lrc.LRCDeltaTime);
        }
        ToolTip("Delay time(ms) add on top of lyrics.");
    }

}
