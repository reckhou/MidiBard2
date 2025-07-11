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
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;

using ImGuiNET;

using MidiBard.IPC;
using MidiBard.UI.Win32;
using MidiBard.Util;

using MidiBard2.Resources;

using static Dalamud.api;

namespace MidiBard;

public partial class PluginUI
{
    private void DrawPlaylistMenu()
    {
        if (IsImportRunning)
        {
            ImGuiUtil.DrawColoredBanner(Style.Colors.Violet, Language.text_Import_in_progress);
        }

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGuiHelpers.ScaledVector2(4, 4));
        ImGuiUtil.PushIconButtonSize(ImGuiHelpers.ScaledVector2(45.5f, 25));

        ImGui.BeginDisabled(IsImportRunning);
        if (ImGui.BeginPopup("OpenFileDialog_selection"))
        {
            if (ImGui.MenuItem(Language.imgui_file_dialog, null, !MidiBard.config.useLegacyFileDialog))
            {
                MidiBard.config.useLegacyFileDialog = false;
            }
            if (ImGui.MenuItem(Language.w32_file_dialog, null, MidiBard.config.useLegacyFileDialog))
            {
                MidiBard.config.useLegacyFileDialog = true;
            }
            ImGui.EndPopup();
        }

        ImGui.BeginGroup();

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "buttonimport",
                Language.icon_button_tooltip_import_file))
        {
            RunImportFileTask();
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "buttonimportFolder",
                Language.icon_button_tooltip_import_folder))
        {
            RunImportFolderTask();
        }

        ImGui.EndGroup();
        ImGui.OpenPopupOnItemClick("OpenFileDialog_selection", ImGuiPopupFlags.MouseButtonRight);
        ImGui.EndDisabled();

        //-------------------

        ImGui.SameLine();
        Vector4? color = MidiBard.config.enableSearching ? MidiBard.config.themeColor : null;
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Search, "searchbutton", Language.icon_button_tooltip_search_playlist, color))
        {
            MidiBard.config.enableSearching ^= true;
        }

        //-------------------

        ImGui.SameLine();
        ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, "clearplaylist", Language.icon_button_tooltip_clearplaylist_tootltip);
        if (ImGui.IsItemHovered())
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                PlaylistManager.Clear();
            }
        }

        //-------------------

        ImGui.SameLine();
        var fontAwesomeIcon = MidiBard.config.UseStandalonePlaylistWindow
            ? FontAwesomeIcon.Compress
            : FontAwesomeIcon.Expand;
        if (ImGuiUtil.IconButton(fontAwesomeIcon, "ButtonStandalonePlaylist",
                Language.setting_label_standalone_playlist_window))
        {
            MidiBard.config.UseStandalonePlaylistWindow ^= true;
        }

        //-------------------

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Eraser, "btnClearHighlightedSongs"))
        {
            PlaylistManager.ResetAllSongsPlayedStatusSync();
            // reset filter
            MidiBard.config.SearchFilterPlayedOption = FilterPlayedSongOptions.ShowAll;
        }
        ImGuiUtil.ToolTip(Language.icon_button_tooltip_clear_highlighted_songs);

        //-------------------

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.EllipsisH, "##playlistMoreContextMenu", Language.icon_button_tooltip_playlist_menu))
        {
            ImGui.OpenPopup("PlaylistPopupMenu");
        }

        ImGui.PopStyleVar();
        ImGuiUtil.PopIconButtonSize();

        if (ImGui.BeginPopup("PlaylistPopupMenu"))
        {
            var shortenPath = Path.ChangeExtension(PlaylistManager.CurrentContainer.FilePathWhenLoading, null).EllipsisString(40);
            ImGui.MenuItem(shortenPath, false);

            var useWin32 = MidiBard.config.useLegacyFileDialog;
#if LINUX
            useWin32 = false; // Force ImGui dialogs on Linux
#endif
            // open playlist
            if (ImGui.MenuItem(Language.menu_label_open_playlist))
            {
                if (useWin32)
                {
#if !LINUX
                    FileDialogs.OpenPlaylistDialog((result, path) =>
                    {
                        if (result != true) return;
                        PlaylistManager.CurrentContainer = PlaylistContainer.FromFile(path);
                    });
#endif
                }
                else
                {
                    fileDialogManager.OpenFileDialog("Open playlist", ".mpl", (b, s) =>
                    {
                        if (!b) return;
                        PlaylistManager.CurrentContainer = PlaylistContainer.FromFile(s);
                    });
                }
            }

            // new playlist
            if (ImGui.MenuItem(Language.menu_label_new_playlist))
            {
                if (PlaylistManager.CurrentContainer.FilePathWhenLoading != null)
                {
                    PlaylistManager.CurrentContainer.Save(PlaylistManager.CurrentContainer.FilePathWhenLoading);
                }

                if (useWin32)
                {
#if !LINUX
                    FileDialogs.SavePlaylistDialog((result, path) =>
                    {
                        if (result != true) return;
                        PlaylistManager.CurrentContainer = PlaylistContainer.FromFile(path, true);
                    }, Language.text_new_playlist);
#endif
                }
                else
                {
                    fileDialogManager.SaveFileDialog(Language.window_title_choose_new_playlist_save_location,
                        ".mpl",
                        Language.text_new_playlist, ".mpl", (b, s) =>
                        {
                            if (b)
                            {
                                PlaylistManager.CurrentContainer = PlaylistContainer.FromFile(s, true);
                            }
                        });
                }
            }

            // sync playlist
            if (ImGui.MenuItem(Language.menu_label_sync_playlist))
            {
                IPCHandles.SyncAllSettings();
                if (MidiBard.config.playOnMultipleDevices && MidiBard.config.usingFileSharingServices)
                {
                    PartyChatCommand.SendReloadPlaylist();
                }
                else
                {
                    IPCHandles.SyncPlaylist();
                }
            }

            // save playlist
            if (ImGui.MenuItem(Language.menu_label_save_playlist))
            {
                PlaylistManager.CurrentContainer.Save();
            }

            // save playlist as...
            if (ImGui.MenuItem(Language.menu_label_clone_current_playlist))
            {
                if (useWin32)
                {
#if !LINUX
                    FileDialogs.SavePlaylistDialog((result, path) =>
                    {
                        if (result != true) return;
                        PlaylistManager.CurrentContainer.Save(path);
                    }, PlaylistManager.CurrentContainer.DisplayName + Language.text_file_copy);
#endif
                }
                else
                {
                    fileDialogManager.SaveFileDialog(Language.window_title_choose_new_playlist_save_location,
                        ".mpl",
                        PlaylistManager.CurrentContainer.DisplayName + Language.text_file_copy,
                        ".mpl", (b, s) =>
                        {
                            if (!b) return;
                            PlaylistManager.CurrentContainer.Save(s);
                        });
                }
            }

            // save playlist search result as...
            var isPlaylistFiltered = MidiBard.config.enableSearching && (!string.IsNullOrEmpty(PlaylistSearchString)
                || MidiBard.config.SearchFilterPlayedOption != FilterPlayedSongOptions.ShowAll);
            if (ImGui.MenuItem(Language.menu_label_save_search_as_playlist, isPlaylistFiltered))
            {
                var playlistSearchString = PlaylistSearchString;
                if (useWin32)
                {
#if !LINUX
                    FileDialogs.SavePlaylistDialog((result, path) =>
                    {
                        if (result != true) return;
                        SaveSearchedPlaylist(path);
                    }, playlistSearchString);
#endif
                }
                else
                {
                    fileDialogManager.SaveFileDialog(Language.window_title_choose_new_playlist_save_location,
                        "*.mpl", playlistSearchString,
                        ".mpl",
                        (b, s) =>
                        {
                            if (!b) return;
                            SaveSearchedPlaylist(s);
                        });
                }

                void SaveSearchedPlaylist(string filePath1)
                {
                    try
                    {
                        RefreshPlaylistSearchResult();
                        var playlistContainer = PlaylistContainer.FromFile(filePath1, true);
                        playlistContainer.SongPaths = MidiBard.Ui.searchedPlaylistIndexs
                            .Select(i => PlaylistManager.FilePathList[i]).ToList();
                        playlistContainer.Save();
                    }
                    catch (Exception e)
                    {
                        PluginLog.Warning(e, "error when saving current search result");
                    }
                }
            }

            if (ImGui.MenuItem("Save playlist as CSV"))
            {
                if (useWin32)
                {
#if !LINUX
                    FileDialogs.SavePlaylistDialog((result, path) =>
                    {
                        if (result != true) return;
                        PlaylistManager.CurrentContainer.ExportToCsv(path);
                    }, PlaylistManager.CurrentContainer.DisplayName + Language.text_file_copy);
#endif
                }
                else
                {
                    fileDialogManager.SaveFileDialog(Language.window_title_choose_new_playlist_save_location,
                        ".csv",
                        PlaylistManager.CurrentContainer.DisplayName + Language.text_file_copy,
                        ".csv", (b, s) =>
                        {
                            if (!b) return;
                            PlaylistManager.CurrentContainer.ExportToCsv(s);
                        });
                }
            }

            //var totalDuration = PlaylistManager.CurrentContainer.TotalDuration;
            //var durationString = totalDuration == TimeSpan.Zero
            //    ? "Not calculated"
            //    : $"{(int)totalDuration.TotalHours}h {(int)totalDuration.Minutes}m {(int)totalDuration.Seconds}s";
            //MenuItem($"Playlist total duration: {durationString}", false);
            if (ImGui.MenuItem("Recalculate playlist duration"))
            {
                PluginLog.Information("Recalculate playlist duration");
                Task.Run(PlaylistManager.CalculateDurationAll);
            }

            if (ImGui.MenuItem("Remove duplicate songs by name"))
            {
                PluginLog.Information("Removing duplicate songs");
                var distinctBy = PlaylistManager.CurrentContainer.SongPaths.DistinctBy(i => i.FileName).ToList();
                var currentContainerSongPaths = PlaylistManager.CurrentContainer.SongPaths;
                ImGuiUtil.AddNotification(NotificationType.Info, $"Removed {currentContainerSongPaths.Count - distinctBy.Count} entries");
                PlaylistManager.CurrentContainer.SongPaths = distinctBy;
            }

            ImGui.Separator();

            //recent used playlists
            ImGui.MenuItem(Language.menu_text_recent_playlist, false);
            var takeLast = MidiBard.config.RecentUsedPlaylists.TakeLast(10).Reverse();
            var id = 89465;
            try
            {
                foreach (var playlistPath in takeLast)
                {
                    ImGui.PushID(id++);
                    try
                    {
                        var ellipsisString = Path.ChangeExtension(playlistPath, null).EllipsisString(40);
                        if (ImGui.BeginMenu(ellipsisString + "        "))
                        {
                            if (ImGui.MenuItem(Language.menu_item_load_playlist))
                            {
                                var playlistContainer = PlaylistContainer.FromFile(playlistPath);
                                if (playlistContainer != null)
                                {
                                    PlaylistManager.CurrentContainer = playlistContainer;
                                }
                                else
                                {
                                    ImGuiUtil.AddNotification(NotificationType.Error, $"{playlistPath} NOT exist!");
                                    MidiBard.config.RecentUsedPlaylists.Remove(playlistPath);
                                }
                            }

                            if (ImGui.MenuItem(Language.menu_item_open_in_file_explorer))
                            {
                                try
                                {
                                    if (!File.Exists(playlistPath))
                                    {
                                        MidiBard.config.RecentUsedPlaylists.Remove(playlistPath);
                                    }

                                    Extensions.OpenFileLocation(playlistPath);
                                }
                                catch (Exception e)
                                {
                                    PluginLog.Warning(e, "error when opening process");
                                }
                            }

                            if (ImGui.MenuItem(Language.menu_item_open_in_text_editor))
                            {
                                try
                                {
                                    if (!File.Exists(playlistPath))
                                    {
                                        MidiBard.config.RecentUsedPlaylists.Remove(playlistPath);
                                    }

                                    Extensions.ExecuteCmd(playlistPath);
                                }
                                catch (Exception e)
                                {
                                    PluginLog.Warning(e, $"error when opening process {playlistPath}");
                                }
                            }

                            if (ImGui.MenuItem(Language.menu_item_remove_from_recent_list))
                            {
                                MidiBard.config.RecentUsedPlaylists.Remove(playlistPath);
                            }

                            ImGui.EndMenu();
                        }
                    }
                    catch (Exception e)
                    {
                        PluginLog.Warning(e, "error when drawing recent playlist");
                    }

                    ImGui.PopID();
                }
            }
            catch
            {
                // ignored
            }

            ImGui.EndPopup();
        }

        //-------------------

        if (MidiBard.config.enableSearching)
        {
            DrawPlaylistSearch();

            var isPlaylistFilteredWithoutMatches = searchedPlaylistIndexs.Count == 0
                && PlaylistManager.FilePathList.Any()
                && MidiBard.config.enableSearching
                && (!string.IsNullOrEmpty(PlaylistSearchString) || MidiBard.config.SearchFilterPlayedOption != FilterPlayedSongOptions.ShowAll);

            if (isPlaylistFilteredWithoutMatches)
            {
                ImGuiUtil.DrawColoredBanner(Style.Colors.Red, Language.text_no_matching_songs_filter);
            }
        }
    }
}
