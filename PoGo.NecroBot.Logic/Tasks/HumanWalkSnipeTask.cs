using GeoCoordinatePortable;
using Newtonsoft.Json;
using PoGo.NecroBot.Logic.Common;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.Logging;
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
        private static int pokestopCount = 0;
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

            cancellationToken.ThrowIfCancellationRequested();

            if (!session.LogicSettings.CatchPokemon) return;

            if (session.LogicSettings.HumanWalkingSnipeTryCatchEmAll)
            {
                var checkBall = await CheckPokeballsToSnipe(session.LogicSettings.HumanWalkingSnipeCatchEmAllMinBalls, session, cancellationToken);
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

                           if (session.LogicSettings.HumanWalkingSnipeCatchPokemonWhileWalking
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
                           if (actionWhenWalking != null)
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
                keepWalking = _session.LogicSettings.HumanWalkingSnipeTryCatchEmAll && pokemons.Count > 0;
            }

            if (caughtAnyPokemonInThisWalk)
            {
                await afterCatchFunc?.Invoke();
            }
        }
        static void CalculateDistanceAndEstTime(RarePokemonInfo p)
        {
            var speedInMetersPerSecond = _session.LogicSettings.WalkingSpeedInKilometerPerHour / 3.6;

            p.distance = LocationUtils.CalculateDistanceInMeters(_session.Client.CurrentLatitude, _session.Client.CurrentLongitude, p.latitude, p.longitude);
            p.estimateTime = p.distance / speedInMetersPerSecond + 30; //margin 30 second
        }
        private static List<RarePokemonInfo> GetRarePokemons(double lat, double lng, bool refreshData = true)
        {
            if (refreshData)
            {
                ReloadData(lat, lng);
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
                    && p.distance < _session.LogicSettings.HumanWalkingSnipeMaxDistance &&
                    p.estimateTime < _session.LogicSettings.HumanWalkingSnipeMaxEstimateTime
                )
                .OrderBy(p => p.distance);
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
            double offset = _session.LogicSettings.SnipingScanOffset; //0.015 
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
                //the pokemon data already in the list
                if (rarePokemons.Any(x => x.id == p.id)) return;
                //check if pokemon in the snip list
                if (!_session.LogicSettings.PokemonToSnipe.Pokemon.Any(x => x == p.Id)) return;

                count++;
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
               
            }
        }
    }
}
