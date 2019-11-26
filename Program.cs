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

            Console.WriteLine("\nToday's top artists:");
            Console.WriteLine("====================================================");
            Console.WriteLine("{0, -10} {1, -40}", "Rank", "Artist Name");
            Console.WriteLine("====================================================");
            var topArtistsToday = await GetTopArtistsRankings(api);
            SaveTopArtistsFile(topArtistsToday);
            topArtistsToday.ForEach(artist => Console.WriteLine("{0, -10} {1, -40}", artist.Rank, artist.Name));
            Dictionary<string, int> topArtistsTodayNameRankMapping = ConvertTopArtistsListToDictionary(topArtistsToday);

            var topArtistsYesterday = GetTopArtistsRankings(DateTime.Today.AddDays(-1));
            if (topArtistsYesterday == null)
            {
                Console.WriteLine("No artists from yesterday to compare with.");
                return;
            }
            Dictionary<string, int> topArtistsYesterdayNameRankMapping = ConvertTopArtistsListToDictionary(topArtistsYesterday);

            Console.WriteLine("\nChanges in Artist Rankings since yesterday:");
            Console.WriteLine("====================================================");
            Console.WriteLine("{0, -40} {1}", "Artist Name", "Change");
            Console.WriteLine("====================================================");
            foreach (KeyValuePair<string, int> entry in topArtistsTodayNameRankMapping)
            {
                String artistName = entry.Key;
                int artistRankToday = entry.Value;
                int artistRankYesterday;
                if (topArtistsYesterdayNameRankMapping.TryGetValue(artistName, out artistRankYesterday))
                {
                    int difference = artistRankToday - artistRankYesterday;
                    if (difference < 0)
                    {
                        Console.WriteLine("{0, -40} {1}", artistName, difference);
                    } 
                    else if (difference > 0)
                    {
                        Console.WriteLine("{0, -40} +{1}", artistName, difference);
                    }
                    else
                    {
                        Console.WriteLine("{0, -40}  0", artistName);
                    }
                } 
                else
                {
                    // Artist exists on today's top artists, but not yesterday's.
                    Console.WriteLine("{0, -40}  NEW", artistName);
                }
            }

            foreach (KeyValuePair<string, int> entry in topArtistsYesterdayNameRankMapping)
            {
                String artistName = entry.Key;
                if (!topArtistsTodayNameRankMapping.ContainsKey(artistName))
                {
                    // Artist exists on yesterday's top artists, but not today's.
                    Console.WriteLine("{0, -40}  FELL OFF", artistName);
                }
            }

        }

        private static async Task<List<ArtistRanking>> GetTopArtistsRankings(SpotifyWebAPI api)
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

        private static List<ArtistRanking> GetTopArtistsRankings(DateTime date)
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

        private static Dictionary<string, int> ConvertTopArtistsListToDictionary(List<ArtistRanking> topArtists)
        {
            var topArtistsDictionary = new Dictionary<string, int>();
            topArtists.ForEach(artist => topArtistsDictionary.Add(artist.Name, artist.Rank));
            return topArtistsDictionary;
        }

        private static void SaveTopArtistsFile(List<ArtistRanking> topArtists)
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