using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

using iTunesLib;

using PushbulletSharp.Filters;
using PushbulletSharp.Models.Responses.WebSocket;
using PushbulletSharp;

using WebSocketSharp;
using PushbulletSharp.Models.Responses;

using Logger = SamSeifert.Utilities.Logger;
using SamSeifert.Utilities.DataStructures;
using System.IO;
using SamSeifert.Utilities;

namespace iTunesGoogleHome
{
    public partial class Form1 : Form
    {
        DateTime lastChecked = DateTime.Now;
        public PushbulletClient Client;
        WebSocket ws;

        public bool _HesDeadJim = false;

        private EditDistanceDict<String> _Playlists = new EditDistanceDict<String>();
        private EditDistanceDict<String> _Songs = new EditDistanceDict<String>();
        private EditDistanceDict<String> _Artists = new EditDistanceDict<String>();
        private EditDistanceDict<String> _Albums = new EditDistanceDict<String>();
        private EditDistanceDict<String> _Song_Artists = new EditDistanceDict<String>();
        private EditDistanceDict<String> _Album_Artists = new EditDistanceDict<String>();

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Logger.Writer = (String s) =>
            {
                Console.WriteLine(DateTime.Now + " " + s);
            };

            this.RefreshItunesData();

            this.LoadFormState();

            this.textBox1.Text = TextSettings.Read("pbkey.txt") ?? "";

            this.bStartPushbullet_Click(sender, e);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            this._HesDeadJim = true;

            if (this.ws != null)
            {
                (this.ws as IDisposable).Dispose();
                this.ws = null;
            }

            TextSettings.Save("pbkey.txt", this.textBox1.Text);

            this.SaveFormState();
        }

        private void bStartPushbullet_Click(object sender, EventArgs e)
        {
            string key = this.textBox1.Text.Trim();
            if (key.Length > 0)
            {
                this.Client = new PushbulletClient(key, TimeZoneInfo.Local);
                ws = new WebSocket(string.Concat("wss://stream.pushbullet.com/websocket/", key));
                ws.OnMessage += Ws_OnMessage;
                ws.Connect();
            }
            this.bStartPushbullet.Enabled = false;
        }

        private void Ws_OnMessage(object sender, MessageEventArgs e)
        {
            if (this._HesDeadJim) return;

            JavaScriptSerializer js = new JavaScriptSerializer();
            WebSocketResponse response = js.Deserialize<WebSocketResponse>(e.Data);

            switch (response.Type)
            {
                // Heartbeat
                case "nop":
                    Logger.WriteLine("Heartbeat");
                    break;
                case "tickle":
                    PushResponseFilter filter = new PushResponseFilter()
                    {
                        Active = true,
                        ModifiedDate = lastChecked
                    };

                    var pushes = Client.GetPushes(filter);
                    foreach (var push in pushes.Pushes)
                    {
                        if ((push.Created - lastChecked).TotalDays > 0)
                        {
                            lastChecked = push.Created;
                            this.NewNotification(push);
                        }
                    }

                    break;
                case "push":
                    Logger.WriteLine(string.Format("New push recieved on {0}.", DateTime.Now));
                    Logger.WriteLine("Push Type: " + response.Push.Type);
                    Logger.WriteLine("Response SubType: " + response.Subtype);
                    break;
                default:
                    Logger.WriteLine("new type that is not supported");
                    break;
            }
        }

        private void NewNotification(PushResponse push)
        {
            switch (push.Title)
            {
                case "next":
                    (new iTunesAppClass())?.NextTrack();
                    Logger.WriteLine("Next Track");
                    break;
                case "pause":
                    (new iTunesAppClass())?.Stop();
                    Logger.WriteLine("Pause");
                    break;
                case "louder":
                    new iTunesAppClass().SoundVolume += 10;
                    Logger.WriteLine("Volume Up");
                    break;
                case "quieter":
                    new iTunesAppClass().SoundVolume -= 10;
                    Logger.WriteLine("Volume Down");
                    break;
                case "play":
                    var trimmed = push.Body?.Trim() ?? "";
                    if (trimmed.Length == 0)
                    {
                        (new iTunesAppClass())?.Play();
                        Logger.WriteLine("Empty Play");
                    }
                    else this.FindBestMatchAndPlay(trimmed);
                    break;
                default:
                    Logger.WriteError(this, "Unrecognized Title: " + push.Title);
                    break;
            }
        }

        const string middle = "*&J@";
        string jjoin(String a1, String a2) { return a1 + middle + a2; }
        void ujoin(String inp, out String a1, out String a2)
        {
            var spl = inp.Split(new String[] { middle }, StringSplitOptions.None);
            if (spl.Length == 2)
            {
                a1 = spl[0];
                a2 = spl[1];
            }
            else
            {
                Logger.WriteError(this, "ojoin has incorrect number of middles: " + spl.Length);
                a1 = "";
                a2 = "";
            }
        }












        private enum MatchType
        {
            nan,
            song,
            album,
            artist,
            playlist,
            song_artist,
            album_artist
        }

        private void RefreshItunesData(iTunesApp itunes = null)
        {
            Logger.WriteLine("Refreshing Data");

            if (itunes == null)
                itunes = new iTunesLib.iTunesApp();

            this._Songs.Clear();
            this._Song_Artists.Clear();
            this._Albums.Clear();
            this._Album_Artists.Clear();
            this._Artists.Clear();
            this._Playlists.Clear();

            foreach (IITTrack track in itunes.LibraryPlaylist.Tracks)
            {
                if (track.Kind != ITTrackKind.ITTrackKindFile) continue;
                
                // IITFileOrCDTrack file = (IITFileOrCDTrack)track;
                // FileInfo fi = new FileInfo(file.Location);

                string song_key = track.Name.ToLower();
                string artist_key = track.Artist.ToLower();
                string album_key = track.Album.ToLower();

                _Artists[artist_key] = track.Artist;
                _Artists["artist " + artist_key] = track.Artist;
                _Songs[song_key] = track.Name;
                _Songs["song " + song_key] = track.Name;
                _Albums[album_key] = track.Album;
                _Albums["album " + album_key] = track.Album;

                String jjoined_key;
                String jjoined_val;

                jjoined_key = jjoin(song_key, artist_key);
                jjoined_val = jjoin(track.Name, track.Artist);
                _Song_Artists[jjoined_key] = jjoined_val;
                _Song_Artists["song " + jjoined_key] = jjoined_val;

                jjoined_key = jjoin(album_key, artist_key);
                jjoined_val = jjoin(track.Album, track.Artist);
                _Album_Artists[jjoined_key] = jjoined_val;
                _Album_Artists["album " + jjoined_key] = jjoined_val;
            }

            // get list of all playlists defined in 
            // Itunes and add them to the combobox
            foreach (IITPlaylist pl in itunes.LibrarySource.Playlists)
            {
                switch (pl.Name)
                {
                    case "Library": // Defaults.  Ignore these
                    case "Music":
                    case "Movies":
                    case "TV Shows":
                    case "Podcasts":
                    case "Audiobooks":
                    case "Genius":
                        break;
                    default:
                        String playlist_key = pl.Name.ToLower();
                        this._Playlists[playlist_key] = pl.Name;
                        this._Playlists["playlist " + playlist_key] = pl.Name;
                        this._Playlists[playlist_key + " playlist"] = pl.Name;
                        break;
                }
            }

            // divide 2 becuase we add everything to the dictionaries twice.  Once raw, and once with qualifier
            Logger.WriteLine(this._Songs.Count / 2 + " Songs!");
            Logger.WriteLine(this._Albums.Count / 2 + " Albums!");
            Logger.WriteLine(this._Artists.Count / 2 + " Artists!");
            Logger.WriteLine(this._Playlists.Count / 2 + " Playlists!");
        }



















        private void FindBestMatchAndPlay(String query)
        {
            var tup = FindBestMatch(query); // NLP
            this.PlayMatched(tup.Item1, tup.Item2);
        }

        private Tuple<String, MatchType> FindBestMatch(String original_query)
        {
            var query = original_query.ToLower();

            MatchType best_matchtype = MatchType.nan;
            float best_score = float.MaxValue;
            String best_match = null;

            Action<String, MatchType, EditDistanceDict<String>> checker = (String key, MatchType t, EditDistanceDict<String> dict) =>
            {
                if (best_score <= 0) // don't even bother.  Let's increase response time!
                    return;

                String current_match = null;
                String current_key = null;
                float score = dict.Get(key, out current_key, out current_match);

                if (t == MatchType.playlist) score -= 1; // Prefer playlists! (play pentatonix = play pentatonix playlist, not artist).

                if (score < best_score )
                {
                    best_score = score;
                    best_match = current_match;
                    best_matchtype = t;
                }
            };

            // Check in order of sizing.  Most likely to ask for playlist or artist, check those first.
            checker(query, MatchType.playlist, this._Playlists); 
            checker(query, MatchType.artist, this._Artists);
            checker(query, MatchType.album, this._Albums);
            checker(query, MatchType.song, this._Songs);

            int by_index = -1;
            while  (true)
            {
                const string search_split = " by ";
                by_index = query.IndexOf(search_split, by_index + 1);

                if (by_index >= 0)
                {
                    String thing = query.Substring(0, by_index);
                    String artist = query.Substring(by_index + search_split.Length);
                    String joined = jjoin(thing, artist);
                    checker(joined, MatchType.album_artist, this._Album_Artists);
                    checker(joined, MatchType.song_artist, this._Song_Artists);
                }
                else break;
            }

            if (best_score > 0)
                Logger.WriteLine("Matching \"" + original_query + "\" to: \"" + best_match + "\", " + best_matchtype);

            return new Tuple<String, MatchType>(best_match, best_matchtype);
        }

        private void PlayMatched(String best_match, MatchType best_matchtype )
        {
            String temp_artist;
            switch (best_matchtype)
            {
                case MatchType.song:
                    this.PlayMatchedSong(best_match);
                    break;
                case MatchType.song_artist:
                    String song;
                    ujoin(best_match, out song, out temp_artist);
                    this.PlayMatchedSong(song, temp_artist);
                    break;
                case MatchType.album:
                    this.PlayMatchedAlbum(best_match);
                    break;
                case MatchType.album_artist:
                    String album;
                    ujoin(best_match, out album, out temp_artist);
                    this.PlayMatchedAlbum(album, temp_artist);
                    break;
                case MatchType.artist:
                    this.PlayMatchedArtist(best_match);
                    break;
                case MatchType.playlist:
                    this.PlayMatchedPlaylist(best_match);
                    break;
            }
        }

        private void PlayMatchedPlaylist(String playlist)
        {
            Logger.WriteLine("Playing Playlist: " + playlist);

            var itunes = new iTunesLib.iTunesApp();
            // Rather than storing the handle (will be incorrect when itunes closes and is reopened, just find playlist by searching!
            foreach (IITPlaylist pl in itunes.LibrarySource.Playlists)
            {
                if (pl.Name != playlist) continue;
                this.PlayPlaylist(pl);
                return;
            }
            this.RefreshItunesData(itunes);
        }

        private void PlayMatchedSong(String song, String artist = null)
        {
            if (artist == null) Logger.WriteLine("Playing Song: " + song);
            else Logger.WriteLine("Playing Song: " + song + ", By: " + artist);

            var itunes = new iTunesLib.iTunesApp();
            // Rather than storing the handle (will be incorrect when itunes closes and is reopened, just find playlist by searching!
            foreach (IITTrack track in itunes.LibraryPlaylist.Tracks)
            {
                if (track.Kind != ITTrackKind.ITTrackKindFile) continue;
                if (track.Name != song) continue;
                if (artist != null)
                    if (track.Artist != artist) continue;
                this.SetupAndRunAutomatedPlaylist(itunes, track);
                return;
            }
            this.RefreshItunesData(itunes);
        }

        private void PlayMatchedAlbum(String album, String artist = null)
        {
            if (artist == null) Logger.WriteLine("Playing Album: " + album);
            else Logger.WriteLine("Playing Album: " + album + ", By: " + artist);

            var itunes = new iTunesLib.iTunesApp();
            var tracks = new List<IITTrack>();
            // Rather than storing the handle (will be incorrect when itunes closes and is reopened, just find playlist by searching!
            foreach (IITTrack track in itunes.LibraryPlaylist.Tracks)
            {
                if (track.Kind != ITTrackKind.ITTrackKindFile) continue;
                if (track.Album != album) continue;
                if (artist != null)
                    if (track.Artist != artist) continue;
                tracks.Add(track);
            }
            if (tracks.Count > 0) this.SetupAndRunAutomatedPlaylist(itunes, tracks.ToArray());
            else this.RefreshItunesData(itunes);
        }

        private void PlayMatchedArtist(String artist)
        {
            Logger.WriteLine("Playing Artist: " + artist);

            var itunes = new iTunesLib.iTunesApp();
            var tracks = new List<IITTrack>();
            // Rather than storing the handle (will be incorrect when itunes closes and is reopened, just find playlist by searching!
            foreach (IITTrack track in itunes.LibraryPlaylist.Tracks)
            {
                if (track.Kind != ITTrackKind.ITTrackKindFile) continue;
                if (track.Artist != artist) continue;
                tracks.Add(track);
            }
            if (tracks.Count > 0) this.SetupAndRunAutomatedPlaylist(itunes, tracks.ToArray());
            else this.RefreshItunesData(itunes);
        }

        public void SetupAndRunAutomatedPlaylist(iTunesApp itunes, params IITTrack[] tracks)
        {
            const string automated_playlist_name = "Automated";

            IITUserPlaylist pl;

            do // If we call delete mid loop, we sometimes don't get second instances of playlist.
            {
                pl = null;
                foreach (IITUserPlaylist old in itunes.LibrarySource.Playlists.OfType<IITUserPlaylist>())
                {
                    if (old.Name != automated_playlist_name) continue;
                    pl = old;
                    break;
                }
                pl?.Delete();
            }
            while (pl != null);

            pl = (IITUserPlaylist)(itunes.CreatePlaylist(automated_playlist_name));

            Array.Sort(tracks, CompareTracks);

            foreach (var t in tracks)
            {
                object track = t;
                pl.AddTrack(ref track);
            }

            this.PlayPlaylist(pl);
        }

        public static int CompareTracks(IITTrack t1, IITTrack t2)
        {
            int c = 0;

            c = t1.Artist.CompareTo(t2.Artist); if (c != 0) return c;
            c = t1.Album.CompareTo(t2.Album); if (c != 0) return c;
            c = t1.TrackNumber.CompareTo(t2.TrackNumber); if (c != 0) return c;

            return 0;
        }

        private void PlayPlaylist(IITPlaylist pl)
        {
            pl.PlayFirstTrack();
            pl.SongRepeat = ITPlaylistRepeatMode.ITPlaylistRepeatModeAll;
            pl.Shuffle = false;
        }

    }
}
