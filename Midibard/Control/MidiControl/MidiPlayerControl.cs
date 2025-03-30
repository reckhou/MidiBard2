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
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Logging;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.Managers;
using MidiBard.Util;
using MidiBard.Util.Lyrics;

using static Dalamud.api;

namespace MidiBard.Control.MidiControl;

internal static class MidiPlayerControl
{
    internal static void Play()
    {
        if (MidiBard.CurrentPlayback == null)
        {
            if (!PlaylistManager.FilePathList.Any())
            {
                PluginLog.Information("empty playlist");
                return;
            }

            if (PlaylistManager.CurrentSongIndex < 0)
            {
                PlaylistManager.LoadPlayback(0, true);
            }
            else
            {
                PlaylistManager.LoadPlayback(null, true);
            }
        }
        else
        {
            try
            {
                if (MidiBard.CurrentPlayback.GetCurrentTime<MidiTimeSpan>() == MidiBard.CurrentPlayback.GetDuration<MidiTimeSpan>())
                {
                    MidiBard.CurrentPlayback.MoveToTime(new MidiTimeSpan(0));
                }

                DoPlay();
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "error when try to start playing, maybe the playback has been disposed?");
            }
        }
    }

    public static void DoPlay(bool isEnsemble = false)
    {
        if (MidiBard.CurrentPlayback == null) return;

        playDeltaTime = 0;
        MidiBard.CurrentPlayback.Start();
        _stat = e_stat.Playing;

        if (isEnsemble)
        {
            Lrc.EnsembleStart();
        }

        Lrc.Play();
    }

    internal static void Pause()
    {
        MidiBard.CurrentPlayback?.Stop();
        _stat = e_stat.Paused;
    }


    internal static void PlayPause()
    {
        if (FilePlayback.IsWaiting)
        {
            FilePlayback.SkipWaiting();
        }
        else
        {
            if (MidiBard.IsPlaying)
            {
                Pause();
                var TimeSpan = MidiBard.CurrentPlayback.GetCurrentTime<MetricTimeSpan>();
                PluginLog.Information($"Timespan: [{TimeSpan.Minutes}:{TimeSpan.Seconds}:{TimeSpan.Milliseconds}]");
            }
            else
            {
                Play();
            }
        }
    }

    internal static void Stop()
    {
        MidiBard.CurrentPlayback?.Dispose();
        MidiBard.CurrentPlayback = null;
        Lrc.Stop();
        _stat = e_stat.Stopped;
    }

    internal static void Next(bool startPlaying = false)
    {
        Lrc.Stop();
        _stat = e_stat.Stopped;
        var songIndex = GetSongIndex(PlaylistManager.CurrentSongIndex, true);
        PlaylistManager.LoadPlayback(songIndex, MidiBard.IsPlaying || startPlaying);
    }

    internal static void Prev()
    {
        Lrc.Stop();
        _stat = e_stat.Stopped;
        var songIndex = GetSongIndex(PlaylistManager.CurrentSongIndex, false);
        PlaylistManager.LoadPlayback(songIndex, MidiBard.IsPlaying);

    }

    private static int GetSongIndex(int songIndex, bool next)
    {
        var playMode = (PlayMode)MidiBard.config.PlayMode;
        switch (playMode)
        {
            case PlayMode.Single:
            case PlayMode.SingleRepeat:
            case PlayMode.ListOrdered:
            case PlayMode.ListRepeat:
                songIndex += next ? 1 : -1;
                break;
        }

        if (playMode == PlayMode.ListRepeat)
        {
            songIndex = songIndex.Cycle(0, PlaylistManager.FilePathList.Count - 1);
        }
        else if (playMode == PlayMode.Random)
        {
            if (PlaylistManager.FilePathList.Count > 1)
            {
                var r = new Random();
                do
                {
                    songIndex = r.Next(0, PlaylistManager.FilePathList.Count);
                } while (songIndex == PlaylistManager.CurrentSongIndex);
            }
        }

        return songIndex;
    }

    internal static void SetTime(ITimeSpan time)
    {
        var bardPlayback = MidiBard.CurrentPlayback;
        if (bardPlayback is null) return;

        try
        {
            if (bardPlayback.IsRunning)
            {
                bardPlayback.MoveToTime(time);
            }
            else
            {
                bardPlayback.MoveToTime(time);
                bardPlayback.PlaybackStart = time;
            }
        }
        catch (Exception e)
        {
            PluginLog.Warning(e.ToString(), "error when try setting current playback time");
        }
    }

    internal static void MoveTime(double timeInSeconds)
    {
        try
        {
            var metricTimeSpan = MidiBard.CurrentPlayback.GetCurrentTime<MetricTimeSpan>();
            var dura = MidiBard.CurrentPlayback.GetDuration<MetricTimeSpan>();
            var totalMicroseconds = metricTimeSpan.TotalMicroseconds + (long)(timeInSeconds * 1_000_000);
            if (totalMicroseconds < 0) totalMicroseconds = 0;
            if (totalMicroseconds > dura.TotalMicroseconds) totalMicroseconds = dura.TotalMicroseconds;
            MidiBard.CurrentPlayback.MoveToTime(new MetricTimeSpan(totalMicroseconds));
        }
        catch (Exception e)
        {
            PluginLog.Warning(e.ToString(), "error when try moving current playback time");
        }
    }

    internal static int playDeltaTime = 0;

    public enum e_stat
    {
        Stopped,
        Paused,
        Playing
    }

    public static e_stat _stat = e_stat.Stopped;


    internal static bool ChangeDeltaTime(int delta)
    {
        if (MidiBard.CurrentPlayback == null || !MidiBard.CurrentPlayback.IsRunning)
        {
            playDeltaTime = 0;
            return false;
        }

        var currentTime = MidiBard.CurrentPlayback.GetCurrentTime<MetricTimeSpan>();
        long msTime = currentTime.TotalMicroseconds;
        //PluginLog.Debug("curTime:" + msTime);
        if (msTime + delta * 1000 < 0)
        {
            return false;
        }
        msTime += delta * 1000;
        MetricTimeSpan newTime = new MetricTimeSpan(msTime);
        //PluginLog.Debug("newTime:" + newTime.TotalMicroseconds);
        MidiBard.CurrentPlayback.MoveToTime(newTime);
        playDeltaTime += delta;

        return true;
    }

    internal static void StopLrc()
    {
        Lrc.Stop();
        _stat = e_stat.Stopped;
        playDeltaTime = 0;
    }
}
