using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

var dbPath = @"c:\hmp\MobileGnollHackLogger\app.db"; // check if this is the right path
var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
optionsBuilder.UseSqlite($"Data Source={dbPath}");
var db = new ApplicationDbContext(optionsBuilder.Options);

var sessions = db.ChatSession.OrderByDescending(s => s.Id).Take(5).ToList();
Console.WriteLine("Recent Sessions:");
foreach (var s in sessions)
{
    Console.WriteLine($"Session {s.Id}: {s.Title} - {s.AspNetUserId}");
    var msgs = db.ChatMessage.Where(m => m.ChatSessionId == s.Id).OrderBy(m => m.TimestampUtc).ToList();
    foreach (var m in msgs)
    {
        Console.WriteLine($"  [{m.Role}] {m.Content?.Substring(0, Math.Min(m.Content.Length, 50))}...");
    }
}
