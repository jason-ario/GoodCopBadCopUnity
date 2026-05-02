using System;
using System.Text;

public static class InviteCodeUtility
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    // Removed confusing chars: 0 O 1 I

    /// <summary>Generates a random short alphanumeric lobby code of the given length.</summary>
    public static string GenerateCode(int length = 6)
    {
        var rng = new Random();
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
            sb.Append(Alphabet[rng.Next(Alphabet.Length)]);
        return sb.ToString();
    }
}