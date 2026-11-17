using System;
class Checks {
 static bool Match(string text) { var code=new TitleScrapCode("UUDDLRLRBA"); bool hit=false; foreach(char c in text) hit |= code.Feed(c); return hit; }
 static void Main() {
  if(!Match("UUDDLRLRBA")) throw new Exception("exact");
  if(Match("UUDDLRLRB")) throw new Exception("incomplete");
  if(Match("UUDDLRR LBA")) throw new Exception("wrong input");
  if(!Match("UUUDDLRLRBA")) throw new Exception("overlapping prefix");
  var code=new TitleScrapCode("UUDDLRLRBA"); foreach(char c in "UUDDLR") code.Feed(c); code.Clear(); foreach(char c in "LRBA") if(code.Feed(c)) throw new Exception("reset");
  Console.WriteLine("PASS: exact, incomplete, incorrect, overlapping prefix, reset (5 checks)");
 }
}