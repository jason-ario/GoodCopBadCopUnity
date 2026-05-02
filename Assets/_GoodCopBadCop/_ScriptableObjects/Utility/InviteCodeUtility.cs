using System;
using System.Text;

public static class InviteCodeUtility
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    // Removed confusing chars: 0 O 1 I

    /// <summary>Encodes the full 64-bit Steam lobby ID into a short alphanumeric code.</summary>
    public static string EncodeLobbyId(ulong lobbyId)
    {
        return InviteCodeUtility.Encode(lobbyId);
    }

    /// <summary>Decodes an alphanumeric invite code back into the original Steam lobby ID.</summary>
    public static ulong DecodeLobbyId(string code)
    {
        return InviteCodeUtility.Decode(code);
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