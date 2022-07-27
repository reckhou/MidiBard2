﻿using System;
using Dalamud.Interface;
using Dalamud.Logging;
using Dalamud.Plugin;
using ImGuiNET;
using MidiBard.Control.MidiControl;
using MidiBard.Managers;
using MidiBard.Resources;
using MidiBard.IPC;
using MidiBard.Managers.Ipc;
using static MidiBard.ImGuiUtil;

namespace MidiBard;

public partial class PluginUI
{
	private unsafe void DrawButtonVisualization()
	{
		ImGui.SameLine();
		var color = MidiBard.config.PlotTracks ? MidiBard.config.themeColor : *ImGui.GetStyleColorVec4(ImGuiCol.Text);
		if (IconButton((FontAwesomeIcon)0xf008, "miniplayer", Language.visualization_tooltip,
				ImGui.ColorConvertFloat4ToU32(color)))
			MidiBard.config.PlotTracks ^= true;
		if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
		{
			_resetPlotWindowPosition = true;
		}
	}

	private unsafe void DrawButtonShowSettingsPanel()
	{
		ImGui.SameLine();
		ImGui.PushStyleColor(ImGuiCol.Text, MidiBard.Ui.showSettingsPanel ? MidiBard.config.themeColor : *ImGui.GetStyleColorVec4(ImGuiCol.Text));

		if (IconButton(FontAwesomeIcon.Cog, "btnsettingp")) showSettingsPanel ^= true;

		ImGui.PopStyleColor();
		ToolTip(Language.Settings_panel);
	}

	private unsafe void DrawButtonShowEnsembleControl()
	{
		if (DalamudApi.api.PartyList.Length < 2 || !DalamudApi.api.PartyList.IsPartyLeader())
        {
			return;
        }

		ImGui.SameLine();
		ImGui.PushStyleColor(ImGuiCol.Text, MidiBard.Ui.ShowEnsembleControlWindow ? MidiBard.config.themeColor : *ImGui.GetStyleColorVec4(ImGuiCol.Text));

		if (IconButton((FontAwesomeIcon)0xF0C0, "btnensemble")) ShowEnsembleControlWindow ^= true;

		ImGui.PopStyleColor();
		ToolTip(Language.button_ensemble_panel);
	}

	private unsafe void DrawButtonPlayPause()
	{
		var PlayPauseIcon = MidiBard.IsPlaying ? (MidiBard.AgentMetronome.EnsembleModeRunning ? FontAwesomeIcon.Stop : FontAwesomeIcon.Pause) : FontAwesomeIcon.Play;
		if (ImGuiUtil.IconButton(PlayPauseIcon, "playpause"))
		{
			if (MidiBard.AgentMetronome.EnsembleModeRunning)
			{
				// stops ensemble instead of pausing one client
				StopEnsemble();
			}
			else
			{
				PluginLog.Debug($"PlayPause pressed. wasplaying: {MidiBard.IsPlaying}");
				MidiPlayerControl.PlayPause();
			}
		}
	}

	private unsafe void DrawButtonStop()
	{
		ImGui.SameLine();
		if (IconButton(FontAwesomeIcon.Stop, "btnstop"))
		{
			if (FilePlayback.IsWaiting)
			{
				FilePlayback.CancelWaiting();
			}
			else
			{
				MidiPlayerControl.Stop();
				IPCHandles.UpdateInstrument(false);
			}
		}
	}

	private unsafe void DrawButtonFastForward()
	{
		ImGui.SameLine();
		if (IconButton(((FontAwesomeIcon)0xf050), "btnff"))
		{
			MidiPlayerControl.Next();
		}

		if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
		{
			MidiPlayerControl.Prev();
		}
	}

	private unsafe void DrawButtonPlayMode()
	{
		ImGui.SameLine();
		FontAwesomeIcon icon = (PlayMode)MidiBard.config.PlayMode switch
		{
			PlayMode.Single => (FontAwesomeIcon)0xf3e5,
			PlayMode.ListOrdered => (FontAwesomeIcon)0xf884,
			PlayMode.ListRepeat => (FontAwesomeIcon)0xf021,
			PlayMode.SingleRepeat => (FontAwesomeIcon)0xf01e,
			PlayMode.Random => (FontAwesomeIcon)0xf074,
			_ => throw new ArgumentOutOfRangeException()
		};

		if (IconButton(icon, "btnpmode"))
		{
			MidiBard.config.PlayMode += 1;
			MidiBard.config.PlayMode %= 5;
		}

		if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
		{
			MidiBard.config.PlayMode += 4;
			MidiBard.config.PlayMode %= 5;
		}

		ToolTip(array[MidiBard.config.PlayMode]);
	}

	private static void StopEnsemble()
	{
		if (MidiBard.AgentMetronome.EnsembleModeRunning)
		{
			MidiPlayerControl.Stop();
			IPCHandles.UpdateInstrument(false);
		}
	}

	string[] array = new string[]
	{
		Language.Playmode_Single,
		Language.SingleRepeat,
		Language.ListOrdered,
		Language.ListRepeat,
		Language.Random,
	};
}