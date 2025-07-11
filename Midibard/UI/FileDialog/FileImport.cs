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
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using MidiBard.UI.Win32;

using static Dalamud.api;

namespace MidiBard;

public partial class PluginUI
{
    public bool IsImportRunning { get; private set; }

    public async void RunImportFileTask()
    {
        if (IsImportRunning) return;
        IsImportRunning = true;
        PluginLog.Debug("Import file task started");

        try
        {
            CheckLastOpenedFolderPath();

            var useLegacyDialog = MidiBard.config.useLegacyFileDialog;
#if LINUX
            useLegacyDialog = false; // Force ImGui dialogs on Linux
#endif
            if (useLegacyDialog)
                await RunImportFileTaskWin32Async();
            else
                await RunImportFileTaskImGuiAsync();
        }
        catch (Exception e)
        {
            PluginLog.Error($"Error when importing files: {e}");
        }
        finally
        {
            IsImportRunning = false;
            PluginLog.Debug("Import file task finished");
        }
    }

    public async void RunImportFolderTask()
    {
        if (IsImportRunning) return;
        IsImportRunning = true;
        PluginLog.Debug("Import folder task started");

        try
        {
            CheckLastOpenedFolderPath();

            var useLegacyDialog = MidiBard.config.useLegacyFileDialog;
#if LINUX
            useLegacyDialog = false; // Force ImGui dialogs on Linux
#endif
            if (useLegacyDialog)
                await RunImportFolderTaskWin32Async();
            else
                await RunImportFolderTaskImGuiAsync();
        }
        catch (Exception e)
        {
            PluginLog.Error($"Error during folder import: {e}");
        }
        finally
        {
            IsImportRunning = false;
            PluginLog.Debug("Import folder task finished");
        }
    }

    private Task RunImportFileTaskWin32Async()
    {
        var tcs = new TaskCompletionSource();

#if !LINUX
        FileDialogs.OpenMidiFileDialog((result, filePaths) =>
        {
            if (result == true && filePaths is { Length: > 0 })
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await PlaylistManager.AddAsync(filePaths);
                        MidiBard.config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]);
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Error during file import: {ex.Message}");
                    }
                    finally
                    {
                        tcs.TrySetResult();
                    }
                });
            }
            else
            {
                tcs.TrySetResult();
            }
        });
#else
        // On Linux, fall back to ImGui dialogs
        tcs.SetResult();
#endif

        return tcs.Task;
    }

    private Task RunImportFileTaskImGuiAsync()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFileDialogResult(bool result, List<string> filePaths)
        {
            if (result && filePaths.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await PlaylistManager.AddAsync(filePaths);
                        MidiBard.config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]);
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Error during file import: {ex.Message}");
                        tcs.TrySetException(ex);
                    }
                });
            }
            else
            {
                tcs.TrySetResult(false);
            }
        }

        try
        {
            fileDialogManager.OpenFileDialog(
                title: "Open",
                filters: ".mid,.midi,.mmsong",
                callback: OnFileDialogResult,
                selectionCountMax: 0,
                startPath: MidiBard.config.lastOpenedFolderPath
            );
        }
        catch (Exception e)
        {
            PluginLog.Error($"Failed to open file dialog: {e}");
            tcs.TrySetException(e);
        }

        return tcs.Task;
    }

    private Task RunImportFolderTaskWin32Async()
    {
        var tcs = new TaskCompletionSource();

#if !LINUX
        FileDialogs.FolderPicker((result, folderPath) =>
        {
            if (result == true && !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var allowedExtensions = new[] { ".mid", ".midi", ".mmsong" };
                        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                            .Where(i => allowedExtensions.Any(ext => i.EndsWith(ext, StringComparison.InvariantCultureIgnoreCase)));
                        await PlaylistManager.AddAsync(files);
                        MidiBard.config.lastOpenedFolderPath = Directory.GetParent(folderPath)?.FullName ?? folderPath;
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Error during folder import: {ex.Message}");
                    }
                    finally
                    {
                        tcs.TrySetResult();
                    }
                });
            }
            else
            {
                tcs.TrySetResult();
            }
        });
#else
        // On Linux, fall back to ImGui dialogs
        tcs.SetResult();
#endif

        return tcs.Task;
    }

    private Task RunImportFolderTaskImGuiAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        fileDialogManager.OpenFolderDialog("Open folder", (result, folderPath) =>
        {
            if (result && Directory.Exists(folderPath))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var allowedExtensions = new[] { ".mid", ".midi", ".mmsong" };
                        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                            .Where(i => allowedExtensions.Any(ext => i.EndsWith(ext, StringComparison.InvariantCultureIgnoreCase)));
                        await PlaylistManager.AddAsync(files);
                        MidiBard.config.lastOpenedFolderPath = Directory.GetParent(folderPath)?.FullName ?? folderPath;
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Error during folder import: {ex.Message}");
                    }
                    finally
                    {
                        tcs.TrySetResult(true);
                    }
                });
            }
            else
            {
                tcs.TrySetResult(false);
            }
        }, MidiBard.config.lastOpenedFolderPath);

        return tcs.Task;
    }

    private void CheckLastOpenedFolderPath()
    {
        if (!Directory.Exists(MidiBard.config.lastOpenedFolderPath))
        {
            MidiBard.config.lastOpenedFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }
}
