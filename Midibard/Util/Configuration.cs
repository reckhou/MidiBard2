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
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Configuration;

using MidiBard.Managers;
using MidiBard.Util;

using Newtonsoft.Json;

namespace MidiBard;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; }

    [JsonIgnore]
    public TrackStatus[] TrackStatus = Enumerable.Repeat(new TrackStatus(), 100).ToArray().JsonSerialize().JsonDeserialize<TrackStatus[]>();
    //public ChannelStatus[] ChannelStatus = Enumerable.Repeat(new ChannelStatus(), 16).ToArray();
    public List<EnsembleMemberConfig> EnsembleMemberConfigs = new();

    public List<string> RecentUsedPlaylists = new List<string>();

    // folder / file dialogs
    public List<string> PinnedImportFolders = new List<string>();
    public string lastOpenedFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public string defaultPerformerFolder = api.PluginInterface.ConfigDirectory.FullName;
    public bool useLegacyFileDialog;

    // playback config
    public float PlaySpeed = 1f;
    public float SecondsBetweenTracks = 3;
    public int PlayMode = 0;
    public int TransposeGlobal = 0;
    public bool AdaptNotesOOR = true;
    public bool AlignMidi = false;
    public double AlignMidiStartOffset = 0;
    public bool LowLatencyMode => false;
    public bool MonitorOnEnsemble = true;
    public bool AutoOpenPlayerWhenPerforming = true;
    public bool AutoClosePlayerWhenPerforming = false;
    public int? SoloedTrack = null;
    public bool lazyNoteRelease = true;
    public string lastUsedMidiDeviceName = "";
    public bool autoRestoreListening = false;
    public bool autoSwitchInstrumentBySongName = true;
    public bool autoTransposeBySongName = true;
    public bool bmpTrackNames = true;
    public bool StopPlayingWhenEnsembleEnds = true;
    public bool SyncClients = true;
    public bool AutoSetOffAFKSwitchingTime = true;

    // TCP IPC configuration (for Linux/Wine)
    public int TCPIPCPort = 21043;
    public string TCPIPCBindAddress = "127.0.0.1";
    public float EnsembleIndicatorDelay = -4;
    public bool UseEnsembleIndicator = false;
    public bool UpdateInstrumentBeforeReadyCheck;
    public GuitarToneMode GuitarToneMode = GuitarToneMode.Off;
    public CompensationModes CompensationMode = CompensationModes.ByInstrumentNote;
    public int[] ManualInstrumentCompensation = EnsembleManager.GetCompensationAver();
    //public bool TrimChords = false;
    //public int TrimTo = 1;
    //public bool autoSwitchInstrumentByTrackName = false;
    //public bool autoTransposeByTrackName = false;
    //public bool autoStartNewListening = false;

    // Play on multiple devices
    public bool playOnMultipleDevices = false;
    public bool useChatPlaylistSync = false;
    public bool usingFileSharingServices = true;

    // Lyrics
    public bool playLyrics = true;
    public ChatType LyricsChatTarget = ChatType.Current;

    // Post Song
    public bool autoPostSongName = false;
    public ChatType SongNameChatTarget = ChatType.Current;
    public string postSongNameCaptureRegex = "";
    public string postSongNameCaptureOutputFormat = "";
    public string postSongNameFindRegex = "";
    public string postSongNameReplacement = "";

    // Theme
    public Vector4 themeColor = new Vector4(0.65882355f, 0.65882355f, 1f, 1f);
    public Vector4 themeColorDark => themeColor * new Vector4(0.25f, 0.25f, 0.25f, 1);
    public Vector4 themeColorTransparent => themeColor * new Vector4(1, 1, 1, 0.33f);
    public Vector4 playedSongColor = new Vector4(0.0f, 0.9804f, 1.0f, 1.0f);
    public ThemeVariant CurrentTheme = ThemeVariant.Default;

    // search
    public bool enableSearching = false;
    public bool SearchUseRegex;
    public FilterPlayedSongOptions SearchFilterPlayedOption = FilterPlayedSongOptions.ShowAll;

    // UI
    public bool AutoOpenOnStartup = false;
    public bool UseStandalonePlaylistWindow = false;
    public bool hidePlayerInformationFromUi = false;
    public int playlistSizeY = 10;
    public bool miniPlayer = false;
    public bool LockPlot = false;
    public int uiLang = api.PluginInterface.UiLanguage == "zh" ? 1 : 0;
    // show / hide items
    public bool UiShowGuitarToneMode = false;
    public bool UiShowPlaySpeed = false;
    public bool UiShowTransposeGlobal = false;
    public bool UiShowAdaptNotesOOR = false;
    public bool UiShowAutoAlignMidi = false;
    public bool showNowPlayingInfo = true;

    //[JsonIgnore] public bool OverrideGuitarTones => GuitarToneMode == GuitarToneMode.Override;

    public void ToggleSearchFilterPlayedOption()
    {
        var totalOptions = Enum.GetValues(typeof(FilterPlayedSongOptions)).Length;
        SearchFilterPlayedOption = (FilterPlayedSongOptions)(((int)SearchFilterPlayedOption + 1) % totalOptions);
    }

    public void AddEnsembleMemberConfig(EnsembleMemberConfig newConfig)
    {
        var existing = EnsembleMemberConfigs.FirstOrDefault(p => p.Cid == newConfig.Cid);
        if (existing == null)
        {
            EnsembleMemberConfigs.Add(newConfig);
        }
    }

    public void RemoveEnsembleMemberConfig(long cid)
    {
        var isEmptyList = EnsembleMemberConfigs == null || EnsembleMemberConfigs.Count == 0;

        if (isEmptyList)
            return;

        var existingIndex = EnsembleMemberConfigs.FindIndex(p => p.Cid == cid);
        if (existingIndex != -1)
        {
            EnsembleMemberConfigs.RemoveAt(existingIndex);
        }
    }

    // TODO: create a generic move to index for lists
    public void MoveEnsembleMemberConfigToIndex(int itemIndex, int targetIndex)
    {
        var isEmptyList = EnsembleMemberConfigs == null || EnsembleMemberConfigs.Count == 0;
        var isInvalidIndex = itemIndex < 0 || itemIndex >= EnsembleMemberConfigs.Count;

        if (isEmptyList || isInvalidIndex)
            return;

        // clamp index
        targetIndex = Math.Clamp(targetIndex, 0, EnsembleMemberConfigs.Count);

        var item = EnsembleMemberConfigs[itemIndex];
        EnsembleMemberConfigs.RemoveAt(itemIndex);
        EnsembleMemberConfigs.Insert(targetIndex, item);
    }

    public void MovePinnedImportFolderToIndex(int itemIndex, int targetIndex)
    {
        var isEmptyList = PinnedImportFolders == null || PinnedImportFolders.Count == 0;
        var isInvalidIndex = itemIndex < 0 || itemIndex >= PinnedImportFolders.Count;

        if (isEmptyList || isInvalidIndex)
            return;

        // clamp index
        targetIndex = Math.Clamp(targetIndex, 0, PinnedImportFolders.Count);

        var item = PinnedImportFolders[itemIndex];
        PinnedImportFolders.RemoveAt(itemIndex);
        PinnedImportFolders.Insert(targetIndex, item);
    }

    public void RemovePinnedImportFolder(int itemIndex)
    {
        var isEmptyList = PinnedImportFolders == null || PinnedImportFolders.Count == 0;
        var isInvalidIndex = itemIndex < 0 || itemIndex >= PinnedImportFolders.Count;

        if (isEmptyList || isInvalidIndex)
            return;

        PinnedImportFolders.RemoveAt(itemIndex);
    }

    public void ChangeEnsembleMemberConfigOrder(long cid, int moveBy)
    {
        var isEmptyList = EnsembleMemberConfigs == null || EnsembleMemberConfigs.Count == 0;

        if (isEmptyList)
            return;

        var existingIndex = EnsembleMemberConfigs.FindIndex(p => p.Cid == cid);
        if (existingIndex != -1)
        {
            int newIndex = Math.Max(0, Math.Min(EnsembleMemberConfigs.Count - 1, existingIndex + moveBy));

            if (newIndex == existingIndex)
                return;

            var item = EnsembleMemberConfigs[existingIndex];
            EnsembleMemberConfigs.RemoveAt(existingIndex);
            EnsembleMemberConfigs.Insert(newIndex, item);
        }
    }

    public void ResetTrackStatus()
    {
        TrackStatus = Enumerable.Repeat(new TrackStatus(), 100).ToArray().JsonSerialize().JsonDeserialize<TrackStatus[]>();
    }

    public string GetChatCommand(ChatType chatType)
    {
        return chatType switch
        {
            ChatType.Current => string.Empty,
            ChatType.Say => "/s ",
            ChatType.Party => "/p ",
            _ => string.Empty
        };
    }

    public void SetTransposeGlobal(int transpose)
    {
        bool isDrumTrackPlaying = false;
        if (MidiBard.CurrentPlayback?.TrackInfos?.Length > 0)
        {
            foreach (var trackInfo in MidiBard.CurrentPlayback?.TrackInfos)
            {
                var insID = trackInfo.InstrumentIDFromTrackName;
                if (trackInfo.IsEnabled && insID >= 10 && insID <= 14)
                {
                    isDrumTrackPlaying = true;
                    break;
                }
            }
        }

        if (isDrumTrackPlaying)
        {
            TransposeGlobal = 0;
            return;
        }

        TransposeGlobal = transpose;
    }
}

public enum PlayMode
{
    Single,
    SingleRepeat,
    ListOrdered,
    ListRepeat,
    Random
}

public enum GuitarToneMode
{
    Off,
    Standard,
    Simple,
    OverrideByTrack,
    //OverrideByChannel,
}

public class TrackStatus
{
    public bool Enabled = false;
    public int Tone = 0;
    public int Transpose = 0;
}

//public struct ChannelStatus
//{
//    public ChannelStatus(bool enabled = true, int tone = 0, int transpose = 0)
//    {
//        Enabled = enabled;
//        Tone = tone;
//        Transpose = transpose;
//    }

//    public bool Enabled = true;
//    public int Tone = 0;
//    public int Transpose = 0;
//}

public class EnsembleMemberConfig
{
    public long Cid;
    public string Name;
    public string TrackAssignmentRegex;
}

public enum ChatType
{
    Current = 0,
    Say = 1,
    Party = 2,
}

public enum FilterPlayedSongOptions
{
    ShowAll = 0,
    ShowPlayed = 1,
    ShowUnPlayed = 2,
}

public enum CompensationModes
{
    None = 0,
    ByInstrument = 1,
    ByInstrumentNote = 2,
}
