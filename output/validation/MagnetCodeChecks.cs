using System;

class MagnetCodeChecks
{
    static int passed;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS: " + name); }
    static bool Match(string code, string input)
    {
        var matcher = new TitleScrapCode(code);
        bool matched = false;
        foreach (char c in input) matched |= matcher.Feed(c);
        return matched;
    }
    static void Main()
    {
        Check(Match("자석", "자석"), "Complete Korean word");
        var korean = new TitleScrapCode("자석"); korean.Feed('자');
        Check(korean.MatchesComposition("석"), "Committed syllable plus IME composition");
        korean.Clear();
        Check(korean.MatchesComposition("자석"), "Whole IME composition");
        Check(!korean.MatchesComposition("자서"), "Incomplete final consonant");
        Check(Match("wktjr", "wktjr"), "Two-set keyboard sequence");
        Check(Match("wktjr", "wwktjr"), "Overlapping prefix recovers");
        Check(!Match("wktjr", "wkxjr"), "Wrong input rejected");
        korean.Feed('자'); korean.Clear();
        Check(!korean.Feed('석'), "Focus or timeout reset breaks partial word");
        korean.Clear(); korean.Feed('자'); korean.Feed('\b');
        Check(!korean.Feed('석'), "Backspace removes first syllable");
        Console.WriteLine(passed + " checks passed");
    }
}
