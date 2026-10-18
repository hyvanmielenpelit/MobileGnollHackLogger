using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Overseer.Services.SourceIndexing;

namespace Overseer.Services;

public class NetHackSourceCodeService : SourceCodeService
{
    public NetHackSourceCodeService(IConfiguration configuration, ILogger<NetHackSourceCodeService> logger, IExternalProcessRunner? processRunner = null)
        : base(configuration, logger, "NetHackSourceCodePath", processRunner)
    {
    }

    protected override string[] TargetDirectories => new[] { "src", "include", "dat" };

    // NetHack has no server-side makedefs build pipeline; headers are already present in include/
    protected override bool SupportsMakedefs => false;

    private protected override void ParseGameData(IReadOnlyDictionary<string, SourceDocument> documents, GameDataParser parser, ObjectsMacroResolver itemResolver)
    {
        // NetHack 5.0 moved game data to include/monsters.h and include/objects.h.
        // Structured parser is GnollHack-only; raw code search tools are used for NetHack.
    }

    protected override void LoadFlagDescriptions()
    {
        // NetHack does not use GnollHack-specific flag descriptions
    }
}
