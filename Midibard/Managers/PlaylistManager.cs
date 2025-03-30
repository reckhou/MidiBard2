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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Dalamud;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Logging;
using Dalamud.Plugin;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl;
using MidiBard.IPC;
using MidiBard.Managers;
using MidiBard.Managers.Ipc;
using MidiBard.Util;

using Newtonsoft.Json;

using ProtoBuf;

using static Dalamud.api;

namespace MidiBard;

static class PlaylistManager
{
    internal static PlaylistContainer LoadLastPlaylist()
    {
        var config = MidiBard.config;
        var recentUsedPlaylists = config.RecentUsedPlaylists;
        var lastOrDefault = recentUsedPlaylists.LastOrDefault();
        var fileExists = false;

        if (lastOrDefault != null)
        {
            fileExists = File.Exists(lastOrDefault);
        }

        if (lastOrDefault is null || !fileExists)
        {
            ImGuiUtil.AddNotification(NotificationType.Error, $"Latest playlist NOT exist: {lastOrDefault}, using default playlist instead!");
            PluginLog.Information("Load Default playlist");
            return PlaylistContainer.FromFile(
                Path.Combine(api.PluginInterface.GetPluginConfigDirectory(), "DefaultPlaylist.mpl"), true);
        }

        PluginLog.Information($"Load playlist: {lastOrDefault}");
        return PlaylistContainer.FromFile(lastOrDefault);
    }

    private static PlaylistContainer _currentContainer;

    public static PlaylistContainer CurrentContainer
    {
        get => _currentContainer ??= LoadLastPlaylist();
        set
        {
            _currentContainer = value;
            IPCHandles.SyncPlaylist();
        }
    }

    internal static void SetContainerPrivate(PlaylistContainer newContainer) => _currentContainer = newContainer;

    public static List<SongEntry> FilePathList => CurrentContainer.SongPaths;

    public static int CurrentSongIndex
    {
        get => CurrentContainer.CurrentSongIndex;
        private set => CurrentContainer.CurrentSongIndex = value;
    }

    public static void Clear()
    {
        FilePathList.Clear();
        CurrentSongIndex = -1;
        IPCHandles.SyncPlaylist();
    }


    public static void RemoveSync(int index)
    {
        var playlistIndex = CurrentContainer.CurrentSongIndex;
        RemoveLocal(playlistIndex, index);
        IPCHandles.RemoveTrackIndex(playlistIndex, index);
        CurrentContainer.Save();
    }

    public static void RemoveLocal(int playlistIndex, int index)
    {
        try
        {
            FilePathList.RemoveAt(index);
            PluginLog.Debug($"removed [{playlistIndex}, {index}]");
            if (index < CurrentSongIndex)
            {
                CurrentSongIndex--;
            }
        }
        catch (Exception e)
        {
            PluginLog.Error(e, $"error when removing song [{playlistIndex}, {index}]");
        }
    }

    internal static readonly ReadingSettings readingSettings = new ReadingSettings
    {
        NoHeaderChunkPolicy = NoHeaderChunkPolicy.Ignore,
        NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
        InvalidChannelEventParameterValuePolicy = InvalidChannelEventParameterValuePolicy.ReadValid,
        InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
        InvalidMetaEventParameterValuePolicy = InvalidMetaEventParameterValuePolicy.SnapToLimits,
        MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore,
        UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore,
        ExtraTrackChunkPolicy = ExtraTrackChunkPolicy.Read,
        UnknownChunkIdPolicy = UnknownChunkIdPolicy.ReadAsUnknownChunk,
        SilentNoteOnPolicy = SilentNoteOnPolicy.NoteOff,
        TextEncoding = MidiBard.config.uiLang == 1
            ? Encoding.GetEncoding("gb18030")
            : Encoding.Default,
        InvalidSystemCommonEventParameterValuePolicy = InvalidSystemCommonEventParameterValuePolicy.SnapToLimits
    };

    internal static async Task AddAsync(IEnumerable<string> filePaths)
    {
        var success = 0;
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            foreach (var (file, path) in CheckValidFiles(filePaths))
            {
                try
                {
                    var songLength = file.GetDurationTimeSpan();
                    FilePathList.Add(new SongEntry { FilePath = path, SongLength = songLength ?? TimeSpan.Zero });
                    success++;
                }
                catch (Exception e)
                {
                    PluginLog.Warning(e, "error when getting duration");
                }
            }

            CalculateDurationAll();
        });

        IPCHandles.SyncPlaylist();
        CurrentContainer.Save();
        PluginLog.Information($"File import all complete in {sw.Elapsed.TotalMilliseconds} ms! success: {success}");
    }

    internal static void CalculateDurationAll()
    {
        var parallelQuery = PlaylistManager.FilePathList.AsParallel();
        parallelQuery.ForAll(i =>
        {
            if (i.SongLength == default)
            {
                try
                {
                    i.SongLength = PlaylistManager.LoadSongFile(i.FilePath).GetDuration<MetricTimeSpan>();
                }
                catch (Exception e)
                {
                    PluginLog.Warning(e, $"error when getting {i.FilePath} duration");
                }
            }
        });
    }
    private static IEnumerable<(MidiFile, string)> CheckValidFiles(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            MidiFile file = null;

            file = LoadSongFile(path);
            if (file is not null) yield return (file, path);
        }
    }

    internal static MidiFile LoadSongFile(string path)
    {
        if (Path.GetExtension(path).Equals(".mmsong"))
            return LoadMMSongFile(path);
        else if (Path.GetExtension(path).Equals(".mid") || Path.GetExtension(path).Equals(".midi"))
            return LoadMidiFile(path);
        return null;
    }

    private static MidiFile LoadMidiFile(string filePath)
    {
        PluginLog.Debug($"[LoadMidiFile] -> {filePath} START");
        MidiFile loaded = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!File.Exists(filePath))
            {
                PluginLog.Warning($"File not exist! path: {filePath}");
                return null;
            }

            using (var f = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                loaded = MidiFile.Read(f, readingSettings);
            }

            PluginLog.Debug($"[LoadMidiFile] -> {filePath} OK! in {stopwatch.Elapsed.TotalMilliseconds} ms");
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "Failed to load file at {0}", filePath);
        }


        return loaded;
    }

    public static async Task<bool> LoadPlayback(int? index = null, bool startPlaying = false, bool sync = true)
    {
        //if (index < 0 || index >= FilePathList.Count)
        //{
        //	PluginLog.Warning($"LoadPlaybackIndex: invalid playlist index {index}");
        //	//return false;
        //}

        if (index is int songIndex) CurrentSongIndex = songIndex;
        if (sync) IPCHandles.LoadPlayback(CurrentSongIndex);
        if (await LoadPlaybackPrivate())
        {
            if (startPlaying)
            {
                MidiPlayerControl.DoPlay();
            }

            return true;
        }

        return false;
    }

    private static async Task<bool> LoadPlaybackPrivate()
    {
        try
        {
            var songEntry = FilePathList[CurrentSongIndex];
            return await FilePlayback.LoadPlayback(songEntry.FilePath);
        }
        catch (Exception e)
        {
            PluginLog.Warning(e.ToString());
            return false;
        }
    }

    private static MidiFile LoadMMSongFile(string filePath)
    {
        PluginLog.Debug($"[LoadMMSongFile] -> {filePath} START");
        MidiFile midiFile = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!File.Exists(filePath))
            {
                PluginLog.Warning($"File not exist! path: {filePath}");
                return null;
            }

            Dictionary<int, string> instr = new Dictionary<int, string>()
                {
                    { 0, "NONE" },
                    { 1, "Harp" },
                    { 2, "Piano" },
                    { 3, "Lute" },
                    { 4, "Fiddle" },
                    { 5, "Flute" },
                    { 6, "Oboe" },
                    { 7, "Clarinet" },
                    { 8, "Fife" },
                    { 9, "Panpipes" },
                    { 10, "Timpani" },
                    { 11, "Bongo" },
                    { 12, "BassDrum" },
                    { 13, "SnareDrum" },
                    { 14, "Cymbal" },
                    { 15, "Trumpet" },
                    { 16, "Trombone" },
                    { 17, "Tuba" },
                    { 18, "Horn" },
                    { 19, "Saxophone" },
                    { 20, "Violin" },
                    { 21, "Viola" },
                    { 22, "Cello" },
                    { 23, "DoubleBass" },
                    { 24, "ElectricGuitarOverdriven" },
                    { 25, "ElectricGuitarClean" },
                    { 26, "ElectricGuitarMuted" },
                    { 27, "ElectricGuitarPowerChords" },
                    { 28, "ElectricGuitarSpecial" }
                };

            Util.MMSongContainer songContainer = null;

            FileInfo fileToDecompress = new FileInfo(filePath);
            using (FileStream originalFileStream = fileToDecompress.OpenRead())
            {
                string currentFileName = fileToDecompress.FullName;
                string newFileName = currentFileName.Remove(currentFileName.Length - fileToDecompress.Extension.Length);
                using (GZipStream decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress))
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        decompressionStream.CopyTo(memoryStream);
                        memoryStream.Position = 0;
                        var data = "";
                        using (var reader = new StreamReader(memoryStream, System.Text.Encoding.ASCII))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                data += line;
                            }
                        }
                        memoryStream.Close();
                        decompressionStream.Close();
                        songContainer = JsonConvert.DeserializeObject<Util.MMSongContainer>(data);
                    }
                }
            }

            midiFile = new MidiFile();
            foreach (Util.MMSong msong in songContainer.songs)
            {
                if (msong.bards.Count() == 0)
                    continue;
                else
                {
                    foreach (var bard in msong.bards)
                    {
                        var thisTrack = new TrackChunk(new SequenceTrackNameEvent(instr[bard.instrument]));
                        using (var manager = thisTrack.ManageTimedEvents())
                        {
                            TimedObjectsCollection<TimedEvent> timedEvents = manager.Objects;
                            int last = 0;
                            foreach (var note in bard.sequence)
                            {
                                if (note.Value == 254)
                                {
                                    var pitched = last + 24;
                                    timedEvents.Add(new TimedEvent(new NoteOffEvent((SevenBitNumber)pitched, (SevenBitNumber)127), note.Key));
                                }
                                else
                                {
                                    var pitched = (SevenBitNumber)note.Value + 24;
                                    timedEvents.Add(new TimedEvent(new NoteOnEvent((SevenBitNumber)pitched, (SevenBitNumber)127), note.Key));
                                    last = note.Value;
                                }
                            }
                        }
                        midiFile.Chunks.Add(thisTrack);
                    }
                    ;
                    break; //Only the first song for now
                }
            }
            midiFile.ReplaceTempoMap(TempoMap.Create(Tempo.FromBeatsPerMinute(25)));
            PluginLog.Debug($"[LoadMMSongFile] -> {filePath} OK! in {stopwatch.Elapsed.TotalMilliseconds} ms");
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "Failed to load file at {0}", filePath);
        }

        return midiFile;
    }
}
