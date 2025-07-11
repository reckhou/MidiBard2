using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;

using ImGuiNET;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.IPC;
using MidiBard.UI.Win32;
using MidiBard.Util;
using MidiBard.Util.Lyrics;

using static Dalamud.api;

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
    public void Draw()
    {
        if (Visible && ImGui.Begin($"{Path.GetFileName(EditingLrc.FilePath) ?? "Lrc Editor"}###Lyric Editor", ref Visible, unsaved ? ImGuiWindowFlags.UnsavedDocument : ImGuiWindowFlags.None))
        {
            if (LrcPending != null)
            {
                ImGui.OpenPopup("Save?");
            }

            var open = true;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowTitleAlign, new Vector2(0.5f));
            var wdl = ImGui.GetWindowDrawList();
            var clipRect = wdl.GetClipRectMin() + wdl.GetClipRectMax();
            clipRect /= 2;
            ImGui.SetNextWindowPos(clipRect, ImGuiCond.Appearing, Vector2.One / 2);
            if (ImGui.BeginPopupModal("Save?", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Dummy(ImGuiHelpers.ScaledVector2(20));
                TextCenterAligned("Editor has unsaved changes. Save now?");
                ImGui.Dummy(ImGuiHelpers.ScaledVector2(20));
                ImGui.Dummy(ImGuiHelpers.ScaledVector2(60, 30)); ImGui.SameLine();
                if (ImGui.Button("Save", new Vector2(ImGui.GetFrameHeight() * 4, ImGui.GetFrameHeight())))
                {
                    AskSave();
                    unsaved = false;
                    LoadLrcToEditor(LrcPending);
                    LrcPending = null;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("Discard", new Vector2(ImGui.GetFrameHeight() * 4, ImGui.GetFrameHeight())))
                {
                    unsaved = false;
                    LoadLrcToEditor(LrcPending);
                    LrcPending = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                ImGui.Dummy(ImGuiHelpers.ScaledVector2(60, 30));

                ImGui.EndPopup();
            }
            ImGui.PopStyleVar();

            if (!open)
            {
                LrcPending = null;
            }

            var currentPlayback = MidiBard.CurrentPlayback;
            if (ImGui.Button("New"))
            {
                var newLrc = GetLrcFromPlayback(currentPlayback);
                LoadLrcToEditor(newLrc);
            }

            ImGui.SameLine();
            if (ImGui.Button("Open"))
            {
#if !LINUX
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
#endif
            }

            //ImGui.SameLine();
            //if (ImGui.Button("Load from playing"))
            //{
            //    LoadLrcToEditor(Lrc.PlayingLrc.JsonClone());
            //}

            ImGui.SameLine();
            if (ImGui.Button("Save"))
            {
                AskSave();
            }
            ImGuiUtil.ToolTip(EditingLrc.FilePath is null ? "Select save location" : $"Save to: {EditingLrc.FilePath}");

            // SameLine();
            // if (Button("Random"))
            // {
            //     var dura = MidiBard.CurrentPlaybackDuration ?? TimeSpan.Zero;
            //     var count = 32;
            //     //if (currentPlayback is not null)
            //     //{
            //     //    EditingLrc.LrcMetadata["ti"] = currentPlayback.DisplayName ?? "";
            //     //    EditingLrc.LrcMetadata["length"] = Lrc.ToLrcTime(MidiBard.CurrentPlaybackDuration ?? TimeSpan.Zero);
            //     //}

            //     LrcLines.Clear();
            //     LrcLines.AddRange(Enumerable.Range(0, count).Select(i => new LrcEntry { TimeStamp = dura / count * i }));
            //     var bNpcNames = api.DataManager.GetExcelSheet<BNpcName>()!.Where(i => !string.IsNullOrWhiteSpace(i.Singular.ToDalamudString().TextValue)).ToList();
            //     LrcLines.ForEach(i =>
            //     {
            //         i.Text = string.Join(' ',
            //             bNpcNames[Random.Shared.Next(0, bNpcNames.Count)].Singular.ToDalamudString().TextValue,
            //             bNpcNames[Random.Shared.Next(0, bNpcNames.Count)].Singular.ToDalamudString().TextValue,
            //             //bNpcNames[Random.Shared.Next(0, bNpcNames.Count)].Singular.RawString,
            //             bNpcNames[Random.Shared.Next(0, bNpcNames.Count)].Singular.ToDalamudString().TextValue);
            //     });
            // }

            ImGui.SameLine();
            if (ImGui.Button("Sort"))
            {
                EditingLrc.Sort();
            }

            ImGuiUtil.ToolTip("Sort lrc lines by time");

            //SameLine();
            //if (Checkbox("AutoSort", ref autosort)) EditingLrc.Sort();

            ImGui.SameLine();
            ImGui.TextUnformatted($"Current line: {EditingLrc.FindLrcIdx(MidiBard.CurrentPlaybackTime)}");


            if (ImGui.CollapsingHeader("LRC Metadata", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var id = 0;
                if (ImGui.BeginTable("metadata", 3, ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("tag", ImGuiTableColumnFlags.WidthStretch, 1);
                    ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthStretch, 3);
                    ImGui.TableSetupColumn("##btn", ImGuiTableColumnFlags.WidthFixed);
                    var metadatas = EditingLrc.LrcMetadata;
                    foreach (var (idtag, value) in (IEnumerable<KeyValuePair<string, string>>)metadatas)
                    {
                        ImGui.PushID(id++);
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(idtag);
                        ImGui.TableNextColumn();
                        var editValue = value;
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputText("##v", ref editValue, 128))
                        {
                            metadatas[idtag] = editValue;
                            unsaved = true;
                        }

                        ImGui.TableNextColumn();
                        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt))
                        {
                            metadatas.Remove(idtag);
                            unsaved = true;
                        }

                        ImGui.PopID();
                    }

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputTextWithHint("##newtag", "New tag name", ref newTagName, 128, ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        AddNewMetadataLine();
                    }

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputTextWithHint("##newtagvalue", "New tag value", ref newTagValue, 128, ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        AddNewMetadataLine();
                    }

                    ImGui.TableNextColumn();
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "add"))
                    {
                        AddNewMetadataLine();
                    }

                    ImGui.EndTable();

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

            if (ImGui.BeginChild("contents", new Vector2(-1)))
            {
                ImGui.BeginGroup();
                ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(ImGui.GetStyle().CellPadding.Y));
                if (ImGui.BeginTable("lrctable", 4, ImGuiTableFlags.Sortable))
                {
                    ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.PreferSortAscending);
                    ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort);
                    ImGui.TableSetupColumn("Text", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort);
                    ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort);
                    ImGui.TableHeadersRow();

                    var findPlayingLine = EditingLrc.FindLrcIdx(MidiBard.CurrentPlaybackTime);

                    #region SortByTime

                    var sortDirection = ImGui.TableGetSortSpecs().Specs.SortDirection;

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
                        ImGui.PushID(i);
                        var shiftRightClicked = false;
                        try
                        {
                            var entry = LrcLines[i];
                            var entryTimeStamp = entry.TimeStamp;
                            var lrcTime = Lrc.ToLrcTime(entryTimeStamp);
                            if (findPlayingLine == i) ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Lerp(MidiBard.config.themeColor, Style.Components.FrameBg, 0.4f));

                            ImGui.TableNextColumn();
                            ImGui.PushFont(UiBuilder.MonoFont);
                            if (ImGui.Button($"{i:000}"))
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

                            ImGui.PopFont();
                            ImGuiUtil.ToolTip($"Jump to {lrcTime}. Drag to move position");

                            if (ImGui.BeginDragDropSource())
                            {
                                DragDropSource = (i, entry.JsonClone());
                                ImGui.SetDragDropPayload("dragdropTime", nint.Zero, 0);
                                ImGui.PushFont(UiBuilder.MonoFont);
                                ImGui.TextUnformatted($"{Lrc.ToLrcTime(DragDropSource.Item2.TimeStamp),10} ");
                                ImGui.PopFont();
                                ImGui.SameLine();
                                ImGui.TextUnformatted(DragDropSource.Item2.Text);
                                ImGui.EndDragDropSource();
                            }

                            if (ImGui.BeginDragDropTarget())
                            {
                                ImGui.AcceptDragDropPayload("dragdropTime");
                                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                                {
                                    LrcLines[DragDropSource.index] = LrcLines[i];
                                    LrcLines[i] = DragDropSource.Item2;
                                    unsaved = true;
                                }

                                ImGui.EndDragDropTarget();
                            }

                            ImGui.TableNextColumn();


                            ImGui.PushFont(UiBuilder.MonoFont);
                            ImGui.SetNextItemWidth(ImGui.CalcTextSize("000:00.000").X + ImGui.GetStyle().FramePadding.X * 2);
                            var timeString = $"{lrcTime,10}";
                            ImGui.InputText("##timestamp", ref timeString, 10);

                            if (ImGui.IsItemDeactivatedAfterEdit())
                            {
                                if (TryParseLrcTimeSpan(timeString, out var timeSpan))
                                {
                                    PluginLog.Information($"{timeString}");
                                    entry.TimeStamp = timeSpan;
                                    unsaved = true;
                                }
                            }

                            ImGui.PopFont();
                            shiftRightClicked |= ImGui.GetIO().KeyShift && ImGui.IsItemClicked(ImGuiMouseButton.Right);

                            ImGui.TableNextColumn();
                            ImGui.SetNextItemWidth(-1);
                            if (ImGui.InputText("##lyrictext", ref entry.Text, 400))
                            {
                                unsaved = true;
                            }

                            shiftRightClicked |= ImGui.GetIO().KeyShift && ImGui.IsItemClicked(ImGuiMouseButton.Right);

                            ImGui.TableNextColumn();

                            if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt))
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
                                catch
                                {
                                    // ignored
                                }
                            }

                            if (findPlayingLine == i) ImGui.PopStyleColor();
                        }
                        catch (Exception e)
                        {
                            PluginLog.Warning(e.ToString());
                        }

                        ImGui.PopID();
                    }

                    ImGui.EndTable();
                }

                TextCenterAligned("Right click to add a new line");
                TextCenterAligned("Shift+Right click to insert a new line");
                ImGui.Dummy(new Vector2(-1, ImGui.GetWindowContentRegionMax().Y - ImGui.GetCursorPosY())); //fill rest space to receive right click

                ImGui.PopStyleVar();
                ImGui.EndGroup();

                var rightClicked = !ImGui.GetIO().KeyShift && ImGui.IsItemClicked(ImGuiMouseButton.Right);
                if (rightClicked)
                {
                    var currentPlaybackTime = MidiBard.CurrentPlaybackTime ?? TimeSpan.Zero;
                    var newLine = new LrcEntry { TimeStamp = currentPlaybackTime };
                    LrcLines.Insert(EditingLrc.FindLrcIdx(currentPlaybackTime) + 1, newLine);
                    unsaved = true;
                }

                ImGui.EndChild();
            }
        }

        ImGui.End();
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
        var size = ImGui.CalcTextSize(text);
        ImGui.SetCursorPosX(ImGuiUtil.GetWindowContentRegionWidth() / 2 - size.X / 2);
        ImGui.TextUnformatted(text);
    }

    private void OpenExportFileDialog(string defalutPath = null)
    {
#if !LINUX
        FileDialogs.SaveFileDialog((success, filePathToSave) =>
        {
            if (!success) return;

            SaveLrc(filePathToSave);
        }, MidiBard.CurrentPlayback?.DisplayName, "All files (*.*)|*.*", "lrc", defalutPath);
#endif
    }

    private void SaveLrc(string filePathToSave)
    {
        var exportString = EditingLrc.GetLrcExportString();
        File.WriteAllText(filePathToSave, exportString, Encoding.UTF8);
        ImGuiUtil.AddNotification(NotificationType.Success, "Lrc Saved " + filePathToSave);
        unsaved = false;

        ReloadLrc();
    }

    private void ReloadLrc()
    {
        IPCEnvelope.Create(MessageTypeCode.ReloadLRC, EditingLrc.FilePath).BroadCast(true);
    }
}
