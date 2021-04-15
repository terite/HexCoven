using System.Text;
using System.Text.Json;

namespace HexCoven
{
    public struct GameParams
    {
        public ChessTeam localTeam { get; set; }
        public bool showMovePreviews { get; set; }
        public float timerDuration { get; set; }
        public bool showClock { get; set; }


        public GameParams(ChessTeam localTeam, bool showMovePreviews, float timerDuration = 0, bool showClock = false)
        {
            this.localTeam = localTeam;
            this.showMovePreviews = showMovePreviews;
            this.timerDuration = timerDuration;
            this.showClock = timerDuration > 0 ? false : showClock;
        }

        public byte[] Serialize()
        {
            return Encoding.ASCII.GetBytes(JsonSerializer.Serialize(this));
        }

        public static GameParams Deserialize(byte[] data)
        {
            return JsonSerializer.Deserialize<GameParams>(Encoding.ASCII.GetString(data));
        }
    }
}
