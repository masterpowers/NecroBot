using GeoCoordinatePortable;
using Newtonsoft.Json;
using PoGo.NecroBot.Logic.Logging;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;
using POGOProtos.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PoGo.NecroBot.Logic.Tasks
{
    public class ManualWalkSnipeTask
    {
        public class Wrapper
        {
            public List<RarePokemonInfo> data { get; set; }
        }
        public class RarePokemonInfo
        {
            internal double distance;
            internal double estimateTime;
            public double created { get; set; }
            public double latitude { get; set; }
            public double longitude { get; set; }
            public int pokemonId { get; set; }
            public string id { get; set; }
            public bool caught { get; set; }

            public PokemonId Id
            {
                get
                {
                    return (PokemonId)(pokemonId);
                }
            }
            public DateTime expired
            {
                get
                {
                    System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
                    dtDateTime = dtDateTime.AddSeconds(created).ToLocalTime();
                    return dtDateTime.AddMinutes(15);
                }
            }
        }
        private static List<RarePokemonInfo> rarePokemons = new List<RarePokemonInfo>();

        private static ISession _session;
        public static async Task Execute(ISession session, CancellationToken cancellationToken, Func<double, double, Task> actionWhenWalking = null)
        {
            _session = session;

            cancellationToken.ThrowIfCancellationRequested();

            if (!session.LogicSettings.CatchPokemon) return;

            var pokemons = GetRarePokemons(session.Client.CurrentLatitude, session.Client.CurrentLongitude);

            foreach (var pokemon in pokemons)
            {
                string strPokemon = session.Translation.GetPokemonTranslation(pokemon.Id);
                Logger.Write($"TASK EXECUTE moving {Math.Round(LocationUtils.CalculateDistanceInMeters(session.Client.CurrentLatitude, session.Client.CurrentLongitude, pokemon.latitude, pokemon.longitude))}m to catch  {strPokemon}", LogLevel.Info, ConsoleColor.Yellow);

                await session.Navigation.Move(new GeoCoordinate(pokemon.latitude, pokemon.longitude,
                       LocationUtils.getElevation(pokemon.latitude, pokemon.longitude)),
                   async () =>
                   {
                       // Catch normal map Pokemon
                       await CatchNearbyPokemonsTask.Execute(session, cancellationToken);
                       //Catch Incense Pokemon
                       await CatchIncensePokemonsTask.Execute(session, cancellationToken);
                       //Logger.Write("TASK EXECUTE catch rate pokemon step moving.....", LogLevel.Info, ConsoleColor.Yellow);
                        if(actionWhenWalking!= null)
                       {
                           await actionWhenWalking(session.Client.CurrentLatitude, session.Client.CurrentLongitude);
                       }
                       return true;
                       //return await Task.FromResult<bool>(true);
                   },
                   session,
                   cancellationToken);
                await CatchNearbyPokemonsTask.Execute(session, cancellationToken);
                await CatchIncensePokemonsTask.Execute(session, cancellationToken);
            }
            await Task.Run(() => { });
        }

        private static List<RarePokemonInfo> GetRarePokemons(double lat, double lng, bool refreshData = true)
        {
            var speedInMetersPerSecond = _session.LogicSettings.WalkingSpeedInKilometerPerHour / 3.6;

            
            rarePokemons.ForEach((p) =>
            {
                p.distance = LocationUtils.CalculateDistanceInMeters(lat, lng, p.latitude, p.longitude);
                p.estimateTime = p.distance / speedInMetersPerSecond + 30; //margin 30 second
            });

            rarePokemons.RemoveAll(p=> p.expired < DateTime.Now.AddSeconds(p.estimateTime));

            //remove list not reach able (expired)
            if (rarePokemons.Count > 0)
            {
                rarePokemons.OrderBy(p => p.distance);

                var first = rarePokemons.Where(p => !p.caught
                    && p.expired > DateTime.Now.AddSeconds(p.estimateTime)
                ).FirstOrDefault();
                if (first != null)
                {
                    first.caught = true;
                    //rarePokemons.RemoveAt(0);
                    return new List<RarePokemonInfo>() { first };
                }
            }
            else {
                if (refreshData)
                {
                    ReloadData(lat, lng);

                    return GetRarePokemons(lat, lng, false);
                }
            }

            return new List<RarePokemonInfo>();

        }
        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        private static DateTime lastUpdated = DateTime.Now.AddMinutes(-10);
        private static void ReloadData(double lat, double lng)
        {
            if ((DateTime.Now - lastUpdated).TotalSeconds < 30) return; //do not reload data if too short

            HttpClient client = new HttpClient();
            double offset = 0.015;
            string url = $"https://www.pokeradar.io/api/v1/submissions?deviceId=1fd29370661111e6b850a13a2bdc4ebf&minLatitude={lat - offset}&maxLatitude={lat + offset}&minLongitude={lng - offset}&maxLongitude={lng + offset}&pokemonId=0";

            var source = client.GetStringAsync(url);

            var data = JsonConvert.DeserializeObject<Wrapper>(source.Result);

            var rw = new Random();
            var speedInMetersPerSecond = _session.LogicSettings.WalkingSpeedInKilometerPerHour / 3.6;

            int count = 0;
            data.data.ForEach((p) =>
            {
                p.distance = LocationUtils.CalculateDistanceInMeters(lat, lng, p.latitude, p.longitude);
                p.estimateTime = p.distance / speedInMetersPerSecond + 30; //margin 30 second

                if (rarePokemons.Any(x => x.id == p.id)) return;

                if (p.pokemonId == (int)PokemonId.Slowbro ||
                p.pokemonId == (int)PokemonId.Dratini ||
                p.pokemonId == (int)PokemonId.Kangaskhan ||
                p.pokemonId == (int)PokemonId.Abra ||
                p.pokemonId == (int)PokemonId.Scyther)
                {
                    count++;
                    rarePokemons.Add(p);

                }
            });

            client.Dispose();
            Logger.Write($"Found {count} rare pokemon to catch..", LogLevel.Info, ConsoleColor.DarkMagenta);
        }
    }
}
