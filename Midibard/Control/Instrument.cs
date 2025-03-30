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

using Dalamud.Interface.Textures;
using Dalamud.Utility;

using Lumina.Excel.Sheets;

using Melanchall.DryWetMidi.Common;

using MidiBard.Managers;
using MidiBard.Util;

namespace MidiBard.Control;

public class Instrument
{
    public Instrument(Perform row)
    {
        Row = row;
        GuitarTone = InstrumentHelper.GetGuitarTone((int)row.RowId);
        IsGuitar = InstrumentHelper.IsGuitar((int)row.RowId);
        ProgramNumber = Row.GetMidiProgramId();
        FFXIVDisplayName = row.Instrument.ToDalamudString().TextValue;
        FFXIVProgramName = Row.GetGameProgramName();
        GeneralMidiProgramName = ProgramNumber.GetGMProgramName();
        InstrumentString = $"{(row.RowId == 0 ? "None" : $"{row.Instrument.ToDalamudString().TextValue} ({row.Name})")}";
        IconTextureWrap = TextureManager.Get((uint)row.Icon);
    }
    public Perform Row { get; }
    public bool IsGuitar { get; }
    public int GuitarTone { get; }
    public SevenBitNumber ProgramNumber { get; }
    public string FFXIVDisplayName { get; }
    public string FFXIVProgramName { get; }
    public string GeneralMidiProgramName { get; }

    public readonly string InstrumentString;
    public override string ToString() => InstrumentString;
    public ISharedImmediateTexture IconTextureWrap { get; }
}
