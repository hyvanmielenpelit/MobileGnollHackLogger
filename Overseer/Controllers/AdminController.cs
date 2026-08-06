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
[Authorize]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CryptoService _cryptoService;

    public AdminController(ApplicationDbContext dbContext, IConfiguration configuration, UserManager<ApplicationUser> userManager, CryptoService cryptoService)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _userManager = userManager;
        _cryptoService = cryptoService;
    }

    private bool CheckAdmin()
    {
        return _configuration.IsAdmin(User.Identity?.Name);
    }

    // --- Groups ---

    [HttpGet("groups")]
    public async Task<IActionResult> GetGroups()
    {
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();

        var configs = await _dbContext.SystemAiApiConfigurations
            .OrderBy(c => c.OrderIndex)
            .Select(c => new SystemAiApiConfigurationDto
            {
                Id = c.Id,
                DisplayName = c.DisplayName,
                Provider = c.Provider,
                ModelId = c.ModelId,
                ThinkingLevel = c.ThinkingLevel,
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
                ModelRole = c.ModelRole
            })
            .ToListAsync();

        return Ok(configs);
    }

    [HttpPost("systemconfigs")]
    public async Task<IActionResult> CreateSystemConfig([FromBody] CreateSystemAiApiConfigurationRequest request)
    {
        if (!CheckAdmin()) return Forbid();

        var orderIndex = await _dbContext.SystemAiApiConfigurations.AnyAsync() 
            ? await _dbContext.SystemAiApiConfigurations.MaxAsync(c => c.OrderIndex) + 1 
            : 0;

        var config = new SystemAiApiConfiguration
        {
            DisplayName = request.DisplayName,
            Provider = request.Provider,
            ModelId = request.ModelId,
            ThinkingLevel = request.ThinkingLevel,
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
            OrderIndex = orderIndex
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
        if (!CheckAdmin()) return Forbid();

        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(id);
        if (config == null) return NotFound();

        config.DisplayName = request.DisplayName;
        config.Provider = request.Provider;
        config.ModelId = request.ModelId;
        config.ThinkingLevel = request.ThinkingLevel;
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
        if (!CheckAdmin()) return Forbid();

        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(id);
        if (config == null) return NotFound();

        if (request?.CounterName != null)
        {
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
        if (!CheckAdmin()) return Forbid();

        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(id);
        if (config == null) return NotFound();

        _dbContext.SystemAiApiConfigurations.Remove(config);
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPut("systemconfigs/reorder")]
    public async Task<IActionResult> ReorderSystemConfigs([FromBody] ReorderRequest request)
    {
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();

        if (await _dbContext.UserSystemAiApiConfigurations.AnyAsync(a => a.AspNetUserId == userId && a.SystemAiApiConfigurationId == request.SystemAiApiConfigurationId))
        {
            return BadRequest("This configuration is already assigned to the user.");
        }

        var orderIndex = await _dbContext.UserSystemAiApiConfigurations.Where(a => a.AspNetUserId == userId).AnyAsync() 
            ? await _dbContext.UserSystemAiApiConfigurations.Where(a => a.AspNetUserId == userId).MaxAsync(c => c.OrderIndex) + 1 
            : 0;

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
            OrderIndex = orderIndex
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
        if (!CheckAdmin()) return Forbid();
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
        if (!CheckAdmin()) return Forbid();
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
        if (!CheckAdmin()) return Forbid();
        var assignment = await _dbContext.UserSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        if (request?.CounterName != null)
        {
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
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();
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
        if (!CheckAdmin()) return Forbid();
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
        if (!CheckAdmin()) return Forbid();
        var assignment = await _dbContext.GroupSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        if (request?.CounterName != null)
        {
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
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();

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
        if (!CheckAdmin()) return Forbid();

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
                    OutputTokens = g.Sum(x => (long)(x.Log.OutputTokens ?? 0))
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
                    OutputTokens = g.Sum(l => (long)(l.OutputTokens ?? 0))
                })
                .FirstOrDefaultAsync();

            return Ok(new AnalyticsResponse
            {
                Rows = row != null ? new List<AnalyticsUserRow> { row } : new(),
                TotalCount = row != null ? 1 : 0
            });
        }
    }

    // --- Helper Methods ---

    private void EncryptApiKey(SystemAiApiConfiguration config, string plainText)
    {
        var (ciphertext, nonce, tag) = _cryptoService.Encrypt(plainText, "SYSTEM_API_KEY");
        config.EncryptedApiKey = ciphertext;
        config.ApiKeyNonce = nonce;
        config.ApiKeyTag = tag;
    }
}
