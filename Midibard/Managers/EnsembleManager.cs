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
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Dalamud.Hooking;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Logging;

using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;

using Midibard.Playlib;

using MidiBard.Control;
using MidiBard.Control.MidiControl;
using MidiBard.IPC;
using MidiBard.Managers.Agents;
using MidiBard.Structs;

using static Dalamud.api;
using static MidiBard.MidiBard;

namespace MidiBard.Managers;

internal class EnsembleManager : IDisposable
{
    //public SyncHelper(out List<(byte[] notes, byte[] tones)> sendNotes, out List<(byte[] notes, byte[] tones)> recvNotes)
    //{
    //	sendNotes = new List<(byte[] notes, byte[] tones)>();
    //	recvNotes = new List<(byte[] notes, byte[] tones)>();
    //}

    //private delegate IntPtr sub_140C87B40(IntPtr agentMetronome, byte beat);
    //   private Hook<sub_140C87B40> UpdateMetronomeHook;

    private delegate long sub_1410F4EC0(IntPtr a1, IntPtr a2);
    private Hook<sub_1410F4EC0> NetworkEnsembleHook;
    internal EnsembleManager()
    {
        //UpdateMetronomeHook = new Hook<sub_140C87B40>(Offsets.UpdateMetronome, HandleUpdateMetronome);
        //UpdateMetronomeHook.Enable();


        NetworkEnsembleHook = api.GameInteropProvider.HookFromAddress<sub_1410F4EC0>(Offsets.NetworkEnsembleStart, (a1, a2) =>
        {
            if (config.MonitorOnEnsemble) StartEnsemble();
            return NetworkEnsembleHook.Original(a1, a2);
        });
        NetworkEnsembleHook.Enable();

        EnsembleStopped += () => EnsembleTimer.Reset();
    }

    internal static List<TimeSpan> EnsembleRecvTime { get; } = new();

    internal static unsafe void BeginEnsembleReadyCheck()
    {
        var ensembleRunning = MidiBard.AgentMetronome.EnsembleModeRunning;
        if (!ensembleRunning)
        {
            if (MidiBard.AgentPerformance.InPerformanceMode && !MidiBard.AgentMetronome.Struct->AgentInterface.IsAgentActive())
            {
                MidiBard.AgentMetronome.Struct->AgentInterface.Show();
            }

            Playlib.BeginReadyCheck();
            Playlib.ConfirmBeginReadyCheck();
        }
    }

    internal static unsafe void StopEnsemble()
    {
        Playlib.BeginReadyCheck();
        Playlib.SendAction("SelectYesno", 3, 0);
    }

    //private unsafe IntPtr HandleUpdateMetronome(IntPtr agentMetronome, byte currentBeat)
    //{
    //	var original = UpdateMetronomeHook.Original(agentMetronome, currentBeat);
    //	try
    //	{
    //		if (MidiBard.config.MonitorOnEnsemble)
    //		{
    //			var metronome = ((AgentMetronome.AgentMetronomeStruct*)agentMetronome);
    //			var beatsPerBar = metronome->MetronomeBeatsPerBar;
    //			var barElapsed = metronome->MetronomeBeatsElapsed;
    //			var ensembleRunning = metronome->EnsembleModeRunning;
    //               PluginLog.Verbose($"[Metronome] {barElapsed} {currentBeat}/{beatsPerBar}");

    //               if (barElapsed == -2 && currentBeat == 0 && ensembleRunning != 0)
    //               {
    //                   PluginLog.Warning($"Prepare: ensemble: {ensembleRunning}");
    //                   StartEnsemble();
    //               }
    //           }
    //	}
    //	catch (Exception e)
    //	{
    //		PluginLog.Error(e, $"error in {nameof(UpdateMetronomeHook)}");
    //	}

    //	return original;
    //}

    private static void StartEnsemble()
    {
        EnsembleRecvTime.Clear();
        EnsemblePrepare?.Invoke();

        //if playback is null, cancel ensemble mode.
        if (CurrentPlayback == null)
        {
            if (MidiBard.config.SyncClients)
            {
                StopEnsemble();
                ImGuiUtil.AddNotification(NotificationType.Error, "Please load a song before starting ensemble!");
                IPC.IPCHandles.ErrPlaybackNull(Dalamud.api.ClientState.LocalPlayer?.Name.ToString());
            }
        }
        else
        {
            EnsembleTimer.Restart();
            MidiBard.CurrentPlayback.Stop();
            MidiBard.CurrentPlayback.MoveToStart();

            try
            {
                MidiPlayerControl.DoPlay(true);
                PluginLog.Warning($"Start ensemble: sw: {EnsembleTimer.Elapsed.TotalMilliseconds}ms");
                EnsembleStart?.Invoke();
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "error EnsembleStart");
            }
        }
    }

    public static int GetCompensationNew(int instrument, int note)
    {
        try
        {
            switch (config.CompensationMode)
            {
                case Configuration.CompensationModes.None:
                    return 0;
                case Configuration.CompensationModes.ByInstrument:
                    {
                        var compensation = config.LegacyInstrumentCompensation;
                        var max = compensation.Max(i => i);
                        return max - compensation[instrument];
                    }
                case Configuration.CompensationModes.ByInstrumentNote:
                    {
                        // other events, make sure it's ahead of any note event
                        if (note < 0)
                        {
                            return CompensationMax - CompensationMin[instrument];
                        }
                        return CompensationMax - Compensation10pct[instrument][note];
                    }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (Exception e)
        {
            PluginLog.Error(e, $"error when getting Compensation value. instrument: {instrument}, note: {note}");
            return 0;
        }
    }

    /// <summary>
    ///     Takes the time ms when first sample volume > 10% * max volume
    /// </summary>
    private static readonly byte[][] Compensation10pct =
    {
        Enumerable.Repeat((byte)0, 37).ToArray(),
        new byte[] // 1 / Harp / 047harp.scd
		{
            66, 66, 66, 66, 66, 66, 66, 66, 66, 66, 66, // scd3
			65, 65, 65, 65, 65, 65, 65, 65, 65,         // scd2
			66, 66, 66, 66, 66, 66, 66, 66, 66,         // scd0
			65, 65, 65, 65, 65, 65, 65, 65              // scd1
		},
        new byte[] // 2 / Piano / 001grandpiano.scd
		{
            70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, // scd4
			70, 70, 70, 70, 70,                                             // scd3
			70, 70, 70, 70, 70,                                             // scd1
			69, 69, 69, 69,                                                 // scd2
			70, 70, 70, 70, 70, 70, 70                                      // scd0
		},
        new byte[] // 3 / Lute / 026steelguitar.scd
		{
            81, 81, 81, 81, 81, 81, 81, 81, 81, 81, 81, 81, 81, 81, 81, // scd3
			78, 78, 78, 78, 78, 78, 78,                                 // scd2
			79, 79, 79, 79, 79, 79, 79,                                 // scd1
			79, 79, 79, 79, 79, 79, 79, 79                              // scd0
		},
        new byte[] // 4 / Fiddle / 046pizzicato.scd
		{
            78, 78, 78,                        // scd1
			68, 68, 68, 68,                    // scd0
			79, 79, 79, 79,                    // scd2
			68, 68, 68,                        // scd3
			67, 67, 67, 67, 67,                // scd4
			72, 72, 72,                        // scd5
			69, 69, 69, 69, 69, 69,            // scd6
			71, 71, 71, 71, 71, 71, 71, 71, 71 // scd7
		},
        new byte[] // 5 / Flute / 074flute.scd
		{
            70, 70, 70, 70, 70, 70, 70, 70, 70, 70, // scd5
			79, 79, 79, 79, 79,                     // scd4
			72, 72, 72, 72,                         // scd3
			77, 77, 77, 77,                         // scd2
			80, 80, 80, 80, 80,                     // scd1
			82, 82, 82, 82, 82, 82, 82, 82, 82      // scd0
		},
        new byte[] // 6 / Oboe / 069oboe.scd
		{
            75, 75, 75, 75, 75, 75, 75, 75, 75, 75,                    // scd4
			74, 74, 74, 74, 74,                                        // scd2
			73, 73, 73,                                                // scd3
			70, 70, 70, 70,                                            // scd1
			68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68 // scd0
		},
        new byte[] // 7 / Clarinet / 072clarinet.scd
		{
            147, 147, 147, 147, 147, 147, 147, 147, 147, 147, 147, // scd4
			79, 79, 79, 79, 79, 79,                                // scd3
			73, 73, 73, 73, 73,                                    // scd2
			78, 78, 78, 78, 78, 78, 78,                            // scd1
			69, 69, 69, 69, 69, 69, 69, 69                         // scd0
		},
        new byte[] // 8 / Fife / 073piccolo.scd
		{
            70, 70, 70, 70, 70, 70, 70, 70,                    // scd4
			73, 73, 73, 73, 73, 73,                            // scd1
			83, 83, 83,                                        // scd0
			88, 88, 88, 88, 88, 88, 88,                        // scd2
			85, 85, 85, 85, 85, 85, 85, 85, 85, 85, 85, 85, 85 // scd3
		},
        new byte[] // 9 / Panpipes / 076panflute.scd
		{
            69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, // scd2
			71, 71, 71, 71,                                                 // scd1
			71, 71, 71, 71,                                                 // scd3
			70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70              // scd0
		},
        new byte[] // 10 / Timpani / 048timpani.scd
		{
            67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, // scd2
			65, 65, 65, 65, 65, 65, 65, 65,                                 // scd1
			67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67              // scd0
		},
        new byte[] // 11 / Bongo / 097bongo.scd
		{
            69, 69, 69, 69, 69, 69, 69, 69,                            // scd2
			65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65,    // scd1
			54, 54, 54, 54, 54, 54, 54, 54, 54, 54, 54, 54, 54, 54, 54 // scd0
		},
        new byte[] // 12 / Bass Drum / 098bd.scd
		{
            71, 71, 71, 71, 71, 71, 71,                        // scd3
			65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65,        // scd2
			55, 55, 55, 55, 55, 55,                            // scd0
			46, 46, 46, 46, 46, 46, 46, 46, 46, 46, 46, 46, 46 // scd1
		},
        new byte[] // 13 / Snare Drum / 099snare.scd
		{
            71, 71, 71, 71, 71, 71, 71, 71, 71, 71, 71, 71,    // scd0
			63, 63, 63, 63, 63, 63, 63, 63,                    // scd1
			62, 62, 62, 62,                                    // scd2
			55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55 // scd3
		},
        new byte[] // 14 / Cymbal / 100cymbal.scd
		{
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 // scd0
		},
        new byte[] // 15 / Trumpet / 057trumpet.scd
		{
            17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, // scd2
			23, 23, 23, 23,                                             // scd1
			8, 8, 8, 8, 8,                                              // scd4
			6, 6, 6,                                                    // scd5
			4, 4, 4, 4,                                                 // scd3
			20, 20, 20, 20, 20, 20                                      // scd0
		},
        new byte[] // 16 / Trombone / 058trombone.scd
		{
            9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, // scd0
			8, 8, 8, 8, 8,                               // scd1
			9, 9, 9, 9,                                  // scd2
			5, 5, 5,                                     // scd3
			9, 9, 9, 9, 9, 9, 9, 9, 9, 9                 // scd4
		},
        new byte[] // 17 / Tuba / 059tuba.scd
		{
            15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, // scd0
			7, 7, 7,                                    // scd5
			13, 13, 13, 13, 13,                         // scd1
			4, 4, 4, 4, 4,                              // scd3
			7, 7, 7, 7, 7,                              // scd2
			27, 27, 27, 27, 27, 27, 27, 27              // scd4
		},
        new byte[] // 18 / Horn / 061frenchhorn.scd
		{
            5, 5, 5, 5, 5, 5, 5, 5, 5,                  // scd0
			10, 10, 10, 10, 10, 10,                     // scd3
			10, 10, 10,                                 // scd4
			6, 6, 6, 6,                                 // scd2
			6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6 // scd1
		},
        new byte[] // 19 / Saxophone / 066altosax.scd
		{
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, // scd3
			6, 6, 6, 6, 6, 6,                   // scd2
			7, 7, 7, 7, 7, 7, 7, 7,             // scd1
			8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8     // scd0
		},
        new byte[] // 20 / Violin / 041violin.scd
		{
            23, 23, 23, 23, 23, 23, 23, 23, 23, 23, // scd0
			18, 18, 18, 18, 18, 18, 18, 18, 18,     // scd1
			18, 18, 18, 18, 18, 18, 18,             // scd4
			14, 14, 14, 14,                         // scd3
			11, 11, 11, 11, 11, 11, 11              // scd2
		},
        new byte[] // 21 / Viola / 042viola.scd
		{
            16, 16, 16, 16, 16, 16, 16, 16, 16, // scd0
			15, 15, 15, 15, 15, 15,             // scd1
			15, 15, 15, 15, 15, 15, 15, 15,     // scd2
			13, 13, 13, 13, 13, 13, 13, 13,     // scd3
			13, 13, 13, 13, 13, 13              // scd4
		},
        new byte[] // 22 / Cello / 043cello.scd
		{
            21, 21, 21, 21, 21, 21,            // scd0
			9, 9, 9, 9, 9, 9, 9, 9,            // scd1
			17, 17, 17, 17, 17, 17, 17, 17,    // scd2
			13, 13, 13, 13, 13, 13,            // scd3
			17, 17, 17, 17, 17, 17, 17, 17, 17 // scd4
		},
        new byte[] // 23 / Double Bass / 044contrabass.scd
		{
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5,      // scd0
			10, 10, 10, 10, 10,                // scd1
			11, 11, 11, 11, 11, 11, 11,        // scd2
			8, 8, 8, 8, 8, 8,                  // scd3
			12, 12, 12, 12, 12, 12, 12, 12, 12 // scd4
		},
        new byte[] // 24 / Electric Guitar: Overdriven / 030driveguitar.scd
		{
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, // scd3
			1, 1, 1, 1, 1, 1,                // scd0
			3, 3, 3, 3, 3,                   // scd4
			3, 3, 3, 3, 3, 3, 3, 3, 3,       // scd1
			3, 3, 3, 3, 3, 3                 // scd2
		},
        new byte[] // 25 / Electric Guitar: Clean / 028cleanguitar.scd
		{
            6, 6, 6, 6, 6, 6, 6, 6, 6, 6, // scd4
			4, 4, 4, 4, 4, 4, 4,          // scd0
			4, 4, 4, 4, 4, 4, 4,          // scd3
			6, 6, 6, 6, 6, 6, 6,          // scd1
			4, 4, 4, 4, 4, 4              // scd2
		},
        new byte[] // 26 / Electric Guitar: Muted / 029muteguitar.scd
		{
            65, 65, 65,     // scd3
			60, 60, 60,     // scd9
			68, 68, 68, 68, // scd6
			63, 63, 63, 63, // scd2
			74, 74, 74, 74, // scd8
			74, 74, 74, 74, // scd5
			68, 68, 68, 68, // scd1
			69, 69, 69, 69, // scd7
			70, 70, 70, 70, // scd4
			68, 68, 68      // scd0
		},
        new byte[] // 27 / Electric Guitar: Power Chords / 031powerguitar.scd
		{
            8, 8, 8, 8, 8, 8, 8, 8, 8, // scd0
			1, 1, 1, 1, 1, 1, 1, 1,    // scd1
			6, 6, 6, 6, 6,             // scd2
			0, 0, 0, 0, 0, 0, 0,       // scd3
			6, 6, 6, 6, 6, 6, 6, 6     // scd4
		},
        new byte[] // 28 / Electric Guitar: Special / 032fxguitar.scd
		{
            11, 11, 11, 11, 11, 11,    // scd0
			0, 0, 0, 0, 0, 0, 0,       // scd1
			0, 0, 0, 0, 0, 0, 0,       // scd2
			5, 5, 5, 5, 5, 5, 5, 5, 5, // scd3
			0, 0, 0, 0, 0, 0, 0, 0     // scd4
		}
    };

    /// <summary>
    ///     Takes the time ms when first sample volume > 0% * max volume
    /// </summary>
    private static readonly byte[][] Compensation0pct =
    {
        Enumerable.Repeat((byte)0, 37).ToArray(),
        new byte[] // 1 / Harp / 047harp.scd
		{
            65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, // scd3
			64, 64, 64, 64, 64, 64, 64, 64, 64,         // scd2
			65, 65, 65, 65, 65, 65, 65, 65, 65,         // scd0
			64, 64, 64, 64, 64, 64, 64, 64              // scd1
		},
        new byte[] // 2 / Piano / 001grandpiano.scd
		{
            69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, 69, // scd4
			68, 68, 68, 68, 68,                                             // scd3
			68, 68, 68, 68, 68,                                             // scd1
			68, 68, 68, 68,                                                 // scd2
			68, 68, 68, 68, 68, 68, 68                                      // scd0
		},
        new byte[] // 3 / Lute / 026steelguitar.scd
		{
            72, 72, 72, 72, 72, 72, 72, 72, 72, 72, 72, 72, 72, 72, 72, // scd3
			72, 72, 72, 72, 72, 72, 72,                                 // scd2
			73, 73, 73, 73, 73, 73, 73,                                 // scd1
			72, 72, 72, 72, 72, 72, 72, 72                              // scd0
		},
        new byte[] // 4 / Fiddle / 046pizzicato.scd
		{
            65, 65, 65,                        // scd1
			66, 66, 66, 66,                    // scd0
			66, 66, 66, 66,                    // scd2
			65, 65, 65,                        // scd3
			65, 65, 65, 65, 65,                // scd4
			65, 65, 65,                        // scd5
			65, 65, 65, 65, 65, 65,            // scd6
			65, 65, 65, 65, 65, 65, 65, 65, 65 // scd7
		},
        new byte[] // 5 / Flute / 074flute.scd
		{
            65, 65, 65, 65, 65, 65, 65, 65, 65, 65, // scd5
			65, 65, 65, 65, 65,                     // scd4
			65, 65, 65, 65,                         // scd3
			65, 65, 65, 65,                         // scd2
			67, 67, 67, 67, 67,                     // scd1
			66, 66, 66, 66, 66, 66, 66, 66, 66      // scd0
		},
        new byte[] // 6 / Oboe / 069oboe.scd
		{
            67, 67, 67, 67, 67, 67, 67, 67, 67, 67,                    // scd4
			67, 67, 67, 67, 67,                                        // scd2
			68, 68, 68,                                                // scd3
			64, 64, 64, 64,                                            // scd1
			64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64 // scd0
		},
        new byte[] // 7 / Clarinet / 072clarinet.scd
		{
            126, 126, 126, 126, 126, 126, 126, 126, 126, 126, 126, // scd4
			63, 63, 63, 63, 63, 63,                                // scd3
			64, 64, 64, 64, 64,                                    // scd2
			66, 66, 66, 66, 66, 66, 66,                            // scd1
			66, 66, 66, 66, 66, 66, 66, 66                         // scd0
		},
        new byte[] // 8 / Fife / 073piccolo.scd
		{
            65, 65, 65, 65, 65, 65, 65, 65,                    // scd4
			67, 67, 67, 67, 67, 67,                            // scd1
			66, 66, 66,                                        // scd0
			67, 67, 67, 67, 67, 67, 67,                        // scd2
			68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68, 68 // scd3
		},
        new byte[] // 9 / Panpipes / 076panflute.scd
		{
            63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, // scd2
			66, 66, 66, 66,                                                 // scd1
			66, 66, 66, 66,                                                 // scd3
			66, 66, 66, 66, 66, 66, 66, 66, 66, 66, 66, 66, 66              // scd0
		},
        new byte[] // 10 / Timpani / 048timpani.scd
		{
            67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, // scd2
			65, 65, 65, 65, 65, 65, 65, 65,                                 // scd1
			67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67, 67              // scd0
		},
        new byte[] // 11 / Bongo / 097bongo.scd
		{
            69, 69, 69, 69, 69, 69, 69, 69,                            // scd2
			65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65, 65,    // scd1
			53, 53, 53, 53, 53, 53, 53, 53, 53, 53, 53, 53, 53, 53, 53 // scd0
		},
        new byte[] // 12 / Bass Drum / 098bd.scd
		{
            70, 70, 70, 70, 70, 70, 70,                        // scd3
			64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64,        // scd2
			53, 53, 53, 53, 53, 53,                            // scd0
			46, 46, 46, 46, 46, 46, 46, 46, 46, 46, 46, 46, 46 // scd1
		},
        new byte[] // 13 / Snare Drum / 099snare.scd
		{
            70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70,    // scd0
			63, 63, 63, 63, 63, 63, 63, 63,                    // scd1
			62, 62, 62, 62,                                    // scd2
			55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55 // scd3
		},
        new byte[] // 14 / Cymbal / 100cymbal.scd
		{
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 // scd0
		},
        new byte[] // 15 / Trumpet / 057trumpet.scd
		{
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // scd2
			3, 3, 3, 3,                                  // scd1
			7, 7, 7, 7, 7,                               // scd4
			4, 4, 4,                                     // scd5
			1, 1, 1, 1,                                  // scd3
			13, 13, 13, 13, 13, 13                       // scd0
		},
        new byte[] // 16 / Trombone / 058trombone.scd
		{
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // scd0
			3, 3, 3, 3, 3,                               // scd1
			4, 4, 4, 4,                                  // scd2
			1, 1, 1,                                     // scd3
			3, 3, 3, 3, 3, 3, 3, 3, 3, 3                 // scd4
		},
        new byte[] // 17 / Tuba / 059tuba.scd
		{
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, // scd0
			1, 1, 1,                         // scd5
			1, 1, 1, 1, 1,                   // scd1
			1, 1, 1, 1, 1,                   // scd3
			2, 2, 2, 2, 2,                   // scd2
			2, 2, 2, 2, 2, 2, 2, 2           // scd4
		},
        new byte[] // 18 / Horn / 061frenchhorn.scd
		{
            1, 1, 1, 1, 1, 1, 1, 1, 1,                  // scd0
			1, 1, 1, 1, 1, 1,                           // scd3
			3, 3, 3,                                    // scd4
			1, 1, 1, 1,                                 // scd2
			2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 // scd1
		},
        new byte[] // 19 / Saxophone / 066altosax.scd
		{
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // scd3
			3, 3, 3, 3, 3, 3,                   // scd2
			2, 2, 2, 2, 2, 2, 2, 2,             // scd1
			3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3     // scd0
		},
        new byte[] // 20 / Violin / 041violin.scd
		{
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, // scd0
			1, 1, 1, 1, 1, 1, 1, 1, 1,    // scd1
			3, 3, 3, 3, 3, 3, 3,          // scd4
			1, 1, 1, 1,                   // scd3
			2, 2, 2, 2, 2, 2, 2           // scd2
		},
        new byte[] // 21 / Viola / 042viola.scd
		{
            4, 4, 4, 4, 4, 4, 4, 4, 4, // scd0
			3, 3, 3, 3, 3, 3,          // scd1
			3, 3, 3, 3, 3, 3, 3, 3,    // scd2
			3, 3, 3, 3, 3, 3, 3, 3,    // scd3
			2, 2, 2, 2, 2, 2           // scd4
		},
        new byte[] // 22 / Cello / 043cello.scd
		{
            6, 6, 6, 6, 6, 6,         // scd0
			1, 1, 1, 1, 1, 1, 1, 1,   // scd1
			3, 3, 3, 3, 3, 3, 3, 3,   // scd2
			2, 2, 2, 2, 2, 2,         // scd3
			3, 3, 3, 3, 3, 3, 3, 3, 3 // scd4
		},
        new byte[] // 23 / Double Bass / 044contrabass.scd
		{
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // scd0
			2, 2, 2, 2, 2,                // scd1
			2, 2, 2, 2, 2, 2, 2,          // scd2
			2, 2, 2, 2, 2, 2,             // scd3
			3, 3, 3, 3, 3, 3, 3, 3, 3     // scd4
		},
        new byte[] // 24 / Electric Guitar: Overdriven / 030driveguitar.scd
		{
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, // scd3
			1, 1, 1, 1, 1, 1,                // scd0
			3, 3, 3, 3, 3,                   // scd4
			2, 2, 2, 2, 2, 2, 2, 2, 2,       // scd1
			3, 3, 3, 3, 3, 3                 // scd2
		},
        new byte[] // 25 / Electric Guitar: Clean / 028cleanguitar.scd
		{
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, // scd4
			0, 0, 0, 0, 0, 0, 0,          // scd0
			3, 3, 3, 3, 3, 3, 3,          // scd3
			1, 1, 1, 1, 1, 1, 1,          // scd1
			3, 3, 3, 3, 3, 3              // scd2
		},
        new byte[] // 26 / Electric Guitar: Muted / 029muteguitar.scd
		{
            64, 64, 64,     // scd3
			59, 59, 59,     // scd9
			67, 67, 67, 67, // scd6
			62, 62, 62, 62, // scd2
			64, 64, 64, 64, // scd8
			66, 66, 66, 66, // scd5
			59, 59, 59, 59, // scd1
			65, 65, 65, 65, // scd7
			66, 66, 66, 66, // scd4
			66, 66, 66      // scd0
		},
        new byte[] // 27 / Electric Guitar: Power Chords / 031powerguitar.scd
		{
            6, 6, 6, 6, 6, 6, 6, 6, 6, // scd0
			1, 1, 1, 1, 1, 1, 1, 1,    // scd1
			6, 6, 6, 6, 6,             // scd2
			0, 0, 0, 0, 0, 0, 0,       // scd3
			6, 6, 6, 6, 6, 6, 6, 6     // scd4
		},
        new byte[] // 28 / Electric Guitar: Special / 032fxguitar.scd
		{
            0, 0, 0, 0, 0, 0,          // scd0
			0, 0, 0, 0, 0, 0, 0,       // scd1
			0, 0, 0, 0, 0, 0, 0,       // scd2
			0, 0, 0, 0, 0, 0, 0, 0, 0, // scd3
			0, 0, 0, 0, 0, 0, 0, 0     // scd4
		}
    };

    public static readonly int CompensationMax = Compensation10pct.Max(i => i.Max());
    public static readonly int[] CompensationMin = Compensation10pct.Select(i => (int)i.Min()).ToArray();
    public static int[] GetCompensationAver() => Compensation10pct.Select(i => (int)Math.Round(i.Select(b => (double)b).Average())).ToArray();

    public static Dictionary<int, int> DefaultInstrumentCompensations => new()
    {
        //[0] = 105,
        [1] = 85,
        [2] = 90,
        [3] = 105,
        [4] = 90,
        [5] = 95,
        [6] = 95,
        [7] = 95,
        [8] = 95,
        [9] = 90,
        [10] = 90,
        [11] = 80,
        [12] = 80,
        [13] = 85,
        [14] = 30,
        [15] = 30,
        [16] = 30,
        [17] = 30,
        [18] = 30,
        [19] = 30,
        [20] = 30,
        [21] = 30,
        [22] = 30,
        [23] = 30,
        [24] = 30,
        [25] = 30,
        [26] = 30,
        [27] = 30,
        [28] = 30,
    };

    internal static void InvokeEnsembleStop() => EnsembleStopped?.Invoke();

    public static event Action EnsembleStart;

    public static event Action EnsemblePrepare;

    public static event Action EnsembleStopped;

    public static readonly Stopwatch EnsembleTimer = new Stopwatch();
    public static bool EnsembleRunning => EnsembleTimer.IsRunning;

    public void Dispose()
    {
        NetworkEnsembleHook?.Dispose();
        //UpdateMetronomeHook?.Dispose();
    }
}
