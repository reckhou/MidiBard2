using System.Collections.Generic;

namespace MidiBard.Util
{
    public class MMSongContainer
    {
        public List<MMSong> songs = new List<MMSong>();
        public MMSongContainer()
        {
            var s = new MMSong();
            songs.Add(s);
        }
    }

    public class MMSong
    {
        public List<MMBards> bards = new List<MMBards>();
        public string title { get; set; } = "";
        public string description { get; set; } = "";
    }

    public class MMBards
    {
        public int instrument { get; set; } = 0;
        public Dictionary<int, int> sequence = new Dictionary<int, int>();
    }
}
