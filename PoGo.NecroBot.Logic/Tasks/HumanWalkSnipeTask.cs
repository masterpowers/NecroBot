using GeoCoordinatePortable;
using Newtonsoft.Json;
using PoGo.NecroBot.Logic.Common;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.Interfaces.Configuration;
using PoGo.NecroBot.Logic.Logging;
using PoGo.NecroBot.Logic.Model.Settings;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;
using POGOProtos.Enums;
using POGOProtos.Inventory.Item;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PoGo.NecroBot.Logic.Tasks
{
    public class HumanWalkSnipeTask
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
            public HumanWalkSnipeFilter FilterSetting { get; set; }

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
        private static ILogicSettings _setting;

        private static int pokestopCount = 0;
        private static List<PokemonId> pokemonToBeSnipedIds = null;

        public static async Task<bool> CheckPokeballsToSnipe(int minPokeballs, ISession session,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Refresh inventory so that the player stats are fresh
            await session.Inventory.RefreshCachedInventory();
            var pokeBallsCount = await session.Inventory.GetItemAmountByType(ItemId.ItemPokeBall);
            pokeBallsCount += await session.Inventory.GetItemAmountByType(ItemId.ItemGreatBall);
            pokeBallsCount += await session.Inventory.GetItemAmountByType(ItemId.ItemUltraBall);
            pokeBallsCount += await session.Inventory.GetItemAmountByType(ItemId.ItemMasterBall);

            if (pokeBallsCount < minPokeballs)
            {
                session.EventDispatcher.Send(new SnipeEvent
                {
                    Message =
                        session.Translation.GetTranslation(TranslationString.NotEnoughPokeballsToSnipe, pokeBallsCount,
                            minPokeballs)
                });
                return false;
            }
            return true;
        }
        public static async Task Execute(ISession session, CancellationToken cancellationToken, Func<double, double, Task> actionWhenWalking = null, Func<Task> afterCatchFunc = null)
        {
            pokestopCount++;
            pokestopCount = pokestopCount % 3;

            if (pokestopCount > 0) return;

            _session = session;
            _setting = session.LogicSettings;

            cancellationToken.ThrowIfCancellationRequested();

            if (!_setting.CatchPokemon) return;

            pokemonToBeSnipedIds = _setting.PokemonToSnipe.Pokemon;
            pokemonToBeSnipedIds.AddRange(_setting.HumanWalkSnipeFilters.Where(x=>!pokemonToBeSnipedIds.Any(t => t == x.Key)).Select(x => x.Key).ToList());      //this will combine with pokemon snipe filter
            
            if (_setting.HumanWalkingSnipeTryCatchEmAll)
            {
                var checkBall = await CheckPokeballsToSnipe(_setting.HumanWalkingSnipeCatchEmAllMinBalls, session, cancellationToken);
                if (!checkBall) return;
            }

            bool keepWalking = true;
            bool caughtAnyPokemonInThisWalk = false;

            while (keepWalking)
            {

                var pokemons = GetRarePokemons(session.Client.CurrentLatitude, session.Client.CurrentLongitude, !caughtAnyPokemonInThisWalk);

                foreach (var pokemon in pokemons)
                {
                    caughtAnyPokemonInThisWalk = true;

                    CalculateDistanceAndEstTime(pokemon);
                    var remainTimes = (pokemon.expired - DateTime.Now).TotalSeconds * 0.9; //just use 90% times
                    var catchPokemonTimeEST = (pokemon.distance / 100) * 15;  //assume that 100m we catch 1 pokemon and it took 10 second for each.
                    string strPokemon = session.Translation.GetPokemonTranslation(pokemon.Id);
                    var spinPokestopEST = (pokemon.distance / 50) * 5;
                    session.EventDispatcher.Send(new HumanWalkSnipeEvent()
                    {
                        PokemonId = pokemon.Id,
                        Latitude = pokemon.latitude,
                        Longitude = pokemon.longitude,
                        Distance = pokemon.distance,
                        Expires = (pokemon.expired - DateTime.Now).TotalSeconds,
                        Estimate = (int)pokemon.estimateTime,
                        Type = HumanWalkSnipeEventTypes.StartWalking
                    });

                    await session.Navigation.Move(new GeoCoordinate(pokemon.latitude, pokemon.longitude,
                           LocationUtils.getElevation(pokemon.latitude, pokemon.longitude)),
                       async () =>
                       {
                           var distance = LocationUtils.CalculateDistanceInMeters(pokemon.latitude, pokemon.longitude, session.Client.CurrentLatitude, session.Client.CurrentLongitude);

                           if (pokemon.FilterSetting.CatchPokemonWhileWalking
                               && distance > 50
                               && ((pokemon.estimateTime + catchPokemonTimeEST) < remainTimes)
                           )
                           {
                               // Catch normal map Pokemon
                               await CatchNearbyPokemonsTask.Execute(session, cancellationToken);
                               //Catch Incense Pokemon
                               // await CatchIncensePokemonsTask.Execute(session, cancellationToken);
                               //Logger.Write("TASK EXECUTE catch rate pokemon step moving.....", LogLevel.Info, ConsoleColor.Yellow);
                           }
                           if (actionWhenWalking != null && 
                           _setting.HumanWalkingSnipeSpinWhileWalking &&
                           (pokemon.estimateTime + catchPokemonTimeEST + spinPokestopEST) < remainTimes)
                           {
                               await actionWhenWalking(session.Client.CurrentLatitude, session.Client.CurrentLongitude);
                           }
                           //return true;
                           return await Task.FromResult<bool>(true);
                       },
                       session,
                       cancellationToken);
                    //.ContinueWith(async (p) =>
                    {
                        session.EventDispatcher.Send(new HumanWalkSnipeEvent()
                        {
                            Latitude = pokemon.latitude,
                            Longitude = pokemon.longitude,
                            Type = HumanWalkSnipeEventTypes.DestinationReached
                        });
                        await CatchNearbyPokemonsTask.Execute(session, cancellationToken);
                        await CatchIncensePokemonsTask.Execute(session, cancellationToken);
                    }
                    //);


                }
                keepWalking = _setting.HumanWalkingSnipeTryCatchEmAll && pokemons.Count > 0;
            }

            if (caughtAnyPokemonInThisWalk && !_setting.HumanWalkingSnipeAlwaysWalkBack)
            {
                await afterCatchFunc?.Invoke();
            }
        }
        static void CalculateDistanceAndEstTime(RarePokemonInfo p)
        {
            var speedInMetersPerSecond = _setting.WalkingSpeedInKilometerPerHour / 3.6;

            p.distance = LocationUtils.CalculateDistanceInMeters(_session.Client.CurrentLatitude, _session.Client.CurrentLongitude, p.latitude, p.longitude);
            p.estimateTime = p.distance / speedInMetersPerSecond + 30; //margin 30 second
        }
        private static List<RarePokemonInfo> GetRarePokemons(double lat, double lng, bool refreshData = true)
        {
            if (refreshData)
            {
                FetchData(lat, lng);
            }

            rarePokemons.RemoveAll(p => p.expired < DateTime.Now);

            rarePokemons.ForEach((p) =>
            {
                CalculateDistanceAndEstTime(p);
            });

            //rarePokemons.RemoveAll(p=> p.expired < DateTime.Now.AddSeconds(p.estimateTime));

            //remove list not reach able (expired)
            if (rarePokemons.Count > 0)
            {
                var ordered = rarePokemons.Where(p => !p.caught
                    && p.expired > DateTime.Now.AddSeconds(p.estimateTime)
                    && p.distance < p.FilterSetting.MaxDistance &&
                    p.estimateTime < p.FilterSetting.MaxWalkTimes
                )
                .OrderBy(p => p.FilterSetting.Priority)
                .ThenBy(p=>p.distance);
                if (ordered != null && ordered.Count() > 0)
                {
                    var first = ordered.First();
                    first.caught = true;
                    //rarePokemons.RemoveAt(0);
                    var results = new List<RarePokemonInfo>() { first };
                    //foreach (var item in ordered)
                    //{
                    //    if (item.id == first.id) continue;

                    //    if (LocationUtils.CalculateDistanceInMeters(first.latitude, first.longitude, item.latitude, item.longitude) < 200)
                    //    {
                    //        results.Add(item);
                    //    }
                    //}
                    return results;
                }
            }


            return new List<RarePokemonInfo>();

        }

        private static void FetchData(double lat, double lng)
        {
            FetchFromPokeradar(lat, lng);
            FetchFromSkiplagged(lat, lat);
            //process data
            PostProcessDataFetched();
            
        }

        private static void PostProcessDataFetched()
        {
            throw new NotImplementedException();
        }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        private static DateTime lastUpdated = DateTime.Now.AddMinutes(-10);
        private static void FetchFromPokeradar(double lat, double lng)
        {
            if ((DateTime.Now - lastUpdated).TotalSeconds < 30) return; //do not reload data if too short

            try
            {
            
            HttpClient client = new HttpClient();
            double offset = _setting.HumanWalkingSnipeSnipingScanOffset; //0.015 
            string url = $"https://www.pokeradar.io/api/v1/submissions?deviceId=1fd29370661111e6b850a13a2bdc4ebf&minLatitude={lat - offset}&maxLatitude={lat + offset}&minLongitude={lng - offset}&maxLongitude={lng + offset}&pokemonId=0";

            var source = client.GetStringAsync(url);

            var data = JsonConvert.DeserializeObject<Wrapper>(source.Result);

            var rw = new Random();
            var speedInMetersPerSecond = _setting.WalkingSpeedInKilometerPerHour / 3.6;
            lastUpdated = DateTime.Now;
            int count = 0;
            data.data.ForEach((p) =>
            {
                p.distance = LocationUtils.CalculateDistanceInMeters(lat, lng, p.latitude, p.longitude);
                p.estimateTime = p.distance / speedInMetersPerSecond + 30; //margin 30 second
                //the pokemon data already in the list
                if (rarePokemons.Any(x => x.id == p.id)) return;
                //check if pokemon in the snip list
                if (!pokemonToBeSnipedIds.Any(x => x == p.Id)) return;

                count++;
                var snipeSetting = _setting.HumanWalkSnipeFilters.FirstOrDefault(x => x.Key == p.Id);

                HumanWalkSnipeFilter config = new HumanWalkSnipeFilter(_setting.HumanWalkingSnipeMaxDistance, 
                    _setting.HumanWalkingSnipeMaxEstimateTime,
                    3, //default priority
                    _setting.HumanWalkingSnipeTryCatchEmAll,
                    _setting.HumanWalkingSnipeSpinWhileWalking);

                if (_setting.HumanWalkSnipeFilters.Any(x => x.Key == p.Id))
                {
                    config = _setting.HumanWalkSnipeFilters.First(x => x.Key == p.Id).Value;
                }
                p.FilterSetting = config;

                rarePokemons.Add(p);
            });

            client.Dispose();
                if (count > 0)
                {
                    _session.EventDispatcher.Send(new HumanWalkSnipeEvent()
                    {
                        Type = HumanWalkSnipeEventTypes.PokemonScanned,
                        RarePokemons = rarePokemons.Select(p => p.Id.ToString()).ToList()
                    });

                    if (_setting.HumanWalkingSnipeDisplayList)
                    {
                        rarePokemons = rarePokemons.OrderBy(p => p.FilterSetting.Priority).OrderBy(p => p.distance).ToList();
                        var ordered = rarePokemons.Where(p => p.expired > DateTime.Now.AddSeconds(p.estimateTime)).ToList();

                        if (ordered.Count > 0)
                        {
                            Logger.Write(string.Format("          | Name              |  Distance     |  Expires        |  Travel times   | Catchable"));
                            foreach (var pokemon in ordered)
                            {
                                Logger.Write(string.Format("SNIPPING  | {0}  \t|  {1:0.00}m  \t|  {2:mm} min {2:ss} sec  |  {3:00} min {4:00} sec  | {5}",
                                    _session.Translation.GetPokemonTranslation(pokemon.Id),
                                    pokemon.distance,
                                    pokemon.expired - DateTime.Now,
                                    pokemon.estimateTime / 60,
                                    pokemon.estimateTime % 60,
                                    pokemon.expired < DateTime.Now.AddSeconds(pokemon.estimateTime) ? "Possible" : "Missied"
                                    ));
                            }
                        }
                    }

                }
            } catch (Exception ex)
            {
                Logger.Write("Error loading data", LogLevel.Error, ConsoleColor.DarkRed);
            }
        }

        private static List<RarePokemonInfo> FetchFromSkiplagged(double lat, double lng)
        {
            List<RarePokemonInfo> results = new List<RarePokemonInfo>();

            string url = $"https://skiplagged.com/api/pokemon.php?bounds=";

            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Accept.TryParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, sdch, br");
            client.DefaultRequestHeaders.Host = "skiplagged.com";
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/52.0.2743.116 Safari/537.36");

            client.GetStringAsync(url).ContinueWith((t) =>
            {
                var response = t.Result;
                results = GetJsonList(response);
               
            }).Wait();
            return results;
        }
        private static List<RarePokemonInfo> GetJsonList(string reader)
        {
            var wrapper = JsonConvert.DeserializeObject<SkippedLaggedWrap>(reader);
            var list = new List<RarePokemonInfo>();
            foreach (var result in wrapper.pokemons)
            {
                var sniperInfo = Map(result);
                if (sniperInfo != null)
                {
                    list.Add(sniperInfo);
                }
            }
            return list;
        }

        private static RarePokemonInfo Map(pokemon result)
        {
            throw new NotImplementedException();
        }
    }

    public class SkippedLaggedWrap
    {
        public double duration { get; set; }
        public List<pokemon> pokemons { get; set; }
        public SkippedLaggedWrap()
        {
            pokemons = new List<pokemon>();
        }
    }
    public class pokemon
    {
        public DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        public DateTime expires_date
        {
            get
            {
                return UnixTimeStampToDateTime(expires);
            }
        }

        public double expires { get; set; }
        public double latitude { get; set; }
        public double longitude { get; set; }
        public int pokemon_id { get; set; }
        public string pokemon_name { get; set; }
    }

}
