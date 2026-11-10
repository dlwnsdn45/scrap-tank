using System;

public static class ScrapCodeChecks
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }
    public static void Main()
    {
        var korean = new TitleScrapCode("바보");
        Check(!korean.Feed('바') && korean.Feed('보'), "Committed Hangul triggers after both syllables");
        korean.Clear();
        Check(!korean.Feed('바') && korean.MatchesComposition("보"), "Committed first syllable plus IME preedit");
        korean.Clear();
        Check(korean.MatchesComposition("바보"), "Whole word in one IME composition");
        korean.Clear();
        Check(!korean.MatchesComposition("바ㅂ") && !korean.MatchesComposition("바"), "Incomplete composition does not trigger");
        korean.Feed('바'); korean.Feed('\b');
        Check(!korean.Feed('보'), "Backspace removes previous syllable");
        korean.Clear(); korean.Feed('바'); korean.Clear();
        Check(!korean.Feed('보'), "Clearing on timeout or focus change breaks partial input");
        korean.Clear(); korean.Feed('가'); korean.Feed('바');
        Check(korean.Feed('보'), "Unrelated prefix does not prevent secret suffix");
        var keys = new TitleScrapCode("qkqh");
        Check(!keys.Feed('q') && !keys.Feed('k') && !keys.Feed('q') && keys.Feed('h'), "Two-set physical keyboard sequence");
        keys.Clear(); keys.Feed('q'); keys.Feed('k'); keys.Feed('x'); keys.Feed('q');
        Check(!keys.Feed('h'), "Incorrect key interrupts the secret sequence");
        korean.Clear(); korean.Feed('바'); korean.Feed('\n');
        Check(!korean.Feed('보'), "Control characters reset the buffer");
        Console.WriteLine(checks + " checks passed");
    }
}
