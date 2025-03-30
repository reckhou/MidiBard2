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

using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using static Dalamud.api;

namespace MidiBard.Util.MidiPreprocessor
{
    internal class MidiPreprocessor
    {
        /// <summary>
        /// Realign the the notes and Events in a <see cref="MidiFile"/> to the beginning
        /// </summary>
        /// <param name="midi"></param>
        /// <returns><see cref="MidiFile"/></returns>
        public static MidiFile RealignMidiFile(MidiFile midi)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            //get the first note on event
            var x = midi.GetTrackChunks().GetNotes().First().GetTimedNoteOnEvent().Time;
            //move everything to the new offset
            Parallel.ForEach(midi.GetTrackChunks(), chunk =>
            {
                chunk = RealignTrackEvents(chunk, x).Result;
            });

            PluginLog.Warning($"[MidiPreprocessor] Realign tracks took: {stopwatch.Elapsed.TotalMilliseconds} ms");
            stopwatch.Stop();
            return midi;
        }

        /// <summary>
        /// Realigns the track events in <see cref="TrackChunk"/>
        /// </summary>
        /// <param name="originalChunk"></param>
        /// <param name="delta"></param>
        /// <returns><see cref="Task{TResult}"/> is <see cref="TrackChunk"/></returns>
        internal static Task<TrackChunk> RealignTrackEvents(TrackChunk originalChunk, long delta)
        {
            using (var manager = originalChunk.ManageTimedEvents())
            {
                foreach (TimedEvent _event in manager.Objects)
                {
                    long newStart = _event.Time - delta;
                    if (newStart <= -1)
                        _event.Time = 0;
                    else
                        _event.Time = newStart;
                }
            }
            return Task.FromResult(originalChunk);
        }

        public static TrackChunk[] ProcessTracks(TrackChunk[] trackChunks, TempoMap tempoMap)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            foreach (var cur in trackChunks)
            {
                cur.ProcessNotes(n => CutNote(n, tempoMap));
            }

            stopwatch.Stop();
            PluginLog.Warning($"[MidiPreprocessor] Process tracks took: {stopwatch.Elapsed.TotalMilliseconds} ms");
            return trackChunks;
        }

        private static void CutNote(Note n, TempoMap tempoMap)
        {
            var length = n.LengthAs<MetricTimeSpan>(tempoMap).TotalMicroseconds / 1000;
            //PluginLog.Verbose($"Note: {n.ToString()} Length: {length}ms");
            if (length > 2000)
            {
                var newLength = length - 50; // cut long notes by 50ms to add a small interval between key up/down
                n.SetLength<Note>(new MetricTimeSpan(newLength * 1000), tempoMap);
            }
        }
    }
}
