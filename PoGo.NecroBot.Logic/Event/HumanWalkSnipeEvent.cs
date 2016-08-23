#region using directives

using System.Collections.Generic;
using PoGo.NecroBot.Logic.Model;
using POGOProtos.Enums;
using Location = PoGo.NecroBot.Logic.Model.Settings.Location;

#endregion

namespace PoGo.NecroBot.Logic.Event
{
    public enum HumanWalkSnipeEventTypes
    {
        StartWalking,
        DestinationReached,
        PokemonScanned,
        AddedSnipePokemon
    }
    public class HumanWalkSnipeEvent : IEvent
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public double Distance { get; set; }

        public double WalkTimes { get; set; }

        public PokemonId PokemonId { get; set; }
        public HumanWalkSnipeEventTypes Type { get; set; }
        public double Expires { get; internal set; }
        public int Estimate { get; internal set; }
        public List<string> RarePokemons { get; internal set; }
    }
}