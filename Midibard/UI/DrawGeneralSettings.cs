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
using System.IO;
using System.Numerics;

using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Logging;

using FFXIVClientStructs.FFXIV.Client.UI.Misc;

using ImGuiNET;

using MidiBard.IPC;
using MidiBard.Managers;
using MidiBard.Util;

using MidiBard2.Resources;

using static Dalamud.api;
using static ImGuiNET.ImGui;
using static MidiBard2.Resources.Language;

namespace MidiBard;

public partial class PluginUI
{
    private readonly string[] _toolTips = {
        "Off: Does not take over game's guitar tone control.",
        "Standard: Standard midi channel and ProgramChange handling, each channel will keep it's program state separately.",
        "Simple: Simple ProgramChange handling, ProgramChange event on any channel will change all channels' program state. (This is BardMusicPlayer's default behavior.)",
        "Override by track: Assign guitar tone manually for each track and ignore ProgramChange events.",
    };

    private bool _resetPlotWindowPosition = false;
    private bool showSettingsPanel;
    private bool CompensationEditWindowVisible;

    private unsafe void DrawSettingsWindow()
    {
        //var itemWidth = ImGuiHelpers.GlobalScale * 100;
        //SameLine(ImGuiUtil.GetWindowContentRegionWidth() / 2);

        ImGuiGroupPanel.BeginGroupPanel(setting_group_label_general_settings);
        {
            if (Checkbox(setting_label_auto_open_MidiBard, ref MidiBard.config.AutoOpenPlayerWhenPerforming))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(setting_label_auto_open_MidiBard);

            //Checkbox(Low_latency_mode, ref MidiBard.config.LowLatencyMode);
            //ImGuiUtil.ToolTip(low_latency_mode_tooltip);

            //ImGui.Checkbox(checkbox_auto_restart_listening, ref MidiBard.config.autoRestoreListening);
            //ImGuiUtil.ToolTip(checkbox_auto_restart_listening_tooltip);

            //ImGui.SameLine(ImGuiUtil.GetWindowContentRegionWidth() / 2);
            //ImGui.Checkbox("Auto listening new device".Localize(), ref MidiBard.config.autoStartNewListening);
            //ImGuiUtil.ToolTip("Auto start listening new midi input device when idle.".Localize());

            ColorEdit4(setting_label_theme_color, ref MidiBard.config.themeColor,
                ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar);
            //ImGuiUtil.ColorPickerButton(1000, label_theme_color, ref MidiBard.config.themeColor,
            //	ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar);
            //if (ImGui.ColorEdit4("Theme color".Localize(), ref MidiBard.config.themeColor,
            //	ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoInputs))

            if (IsItemClicked(ImGuiMouseButton.Right))
            {
                var @in = 0xFFFFA8A8;
                MidiBard.config.themeColor = ColorConvertU32ToFloat4(@in);
            }

            if (Combo(setting_label_select_ui_language, ref MidiBard.config.uiLang, uilangStrings,
                    uilangStrings.Length))
            {
                MidiBard.ConfigureLanguage(MidiBard.GetCultureCodeString((MidiBard.CultureCode)MidiBard.config.uiLang));
            }
        }
        ImGuiGroupPanel.EndGroupPanel();


        ImGuiGroupPanel.BeginGroupPanel(setting_group_label_ensemble_settings);

        if (Checkbox(setting_label_sync_clients, ref MidiBard.config.SyncClients))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_sync_clients);

        SameLine(ImGuiUtil.GetWindowContentRegionWidth() - GetFrameHeightWithSpacing() - ImGuiUtil.GetIconButtonSize((FontAwesomeIcon)0xF362).X);
        if (ImGuiUtil.IconButton((FontAwesomeIcon)0xF362, "syncbtn", icon_button_tooltip_sync_settings))
        {
            IPCHandles.SyncAllSettings();
            IPCHandles.SyncPlaylist();
        }

        if (Checkbox(setting_label_monitor_ensemble, ref MidiBard.config.MonitorOnEnsemble))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_monitor_ensemble);

        var cursorPosX = GetCursorPosX();
        var itemWidth = -cursorPosX + GetWindowContentRegionMin().X;
        ImGui.Checkbox(ensemble_config_Draw_ensemble_progress_indicator_on_visualizer, ref MidiBard.config.UseEnsembleIndicator);

        string[] values = new string[] { "None", "Legacy", "Default" };
        var current = (int)MidiBard.config.CompensationMode;
        BeginGroup();
        AlignTextToFramePadding();
        TextUnformatted("Ensemble Compensation Mode: ");
        SameLine();
        SetNextItemWidth(itemWidth);
        if (Combo("##Compensation Mode", ref current, values, values.Length))
        {
            MidiBard.config.CompensationMode = (Configuration.CompensationModes)current;
            IPCHandles.SyncAllSettings();
        }
        EndGroup();
        ImGuiUtil.ToolTip("""
            Ensemble instrument compensation mode selection:
            
            - None: No instrument delay compensation for instruments is performed during ensemble mode, which may result a lack of alignment between instruments during ensemble play. Choose this option only if your MIDI file already has instrument delay compensation.
            - Legacy: Allows you to adjust the delay compensation value for each instrument, but notes of different pitches for the same instrument may not align perfectly.
            - Default: New default instrument delay compensation mode, with different compensation times for notes of different pitches, useful for instruments such as clarinet and bass drum.
            """);

        if (MidiBard.config.CompensationMode == Configuration.CompensationModes.ByInstrument)
        {

            if (Button("Edit Instrument Compensations"))
            {
                CompensationEditWindowVisible ^= true;
            }
        }


        ImGuiGroupPanel.EndGroupPanel();

        ImGuiGroupPanel.BeginGroupPanel(setting_group_label_performance_settings);

        if (Checkbox(setting_label_auto_switch_instrument_bmp, ref MidiBard.config.bmpTrackNames))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_auto_switch_transpose_instrument_bmp_trackname);

        ImGui.Checkbox(setting_label_auto_switch_instrument_by_file_name, ref MidiBard.config.autoSwitchInstrumentBySongName);
        ImGuiUtil.ToolTip(setting_tooltip_label_auto_switch_instrument_by_file_name);

        Checkbox(setting_label_auto_transpose_by_file_name, ref MidiBard.config.autoTransposeBySongName);
        ImGuiUtil.ToolTip(setting_tooltip_auto_transpose_by_file_name);

        if (ImGui.Checkbox("Play Lyrics", ref MidiBard.config.playLyrics))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip("Choose this if you want to post lyrics.");

        bool pmdWasOn = MidiBard.config.playOnMultipleDevices;
        if (ImGui.Checkbox("Play on Multiple Devices", ref MidiBard.config.playOnMultipleDevices))
        {
            if (pmdWasOn || MidiBard.config.playOnMultipleDevices)
            {
                PartyChatCommand.SendPMD(MidiBard.config.playOnMultipleDevices);
            }
        }
        ImGuiUtil.ToolTip("Choose this if your bards are spread between different devices.");

        if (MidiBard.config.playOnMultipleDevices)
        {
            if (ImGui.Checkbox("Using File Sharing Services", ref MidiBard.config.usingFileSharingServices))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Using File Sharing Services like Google Drive to sync songs and performer settings.");
        }

        ImGui.Text($"Default Performer Folder:");
        var text = "Change";
        var size = ImGuiHelpers.GetButtonSize(text);
        SameLine(GetWindowWidth() - 2 * cursorPosX - size.X);
        if (ImGui.Button(text))
        {
            RunSetDefaultPerformerFolderImGui();
        }

        ImGui.TextUnformatted(Path.ChangeExtension(MidiBard.config.defaultPerformerFolder, null).EllipsisString(40));

        ImGuiGroupPanel.EndGroupPanel();
        Spacing();
    }

    private void RunSetDefaultPerformerFolderImGui()
    {
        fileDialogManager.OpenFolderDialog("Set Default Performer Folder", (b, filePath) =>
        {
            PluginLog.Debug($"dialog result: {b}\n{string.Join("\n", filePath)}");
            if (b)
            {
                MidiFileConfigManager.SetDefaultPerformerFolder(filePath);
                MidiBard.SaveConfig();
                IPCHandles.SyncAllSettings();
                IPCHandles.UpdateDefaultPerformer();
            }
        }, MidiBard.config.defaultPerformerFolder);
    }

    private void DrawCompensationEditWindow()
    {
        if (!CompensationEditWindowVisible) return;
        if (Begin("Instrument Delay Compensation", ref CompensationEditWindowVisible))
        {
            if (BeginTable("ins", 3, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
            {
                TableSetupColumn("##InstrumentImage", ImGuiTableColumnFlags.WidthFixed);
                TableSetupColumn("Instrument", ImGuiTableColumnFlags.WidthFixed);
                TableSetupColumn("Compensation(ms)", ImGuiTableColumnFlags.WidthStretch);
                TableHeadersRow();
                foreach (var instrument in MidiBard.Instruments)
                {
                    if (instrument.Row.RowId == 0) continue;
                    TableNextColumn();
                    Image(instrument.IconTextureWrap.GetWrapOrEmpty().ImGuiHandle, new Vector2(GetFrameHeight()));
                    TableNextColumn();
                    AlignTextToFramePadding();
                    TextUnformatted(instrument.FFXIVDisplayName);
                    TableNextColumn();
                    SetNextItemWidth(-1);
                    var compensationMs = MidiBard.config.LegacyInstrumentCompensation[(int)instrument.Row.RowId];
                    if (InputInt($"##{instrument.Row.RowId}", ref compensationMs, 1, 1))
                    {
                        compensationMs = compensationMs.Clamp(0, 500);
                        MidiBard.config.LegacyInstrumentCompensation[(int)instrument.Row.RowId] = compensationMs;
                        IPCHandles.SyncAllSettings();
                    }
                }
                EndTable();
            }

            if (Button("Reset to default"))
            {
                MidiBard.config.LegacyInstrumentCompensation = EnsembleManager.GetCompensationAver();
                IPCHandles.SyncAllSettings();
            }
        }
        End();
    }
}
