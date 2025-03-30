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
using System.Threading;
using System.Threading.Tasks;

using Dalamud;
using Dalamud.Logging;

using Microsoft.Win32;

using MidiBard.Util;

using MidiBard2.Resources;

namespace MidiBard.UI.Win32;

static class FileDialogs
{
    //public delegate void MultiFileSelectedCallback(bool? fileDialogResult, string[] filePaths);

    //public delegate void FileSelectedCallback(bool? fileDialogResult, string filePath);

    //public delegate void FolderSelectedCallback(bool? fileDialogResult, string folderPath);

    //public delegate void SaveFileDialogCallback(bool? fileDialogResult, string filePath);

    public static void OpenMidiFileDialog(Action<bool?, string[]> callback)
    {
        var t = new Thread(() =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Midi Files (*.mid, *.midi, *.mmsong)|*.mid;*.midi;*.mmsong",
                RestoreDirectory = true,
                CheckFileExists = true,
                Multiselect = true,
                InitialDirectory = MidiBard.config.lastOpenedFolderPath
            };

            callback(dialog.ShowDialog(), dialog.FileNames);
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public static void OpenPlaylistDialog(Action<bool?, string> callback)
    {
        var t = new Thread(() =>
        {
            var dialog = new OpenFileDialog()
            {
                Filter = "Midibard playlist (*.mpl)|*.mpl",
                RestoreDirectory = true,
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = MidiBard.config.lastOpenedFolderPath
            };
            callback(dialog.ShowDialog(), dialog.FileName);
        });

        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public static void FolderPicker(Action<bool?, string> callback)
    {
        var t = new Thread(() =>
        {
            var dlg = new FolderPicker();
            if (Directory.Exists(MidiBard.config.lastOpenedFolderPath))
            {
                dlg.InputPath = MidiBard.config.lastOpenedFolderPath;
            }
            callback(dlg.ShowDialog(api.PluginInterface.UiBuilder.WindowHandlePtr), dlg.ResultPath);
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public static void SavePlaylistDialog(Action<bool?, string> callback, string filename)
    {
        var t = new Thread(() =>
        {
            var dialog = new SaveFileDialog
            {
                Filter = $"{Language.text_midibard_playlist} (*.mpl)|*.mpl",
                RestoreDirectory = true,
                AddExtension = true,
                DefaultExt = ".mpl",
                OverwritePrompt = true,
                FileName = filename,
                InitialDirectory = MidiBard.config.lastOpenedFolderPath
            };
            callback(dialog.ShowDialog(), dialog.FileName);
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public static void SaveFileDialog(Action<bool, string> callback, string filename = null, string filter = null, string defaultExt = null, string initDirectory = null)
    {
        var t = new Thread(() =>
        {
            var dialog = new SaveFileDialog
            {
                RestoreDirectory = true,
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = initDirectory ?? MidiBard.config.lastOpenedFolderPath
            };

            if (filename is not null) dialog.FileName = filename;
            if (filter is not null) dialog.Filter = filter;
            if (defaultExt is not null) dialog.DefaultExt = defaultExt;

            callback(dialog.ShowDialog() == true, dialog.FileName);

        });

        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }


    public static void OpenFileDialog(Action<bool, string, string[]> callback, string filter = null, bool multiselect = true, string initDirectory = null)
    {
        var t = new Thread(() =>
        {
            var dialog = new OpenFileDialog()
            {
                Filter = filter,
                RestoreDirectory = true,
                CheckFileExists = true,
                Multiselect = multiselect,
                InitialDirectory = initDirectory ?? MidiBard.config.lastOpenedFolderPath
            };
            callback(dialog.ShowDialog() == true, dialog.FileName, dialog.FileNames);
        });

        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

}
