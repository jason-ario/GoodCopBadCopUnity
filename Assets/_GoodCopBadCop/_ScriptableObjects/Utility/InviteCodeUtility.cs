using System;
using System.Text;

public static class InviteCodeUtility
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    // Removed confusing chars: 0 O 1 I

    public static string EncodeLobbyId(ulong lobbyId)
    {
        uint shortId = (uint)(lobbyId & 0xFFFFFFFF);
        return InviteCodeUtility.Encode(shortId);
    }
    
    public static ulong DecodeLobbyId(string code)
    {
        uint shortId = (uint)InviteCodeUtility.Decode(code);

        // Steam lobby CSteamID prefix:
        // Universe = 1 (Public, bits 56-63), AccountType = 8 (Chat, bits 52-55),
        // Instance = 0x40000 (lobby flag, bits 32-51)
        const ulong SteamLobbyPrefix = 0x0180004000000000UL;

        return SteamLobbyPrefix | shortId;
    }
    
    public static string Encode(ulong value)
    {
        if (value == 0) return "A";

        var sb = new StringBuilder();
        while (value > 0)
        {
            sb.Insert(0, Alphabet[(int)(value % (ulong)Alphabet.Length)]);
            value /= (ulong)Alphabet.Length;
        }
        return sb.ToString();
    }

    public static ulong Decode(string code)
    {
        ulong result = 0;
        foreach (char c in code.ToUpper())
        {
            int index = Alphabet.IndexOf(c);
            if (index < 0)
                throw new Exception("Invalid invite code character");

            result = result * (ulong)Alphabet.Length + (ulong)index;
        }
        return result;
    }
}