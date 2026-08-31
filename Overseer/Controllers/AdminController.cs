using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Extensions;
using Overseer.Models;
using Overseer.Services;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CryptoService _cryptoService;
    private readonly Overseer.Services.Providers.AiRequestGovernor _governor;

    public AdminController(ApplicationDbContext dbContext, IConfiguration configuration, UserManager<ApplicationUser> userManager, CryptoService cryptoService, Overseer.Services.Providers.AiRequestGovernor governor)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _userManager = userManager;
        _cryptoService = cryptoService;
        _governor = governor;
    }

    private static readonly System.Collections.Generic.HashSet<string> AllowedCounters = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "DailyChatRequestsCount", "MonthlyChatRequestsCount", "TotalChatRequestsCount",
        "DailyTitleRequestsCount", "MonthlyTitleRequestsCount", "TotalTitleRequestsCount",
        "DailyChatTokensCount", "MonthlyChatTokensCount", "TotalChatTokensCount",
        "DailyTitleTokensCount", "MonthlyTitleTokensCount", "TotalTitleTokensCount"
    };

    // --- Groups ---

    [HttpGet("groups")]
    public async Task<IActionResult> GetGroups()
    {
        var groups = await _dbContext.Groups
            .Select(g => new AdminGroupDto
            {
                Id = g.Id,
                DisplayName = g.DisplayName,
                CreatedAtUtc = g.CreatedAtUtc,
                UserCount = _dbContext.UserGroups.Count(ug => ug.GroupId == g.Id)
            })
            .ToListAsync();

        return Ok(groups);
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
    {
        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return BadRequest("Group name cannot be empty.");
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(displayName, @"^[a-zA-Z0-9 _\-]+$"))
        {
            return BadRequest("Group name contains invalid characters.");
        }

        var group = new Group
        {
            DisplayName = displayName,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Groups.Add(group);
        await _dbContext.SaveChangesAsync();

        return Ok(new AdminGroupDto
        {
            Id = group.Id,
            DisplayName = group.DisplayName,
            CreatedAtUtc = group.CreatedAtUtc,
            UserCount = 0
        });
    }

    [HttpDelete("groups/{id}")]
    public async Task<IActionResult> DeleteGroup(long id)
    {
        var group = await _dbContext.Groups.FindAsync(id);
        if (group == null) return NotFound();

        _dbContext.Groups.Remove(group);
        await _dbContext.SaveChangesAsync();

        return Ok();
    }

    // --- Users ---

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? usernameFilter = null, [FromQuery] string sortColumn = "UserName", [FromQuery] string sortOrder = "asc")
    {
        var query = _userManager.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(usernameFilter))
        {
            query = query.Where(u => (u.UserName != null && u.UserName.Contains(usernameFilter)) || (u.Email != null && u.Email.Contains(usernameFilter)));
        }

        var totalCount = await query.CountAsync();
        
        bool isDesc = sortOrder.ToLower() == "desc";
        if (sortColumn.Equals("Email", StringComparison.OrdinalIgnoreCase))
        {
            query = isDesc ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email);
        }
        else // default to UserName
        {
            query = isDesc ? query.OrderByDescending(u => u.UserName) : query.OrderBy(u => u.UserName);
        }

        var users = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var dtos = new List<AdminUserDto>();

        foreach(var u in users)
        {
            var userGroups = await _dbContext.UserGroups
                .Include(ug => ug.Group)
                .Where(ug => ug.AspNetUserId == u.Id)
                .Select(ug => new AdminGroupDto { Id = ug.GroupId, DisplayName = ug.Group.DisplayName })
                .ToListAsync();

            dtos.Add(new AdminUserDto
            {
                Id = u.Id,
                UserName = u.UserName ?? "",
                Email = u.Email ?? "",
                Groups = userGroups
            });
        }

        return Ok(new { TotalCount = totalCount, Rows = dtos });
    }

    [HttpPost("users/{userId}/groups")]
    public async Task<IActionResult> AssignGroupToUser(string userId, [FromBody] AssignGroupRequest request)
    {
        var exists = await _dbContext.UserGroups.AnyAsync(ug => ug.AspNetUserId == userId && ug.GroupId == request.GroupId);
        if (!exists)
        {
            _dbContext.UserGroups.Add(new UserGroup { AspNetUserId = userId, GroupId = request.GroupId });
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }

    [HttpDelete("users/{userId}/groups/{groupId}")]
    public async Task<IActionResult> RemoveGroupFromUser(string userId, long groupId)
    {
        var ug = await _dbContext.UserGroups.FirstOrDefaultAsync(u => u.AspNetUserId == userId && u.GroupId == groupId);
        if (ug != null)
        {
            _dbContext.UserGroups.Remove(ug);
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }

    // --- System AI Configurations ---

    [HttpGet("systemconfigs")]
    public async Task<IActionResult> GetSystemConfigs()
    {
        var configs = await _dbContext.SystemAiApiConfigurations
            .OrderBy(c => c.OrderIndex)
            .Select(c => new SystemAiApiConfigurationDto
            {
                Id = c.Id,
                DisplayName = c.DisplayName,
                Provider = c.Provider,
                ModelId = c.ModelId,
                ThinkingLevel = c.ThinkingLevel,
                ReasoningMode = c.ReasoningMode,
                ReasoningSummary = c.ReasoningSummary,
                ServiceTier = c.ServiceTier,
                MaxInputTokens = c.MaxInputTokens,
                MaxOutputTokens = c.MaxOutputTokens,
                OrderIndex = c.OrderIndex,
                IsEnabled = c.IsEnabled,
                HasApiKey = !string.IsNullOrEmpty(c.EncryptedApiKey),
                IsSystemWide = c.IsSystemWide,
                MaxDailyChatRequests = c.MaxDailyChatRequests,
                MaxMonthlyChatRequests = c.MaxMonthlyChatRequests,
                MaxTotalChatRequests = c.MaxTotalChatRequests,
                MaxDailyTitleRequests = c.MaxDailyTitleRequests,
                MaxMonthlyTitleRequests = c.MaxMonthlyTitleRequests,
                MaxTotalTitleRequests = c.MaxTotalTitleRequests,
                MaxDailyChatTokens = c.MaxDailyChatTokens,
                MaxMonthlyChatTokens = c.MaxMonthlyChatTokens,
                MaxTotalChatTokens = c.MaxTotalChatTokens,
                MaxDailyTitleTokens = c.MaxDailyTitleTokens,
                MaxMonthlyTitleTokens = c.MaxMonthlyTitleTokens,
                MaxTotalTitleTokens = c.MaxTotalTitleTokens,
                DailyChatRequestsCount = c.DailyChatRequestsCount,
                MonthlyChatRequestsCount = c.MonthlyChatRequestsCount,
                TotalChatRequestsCount = c.TotalChatRequestsCount,
                DailyTitleRequestsCount = c.DailyTitleRequestsCount,
                MonthlyTitleRequestsCount = c.MonthlyTitleRequestsCount,
                TotalTitleRequestsCount = c.TotalTitleRequestsCount,
                DailyChatTokensCount = c.DailyChatTokensCount,
                MonthlyChatTokensCount = c.MonthlyChatTokensCount,
                TotalChatTokensCount = c.TotalChatTokensCount,
                DailyTitleTokensCount = c.DailyTitleTokensCount,
                MonthlyTitleTokensCount = c.MonthlyTitleTokensCount,
                TotalTitleTokensCount = c.TotalTitleTokensCount,
                ModelRole = c.ModelRole,
                ParallelExecutionMode = (int)c.ParallelExecutionMode,
                Note = c.Note
            })
            .ToListAsync();

        return Ok(configs);
    }

    [HttpPost("systemconfigs")]
    public async Task<IActionResult> CreateSystemConfig([FromBody] CreateSystemAiApiConfigurationRequest request)
    {
        var orderIndex = await _dbContext.SystemAiApiConfigurations.AnyAsync() 
            ? await _dbContext.SystemAiApiConfigurations.MaxAsync(c => c.OrderIndex) + 1 
            : 0;

        var config = new SystemAiApiConfiguration
        {
            DisplayName = request.DisplayName,
            Provider = request.Provider,
            ModelId = request.ModelId,
            ThinkingLevel = request.ThinkingLevel,
            ReasoningMode = request.ReasoningMode,
            ReasoningSummary = request.ReasoningSummary,
            ServiceTier = request.ServiceTier,
            MaxInputTokens = request.MaxInputTokens,
            MaxOutputTokens = request.MaxOutputTokens,
            IsEnabled = request.IsEnabled,
            IsSystemWide = request.IsSystemWide,
            MaxDailyChatRequests = request.MaxDailyChatRequests,
            MaxMonthlyChatRequests = request.MaxMonthlyChatRequests,
            MaxTotalChatRequests = request.MaxTotalChatRequests,
            MaxDailyTitleRequests = request.MaxDailyTitleRequests,
            MaxMonthlyTitleRequests = request.MaxMonthlyTitleRequests,
            MaxTotalTitleRequests = request.MaxTotalTitleRequests,
            MaxDailyChatTokens = request.MaxDailyChatTokens,
            MaxMonthlyChatTokens = request.MaxMonthlyChatTokens,
            MaxTotalChatTokens = request.MaxTotalChatTokens,
            MaxDailyTitleTokens = request.MaxDailyTitleTokens,
            MaxMonthlyTitleTokens = request.MaxMonthlyTitleTokens,
            MaxTotalTitleTokens = request.MaxTotalTitleTokens,
            ModelRole = request.ModelRole,
            ParallelExecutionMode = (MobileGnollHackLogger.Data.ParallelExecutionMode)request.ParallelExecutionMode,
            OrderIndex = orderIndex,
            Note = request.Note
        };

        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            EncryptApiKey(config, request.ApiKey);
        }

        _dbContext.SystemAiApiConfigurations.Add(config);
        await _dbContext.SaveChangesAsync();

        return Ok(new { id = config.Id });
    }

    [HttpPut("systemconfigs/{id}")]
    public async Task<IActionResult> UpdateSystemConfig(long id, [FromBody] UpdateSystemAiApiConfigurationRequest request)
    {
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(id);
        if (config == null) return NotFound();

        config.DisplayName = request.DisplayName;
        config.Provider = request.Provider;
        config.ModelId = request.ModelId;
        config.ThinkingLevel = request.ThinkingLevel;
        config.ReasoningMode = request.ReasoningMode;
        config.ReasoningSummary = request.ReasoningSummary;
        config.ServiceTier = request.ServiceTier;
        config.MaxInputTokens = request.MaxInputTokens;
        config.MaxOutputTokens = request.MaxOutputTokens;
        config.IsEnabled = request.IsEnabled;
        config.IsSystemWide = request.IsSystemWide;
        config.MaxDailyChatRequests = request.MaxDailyChatRequests;
        config.MaxMonthlyChatRequests = request.MaxMonthlyChatRequests;
        config.MaxTotalChatRequests = request.MaxTotalChatRequests;
        config.MaxDailyTitleRequests = request.MaxDailyTitleRequests;
        config.MaxMonthlyTitleRequests = request.MaxMonthlyTitleRequests;
        config.MaxTotalTitleRequests = request.MaxTotalTitleRequests;
        config.MaxDailyChatTokens = request.MaxDailyChatTokens;
        config.MaxMonthlyChatTokens = request.MaxMonthlyChatTokens;
        config.MaxTotalChatTokens = request.MaxTotalChatTokens;
        config.MaxDailyTitleTokens = request.MaxDailyTitleTokens;
        config.MaxMonthlyTitleTokens = request.MaxMonthlyTitleTokens;
        config.MaxTotalTitleTokens = request.MaxTotalTitleTokens;
        config.ModelRole = request.ModelRole;
        config.ParallelExecutionMode = (MobileGnollHackLogger.Data.ParallelExecutionMode)request.ParallelExecutionMode;
        config.Note = request.Note;

        if (request.ApiKey != null)
        {
            if (string.IsNullOrWhiteSpace(request.ApiKey))
            {
                config.EncryptedApiKey = null;
                config.ApiKeyNonce = null;
                config.ApiKeyTag = null;
            }
            else
            {
                EncryptApiKey(config, request.ApiKey);
            }
        }

        await _dbContext.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("systemconfigs/{id}/reset")]
    public async Task<IActionResult> ResetSystemConfigRateLimits(long id, [FromBody] ResetCounterRequest? request = null)
    {
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(id);
        if (config == null) return NotFound();

        if (request?.CounterName != null)
        {
            if (!AllowedCounters.Contains(request.CounterName))
            {
                return BadRequest("Invalid counter name.");
            }
            var prop = config.GetType().GetProperty(request.CounterName);
            if (prop != null && prop.CanWrite)
            {
                if (prop.PropertyType == typeof(int)) prop.SetValue(config, 0);
                else if (prop.PropertyType == typeof(long)) prop.SetValue(config, 0L);
            }
        }
        else
        {
            config.DailyChatRequestsCount = 0;
            config.MonthlyChatRequestsCount = 0;
            config.TotalChatRequestsCount = 0;
            config.DailyTitleRequestsCount = 0;
            config.MonthlyTitleRequestsCount = 0;
            config.TotalTitleRequestsCount = 0;
            config.DailyChatTokensCount = 0;
            config.MonthlyChatTokensCount = 0;
            config.TotalChatTokensCount = 0;
            config.DailyTitleTokensCount = 0;
            config.MonthlyTitleTokensCount = 0;
            config.TotalTitleTokensCount = 0;
        }
        
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("systemconfigs/{id}")]
    public async Task<IActionResult> DeleteSystemConfig(long id)
    {
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(id);
        if (config == null) return NotFound();

        _dbContext.SystemAiApiConfigurations.Remove(config);
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPut("systemconfigs/reorder")]
    public async Task<IActionResult> ReorderSystemConfigs([FromBody] ReorderRequest request)
    {
        var configs = await _dbContext.SystemAiApiConfigurations.ToListAsync();
        var idDict = configs.ToDictionary(c => c.Id);

        for (int i = 0; i < request.OrderedIds.Length; i++)
        {
            if (idDict.TryGetValue(request.OrderedIds[i], out var config))
            {
                config.OrderIndex = i;
            }
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    // --- User System Config Assignments ---

    [HttpGet("users/{userId}/systemconfigs")]
    public async Task<IActionResult> GetUserSystemConfigs(string userId)
    {
        var assignments = await _dbContext.UserSystemAiApiConfigurations
            .Where(a => a.AspNetUserId == userId)
            .OrderBy(a => a.OrderIndex)
            .Select(a => new UserSystemAiConfigDto
            {
                Id = a.Id,
                UserId = a.AspNetUserId,
                SystemAiApiConfigurationId = a.SystemAiApiConfigurationId,
                IsEnabled = a.IsEnabled,
                OrderIndex = a.OrderIndex,
                MaxDailyChatRequests = a.MaxDailyChatRequests,
                MaxMonthlyChatRequests = a.MaxMonthlyChatRequests,
                MaxTotalChatRequests = a.MaxTotalChatRequests,
                MaxDailyTitleRequests = a.MaxDailyTitleRequests,
                MaxMonthlyTitleRequests = a.MaxMonthlyTitleRequests,
                MaxTotalTitleRequests = a.MaxTotalTitleRequests,
                MaxDailyChatTokens = a.MaxDailyChatTokens,
                MaxMonthlyChatTokens = a.MaxMonthlyChatTokens,
                MaxTotalChatTokens = a.MaxTotalChatTokens,
                MaxDailyTitleTokens = a.MaxDailyTitleTokens,
                MaxMonthlyTitleTokens = a.MaxMonthlyTitleTokens,
                MaxTotalTitleTokens = a.MaxTotalTitleTokens,
                DailyChatRequestsCount = a.DailyChatRequestsCount,
                MonthlyChatRequestsCount = a.MonthlyChatRequestsCount,
                TotalChatRequestsCount = a.TotalChatRequestsCount,
                DailyTitleRequestsCount = a.DailyTitleRequestsCount,
                MonthlyTitleRequestsCount = a.MonthlyTitleRequestsCount,
                TotalTitleRequestsCount = a.TotalTitleRequestsCount,
                DailyChatTokensCount = a.DailyChatTokensCount,
                MonthlyChatTokensCount = a.MonthlyChatTokensCount,
                TotalChatTokensCount = a.TotalChatTokensCount,
                DailyTitleTokensCount = a.DailyTitleTokensCount,
                MonthlyTitleTokensCount = a.MonthlyTitleTokensCount,
                TotalTitleTokensCount = a.TotalTitleTokensCount,
                ModelRole = a.ModelRole
            })
            .ToListAsync();

        return Ok(assignments);
    }

    [HttpPost("users/{userId}/systemconfigs")]
    public async Task<IActionResult> AssignSystemConfigToUser(string userId, [FromBody] AssignConfigToUserRequest request)
    {
        if (await _dbContext.UserSystemAiApiConfigurations.AnyAsync(a => a.AspNetUserId == userId && a.SystemAiApiConfigurationId == request.SystemAiApiConfigurationId))
        {
            return BadRequest("This configuration is already assigned to the user.");
        }

        var assignment = new UserSystemAiApiConfiguration
        {
            AspNetUserId = userId,
            SystemAiApiConfigurationId = request.SystemAiApiConfigurationId,
            IsEnabled = request.IsEnabled,
            MaxDailyChatRequests = request.MaxDailyChatRequests,
            MaxMonthlyChatRequests = request.MaxMonthlyChatRequests,
            MaxTotalChatRequests = request.MaxTotalChatRequests,
            MaxDailyTitleRequests = request.MaxDailyTitleRequests,
            MaxMonthlyTitleRequests = request.MaxMonthlyTitleRequests,
            MaxTotalTitleRequests = request.MaxTotalTitleRequests,
            MaxDailyChatTokens = request.MaxDailyChatTokens,
            MaxMonthlyChatTokens = request.MaxMonthlyChatTokens,
            MaxTotalChatTokens = request.MaxTotalChatTokens,
            MaxDailyTitleTokens = request.MaxDailyTitleTokens,
            MaxMonthlyTitleTokens = request.MaxMonthlyTitleTokens,
            MaxTotalTitleTokens = request.MaxTotalTitleTokens,
            ModelRole = request.ModelRole,
            OrderIndex = null
        };

        _dbContext.UserSystemAiApiConfigurations.Add(assignment);
        await _dbContext.SaveChangesAsync();

        return Ok(new UserSystemAiConfigDto
        {
            Id = assignment.Id,
            UserId = assignment.AspNetUserId,
            SystemAiApiConfigurationId = assignment.SystemAiApiConfigurationId,
            IsEnabled = assignment.IsEnabled,
            OrderIndex = assignment.OrderIndex,
            MaxDailyChatRequests = assignment.MaxDailyChatRequests,
            MaxMonthlyChatRequests = assignment.MaxMonthlyChatRequests,
            MaxTotalChatRequests = assignment.MaxTotalChatRequests,
            MaxDailyTitleRequests = assignment.MaxDailyTitleRequests,
            MaxMonthlyTitleRequests = assignment.MaxMonthlyTitleRequests,
            MaxTotalTitleRequests = assignment.MaxTotalTitleRequests,
            MaxDailyChatTokens = assignment.MaxDailyChatTokens,
            MaxMonthlyChatTokens = assignment.MaxMonthlyChatTokens,
            MaxTotalChatTokens = assignment.MaxTotalChatTokens,
            MaxDailyTitleTokens = assignment.MaxDailyTitleTokens,
            MaxMonthlyTitleTokens = assignment.MaxMonthlyTitleTokens,
            MaxTotalTitleTokens = assignment.MaxTotalTitleTokens,
            DailyChatRequestsCount = assignment.DailyChatRequestsCount,
            MonthlyChatRequestsCount = assignment.MonthlyChatRequestsCount,
            TotalChatRequestsCount = assignment.TotalChatRequestsCount,
            DailyTitleRequestsCount = assignment.DailyTitleRequestsCount,
            MonthlyTitleRequestsCount = assignment.MonthlyTitleRequestsCount,
            TotalTitleRequestsCount = assignment.TotalTitleRequestsCount,
            DailyChatTokensCount = assignment.DailyChatTokensCount,
            MonthlyChatTokensCount = assignment.MonthlyChatTokensCount,
            TotalChatTokensCount = assignment.TotalChatTokensCount,
            DailyTitleTokensCount = assignment.DailyTitleTokensCount,
            MonthlyTitleTokensCount = assignment.MonthlyTitleTokensCount,
            TotalTitleTokensCount = assignment.TotalTitleTokensCount,
            ModelRole = assignment.ModelRole
        });
    }
    
    [HttpDelete("user-systemconfigs/{id}")]
    public async Task<IActionResult> RemoveSystemConfigFromUser(long id)
    {
        var assignment = await _dbContext.UserSystemAiApiConfigurations.FindAsync(id);
        if (assignment != null)
        {
            _dbContext.UserSystemAiApiConfigurations.Remove(assignment);
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }
    
    [HttpPut("user-systemconfigs/{id}")]
    public async Task<IActionResult> UpdateUserSystemConfig(long id, [FromBody] UpdateUserSystemAiConfigRequest request)
    {
        var assignment = await _dbContext.UserSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        assignment.IsEnabled = request.IsEnabled;
        assignment.MaxDailyChatRequests = request.MaxDailyChatRequests;
        assignment.MaxMonthlyChatRequests = request.MaxMonthlyChatRequests;
        assignment.MaxTotalChatRequests = request.MaxTotalChatRequests;
        assignment.MaxDailyTitleRequests = request.MaxDailyTitleRequests;
        assignment.MaxMonthlyTitleRequests = request.MaxMonthlyTitleRequests;
        assignment.MaxTotalTitleRequests = request.MaxTotalTitleRequests;
        assignment.MaxDailyChatTokens = request.MaxDailyChatTokens;
        assignment.MaxMonthlyChatTokens = request.MaxMonthlyChatTokens;
        assignment.MaxTotalChatTokens = request.MaxTotalChatTokens;
        assignment.MaxDailyTitleTokens = request.MaxDailyTitleTokens;
        assignment.MaxMonthlyTitleTokens = request.MaxMonthlyTitleTokens;
        assignment.MaxTotalTitleTokens = request.MaxTotalTitleTokens;
        assignment.ModelRole = request.ModelRole;

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("user-systemconfigs/{id}/reset")]
    public async Task<IActionResult> ResetUserSystemConfigRateLimits(long id, [FromBody] ResetCounterRequest? request = null)
    {
        var assignment = await _dbContext.UserSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        if (request?.CounterName != null)
        {
            if (!AllowedCounters.Contains(request.CounterName))
            {
                return BadRequest("Invalid counter name.");
            }
            var prop = assignment.GetType().GetProperty(request.CounterName);
            if (prop != null && prop.CanWrite)
            {
                if (prop.PropertyType == typeof(int)) prop.SetValue(assignment, 0);
                else if (prop.PropertyType == typeof(long)) prop.SetValue(assignment, 0L);
            }
        }
        else
        {
            assignment.DailyChatRequestsCount = 0;
            assignment.MonthlyChatRequestsCount = 0;
            assignment.TotalChatRequestsCount = 0;
            assignment.DailyTitleRequestsCount = 0;
            assignment.MonthlyTitleRequestsCount = 0;
            assignment.TotalTitleRequestsCount = 0;
            assignment.DailyChatTokensCount = 0;
            assignment.MonthlyChatTokensCount = 0;
            assignment.TotalChatTokensCount = 0;
            assignment.DailyTitleTokensCount = 0;
            assignment.MonthlyTitleTokensCount = 0;
            assignment.TotalTitleTokensCount = 0;
        }
        
        await _dbContext.SaveChangesAsync();
        return Ok();
    }
    
    [HttpPut("users/{userId}/systemconfigs/reorder")]
    public async Task<IActionResult> ReorderUserSystemConfigs(string userId, [FromBody] ReorderRequest request)
    {
        var configs = await _dbContext.UserSystemAiApiConfigurations.Where(a => a.AspNetUserId == userId).ToListAsync();
        var idDict = configs.ToDictionary(c => c.Id);

        for (int i = 0; i < request.OrderedIds.Length; i++)
        {
            if (idDict.TryGetValue(request.OrderedIds[i], out var config))
            {
                config.OrderIndex = i;
            }
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    // --- Group System Config Assignments ---

    [HttpGet("groups/{groupId}/systemconfigs")]
    public async Task<IActionResult> GetGroupSystemConfigs(long groupId)
    {
        var assignments = await _dbContext.GroupSystemAiApiConfigurations
            .Where(a => a.GroupId == groupId)
            .OrderBy(a => a.OrderIndex)
            .Select(a => new GroupSystemAiConfigDto
            {
                Id = a.Id,
                GroupId = a.GroupId,
                SystemAiApiConfigurationId = a.SystemAiApiConfigurationId,
                IsEnabled = a.IsEnabled,
                OrderIndex = a.OrderIndex,
                MaxDailyChatRequests = a.MaxDailyChatRequests,
                MaxMonthlyChatRequests = a.MaxMonthlyChatRequests,
                MaxTotalChatRequests = a.MaxTotalChatRequests,
                MaxDailyTitleRequests = a.MaxDailyTitleRequests,
                MaxMonthlyTitleRequests = a.MaxMonthlyTitleRequests,
                MaxTotalTitleRequests = a.MaxTotalTitleRequests,
                MaxDailyChatTokens = a.MaxDailyChatTokens,
                MaxMonthlyChatTokens = a.MaxMonthlyChatTokens,
                MaxTotalChatTokens = a.MaxTotalChatTokens,
                MaxDailyTitleTokens = a.MaxDailyTitleTokens,
                MaxMonthlyTitleTokens = a.MaxMonthlyTitleTokens,
                MaxTotalTitleTokens = a.MaxTotalTitleTokens,
                DailyChatRequestsCount = a.DailyChatRequestsCount,
                MonthlyChatRequestsCount = a.MonthlyChatRequestsCount,
                TotalChatRequestsCount = a.TotalChatRequestsCount,
                DailyTitleRequestsCount = a.DailyTitleRequestsCount,
                MonthlyTitleRequestsCount = a.MonthlyTitleRequestsCount,
                TotalTitleRequestsCount = a.TotalTitleRequestsCount,
                DailyChatTokensCount = a.DailyChatTokensCount,
                MonthlyChatTokensCount = a.MonthlyChatTokensCount,
                TotalChatTokensCount = a.TotalChatTokensCount,
                DailyTitleTokensCount = a.DailyTitleTokensCount,
                MonthlyTitleTokensCount = a.MonthlyTitleTokensCount,
                TotalTitleTokensCount = a.TotalTitleTokensCount,
                ModelRole = a.ModelRole
            })
            .ToListAsync();

        return Ok(assignments);
    }

    [HttpPost("groups/{groupId}/systemconfigs")]
    public async Task<IActionResult> AssignSystemConfigToGroup(long groupId, [FromBody] AssignConfigToGroupRequest request)
    {
        if (await _dbContext.GroupSystemAiApiConfigurations.AnyAsync(a => a.GroupId == groupId && a.SystemAiApiConfigurationId == request.SystemAiApiConfigurationId))
        {
            return BadRequest("This configuration is already assigned to the group.");
        }

        var orderIndex = await _dbContext.GroupSystemAiApiConfigurations.Where(a => a.GroupId == groupId).AnyAsync() 
            ? await _dbContext.GroupSystemAiApiConfigurations.Where(a => a.GroupId == groupId).MaxAsync(c => c.OrderIndex) + 1 
            : 0;

        var assignment = new GroupSystemAiApiConfiguration
        {
            GroupId = groupId,
            SystemAiApiConfigurationId = request.SystemAiApiConfigurationId,
            IsEnabled = request.IsEnabled,
            MaxDailyChatRequests = request.MaxDailyChatRequests,
            MaxMonthlyChatRequests = request.MaxMonthlyChatRequests,
            MaxTotalChatRequests = request.MaxTotalChatRequests,
            MaxDailyTitleRequests = request.MaxDailyTitleRequests,
            MaxMonthlyTitleRequests = request.MaxMonthlyTitleRequests,
            MaxTotalTitleRequests = request.MaxTotalTitleRequests,
            MaxDailyChatTokens = request.MaxDailyChatTokens,
            MaxMonthlyChatTokens = request.MaxMonthlyChatTokens,
            MaxTotalChatTokens = request.MaxTotalChatTokens,
            MaxDailyTitleTokens = request.MaxDailyTitleTokens,
            MaxMonthlyTitleTokens = request.MaxMonthlyTitleTokens,
            MaxTotalTitleTokens = request.MaxTotalTitleTokens,
            ModelRole = request.ModelRole,
            OrderIndex = orderIndex
        };

        _dbContext.GroupSystemAiApiConfigurations.Add(assignment);
        await _dbContext.SaveChangesAsync();

        return Ok(new GroupSystemAiConfigDto
        {
            Id = assignment.Id,
            GroupId = assignment.GroupId,
            SystemAiApiConfigurationId = assignment.SystemAiApiConfigurationId,
            IsEnabled = assignment.IsEnabled,
            OrderIndex = assignment.OrderIndex,
            MaxDailyChatRequests = assignment.MaxDailyChatRequests,
            MaxMonthlyChatRequests = assignment.MaxMonthlyChatRequests,
            MaxTotalChatRequests = assignment.MaxTotalChatRequests,
            MaxDailyTitleRequests = assignment.MaxDailyTitleRequests,
            MaxMonthlyTitleRequests = assignment.MaxMonthlyTitleRequests,
            MaxTotalTitleRequests = assignment.MaxTotalTitleRequests,
            MaxDailyChatTokens = assignment.MaxDailyChatTokens,
            MaxMonthlyChatTokens = assignment.MaxMonthlyChatTokens,
            MaxTotalChatTokens = assignment.MaxTotalChatTokens,
            MaxDailyTitleTokens = assignment.MaxDailyTitleTokens,
            MaxMonthlyTitleTokens = assignment.MaxMonthlyTitleTokens,
            MaxTotalTitleTokens = assignment.MaxTotalTitleTokens,
            DailyChatRequestsCount = assignment.DailyChatRequestsCount,
            MonthlyChatRequestsCount = assignment.MonthlyChatRequestsCount,
            TotalChatRequestsCount = assignment.TotalChatRequestsCount,
            DailyTitleRequestsCount = assignment.DailyTitleRequestsCount,
            MonthlyTitleRequestsCount = assignment.MonthlyTitleRequestsCount,
            TotalTitleRequestsCount = assignment.TotalTitleRequestsCount,
            DailyChatTokensCount = assignment.DailyChatTokensCount,
            MonthlyChatTokensCount = assignment.MonthlyChatTokensCount,
            TotalChatTokensCount = assignment.TotalChatTokensCount,
            DailyTitleTokensCount = assignment.DailyTitleTokensCount,
            MonthlyTitleTokensCount = assignment.MonthlyTitleTokensCount,
            TotalTitleTokensCount = assignment.TotalTitleTokensCount,
            ModelRole = assignment.ModelRole
        });
    }

    [HttpDelete("group-systemconfigs/{id}")]
    public async Task<IActionResult> RemoveSystemConfigFromGroup(long id)
    {
        var assignment = await _dbContext.GroupSystemAiApiConfigurations.FindAsync(id);
        if (assignment != null)
        {
            _dbContext.GroupSystemAiApiConfigurations.Remove(assignment);
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }
    
    [HttpPut("group-systemconfigs/{id}")]
    public async Task<IActionResult> UpdateGroupSystemConfig(long id, [FromBody] UpdateGroupSystemAiConfigRequest request)
    {
        var assignment = await _dbContext.GroupSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        assignment.IsEnabled = request.IsEnabled;
        assignment.MaxDailyChatRequests = request.MaxDailyChatRequests;
        assignment.MaxMonthlyChatRequests = request.MaxMonthlyChatRequests;
        assignment.MaxTotalChatRequests = request.MaxTotalChatRequests;
        assignment.MaxDailyTitleRequests = request.MaxDailyTitleRequests;
        assignment.MaxMonthlyTitleRequests = request.MaxMonthlyTitleRequests;
        assignment.MaxTotalTitleRequests = request.MaxTotalTitleRequests;
        assignment.MaxDailyChatTokens = request.MaxDailyChatTokens;
        assignment.MaxMonthlyChatTokens = request.MaxMonthlyChatTokens;
        assignment.MaxTotalChatTokens = request.MaxTotalChatTokens;
        assignment.MaxDailyTitleTokens = request.MaxDailyTitleTokens;
        assignment.MaxMonthlyTitleTokens = request.MaxMonthlyTitleTokens;
        assignment.MaxTotalTitleTokens = request.MaxTotalTitleTokens;
        assignment.ModelRole = request.ModelRole;

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("group-systemconfigs/{id}/reset")]
    public async Task<IActionResult> ResetGroupSystemConfigRateLimits(long id, [FromBody] ResetCounterRequest? request = null)
    {
        var assignment = await _dbContext.GroupSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        if (request?.CounterName != null)
        {
            if (!AllowedCounters.Contains(request.CounterName))
            {
                return BadRequest("Invalid counter name.");
            }
            var prop = assignment.GetType().GetProperty(request.CounterName);
            if (prop != null && prop.CanWrite)
            {
                if (prop.PropertyType == typeof(int)) prop.SetValue(assignment, 0);
                else if (prop.PropertyType == typeof(long)) prop.SetValue(assignment, 0L);
            }
        }
        else
        {
            assignment.DailyChatRequestsCount = 0;
            assignment.MonthlyChatRequestsCount = 0;
            assignment.TotalChatRequestsCount = 0;
            assignment.DailyTitleRequestsCount = 0;
            assignment.MonthlyTitleRequestsCount = 0;
            assignment.TotalTitleRequestsCount = 0;
            assignment.DailyChatTokensCount = 0;
            assignment.MonthlyChatTokensCount = 0;
            assignment.TotalChatTokensCount = 0;
            assignment.DailyTitleTokensCount = 0;
            assignment.MonthlyTitleTokensCount = 0;
            assignment.TotalTitleTokensCount = 0;
        }
        
        await _dbContext.SaveChangesAsync();
        return Ok();
    }
    
    [HttpPut("groups/{groupId}/systemconfigs/reorder")]
    public async Task<IActionResult> ReorderGroupSystemConfigs(long groupId, [FromBody] ReorderRequest request)
    {
        var configs = await _dbContext.GroupSystemAiApiConfigurations.Where(a => a.GroupId == groupId).ToListAsync();
        var idDict = configs.ToDictionary(c => c.Id);

        for (int i = 0; i < request.OrderedIds.Length; i++)
        {
            if (idDict.TryGetValue(request.OrderedIds[i], out var config))
            {
                config.OrderIndex = i;
            }
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    // --- Errors ---

    [HttpGet("errors")]
    public async Task<IActionResult> GetErrors()
    {
        var errors = await _dbContext.SystemAiErrorLogs
            .Include(e => e.SystemAiApiConfiguration)
            .Where(e => !e.IsDismissed)
            .OrderByDescending(e => e.TimestampUtc)
            .Select(e => new SystemAiErrorLogDto
            {
                Id = e.Id,
                SystemAiApiConfigurationId = e.SystemAiApiConfigurationId,
                ConfigurationName = e.SystemAiApiConfiguration.DisplayName,
                ErrorMessage = e.ErrorMessage,
                HttpStatusCode = e.HttpStatusCode,
                TimestampUtc = e.TimestampUtc
            })
            .ToListAsync();

        return Ok(errors);
    }

    [HttpPost("errors/{id}/dismiss")]
    public async Task<IActionResult> DismissError(long id)
    {
        var error = await _dbContext.SystemAiErrorLogs.FindAsync(id);
        if (error == null) return NotFound();

        var user = await _userManager.GetUserAsync(User);

        error.IsDismissed = true;
        error.DismissedByUserId = user?.Id;
        error.DismissedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    // --- Analytics ---

    [HttpGet("systemconfigs/{id}/analytics")]
    public async Task<IActionResult> GetConfigAnalytics(long id,
        [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] string? mode, [FromQuery] string? usernameFilter,
        [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var query = _dbContext.SystemAiUsageLogs
            .Where(l => l.SystemAiApiConfigurationId == id);

        if (startDate.HasValue) query = query.Where(l => l.TimestampUtc >= startDate.Value);
        if (endDate.HasValue) query = query.Where(l => l.TimestampUtc < endDate.Value.AddDays(1));

        if (mode == "individual")
        {
            // Group by user, with optional username filter
            var userQuery = query
                .Join(_dbContext.Users, l => l.AspNetUserId, u => u.Id,
                      (l, u) => new { Log = l, User = u });

            if (!string.IsNullOrWhiteSpace(usernameFilter))
                userQuery = userQuery.Where(x =>
                    x.User.UserName!.Contains(usernameFilter));

            int size = Math.Clamp(pageSize ?? 10, 1, 100);
            int pg = Math.Max(1, page ?? 1);

            var totalCount = await userQuery.Select(x => x.User.Id).Distinct().CountAsync();

            var rows = await userQuery
                .GroupBy(x => new { x.User.Id, x.User.UserName })
                .Select(g => new AnalyticsUserRow
                {
                    UserId = g.Key.Id,
                    UserName = g.Key.UserName ?? "",
                    ChatRequests = g.Count(x => x.Log.RoleContext == 1),
                    TitleRequests = g.Count(x => x.Log.RoleContext == 2),
                    InputTokens = g.Sum(x => (long)(x.Log.InputTokens ?? 0)),
                    OutputTokens = g.Sum(x => (long)(x.Log.OutputTokens ?? 0)),
                    CacheReadTokens = g.Sum(x => (long)(x.Log.CacheReadInputTokens ?? 0)),
                    CacheCreationTokens = g.Sum(x => (long)(x.Log.CacheCreationInputTokens ?? 0)),
                    AvgDurationMs = (int)(g.Average(x => x.Log.TotalDurationMs) ?? 0)
                })
                .OrderByDescending(r => r.ChatRequests + r.TitleRequests)
                .Skip((pg - 1) * size)
                .Take(size)
                .ToListAsync();

            return Ok(new AnalyticsResponse { Rows = rows, TotalCount = totalCount });
        }
        else
        {
            // Aggregate all users into a single row
            var row = await query
                .GroupBy(l => 1)
                .Select(g => new AnalyticsUserRow
                {
                    UserName = "All Users",
                    ChatRequests = g.Count(l => l.RoleContext == 1),
                    TitleRequests = g.Count(l => l.RoleContext == 2),
                    InputTokens = g.Sum(l => (long)(l.InputTokens ?? 0)),
                    OutputTokens = g.Sum(l => (long)(l.OutputTokens ?? 0)),
                    CacheReadTokens = g.Sum(l => (long)(l.CacheReadInputTokens ?? 0)),
                    CacheCreationTokens = g.Sum(l => (long)(l.CacheCreationInputTokens ?? 0)),
                    AvgDurationMs = (int)(g.Average(l => l.TotalDurationMs) ?? 0)
                })
                .FirstOrDefaultAsync();

            return Ok(new AnalyticsResponse
            {
                Rows = row != null ? new List<AnalyticsUserRow> { row } : new(),
                TotalCount = row != null ? 1 : 0
            });
        }
    }

    // --- Governor & Telemetry ---

    [HttpGet("governor/status")]
    public IActionResult GetGovernorStatus()
    {
        var statusList = _governor.GetStatus();
        var dto = new AiGovernorStatusDto
        {
            MaxConcurrentCalls = _governor.MaxConcurrentCalls,
            MaxRetryAfterSeconds = _governor.MaxRetryAfterSeconds,
            ActiveKeys = statusList.Select(s => new AiGovernorKeyStatusDto
            {
                CredentialKey = s.CredentialKey,
                IsRateLimited = s.IsRateLimited,
                RemainingCooldownSeconds = Math.Round(s.RemainingCooldownSeconds, 1)
            }).ToList()
        };
        return Ok(dto);
    }

    [HttpPost("governor/reset-cooldown")]
    public IActionResult ResetGovernorCooldown([FromBody] ResetCooldownRequest? request)
    {
        _governor.ClearCooldown(request?.CredentialKey);
        return Ok();
    }

    [HttpGet("ai-telemetry/summary")]
    public async Task<IActionResult> GetAiTelemetrySummary([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var query = _dbContext.SystemAiUsageLogs.AsQueryable();
        if (startDate.HasValue) query = query.Where(l => l.TimestampUtc >= startDate.Value);
        if (endDate.HasValue) query = query.Where(l => l.TimestampUtc < endDate.Value.AddDays(1));

        var modelsData = await query
            .GroupBy(l => new { l.Provider, l.ModelId })
            .Select(g => new
            {
                Provider = g.Key.Provider,
                ModelId = g.Key.ModelId,
                Requests = g.LongCount(),
                InputTokens = g.Sum(x => (long)(x.InputTokens ?? 0)),
                OutputTokens = g.Sum(x => (long)(x.OutputTokens ?? 0)),
                CacheReadTokens = g.Sum(x => (long)(x.CacheReadInputTokens ?? 0)),
                CacheCreationTokens = g.Sum(x => (long)(x.CacheCreationInputTokens ?? 0)),
                AvgDurationMs = (int)(g.Average(x => x.TotalDurationMs) ?? 0)
            })
            .ToListAsync();

        var modelDtos = modelsData.Select(m =>
        {
            long totalInput = m.InputTokens;
            double hitRatio = totalInput > 0 ? (double)m.CacheReadTokens / totalInput : 0.0;
            return new AiModelUsageBreakdownDto
            {
                Provider = m.Provider,
                ModelId = m.ModelId,
                Requests = m.Requests,
                InputTokens = m.InputTokens,
                OutputTokens = m.OutputTokens,
                CacheReadTokens = m.CacheReadTokens,
                CacheCreationTokens = m.CacheCreationTokens,
                CacheHitRatio = Math.Round(hitRatio, 4),
                AvgDurationMs = m.AvgDurationMs
            };
        }).OrderByDescending(m => m.Requests).ToList();

        long totalReqs = modelDtos.Sum(m => m.Requests);
        long totalIn = modelDtos.Sum(m => m.InputTokens);
        long totalOut = modelDtos.Sum(m => m.OutputTokens);
        long totalRead = modelDtos.Sum(m => m.CacheReadTokens);
        long totalCreate = modelDtos.Sum(m => m.CacheCreationTokens);
        double overallHitRatio = totalIn > 0 ? (double)totalRead / totalIn : 0.0;
        int overallAvgDuration = modelDtos.Count > 0 ? (int)modelDtos.Average(m => m.AvgDurationMs) : 0;

        var chatReqs = await query.CountAsync(l => l.RoleContext == 1);
        var titleReqs = await query.CountAsync(l => l.RoleContext == 2);

        var summary = new AiTelemetrySummaryDto
        {
            TotalRequests = totalReqs,
            TotalChatRequests = chatReqs,
            TotalTitleRequests = titleReqs,
            TotalInputTokens = totalIn,
            TotalOutputTokens = totalOut,
            TotalCacheReadTokens = totalRead,
            TotalCacheCreationTokens = totalCreate,
            CacheHitRatio = Math.Round(overallHitRatio, 4),
            AvgDurationMs = overallAvgDuration,
            Models = modelDtos
        };

        return Ok(summary);
    }

    // --- Helper Methods ---

    private void EncryptApiKey(SystemAiApiConfiguration config, string plainText)
    {
        var (ciphertext, nonce, tag) = _cryptoService.Encrypt(plainText, "SYSTEM_API_KEY");
        config.EncryptedApiKey = ciphertext;
        config.ApiKeyNonce = nonce;
        config.ApiKeyTag = tag;
    }

    [HttpGet("system-alerts")]
    public IActionResult GetSystemAlerts([FromServices] ConfigHealthService configHealthService)
    {
        return Ok(configHealthService.GetSystemAlerts());
    }

    // --- Database Storage & Maintenance ---

    [HttpGet("storage-metrics")]
    public async Task<IActionResult> GetStorageMetrics([FromServices] DatabaseStorageMetricsService metricsService)
    {
        var metrics = await metricsService.GetStorageMetricsAsync();
        return Ok(metrics);
    }

    [HttpPost("maintenance/run-now")]
    public async Task<IActionResult> RunMaintenanceNow([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var result = await retentionService.RunFullMaintenanceAsync(request);
        return Ok(result);
    }

    [HttpPost("maintenance/purge-trash-now")]
    public async Task<IActionResult> PurgeTrashNow([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var isDryRun = request?.DryRun ?? false;
        var trashIds = await _dbContext.ChatSession
            .Where(s => s.IsDeleted)
            .Select(s => s.Id)
            .ToListAsync();

        var result = await retentionService.PermanentlyPurgeSessionsAsync(trashIds, isDryRun);
        return Ok(result);
    }

    [HttpPost("maintenance/purge-inactive")]
    public async Task<IActionResult> PurgeInactive([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var isDryRun = request?.DryRun ?? false;
        var days = request?.InactivityDays ?? 90;
        var count = await retentionService.SoftDeleteInactiveSessionsAsync(days, isDryRun);
        return Ok(new { success = true, isDryRun, softDeletedCount = count, message = $"Soft-deleted {count} inactive sessions older than {days} days." });
    }

    [HttpPost("maintenance/prune-tool-results")]
    public async Task<IActionResult> PruneToolResults([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var isDryRun = request?.DryRun ?? false;
        var days = request?.ToolCallPruneDays ?? 30;
        var count = await retentionService.PruneAgedToolCallResultsAsync(days, isDryRun);
        return Ok(new { success = true, isDryRun, prunedCount = count, message = $"Pruned {count} tool call results older than {days} days." });
    }

    [HttpPost("maintenance/sweep-orphans")]
    public async Task<IActionResult> SweepOrphanedFolders([FromBody] MaintenanceRequestDto? request, [FromServices] ChatRetentionService retentionService)
    {
        var isDryRun = request?.DryRun ?? false;
        var count = await retentionService.SweepOrphanedDiskDirectoriesAsync(isDryRun);
        return Ok(new { success = true, isDryRun, sweptCount = count, message = $"Swept {count} orphaned disk folders." });
    }

    [HttpPost("maintenance/send-report-email")]
    public async Task<IActionResult> SendReportEmail([FromServices] DatabaseStorageMetricsService metricsService)
    {
        var metrics = await metricsService.GetStorageMetricsAsync();
        var sent = await metricsService.SendStorageWarningReportEmailAsync(metrics, metrics.StatusLevel == "Normal" ? "Informational" : metrics.StatusLevel);
        return Ok(new { success = sent, message = sent ? "Storage report email sent successfully." : "Failed to send email. Check EmailSender configuration." });
    }

    [HttpPost("test-sentry")]
    public IActionResult TestSentryError()
    {
        throw new Exception("Sentry Backend Crash Test triggered by Admin");
    }
}
