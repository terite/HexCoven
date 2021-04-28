namespace HexCoven
{
    public enum MessageType : byte
    {
        None = 0,
        Disconnect = 1,
        Ping = 2, Pong = 3,
        ProposeTeamChange = 4, ApproveTeamChange = 5, DenyTeamChange = 6,
        Ready = 7, Unready = 8, StartMatch = 9,
        Surrender = 10,
        BoardState = 11,
        Promotion = 12,
        PreviewMovesOn = 13, PreviewMovesOff = 14,
        OfferDraw = 15, AcceptDraw = 16, DenyDraw = 17,
        UpdateName = 18,
        FlagFall = 19,
        Connect = 20,
        Checkmate = 21,
        Stalemate = 22,
    }
}
