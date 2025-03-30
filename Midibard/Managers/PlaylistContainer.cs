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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Dalamud.Logging;

using JetBrains.Annotations;

using MidiBard.Util;

using Newtonsoft.Json;

using ProtoBuf;

using static Dalamud.api;

namespace MidiBard;

[ProtoContract]
public class PlaylistContainer
{
    private static Regex metadataParser = new Regex(@"^\[(?<key>.+?):(?<value>.+)\]$");
    public static PlaylistContainer FromFile(string filePath, bool createIfNotExist = false)
    {
        if (File.Exists(filePath))
        {
            RecordToRecentUsed(filePath);
            var container = new PlaylistContainer();
            var readLines = File.ReadAllLines(filePath, Encoding.UTF8);
            var songEntries = readLines.Select(i =>
            {
                try
                {
                    var fullPath = Path.GetFullPath(i, filePath);
                    return new SongEntry { FilePath = fullPath };
                }
                catch (Exception e)
                {
                    return null;
                }
            }).Where(i => i is not null);
            container.SongPaths.AddRange(songEntries);
            container.FilePathWhenLoading = filePath;
            return container;
        }

        if (!createIfNotExist) return null;

        var newContainer = new PlaylistContainer { FilePathWhenLoading = filePath };
        newContainer.Save(filePath);
        RecordToRecentUsed(filePath);
        return newContainer;
    }

    private PlaylistContainer() { }

    private static void RecordToRecentUsed(string filePath)
    {
        var usedPlaylists = MidiBard.config.RecentUsedPlaylists;
        if (usedPlaylists.Contains(filePath))
        {
            usedPlaylists.Remove(filePath);
        }

        usedPlaylists.Add(filePath);

        const int maxRecentRecordSize = 30;
        if (usedPlaylists.Count > maxRecentRecordSize)
        {
            usedPlaylists.RemoveRange(0, usedPlaylists.Count - maxRecentRecordSize);
        }
    }

    public void Save()
    {
        Save(FilePathWhenLoading, this);
    }

    public void Save(string filePath)
    {
        Save(filePath, this);
    }

    public static void Save(string filePath, PlaylistContainer obj)
    {
        try
        {
            RecordToRecentUsed(filePath);
            obj.FilePathWhenLoading = filePath;
            var contents = obj.SongPaths.Select(i => Path.GetRelativePath(filePath, i.FilePath)).ToArray();
            File.WriteAllLines(filePath, contents, Encoding.UTF8);
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "error when saving playlist");
        }
    }

    public string DisplayName => Path.GetFileNameWithoutExtension(FilePathWhenLoading);

    [ProtoMember(1)] public string FilePathWhenLoading = null;
    [ProtoMember(2)] public List<SongEntry> SongPaths = new();
    [ProtoMember(3)] private int _currentSongIndex = -1;

    [ProtoMember(4)]
    public int CurrentSongIndex
    {
        get => _currentSongIndex;
        set => _currentSongIndex = value.Clamp(-1, SongPaths.Count - 1);
    }

    private int lastReadDurationtick = 0;
    private TimeSpan _totalDuration;
    public TimeSpan TotalDuration
    {
        get
        {
            try
            {
                var tickCount = Environment.TickCount;
                if (tickCount > lastReadDurationtick + 20)
                {
                    var d = 0L;
                    for (var i = 0; i < SongPaths.Count; i++)
                    {
                        d += SongPaths[i].SongLength.Ticks;
                    }

                    var fromTicks = TimeSpan.FromTicks(d);
                    _totalDuration = fromTicks;
                    lastReadDurationtick = tickCount;
                    return fromTicks;
                }
            }
            catch (Exception e)
            {
            }

            return _totalDuration;
        }
    }

    public SongEntry? CurrentSongEntry
    {
        get
        {
            if (CurrentSongIndex < 0)
            {
                return null;
            }

            try
            {
                return SongPaths[CurrentSongIndex];
            }
            catch (Exception e)
            {
                return null;
            }
        }
    }
}

[ProtoContract]
public class SongEntry
{
    [ProtoMember(1)] public string FilePath;
    [ProtoMember(2)] public TimeSpan SongLength;
    [JsonIgnore] private string _name;
    [JsonIgnore] public string FileName => _name ??= Path.GetFileNameWithoutExtension(FilePath);
    [JsonIgnore] public string LrcPath => Path.ChangeExtension(FilePath, "lrc");
}
