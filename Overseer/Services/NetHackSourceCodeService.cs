using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Overseer.Services;

public class NetHackSourceCodeService : SourceCodeService
{
    public NetHackSourceCodeService(IConfiguration configuration, ILogger<NetHackSourceCodeService> logger)
        : base(configuration, logger, "NetHackSourceCodePath")
    {
    }

    protected override string[] TargetDirectories => new[] { "src", "include", "dat" };

    protected override void RegenerateHeaders(bool force = false)
    {
        // NetHack has no server-side makedefs build pipeline; headers are already present in include/
    }

    protected override void ParseGameData()
    {
        // NetHack 5.0 moved game data to include/monsters.h and include/objects.h.
        // Structured parser is GnollHack-only; raw code search tools are used for NetHack.
    }

    protected override void LoadFlagDescriptions()
    {
        // NetHack does not use GnollHack-specific flag descriptions
    }
}
