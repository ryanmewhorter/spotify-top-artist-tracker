using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CsvHelper;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web.Enums;
using SpotifyAPI.Web.Models;
using SpotifyWebApiDemo;
using System.Linq;

namespace SpotifyAPI.Web.Examples.CLI
{
    internal static class Program
    {
        private const TimeRangeType _defaultTimeRange = TimeRangeType.ShortTerm;
        private const string repository = "C:\\dev\\sandbox\\csharp\\top_artist_history";
        private const int NEW_ARTIST_RANK_VALUE = Int32.MinValue + 1;
        private const int REMOVED_ARTIST_RANK_VALUE = Int32.MinValue;
        private const string NO_CHANGE_TEXT = "--";
        private const string NEW_ARTIST_TEXT = "new";
        private const string REMOVED_ARTIST_TEXT = "removed";
        private static string _clientId = "";
        private static string _secretId = "";

        public static void Main(string[] args)
        {
            _clientId = string.IsNullOrEmpty(_clientId)
                ? Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID")
                : _clientId;

            _secretId = string.IsNullOrEmpty(_secretId)
                ? Environment.GetEnvironmentVariable("SPOTIFY_SECRET_ID")
                : _secretId;

            Console.WriteLine("####### Spotify API Example #######");
            Console.WriteLine("This example uses AuthorizationCodeAuth.");
            Console.WriteLine(
                "Tip: If you want to supply your ClientID and SecretId beforehand, use env variables (SPOTIFY_CLIENT_ID and SPOTIFY_SECRET_ID)");

            var auth =
                new AuthorizationCodeAuth(_clientId, _secretId, "http://localhost:4002", "http://localhost:4002",
                    Scope.PlaylistReadPrivate | Scope.PlaylistReadCollaborative | Scope.UserTopRead);
            auth.AuthReceived += AuthOnAuthReceived;
            auth.Start();
            auth.OpenBrowser();

            Console.ReadLine();
            auth.Stop(0);



        }

        private static async void AuthOnAuthReceived(object sender, AuthorizationCode payload)
        {
            var auth = (AuthorizationCodeAuth)sender;
            auth.Stop();

            Token token = await auth.ExchangeCode(payload.Code);
            var api = new SpotifyWebAPI
            {
                AccessToken = token.AccessToken,
                TokenType = token.TokenType
            };
            await OnApiReady(api);
        }

        private static async Task OnApiReady(SpotifyWebAPI api)
        {
            PrivateProfile profile = await api.GetPrivateProfileAsync();
            string name = string.IsNullOrEmpty(profile.DisplayName) ? profile.Id : profile.DisplayName;
            Console.WriteLine($"Hello there, {name}!");

            var topArtistsToday = await GetTopArtistsRankings(api);
            SaveTopArtistsFile(topArtistsToday);

            var topArtistsYesterday = GetTopArtistsRankings(DateTime.Today.AddDays(-1));
            if (topArtistsYesterday == null)
            {
                Console.WriteLine("No artists from yesterday to compare with.");
                return;
            }
            IDictionary<string, int> topArtistsTodayNameRankMapping = ConvertTopArtistsListToDictionary(topArtistsToday);
            IDictionary<string, int> topArtistsYesterdayNameRankMapping = ConvertTopArtistsListToDictionary(topArtistsYesterday);
            IDictionary<string, int> rankingChanges = CompareTopArtistRankings(topArtistsTodayNameRankMapping, topArtistsYesterdayNameRankMapping);

            Console.WriteLine("\nToday's top artists:");
            Console.WriteLine("===========================================================================");
            Console.WriteLine("{0, -10} {1, -40} {2, -10}", "Rank", "Artist Name", "Change from Yesterday");
            Console.WriteLine("===========================================================================");
            foreach (KeyValuePair<string, int> entry in topArtistsTodayNameRankMapping)
            {
                Console.WriteLine("{0, -10} {1, -40} {2, -10}", entry.Value, entry.Key, FormatRankChange(rankingChanges[entry.Key]));
                rankingChanges.Remove(entry.Key);
            }
            foreach (KeyValuePair<string, int> entry in rankingChanges)
            {
                Console.WriteLine("{0, -10} {1, -40} {2, -10}", "", entry.Key, REMOVED_ARTIST_TEXT);
            }
            
        }

        private static string FormatRankChange(int rankChange)
        {
            if (rankChange == NEW_ARTIST_RANK_VALUE)
            {
                return NEW_ARTIST_TEXT;
            }
            else if (rankChange == REMOVED_ARTIST_RANK_VALUE)
            {
                return REMOVED_ARTIST_TEXT;
            }
            else if (rankChange > 0)
            {
                return "+" + rankChange;
            }
            else if (rankChange == 0)
            {
                return NO_CHANGE_TEXT;
            }
            else
            {
                return rankChange.ToString();
            }
        }

        private static IDictionary<string, int> CompareTopArtistRankings(
            IDictionary<string, int> rankingsA, IDictionary<string, int> rankingsB)
        {
            Dictionary<string, int> rankingChanges = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> entry in rankingsA)
            {
                String artistName = entry.Key;
                int artistRankA = entry.Value;
                int artistRankB;
                if (rankingsB.TryGetValue(artistName, out artistRankB))
                {
                    rankingChanges.Add(artistName, artistRankA - artistRankB);
                }
                else
                {
                    // Artist exists on today's top artists, but not yesterday's.
                    rankingChanges.Add(artistName, NEW_ARTIST_RANK_VALUE);
                }
            }

            foreach (KeyValuePair<string, int> entry in rankingsB)
            {
                String artistName = entry.Key;
                if (!rankingsA.ContainsKey(artistName))
                {
                    // Artist exists on yesterday's top artists, but not today's.
                    rankingChanges.Add(artistName, REMOVED_ARTIST_RANK_VALUE);
                }
            }

            // Sort dictionary by ranking change descending using LINQ
            return (from entry in rankingChanges
                   orderby entry.Value descending
                   select entry)
                   .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        private static async Task<IEnumerable<ArtistRanking>> GetTopArtistsRankings(SpotifyWebAPI api)
        {
            // Collect Top Artist data from Spotify API and fill list with it.
            List<ArtistRanking> topArtists = new List<ArtistRanking>();
            int ranking = 0;
            await ForEachPage<FullArtist>(api, await api.GetUsersTopArtistsAsync(_defaultTimeRange), artist =>
            {
                var artistRanking = new ArtistRanking { Id = artist.Id, Name = artist.Name, Rank = ++ranking };
                topArtists.Add(artistRanking);
            });
            return topArtists;
        }

        private static IEnumerable<ArtistRanking> GetTopArtistsRankings(DateTime date)
        {
            var artistNameRankMapping = new Dictionary<string, int>();
            var fileLocation = GetTopArtistsFileLocationByDate(date);
            try
            {
                using (var csv = new CsvReader(new StreamReader(fileLocation)))
                {
                    return csv.GetRecords<ArtistRanking>().ToList();
                }
            } catch (Exception ex)
            {
                return null;
            }
        }

        private static IDictionary<string, int> ConvertTopArtistsListToDictionary(IEnumerable<ArtistRanking> topArtists)
        {
            var topArtistsDictionary = new Dictionary<string, int>();
            foreach (var artist in topArtists)
            {
                topArtistsDictionary.Add(artist.Name, artist.Rank);
            }
            return topArtistsDictionary;
        }

        private static void SaveTopArtistsFile(IEnumerable<ArtistRanking> topArtists)
        {
            // Using CsvHelper, write list of Artist Rankings to a CSV file
            using (var writer = new StreamWriter(GetTopArtistsFileLocationByDate(DateTime.Today)))
            using (var csv = new CsvWriter(writer))
            {
                csv.WriteRecords(topArtists);
            }
        }

        private static string GetTopArtistsFileLocationByDate(DateTime date)
        {
            return repository + "\\" + date.Year + "-" + date.Month + "-" + date.Day + "-TOP-SPOTIFY-ARTISTS.csv";
        }

        private static async Task ForEachPage<T>(SpotifyWebAPI api, Paging<T> page, Action<T> operation)
        {
            if (page.Items == null) return;
            page.Items.ForEach(item => {
                operation(item);       
            });
            if (page.HasNextPage())
            {
                await ForEachPage(api, await api.GetNextPageAsync(page), operation);
            }
        }
    }
}