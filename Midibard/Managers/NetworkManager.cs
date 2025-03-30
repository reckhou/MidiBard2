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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Dalamud.Hooking;
using Dalamud.Logging;
using Dalamud.Memory;

using MidiBard.Structs;
using MidiBard.Util;

using static Dalamud.api;

#if false
namespace MidiBard.Managers
{
    class NetworkManager : IDisposable
    {
        //[StructLayout(LayoutKind.Explicit, Size = 1)]
        //public struct Note
        //{
        //	[FieldOffset(0)] public byte note;

        //	public override string ToString()
        //	{
        //		return new Melanchall.DryWetMidi.MusicTheory.Note();
        //	}
        //}

        private unsafe void SoloSend(IntPtr dataptr)
        {
            var l = 10;
            LogNotes("SoloSend", dataptr, l);
		}

        private unsafe void LogNotes(string label, IntPtr dataptr, int count)
        {
            Span<byte> notes = new Span<byte>((dataptr + 0x10).ToPointer(), count);
            Span<byte> tones = new Span<byte>((dataptr + 0x10 + count).ToPointer(), count);
            //for (int i = 0; i < notes.Length; i++)
            //{
            //	if (notes[i] is not (0xFF or 0xFE))
            //	{
            //		tones[i] = 3;
            //	}
            //}
            StringBuilder sb = new StringBuilder();
            sb.Append($"[{label}] ");
            for (int i = 0; i < count; i++)
            {
                var t = tones[i] switch
                {
                    0 => "A",
                    1 => "B",
                    2 => "C",
                    3 => "D",
                    4 => "E",
                    _ => throw new ArgumentOutOfRangeException()
                };

                var s = notes[i] switch
                {
                    0xff => "    ",
                    0xfe => "----",
                    var n => $"{n:00} {t}"
                };

                sb.Append(s + "|");
            }

            //PluginLog.Information($"[{nameof(SoloSend)}] {notes.toString()} : {tones.toString()}");
            PluginLog.Information(sb.ToString());
        }

        private unsafe void SoloRecv(uint sourceId, IntPtr data)
        {

            //var ipc = Marshal.PtrToStructure<SoloPerformanceIpc>(data);
            //PluginLog.Information($"[{nameof(SoloRecv)}] {toString(ipc.NoteNumbers)} : {toString(ipc.NoteTones)}");

        }

        private unsafe void EnsembleSend(IntPtr dataptr)
        {

#if DEBUG
           LogNotes("EnsembleSend", dataptr, 60);
#endif

        }

        private unsafe void EnsembleRecv(uint sourceId, IntPtr data)
		{

            var firstEnsemblePacket = !EnsembleManager.EnsembleRecvTime.Any();
            if (firstEnsemblePacket)
            {
                MidiBard.config.EnsembleIndicatorDelay = -(float)EnsembleManager.EnsembleTimer.Elapsed.TotalSeconds - 1.15f;
            }

            //  PluginLog.Warning($"EnsembleRecv {EnsembleManager.EnsembleTimer.Elapsed}");
            //var ipc = Marshal.PtrToStructure<EnsemblePerformanceIpc>(data);
            EnsembleManager.EnsembleRecvTime.Add((EnsembleManager.EnsembleTimer.Elapsed));
			//foreach (var perCharacterData in ipc.EnsembleCharacterDatas.Where(i => i.IsValid))
			//{
			//	//PluginLog.Information($"[{nameof(EnsembleRecv)}] {perCharacterData.CharacterId:X} {perCharacterData.NoteNumbers.toString()}");
			//}
        }

		delegate IntPtr sub_14070A1C0(uint sourceId, IntPtr data);
        private readonly Hook<sub_14070A1C0> soloReceivedHook;

        delegate IntPtr sub_14070A230(uint sourceId, IntPtr data);
        private readonly Hook<sub_14070A230> ensembleReceivedHook;

        delegate void sub_14119B2E0(IntPtr a1);
        private readonly Hook<sub_14119B2E0> soloSendHook;

        delegate void sub_14119B120(IntPtr a1);
        private readonly Hook<sub_14119B120> ensembleSendHook;

        private NetworkManager()
        {
            //ensembleSendHook = new Hook<sub_14119B120>(Offsets.EnsembleSendHandler, (dataptr) =>
            //{
            //    try
            //    {
            //        EnsembleSend(dataptr);
            //    }
            //    catch (Exception e)
            //    {
            //        PluginLog.Error(e, $"error in {nameof(ensembleSendHook)}");
            //    }

            //    ensembleSendHook.Original(dataptr);
            //});

            //soloSendHook = new Hook<sub_14119B2E0>(Offsets.SoloSendHandler, (dataptr) =>
            //{
            //    try
            //    {
            //        SoloSend(dataptr);
            //    }
            //    catch (Exception e)
            //    {
            //        PluginLog.Error(e, "error in solo send handler hook");
            //    }

            //    soloSendHook.Original(dataptr);
            //});

            //soloReceivedHook = new Hook<sub_14070A1C0>(Offsets.SoloReceivedHandler, (id, data) =>
            //{
            //    try
            //    {
            //        SoloRecv(id, data);
            //    }
            //    catch (Exception e)
            //    {
            //        PluginLog.Error(e, "error in solo recv handler hook");
            //    }
            //    return soloReceivedHook.Original(id, data);
            //});

            ensembleReceivedHook = api.GameInteropProvider.HookFromAddress<sub_14070A230>(Offsets.EnsembleReceivedHandler, (id, data) =>
            {
                try
                {
                    EnsembleRecv(id, data);
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, "error in ensemble recv handler hook");
                }
                return ensembleReceivedHook.Original(id, data);
            });


            ensembleSendHook?.Enable();
            soloSendHook?.Enable();
            soloReceivedHook?.Enable();
            ensembleReceivedHook?.Enable();
        }

        public static NetworkManager Instance { get; } = new NetworkManager();

        public void Dispose()
        {
            soloSendHook?.Dispose();
            ensembleSendHook?.Dispose();
            soloReceivedHook?.Dispose();
            ensembleReceivedHook?.Dispose();
        }
    }
}
#endif
