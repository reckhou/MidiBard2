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

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

using Midibard.Playlib;

using MidiBard.Managers;
using MidiBard.Managers.Agents;

using static Dalamud.api;

namespace MidiBard.Control;

public class BardPlayDevice : IOutputDevice
{
    public abstract record MidiEventMetaData;
    public record MidiDeviceMetaData : MidiEventMetaData;
    public record MidiPlaybackMetaData(int TrackIndex, long Time, int EventValue) : MidiEventMetaData
    {
        public int EventValueTransposed => EventValue >= 0 ? BardPlayDevice.GetNoteNumberTranslatedByTrack(EventValue, TrackIndex) : EventValue;
    }
    private MidiClock PlaybackTicker;
    private readonly List<(MidiEvent, MidiPlaybackMetaData)>[] MidiEventsBuffer;
    const int BufferLength = 500;

    public BardPlayDevice()
    {
        Channels = new ChannelState[16];
        CurrentChannel = FourBitNumber.MinValue;
        MidiEventsBuffer = new List<(MidiEvent, MidiPlaybackMetaData)>[BufferLength];
        for (var i = 0; i < MidiEventsBuffer.Length; i++)
        {
            MidiEventsBuffer[i] = new List<(MidiEvent, MidiPlaybackMetaData)>();
        }

        PlaybackTicker = new MidiClock(false, new HighPrecisionTickGenerator(), TimeSpan.FromMilliseconds(1));
        PlaybackTicker.Ticked += PlaybackTickerTicked;
        PlaybackTicker.Restart();
    }

    private long CurrentBufferIndex;
    private List<(MidiEvent, MidiPlaybackMetaData)> NotesCurrentTick => MidiEventsBuffer[CurrentBufferIndex];

    private void PlaybackTickerTicked(object sender, EventArgs e)
    {
        if (IsDisposed) return;
        try
        {
            foreach (var (midiEvent, (trackIndex, time, eventValue)) in NotesCurrentTick.OrderBy(i => i.Item2.EventValueTransposed))
            {
                try
                {
                    //Actually Play event
                    PluginLog.Verbose($"[MidiClockTick] buffer: {CurrentBufferIndex} remain: {NotesCurrentTick.Count} {midiEvent} T{trackIndex}");
                    PlayMidiEvent(midiEvent, trackIndex, false);
                }
                catch (Exception exception)
                {
                    PluginLog.Error(exception, "exception in dequeue tick method");
                }
            }
        }
        catch (Exception exception)
        {
            PluginLog.Error(exception, "error when dequeuing midi event");
        }

        NotesCurrentTick.Clear();

        CurrentBufferIndex++;
        if (CurrentBufferIndex >= BufferLength)
        {
            CurrentBufferIndex = 0;
        }
    }

    //int GetNoteDelay(int instrument, int noteNumber)
    //{
    //    if (noteNumber == -1)
    //    {
    //        return EnsembleManager.GetCompensationNew(instrument, noteNumber);
    //    }

    //    var instrumentDelayFromConfig = 0; //switch ... blahblahblah
    //    return instrumentDelayFromConfig;
    //}

    private (MidiPlaybackMetaData metadata, int delayms) lastnoteon = (new MidiPlaybackMetaData(-1, -1, -1), 0);
    public void QueuePlaybackMidiEvent(MidiEvent midiEvent, MidiPlaybackMetaData metadata)
    {
        var trackIndex = metadata.TrackIndex;

        int delayMs;
        if (midiEvent is not NoteEvent noteEvent)
        {
            delayMs = EnsembleManager.GetCompensationNew(MidiBard.CurrentInstrumentWithTone, -1);
        }
        else
        {
            delayMs = EnsembleManager.GetCompensationNew(MidiBard.CurrentInstrumentWithTone, GetNoteNumberTranslatedByTrack(noteEvent.NoteNumber, trackIndex));

            if (midiEvent is NoteOnEvent noteOn)
            {
                //same track and same time
                if (metadata.TrackIndex == lastnoteon.metadata.TrackIndex && metadata.Time == lastnoteon.metadata.Time)
                {
                    var eventValueTransposed = metadata.EventValueTransposed;
                    var lastEventValueTransposed = lastnoteon.metadata.EventValueTransposed;
                    PluginLog.Debug($"chord note t{metadata.Time,6}/{lastnoteon.metadata.Time,-6} noteNumber:{noteOn.NoteNumber} delay:{delayMs}/{lastnoteon.delayms} eventValue:{eventValueTransposed}/{lastEventValueTransposed}");
                    //new note delay is > previous delay
                    if (delayMs < lastnoteon.delayms && eventValueTransposed > lastEventValueTransposed
                        || delayMs > lastnoteon.delayms && eventValueTransposed < lastEventValueTransposed)
                    {
                        //new note is lower than previous note
                        PluginLog.Warning($"correct delayms from {delayMs} -> {lastnoteon.delayms}");
                        delayMs = lastnoteon.delayms;

                    }
                }

                lastnoteon = (metadata, delayMs);
            }
        }



        var delayedBufferIndex = (CurrentBufferIndex + delayMs + 1) % BufferLength;

        PluginLog.Verbose($"[enqueue] ti{metadata.Time} dt{midiEvent.DeltaTime} event {midiEvent} to: {CurrentBufferIndex}+{delayMs}={delayedBufferIndex} ({EnsembleManager.CompensationMax - delayMs})");
        MidiEventsBuffer[delayedBufferIndex].Add((midiEvent, metadata));
    }

    private struct ChannelState
    {
        public SevenBitNumber Program { get; set; }

        public ChannelState(SevenBitNumber? program)
        {
            this.Program = program ?? SevenBitNumber.MinValue;
        }
    }

    private readonly ChannelState[] Channels;

    private FourBitNumber CurrentChannel;

    public void ResetChannelStates()
    {
        for (var i = 0; i < Channels.Length; i++)
        {
            Channels[i].Program = SevenBitNumber.MinValue;
        }
    }

    public event EventHandler<MidiEventSentEventArgs> EventSent;

    public void PrepareForEventsSending()
    {
    }

    [Obsolete("Use SendEventWithMetadata Instead", true)]
    public void SendEvent(MidiEvent midiEvent)
    {
    }

    public void SendEventWithMetadata(MidiEvent midiEvent, object metadata)
    {
        if (IsDisposed) return;
        if (!MidiBard.AgentPerformance.InPerformanceMode) return;

        switch (metadata)
        {
            case MidiDeviceMetaData:
                {
                    PlayMidiEvent(midiEvent, 0, true);
                    return;
                }
            case MidiPlaybackMetaData midiPlaybackMeta:
                {
                    if (MidiBard.CurrentPlayback?.TrackInfos[midiPlaybackMeta.TrackIndex].IsPlaying != true) return;
                    if (EnsembleManager.EnsembleRunning)
                    {
                        QueuePlaybackMidiEvent(midiEvent, midiPlaybackMeta);
                        return;
                    }

                    PlayMidiEvent(midiEvent, midiPlaybackMeta.TrackIndex, false);
                    break;
                }
        }
    }

    private unsafe bool PlayMidiEvent(MidiEvent midiEvent, int trackIndex, bool isDevice)
    {
        if (IsDisposed) return false;

        switch (midiEvent)
        {
            case ProgramChangeEvent programChangeEvent:
                ProcessProgramChange(programChangeEvent);
                break;
            case NoteEvent noteEvent:
                var noteNum = isDevice ? GetNoteNumberTranslated(noteEvent.NoteNumber) : GetNoteNumberTranslatedByTrack(noteEvent.NoteNumber, trackIndex);
                if (noteNum is < 0 or > 36) return false;

                if (MidiBard.PlayingGuitar)
                {
                    switch (MidiBard.config.GuitarToneMode)
                    {
                        case GuitarToneMode.Off:
                            break;
                        case GuitarToneMode.Standard:
                        case GuitarToneMode.Simple:
                            {
                                ApplyToneByChannel(noteEvent.Channel);
                                break;
                            }
                        case GuitarToneMode.OverrideByTrack when !isDevice:
                            {
                                ApplyToneByTrack(trackIndex);
                                break;
                            }
                    }
                }

                return noteEvent switch
                {
                    NoteOnEvent => KeyDown(noteNum),
                    NoteOffEvent => KeyUp(noteNum)
                };
        }

        return false;
    }

    private static unsafe bool KeyUp(int noteNum)
    {
        var agentPerformance = AgentPerformance.Instance;
        //not holding same note. skip.
        if (agentPerformance.Struct->CurrentPressingNote - 39 != noteNum)
        {
            PluginLog.Verbose($"[SkipKUp] {noteNum} != {agentPerformance.Struct->CurrentPressingNote - 39}");
            return true;
        }

        // only release a key when it been pressing
        if (Playlib.ReleaseKey(noteNum))
        {
            PluginLog.Debug($"[KeyUp  ] {noteNum}");
            agentPerformance.Struct->CurrentPressingNote = -100;
            return true;
        }

        return false;
    }

    private static unsafe bool KeyDown(int noteNum)
    {
        var agentPerformance = AgentPerformance.Instance;
        //currently holding the same note?
        if (agentPerformance.noteNumber - 39 == noteNum)
        {
            // release repeated note in order to press it again
            if (Playlib.ReleaseKey(noteNum))
            {
                agentPerformance.Struct->CurrentPressingNote = -100;
                PluginLog.Verbose($"[ReKeyUp] {noteNum}");
            }
        }

        if (Playlib.PressKey(noteNum, ref agentPerformance.Struct->NoteOffset, ref agentPerformance.Struct->OctaveOffset))
        {
            agentPerformance.Struct->CurrentPressingNote = noteNum + 39;
            PluginLog.Debug($"[KeyDown] {noteNum}");
            return true;
        }

        return false;
    }

    private void ApplyToneByTrack(int trackIndex)
    {
        int tone = MidiBard.config.TrackStatus[trackIndex].Tone;
        Playlib.GuitarSwitchTone(tone);
    }

    private void ApplyToneByChannel(FourBitNumber channel)
    {
        CurrentChannel = channel;
        if (!TryGetToneFromProgram(Channels[channel].Program, out var tone)) return;
        Playlib.GuitarSwitchTone(tone);
    }

    private void ProcessProgramChange(ProgramChangeEvent programChangeEvent)
    {
        switch (MidiBard.config.GuitarToneMode)
        {
            case GuitarToneMode.Off:
                break;
            case GuitarToneMode.Standard:
                Channels[programChangeEvent.Channel].Program = programChangeEvent.ProgramNumber;
                break;
            case GuitarToneMode.Simple:
                for (var i = 0; i < Channels.Length; i++)
                {
                    Channels[i].Program = programChangeEvent.ProgramNumber;
                }
                break;
            case GuitarToneMode.OverrideByTrack:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return;
    }

    private static bool TryGetToneFromProgram(SevenBitNumber program, out int tone)
    {
        tone = 0;
        if (!MidiBard.ProgramInstruments.TryGetValue(program, out var instrumentId)) return false;
        var instrument = MidiBard.Instruments[instrumentId];
        if (!instrument.IsGuitar) return false;
        tone = instrument.GuitarTone;
        return true;

    }

    static string GetNoteName(NoteEvent note) => $"{note.GetNoteName().ToString().Replace("Sharp", "#")}{note.GetNoteOctave()}";


    public static int GetNoteNumberTranslatedByTrack(int noteNumber, int trackIndex)
    {
        noteNumber += MidiBard.config.TrackStatus[trackIndex].Transpose;
        return GetNoteNumberTranslated(noteNumber);
    }

    private static int GetNoteNumberTranslated(int noteNumber)
    {
        noteNumber = noteNumber - 48 + MidiBard.config.TransposeGlobal;

        if (MidiBard.config.AdaptNotesOOR)
        {
            if (noteNumber < 0)
            {
                noteNumber = (noteNumber + 1) % 12 + 11;
            }
            else if (noteNumber > 36)
            {
                noteNumber = (noteNumber - 1) % 12 + 25;
            }
        }

        return noteNumber;
    }

    private bool IsDisposed;
    private void ReleaseUnmanagedResources()
    {
        PlaybackTicker.Ticked -= PlaybackTickerTicked;
        PlaybackTicker.Stop();
        PlaybackTicker.Dispose();
        IsDisposed = true;
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    ~BardPlayDevice()
    {
        ReleaseUnmanagedResources();
    }
}
