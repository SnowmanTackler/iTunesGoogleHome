using iTunesLib;

using PushbulletSharp.Models.Responses;

using SamSeifert.Utilities;
using SamSeifert.Utilities.DataStructures;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace iTunesGoogleHome
{
    public class PushHandler
    {
        const string automated_playlist_name = "Automated";

        private TextBox tbMatches;

        private bool _FirstSearch = true;
        private EditDistanceDict<String> _Playlists = new EditDistanceDict<String>();
        private EditDistanceDict<String> _Songs = new EditDistanceDict<String>();
        private EditDistanceDict<String> _Artists = new EditDistanceDict<String>();
        private EditDistanceDict<String> _Albums = new EditDistanceDict<String>();
        private EditDistanceDict<String> _Song_Artists = new EditDistanceDict<String>();
        private EditDistanceDict<String> _Album_Artists = new EditDistanceDict<String>();

        public PushHandler(TextBox matches)
        {
            this.tbMatches = matches;
        }


        public void NewNotification(PushResponse push)
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
                case "play music":
                case "play all music":
                case "play all":
                case "play all songs":
                case "play songs":
                    var trimmed = push.Body?.Trim() ?? "";
                    if (trimmed.Length == 0)
                    {
                        (new iTunesAppClass())?.Play();
                        Logger.WriteLine("Empty Play");
                    }
                    else
                    {
                        this.FindBestMatchAndPlay(trimmed);
                    }
                    break;
                default:
                    Logger.WriteError(this, "Unrecognized Title: " + push.Title);
                    break;
            }
        }

        const string middle = " & "; // this is ok because ampersands are all removed from queries
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

        private void RefreshItunesData(iTunesApp itunes, bool use_songs = true)
        {
            using (Logger.Time("Refreshing iTunes Data"))
            {
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

                    Func<String, String> to_key = s =>
                    {
                        return s.ToLower()
                        .Replace("&", " and ") // "women & songs" => "Women and songs"
                        .Trim()
                        .RemoveDoubleSpaces()
                        ;
                    };

                    string song_key = to_key(track.Name);
                    string artist_key = to_key(track.Artist);
                    string album_key = to_key(track.Album);

                    _Artists[artist_key] = track.Artist;
                    _Artists["artist " + artist_key] = track.Artist;

                    if ("alanis morissette" == artist_key)
                    {
                        _Artists["wet cat"] = track.Artist;
                        _Artists["the wet cat"] = track.Artist;
                    }

                    String jjoined_key;
                    String jjoined_val;

                    if (use_songs) // Songs too slow on home computer.  Don't populate List
                    {
                        _Songs[song_key] = track.Name;
                        _Songs["song " + song_key] = track.Name;
                        jjoined_key = jjoin(song_key, artist_key);
                        jjoined_val = jjoin(track.Name, track.Artist);
                        _Song_Artists[jjoined_key] = jjoined_val;
                        _Song_Artists["song " + jjoined_key] = jjoined_val;
                    }

                    _Albums[album_key] = track.Album;
                    _Albums["album " + album_key] = track.Album;
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
                        // Defaults.  Ignore these
                        case "Library": 
                        case "Music":
                        case "Movies":
                        case "TV Shows":
                        case "Podcasts":
                        case "Audiobooks":
                        case "Genius":
                        case automated_playlist_name:
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
                if (use_songs)
                    Logger.WriteLine(this._Songs.Count / 2 + " Songs!");
                Logger.WriteLine(this._Albums.Count / 2 + " Albums!");
                Logger.WriteLine(this._Artists.Count / 2 + " Artists!");
                Logger.WriteLine(this._Playlists.Count / 3 + " Playlists!");
            }
        }



















        private void FindBestMatchAndPlay(String query)
        {
            Tuple<String, MatchType> tup;
            iTunesAppClass itunes;

            using (Logger.Time("Opening iTunes"))
                itunes = new iTunesAppClass();

            using (Logger.Time("Pausing iTunes"))
                itunes.Stop(); // Makes it seem more responsive

            if (this._FirstSearch) this.RefreshItunesData(itunes);
            this._FirstSearch = false;

            using (Logger.Time("Finding Match"))
                tup = FindBestMatch(query); // NLP
            using (Logger.Time("Working iTunes"))
                this.PlayMatched(itunes, tup.Item1, tup.Item2);

            MainForm.ThreadSafeTextBoxWrite(this.tbMatches, "\"" + query + "\" to " + tup.Item2 + " \"" + tup.Item1 + "\"", "");
        }

        private String UngoogleQuery(String q)
        {
            return (" " + q + " ")
                .ToLower()
                .Replace(" xmas ", " christmas ") // if you say "Christmas" google returns "Xmas"
                .Trim();
        }

        private Tuple<String, MatchType> FindBestMatch(String query)
        {
            query = UngoogleQuery(query);

            MatchType best_matchtype = MatchType.nan;
            float best_score = float.MaxValue;
            String best_match = null;

            Action<String, MatchType, EditDistanceDict<String>> checker = (String key, MatchType t, EditDistanceDict<String> dict) =>
            {
                if (dict.Count == 0) return;
                if (best_score <= 0) return; // don't bother searching if wel already have perfect match.  Increases response time!

                String current_match = null;
                String current_key = null;
                float score = dict.Get(key, out current_key, out current_match);

                if (t == MatchType.playlist) score -= 1; // Prefer playlists! (play pentatonix = play pentatonix playlist, not artist).

                if (score < best_score)
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
            while (true)
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

            return new Tuple<String, MatchType>(best_match, best_matchtype);
        }

        private void PlayMatched(iTunesApp itunes, String best_match, MatchType best_matchtype)
        {
            String temp_artist;
            switch (best_matchtype)
            {
                case MatchType.song:
                    this.PlayMatchedSong(itunes, best_match);
                    break;
                case MatchType.song_artist:
                    String song;
                    ujoin(best_match, out song, out temp_artist);
                    this.PlayMatchedSong(itunes, song, temp_artist);
                    break;
                case MatchType.album:
                    this.PlayMatchedAlbum(itunes, best_match);
                    break;
                case MatchType.album_artist:
                    String album;
                    ujoin(best_match, out album, out temp_artist);
                    this.PlayMatchedAlbum(itunes, album, temp_artist);
                    break;
                case MatchType.artist:
                    this.PlayMatchedArtist(itunes, best_match);
                    break;
                case MatchType.playlist:
                    this.PlayMatchedPlaylist(itunes, best_match);
                    break;
            }
        }

        private void PlayMatchedPlaylist(iTunesApp itunes, String playlist)
        {
            Logger.WriteLine("Playing Playlist: " + playlist);

            // Rather than storing the handle (will be incorrect when itunes closes and is reopened, just find playlist by searching!
            foreach (IITPlaylist pl in itunes.LibrarySource.Playlists)
            {
                if (pl.Name != playlist) continue;
                this.PlayPlaylist(pl);
                return;
            }
            this.RefreshItunesData(itunes);
        }

        private void PlayMatchedSong(iTunesApp itunes, String song, String artist = null)
        {
            if (artist == null) Logger.WriteLine("Playing Song: " + song);
            else Logger.WriteLine("Playing Song: " + song + ", By: " + artist);

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

        private void PlayMatchedAlbum(iTunesApp itunes, String album, String artist = null)
        {
            if (artist == null) Logger.WriteLine("Playing Album: " + album);
            else Logger.WriteLine("Playing Album: " + album + ", By: " + artist);

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

        private void PlayMatchedArtist(iTunesApp itunes, String artist)
        {
            Logger.WriteLine("Playing Artist: " + artist);

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

            var sortable_tracks = tracks.Select(t => new SamTrack(t)).Sorted();
            pl = (IITUserPlaylist)(itunes.CreatePlaylist(automated_playlist_name));
            foreach (var t in sortable_tracks)
            {
                object track = t._Track;
                pl.AddTrack(ref track);
            }

            this.PlayPlaylist(pl);
        }

        private void PlayPlaylist(IITPlaylist pl)
        {
            if (pl.Tracks.Count != 0)
            {
                pl.PlayFirstTrack();
                pl.SongRepeat = ITPlaylistRepeatMode.ITPlaylistRepeatModeAll;
                pl.Shuffle = false;
            }
        }




        /// <summary>
        /// If you try to sort a bunch of IITTracks, you occassionally get the exception below.  I think (but am not certain) each call to IITTrack.Artist
        /// has to go through iTunes, and sort requires daisy chaining too many calls like this together.  Causes COM library to poop out.  Solve this problem
        /// by calling and storing each called variable 
        /// </summary>
        public class SamTrack : IComparable
        {
            public readonly IITTrack _Track;
            private string _Album = null;
            private string _Artist = null;
            private int _TrackNumber = int.MinValue;

            public SamTrack(IITTrack t)
            {
                this._Track = t;
                this._Album = t.Album ?? "";
                this._Artist = t.Artist ?? "";
                this._TrackNumber = t.TrackNumber;
            }

            public int CompareTo(object obj)
            {
                var t1 = this;
                var t2 = obj as SamTrack;

                int c = 0;

                c = t1._Artist.CompareTo(t2._Artist); if (c != 0) return c;
                c = t1._Album.CompareTo(t2._Album); if (c != 0) return c;
                c = t1._TrackNumber.CompareTo(t2._TrackNumber); if (c != 0) return c;

                return 0;
            }
        }
    }
}