﻿
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using MidiBard.Common;


namespace MidiBard.HSC.Music
{
    public class MidiProcessor
    {

        public static void ProcessChords(string fileName, IEnumerable<TrackChunk> trackChunks)
        {

            int trackIndex = 0;

            if (!HSC.Settings.PlaylistSettings.Settings.ContainsKey(fileName))
                return;

            var settings = HSC.Settings.PlaylistSettings.Settings[fileName];

            Parallel.ForEach(trackChunks, trackChunk =>
            {
                ChordsManagingUtilities.ProcessChords(trackChunk, chord => ProcessChord(settings, chord, trackIndex), 
                    new ChordDetectionSettings() {  
                        NoteDetectionSettings = new NoteDetectionSettings() { 
                            NoteStartDetectionPolicy = NoteStartDetectionPolicy.FirstNoteOn, 
                            NoteSearchContext = NoteSearchContext.AllEventsCollections},
                       NotesMinCount = 2,
                       NotesTolerance = 0,
                      ChordSearchContext = ChordSearchContext.AllEventsCollections
                    });
                trackIndex++;
            });
        }

        private static void ProcessChord(
            MidiSequence sequence,
            Chord chord,
            int trackIndex)
        {
            IEnumerable<Note> notesToKeep;

            if (chord.Notes.Count() == 1)
                return;

            //global first
            if (sequence.PlayAll)
                return;

            if (sequence.HighestOnly)
                notesToKeep = ProcessChordNotesHighestOnly(chord);

            else
                notesToKeep = ProcessChordNotesReduce(chord, sequence.ReduceMaxNotes);

            //by track
            if (!sequence.Tracks.ContainsKey(trackIndex))
            {
                chord.Notes.RemoveAll(n => !notesToKeep.Contains(n));
                return;
            }
          
            var track = sequence.Tracks[trackIndex];

            if (track.PlayAll)
                return;

            if (track.HighestOnly)
                notesToKeep = ProcessChordNotesHighestOnly(chord);
            else
                notesToKeep = ProcessChordNotesReduce(chord, track.ReduceMaxNotes);

            chord.Notes.RemoveAll(no => notesToKeep.All(n => n.NoteNumber != no.NoteNumber));
        }

        private static IEnumerable<Note> ProcessChordNotesHighestOnly(Chord chord)
        {
            if (chord.Notes.Count() < 2)
                return null;

            var highestNote = chord.Notes.Last();

            return new[] { highestNote };
        }

        private static IEnumerable<Note> ProcessChordNotesReduce(Chord chord, int maxNotes)
        {
            if (chord.Notes.Count() < 2)
                return null;

            var reducedChordNotes = ReduceChordNotes(chord.Notes, maxNotes);

            return reducedChordNotes;
        }

        private static IEnumerable<Note> ReduceChordNotes(IEnumerable<Note> chordNotes, int maxNotes)
        {
            chordNotes = chordNotes.OrderBy(n => n.NoteNumber);

            var lowestNote = chordNotes.First();
            var highestNote = chordNotes.Last();

            if (maxNotes == 2)
                return new[] { lowestNote, highestNote };

            var notes = new[] { lowestNote }
            .Concat(chordNotes.Skip(1).Take(maxNotes - 2))
            .Concat(new[] { highestNote });

            return notes.OrderBy(n => n.NoteNumber);
        }
    }
}