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
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;

using JetBrains.Annotations;

using Lumina.Excel;
using Lumina.Excel.Sheets;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Interaction;

using Midibard.Playlib;

using MidiBard;
using MidiBard.Control;
using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl;
using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.IPC;
using MidiBard.Managers;
using MidiBard.Managers.Agents;
using MidiBard.Util;
using MidiBard.Util.Lyrics;

using MidiBard2.IPC;

using static Dalamud.api;

namespace MidiBard;

public class MidiBard : IDalamudPlugin
{
    internal static readonly Version Version = typeof(MidiBard).Assembly.GetName().Version;
    internal static readonly string VersionString = Version?.ToString();
    public static Configuration config { get; internal set; }
    internal static PluginUI Ui { get; set; }
    internal static BardPlayback CurrentPlayback { get; set; }
    internal static AgentMetronome AgentMetronome { get; set; }
    internal static AgentPerformance AgentPerformance { get; set; }
    internal static EnsembleManager EnsembleManager { get; set; }
    internal static IPCManager IpcManager { get; set; }
    internal static PluginIPC PluginIpc { get; set; }
    public static BardPlayDevice BardPlayDevice { get; private set; }

    private int configSaverTick;
    private static bool wasEnsembleModeRunning = false;

    internal static ExcelSheet<Perform> InstrumentSheet;
    internal static Instrument[] Instruments;
    internal static Instrument[] Guitars;
    internal static string[] InstrumentStrings;
    internal static readonly byte[] guitarGroup = { 24, 25, 26, 27, 28 };
    internal static IDictionary<SevenBitNumber, uint> ProgramInstruments;
    internal static PartyWatcher PartyWatcher;

    internal static bool SlaveMode = false;
    internal static int CurrentInstrumentWithTone => CurrentInstrument >= 24 ? 24 + CurrentTone : CurrentInstrument;
    internal static unsafe byte CurrentInstrument => *(byte*)(Offsets.PerformanceStructPtr + 3 + Offsets.InstrumentOffset);
    internal static unsafe byte CurrentTone => *(byte*)(Offsets.PerformanceStructPtr + 3 + Offsets.InstrumentOffset + 1);
    internal static bool PlayingGuitar => InstrumentHelper.IsGuitar(CurrentInstrument);
    internal static bool IsPlaying => CurrentPlayback?.IsRunning == true;
    internal static TimeSpan? CurrentPlaybackTime => CurrentPlayback?.GetCurrentTime<MetricTimeSpan>().GetTimeSpan();
    internal static TimeSpan? CurrentPlaybackDuration => CurrentPlayback?.GetDuration<MetricTimeSpan>().GetTimeSpan();

    public string Name => "MidiBard 2";

    public unsafe MidiBard(IDalamudPluginInterface pi)
    {
        api.Initialize(this, pi);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        InstrumentSheet = api.DataManager.Excel.GetSheet<Perform>();
        Instruments = InstrumentSheet!
            .Where(i => !string.IsNullOrWhiteSpace(i.Instrument.ToDalamudString().TextValue) || i.RowId == 0)
            .Select(i => new Instrument(i))
            .ToArray();

        Guitars = Instruments.Where(i => i.IsGuitar).ToArray();
        InstrumentStrings = Instruments.Select(i => i.InstrumentString).ToArray();

        ProgramInstruments = new Dictionary<SevenBitNumber, uint>();
        foreach (var (programNumber, instrument) in Instruments.Select((i, index) => (i.ProgramNumber, index)))
        {
            ProgramInstruments[programNumber] = (uint)instrument;
        }

        TryLoadConfig();
        MidiFileConfigManager.Init();

        //migrate old playlist
        if (MidiBard.config.Playlist.Any())
        {
            PlaylistManager.CurrentContainer.SongPaths.AddRange(MidiBard.config.Playlist.Select(i => new SongEntry() { FilePath = i }));
            MidiBard.config.Playlist.Clear();
        }

        ConfigureLanguage(GetCultureCodeString((CultureCode)config.uiLang));

        IpcManager = new IPCManager();
        PartyWatcher = new PartyWatcher();
        PluginIpc = new PluginIPC();

        //playlib.init();
        OffsetManager.Setup(api.SigScanner);
        //GuitarTonePatch.InitAndApply();

        var raptureAtkModule = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->UIModule->GetRaptureAtkModule();
        var pAgentPerformanceMetronome = raptureAtkModule->AgentModule.GetAgentByInternalId(FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId.PerformanceMetronome);
        var pAgentPerformance = raptureAtkModule->AgentModule.GetAgentByInternalId(FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId.PerformanceMode);

        AgentMetronome = new AgentMetronome((IntPtr)pAgentPerformanceMetronome);
        AgentPerformance = new AgentPerformance((IntPtr)pAgentPerformance);
        EnsembleManager = new EnsembleManager();

        //#if DEBUG
        //			_ = NetworkManager.Instance;
        //			_ = Testhooks.Instance;
        //#endif
        api.ChatGui.ChatMessage += PartyChatCommand.OnChatMessage;

        BardPlayDevice = new BardPlayDevice();
        InputDeviceManager.ScanMidiDeviceThread.Start();

        Ui = new PluginUI();
        api.PluginInterface.UiBuilder.Draw += Ui.Draw;
        api.PluginInterface.UiBuilder.OpenMainUi += () => Ui.Toggle();
        api.PluginInterface.UiBuilder.OpenConfigUi += () => Ui.Toggle();
        api.Framework.Update += OnFrameworkUpdate;
        api.Framework.Update += Lrc.Tick;

        if (api.PluginInterface.IsDev) Ui.Open();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        PerformanceEvents.Instance.InPerformanceMode = AgentPerformance.InPerformanceMode;

        if (Ui.MainWindowOpened)
        {
            if (configSaverTick++ == 3600)
            {
                configSaverTick = 0;
                SaveConfig();
            }
        }

        if (!MidiBard.config.MonitorOnEnsemble) return;

        if (wasEnsembleModeRunning)
        {
            if (!AgentMetronome.EnsembleModeRunning || !AgentPerformance.InPerformanceMode)
            {
                EnsembleManager.InvokeEnsembleStop();
                if (config.StopPlayingWhenEnsembleEnds)
                {
                    MidiPlayerControl.Pause();
                }
            }
        }

        wasEnsembleModeRunning = AgentMetronome.EnsembleModeRunning && AgentPerformance.InPerformanceMode;

        if (AgentPerformance.InPerformanceMode)
        {
            Playlib.ConfirmReceiveReadyCheck();
        }
    }

    [Command("/midibard")]
    [HelpMessage("Toggle MidiBard window")]
    public void Command1(string command, string args) => OnCommand(command, args);

    [Command("/mbard")]
    [HelpMessage("Toggle MidiBard window\n")]
    public void OnCommand(string command, string args)
    {
        var argStrings = args.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        api.PluginLog.Debug($"command: {command}, {string.Join('|', argStrings)}");
        if (argStrings.Any())
        {
            switch (argStrings[0])
            {
                case "cancel":
                    PerformActions.DoPerformActionOnTick(0);
                    break;
                case "perform":
                    try
                    {
                        var instrumentInput = argStrings[1];
                        if (instrumentInput == "cancel")
                        {
                            PerformActions.DoPerformActionOnTick(0);
                        }
                        else if (uint.TryParse(instrumentInput, out var id1) && id1 < InstrumentStrings.Length)
                        {
                            SwitchInstrument.SwitchToContinue(id1);
                        }
                        else if (SwitchInstrument.TryParseInstrumentName(instrumentInput, out var id2))
                        {
                            SwitchInstrument.SwitchToContinue(id2);
                        }
                    }
                    catch (Exception e)
                    {
                        PluginLog.Warning(e, "error when parsing or finding instrument strings");
                        api.ChatGui.PrintError($"failed parsing command argument \"{args}\"");
                    }

                    break;
                case "playpause":
                    MidiPlayerControl.PlayPause();
                    break;
                case "play":
                    MidiPlayerControl.Play();
                    break;
                case "pause":
                    MidiPlayerControl.Pause();
                    break;
                case "stop":
                    MidiPlayerControl.Stop();
                    break;
                case "next":
                    MidiPlayerControl.Next();
                    break;
                case "prev":
                    MidiPlayerControl.Prev();
                    break;
                case "visual":
                    try
                    {
                        MidiBard.config.PlotTracks = argStrings[1] switch
                        {
                            "on" => true,
                            "off" => false,
                            _ => !MidiBard.config.PlotTracks
                        };
                    }
                    catch (Exception e)
                    {
                        MidiBard.config.PlotTracks ^= true;
                    }
                    break;
                case "rewind":
                    {
                        double timeInSeconds = -5;
                        try
                        {
                            timeInSeconds = -double.Parse(argStrings[1]);
                        }
                        catch (Exception e)
                        {
                        }

                        MidiPlayerControl.MoveTime(timeInSeconds);
                    }
                    break;
                case "fastforward":
                    {
                        double timeInSeconds = 5;
                        try
                        {
                            timeInSeconds = double.Parse(argStrings[1]);
                        }
                        catch (Exception e)
                        {
                        }

                        MidiPlayerControl.MoveTime(timeInSeconds);
                    }
                    break;
                case "transpose":
                    {
                        try
                        {
                            if (argStrings[1] == "set")
                            {
                                config.TransposeGlobal = int.Parse(argStrings[2]);
                            }
                            else
                            {
                                config.TransposeGlobal += int.Parse(argStrings[1]);
                            }
                        }
                        catch (Exception e)
                        {
                            //
                        }
                    }
                    break;
            }
        }
        else
        {
            Ui.Toggle();
        }
    }

    public enum CultureCode
    {
        English,
        简体中文,
        //繁體中文,
        //日本語,
        //Deutsch,
    }

    public static string GetCultureCodeString(CultureCode culture)
    {
        return culture switch
        {
            CultureCode.English => "en",
            CultureCode.简体中文 => "zh-Hans",
            //CultureCode.繁體中文 => "zh-Hant",
            //CultureCode.日本語 => "ja",
            //CultureCode.Deutsch => "de",
            _ => null
        };
    }

    //https://git.annaclemens.io/ascclemens/SoundFilter/src/commit/0a109907477bf1839e220c460253da68c6162d5c/SoundFilter/Ui/PluginUi.cs#L31
    internal static void ConfigureLanguage(string? langCode = null)
    {
        // ReSharper disable once ConstantNullCoalescingCondition
        langCode ??= api.PluginInterface.UiLanguage ?? "en";
        try
        {
            MidiBard2.Resources.Language.Culture = new CultureInfo(langCode);
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, $"Could not set culture to {langCode} - falling back to default");
            MidiBard2.Resources.Language.Culture = CultureInfo.DefaultThreadCurrentUICulture;
        }
    }

    internal static void SaveConfig()
    {
        var startNew = Stopwatch.StartNew();
        Task.Run(() =>
        {
            try
            {
                api.PluginInterface.SavePluginConfig(config);
                PluginLog.Verbose($"config saved in {startNew.Elapsed.TotalMilliseconds}ms");
            }
            catch (Exception e)
            {
                PluginLog.Warning($"error when saving config {e.Message}");
                //ImGuiUtil.AddNotification(NotificationType.Error, "Error when saving config");

            }
        });
    }

    internal static void TryLoadConfig(int trycount = 10)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                config = (Configuration)api.PluginInterface.GetPluginConfig() ?? new Configuration();
                foreach (var cur in config.TrackStatus)
                {
                    cur.Enabled = false;
                }
                config.TrackStatus[0].Enabled = true;
                return;
            }
            catch (Exception e)
            {
                if (i == trycount) throw;
                Thread.Sleep(50);
                PluginLog.Warning(e, $"error when loading config, trying again... {i}");
            }
        }
    }


    #region IDisposable Support

    void FreeUnmanagedResources()
    {
        try
        {
#if DEBUG
            Testhooks.Instance?.Dispose();
#endif
            InputDeviceManager.ShouldScanMidiDeviceThread = false;
            api.Framework.Update -= OnFrameworkUpdate;
            api.Framework.Update -= Lrc.Tick;
            api.PluginInterface.UiBuilder.OpenMainUi -= () => Ui.Toggle();
            api.PluginInterface.UiBuilder.OpenConfigUi -= () => Ui.Toggle();
            api.PluginInterface.UiBuilder.Draw -= Ui.Draw;
            PlaylistManager.CurrentContainer.Save();

            PluginIpc?.Dispose();
            EnsembleManager?.Dispose();
            PartyWatcher?.Dispose();
            IpcManager?.Dispose();
#if false
            NetworkManager.Instance.Dispose();
#endif
            InputDeviceManager.DisposeCurrentInputDevice();
            try
            {
                CurrentPlayback?.Stop();
                CurrentPlayback?.Dispose();
                CurrentPlayback = null;
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "error when disposing playback");
            }

            BardPlayDevice?.Dispose();
            //GuitarTonePatch.Dispose();
            Dalamud.api.Dispose();
        }
        catch (Exception e2)
        {
            PluginLog.Error(e2, "error when disposing midibard");
        }
    }

    public void Dispose()
    {
        try
        {
            SaveConfig();
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "error when saving config file");
        }

        api.ChatGui.ChatMessage -= PartyChatCommand.OnChatMessage;
        //Cbase.Dispose();

        FreeUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    ~MidiBard()
    {
        FreeUnmanagedResources();
    }
    #endregion
}
