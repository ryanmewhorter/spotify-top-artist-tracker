using CsvHelper.Configuration.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SpotifyWebApiDemo
{
    class ArtistRanking
    {
        [Index(0)]
        public string Id { get; set; }

        [Index(1)]
        public string Name { get; set; }

        [Index(2)]
        public int Rank { get; set; }

        public override string ToString()
        {
            return $"ArtistRanking: {Rank} - {Name}";
        }
    }
}
