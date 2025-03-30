using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Dalamud;
using Dalamud.Game;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Logging;
using Dalamud.Plugin.Services;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl;
using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.Managers.Agents;
using MidiBard.Managers.Ipc;

using static Dalamud.api;



namespace MidiBard.Util.Lyrics
{
    public class Lrc
    {
        public static Lrc PlayingLrc;

        /// <summary>
        /// 歌曲
        /// </summary>
        public string Title => LrcMetadata.GetValueOrDefault("ti");

        /// <summary>
        /// 艺术家
        /// </summary>
        public string Artist => LrcMetadata.GetValueOrDefault("ar");

        /// <summary>
        /// 专辑
        /// </summary>
        public string Album => LrcMetadata.GetValueOrDefault("al");

        /// <summary>
        /// 歌词作者
        /// </summary>
        public string LrcBy => LrcMetadata.GetValueOrDefault("by");

        /// <summary>
        /// 偏移量
        /// </summary>
        public long Offset { get; set; }

        /// <summary>
        /// 歌词
        /// </summary>

        private static readonly Regex ParseTimeLyric = new Regex(@"^\[(?<min>\d+?):(?<sec>\d{1,2})\.(?<ff>\d+?)\](?<text>.*)$", RegexOptions.Compiled);
        private static readonly Regex ParseMetadata = new Regex(@"^\[(?<idTag>.+?):(?<tagContent>.*)\]\s*$", RegexOptions.Compiled);
        private static readonly Regex ParsePoster = new Regex(@"^(?<poster>.+?):(?<text>.+)$", RegexOptions.Compiled);
        public static string ToLrcTime(TimeSpan timeSpan) => $"{(int)timeSpan.TotalMinutes:00}:{timeSpan.Seconds:00}.{timeSpan:ff}";

        public void Sort() => LrcLines.Sort((x, y) => x.TimeStamp.CompareTo(y.TimeStamp));

        public List<LrcEntry> LrcLines { get; init; }

        public Dictionary<string, string> LrcMetadata { get; init; }
        public string FilePath { get; set; }

        public string GetLrcExportString()
        {
            var sb = new StringBuilder();
            //if (LrcLines.Any()) LrcMetadata["length"] = ToLrcTime(MidiBard.CurrentPlaybackDuration ?? LrcLines.Max(i => i.TimeStamp));
            LrcMetadata["re"] = @"www.MidiBard.org";
            LrcMetadata["ve"] = MidiBard.VersionString;

            //write metadatas
            foreach (var metadata in LrcMetadata)
            {
                sb.AppendLine($"[{metadata.Key}:{metadata.Value}]");
            }

            //write time-lyric lines
            foreach (var lyricEntry in LrcLines)
            {
                sb.AppendLine($"[{ToLrcTime(lyricEntry.TimeStamp)}]{lyricEntry.Text}");
            }

            return sb.ToString();
        }

        protected Lrc()
        {
            LrcMetadata = new Dictionary<string, string>();
            LrcLines = new List<LrcEntry>();
        }

        public Lrc(string lrcPath) : this(File.ReadAllLines(lrcPath, GetEncoding(lrcPath)))
        {
            FilePath = lrcPath;
        }

        private static Encoding GetEncoding(string lrcPath)
        {
            var encoding = FileHelpers.GetEncoding(lrcPath);
            PluginLog.Information(encoding.ToString());
            return encoding;
        }

        public Lrc(string[] lines) : this()
        {
            foreach (var line in lines)
            {
                var matchLyric = ParseTimeLyric.Match(line);
                if (matchLyric.Success)
                {
                    var minutes = matchLyric.Groups["min"].Value;
                    var seconds = matchLyric.Groups["sec"].Value;
                    var ff = matchLyric.Groups["ff"].Value;
                    var lyricText = matchLyric.Groups["text"].Value;

                    var fractionSecond = double.Parse($"0.{ff}", CultureInfo.InvariantCulture);
                    var time = TimeSpan.FromMinutes(int.Parse(minutes)) + TimeSpan.FromSeconds(int.Parse(seconds) + fractionSecond);

                    LrcLines.Add(new LrcEntry() { TimeStamp = time, Text = lyricText });
                }
                else
                {
                    var matchMetadata = ParseMetadata.Match(line);
                    if (matchMetadata.Success)
                    {
                        var idTag = matchMetadata.Groups["idTag"].Value;
                        var tagContent = matchMetadata.Groups["tagContent"].Value;

                        LrcMetadata[idTag] = tagContent;
                    }
                }

            }

            Offset = LrcMetadata.TryGetValue("offset", out var offsetString) && long.TryParse(offsetString, out var offset) ? offset : 0L;
            //Sort();
        }

        /// <summary>
        /// Get lyric info
        /// </summary>
        /// <param name="LrcPath">path of lrc file</param>
        /// <returns>returns lyrics info</returns>
        public static void InitLrc(string midiFilePath)
        {
            bool loadSuccessfull = true;

            var lrcPath = Path.ChangeExtension(midiFilePath, "lrc");

            try
            {
                PlayingLrc = new Lrc(lrcPath);
                LrcEditor.Instance.LoadLrcToEditor(PlayingLrc);
            }
            catch (Exception ex)
            {
                PlayingLrc = null;
                loadSuccessfull = false;
                //PluginLog.Error(ex.ToString());
            }

            if (loadSuccessfull)
            {
                PluginLog.Info($"Load LRC: {lrcPath}");
                api.ChatGui.Print($"[MidiBard 2] Lyrics Loaded: {lrcPath}");
            }
        }

        public static bool HasLyric()
        {
            return PlayingLrc != null;
        }


        /// <summary>
        /// process lrc line to get poster character name and lyric
        /// </summary>
        /// <param name="line">input lrc line without timestamp</param>
        /// <param name="characterName">parsed character name if exist</param>
        /// <param name="lyric">parsed lyric text ready to post</param>
        /// <returns>Input line has a character name</returns>
        static bool ProcessLine(string line, out string characterName, out string lyric)
        {
            var match = ParsePoster.Match(line);
            if (match.Success)
            {
                characterName = match.Groups["poster"].Value;
                lyric = match.Groups["text"].Value;
                return true;
            }

            characterName = "";
            lyric = line;

            return false;
        }

        public static int LrcIdx = -1;
        internal static int LRCDeltaTime = 50;
        static bool SongTitlePosted = false;

        public static bool LrcLoaded()
        {
            return api.PartyList.IsInParty() && Lrc.PlayingLrc != null && Lrc.PlayingLrc.LrcLines.Count > 0;
        }

        public static void Play()
        {
            LRCDeltaTime = 100; // Assume usual delay between sending and other clients receiving the message would be ~100ms

            if (HasLyric())
            {
                if (!api.PartyList.IsInParty())
                {
                    api.ChatGui.Print(string.Format("[MidiBard 2] Not in a party, Lyrics will not be posted."));
                }
            }

            try
            {
                PlayingLrc?.Sort();
                if (MidiPlayerControl._stat != MidiPlayerControl.e_stat.Paused)
                {
                    LrcIdx = -1;
                }
            }
            catch (Exception e)
            {
                PluginLog.Error(e.ToString());
            }
        }

        public static void Stop()
        {
            LrcIdx = -1;
            SongTitlePosted = false;
        }

        internal static void ChangeLRCDeltaTime(int delta)
        {
            if (!MidiBard.IsPlaying)
            {
                LRCDeltaTime = 100;
                return;
            }

            LRCDeltaTime += delta;
        }

        public static void EnsembleStart()
        {
            if (PlayingLrc == null)
                return;

            PlayingLrc.Offset += (long)(4.045 * 1000); // a hack way to get ensemble delay, see MidiFilePlot.cs:90 
            //PluginLog.LogVerbose("LRC Offset: " + PlayingLrc.Offset);
        }

        public static void Tick(IFramework framework)
        {
            try
            {
                if (!MidiBard.config.playLyrics || MidiPlayerControl._stat != MidiPlayerControl.e_stat.Playing || !HasLyric())
                {
                    return;
                }

                var ensembleRunning = MidiBard.AgentMetronome.EnsembleModeRunning;
                var playingLrc = PlayingLrc;

                // post song info at the beginning
                if (!SongTitlePosted && api.PartyList.IsPartyLeader())
                {
                    var msg = $"♪ {playingLrc.Title} ♪ ";
                    msg += !string.IsNullOrWhiteSpace(playingLrc.Artist) ? $"Artist: {playingLrc.Artist} ♪ " : "";
                    msg += !string.IsNullOrWhiteSpace(playingLrc.Album) ? $"Album: {playingLrc.Album} ♪ " : "";
                    msg += !string.IsNullOrWhiteSpace(playingLrc.LrcBy) ? $"Lyric By: {playingLrc.LrcBy} ♪ " : "";

                    if (!ensembleRunning)
                    {
                        msg = "/p " + msg;
                    }
                    else
                    {
                        msg = "/s " + msg;
                    }

                    Chat.SendMessage(msg);
                    SongTitlePosted = true;
                    PluginLog.Information($"song title posted");
                }

                //TODO: when lrc multiple lines has same timestamp, all lines should be posted

                // post lyrics
                var idx = playingLrc.FindLrcIdx(MidiBard.CurrentPlaybackTime);
                if (idx < 0 || idx == LrcIdx || LrcIdx >= playingLrc.LrcLines.Count) return;
                PluginLog.Information($"post lyric {idx}");

                bool shouldPostLyric = false;
                var isCharacterPostLyric = ProcessLine(playingLrc.LrcLines[idx].Text, out var characterName, out var lyric);
                PluginLog.Debug($"Poster: {characterName}, Lyric: {lyric}");
                if (ensembleRunning)
                {
                    if (isCharacterPostLyric)
                    {
                        if (api.ClientState.LocalPlayer?.Name.TextValue.ContainsIgnoreCase(characterName) == true)
                        {
                            shouldPostLyric = true;
                        }
                    }
                    else
                    {
                        if (api.PartyList.IsPartyLeader())
                        {
                            shouldPostLyric = true;
                        }
                    }
                }

                PluginLog.Verbose($@"Post Lyrics: {shouldPostLyric}");

                if (shouldPostLyric)
                {
                    string msg = $"/s ♪ {lyric} ♪";
                    PluginLog.Verbose($"{lyric}");
                    Chat.SendMessage(msg);
                }

                LrcIdx = idx;
            }
            catch (Exception ex)
            {
                PluginLog.Error($"exception: {ex}");
            }
        }
        internal int FindLrcIdx(TimeSpan? playbackTime)
        {
            if (playbackTime is null) return -1;
            if (!LrcLines.Any()) return -1;
            var currentLrcTime = playbackTime - TimeSpan.FromMilliseconds(Offset) + TimeSpan.FromMilliseconds(LRCDeltaTime);
            if (currentLrcTime < TimeSpan.Zero) return -1;

            var maxBy = LrcLines.MaxBy(i => i.TimeStamp < currentLrcTime ? (TimeSpan?)i.TimeStamp : null);
            // For the 1st line of lyrics
            // Even Func<TSource,TKey> keySelector is NULL, MaxBy always return the 1st element of the list
            // So we need an extra check to avoid posting 1st line immediately
            if (currentLrcTime < maxBy.TimeStamp)
            {
                return -1;
            }

            return LrcLines.IndexOf(maxBy);
        }
    }
}
