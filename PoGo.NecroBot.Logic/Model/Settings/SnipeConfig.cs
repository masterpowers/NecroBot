namespace PoGo.NecroBot.Logic.Model.Settings
{
    public class SnipeConfig
    {
        public bool UseSnipeLocationServer;
        public string SnipeLocationServer = "localhost";
        public int SnipeLocationServerPort = 16969;
        public bool GetSniperInfoFromPokezz;
        public bool GetOnlyVerifiedSniperInfoFromPokezz = true;
        public bool GetSniperInfoFromPokeSnipers = true;
        public bool GetSniperInfoFromPokeWatchers = true;
        public bool GetSniperInfoFromSkiplagged = true;
        public int MinPokeballsToSnipe = 20;
        public int MinPokeballsWhileSnipe = 0;
        public int MinDelayBetweenSnipes = 60000;
        public double SnipingScanOffset = 0.005;
        public bool SnipeAtPokestops;
        public bool SnipeIgnoreUnknownIv;
        public bool UseTransferIvForSnipe;
        public bool SnipePokemonNotInPokedex;
        public bool HumanWalkingSnipeDisplayList = true;
        public double HumanWalkingSnipeMaxDistance = 1000.0;
        public double HumanWalkingSnipeMaxEstimateTime = 300.0;
        public int HumanWalkingSnipeCatchEmAllMinBalls = 100;
        public bool HumanWalkingSnipeTryCatchEmAll = true;
        public bool HumanWalkingSnipeCatchPokemonWhileWalking = true;
        public bool HumanWalkingSnipeSpinWhileWalking = true;
        public bool HumanWalkingSnipeAlwaysWalkBack = false;
        public double HumanWalkingSnipeSnipingScanOffset = 0.015;
    }

}