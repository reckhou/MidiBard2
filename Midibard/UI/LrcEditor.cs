using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Utility;

using ImGuiNET;

using Lumina.Excel.Sheets;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.IPC;
using MidiBard.UI.Win32;
using MidiBard.Util;
using MidiBard.Util.Lyrics;

using static Dalamud.api;
using static ImGuiNET.ImGui;
using static MidiBard.ImGuiUtil;

namespace MidiBard;

public class LrcEntry
{
    public string Text = "";
    public TimeSpan TimeStamp;
}

public class LrcEditor
{
    private const string LrcFileFilter = "Lyric file (*.lrc)|*.lrc";
    private string newTagName = "";
    private string newTagValue = "";

    private LrcEditor()
    { }

    public static LrcEditor Instance { get; } = new();
    private (int index, LrcEntry) DragDropSource { get; set; }
    private Lrc EditingLrc { get; set; } = GetEmptyLrc;
    private static Lrc GetEmptyLrc => new(new[] { "[0:00.0]" });
    private bool unsaved { get; set; }
    private ImGuiSortDirection lastSortDirection { get; set; } = ImGuiSortDirection.None;
    private Regex LrcTimeFormat { get; } = new(@"(?<min>\d+):(?<sec>\d{1,2})\.(?<ff>\d+)", RegexOptions.Compiled);
    private List<LrcEntry> LrcLines => EditingLrc.LrcLines;

    private Lrc LrcPending { get; set; }
    internal static Lrc GetLrcFromPlayback(BardPlayback currentPlayback)
    {
        var newLrc = GetEmptyLrc;
        if (currentPlayback is not null)
        {
            newLrc.LrcMetadata["ti"] = currentPlayback.DisplayName;
            newLrc.LrcMetadata["length"] = Lrc.ToLrcTime(currentPlayback.GetDuration<MetricTimeSpan>());
            newLrc.FilePath = Path.ChangeExtension(currentPlayback.FilePath, "lrc");
        }

        return newLrc;
    }
    internal static Lrc GetLrcFromSongEntry(SongEntry songEntry)
    {
        var newLrc = GetEmptyLrc;
        if (songEntry is null) return newLrc;

        var lrcPath = songEntry.LrcPath;
        if (File.Exists(lrcPath))
        {
            return new Lrc(lrcPath);
        }
        PluginLog.Information("file not exist, create new lrc");

        newLrc.LrcMetadata["ti"] = songEntry.FileName;
        newLrc.LrcMetadata["length"] = Lrc.ToLrcTime(PlaylistManager.LoadSongFile(songEntry.FilePath)?.GetDurationTimeSpan() ?? TimeSpan.Zero);
        newLrc.FilePath = Path.ChangeExtension(songEntry.FilePath, "lrc");

        return newLrc;
    }
    public void LoadLrcToEditor(Lrc lrc)
    {
        if (lrc is null)
        {
            return;
        }

        if (unsaved)
        {
            LrcPending = lrc;
            return;
        }

        EditingLrc = lrc;
        unsaved = false;
    }

    private bool TryParseLrcTimeSpan(string input, out TimeSpan timeSpan)
    {
        var match = LrcTimeFormat.Match(input);
        var minutes = match.Groups["min"];
        var seconds = match.Groups["sec"];
        var ff = match.Groups["ff"];
        if (match.Success)
        {
            var fractionSecond = double.Parse($"0.{ff.Value}", CultureInfo.InvariantCulture);
            timeSpan = TimeSpan.FromMinutes(int.Parse(minutes.Value)) + TimeSpan.FromSeconds(int.Parse(seconds.Value) + fractionSecond);
            return true;
        }

        timeSpan = TimeSpan.Zero;
        return false;
    }

    public bool Visible = false;
    public void Show() => Visible = true;
    public void Close() => Visible = false;
    public unsafe void Draw()
    {
        if (Visible && Begin($"{Path.GetFileName(EditingLrc.FilePath) ?? "Lrc Editor"}###Lyric Editor", ref Visible, unsaved ? ImGuiWindowFlags.UnsavedDocument : ImGuiWindowFlags.None))
        {
            if (LrcPending != null)
            {
                OpenPopup("Save?");
            }

            var open = true;
            PushStyleVar(ImGuiStyleVar.WindowTitleAlign, new Vector2(0.5f));
            var wdl = GetWindowDrawList();
            var clipRect = wdl.GetClipRectMin() + wdl.GetClipRectMax();
            clipRect /= 2;
            SetNextWindowPos(clipRect, ImGuiCond.Appearing, Vector2.One / 2);
            if (BeginPopupModal("Save?", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Dummy(ImGuiHelpers.ScaledVector2(20));
                TextCenterAligned("Editor has unsaved changes. Save now?");
                ImGui.Dummy(ImGuiHelpers.ScaledVector2(20));
                ImGui.Dummy(ImGuiHelpers.ScaledVector2(60, 30)); SameLine();
                if (Button("Save", new Vector2(GetFrameHeight() * 4, GetFrameHeight())))
                {
                    AskSave();
                    unsaved = false;
                    LoadLrcToEditor(LrcPending);
                    LrcPending = null;
                    CloseCurrentPopup();
                }

                SameLine();
                if (Button("Discard", new Vector2(GetFrameHeight() * 4, GetFrameHeight())))
                {
                    unsaved = false;
                    LoadLrcToEditor(LrcPending);
                    LrcPending = null;
                    CloseCurrentPopup();
                }
                SameLine();
                ImGui.Dummy(ImGuiHelpers.ScaledVector2(60, 30));

                EndPopup();
            }
            PopStyleVar();
            if (!open)
            {
                LrcPending = null;
            }

            var currentPlayback = MidiBard.CurrentPlayback;
            if (Button("New"))
            {
                var newLrc = GetLrcFromPlayback(currentPlayback);
                LoadLrcToEditor(newLrc);
            }

            SameLine();
            if (Button("Open"))
            {
                FileDialogs.OpenFileDialog((selected, filename, _) =>
                {
                    if (!selected) return;
                    try
                    {
                        LoadLrcToEditor(new Lrc(filename));
                        unsaved = false;
                    }
                    catch (Exception e)
                    {
                        PluginLog.Error(e, "error when opening lrc file");
                    }
                }, LrcFileFilter, false);
            }

            //SameLine();
            //if (Button("Load from playing"))
            //{
            //    LoadLrcToEditor(Lrc.PlayingLrc.JsonClone());
            //}

            SameLine();
            if (Button("Save"))
            {
                AskSave();
            }
            ToolTip(EditingLrc.FilePath is null ? "Select save location" : $"Save to: {EditingLrc.FilePath}");

            if (false)
            {
                SameLine();
                if (Button("Random"))
                {
                    var dura = MidiBard.CurrentPlaybackDuration ?? TimeSpan.Zero;
                    var count = 32;
                    //if (currentPlayback is not null)
                    //{
                    //    EditingLrc.LrcMetadata["ti"] = currentPlayback.DisplayName ?? "";
                    //    EditingLrc.LrcMetadata["length"] = Lrc.ToLrcTime(MidiBard.CurrentPlaybackDuration ?? TimeSpan.Zero);
                    //}

                    LrcLines.Clear();
                    LrcLines.AddRange(Enumerable.Range(0, count).Select(i => new LrcEntry { TimeStamp = dura / count * i }));
                    var bNpcNames = api.DataManager.GetExcelSheet<BNpcName>()!.Where(i => !string.IsNullOrWhiteSpace(i.Singular.ToDalamudString().TextValue)).ToList();
                    LrcLines.ForEach(i =>
                    {
                        i.Text = string.Join(' ',
                            bNpcNames[Random.Shared.Next(0, bNpcNames.Count)].Singular.ToDalamudString().TextValue,
                            bNpcNames[Random.Shared.Next(0, bNpcNames.Count)].Singular.ToDalamudString().TextValue,
                            //bNpcNames[Random.Shared.Next(0, bNpcNames.Count)].Singular.RawString,
                            bNpcNames[Random.Shared.Next(0, bNpcNames.Count)].Singular.ToDalamudString().TextValue);
                    });
                }
            }

            SameLine();
            if (Button("Sort"))
            {
                EditingLrc.Sort();
            }

            ToolTip("Sort lrc lines by time");

            //SameLine();
            //if (Checkbox("AutoSort", ref autosort)) EditingLrc.Sort();

            SameLine();
            TextUnformatted($"Current line: {EditingLrc.FindLrcIdx(MidiBard.CurrentPlaybackTime)}");


            if (CollapsingHeader("LRC Metadata", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var id = 0;
                if (BeginTable("metadata", 3, ImGuiTableFlags.SizingStretchProp))
                {
                    TableSetupColumn("tag", ImGuiTableColumnFlags.WidthStretch, 1);
                    TableSetupColumn("value", ImGuiTableColumnFlags.WidthStretch, 3);
                    TableSetupColumn("##btn", ImGuiTableColumnFlags.WidthFixed);
                    var metadatas = EditingLrc.LrcMetadata;
                    foreach (var (idtag, value) in (IEnumerable<KeyValuePair<string, string>>)metadatas)
                    {
                        PushID(id++);
                        TableNextRow();
                        TableNextColumn();
                        TextUnformatted(idtag);
                        TableNextColumn();
                        var editValue = value;
                        SetNextItemWidth(-1);
                        if (InputText("##v", ref editValue, 128))
                        {
                            metadatas[idtag] = editValue;
                            unsaved = true;
                        }

                        TableNextColumn();
                        if (IconButton(FontAwesomeIcon.TrashAlt))
                        {
                            metadatas.Remove(idtag);
                            unsaved = true;
                        }

                        PopID();
                    }

                    TableNextRow();
                    TableNextColumn();
                    SetNextItemWidth(-1);
                    if (InputTextWithHint("##newtag", "New tag name", ref newTagName, 128, ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        AddNewMetadataLine();
                    }

                    TableNextColumn();
                    SetNextItemWidth(-1);
                    if (InputTextWithHint("##newtagvalue", "New tag value", ref newTagValue, 128, ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        AddNewMetadataLine();
                    }

                    TableNextColumn();
                    if (IconButton(FontAwesomeIcon.Plus, "add"))
                    {
                        AddNewMetadataLine();
                    }

                    EndTable();

                    void AddNewMetadataLine()
                    {
                        if (string.IsNullOrWhiteSpace(newTagName)) return;
                        if (metadatas.TryAdd(newTagName, newTagValue))
                        {
                            newTagName = "";
                            newTagValue = "";
                        }

                        unsaved = true;
                    }
                }
            }

            if (BeginChild("contents", new Vector2(-1)))
            {
                BeginGroup();
                PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(GetStyle().CellPadding.Y));
                if (BeginTable("lrctable", 4, ImGuiTableFlags.Sortable))
                {
                    TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.PreferSortAscending);
                    TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort);
                    TableSetupColumn("Text", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort);
                    TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort);
                    TableHeadersRow();

                    var findPlayingLine = EditingLrc.FindLrcIdx(MidiBard.CurrentPlaybackTime);
                    var shouldSort = false;

                    #region SortByTime

                    var sortDirection = TableGetSortSpecs().Specs.SortDirection;

                    if (sortDirection == ImGuiSortDirection.Ascending)
                    {
                        for (var i = 0; i < LrcLines.Count; i++)
                        {
                            Iteration(ref i);
                        }
                    }
                    else if (sortDirection == ImGuiSortDirection.Descending)
                    {
                        for (var i = LrcLines.Count - 1; i >= 0; i--)
                        {
                            Iteration(ref i);
                        }
                    }

                    #endregion

                    //if (shouldSort)
                    //{
                    //    EditingLrc.Sort();
                    //    PluginLog.Information("do sort");
                    //}

                    void Iteration(ref int i)
                    {
                        PushID(i);
                        var shiftRightClicked = false;
                        try
                        {
                            var entry = LrcLines[i];
                            var entryTimeStamp = entry.TimeStamp;
                            var lrcTime = Lrc.ToLrcTime(entryTimeStamp);
                            if (findPlayingLine == i) PushStyleColor(ImGuiCol.FrameBg, Vector4.Lerp(MidiBard.config.themeColor, *GetStyleColorVec4(ImGuiCol.FrameBg), 0.4f));

                            TableNextColumn();
                            PushFont(UiBuilder.MonoFont);
                            if (Button($"{i:000}"))
                            {
                                try
                                {
                                    currentPlayback?.MoveToTime(new MetricTimeSpan(entryTimeStamp));
                                }
                                catch (Exception e)
                                {
                                    PluginLog.Error(e, "error moving playback time");
                                }
                            }

                            PopFont();
                            ToolTip($"Jump to {lrcTime}. Drag to move position");

                            if (BeginDragDropSource())
                            {
                                DragDropSource = (i, entry.JsonClone());
                                SetDragDropPayload("dragdropTime", nint.Zero, 0);
                                PushFont(UiBuilder.MonoFont);
                                TextUnformatted($"{Lrc.ToLrcTime(DragDropSource.Item2.TimeStamp),10} ");
                                PopFont();
                                SameLine();
                                TextUnformatted(DragDropSource.Item2.Text);
                                EndDragDropSource();
                            }

                            if (BeginDragDropTarget())
                            {
                                AcceptDragDropPayload("dragdropTime");
                                if (IsMouseReleased(ImGuiMouseButton.Left))
                                {
                                    LrcLines[DragDropSource.index] = LrcLines[i];
                                    LrcLines[i] = DragDropSource.Item2;
                                    unsaved = true;
                                }

                                EndDragDropTarget();
                            }

                            TableNextColumn();


                            PushFont(UiBuilder.MonoFont);
                            SetNextItemWidth(CalcTextSize("000:00.000").X + GetStyle().FramePadding.X * 2);
                            var timeString = $"{lrcTime,10}";
                            InputText("##timestamp", ref timeString, 10);

                            if (IsItemDeactivatedAfterEdit())
                            {
                                if (TryParseLrcTimeSpan(timeString, out var timeSpan))
                                {
                                    PluginLog.Information($"{timeString}");
                                    entry.TimeStamp = timeSpan;
                                    shouldSort = true;
                                    unsaved = true;
                                }
                            }

                            PopFont();
                            shiftRightClicked |= GetIO().KeyShift && IsItemClicked(ImGuiMouseButton.Right);

                            TableNextColumn();
                            SetNextItemWidth(-1);
                            if (InputText("##lyrictext", ref entry.Text, 400))
                            {
                                unsaved = true;
                            }

                            shiftRightClicked |= GetIO().KeyShift && IsItemClicked(ImGuiMouseButton.Right);

                            TableNextColumn();

                            if (IconButton(FontAwesomeIcon.TrashAlt))
                            {
                                LrcLines.Remove(entry);
                                unsaved = true;
                            }

                            if (shiftRightClicked)
                            {
                                try
                                {
                                    LrcLines.Insert(i + 1, new LrcEntry { TimeStamp = entryTimeStamp });
                                    unsaved = true;
                                }
                                catch (Exception e)
                                {
                                    //
                                }
                            }

                            if (findPlayingLine == i) PopStyleColor();
                        }
                        catch (Exception e)
                        {
                            PluginLog.Warning(e.ToString());
                        }

                        PopID();
                    }

                    EndTable();
                }



                TextCenterAligned("Right click to add a new line");
                TextCenterAligned("Shift+Right click to insert a new line");
                Dummy(new Vector2(-1, GetWindowContentRegionMax().Y - GetCursorPosY())); //fill rest space to receive right click

                PopStyleVar();
                EndGroup();

                var rightClicked = !GetIO().KeyShift && IsItemClicked(ImGuiMouseButton.Right);
                if (rightClicked)
                {
                    var currentPlaybackTime = MidiBard.CurrentPlaybackTime ?? TimeSpan.Zero;
                    var newLine = new LrcEntry { TimeStamp = currentPlaybackTime };
                    LrcLines.Insert(EditingLrc.FindLrcIdx(currentPlaybackTime) + 1, newLine);
                    unsaved = true;
                }

                EndChild();
            }
        }

        End();
    }

    private void AskSave()
    {
        var path = EditingLrc.FilePath;
        if (path is null)
        {
            OpenExportFileDialog();
        }
        else
        {
            SaveLrc(path);
        }
    }

    void TextCenterAligned(string text)
    {
        var size = CalcTextSize(text);
        SetCursorPosX(ImGuiUtil.GetWindowContentRegionWidth() / 2 - size.X / 2);
        TextUnformatted(text);
    }

    private void OpenExportFileDialog(string defalutPath = null)
    {
        FileDialogs.SaveFileDialog((success, filePathToSave) =>
        {
            if (!success) return;

            SaveLrc(filePathToSave);
        }, MidiBard.CurrentPlayback?.DisplayName, "All files (*.*)|*.*", "lrc", defalutPath);
    }

    private void SaveLrc(string filePathToSave)
    {
        var exportString = EditingLrc.GetLrcExportString();
        File.WriteAllText(filePathToSave, exportString, Encoding.UTF8);
        AddNotification(NotificationType.Success, "Lrc Saved " + filePathToSave);
        unsaved = false;

        ReloadLrc();
    }

    private void ReloadLrc()
    {
        IPCEnvelope.Create(MessageTypeCode.ReloadLRC, EditingLrc.FilePath).BroadCast(true);
    }
}
