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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard;

public record TrackInfo
{
    //  var (programTrackChunk, programTrackInfo) =
    //  CurrentTracks.FirstOrDefault(i => Regex.IsMatch(i.trackInfo.TrackName, @"^Program:.+$", RegexOptions.Compiled | RegexOptions.IgnoreCase));

    public string[] TrackNameEventsText { get; init; }
    public string[] ProgramChangeEventsText { get; init; }
    public int NoteCount { get; init; }
    public Note LowestNote { get; init; }
    public Note HighestNote { get; init; }
    public MetricTimeSpan DurationMetric { get; init; }
    public long DurationMidi { get; init; }
    public bool IsProgramControlled { get; init; }
    public string TrackName { get; init; }
    public int Index { get; set; }

    public ref bool IsEnabled => ref MidiBard.config.TrackStatus[Index].Enabled;
    public bool IsPlaying => MidiBard.config.SoloedTrack is int t ? t == Index : IsEnabled;

    public int TransposeFromTrackName => GetTransposeByName(TrackName);
    public uint? InstrumentIDFromTrackName => GetInstrumentIDByName(TrackName);
    public uint? GuitarToneFromTrackName => GetInstrumentIDByName(TrackName) - 24;
    /*
     harp 竖琴  piano 钢琴  lute 鲁特  fiddle提琴拨弦 flute长笛 oboe 双簧管 clarinet 单簧管 fife 横笛 panpipes 排箫
    TIMPANI定音鼓 BONGO邦戈鼓 bassdrum低音鼓 snaredrum小军鼓 CYMBAL镲 Trumpet小号 Trombone长号 Tuba大号 Horn圆号 Saxophone萨克斯 Violin小提琴 Viola中提琴 Cello大提琴
    DoubleBass 低音提琴 ElectricGuitaroverdriven过载 ElectricGuitarclean清音 ElectricGuitarMuted闷音 ElectricGuitarPowerchords重力 ElectricGuitarspecial特殊奏法
    */
    private static readonly Dictionary<string, uint?> instrumentIdMap = new() {
        { "harp", 1 },
        { "竖琴", 1 },

        { "piano", 2 },
        { "钢琴", 2 },

        { "lute", 3 },
        { "鲁特", 3 },

        { "fiddle", 4 },
        { "提琴拨弦", 4 },

        { "flute", 5 },
        { "长笛", 5 },

        { "oboe", 6 },
        { "双簧管", 6 },

        { "clarinet", 7 },
        { "单簧管", 7 },

        { "fife", 8 },
        { "横笛", 8 },

        { "panpipes", 9 },
        { "排箫", 9 },

        { "timpani", 10 },
        { "定音鼓", 10 },

        { "bongo", 11 },
        { "邦戈鼓", 11 },

        { "bassdrum", 12 },
        { "低音鼓", 12 },

        { "snaredrum", 13 },
        { "小军鼓", 13 },
        { "军鼓", 13 },

        { "cymbal", 14 },
        { "镲", 14 },

        { "trumpet", 15 },
        { "小号", 15 },

        { "trombone", 16 },
        { "长号", 16 },

        { "tuba", 17 },
        { "大号", 17 },

        { "horn", 18 },
        { "圆号", 18 },

        { "saxophone", 19 },
        { "萨克斯", 19 },
        // alias
        { "sax", 19 },

        { "violin", 20 },
        { "小提琴", 20 },

        { "viola", 21 },
        { "中提琴", 21 },

        { "cello", 22 },
        { "大提琴", 22 },

        { "doublebass", 23 },
        { "低音提琴", 23 },
        // alias
        { "contrabass", 23 },

        { "electricguitaroverdriven", 24 },
        { "过载", 24 },

        { "electricguitarclean", 25 },
        { "清音", 25 },

        { "electricguitarmuted", 26 },
        { "闷音", 26 },

        { "electricguitarpowerchords", 27 },
        { "重力", 27 },

        { "electricguitarspecial", 28 },
        { "特殊奏法", 28 },

        //alias
        { "snare", 13 },
        { "programelectricguitar", 24 },
        { "program", 24 },
        { "electricguitar", 24 }

    };

    public override string ToString()
    {
        return $"{TrackName} / {NoteCount} notes / {LowestNote}-{HighestNote}";
    }

    public string ToLongString()
    {
        return $"Track name:\n　{TrackName} \nNote count: \n　{NoteCount} notes \nRange:\n　{LowestNote}-{HighestNote} \n ProgramChange events: \n　{string.Join("\n　", ProgramChangeEventsText.Distinct())} \nDuration: \n　{DurationMetric}";
    }

    public static uint? GetInstrumentIDByName(string trackName)
    {
        RegexOptions regexOptions = RegexOptions.IgnoreCase | RegexOptions.Multiline;
        string sanitizedTrackName = Regex.Replace(trackName, @"(\s+|:)", "", regexOptions).ToLowerInvariant();

        string[] instrumentsKeys = instrumentIdMap.Keys.ToArray();
        string instrumentsPattern = String.Join("|", instrumentsKeys);
        string trackNamePattern = $@"({instrumentsPattern})";
        Regex expression = new Regex(trackNamePattern, regexOptions);
        Match match = expression.Match(sanitizedTrackName);

        uint? instrumentId = null;

        string instrumentName = match.Success ? match.Value.ToString() : "";
        instrumentIdMap.TryGetValue(instrumentName, out instrumentId);
        return instrumentId;
    }

    public static int GetTransposeByName(string trackName)
    {
        RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.Multiline;
        string sanitizedTrackName = Regex.Replace(trackName, @"(\s+|:)", "", options).ToLowerInvariant();
        string octavePattern = $@"(?:(\+|-)(?:\s+)?(\d))";
        Regex expression = new Regex(octavePattern, options);
        var matches = expression.Matches(sanitizedTrackName);

        int octave = 0;

        foreach (Match match in matches)
        {
            GroupCollection groups = match.Groups;
            string plusMinusSign = groups[1].Value.ToString();
            bool isParsable = Int32.TryParse(groups[2].Value, out octave);
            octave = (plusMinusSign == "-" ? -octave : octave) * 12;
        }
        //PluginLog.Debug("Transpose octave: " + octave);

        return octave;
    }
}
